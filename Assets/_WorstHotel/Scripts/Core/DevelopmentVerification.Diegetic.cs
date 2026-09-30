#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem.LowLevel;

namespace WorstHotel
{
    public sealed partial class DevelopmentVerification
    {
        // Opt-in Windows presentation capture using the existing runner, not a human-play claim.
        IEnumerator VerifyDiegeticPresentation()
        {
            driveSpeed = 1;
            facts.Add("DIEGETIC PRESENTATION FIXTURE: explicit viewpoint placement, book opening and known guest setup. Physical input is checked separately by three focused PlayMode scenarios. Human walk-through remains required.");
            var ui = ManagementUI.Instance; var actor = coop.Players[0];
            ui.Close();
            Position(actor, new Vector3(-1.1f, .08f, -.6f), new Vector3(-5, 1.5f, 2.5f));
            yield return Capture("01-clean-reception", "Normal HUD / current reception layout");
            foreach (var kind in new[] { HotelBook.Reservations, HotelBook.Services, HotelBook.Accounts, HotelBook.Renovation, HotelBook.BoilerManual, HotelBook.Supplies })
            {
                yield return PresentBook(kind);
                yield return Capture("book-" + kind, "Physical " + kind + " / actual Windows IMGUI");
                if (kind == HotelBook.Accounts && session.Simulation.ContractEnabled)
                {
                    yield return ChoosePresentedBook("Contract terms");
                    Require(ui.DisplayedBookText.Contains("Pay the ownership contract at 22:00."), "contract terms navigation reaches the terms page");
                    yield return Capture("contract-terms", "Contract terms, growth and first deadline on the physical page");
                    yield return PressMenu(GamepadButton.DpadDown);
                    yield return PressMenu(GamepadButton.South);
                    Require(ui.DisplayedBookText.Contains("Cash on hand") && ui.DisplayedBookText.Contains("Laundry service"),
                        "terms resets focus so one down selects the cash page");
                    yield return Capture("cash-reserve", "Current income and expenses, cash, separate operating and contract deadlines");
                }
                if (kind == HotelBook.Reservations)
                {
                    for (int i = 0; i < 3; i++) yield return PressMenu(GamepadButton.DpadDown);
                    yield return PressMenu(GamepadButton.South);
                    yield return Capture("room-sales", "Reservation book sales page");
                }
                if (kind == HotelBook.BoilerManual)
                {
                    yield return PressMenu(GamepadButton.South);
                    yield return Capture("service-record", "Manual inspection and service page");
                }
                if (kind == HotelBook.Supplies)
                {
                    int cash = session.Simulation.Economy.Cash;
                    yield return ChoosePresentedBook("Order 3 bulbs");
                    Require(session.Simulation.Economy.Cash == cash - 45 && session.Simulation.PeriodBulbSpend == 45,
                        "physical supply book command debits the chosen pack once");
                    Require(session.Simulation.Services.BulbsAvailable == 3 && session.Simulation.BulbsInTransit == 3,
                        "paid bulbs remain in transit before the window");
                    yield return Capture("supplies-ordered", "Supply ledger after an actual paid order / future delivery");
                    yield return ChoosePresentedBook("Service terms");
                    yield return Capture("supplies-terms", "Laundry and bulb prices / delivery terms on physical paper");
                }
            }
            ui.Close(); yield return new WaitForSecondsRealtime(.35f);
            Position(actor, new Vector3(25, .08f, 3.5f), new Vector3(24.5f, 2.0f, 6.0f));
            yield return Capture("boiler-instruments", "Physical pressure / load / catch / labels");
            electricalPanel.cover.RequestOpen();
            yield return new WaitForSecondsRealtime(.8f);
            Position(actor, new Vector3(26.4f, .08f, 2.3f), new Vector3(28.9f, 1.65f, 2.3f));
            yield return Capture("electrical-instruments", "Local ammeters and actual A/B circuit labels");
            Require(session.Simulation.Electrical.ForceTrip("A").Success, "explicit branch A blackout fixture");
            Position(actor, new Vector3(0, .08f, 7.2f), new Vector3(0, 1.6f, 20));
            yield return Capture("blackout-A", "A off / B on: actual lights and surfaces");
            Require(session.Simulation.Electrical.ForceTrip("B").Success, "explicit branch B blackout fixture");
            yield return Capture("blackout-both", "Both branches off / no failure banner");

            session.NewGame(); guests.enabled = false;
            var model = session.Simulation;
            session.AdvanceTime(70);
            var reservation = model.Reservations.First();
            session.AdvanceTime(Mathf.Max(0, reservation.Offer.ArrivalAt - model.Elapsed + .2f));
            var guest = model.Guests.First(g => g.GuestId == reservation.Id);
            if (guest.Agent.State == GuestAgentState.Arriving) session.ReportGuestReachedReception(guest.GuestId);
            Require(model.Keys.PickUp(0, guest.RoomId).Success && model.CheckIn(0, guest.GuestId).Success, "labelled guest key fixture");
            Require(session.ReportGuestReachedRoom(guest.GuestId).Success, "labelled guest room fixture");
            Require(session.DebugSetMildCold(guest.GuestId).Success, "labelled cold fixture"); session.AdvanceTime(.3f);
            Require(session.DebugForceService(guest.GuestId, ServiceKind.ExtraBlanket).Success, "labelled private blanket request");
            var request = model.Services.Cases.First(c => c.GuestId == guest.GuestId && c.Kind == ServiceKind.ExtraBlanket && c.Active);
            Require(session.DebugBeginGuestContact(guest.GuestId, GuestContactChannel.Phone).Success, "labelled phone contact fixture");
            Require(model.SignalGuestResponseAnchorReached(guest.GuestId, request.Response.Id, request.Response.ActionVersion, GuestResponseAnchor.RoomPhone).Success, "labelled phone anchor fixture");
            var phone = FindAnyObjectByType<ReceptionPhoneInteraction>();
            var aim = phone.GetComponent<Collider>().bounds.center;
            Position(actor, new Vector3(aim.x, .08f, aim.z - 1.45f), aim); actor.enabled = true;
            // Keep the actual physical ray at the phone; OpenWakePhone grants no authority without it.
            yield return null; yield return null;
            actor.PlayerCamera.transform.LookAt(aim);
            yield return null;
            actor.enabled = false;
            actor.PlayerCamera.transform.LookAt(aim);
            yield return null;
            Require(actor.Interactor.Focused == phone, "actual phone ray in presentation fixture");
            phone.Interact(actor.Interactor);
            Require(ui.IsWakePhoneOpen && request.IsKnownToHotel, "first physical phone use answers immediately");
            yield return Capture("phone-answer", "Caller disclosed only after answering / physical lifted handset");
            Require(session.RespondToService(0, request.Id, true).Success, "explicit promise accepted");
            ui.Close();
            yield return PresentBook(HotelBook.Services);
            yield return Capture("known-service-note", "Accepted communicated promise in physical notes");
            ui.Close();
            if (model.ContractEnabled)
            {
                // Reuse the existing opt-in presentation pass. Explicit clock advances and a
                // purchase isolate UI states; this is not evidence of natural pacing or balance.
                session.NewGame(); model = session.Simulation;
                facts.Add("CONTRACT PRESENTATION FIXTURE: fresh production cash, bounded clock advances, actual insulation purchase; not a human playthrough.");
                AdvanceDiegeticContractTo(model.FirstContractAt + .25f);
                Require(!model.OwnershipLost && model.LastContractPayment?.PaidAmount == 350 && model.ContractSequence == 1,
                    "first contract paid at 22:00 after the first checkout window");
                Require(model.LastReport != null && model.LastReport.ContractPayment == null,
                    "the preceding 06:00 report contains no contract receipt");
                var receipt = model.LastContractPayment;
                yield return PresentBook(HotelBook.Accounts);
                Require(ui.DisplayedBookText.Contains("Cash after payment   $900"), "unread evening receipt opens immediately in accounts");
                yield return Capture("contract-paid-receipt", "22:00 contract receipt available before the next morning report");
                yield return ChoosePresentedBook("Current guest receipts");
                var unserved = model.CurrentReceipts.First();
                yield return ChoosePresentedBook(unserved.RoomId + " · ");
                Require(ui.DisplayedBookText.Contains("Agreed price   $" + unserved.AgreedPrice) &&
                    ui.DisplayedBookText.Contains("Net received   $0"), "unserved receipt preserves agreed price without imaginary cash");
                yield return Capture("guest-unserved-receipt", "Original booked amount, zero actual charge and existing unserved explanation");
                ui.Close();
                Require(model.PurchaseInsulation(0).Success, "actual $450 insulation purchase uses the $900 evening balance before the morning charge");
                AdvanceDiegeticContractTo(model.NextReportAt + .25f);
                Require(!model.OwnershipLost && model.LastReport.ContractPayment == receipt && model.Economy.Cash == 100,
                    "06:00 charges operations and reports the earlier payment without another contract attempt");
                yield return PresentBook(HotelBook.Accounts);
                yield return ChoosePresentedBook("Daily reports");
                yield return Capture("contract-report-summary", "Morning income, expenses and cash in the physical accounts book");
                yield return ChoosePresentedBook("Report contract receipt");
                Require(ui.DisplayedBookText.Contains("Cash after payment   $900") && ui.DisplayedBookText.Contains("Cash at report   $100"),
                    "report keeps the evening receipt balance separate from the morning balance");
                yield return Capture("contract-paid-report", "Morning report / earlier payment cash distinct from cash after purchase and operating charge");
                ui.Close();
                var morningReport = model.LastReport;
                AdvanceDiegeticContractTo(model.NextContractAt);
                Require(model.OwnershipLost && session.Phase == DayPhase.Results, "insufficient remaining cash ends ownership");
                Require(model.LastReport == morningReport && model.LastContractPayment.PaidAmount == 0 &&
                    session.Report == model.OwnershipLossReport, "notice uses the frozen failure period and failed 22:00 receipt");
                yield return Capture("ownership-revoked", "Official notice / exact cash, payment and shortfall / terminal state");
            }
            finalCash = model.Economy.Cash;
        }

