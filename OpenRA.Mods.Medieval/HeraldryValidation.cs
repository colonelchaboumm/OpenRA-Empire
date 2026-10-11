using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Network;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Medieval
{
	public class MedievalHeraldryValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalHeraldryValidation();
	}
	public sealed class MedievalHeraldryValidation : ITick, IWorldLoaded
	{
		WorldRenderer renderer;
		string output;
		string cityName;
		int tick, population;
		Actor market, stall, enemyMarket, barracks, recruiter, soldier, enemy, flag, bearer, duplicate, drummer;
		Player foe;
		bool finished, trained, carried, captured, killed;
		readonly List<string> errors = [];
		readonly HashSet<int> frames = [];
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void Check(bool ok, string message) { if (!ok && !errors.Contains(message)) errors.Add(message); }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "heraldry"); Directory.CreateDirectory(output);
		}
		Actor Spawn(World w, string type, Player owner, int x, int y) => w.CreateActor("md." + type, [new OwnerInit(owner), new LocationInit(Cell(x, y))]);
		void Select(World w, Actor actor) => Sync.RunUnsynced(w, () => w.Selection.Combine(w, [actor], false, true));
		void Button(World w, string id) => Sync.RunUnsynced(w, () =>
		{
			var button = Ui.Root.Get<ButtonWidget>(id); Check(!button.IsDisabled(), "Button disabled: " + id); button.OnClick();
		});
		void Identity(World w, Actor at, string hex, int emblem, string country)
		{
			Select(w, at); Button(w, "MD_HERALDRY_OPEN");
			Sync.RunUnsynced(w, () =>
			{
				Check(Ui.Root.Get<TextFieldWidget>("MD_HERALDRY_COUNTRY").Text == MedievalHeraldry.For(at.Owner).CountryName, "Opening any market must load the current country name.");
				Ui.Root.Get<TextFieldWidget>("MD_HERALDRY_COLOR").Text = hex;
				Ui.Root.Get<TextFieldWidget>("MD_HERALDRY_COUNTRY").Text = "   ";
				Check(Ui.Root.Get<ButtonWidget>("MD_HERALDRY_APPLY").IsDisabled(), "A blank country must disable Apply.");
				Ui.Root.Get<TextFieldWidget>("MD_HERALDRY_COUNTRY").Text = country;
			});
			Button(w, "MD_HERALDRY_EMBLEM_" + emblem); Button(w, "MD_HERALDRY_APPLY");
		}
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.TraitOrDefault<MedievalBuilding>() != null || a.TraitOrDefault<MedievalSource>() != null || a.TraitOrDefault<Mobile>() != null).ToArray()) a.Dispose();
			foe = w.Players.Single(p => p.InternalName == "Creeps");
			w.LocalPlayer.EnemyPlayersMask = foe.PlayerMask; foe.EnemyPlayersMask = w.LocalPlayer.PlayerMask;
			market = Spawn(w, "market", w.LocalPlayer, 22, 28); market.Trait<MedievalStockpile>().Stock.Add(1, 200);
			stall = Spawn(w, "marketstall", w.LocalPlayer, 25, 36);
			enemyMarket = Spawn(w, "market", foe, 37, 32); enemyMarket.Trait<MedievalStockpile>().Stock.Add(1, 200);
			Spawn(w, "warehouse", w.LocalPlayer, 22, 33).Trait<MedievalStockpile>().Stock.Add(1, 200);
			barracks = Spawn(w, "barracks", w.LocalPlayer, 26, 28);
			recruiter = Spawn(w, "recruiter", w.LocalPlayer, 25, 29); barracks.Trait<MedievalMilitaryWorkshop>().AssignStaff(barracks, recruiter);
			soldier = Spawn(w, "heraldrytroop", w.LocalPlayer, 30, 31); enemy = Spawn(w, "heraldrytroop", foe, 33, 31);
			flag = Spawn(w, "banner", w.LocalPlayer, 24, 31);
			Spawn(w, "builder", w.LocalPlayer, 25, 33); Spawn(w, "farmer", w.LocalPlayer, 26, 34);
			Spawn(w, "banner", foe, 36, 31);
		}
		void Finish(World w)
		{
			Check(trained && carried, "A recruiter must carry food and finish training the standard bearer.");
			Check(captured && killed, "Capture and death checks must finish.");
			Check(frames.Count >= 4, "The flag must animate in the native renderer.");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, tick, errors, trained, carried, captured, killed, frames = frames.Count,
				color = MedievalHeraldry.For(w.LocalPlayer).Hex, emblem = MedievalHeraldry.For(w.LocalPlayer).Emblem,
				country = MedievalHeraldry.For(w.LocalPlayer).CountryName }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL HERALDRY: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors)));
			finished = true; Game.RunAfterDelay(1000, Game.Exit);
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (finished || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.AddFrameEndTask(Setup); return; }
			Sync.RunUnsynced(w, () => renderer.Viewport.Center(w.Map.CenterOfCell(Cell(30, 32))));
			if (recruiter.Trait<MedievalWorker>() is { Cargo: > 0, Resource: 1 }) carried = true;
			if (bearer != null && bearer.IsInWorld && !bearer.IsDead) frames.Add(bearer.Trait<WithMedievalInfantryBody>().Frame);
			if (tick == 10)
			{
				Select(w, enemyMarket);
				Check(!Ui.Root.Get<ButtonWidget>("MD_HERALDRY_OPEN").IsVisible(), "The identity panel must be unavailable on an enemy market.");
				Identity(w, market, "#B63838", 0, "Royaume d'Été");
				// Map actors (including Creeps) share the host client. Exercise the real validators with a distinct sender.
				using var manager = new OrderManager(new EchoConnection()) { LobbyInfo = w.LobbyInfo };
				var order = new Order("MedievalSetEmpireIdentity", market, false) { TargetString = "Nom interdit", ExtraData = 0x01356FAD };
				Check(w.OrderValidators.Any(v => !v.OrderValidation(manager, w, int.MaxValue, order)), "A different client must not rename this player's country.");
				Check(w.OrderValidators.All(v => v.OrderValidation(manager, w, market.Owner.ClientIndex, order)), "The owning client must be allowed to change its country.");
			}
			if (tick == 30)
			{
				population = MedievalSettlement.Population(w.LocalPlayer);
				Check(MedievalHeraldry.For(w.LocalPlayer).Hex == "B63838", "The real market button must change the empire color.");
				Check(MedievalHeraldry.For(w.LocalPlayer).CountryName == "Royaume d'Été", "The country name must apply with the color and emblem.");
				Check(MedievalHeraldry.For(foe).CountryName == "Mon pays", "Changing the local identity must preserve the opponent's country.");
				cityName = MedievalLocalities.MarketName(market);
				// Set the map opponent's own identity independently.
				MedievalHeraldry.For(foe).SetEmpire("Royaume voisin", 0x356FAD, 1);
				Game.Renderer.SaveScreenshot(Path.Combine(output, "identity-panel.png"));
				Select(w, barracks); Button(w, "MD_BARRACKS_STANDARD");
			}
			if (!trained && tick > 40)
			{
				bearer = w.ActorsHavingTrait<MedievalStandardBearer>().FirstOrDefault(a => a.Owner == w.LocalPlayer);
				if (bearer != null)
				{
					trained = true;
					Check(MedievalSettlement.Population(w.LocalPlayer) == population + 1, "The standard bearer must occupy one housing slot.");
					Check(MedievalFoodEconomy.Demand(w.LocalPlayer) == (population + 1) * 2, "The standard bearer must consume normal meals.");
					w.IssueOrder(new Order("Move", bearer, Target.FromCell(w, Cell(30, 30)), false));
				}
			}
			if (tick == 1200)
			{
				if (bearer == null) { Check(false, "Recruitment did not finish in time."); Finish(w); return; }
				Check(soldier.Trait<MedievalMorale>().StandardBonus == 15 && enemy.Trait<MedievalMorale>().StandardBonus == 0, "Only nearby allies receive the flag bonus.");
				var food = w.ActorsWithTrait<MedievalStockpile>().Where(a => a.Actor.Owner == w.LocalPlayer).Sum(a => a.Trait.Stock.Amount(1))
					+ barracks.Trait<MedievalMilitaryWorkshop>().Inputs.Amount(1) + (recruiter.Trait<MedievalWorker>().Resource == 1 ? recruiter.Trait<MedievalWorker>().Cargo : 0);
				Check(food + w.LocalPlayer.PlayerActor.Trait<MedievalFoodEconomy>().TotalConsumed == 375, "Recruitment must consume exactly 25 food in addition to meals.");
				Sync.RunUnsynced(w, () =>
				{
					foreach (var a in new[] { soldier, bearer, flag })
						Check(a.Render(renderer).OfType<IPalettedRenderable>().Any(r => r.Palette?.Name == "md-livery" + a.Owner.InternalName), "Native unit/flag render must use its empire palette: " + a.Info.Name);
					foreach (var a in new[] { market, stall })
						Check(a.Render(renderer).OfType<IPalettedRenderable>().Any(r => r.Palette?.Name == "md-market-livery" + a.Owner.InternalName), "Native market render must use its empire palette.");
				});
				w.AddFrameEndTask(_ => { duplicate = Spawn(w, "standardbearer", w.LocalPlayer, 31, 30); drummer = Spawn(w, "drummer", w.LocalPlayer, 29, 31); });
				Select(w, bearer);
			}
			if (tick == 1300)
			{
				Check(soldier.Trait<MedievalMorale>().Encouragement == 30, "Duplicate flags and a drummer must be capped at +30.");
				Game.Renderer.SaveScreenshot(Path.Combine(output, "standard-bearers.png"));
				Identity(w, stall, "#39814B", 4, "Terres du Cerf");
			}
			if (tick == 1340)
			{
				Check(MedievalHeraldry.For(w.LocalPlayer).Emblem == 4 && MedievalHeraldry.For(w.LocalPlayer).Hex == "39814B", "A small market must share the empire identity.");
				Check(MedievalHeraldry.For(foe).Hex == "356FAD", "Another player's identity must stay independent.");
				Check(MedievalHeraldry.For(w.LocalPlayer).CountryName == "Terres du Cerf" && MedievalHeraldry.For(foe).CountryName == "Royaume voisin", "Country renaming at a small market must affect only its owner.");
				Check(MedievalLocalities.MarketName(market) == cityName, "Changing country must preserve the city's name.");
				Button(w, "MD_HERALDRY_CLOSE"); Select(w, market); Button(w, "MD_HERALDRY_OPEN");
				Check(Ui.Root.Get<TextFieldWidget>("MD_HERALDRY_COUNTRY").Text == "Terres du Cerf", "The large market must reload the name chosen at the small market.");
				Check(bearer.Trait<WithMedievalInfantryBody>().ImageName.EndsWith("-4", StringComparison.Ordinal)
					&& flag.TraitsImplementing<WithSpriteBody>().First(b => b.Info.Name == "body").DefaultAnimation.Name.EndsWith("-4", StringComparison.Ordinal), "Existing carried and decorative flags must change emblem.");
				Select(w, bearer);
			}
			if (tick == 1360)
			{
				Game.Renderer.SaveScreenshot(Path.Combine(output, "green-empire.png"));
				w.AddFrameEndTask(_ => { duplicate.Dispose(); drummer.Dispose(); bearer.ChangeOwner(foe); });
			}
			if (tick == 1410)
			{
				captured = bearer.Trait<WithMedievalInfantryBody>().ImageName.EndsWith("-1", StringComparison.Ordinal)
					&& soldier.Trait<MedievalMorale>().StandardBonus == 0 && enemy.Trait<MedievalMorale>().StandardBonus == 15;
				Check(captured, "Ownership changes must transfer the heraldry and the aura to the new side.");
				w.IssueOrder(new Order("Move", bearer, Target.FromCell(w, Cell(48, 45)), false));
			}
			if (tick == 2000)
			{
				Check(enemy.Trait<MedievalMorale>().StandardBonus == 0, "A flag leaving the radius must stop supporting the troop.");
				bearer.Trait<Health>().InflictDamage(bearer, bearer, new Damage(100000), true);
				killed = !bearer.Trait<MedievalStandardBearer>().Active(bearer);
				Check(killed, "A killed standard bearer must provide no morale.");
				Finish(w);
			}
		}
	}
}
