using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Primitives;

namespace OpenRA.Mods.Medieval
{
	// The thunder event and refuge timer are simulation state, independent of
	// playback, camera and local particles. Muted clients flee at the same time.
	public sealed class StormClock
	{
		public int Strikes { get; private set; }
		public int Age { get; private set; } = 1000;
		public int UntilStrike { get; private set; } = 150;
		public bool Active { get; private set; }
		public const int CalmTicks = 200;
		public int ShelterTicks { get; private set; }
		public bool ThunderNow => Strikes > 0 && Age == ThunderDelay;
		public int ThunderDelay => 12 + Strikes * 17 % 44;
		public float Flash => Age < 3 ? (3 - Age) / 3f : Age is >= 6 and < 10 ? (10 - Age) / 8f : 0;
		public void Advance(int rain, bool snow)
		{
			Age = Math.Min(Age + 1, 1000);
			Active = !snow && rain >= 650;
			if (snow) ShelterTicks = 0;
			else if (ThunderNow || Active && ShelterTicks > 0) ShelterTicks = CalmTicks;
			else if (!Active && ShelterTicks > 0) ShelterTicks--;
			if (!Active) { UntilStrike = 150; return; }
			if (--UntilStrike > 0) return;
			Strikes++; Age = 0;
			UntilStrike = 275 + (int)((uint)Strikes * 173u % 400);
		}
	}
	// Derived reservation cache, only accessed from synchronized citizen ticks.
	// One occupancy scan per tick avoids one population scan for every fleeing person.
	public sealed class StormHousing
	{
		readonly World world;
		int tick = -1;
		Actor[] houses = [];
		Dictionary<Actor, int> occupied = [];
		public StormHousing(World world) { this.world = world; }
		public Actor Reserve(Actor citizen, Actor preferred, Actor avoid)
		{
			if (tick != world.WorldTick)
			{
				tick = world.WorldTick;
				houses = world.ActorsHavingTrait<MedievalShelter>().Where(a => a.Info.Name == "md.house" && MedievalStockpile.Active(a)
					&& a.TraitOrDefault<MedievalBurnable>()?.Burning != true).ToArray();
				occupied = world.ActorsHavingTrait<MedievalRest>().Where(MedievalStockpile.Active)
					.Select(a => a.Trait<MedievalRest>().Shelter).Where(a => a != null).GroupBy(a => a).ToDictionary(g => g.Key, g => g.Count());
			}
			var home = houses.Where(a => a != avoid && a.Owner == citizen.Owner && MedievalStockpile.Active(a)
				&& a.TraitOrDefault<MedievalBurnable>()?.Burning != true && occupied.GetValueOrDefault(a) < a.Trait<MedievalShelter>().Info.Beds)
				.OrderBy(a => a == preferred ? 0 : 1).ThenBy(a => (a.CenterPosition - citizen.CenterPosition).HorizontalLengthSquared).ThenBy(a => a.ActorID)
				.FirstOrDefault();
			if (home != null) occupied[home] = occupied.GetValueOrDefault(home) + 1;
			return home;
		}
	}
	public sealed class StormPresentation
	{
		readonly Random random;
		readonly List<(Vector3 Start, Vector3 End, bool Branch)> bolt = [];
		WPos source;
		int observedStrike, thunderAt = -1, reactionAt = -1;
		public int ThunderEvents { get; private set; }
		public int Reactions { get; private set; }
		public bool PendingThunder => thunderAt >= 0;
		public StormPresentation(int seed) { random = new(seed); }
		public void Advance(WorldRenderer wr, StormClock clock, bool snow)
		{
			var w = wr.World;
			if (snow) { bolt.Clear(); thunderAt = reactionAt = -1; observedStrike = clock.Strikes; return; }
			if (clock.Strikes != observedStrike)
			{
				observedStrike = clock.Strikes; bolt.Clear();
				// A storm remains audible in open countryside. Its source is atmospheric,
				// never an unseen unit or a target that could disclose hidden information.
				source = wr.Viewport.CenterPosition;
				thunderAt = w.WorldTick + Math.Max(0, clock.ThunderDelay - clock.Age);
				if (clock.Age > clock.ThunderDelay) thunderAt = -1;
				var tl = wr.Viewport.TopLeft; var br = wr.Viewport.BottomRight;
				var x = tl.X + (br.X - tl.X) * (.2f + .6f * (float)random.NextDouble());
				var y = tl.Y + (br.Y - tl.Y) * .15f;
				var endY = tl.Y + (br.Y - tl.Y) * (.45f + .2f * (float)random.NextDouble());
				var p = new Vector3(x, y, 0);
				for (var i = 1; i <= 9; i++)
				{
					var end = new Vector3(x + random.Next(-22, 23), y + (endY - y) * i / 9, 0);
					bolt.Add((p, end, false));
					if (i is 3 or 6) bolt.Add((end, end + new Vector3(random.Next(-35, 36), 22, 0), true));
					p = end;
				}
			}
			var audio = w.WorldActor.TraitOrDefault<MedievalAudio>();
			if (thunderAt >= 0 && w.WorldTick >= thunderAt)
			{
				thunderAt = -1; ThunderEvents++;
				audio?.Play("thunder", source, gain: .72f + (56 - clock.ThunderDelay) / 100f);
				reactionAt = w.WorldTick + 8 + random.Next(12);
			}
			if (reactionAt < 0 || w.WorldTick < reactionAt) return;
			reactionAt = -1;
			// At most one brief reaction, and only from an awake, visible citizen.
			foreach (var a in w.ScreenMap.RenderableActorsInBox(wr.Viewport.TopLeft, wr.Viewport.BottomRight))
			{
				if (a.IsDead || a.Disposed || a.TraitOrDefault<MedievalWorker>() == null || a.TraitOrDefault<MedievalMorale>() != null
					|| a.TraitOrDefault<MedievalRest>()?.Sleeping == true || w.FogObscures(a)
					|| w.RenderPlayer != null && !a.CanBeViewedByPlayer(w.RenderPlayer)) continue;
				if (audio?.Play("storm-surprise", a.CenterPosition) == true) Reactions++;
				break;
			}
		}
		public void Render(WorldRenderer wr, StormClock clock, bool snow)
		{
			if (snow || clock.Flash <= 0) return;
			var cr = Game.Renderer.WorldRgbaColorRenderer;
			var tl = wr.Viewport.TopLeft; var br = wr.Viewport.BottomRight;
			// Soft, short illumination, well below a full white screen. UI stays unchanged.
			cr.FillRect(new Vector3(tl.X, tl.Y, 0), new Vector3(br.X, br.Y, 0), Color.FromArgb((int)(65 * clock.Flash), 210, 228, 245));
			foreach (var segment in bolt)
			{
				cr.DrawLine(segment.Start, segment.End, segment.Branch ? 3 : 7, Color.FromArgb((int)(42 * clock.Flash), 137, 176, 236));
				cr.DrawLine(segment.Start, segment.End, segment.Branch ? .9f : 1.6f, Color.FromArgb((int)(235 * clock.Flash), 236, 245, 255));
			}
		}
	}
}
