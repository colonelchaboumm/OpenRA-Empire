using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public readonly record struct LivestockPose(Vector2 Position, int Facing, int Frame, bool Moving);
	// Pure visual trajectories: bounded by the fence, no actors, pathfinding or shared RNG.
	public static class LivestockMotion
	{
		public static uint Hash(uint n) { n = (n ^ (n >> 16)) * 0x45d9f3b; n = (n ^ (n >> 16)) * 0x45d9f3b; return n ^ (n >> 16); }
		public static LivestockPose Sample(CVec size, uint seed, int animal, int tick)
		{
			var id = Hash(seed + (uint)animal * 173 + 1);
			var duration = 480 + (int)(id % 220); var clock = tick + (int)(id % 900);
			var leg = clock / duration; var progress = clock % duration;
			Vector2 Point(int n)
			{
				var h = Hash(id + (uint)n * 31337);
				return new Vector2(.68f + (size.X - 1.36f) * (h & 65535) / 65535f,
					.68f + (size.Y - 1.36f) * (h >> 16) / 65535f);
			}
			var from = Point(leg); var to = Point(leg + 1);
			var moving = progress < duration * 3 / 4;
			var t = Math.Clamp(progress * 4f / (duration * 3), 0, 1); t = t * t * (3 - 2 * t);
			var delta = to - from;
			var facing = ((int)MathF.Round(MathF.Atan2(delta.X + delta.Y, delta.X - delta.Y) * 4 / MathF.PI) + 8) % 8;
			return new(Vector2.Lerp(from, to, t), facing, (moving ? 64 : 0) + facing * 8 + (tick / (moving ? 4 : 12) + animal) % 8, moving);
		}
	}
	public class MedievalLivestockInfo : TraitInfo, Requires<MedievalAgriculturalAreaInfo>
	{
		public readonly string Image = "md.livestock-cow";
		public readonly int BirthTicks = 1500;
		public override object Create(ActorInitializer init) => new MedievalLivestock(init.Self, this, init.Self.Trait<MedievalAgriculturalArea>());
	}
	public sealed class LivestockBreedingClock
	{
		public int Progress { get; private set; }
		public bool Advance(int adults, int count, int incoming, int capacity, int duration, bool active)
		{
			if (!active || adults < 2 || count + incoming >= capacity) { Progress = 0; return false; }
			if (++Progress < Math.Max(1, duration)) return false;
			Progress = 0; return true;
		}
	}
	public sealed class MedievalLivestock : IRender, ITick, INotifyRemovedFromWorld, INotifyOwnerChanged, ISync
	{
		readonly Actor self;
		readonly MedievalLivestockInfo info;
		readonly MedievalAgriculturalArea area;
		readonly HashSet<Actor> animals = [];
		readonly HashSet<Actor> arrivals = [];
		readonly LivestockBreedingClock breeding = new();
		[VerifySync] bool birthPending;
		[VerifySync] public int BreedingProgress => breeding.Progress;
		[VerifySync] public int MembersHash => animals.OrderBy(a => a.ActorID).Aggregate(17, (h, a) => unchecked(h * 31 + (int)a.ActorID));
		[VerifySync] public int IncomingHash => arrivals.OrderBy(a => a.ActorID).Aggregate(17, (h, a) => unchecked(h * 31 + (int)a.ActorID));
		public IEnumerable<Actor> Animals => animals.OrderBy(a => a.ActorID);
		public int Count => animals.Count;
		public int Incoming => arrivals.Count + (birthPending ? 1 : 0);
		public int Capacity => area.CattleCapacity;
		public bool Sheep => area.Info.Profession == "shepherd";
		public string AnimalType => Sheep ? "md.sheep" : "md.cow";
		public int Adults => animals.Count(a => MedievalStockpile.Active(a) && a.Trait<MedievalAnimal>().Adult && !MedievalHerding.Claimed(a, self.Owner));
		public CPos Gate => self.Location + new CVec(area.Size.X / 2, area.Size.Y - 1);
		public CPos Outside => Gate + new CVec(0, 1);
		public bool HasRoom => Count + Incoming < Capacity;
		public bool Compatible(Actor animal) => animal?.Info.Name == AnimalType;
		public bool CanReceive(Actor animal, Player owner) => self.Owner == owner && MedievalStockpile.Active(self) && Compatible(animal)
			&& (arrivals.Contains(animal) || HasRoom) && animal.Trait<MedievalAnimal>().Pen != self;
		public bool Reserve(Actor animal, Player owner)
		{
			if (!CanReceive(animal, owner)) return false;
			arrivals.Add(animal); return true;
		}
		public void Unreserve(Actor animal) { arrivals.Remove(animal); }
		public void Remove(Actor animal) { animals.Remove(animal); }
		public bool Enter(Actor animal)
		{
			if (!CanReceive(animal, animal.Owner)) return false;
			arrivals.Remove(animal); animals.Add(animal); animal.Trait<MedievalAnimal>().SetPen(animal, self); return true;
		}
		public MedievalLivestock(Actor self, MedievalLivestockInfo info, MedievalAgriculturalArea area) { this.self = self; this.info = info; this.area = area; }
		public string Status => $"{Count}/{Capacity} {(Sheep ? "moutons" : "bovins")} | Arrivees : {Incoming} | "
			+ (Adults < 2 ? "Deux adultes requis pour reproduire" : !HasRoom ? "Enclos plein" : $"Naissance : {(info.BirthTicks - BreedingProgress + 24) / 25}s");
		public bool CanProduce => Sheep ? animals.Any(a => MedievalStockpile.Active(a) && a.Trait<MedievalAnimal>().CanShear && !MedievalHerding.Claimed(a, self.Owner)) : Adults > 2;
		public int Produce()
		{
			if (!MedievalStockpile.Active(self) || !CanProduce) return 0;
			var animal = Animals.First(a => MedievalStockpile.Active(a) && (Sheep ? a.Trait<MedievalAnimal>().CanShear : a.Trait<MedievalAnimal>().Adult) && !MedievalHerding.Claimed(a, self.Owner));
			if (Sheep) { animal.Trait<MedievalAnimal>().Shear(); return 8; }
			// One carcass is one real surplus adult; always preserve the breeding pair.
			animal.Trait<MedievalAnimal>().SetPen(animal, null);
			animal.World.AddFrameEndTask(_ => animal.Dispose()); return 30;
		}
		void ITick.Tick(Actor actor)
		{
			if (birthPending) return;
			var adults = Adults;
			// Empty/full pens never scan for a farm; the index also excludes forests and rocks.
			if (!breeding.Advance(adults, Count, Incoming, Capacity, info.BirthTicks,
				MedievalStockpile.Active(self) && adults >= 2 && HasRoom && area.Farm(self) != null)) return;
			birthPending = true;
			self.World.AddFrameEndTask(w =>
			{
				birthPending = false;
				if (!MedievalStockpile.Active(self) || Adults < 2 || !HasRoom || area.Farm(self) == null) return;
				var baby = w.CreateActor(AnimalType, [new OwnerInit(self.Owner), new LocationInit(Gate), new MedievalYoungAnimalInit()]);
				Enter(baby);
			});
		}
		void ReleaseAnimals()
		{
			foreach (var a in animals.ToArray()) a.Trait<MedievalAnimal>().SetPen(a, null);
			arrivals.Clear();
		}
		void INotifyRemovedFromWorld.RemovedFromWorld(Actor actor) { ReleaseAnimals(); }
		void INotifyOwnerChanged.OnOwnerChanged(Actor actor, Player oldOwner, Player newOwner) { ReleaseAnimals(); }
		public LivestockPose Pose(Actor self, int animal) => LivestockMotion.Sample(area.Size, self.ActorID, animal, self.World.WorldTick);
		public WPos Position(Actor self, LivestockPose pose)
		{
			var x = pose.Position.X - .5f; var y = pose.Position.Y - .5f;
			return self.World.Map.CenterOfCell(self.Location) + new WVec((int)((x - y) * 724), (int)((x + y) * 724), 0);
		}
		IEnumerable<IRenderable> IRender.Render(Actor self, WorldRenderer wr)
		{
			if (!MedievalStockpile.Active(self) || self.World.FogObscures(self)) yield break;
			// Draw the near fence in front of the flock, with the same footprint anchor.
			var fence = self.World.Map.Sequences.GetSequence(self.Info.Name, "fence").GetSprite(0);
			yield return new SpriteRenderable(fence, self.CenterPosition, WVec.Zero, (area.Size.X + area.Size.Y) * 362, null, 1, 1, Vector3.One, TintModifiers.None, false);
		}
		IEnumerable<Rectangle> IRender.ScreenBounds(Actor self, WorldRenderer wr)
		{
			var p = wr.ScreenPxPosition(self.CenterPosition); var r = (area.Size.X + area.Size.Y) * 12 + 12;
			yield return new Rectangle(p.X - r, p.Y - r / 2 - 40, r * 2, r + 70);
		}
	}
}