        void AdvanceDiegeticContractTo(float target)
        {
            var model = session.Simulation;
            for (int i = 0; i < 8 && model.Running && model.Elapsed < target; i++)
            {
                float before = model.Elapsed;
                session.AdvanceTime(Mathf.Min(model.Operations.SecondsPerDay, Mathf.Max(1f / session.Settings.TickRate, target - before)));
                Require(model.Elapsed > before, "bounded presentation advance makes progress");
            }
            Require(model.Elapsed >= target, "contract presentation reaches its deadline in one-day advances");
        }

        IEnumerator ChoosePresentedBook(string title)
        {
            var ui = ManagementUI.Instance;
            Require(ui.BookOptionTitles.Any(value => value.StartsWith(title)), "physical book choice exists: " + title);
            for (int i = 0; i < 30 && !ui.FocusedBookOption.StartsWith(title); i++)
                yield return PressMenu(GamepadButton.DpadDown);
            Require(ui.FocusedBookOption.StartsWith(title), "physical book focus: " + title);
            yield return PressMenu(GamepadButton.South);
        }

        IEnumerator PresentBook(HotelBook kind)
        {
            var ui = ManagementUI.Instance; ui.Close();
            yield return new WaitForSecondsRealtime(.3f);
            var book = DiegeticBookInteraction.Find(kind);
            Require(book, "physical book exists: " + kind);
            var forward = book.transform.forward; forward.y = 0; forward.Normalize();
            var point = book.transform.position - forward * 1.45f; point.y = .08f;
            Position(coop.Players[0], point, book.transform.position);
            ui.OpenBook(0, kind);
            yield return new WaitForSecondsRealtime(.6f);
        }
    }
}
#endif
