using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Orders;
using OpenRA.Primitives;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Medieval
{
	public class MedievalMessengerValidationInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) => new MedievalMessengerValidation();
	}
	// Three player identities in the real engine. Remote commands are round-tripped
	// through the network format and resolved by the recipient's Player actor.
	public sealed class MedievalMessengerValidation : IWorldLoaded, ITick
	{
		WorldRenderer renderer;
		Actor home, target, thirdHome, courier, attacker, depot, market;
		MedievalMessenger lastMail;
		Player rival, third;
		int tick, stage, until, initialFood;
		bool finished, recipientKill, interception, struckInTransit, interceptionHalted;
		Order stale;
		readonly List<string> errors = [];
		string output;
		static CPos Cell(int x, int y) => new(x + 32, y - 32);
		void Check(bool value, string error) { if (!value && !errors.Contains(error)) errors.Add(error); }
		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{ renderer = wr; output = Path.Combine(Platform.EngineDir, "analysis", "medieval", "messenger-preview"); Directory.CreateDirectory(output); }
		Actor Spawn(World w, string type, int x, int y, Player owner = null) => w.CreateActor("md." + type, [new OwnerInit(owner ?? w.LocalPlayer), new LocationInit(Cell(x, y))]);
		void Select(World w, Actor a) => Sync.RunUnsynced(w, () => w.Selection.Combine(w, [a], false, true));
		void Button(World w, string id) => Sync.RunUnsynced(w, () =>
		{
			var b = Ui.Root.Get<ButtonWidget>(id); Check(!b.IsDisabled(), "Expected enabled button: " + id); b.OnClick();
		});
		void Remote(World w, Order order)
		{
			using var reader = new BinaryReader(new MemoryStream(order.Serialize()));
			var received = Order.Deserialize(w, reader);
			Check(received.TargetString == order.TargetString && received.ExtraData == order.ExtraData, "Network format must preserve text and revision.");
			((IResolveOrder)received.Subject.Trait<MedievalCorrespondence>()).ResolveOrder(received.Subject, received);
		}
		void Remote(World w, Player player, string command, string text = "", Actor destination = null) => Remote(w, MedievalMessenger.Command(player, courier, command, text, destination));
		void SendFromUI(World w, Actor destination, string body)
		{
			Select(w, courier);
			Sync.RunUnsynced(w, () =>
			{
				var input = Ui.Root.Get<TextFieldWidget>("MD_MAIL_INPUT"); input.Text = "  ";
				Check(Ui.Root.Get<ButtonWidget>("MD_MAIL_SEND").IsDisabled(), "Blank letters must not be sent.");
				input.Text = body; Button(w, "MD_MAIL_SEND");
				var generator = w.OrderGenerator as MedievalMessengerTargetGenerator;
				Check(generator != null, "Send must open a barracks targeting tool."); if (generator == null) return;
				var pixel = renderer.Viewport.WorldToViewPx(renderer.ScreenPxPosition(destination.CenterPosition));
				var orders = ((IOrderGenerator)generator).Order(w, destination.Location, pixel,
					new MouseInput(MouseInputEvent.Down, ((OrderGenerator)generator).ActionButton, pixel, int2.Zero, Modifiers.None, 1)).ToArray();
				Check(orders.Length == 1 && orders[0].Subject == w.LocalPlayer.PlayerActor && orders[0].ExtraActors.Single() == destination, "Targeting must send one authenticated player order with the selected barracks.");
				foreach (var order in orders) w.IssueOrder(order);
			});
		}
		void Setup(World w)
		{
			rival = w.Players.Single(p => p.InternalName == "Rival"); third = w.Players.Single(p => p.InternalName == "Third");
			w.LocalPlayer.EnemyPlayersMask = rival.PlayerMask.Union(third.PlayerMask);
			depot = Spawn(w, "warehouse", 20, 29); depot.Trait<MedievalStockpile>().Stock.Add(1, 300);
			market = Spawn(w, "market", 30, 33); market.Trait<MedievalStockpile>().Stock.Add(1, 200);
			initialFood = depot.Trait<MedievalStockpile>().Stock.Amount(1) + market.Trait<MedievalStockpile>().Stock.Amount(1);
			home = Spawn(w, "barracks", 24, 28); target = Spawn(w, "barracks", 40, 28, rival); thirdHome = Spawn(w, "barracks", 44, 39, third);
			stage = 1; until = tick + 10;
		}
		void Finish(World w)
		{
			Check(stage == 21, "All correspondence scenarios must complete.");
			File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = errors.Count == 0, tick, stage, recipientKill, interception, struckInTransit, errors }, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("MEDIEVAL MESSENGER: " + (errors.Count == 0 ? "PASS" : string.Join("; ", errors)));
			finished = true; Game.RunAfterDelay(1000, Game.Exit);
		}
		void ITick.Tick(Actor self)
		{
			var w = self.World; if (finished || w.LocalPlayer == null) return; tick++;
			renderer.Viewport.Center(w.Map.CenterOfCell(Cell(32, 29)));
			if (tick == 1) { w.AddFrameEndTask(Setup); return; }
			if (stage == 1 && tick >= until) { Select(w, home); Button(w, "MD_BARRACKS_STAFF"); stage = 2; }
			else if (stage == 2 && home.Trait<MedievalMilitaryWorkshop>().Staff(home) != null)
			{ Button(w, "MD_BARRACKS_MESSENGER"); stage = 3; }
			else if (stage == 3 && w.ActorsWithTrait<MedievalMessenger>().Any())
			{
				courier = w.ActorsWithTrait<MedievalMessenger>().First().Actor;
				Check(MedievalSettlement.Population(w.LocalPlayer) == 2 && MedievalFoodEconomy.Demand(w.LocalPlayer) == 4, "Recruiter and messenger must both consume housing and food.");
				Check(depot.Trait<MedievalStockpile>().Stock.Amount(1) + market.Trait<MedievalStockpile>().Stock.Amount(1)
					+ w.LocalPlayer.PlayerActor.Trait<MedievalFoodEconomy>().TotalConsumed == initialFood - 30, "Recruitment must deduct exactly 10 + 20 food.");
				Check(courier.Trait<MedievalMessenger>().CanCompose(courier, w.LocalPlayer), "New messenger must spawn beside its barracks.");
				Check(!((IRenderAnnotations)courier.Trait<MedievalMessenger>()).RenderAnnotations(courier, renderer).Any(), "Empty messenger must not show an envelope.");
				SendFromUI(w, target, "Trêve à midi ? Rendez-vous près du moulin."); stage = 4; until = tick + 40;
			}
			var mail = courier != null && !courier.Disposed ? lastMail = courier.Trait<MedievalMessenger>() : lastMail;
			// A courier halted halfway along the road is still vulnerable, including
			// to a third player. Ordinary arrows may miss a continuously moving target.
			if (stage == 17 && courier.IsInWorld && !courier.IsDead && mail.Journey == MessengerJourney.Outbound
				&& courier.Location.X >= 62 && !interceptionHalted)
			{
				w.IssueOrder(new Order("Stop", courier, false)); interceptionHalted = true;
			}
			if (stage == 17 && courier.IsInWorld && !courier.IsDead && mail.Journey == MessengerJourney.Outbound
				&& courier.Trait<Health>().HP < courier.Trait<Health>().MaxHP) struckInTransit = true;
			if (stage == 4 && tick >= until)
			{
				Check(mail.Journey == MessengerJourney.Outbound && mail.Letter.Present, "Send must start physical travel.");
				Remote(w, rival, "MedievalReadLetter"); Remote(w, third, "MedievalReturnMessenger");
				Check(!mail.Letter.Opened && mail.Journey == MessengerJourney.Outbound, "No remote reading or third-party refusal in transit.");
				Check(((IRenderAnnotations)mail).RenderAnnotations(courier, renderer).Single() is MessengerEnvelopeRenderable, "Letter must show exactly one envelope.");
				w.IssueOrder(new Order("Stop", courier, false)); stage = 5; until = tick + 30;
			}
			else if (stage == 5 && tick >= until)
			{
				Check(mail.Paused && mail.Letter.Present, "Stopping must pause travel and preserve the letter.");
				Button(w, "MD_MAIL_RESUME"); stage = 6;
			}
			else if (stage == 6 && mail.Journey == MessengerJourney.Waiting)
			{
				Check(MedievalAccess.Adjacent(courier, target) && mail.VisibleText(courier, rival) == "", "Delivery must be adjacent and sealed.");
				Remote(w, third, "MedievalReadLetter"); Remote(w, third, "MedievalReturnMessenger"); Remote(w, rival, "MedievalReplyLetter", "Too early");
				Check(mail.Journey == MessengerJourney.Waiting && !mail.Letter.Opened, "Only the recipient can act, and a reply requires opening.");
				Remote(w, rival, "MedievalReadLetter");
				Check(mail.VisibleText(courier, rival) == "Trêve à midi ? Rendez-vous près du moulin.", "Recipient must read original Unicode text.");
				stale = MedievalMessenger.Command(rival, courier, "MedievalReturnMessenger");
				Remote(w, rival, "MedievalReplyLetter", "D'accord. Nous acceptons la trêve !");
				Check(mail.VisibleText(courier, w.LocalPlayer) == "" && !mail.Letter.Opened, "Reply must remain sealed until returned."); stage = 7;
			}
			else if (stage == 7 && mail.Journey == MessengerJourney.Returned)
			{
				Check(courier.Owner == w.LocalPlayer && mail.Origin == home && MedievalAccess.Adjacent(courier, home), "Reply must return the same messenger to the original barracks.");
				Check(Ui.Root.Get<ButtonWidget>("MD_MAIL_ALERT").IsVisible(), "Unread return must trigger the inbox alert.");
				Button(w, "MD_MAIL_READ"); stage = 8; until = tick + 25;
			}
			else if (stage == 8 && tick >= until)
			{
				Check(Ui.Root.Get<LabelWidget>("MD_MAIL_BODY").GetText() == "D'accord. Nous acceptons la trêve !", "Returning reply must appear in the local message panel.");
				Game.Renderer.SaveScreenshot(Path.Combine(output, "reply.png"));
				SendFromUI(w, target, "Cette lettre sera refusée sans lecture."); stage = 9;
			}
			else if (stage == 9 && mail.Journey == MessengerJourney.Waiting)
			{
				Remote(w, stale); Check(mail.Journey == MessengerJourney.Waiting, "Old response order must not affect a new trip.");
				Remote(w, rival, "MedievalReturnMessenger"); Check(mail.Refused && !mail.Letter.Opened, "Refusal must preserve an unopened letter."); stage = 10;
			}
			else if (stage == 10 && mail.Journey == MessengerJourney.Returned)
			{
				Check(mail.Refused && mail.Letter.TextFor(rival.InternalName, true) == "", "Refused contents must remain unavailable to recipient.");
				SendFromUI(w, thirdHome, "Bonjour au troisième bourg."); stage = 11;
			}
			else if (stage == 11 && mail.Journey == MessengerJourney.Waiting)
			{
				Remote(w, rival, "MedievalReadLetter"); Check(!mail.Letter.Opened, "Previous recipient cannot read mail for a different player.");
				Remote(w, third, "MedievalReadLetter"); Check(mail.Letter.Opened, "Third player must be able to receive its own correspondence.");
				Remote(w, third, "MedievalReturnMessenger"); stage = 12;
			}
			else if (stage == 12 && mail.Journey == MessengerJourney.Returned)
			{ Check(!mail.Refused, "Read-and-return must differ from refusing unopened mail."); SendFromUI(w, target, "Dernière offre."); stage = 13; }
			else if (stage == 13 && mail.Journey == MessengerJourney.Waiting)
			{
				w.AddFrameEndTask(_ => { attacker = Spawn(w, "messengerattacker", 38, 28, rival); w.IssueOrder(new Order("Attack", attacker, Target.FromActor(courier), false)); }); stage = 14;
			}
			else if (stage == 14 && (courier.IsDead || !courier.IsInWorld))
			{
				recipientKill = true; until = tick + 10; stage = 15;
			}
			else if (stage == 15 && tick >= until)
			{
				Check(!mail.Letter.Present, "Killing messenger must destroy the letter.");
				w.AddFrameEndTask(_ => { attacker.Dispose(); courier = Spawn(w, "messenger", 25, 31); }); stage = 16; until = tick + 10;
			}
			else if (stage == 16 && tick >= until)
			{
				SendFromUI(w, target, "Courrier intercepté.");
				w.AddFrameEndTask(_ => { attacker = Spawn(w, "messengerinterceptor", 32, 27, third); w.IssueOrder(new Order("Attack", attacker, Target.FromActor(courier), false)); }); stage = 17;
			}
			else if (stage == 17 && (courier.IsDead || !courier.IsInWorld))
			{
				interception = true;
				Check(struckInTransit, "Third player must be able to hit the messenger during its journey.");
				Check(!MedievalAccess.Adjacent(courier, target), "Third-player interception must happen on the road, before reaching the barracks.");
				w.AddFrameEndTask(_ => { attacker.Dispose(); courier = Spawn(w, "messenger", 25, 31); }); stage = 18; until = tick + 10;
			}
			else if (stage == 18 && tick >= until)
			{
				if (mail.Journey == MessengerJourney.Idle)
				{ Remote(w, w.LocalPlayer, "MedievalSendLetter", "Caserne disparue", target); w.AddFrameEndTask(_ => target.Dispose()); until = tick + 30; }
				else if (mail.Journey == MessengerJourney.Returned)
				{
					Check(mail.CanCompose(courier, w.LocalPlayer), "Courier must be able to compose after a lost destination.");
					Check(mail.CanTarget(courier, w.LocalPlayer, thirdHome), "Third barracks must remain targetable after an aborted trip.");
					Remote(w, w.LocalPlayer, "MedievalSendLetter", "Retour impossible", thirdHome);
					Check(mail.Journey == MessengerJourney.Outbound, "Sending after an aborted trip must depart again.");
					w.AddFrameEndTask(_ => home.Dispose()); stage = 19; until = tick + 30;
				}
			}
			else if (stage == 19 && tick >= until)
			{
				Check(!home.IsInWorld, "Origin must be removed before recall.");
				Remote(w, w.LocalPlayer, "MedievalRecallMessenger"); stage = 20; until = tick + 20;
			}
			else if (stage == 20 && tick >= until)
			{
				if (mail.Journey == MessengerJourney.Stranded)
				{
					Check(mail.Letter.Present && !mail.Paused, "Lost origin must leave the letter on a surviving, controllable messenger."); stage = 21; Finish(w);
				}
				else { errors.Add("Origin-loss outcome: " + mail.Journey); Finish(w); }
			}
			if (tick > 18000) { errors.Add($"Timeout: stage {stage}, courier {mail?.Journey}, status {mail?.Status}."); Finish(w); }
		}
	}
}
