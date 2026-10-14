using System;
using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public class MedievalWindmillInfo : TraitInfo, Requires<WithSpriteBodyInfo>
	{
		public override object Create(ActorInitializer init) => new MedievalWindmill();
	}
	public sealed class MedievalWindmill : ITick
	{
		float rotation;
		bool playing;
		void ITick.Tick(Actor self)
		{
			if (!MedievalStockpile.Active(self)) return;
			var body = self.TraitsImplementing<WithSpriteBody>().First(b => b.Info.Name == "body");
			var wind = self.World.WorldActor.TraitOrDefault<MedievalAmbience>()?.Scene.Wind.Length() ?? .5f;
			rotation = (rotation + .06f + wind * .1f) % 16;
			if (playing) return;
			body.DefaultAnimation.PlayFetchIndex("idle", () => (int)rotation);
			playing = true;
		}
	}

	// Distance-driven gait: stopping and hunger cannot cause skating feet.
	public sealed class MedievalGait
	{
		public const int CycleDistance = 960;
		int distance;
		public int Frame => distance * 8 / CycleDistance;
		public void Advance(int travel)
		{
			// Relocation/teleport is not a step. Always keep the accumulator bounded.
			if (travel > 0 && travel <= 1024) distance = (distance + travel) % CycleDistance;
		}
	}

	public class WithMedievalInfantryBodyInfo : WithInfantryBodyInfo
	{
		public override object Create(ActorInitializer init) => new WithMedievalInfantryBody(init, this);
	}
	public sealed class WithMedievalInfantryBody : WithInfantryBody
	{
		readonly MedievalGait gait = new();
		readonly IMove movement;
		readonly bool standardBearer;
		WPos previous;
		int clock;
		int frame;
		string controlledSequence;
		public string Sequence => DefaultAnimation.CurrentSequence.Name;
		public int Frame => DefaultAnimation.CurrentFrame;
		public string ImageName => DefaultAnimation.Name;
		internal OpenRA.Graphics.Animation DeathAnimation => DefaultAnimation;
		public WithMedievalInfantryBody(ActorInitializer init, WithMedievalInfantryBodyInfo info) : base(init, info)
		{
			previous = init.Self.CenterPosition;
			movement = init.Self.Trait<IMove>();
			standardBearer = init.Self.Info.HasTraitInfo<MedievalStandardBearerInfo>();
		}
		protected override void Tick(Actor self)
		{
			if (standardBearer) DefaultAnimation.ChangeImage("md.heraldic-standardbearer-" + MedievalHeraldry.For(self.Owner).Emblem, "stand");
			base.Tick(self);
			var travel = (self.CenterPosition - previous).HorizontalLength;
			previous = self.CenterPosition;
			var moving = movement.CurrentMovementTypes.HasMovementType(MovementType.Horizontal);
			if (moving) gait.Advance(travel);
			if (state == AnimationState.Attacking) { controlledSequence = null; return; }
			var worker = self.TraitOrDefault<MedievalWorker>();
			var crew = self.TraitOrDefault<MedievalSiegeCrew>();
			var commander = self.TraitOrDefault<MedievalCommander>();
			var herding = self.TraitOrDefault<MedievalHerding>();
			var saboteur = self.TraitOrDefault<MedievalSaboteur>();
			var firefighter = self.TraitOrDefault<MedievalFirefighter>();
			var carrying = worker?.Cargo > 0 || crew?.Cargo > 0 || commander != null && self.Trait<MedievalCampStore>().Stock.Amount(1) > 0;
			var drumming = self.TraitOrDefault<MedievalDrummer>()?.Playing(self) == true;
			var pushing = crew?.Pushing == true;
			var ramStroke = crew?.RamStroke(self) ?? -1;
			var sequence = moving ? carrying ? "carry-run" : pushing ? "push" : "run"
				: crew?.Handling == true ? "load" : crew?.Cargo > 0 ? "carry-stand" : ramStroke >= 0 ? "push" : pushing ? "push-stand"
				: crew?.Working == true ? "push"
				: drumming ? "work"
				: commander?.Building == true ? "work"
				: self.TraitOrDefault<MedievalHerding>()?.Working == true ? "work"
				: worker?.IsWorking == true ? worker.IsHarvesting ? "harvest" : "work"
				: worker?.HandlingTicks > 0 ? "load" : carrying ? "carry-stand" : "stand";
			var herdSequence = herding?.Leading == true ? moving ? "lead" : "lead-stand" : herding?.Working == true && !moving ? "catch" : null;
			var hunting = worker?.HuntingSequence;
			if (hunting != null && DefaultAnimation.HasSequence(hunting)) sequence = hunting;
			if (herdSequence != null && DefaultAnimation.HasSequence(herdSequence)) sequence = herdSequence;
			if (saboteur?.Working == true && !moving) sequence = saboteur.Slaughter ? "slaughter" : "ignite";
			if (firefighter?.Busy == true) sequence = moving ? firefighter.Water ? "water-run" : "water-empty-run"
				: firefighter.Phase == 1 ? "water-fill" : firefighter.Phase == 3 ? "water-pour" : firefighter.Water ? "water-stand" : "water-empty-stand";
			if (!moving && firefighter?.Busy != true && self.TraitOrDefault<MedievalPriest>()?.Healing(self) == true && DefaultAnimation.HasSequence("heal")) sequence = "heal";
			if (sequence == "catch") frame = herding.CaptureFrame;
			if (controlledSequence != sequence || DefaultAnimation.CurrentSequence.Name != sequence)
			{
				controlledSequence = sequence;
				clock = sequence == "stand" ? (int)(self.ActorID % 8) * 600 : 0;
				// Healing has sixteen poses; do not carry its index into an eight-pose sequence.
				frame = sequence == "catch" ? herding.CaptureFrame : 0;
				DefaultAnimation.PlayFetchIndex(sequence, () => frame);
			}
			clock = (clock + (worker?.IsWorking == true || crew?.Working == true || commander?.Building == true || drumming ? self.TraitOrDefault<MedievalNeedsFood>()?.WorkPercent ?? 100 : 100)) % 4800;
			frame = sequence == "catch" ? herding.CaptureFrame : moving ? gait.Frame : ramStroke >= 0 && sequence == "push" ? ramStroke * 8 / RamMotion.FireTicks
				: sequence is "hunt-aim" or "hunt-release" ? worker.HuntingFrame
				: sequence == "hunt-gather" ? clock / 300 % 8
				: sequence == "heal" ? clock / 300 % 16
				: sequence == "load" && worker?.HandlingStock == true ? worker.HandlingPose
				: sequence == "load" && crew?.Handling != true ? Math.Clamp(8 - (worker?.HandlingTicks ?? 8), 0, 7)
				: clock / (sequence is "work" or "harvest" or "ignite" or "slaughter" or "water-fill" or "water-pour" ? 300 : 600) % 8;
		}
	}
}
