using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Primitives;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Medieval
{
	public class MedievalTextileValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalTextileValidation();
	}
	public sealed class MedievalTextileValidation : IWorldLoaded, ITick
	{
		WorldRenderer renderer;
		string output;
		Actor depot, loom, barracks, banner, shepherd, cancelled;
		int tick, completedAt, consumed, cancelAt, stockBeforeCancel;
		bool done, blocked, woolCarried, clothCarried, builderCarried, recruiterCarried, outsideHandling, resumed;
		readonly List<string> errors = [];
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void Check(bool value, string message) { if (!value && !errors.Contains(message)) errors.Add(message); }
		Actor Spawn(World w, string name, int x, int y, bool site = false)
		{
			var init = new TypeDictionary { new OwnerInit(w.LocalPlayer), new LocationInit(Cell(x, y)) };
			if (site) init.Add(new MedievalConstructionInit());
			return w.CreateActor("md." + name, init);
		}
		int Cloth(World w) => w.Actors.Sum(a => (a.TraitOrDefault<MedievalCampStore>()?.Stock.Amount(2) ?? 0)
			+ (a.TraitOrDefault<MedievalBuilding>()?.CampMaterials.Amount(2) ?? 0)
			+ (a.TraitOrDefault<MedievalWorker>() is { CampCargo: true, Resource: 2 } worker ? worker.Cargo : 0)) + consumed;
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "textile", "native"); Directory.CreateDirectory(output);
		}
		void Setup(World w)
		{
			foreach (var a in w.Actors.Where(a => a.TraitOrDefault<MedievalBuilding>() != null || a.TraitOrDefault<MedievalSource>() != null || a.TraitOrDefault<Mobile>() != null).ToArray()) a.Dispose();
			depot = Spawn(w, "warehouse", 22, 33); depot.Trait<MedievalStockpile>().Stock.Add(0, 200); depot.Trait<MedievalStockpile>().Stock.Add(1, 250);
			Spawn(w, "market", 22, 28).Trait<MedievalStockpile>().Stock.Add(1, 200);
			Spawn(w, "house", 26, 23); Spawn(w, "house", 29, 23);
			Spawn(w, "sheepfold", 28, 35);
			var pen = Spawn(w, "sheeppasture.4x4", 33, 35);
			for (var i = 0; i < 2; i++) Check(pen.Trait<MedievalLivestock>().Enter(Spawn(w, "sheep", 34 + i, 36)), "The pen needs actual adult sheep.");
			loom = Spawn(w, "tentweaver", 28, 28); loom.Trait<MedievalTentWorkshop>().Target = 0; loom.Trait<MedievalTentWorkshop>().ClothTarget = 0;
			shepherd = Spawn(w, "shepherd", 31, 35); Spawn(w, "weaver", 27, 29);
			Spawn(w, "hauler", 26, 33); Spawn(w, "hauler", 26, 34);
			barracks = Spawn(w, "barracks", 33, 27); var recruiter = Spawn(w, "recruiter", 32, 28);
			Check(barracks.Trait<MedievalMilitaryWorkshop>().AssignStaff(barracks, recruiter), "Assign a real recruiter.");
			banner = Spawn(w, "banner", 30, 32, true); Spawn(w, "builder", 30, 33); Spawn(w, "builder", 31, 33);
			Check(barracks.Trait<MedievalMilitaryWorkshop>().Request(barracks, "md.standardbearer"), "Queue a standard bearer.");
			Sync.RunUnsynced(w, () => { renderer.Viewport.Center(w.Map.CenterOfCell(Cell(28, 31))); renderer.Viewport.AdjustZoom((float)Math.Log(1.2f / renderer.Viewport.Zoom)); });
		}
		void Finish(World w)
		{
			Check(blocked && completedAt > 0, "Both a flag and its bearer must wait for real textile deliveries, then complete.");
			Check(woolCarried && clothCarried && builderCarried && recruiterCarried, "Shepherd, carrier, builder and recruiter must physically carry wool/cloth.");
			Check(outsideHandling, "Textiles must use real outdoor piles for pickup and delivery.");
			Check(resumed, "The workshop must replenish used cloth to its target.");
			Check(cancelAt > 0 && !cancelled.IsInWorld && Cloth(w) == stockBeforeCancel, "Cancelling a delivered flag site must conserve its two cloth units.");
			Check(banner.Trait<MedievalBuilding>().CampMaterials.Amount(2) == 2, "Exactly two cloth must be incorporated in a flag.");
			Check(w.Actors.Count(a => a.Owner == w.LocalPlayer && a.Info.Name == "md.standardbearer") == 1, "One order creates exactly one bearer.");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, errors, tick, blocked, completedAt, woolCarried, clothCarried, builderCarried, recruiterCarried, outsideHandling, resumed, cancelAt, cloth = Cloth(w), stockBeforeCancel }, new JsonSerializerOptions { WriteIndented = true }));
			Game.Renderer.SaveScreenshot(Path.Combine(output, "textile-chain.png"));
			Console.WriteLine("MEDIEVAL TEXTILE: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors))); done = true; Game.RunAfterDelay(700, Game.Exit);
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (done || w.LocalPlayer == null) return; tick++;
			if (tick == 1) { w.LocalPlayer.Shroud.Disabled = true; w.AddFrameEndTask(Setup); return; }
			if (tick < 10) return;
			foreach (var pair in w.ActorsWithTrait<MedievalWorker>())
			{
				var worker = pair.Trait;
				woolCarried |= worker.CampCargo && worker.Resource == 0 && worker.Cargo > 0;
				if (worker.CampCargo && worker.Resource == 2)
				{
					clothCarried |= worker.Cargo > 0;
					builderCarried |= worker.Cargo > 0 && worker.Info.Profession == "builder";
					recruiterCarried |= worker.Cargo > 0 && worker.Info.Profession == "recruiter";
					outsideHandling |= worker.HandlingStock && worker.HandlingZone > 0;
				}
			}
			if (tick == 20) w.IssueOrder(new Order("MedievalPlaceStockyard", depot, Target.FromCell(w, Cell(18, 33)), false) { ExtraLocation = Cell(20, 34), TargetString = "Camp", ExtraData = 0 });
			if (tick == 180)
			{
				blocked = !banner.Trait<MedievalBuilding>().Complete && !barracks.Trait<MedievalMilitaryWorkshop>().Ready(barracks) && barracks.Trait<MedievalMilitaryWorkshop>().Progress == 0;
				Check(Cloth(w) == 0, "No free cloth may exist before weaving.");
				Sync.RunUnsynced(w, () => { w.Selection.Combine(w, [loom], false, true); Ui.Root.Get<ButtonWidget>("MD_CAMP_OPEN").OnClick(); Ui.Root.Get<ButtonWidget>("MD_CAMP_CLOTH_MORE").OnClick(); });
			}
			if (tick == 185)
			{
				Check(loom.Trait<MedievalTentWorkshop>().ClothTarget == 1, "The UI must send a synchronized cloth target order.");
				w.IssueOrder(new Order("MedievalClothTarget", loom, false) { ExtraData = 6 });
			}
			if (completedAt == 0 && banner.Trait<MedievalBuilding>().Complete && w.Actors.Any(a => a.Info.Name == "md.standardbearer"))
			{
				completedAt = tick; consumed = 2;
				Check(barracks.Trait<MedievalCampStore>().Stock.Amount(2) == 0, "Recruitment must consume its two delivered cloth exactly once.");
			}
			if (completedAt > 0 && cancelAt == 0 && MedievalCampStore.Amount(w.LocalPlayer, 2) >= 6)
			{
				resumed = true; stockBeforeCancel = Cloth(w); cancelAt = tick;
				((IResolveOrder)loom.Trait<MedievalTentWorkshop>()).ResolveOrder(loom, new Order("MedievalClothTarget", loom, false) { ExtraData = 0 });
				w.AddFrameEndTask(_ =>
				{
					cancelled = Spawn(w, "banner", 30, 31, true);
					// Isolate cancellation from construction work, retaining normal delivery.
					cancelled.Trait<MedievalBuilding>().Materials.ReserveIn(0, cancelled.Trait<MedievalBuilding>().Materials.Capacity(0));
				});
			}
			if (cancelled is { IsInWorld: true } && cancelled.Trait<MedievalBuilding>().CampMaterials.Amount(2) == 2)
				w.IssueOrder(new Order("MedievalCancel", w.LocalPlayer.PlayerActor, Target.FromActor(cancelled), false));
			if (cancelAt > 0 && tick > cancelAt + 350 && !cancelled.IsInWorld || tick >= 11000) Finish(w);
		}
	}
}
