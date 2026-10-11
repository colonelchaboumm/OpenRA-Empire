using System;
using System.Collections.Generic;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	// A shuffled set gives varied streets without touching the world's gameplay RNG.
	// Only a successfully placed house advances it; previews and cancelled input do not.
	public static class HouseStyleDeck
	{
		public static readonly string[] Names = ["Maison du bourg", "Petite chaumiere", "Maison a trois niveaux", "Grande maison", "Maison avec jardin", "Maison a balcon"];
		public const int Count = 6;
		static uint Mix(uint value)
		{
			unchecked { value ^= value >> 16; value *= 0x7feb352d; value ^= value >> 15; value *= 0x846ca68b; return value ^ (value >> 16); }
		}
		static int[] Shuffle(uint seed, int batch)
		{
			int[] styles = [0, 1, 2, 3, 4, 5];
			var state = Mix(seed ^ unchecked((uint)batch * 0x9e3779b9));
			for (var i = Count - 1; i > 0; i--)
			{
				state = Mix(unchecked(state + 0x9e3779b9));
				var j = (int)(state % (uint)(i + 1));
				(styles[i], styles[j]) = (styles[j], styles[i]);
			}
			return styles;
		}
		public static int At(int placed, uint seed)
		{
			if (placed < 0) throw new ArgumentOutOfRangeException(nameof(placed));
			var batch = placed / Count;
			var styles = Shuffle(seed, batch);
			// Swapping the first two leaves the last style unchanged, so no recursion is needed.
			if (batch > 0 && styles[0] == Shuffle(seed, batch - 1)[Count - 1]) (styles[0], styles[1]) = (styles[1], styles[0]);
			return styles[placed % Count];
		}
		public static uint Seed(World world) => unchecked((uint)world.LobbyInfo.GlobalSettings.RandomSeed);
		public static int Next(Player player) => At(player.PlayerActor.Trait<MedievalSettlement>().HousesPlaced, Seed(player.World) ^ player.PlayerActor.ActorID);
	}

	public class MedievalHouseStyleInit : ValueActorInit<int>, ISingleInstanceInit
	{
		public MedievalHouseStyleInit(int value) : base(value) { }
	}
	public class MedievalHouseAppearanceInfo : TraitInfo, Requires<MedievalBuildingInfo>
	{
		public readonly int[] ChimneyX = [-1, 0, 0, 0, 0, 0];
		public readonly int[] ChimneyY = [-58, -50, -100, -70, -60, -80];
		public readonly int[] SelectionHeights = [60, 50, 100, 70, 60, 80];
		public override object Create(ActorInitializer init) => new MedievalHouseAppearance(init, this);
	}
	public sealed class MedievalHouseAppearance : ISync
	{
		readonly MedievalHouseAppearanceInfo info;
		[VerifySync] public readonly int Style;
		public string Name => HouseStyleDeck.Names[Style];
		public int2 ChimneyOffset => new(info.ChimneyX[Style], info.ChimneyY[Style]);
		public int Height => MedievalArchitecture.For("md.house", Style).Height(info.SelectionHeights[Style]);
		public MedievalHouseAppearance(ActorInitializer init, MedievalHouseAppearanceInfo info)
		{
			this.info = info;
			Style = init.GetValue<MedievalHouseStyleInit, int>(info, HouseStyleDeck.At((int)(init.Self.ActorID % int.MaxValue), HouseStyleDeck.Seed(init.World)));
			if (Style < 0 || Style >= HouseStyleDeck.Count || info.ChimneyX.Length != HouseStyleDeck.Count
				|| info.ChimneyY.Length != HouseStyleDeck.Count || info.SelectionHeights.Length != HouseStyleDeck.Count)
				throw new ArgumentException("A medieval house requires six styles with matching chimney and height data.");
		}
	}

	public class WithMedievalHouseBodyInfo : WithSpriteBodyInfo, Requires<MedievalHouseAppearanceInfo>
	{
		public override object Create(ActorInitializer init) => new WithMedievalHouseBody(init, this);
		public override IEnumerable<IActorPreview> RenderPreviewSprites(ActorPreviewInitializer init, string image, int facings, PaletteReference palette)
		{
			if (!EnabledByDefault) yield break;
			var animation = new Animation(init.World, image);
			var style = Math.Clamp(init.GetValue<MedievalHouseStyleInit, int>(0), 0, HouseStyleDeck.Count - 1);
			animation.PlayFetchIndex(Sequence, () => style);
			yield return new SpriteActorPreview(animation, () => WVec.Zero, () => 0, palette);
		}
	}
	public sealed class WithMedievalHouseBody : WithSpriteBody
	{
		public WithMedievalHouseBody(ActorInitializer init, WithMedievalHouseBodyInfo info) : base(init, info) { }
		protected override void TraitEnabled(Actor self) => DefaultAnimation.PlayFetchIndex(Info.Sequence, () => self.Trait<MedievalHouseAppearance>().Style);
	}

	public class MedievalHouseSelectableInfo : IsometricSelectableInfo, Requires<MedievalHouseAppearanceInfo>
	{
		public override object Create(ActorInitializer init) => new MedievalHouseSelectable(init.Self, this);
	}
	public sealed class MedievalHouseSelectable : IsometricSelectable
	{
		readonly MedievalHouseSelectableInfo info;
		public MedievalHouseSelectable(Actor self, MedievalHouseSelectableInfo info) : base(self, info) { this.info = info; }
		int Height(Actor self) => MedievalStockpile.Active(self) ? self.Trait<MedievalHouseAppearance>().Height : 20;
		public override Polygon Bounds(Actor self, WorldRenderer wr) => Bounds(self, wr, info.Bounds, Height(self));
		public override Polygon DecorationBounds(Actor self, WorldRenderer wr) => Bounds(self, wr, info.DecorationBounds != null ? info.DecorationBounds : info.Bounds, Height(self));
	}
}
