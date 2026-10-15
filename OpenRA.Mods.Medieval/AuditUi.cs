using System.Linq;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Widgets;

namespace OpenRA.Mods.Medieval
{
	// Native validation helpers follow the same contextual tabs as a player.
	static class MedievalAuditUi
	{
		public static bool OpenContext(World world, Actor actor, string name)
		{
			var opened = false;
			Sync.RunUnsynced(world, () =>
			{
				world.Selection.Combine(world, [actor], false, true);
				var root = Ui.Root.Get("MD_CONTEXT_TABS").Parent;
				root.LogicObjects.OfType<MedievalConstructionMenuLogic>().Single().Tick();
				var hud = root.LogicObjects.OfType<MedievalHudLogic>().Single();
				hud.Tick();
				for (var page = 0; page < 8; page++)
				{
					var tab = Enumerable.Range(0, 3).Select(i => Ui.Root.Get<ButtonWidget>("MD_CONTEXT_TAB_" + i))
						.FirstOrDefault(t => t.IsVisible() && t.GetText() == name);
					if (tab != null) { tab.OnClick(); hud.Tick(); opened = true; return; }
					var next = Ui.Root.Get<ButtonWidget>("MD_CONTEXT_NEXT");
					if (!next.IsVisible() || next.IsDisabled()) return;
					next.OnClick();
				}
			});
			return opened;
		}
	}
}
