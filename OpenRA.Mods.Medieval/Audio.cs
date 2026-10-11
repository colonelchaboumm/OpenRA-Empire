using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public readonly record struct VillageSound(int Variants, float Volume, int Interval, bool Important = false);
	[TraitLocation(SystemActors.World)]
	public class MedievalAudioInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalAudio(init.World);
	}
	// Entirely cosmetic. Never use playback success, camera or this RNG to change simulation state.
	public sealed class MedievalAudio : IWorldLoaded, ITick, ITickRender, INotifyActorDisposing
	{
		public static readonly IReadOnlyDictionary<string, VillageSound> Bank = new Dictionary<string, VillageSound>
		{
			["step"] = new(3, .12f, 3), ["cart"] = new(2, .20f, 12),
			["snow-step"] = new(4, .21f, 4), ["snow-cart"] = new(2, .23f, 14),
			["thunder"] = new(3, .70f, 180, true), ["storm-surprise"] = new(3, .34f, 225, true),
			["axe"] = new(3, .43f, 5), ["pick"] = new(3, .32f, 5), ["hammer"] = new(2, .30f, 5),
			["anvil"] = new(2, .30f, 6), ["hoe"] = new(2, .22f, 7), ["harvest"] = new(2, .20f, 7),
			["load"] = new(2, .19f, 6), ["saw"] = new(2, .22f, 20), ["pulley"] = new(2, .22f, 20),
			["church-bell"] = new(2, .44f, 35, true), ["gold-sort"] = new(2, .20f, 30),
			["wind"] = new(1, .13f, 220), ["bird"] = new(3, .15f, 110), ["cattle"] = new(2, .23f, 180),
			["sheep"] = new(3, .21f, 130), ["calf"] = new(2, .18f, 160), ["lamb"] = new(2, .16f, 140),
			["hoof"] = new(3, .09f, 8), ["night"] = new(2, .19f, 215), ["owl"] = new(2, .19f, 270),
			["forge"] = new(2, .24f, 45), ["combat-cry"] = new(3, .48f, 90, true),
			["rally"] = new(3, .50f, 250, true), ["patrol"] = new(4, .28f, 375, true),
			["drum-group"] = new(2, .32f, 20, true),
			["market"] = new(1, .16f, 100), ["fire"] = new(1, .12f, 100),
			["sword"] = new(3, .35f, 3), ["bow"] = new(2, .35f, 3), ["hit"] = new(2, .24f, 4),
			["ram-pull"] = new(1, .37f, 8), ["ram-hit"] = new(2, .75f, 3, true),
			["trebuchet-release"] = new(1, .62f, 4, true), ["stone-air"] = new(1, .35f, 8),
			["stone-impact"] = new(2, .85f, 3, true), ["tree-fall"] = new(1, .42f, 15),
			["collapse"] = new(1, .55f, 8, true), ["gate"] = new(1, .30f, 15),
			["complete"] = new(1, .30f, 10, true), ["ready"] = new(1, .26f, 10, true),
			["select"] = new(1, .20f, 4), ["order"] = new(1, .18f, 4), ["drum"] = new(1, .32f, 20, true)
		};
		readonly World world;
		readonly Random random;
		readonly List<(ISound Sound, bool Important, string Cue, WPos Position, bool Interface)> voices = [];
		readonly Dictionary<string, int> next = [];
		readonly Dictionary<string, int> played = [];
		WorldRenderer renderer;
		MedievalDayNight dayNight;
		MedievalWeather weather;
		int tick, ambientAt, birdsAt, nightAt, drumsAt, speechAt;
		bool wasNight;
		public int LargestDrumGroup { get; private set; }
		public float LastDrumGain { get; private set; }
		public static float DrumGain(int count) => count <= 0 ? 0 : Math.Min(2f, 1f + .25f * (float)Math.Sqrt(count - 1));
		static bool Speech(string cue) => cue is "combat-cry" or "rally" or "patrol" or "storm-surprise";
		public int PeakVoices { get; private set; }
		public int Filtered { get; private set; }
		public int ActiveVoices => voices.Count(v => !v.Sound.Complete);
		public IReadOnlyDictionary<string, int> Played => played;
		public MedievalAudio(World world) { this.world = world; random = new Random(world.LocalRandom.Next()); }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr; dayNight = w.WorldActor.TraitOrDefault<MedievalDayNight>(); weather = w.WorldActor.TraitOrDefault<MedievalWeather>();
		}
		public bool Audible(WPos position)
		{
			if (renderer == null || world.Type != WorldType.Regular || world.Paused || world.FogObscures(position)) return false;
			var point = renderer.ScreenPxPosition(position);
			var top = renderer.Viewport.TopLeft; var bottom = renderer.Viewport.BottomRight;
			return point.X >= top.X - 64 && point.X <= bottom.X + 64 && point.Y >= top.Y - 96 && point.Y <= bottom.Y + 96;
		}
		public bool Play(string cue, WPos position, bool interfaceSound = false, float gain = 1)
		{
			if (!Bank.TryGetValue(cue, out var entry) || world.Type != WorldType.Regular || world.Paused) return false;
			if (gain <= 0 || !float.IsFinite(gain) || Speech(cue) && tick < speechAt) return false;
			if (next.TryGetValue(cue, out var until) && tick < until) return false;
			// A throttled cue cannot play even if audible. Avoid projecting and
			// checking the fog for every footstep/sword in the same dense group.
			if (!interfaceSound && !Audible(position)) { Filtered++; return false; }
			voices.RemoveAll(v => v.Sound.Complete);
			// One intelligible voice at a time, even when hundreds of soldiers fight.
			if (Speech(cue) && voices.Any(v => Speech(v.Cue))) return false;
			if (voices.Count >= 18)
			{
				var replace = voices.FindIndex(v => !v.Important);
				if (!entry.Important || replace < 0) return false;
				Game.Sound.StopSound(voices[replace].Sound); voices.RemoveAt(replace);
			}
			var file = "medieval|audio/" + cue + (cue == "drum" ? "" : "-" + (random.Next(entry.Variants) + 1)) + ".wav";
			var volume = Math.Clamp(entry.Volume * gain, 0, .85f);
			var sound = interfaceSound ? Game.Sound.Play(SoundType.UI, file, volume)
				: Game.Sound.Play(SoundType.World, file, position, volume);
			if (sound == null) return false;
			voices.Add((sound, entry.Important, cue, position, interfaceSound)); PeakVoices = Math.Max(PeakVoices, voices.Count);
			if (Speech(cue)) speechAt = tick + (cue == "patrol" ? 140 : 75);
			next[cue] = tick + entry.Interval; played[cue] = played.GetValueOrDefault(cue) + 1;
			return true;
		}
		public static void At(World world, string cue, WPos position) => world.WorldActor.TraitOrDefault<MedievalAudio>()?.Play(cue, position);
		public static void Notify(Actor actor, string cue)
		{
			if (actor.Owner == actor.World.LocalPlayer) actor.World.WorldActor.TraitOrDefault<MedievalAudio>()?.Play(cue, actor.CenterPosition, true);
		}
		void ITick.Tick(Actor self)
		{
			if (renderer == null || world.Type != WorldType.Regular) return;
			tick++; voices.RemoveAll(v => v.Sound.Complete);
			var center = renderer.Viewport.CenterPosition;
			var night = dayNight?.Clock.NightWatch == true;
			if (night != wasNight)
			{
				StopWhere(v => night ? v.Cue == "bird" : v.Cue is "night" or "owl");
				nightAt = tick; birdsAt = tick + 35; wasNight = night;
			}
			if (tick % 10 == 0) StopWhere(v => !v.Interface && !Audible(v.Position));
			if (tick >= ambientAt) { Play("wind", center); ambientAt = tick + 245; }
			if (night && tick >= nightAt)
			{
				Play("night", center, gain: 1 - (weather?.RainPermille ?? 0) / 1400f);
				nightAt = tick + 220;
			}
			if (tick >= birdsAt)
			{
				var trees = world.ActorsWithTrait<MedievalSource>().Where(p => p.Trait.StandingTree && Audible(p.Actor.CenterPosition)).Take(24).ToArray();
				if (trees.Length > 0 && (weather?.RainPermille ?? 0) < 500) Play(night ? "owl" : "bird", trees[random.Next(trees.Length)].Actor.CenterPosition);
				birdsAt = tick + (night ? random.Next(320, 620) : random.Next(130, 290));
			}
			if (tick >= drumsAt) MixDrums(center);
		}
		void MixDrums(WPos center)
		{
			// One bounded mix per beat, never an all-musicians scan for every drummer.
			var musicians = world.ActorsWithTrait<MedievalDrummer>()
				.Where(t => t.Trait.Playing(t.Actor) && Audible(t.Actor.CenterPosition)
					&& (world.RenderPlayer == null || t.Actor.CanBeViewedByPlayer(world.RenderPlayer))).Take(64).ToArray();
			drumsAt = tick + 24;
			if (musicians.Length == 0) return;
			var lead = musicians.MinBy(t => (t.Actor.CenterPosition - center).HorizontalLengthSquared);
			var count = 0; var allHungry = true;
			foreach (var musician in musicians)
				if (musician.Actor.Owner == lead.Actor.Owner && lead.Trait.Covers(lead.Actor, musician.Actor.CenterPosition))
				{
					count++; allHungry &= musician.Actor.Trait<MedievalNeedsFood>().Need.Hungry;
				}
			var gain = DrumGain(count) * (allHungry ? .75f : 1);
			if (Play(count > 1 ? "drum-group" : "drum", lead.Actor.CenterPosition, gain: gain))
			{
				LargestDrumGroup = Math.Max(LargestDrumGroup, count); LastDrumGain = gain;
			}
			if (allHungry) drumsAt = tick + 48;
		}
		void StopWhere(Predicate<(ISound Sound, bool Important, string Cue, WPos Position, bool Interface)> predicate)
		{
			for (var i = voices.Count - 1; i >= 0; i--)
				if (predicate(voices[i])) { Game.Sound.StopSound(voices[i].Sound); voices.RemoveAt(i); }
		}
		void StopVoices() { foreach (var v in voices) Game.Sound.StopSound(v.Sound); voices.Clear(); }
		void ITickRender.TickRender(WorldRenderer wr, Actor self)
		{
			if (!world.Paused) return;
			StopVoices(); ambientAt = nightAt = drumsAt = tick; birdsAt = tick + 50;
		}
		void INotifyActorDisposing.Disposing(Actor self) { StopVoices(); }
	}

	public class MedievalActorAudioInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalActorAudio(init.Self);
	}
	public sealed class MedievalActorAudio : ITick, INotifyCreated, INotifyAttack, INotifyDamage, INotifyKilled, INotifySelected, IResolveOrder
	{
		readonly Actor self;
		MedievalAudio audio;
		MedievalWorker worker;
		WithMedievalInfantryBody body;
		MedievalSource source;
		MedievalBuilding building;
		MedievalBuildingAnimation machinery;
		MedievalSiegeEngine siege;
		MedievalAnimal animal;
		MedievalRest rest;
		MedievalPatrol patrol;
		MedievalStandardBearer standard;
		MedievalSeasons seasons;
		MedievalTrails trails;
		Gate gate;
		WPos previous;
		int travel, frame = -1, cargo, fall = -1, gatePosition, clock, cadence, speechAt, combatUntil;
		bool built, movable, saboteur, military;
		public MedievalActorAudio(Actor self) { this.self = self; }
		void INotifyCreated.Created(Actor actor)
		{
			previous = self.CenterPosition;
			audio = self.World.WorldActor.TraitOrDefault<MedievalAudio>();
			seasons = self.World.WorldActor.TraitOrDefault<MedievalSeasons>();
			trails = self.World.WorldActor.TraitOrDefault<MedievalTrails>();
			worker = self.TraitOrDefault<MedievalWorker>(); body = self.TraitOrDefault<WithMedievalInfantryBody>();
			source = self.TraitOrDefault<MedievalSource>(); building = self.TraitOrDefault<MedievalBuilding>(); built = building?.Complete ?? false;
			machinery = self.TraitOrDefault<MedievalBuildingAnimation>(); siege = self.TraitOrDefault<MedievalSiegeEngine>();
			gate = self.TraitOrDefault<Gate>(); gatePosition = gate?.Position ?? 0;
			cadence = (int)(self.ActorID % 80);
			movable = self.TraitOrDefault<Mobile>() != null;
			saboteur = self.TraitOrDefault<MedievalSaboteur>() != null;
			animal = self.TraitOrDefault<MedievalAnimal>(); rest = self.TraitOrDefault<MedievalRest>();
			patrol = self.TraitOrDefault<MedievalPatrol>(); standard = self.TraitOrDefault<MedievalStandardBearer>();
			military = self.TraitOrDefault<MedievalMorale>() != null;
			speechAt = 90 + (int)(self.ActorID * 73 % 220);
		}
		bool Play(string cue)
		{
			if (rest?.Sleeping == true || saboteur && self.World.RenderPlayer != null && !self.CanBeViewedByPlayer(self.World.RenderPlayer)) return false;
			return audio?.Play(cue, self.CenterPosition) == true;
		}
		void ITick.Tick(Actor actor)
		{
			if (source != null && source.Info.Natural != NaturalResourceKind.None && source.FallAge == fall) return;
			clock++;
			var distance = movable ? (self.CenterPosition - previous).HorizontalLength : 0;
			if (movable) previous = self.CenterPosition;
			if (distance > 0 && distance < 1024)
			{
				travel += distance;
				var wheeled = siege != null || self.Info.Name is "md.supplycart" or "md.tradecart" or "md.stonecart" or "md.caravan";
				if (travel >= (wheeled ? 900 : 480))
				{
					travel %= wheeled ? 900 : 480;
					var snowy = seasons?.Clock.Snow > 150 && self.World.Map.Contains(self.Location)
						&& self.World.Map.GetTerrainInfo(self.Location).Type is "Clear" or "Rough" or "Road"
						&& trails?.ProtectsCell(self.Location) != true;
					Play(snowy ? wheeled ? "snow-cart" : "snow-step" : wheeled ? "cart" : animal != null ? "hoof" : "step");
				}
			}
			if (worker != null)
			{
				if (worker.Cargo != cargo && worker.HandlingTicks > 0) Play("load"); cargo = worker.Cargo;
				if (worker.IsWorking && worker.Info.Profession != "hunter" && body != null && body.Frame == 5 && frame != 5)
					Play(worker.IsDemolishing ? "hammer" : worker.Info.Profession switch
					{
						"lumberjack" => "axe", "stonecutter" or "goldminer" => "pick", "farmer" => worker.IsHarvesting ? "harvest" : "hoe",
						"breeder" or "shepherd" => "harvest", "hunter" => "bow", "blacksmith" => "anvil", "builder" => "hammer", _ => "load"
					});
				if (worker.IsWorking && worker.Info.Profession == "blacksmith" && (clock + cadence) % 95 == 0) Play("forge");
				frame = body?.Frame ?? -1;
			}
			if (source != null)
			{
				if (fall < 0 && source.FallAge >= 0) Play("tree-fall"); fall = source.FallAge;
			}
			if (animal != null && (clock + self.ActorID * 73) % (230 + self.ActorID % 160) == 0)
				Play(self.Info.Name == "md.sheep" ? animal.Adult ? "sheep" : "lamb" : animal.Adult ? "cattle" : "calf");
			if (clock >= speechAt && (standard != null || patrol != null) && rest?.Resting != true && !saboteur)
			{
				speechAt = clock + 350 + (int)(self.ActorID % 200);
				string speech = null;
				if (standard?.Active(self) == true && audio?.Audible(self.CenterPosition) == true
					&& self.World.FindActorsInCircle(self.CenterPosition, standard.Info.Radius).Any(a => a != self && !a.IsDead
						&& self.Owner.RelationshipWith(a.Owner) == PlayerRelationship.Ally && a.TraitOrDefault<MedievalMorale>() != null
						&& a.TraitOrDefault<MedievalRest>()?.Resting != true)) speech = "rally";
				else if (patrol?.Active == true && clock > combatUntil && self.CurrentActivity is MilitiaRound) speech = "patrol";
				// A nearby battle can occupy the shared voice slot. Retry shortly instead
				// of losing the entire next patrol/rally interval. Never queue stale lines.
				if (speech != null && !Play(speech) && audio?.Audible(self.CenterPosition) == true) speechAt = clock + 25 + (int)(self.ActorID % 20);
			}
			if (building?.Complete == true)
			{
				if (!built) MedievalAudio.Notify(self, "complete"); built = true;
				if ((clock + cadence) % 150 == 0 && self.Info.Name is "md.market" or "md.foundingmarket" or "md.marketstall") Play("market");
				if ((clock + cadence) % 160 == 0 && self.Info.Name == "md.forge") Play("fire");
			}
			if (machinery?.Operating == true && (clock + cadence) % 32 == 0)
			{
				if (self.Info.Name == "md.sawmill") Play("saw");
				else if (self.Info.Name == "md.quarry") Play("pulley");
			}
			if (siege?.Busy == true && (clock + cadence) % 40 == 0) Play("pulley");
			if (gate != null)
			{
				if (gate.Position != gatePosition && (gatePosition == 0 || gatePosition == gate.OpenPosition)) Play("gate");
				gatePosition = gate.Position;
			}
		}
		void INotifyAttack.PreparingAttack(Actor actor, in Target target, Armament armament, Barrel barrel) { }
		void INotifyAttack.Attacking(Actor actor, in Target target, Armament armament, Barrel barrel)
		{
			if (siege != null) { if (!siege.Info.Trebuchet) Play("ram-pull"); return; }
			Play(self.Info.Name is "md.archer" or "md.elitearcher" ? "bow" : "sword");
			combatUntil = clock + 100;
			if (military && !saboteur) Play("combat-cry");
		}
		void INotifyDamage.Damaged(Actor actor, AttackInfo e)
		{
			if (e.Damage.Value <= 0 || e.Attacker == self) return;
			combatUntil = clock + 100;
			if (building == null && siege == null) Play("hit");
		}
		void INotifyKilled.Killed(Actor actor, AttackInfo e) { Play(building != null || siege != null ? "collapse" : "hit"); }
		void INotifySelected.Selected(Actor actor) { if (self.Owner == self.World.LocalPlayer) audio?.Play("select", self.CenterPosition, true); }
		void IResolveOrder.ResolveOrder(Actor actor, Order order)
		{
			if (self.Owner == self.World.LocalPlayer && order.OrderString is "Move" or "Attack" or "ForceAttack" or "MedievalSiegeDeploy" or "MedievalSiegePack")
				audio?.Play("order", self.CenterPosition, true);
		}
	}
}
