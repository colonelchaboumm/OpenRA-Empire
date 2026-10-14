using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public sealed class MoraleState
	{
		public const int Normal = 70;
		public int Value { get; private set; } = Normal;
		public int Target { get; private set; } = Normal;
		public int Firepower => 65 + Value / 2;
		public static int Standards(IEnumerable<(PlayerRelationship Relation, bool Hungry, bool Starving)> sources) => sources
			.Where(s => s.Relation == PlayerRelationship.Ally && !s.Starving).Select(s => s.Hungry ? 7 : 15).DefaultIfEmpty().Max();
		public static int Support(int music, int standard) => Math.Min(30, music + standard);
		public void Advance(bool hungry, bool starving, int encouragement, int intimidation, int nationalPride = 0)
		{
			Target = Math.Clamp(Normal - (starving ? 50 : hungry ? 30 : 0) + encouragement - intimidation + nationalPride, 0, 100);
			Value += Math.Clamp(Target - Value, -2, 1);
		}
		// The strongest friendly and hostile rhythm apply once each, irrespective of drummer count.
		public static (int Encouragement, int Intimidation) Music(IEnumerable<(PlayerRelationship Relation, bool Hungry, bool Starving)> sources)
		{
			var ally = 0; var enemy = 0;
			foreach (var source in sources.Where(s => !s.Starving))
			{
				if (source.Relation == PlayerRelationship.Ally) ally = Math.Max(ally, source.Hungry ? 10 : 20);
				if (source.Relation == PlayerRelationship.Enemy) enemy = Math.Max(enemy, source.Hungry ? 5 : 10);
			}
			return (ally, enemy);
		}
	}

	public class MedievalMoraleInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalMorale();
	}
	public sealed class MedievalMorale : ITick, IFirepowerModifier, IRenderAnnotations, ISync
	{
		public readonly MoraleState State = new();
		[VerifySync] public int Value => State.Value;
		[VerifySync] public int Target => State.Target;
		[VerifySync] public int Encouragement { get; private set; }
		[VerifySync] public int Intimidation { get; private set; }
		[VerifySync] public int StandardBonus { get; private set; }
		[VerifySync] public int MusicBonus { get; private set; }
		[VerifySync] public int PriestBonus { get; private set; }
		[VerifySync] public int NationalBonus { get; private set; }
		[VerifySync] public int InfluencePenalty { get; private set; }
		[VerifySync] int clock;
		public string Description => $"Moral {Value}/100 | Degats {State.Firepower}%"
			+ (NationalBonus != 0 ? $" | Nation {NationalBonus:+0;-0}" : "")
			+ (InfluencePenalty > 0 ? $" | Influence adverse -{InfluencePenalty}" : "")
			+ (PriestBonus > 0 ? $" | Pretre +{PriestBonus}" : "")
			+ (MusicBonus > 0 ? $" | Tambour +{MusicBonus}" : "") + (StandardBonus > 0 ? $" | Drapeau +{StandardBonus}" : "")
			+ (MusicBonus + StandardBonus > 30 ? " | Soutien plafonne +30" : "") + (Intimidation > 0 ? $" | Ennemi -{Intimidation}" : "");
		void ITick.Tick(Actor self)
		{
			if (++clock < 25) return;
			clock = 0;
			var music = MoraleState.Music(self.World.ActorsWithTrait<MedievalDrummer>()
				.Where(t => t.Trait.Covers(t.Actor, self.CenterPosition))
				.Select(t => (self.Owner.RelationshipWith(t.Actor.Owner), t.Actor.Trait<MedievalNeedsFood>().Need.Hungry, t.Actor.Trait<MedievalNeedsFood>().Need.Starving)));
			StandardBonus = MoraleState.Standards(self.World.ActorsWithTrait<MedievalStandardBearer>()
				.Where(t => t.Trait.Covers(t.Actor, self.CenterPosition))
				.Select(t => (self.Owner.RelationshipWith(t.Actor.Owner), t.Actor.Trait<MedievalNeedsFood>().Need.Hungry, t.Actor.Trait<MedievalNeedsFood>().Need.Starving)));
			MusicBonus = music.Encouragement;
			PriestBonus = self.World.ActorsHavingTrait<MedievalPriest>().Where(a => a.Owner == self.Owner)
				.Select(a => a.Trait<MedievalPriest>().Bonus(a, self.CenterPosition)).DefaultIfEmpty(0).Max();
			Encouragement = Math.Min(40, MoraleState.Support(MusicBonus, StandardBonus) + PriestBonus); Intimidation = music.Intimidation;
			var need = self.TraitOrDefault<MedievalNeedsFood>()?.Need;
			NationalBonus = MedievalNationalPride.For(self.Owner)?.State.MoraleBonus ?? 0;
			InfluencePenalty = self.TraitOrDefault<MedievalInfluenceMood>()?.Penalty ?? 0;
			State.Advance(need?.Hungry == true, need?.Starving == true, Encouragement, Intimidation + InfluencePenalty, NationalBonus);
		}
		int IFirepowerModifier.GetFirepowerModifier() => State.Firepower;
		IEnumerable<IRenderable> IRenderAnnotations.RenderAnnotations(Actor self, WorldRenderer wr)
		{
			if (self.World.Selection.Contains(self) && !self.World.FogObscures(self) && (self.World.RenderPlayer == null || self.CanBeViewedByPlayer(self.World.RenderPlayer)))
				yield return new MoraleBarRenderable(self.CenterPosition, Value);
		}
		bool IRenderAnnotations.SpatiallyPartitionable => true;
	}

	public class MedievalDrummerInfo : TraitInfo, Requires<MedievalMoraleInfo>, Requires<MedievalNeedsFoodInfo>
	{
		public readonly WDist Radius = new(5120);
		public override object Create(ActorInitializer init) => new MedievalDrummer(this);
	}
	public sealed class MedievalDrummer : IRenderAnnotations
	{
		public readonly MedievalDrummerInfo Info;
		public MedievalDrummer(MedievalDrummerInfo info) { Info = info; }
		public bool Playing(Actor self) => self.IsInWorld && !self.IsDead && !self.Trait<MedievalNeedsFood>().Need.Starving && self.TraitOrDefault<MedievalRest>()?.Sleeping != true;
		public bool Covers(Actor self, WPos position) => Playing(self) && MedievalConstructionSupply.Within(self.CenterPosition, position, Info.Radius);
		public string Description(Actor self) => !Playing(self) ? "Tambour silencieux : famine"
			: self.Trait<MedievalNeedsFood>().Need.Hungry ? "Tambour affaibli : allies +10 / ennemis -5, rayon 5 cases"
			: "Tambour : allies +20 / ennemis -10, rayon 5 cases";
		IEnumerable<IRenderable> IRenderAnnotations.RenderAnnotations(Actor self, WorldRenderer wr)
		{
			if (!self.World.Selection.Contains(self) || self.World.FogObscures(self)) yield break;
			foreach (var line in MedievalFoodProvider.Ring(self.CenterPosition, Info.Radius, Playing(self) ? Color.Goldenrod : Color.Gray)) yield return line;
		}
		bool IRenderAnnotations.SpatiallyPartitionable => false;
	}

	public class MedievalStandardBearerInfo : TraitInfo, Requires<MedievalMoraleInfo>, Requires<MedievalNeedsFoodInfo>
	{
		public readonly WDist Radius = new(5120);
		public override object Create(ActorInitializer init) => new MedievalStandardBearer(this);
	}
	public sealed class MedievalStandardBearer : IRenderAnnotations
	{
		public readonly MedievalStandardBearerInfo Info;
		public MedievalStandardBearer(MedievalStandardBearerInfo info) { Info = info; }
		public bool Active(Actor self) => self.IsInWorld && !self.IsDead && !self.WillDispose && !self.Trait<MedievalNeedsFood>().Need.Starving && self.TraitOrDefault<MedievalRest>()?.Sleeping != true;
		public bool Covers(Actor self, WPos position) => Active(self) && MedievalConstructionSupply.Within(self.CenterPosition, position, Info.Radius);
		public string Description(Actor self) => !Active(self) ? "Etendard affaibli par la famine : aucun bonus"
			: $"Etendard : allies +{(self.Trait<MedievalNeedsFood>().Need.Hungry ? 7 : 15)}, rayon 5 cases. Soutien total maximum +30.";
		IEnumerable<IRenderable> IRenderAnnotations.RenderAnnotations(Actor self, WorldRenderer wr)
		{
			if (!self.World.Selection.Contains(self) || self.World.FogObscures(self)) yield break;
			foreach (var line in MedievalFoodProvider.Ring(self.CenterPosition, Info.Radius, Active(self) ? MedievalHeraldry.For(self.Owner).Color : Color.Gray)) yield return line;
		}
		bool IRenderAnnotations.SpatiallyPartitionable => false;
	}
	sealed class MoraleBarRenderable : IRenderable, IFinalizedRenderable
	{
		readonly int value;
		public WPos Pos { get; }
		public int ZOffset => 0;
		public bool IsDecoration => true;
		public MoraleBarRenderable(WPos pos, int value) { Pos = pos; this.value = value; }
		public IRenderable WithZOffset(int z) => this;
		public IRenderable OffsetBy(in WVec offset) => new MoraleBarRenderable(Pos + offset, value);
		public IRenderable AsDecoration() => this;
		public IFinalizedRenderable PrepareRender(WorldRenderer wr) => this;
		public Rectangle ScreenBounds(WorldRenderer wr) => Rectangle.Empty;
		public void RenderDebugGeometry(WorldRenderer wr) { }
		public void Render(WorldRenderer wr)
		{
			var at = wr.Viewport.WorldToViewPx(wr.ScreenPxPosition(Pos)) + new int2(-27, -62);
			var start = new Vector3(at.X, at.Y, 0); var cr = Game.Renderer.RgbaColorRenderer;
			var color = value < 45 ? Color.OrangeRed : value < 70 ? Color.Goldenrod : Color.LightGreen;
			cr.FillRect(start, start + new Vector3(54, 19, 0), Color.FromArgb(225, 22, 30, 27));
			Game.Renderer.Fonts["TinyBold"].DrawTextWithContrast("Moral " + value, new Vector2(at.X + 3, at.Y), color, Color.Black, 1);
			cr.FillRect(start + new Vector3(2, 14, 0), start + new Vector3(52, 18, 0), Color.Gray);
			cr.FillRect(start + new Vector3(2, 14, 0), start + new Vector3(2 + value / 2f, 18, 0), color);
		}
	}
}
