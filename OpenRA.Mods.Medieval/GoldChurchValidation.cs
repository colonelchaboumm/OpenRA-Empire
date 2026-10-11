using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Medieval
{
	public class MedievalGoldChurchValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalGoldChurchValidation();
	}
	public sealed class MedievalGoldChurchValidation : IWorldLoaded, ITick
	{
		WorldRenderer renderer;
		string output;
		Actor depot, mine, church, priest, soldier, citizen, ore, market, cart;
		int tick, builtAt, priestAt, initialGold, initialFood, generated, soldierHP, citizenHP, priestFood;
		bool done, mining, hauling, buildingGold, waited, cured, moral, declined, interrupted, cartGold, goldConserved, chart;
		readonly List<string> errors = [];
		readonly HashSet<int> healingFrames = [];
		readonly HashSet<int> sortingFrames = [];
		bool healingPulse, healingStopped;
		int firstSortingAt, audibleStrikes;
		readonly HashSet<string> oreViews = [];
		string orePreview;
		int orePreviewUntil;
		void PreviewOre(World w)
		{
			var source = ore.IsInWorld ? ore.Trait<MedievalSource>() : null;
			var state = !ore.IsInWorld ? "epuise" : source.Remaining == source.Info.Amount ? "intact"
				: source.Fraction <= .12f && source.Remaining > 0 ? "presque-vide"
				: source.Fraction <= .5f && source.Remaining > 0 ? "mi-extraction" : null;
			if (orePreview == null && state != null && oreViews.Add(state)) { orePreview = state; orePreviewUntil = tick + 6; }
			if (orePreview == null) return;
			Sync.RunUnsynced(w, () =>
			{
				renderer.Viewport.Center(w.Map.CenterOfCell(Cell(36, 29)));
				renderer.Viewport.AdjustZoom((float)Math.Log(1.8f / renderer.Viewport.Zoom));
			});
			if (tick < orePreviewUntil) return;
			Game.Renderer.SaveScreenshot(Path.Combine(output, "gisement-" + orePreview + ".png")); orePreview = null;
		}
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void Check(bool value, string message) { if (!value) errors.Add(message); }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "gold-church", "native"); Directory.CreateDirectory(output);
		}
		void Setup(World w)
		{
			generated = w.ActorsHavingTrait<MedievalSource>().Count(a => a.Trait<MedievalSource>().Info.Resource == Material.Gold);
			foreach (var a in w.Actors.Where(a => a.TraitOrDefault<MedievalBuilding>() != null || a.TraitOrDefault<MedievalSource>() != null || a.TraitOrDefault<Mobile>() != null).ToArray()) a.Dispose();
			Actor Spawn(string name, int x, int y) => w.CreateActor("md." + name, [new OwnerInit(w.LocalPlayer), new LocationInit(Cell(x, y))]);
			depot = Spawn("warehouse", 23, 30); var stock = depot.Trait<MedievalStockpile>().Stock; stock.Add(0, 350); stock.Add(3, 250); stock.Add(1, 150);
			market = Spawn("market", 24, 35); market.Trait<MedievalStockpile>().Stock.Add(1, 200);
			Spawn("house", 22, 25); Spawn("house", 25, 25); Spawn("house", 28, 25);
			mine = Spawn("goldmine", 34, 29);
			for (var i = 0; i < 3; i++) { Spawn("goldminer", 35+i, 33); Spawn("hauler", 27+i, 32); Spawn("builder", 27+i, 38); }
			soldier = Spawn("militia", 30, 41); citizen = Spawn("farmer", 31, 41);
			soldier.Trait<AutoTarget>().SetStance(soldier, UnitStance.HoldFire);
			cart = Spawn("supplycart", 23, 39);
			ore = w.CreateActor("md.gold", [new OwnerInit(w.Players.First(p => p.InternalName == "Neutral")), new LocationInit(Cell(38, 28))]);
			initialGold = ore.Trait<MedievalSource>().Remaining;
		}
		int TotalGold(World w) => w.Actors.Where(a => a.IsInWorld && !a.IsDead).Sum(a => (a.TraitOrDefault<MedievalStockpile>()?.Stock.Amount(4) ?? 0)
			+ (a.TraitOrDefault<MedievalBuilding>()?.Materials.Amount(4) ?? 0)
			+ (a.TraitOrDefault<MedievalSource>() is MedievalSource src && src.Info.Resource == Material.Gold ? src.Remaining : 0)
			+ (a.TraitOrDefault<MedievalWorker>() is { Resource: 4, EquipmentCargo: false, CampCargo: false } worker ? worker.Cargo : 0));
		int AccountedFood(World w) => w.Actors.Where(a => a.Owner == w.LocalPlayer && !a.IsDead).Sum(a => (a.TraitOrDefault<MedievalStockpile>()?.Stock.Amount(1) ?? 0)
			+ (a.TraitOrDefault<MedievalWorker>() is { Resource: 1, EquipmentCargo: false, CampCargo: false } worker ? worker.Cargo : 0))
			+ w.LocalPlayer.PlayerActor.Trait<MedievalFoodEconomy>().TotalConsumed;
		void Care(World w, Actor a) => w.IssueOrder(new Order("MedievalChurchCare", a, Target.FromActor(church), false));
		void Finish(World w)
		{
			Check(generated > 0, "The ordinary map must acquire finite gold deposits.");
			Check(builtAt > 0 && church?.Trait<MedievalBuilding>().Materials.Amount(4) == 60, "Builders must deliver exactly 60 gold and finish the church.");
			Check(mining && hauling && buildingGold, "Gold must be physically mined, hauled and carried by builders.");
			Check(waited, "Wounded people must wait without free healing when no priest is present.");
			Check(priestAt > 0 && priestFood == 30, "Church recruitment must consume exactly 30 food.");
			Check(cured, "A priest at the church must fully heal both a soldier and a citizen.");
			Check(moral, "A nearby priest must grant a real morale bonus.");
			Check(declined, "Removing the priest must stop church healing and remove its morale aura.");
			Check(interrupted, "A manual movement order must immediately cancel treatment.");
			Check(cartGold, "A supply cart must accept selected gold loaded by a carrier.");
			Check(depot.Trait<MedievalStockyard>().HasArea, "Gold must be displayed in an actual exterior stockyard.");
			Check(!ore.IsInWorld, "A fully mined gold deposit must disappear and leave rubble.");
			Check(w.Actors.Any(a => a.Info.Name == "md.goldrubble" && a.Location == Cell(38, 28)), "Exhausted gold must leave its own passable rocky rubble.");
			goldConserved = TotalGold(w) == initialGold;
			Check(goldConserved, "Gold conservation: deposits + carried + stored + incorporated must equal initial gold.");
			Check(healingFrames.Count == 16 && healingPulse && healingStopped, "Real care must play all sixteen priest frames, show a patient pulse and finish cleanly.");
			Check(sortingFrames.Count > 20, "Actual gold deliveries must animate the sorting workshop.");
			var bells = w.WorldActor.Trait<MedievalAudio>().Played.GetValueOrDefault("church-bell");
			Check(bells > 0 && church.Trait<MedievalChurchAnimation>().Bell.Strikes > 0, "The church must swing and play its actual bell sound.");
			File.WriteAllText(Path.Combine(output,"animation-result.json"),JsonSerializer.Serialize(new { passed=errors.Count==0, errors, healingFrames=healingFrames.Count, healingPulse, healingStopped, sortingFrames=sortingFrames.Count, bells, audibleStrikes, strikes=church.Trait<MedievalChurchAnimation>().Bell.Strikes, audio=w.WorldActor.Trait<MedievalAudio>().Played, Game.Sound.DisableWorldSounds, Game.Sound.DisableAllSounds },new JsonSerializerOptions{WriteIndented=true}));
			File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new { passed=errors.Count==0,errors,tick,generated,builtAt,priestAt,mining,hauling,buildingGold,waited,cured,moral,declined,interrupted,cartGold,goldConserved,initialGold,totalGold=TotalGold(w),priestFood },new JsonSerializerOptions{WriteIndented=true}));
			Console.WriteLine("MEDIEVAL GOLD CHURCH: "+(errors.Count==0?"PASS":string.Join("; ",errors)));done=true;Game.RunAfterDelay(800,Game.Exit);
		}
		void ITick.Tick(Actor self)
		{
			var w=self.World;if(done||w.LocalPlayer==null)return;tick++;
			if(tick==1){w.AddFrameEndTask(Setup);return;}if(tick<10)return;
			if(tick==20)
			{
				w.IssueOrder(new Order("Stop",citizen,false));
				w.IssueOrder(new Order("MedievalPlaceStockyard",depot,Target.FromCell(w,Cell(18,30)),false){ExtraLocation=Cell(21,33)});
				w.IssueOrder(new Order("MedievalPlace",w.LocalPlayer.PlayerActor,Target.FromCell(w,Cell(30,35)),false){TargetString="md.church"});
				Sync.RunUnsynced(w,()=>{renderer.Viewport.Center(w.Map.CenterOfCell(Cell(30,33)));renderer.Viewport.AdjustZoom((float)Math.Log(1.2f/renderer.Viewport.Zoom));});
			}
			church ??= w.ActorsHavingTrait<MedievalChurch>().FirstOrDefault(a=>a.Owner==w.LocalPlayer);
			if (mine.Trait<MedievalGoldWorkshopAnimation>() is { Operating: true } workshop)
			{
				sortingFrames.Add(workshop.Phase);
				if(firstSortingAt==0){firstSortingAt=tick;Sync.RunUnsynced(w,()=>{renderer.Viewport.AdjustZoom((float)Math.Log(1.8f/renderer.Viewport.Zoom));renderer.Viewport.Center(mine.CenterPosition);});}
			}
			if(firstSortingAt>0&&(tick==firstSortingAt+8||tick==firstSortingAt+22)) Game.Renderer.SaveScreenshot(Path.Combine(output,"tri-or-"+(tick-firstSortingAt)+".png"));
			if (church?.Trait<MedievalChurchAnimation>().Bell.Phase == 12 && w.WorldActor.Trait<MedievalAudio>().Audible(church.CenterPosition)) audibleStrikes++;
			if (priest is { IsDead: false, IsInWorld: true } && priest.Trait<WithMedievalInfantryBody>() is { Sequence: "heal" } animation) healingFrames.Add(animation.Frame);
			healingPulse |= soldier.Trait<MedievalPatient>().HealPulse > 0 || citizen.Trait<MedievalPatient>().HealPulse > 0;
			foreach(var a in w.ActorsHavingTrait<MedievalWorker>().Where(a=>a.IsInWorld&&!a.IsDead))
			{
				var worker=a.Trait<MedievalWorker>();
				mining |= worker.Info.Profession=="goldminer"&&worker.IsHarvesting;
				hauling |= worker.Info.Profession=="hauler"&&worker.Resource==4&&worker.Cargo>0;
				buildingGold |= worker.Info.Profession=="builder"&&worker.Resource==4&&worker.Cargo>0;
			}
			if(church!=null&&MedievalStockpile.Active(church)&&builtAt==0)
			{
				builtAt=tick;
				foreach(var a in new[]{soldier,citizen}){var hp=a.Trait<Health>();hp.InflictDamage(a,a,new Damage(hp.MaxHP/2),true);}
				soldierHP=soldier.Trait<Health>().HP;citizenHP=citizen.Trait<Health>().HP;Care(w,soldier);Care(w,citizen);
				Sync.RunUnsynced(w,()=>{w.Selection.Combine(w,[church],false,true);renderer.Viewport.Center(church.CenterPosition);});
			}
			if(builtAt>0&&tick==builtAt+250)
			{
				waited=soldier.Trait<Health>().HP==soldierHP&&citizen.Trait<Health>().HP==citizenHP;
				initialFood=AccountedFood(w);
				Sync.RunUnsynced(w,()=>Ui.Root.Get<ButtonWidget>("MD_CHURCH_RECRUIT").OnClick());
			}
			if(builtAt>0&&tick==builtAt+258)priestFood=initialFood-AccountedFood(w);
			if(builtAt>0&&tick>builtAt&&tick<builtAt+160) Sync.RunUnsynced(w,()=>renderer.Viewport.Center(church.CenterPosition));
			if(builtAt>0&&(tick==builtAt+13||tick==builtAt+38)) Game.Renderer.SaveScreenshot(Path.Combine(output,"cloche-"+(tick-builtAt)+".png"));
			priest ??= w.ActorsHavingTrait<MedievalPriest>().FirstOrDefault(a=>a.Owner==w.LocalPlayer);
			if(priest!=null&&priestAt==0)priestAt=tick;
			if(priestAt>0&&tick==priestAt+20)
			{
				var cell=MedievalAccess.Perimeter(church).Where(c=>w.Map.Contains(c)&&!w.ActorMap.GetActorsAt(c).Any()).OrderByDescending(c=>w.Map.CenterOfCell(c).Y).First();
				w.IssueOrder(new Order("Move",priest,Target.FromCell(w,cell),false));
			}
			if(priestAt>0&&tick==priestAt+90) Sync.RunUnsynced(w,()=>{w.Selection.Clear();renderer.Viewport.AdjustZoom((float)Math.Log(1.8f/renderer.Viewport.Zoom));renderer.Viewport.Center(church.CenterPosition);});
			if(priestAt>0&&tick>priestAt+90&&tick<priestAt+150) Sync.RunUnsynced(w,()=>renderer.Viewport.Center(church.CenterPosition));
			if(priestAt>0&&(tick==priestAt+125||tick==priestAt+143)) Game.Renderer.SaveScreenshot(Path.Combine(output,"soins-"+(tick-priestAt)+".png"));
			if(priestAt>0&&tick<priestAt+500)moral |= soldier.Trait<MedievalMorale>().PriestBonus==10;
			if(priestAt>0&&tick==priestAt+550)
			{
				cured=new[]{soldier,citizen}.All(a=>a.Trait<Health>().HP==a.Trait<Health>().MaxHP&&a.Trait<MedievalPatient>().Church==null);
				healingStopped = !priest.Trait<MedievalPriest>().Healing(priest) && priest.Trait<WithMedievalInfantryBody>().Sequence != "heal";
				Game.Renderer.SaveScreenshot(Path.Combine(output,"eglise-et-mine.png"));chart=true;
				priest.Kill(soldier);
				soldier.Trait<Health>().InflictDamage(soldier,soldier,new Damage(1000),true);soldierHP=soldier.Trait<Health>().HP;Care(w,soldier);
				w.IssueOrder(new Order("MedievalCartCargo",cart,false){ExtraData=4});
			}
			if(priestAt>0&&tick==priestAt+700)
			{
				declined=soldier.Trait<Health>().HP==soldierHP&&soldier.Trait<MedievalMorale>().PriestBonus==0;
				w.IssueOrder(new Order("Move",soldier,Target.FromCell(w,Cell(28,42)),false));
			}
			if(priestAt>0&&tick==priestAt+560) Sync.RunUnsynced(w,()=>{w.Selection.Combine(w,[depot],false,true);renderer.Viewport.Center(w.Map.CenterOfCell(Cell(20,32)));});
			if(priestAt>0&&tick==priestAt+575)
			{
				Sync.RunUnsynced(w,()=>Check(Ui.Root.Get<LabelWidget>("MD_YARD_MORE").GetText().Contains($"Or {depot.Trait<MedievalStockpile>().Stock.Amount(4)}/{depot.Trait<MedievalStockpile>().Stock.Capacity(4)}", StringComparison.Ordinal), "Exterior stockyard panel must show the actual gold count and capacity."));
				Game.Renderer.SaveScreenshot(Path.Combine(output,"stock-or.png"));
			}
			if(priestAt>0&&tick==priestAt+750)interrupted=soldier.Trait<MedievalPatient>().Church==null;
			cartGold |= cart.Trait<MedievalStockpile>().Stock.Amount(4)>0;
			PreviewOre(w);
			if(priestAt>0&&tick>priestAt+1000&&cartGold||tick>=6500){if(!chart)Game.Renderer.SaveScreenshot(Path.Combine(output,"incomplete.png"));Finish(w);}
		}
	}
}
