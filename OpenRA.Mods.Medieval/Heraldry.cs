using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Medieval
{
	public class MedievalHeraldryInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalHeraldry(init.Self.Owner.Color);
	}
	// One synchronized identity per empire, shared by all present and future actors.
	public sealed class MedievalHeraldry : ISync
	{
		public static readonly string[] Emblems = ["Lion", "Aigle", "Soleil", "Tour", "Cerf", "Fleur de lys"];
		public uint Rgb { get; private set; }
		public string CountryName { get; private set; } = "Mon pays";
		[VerifySync] public int ColorHash => (int)Rgb;
		[VerifySync] public int CountryHash => LocalityNames.Hash(CountryName);
		[VerifySync] public int Emblem { get; private set; }
		public string Hex => Rgb.ToString("X6", CultureInfo.InvariantCulture);
		public Color Color => Color.FromArgb((int)(Rgb >> 16), (int)(Rgb >> 8 & 255), (int)(Rgb & 255));
		public MedievalHeraldry(Color color) { Rgb = (uint)(color.R << 16 | color.G << 8 | color.B); }
		public static MedievalHeraldry For(Player owner) => owner.PlayerActor.Trait<MedievalHeraldry>();
		public static bool TryColor(string text, out uint rgb)
		{
			rgb = 0; var value = text?.Trim();
			if (value?.StartsWith('#') == true) value = value[1..];
			return value?.Length == 6 && value.All(Uri.IsHexDigit)
				&& uint.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out rgb);
		}
		public bool Set(string hex, uint emblem)
		{
			return TryColor(hex, out var rgb) && SetEmpire(CountryName, rgb, emblem);
		}
		public bool SetEmpire(string countryName, uint rgb, uint emblem)
		{
			var name = LocalityNames.Normalize(countryName);
			if (name.Length == 0 || rgb > 0xFFFFFF || emblem >= Emblems.Length) return false;
			CountryName = name; Rgb = rgb; Emblem = (int)emblem; return true;
		}
	}
	public class MedievalHeraldryMarketInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalHeraldryMarket();
	}
	public sealed class MedievalHeraldryMarket : IResolveOrder
	{
		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (self.World.Paused || !MedievalStockpile.Active(self)) return;
			if (order.OrderString == "MedievalSetEmpireIdentity")
				// The lower 24 bits carry RGB; the upper byte carries the emblem. The string carries the country name.
				MedievalHeraldry.For(self.Owner).SetEmpire(order.TargetString, order.ExtraData & 0xFFFFFF, order.ExtraData >> 24);
			else if (order.OrderString == "MedievalSetHeraldry")
				MedievalHeraldry.For(self.Owner).Set(order.TargetString, order.ExtraData);
		}
	}
	public class MedievalHeraldryPalettesInfo : TraitInfo
	{
		[PaletteDefinition(true)] public readonly string BaseName = "md-livery";
		[PaletteDefinition(true)] public readonly string MarketBaseName = "md-market-livery";
		public override object Create(ActorInitializer init) => new MedievalHeraldryPalettes(init.World, this);
	}
	// GPU color shifts preserve texture/light and need no per-unit recoloring or bitmap copies.
	public sealed class MedievalHeraldryPalettes : ILoadsPlayerPalettes, ITickRender
	{
		readonly World world;
		readonly MedievalHeraldryPalettesInfo info;
		readonly Dictionary<string, uint> applied = [];
		public MedievalHeraldryPalettes(World world, MedievalHeraldryPalettesInfo info) { this.world = world; this.info = info; }
		public static void Shift(WorldRenderer wr, string palette, Color color, bool market = false)
		{
			var reference = market ? Color.FromArgb(60, 108, 154) : Color.FromArgb(196, 79, 196);
			var (r, g, b) = color.ToLinear(); var (h, s, v) = Color.RgbToHsv(r, g, b);
			var (rr, rg, rb) = reference.ToLinear(); var (rh, rs, rv) = Color.RgbToHsv(rr, rg, rb);
			wr.SetPaletteColorShift(palette, h - rh, s - rs, v / rv, market ? .50f : .79f, market ? .71f : .88f);
		}
		public void LoadPlayerPalettes(WorldRenderer wr, string playerName, Color color, bool replaceExisting)
		{
			foreach (var prefix in new[] { info.BaseName, info.MarketBaseName })
			{
				wr.AddPalette(prefix + playerName, new ImmutablePalette(Enumerable.Repeat(0u, Palette.Size)), allowOverwrite: replaceExisting);
				Shift(wr, prefix + playerName, color, prefix == info.MarketBaseName);
			}
			applied.Remove(playerName);
		}
		void ITickRender.TickRender(WorldRenderer wr, Actor self)
		{
			foreach (var player in world.Players)
			{
				var heraldry = player.PlayerActor.TraitOrDefault<MedievalHeraldry>();
				if (heraldry == null || applied.TryGetValue(player.InternalName, out var rgb) && rgb == heraldry.Rgb) continue;
				Shift(wr, info.BaseName + player.InternalName, heraldry.Color);
				Shift(wr, info.MarketBaseName + player.InternalName, heraldry.Color, true);
				applied[player.InternalName] = heraldry.Rgb;
			}
		}
	}
	public class MedievalHeraldicImageInfo : TraitInfo, Requires<WithSpriteBodyInfo>
	{
		public readonly string ImagePrefix = "md.heraldic-banner-";
		public override object Create(ActorInitializer init) => new MedievalHeraldicImage(this);
	}
	public sealed class MedievalHeraldicImage : ITick, INotifyCreated
	{
		readonly MedievalHeraldicImageInfo info;
		WithSpriteBody body;
		public MedievalHeraldicImage(MedievalHeraldicImageInfo info) { this.info = info; }
		void INotifyCreated.Created(Actor self) { body = self.TraitsImplementing<WithSpriteBody>().First(b => b.Info.Name == "body"); }
		void ITick.Tick(Actor self)
		{
			if (MedievalStockpile.Active(self))
				body.DefaultAnimation.ChangeImage(info.ImagePrefix + MedievalHeraldry.For(self.Owner).Emblem, self.TraitOrDefault<Mobile>() == null ? "idle" : "stand");
		}
	}
}
