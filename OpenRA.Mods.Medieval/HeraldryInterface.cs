using System;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.Medieval
{
	public sealed class MedievalHeraldryLogic : ChromeLogic
	{
		readonly World world;
		readonly WorldRenderer renderer;
		readonly TextFieldWidget color;
		readonly TextFieldWidget country;
		Actor market;
		bool open, pending;
		int emblem;
		uint previewRgb = uint.MaxValue;
		string status = "";
		Actor Selected() => world.Selection.Actors.FirstOrDefault(a => a.Owner == world.LocalPlayer && MedievalStockpile.Active(a)
			&& a.TraitOrDefault<MedievalHeraldryMarket>() != null);
		public override void Tick()
		{
			if (!open) return;
			if (Selected() != market) { Close(); return; }
			if (MedievalHeraldry.TryColor(color.Text, out var rgb))
			{
				if (rgb != previewRgb)
				{
					previewRgb = rgb;
					MedievalHeraldryPalettes.Shift(renderer, "md-heraldry-preview", Color.FromArgb((int)(rgb >> 16), (int)(rgb >> 8 & 255), (int)(rgb & 255)));
				}
				var identity = MedievalHeraldry.For(market.Owner);
				if (pending && identity.Rgb == rgb && identity.Emblem == emblem && identity.CountryName == LocalityNames.Normalize(country.Text))
				{
					country.Text = identity.CountryName; pending = false; status = "Identite appliquee a tout votre empire.";
				}
			}
		}
		void Close() { open = false; color.YieldKeyboardFocus(); country.YieldKeyboardFocus(); }
		[ObjectCreator.UseCtor]
		public MedievalHeraldryLogic(Widget widget, World world, WorldRenderer worldRenderer)
		{
			this.world = world; renderer = worldRenderer;
			color = widget.Get<TextFieldWidget>("MD_HERALDRY_COLOR");
			country = widget.Get<TextFieldWidget>("MD_HERALDRY_COUNTRY");
			if (world.LocalPlayer == null) return;
			renderer.AddPalette("md-heraldry-preview", new ImmutablePalette(Enumerable.Repeat(0u, Palette.Size)));
			MedievalHeraldryPalettes.Shift(renderer, "md-heraldry-preview", MedievalHeraldry.For(world.LocalPlayer).Color);
			var toggle = widget.Get<ButtonWidget>("MD_HERALDRY_OPEN");
			toggle.GetText = () => "Identité du pays";
			toggle.IsVisible = () => Selected() != null;
			toggle.OnClick = () =>
			{
				market = Selected(); if (market == null) return;
				if (open) { Close(); return; }
				open = true; pending = false; status = "";
				var identity = MedievalHeraldry.For(market.Owner); emblem = identity.Emblem; color.Text = "#" + identity.Hex;
				country.Text = identity.CountryName;
			};
			widget.Get("MD_HERALDRY_PANEL").IsVisible = () => open && Selected() == market;
			widget.Get<LabelWidget>("MD_HERALDRY_TITLE").GetText = () => "IDENTITE DE VOTRE EMPIRE";
			widget.Get<LabelWidget>("MD_HERALDRY_HINT").GetText = () => "Drapeaux, tenues, armures, charrettes et marches.\nLes unites et batiments deja presents changent aussi.";
			widget.Get<LabelWidget>("MD_HERALDRY_LABEL").GetText = () => "Couleur : #RRGGBB";
			widget.Get<LabelWidget>("MD_HERALDRY_COUNTRY_LABEL").GetText = () => "Nom du pays :";
			for (var i = 0; i < MedievalHeraldry.Emblems.Length; i++)
			{
				var index = i; var button = widget.Get<ButtonWidget>("MD_HERALDRY_EMBLEM_" + i);
				button.GetText = () => ""; button.IsHighlighted = () => emblem == index;
				button.OnClick = () => { emblem = index; pending = false; status = ""; };
				widget.Get<LabelWidget>("MD_HERALDRY_NAME_" + i).GetText = () => MedievalHeraldry.Emblems[index];
				var icon = widget.Get<SpriteWidget>("MD_HERALDRY_ICON_" + i);
				icon.GetSprite = () => world.Map.Sequences.GetSequence("md.heraldry-icons", "idle").GetSprite(index);
				icon.GetPalette = () => "md-heraldry-preview";
			}
			var presets = new[] { "B63838", "356FAD", "D2A841", "39814B", "8654A2", "E4DDC7", "393B42", "C87435" };
			for (var i = 0; i < presets.Length; i++)
			{
				var value = presets[i]; var swatch = widget.Get<ColorBlockWidget>("MD_HERALDRY_PRESET_" + i);
				Color.TryParse(value, out var tint); swatch.GetColor = () => tint;
				swatch.OnMouseDown = _ => { color.Text = "#" + value; pending = false; status = ""; };
			}
			widget.Get<ColorBlockWidget>("MD_HERALDRY_SWATCH").GetColor = () => MedievalHeraldry.TryColor(color.Text, out var rgb)
				? Color.FromArgb((int)(rgb >> 16), (int)(rgb >> 8 & 255), (int)(rgb & 255)) : Color.Gray;
			var apply = widget.Get<ButtonWidget>("MD_HERALDRY_APPLY"); apply.GetText = () => "Appliquer a l'empire";
			apply.IsDisabled = () => world.Paused || Selected() != market || !MedievalHeraldry.TryColor(color.Text, out _) || LocalityNames.Normalize(country.Text).Length == 0;
			apply.OnClick = () =>
			{
				if (apply.IsDisabled()) return;
				MedievalHeraldry.TryColor(color.Text, out var rgb);
				world.IssueOrder(new Order("MedievalSetEmpireIdentity", market, false) { ExtraData = rgb | (uint)emblem << 24, TargetString = country.Text });
				pending = true; status = "Application en cours..."; color.YieldKeyboardFocus(); country.YieldKeyboardFocus();
			};
			color.OnEnterKey = _ => { apply.OnClick(); return true; };
			country.OnEnterKey = _ => { apply.OnClick(); return true; };
			widget.Get<LabelWidget>("MD_HERALDRY_STATUS").GetText = () => LocalityNames.Normalize(country.Text).Length == 0
				? "Saisissez un nom de pays (32 caracteres maximum)."
				: !MedievalHeraldry.TryColor(color.Text, out _) ? "Saisissez six chiffres hexadecimaux, par exemple #356FAD." : status;
			var close = widget.Get<ButtonWidget>("MD_HERALDRY_CLOSE"); close.GetText = () => "Fermer";
			close.OnClick = Close;
		}
	}
}
