using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace WorstHotel.Tests
{
    public sealed partial class Phase1PlayModeTests
    {
        IEnumerator ChooseOperationsOption(string startsWith)
        {
            var ui = ManagementUI.Instance;
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That(ui.IsOperationsOpen, Is.True);
            Assert.That(ui.OperationsOptionTitles.Any(title => title.StartsWith(startsWith, StringComparison.Ordinal)), Is.True,
                "Missing operations choice: " + startsWith);
            int attempts = 0;
            while (ui.FocusedOperationsOption == null || !ui.FocusedOperationsOption.StartsWith(startsWith, StringComparison.Ordinal))
            {
                Assert.That(attempts++, Is.LessThan(24), "No enabled controller path to: " + startsWith);
                InputSystem.QueueStateEvent(padA, new GamepadState().WithButton(GamepadButton.DpadDown));
                yield return null; yield return null;
                QueueUse(padA, false); yield return null; yield return null;
            }
            QueueUse(padA, true); yield return null; yield return null;
            QueueUse(padA, false); yield return null; yield return null; yield return null;
        }

        [UnityTest, Category("ContinuousOperations"), Category("AutomaticSales")]
        public IEnumerator PhysicalReceptionControllerChangesSalesThenReassignsAutomaticStayAndHandsOverActualKey()
        {
            var session = GameSession.Instance; var model = session.Simulation;
            Assert.That(session.config.automaticBookings && model.AutomaticBookingsEnabled, Is.True);
            // Labelled supply setup leaves one controller-opened room as the only offer.
            // Demand, enquiry times and physical guest/key routes remain production behavior.
            foreach (var row in model.RoomSalesPolicies)
                Assert.That(model.SetRoomSalesPolicy(0, row.RoomId, false, 180, row.Revision).Success, Is.True);
            var terminal = Object.FindAnyObjectByType<ReceptionTerminal>();
            yield return FaceStation(bootstrap.Players[0], padA, terminal, terminal.transform.position + Vector3.up * .35f);
            QueueUse(padA, true);
            yield return WaitForCondition(() => ManagementUI.Instance.IsOperationsOpen, 2, "Real reception interaction must open live hotel operations.");
            QueueUse(padA, false); yield return null; yield return null;
            var ui = ManagementUI.Instance;
            Assert.That(bootstrap.Players[1].Interactor.CanAct, Is.True);
            yield return ChooseOperationsOption("Room sales / rates");
            yield return ChooseOperationsOption("Room 101 · ");
            Assert.That(ui.DisplayedSalesRoomId, Is.EqualTo(101));
            yield return ChooseOperationsOption("Open to new sales");
            yield return ChooseOperationsOption("Apply sales policy");
            Assert.That(model.RoomSalesPolicies.Single(row => row.RoomId == 101).OpenForSale, Is.True);
            Assert.That(model.Reservations, Is.Empty, "Applying a policy is not an immediate acceptance command.");
            float next = model.NextSalesDecisionAt;
            if (next - model.Elapsed > .5f) session.AdvanceTime(next - model.Elapsed - .5f);
            yield return WaitForCondition(() => model.Reservations.Count == 1, 3,
                "The next real Update/enquiry boundary must create a reservation without an Accept button.");
            var reservation = model.Reservations.Single();
            Assert.That(reservation.IsAutomatic && reservation.ActorId == -1, Is.True);
            Assert.That(model.Elapsed, Is.GreaterThanOrEqualTo(next));
            Assert.That(model.Guests, Is.Empty); Assert.That(model.PeriodCheckoutIncome, Is.Zero);
            int price = reservation.Price;
            int draftRevision = ui.DisplayedSalesPolicyRevision;
            int draftPrice = ui.DisplayedSalesPrice;
            yield return ChooseOperationsOption("+ $");
            Assert.That(ui.DisplayedSalesPrice, Is.EqualTo(draftPrice + session.Economy.PriceStep));
            Assert.That(model.SetRoomSalesPolicy(1, 101, true, 220, draftRevision).Success, Is.True);
            yield return null; yield return null;
            Assert.That(session.SetRoomSalesPolicy(0, 101, true, ui.DisplayedSalesPrice, draftRevision).Success, Is.False);
            var policy = model.RoomSalesPolicies.Single(row => row.RoomId == 101);
            Assert.That(policy.Price, Is.EqualTo(220));
            Assert.That(policy.Revision, Is.EqualTo(draftRevision + 1), "A stale controller draft cannot overwrite the partner's edit.");
            Assert.That(ui.DisplayedSalesPolicyRevision, Is.EqualTo(draftRevision));
            yield return ChooseOperationsOption("Refresh room policy");
            yield return ChooseOperationsOption("+ $");
            yield return ChooseOperationsOption("Apply sales policy");
            Assert.That(policy.Price, Is.EqualTo(220 + session.Economy.PriceStep), "A fresh controller draft changes the advertised rate.");
            yield return ChooseOperationsOption("Close to new sales");
            yield return ChooseOperationsOption("Apply sales policy");
            Assert.That(policy.OpenForSale, Is.False);
            Assert.That(reservation.Price, Is.EqualTo(price));
            Assert.That(reservation.Status, Is.EqualTo(ReservationStatus.Reserved));
            yield return ChooseOperationsOption("Back to room sales");
            yield return ChooseOperationsOption("Back to operations");
            yield return ChooseOperationsOption("Today's bookings");
            yield return ChooseOperationsOption(reservation.Offer.Application.GuestName + " · ");
            Assert.That(ui.OperationsOptionTitles.Any(title => title.StartsWith("Accept booking") || title.StartsWith("Update agreed price")), Is.False);
            int oldRevision = reservation.Revision;
            yield return ChooseOperationsOption("Room 106 · ");
            yield return ChooseOperationsOption("Reassign booking · room 106");
            Assert.That(reservation.RoomId, Is.EqualTo(106)); Assert.That(reservation.Price, Is.EqualTo(price));
            Assert.That(reservation.Revision, Is.EqualTo(oldRevision + 1));
            Assert.That(session.CancelBooking(1, reservation.Id, oldRevision).Success, Is.False);
            Assert.That(model.Guests, Is.Empty);
            Assert.That(session.Rooms.All(room => !room.Occupied && room.ReservedGuestId == null), Is.True);
            ui.Close();
            session.AdvanceTime(reservation.Offer.ArrivalAt - model.Elapsed + .1f);
            var guest = model.Guests.Single();
            yield return WaitForCondition(() => guest.Agent.State == GuestAgentState.WaitingForCheckIn, 25,
                "The automatically booked guest must physically walk to reception.");
            var interaction = Object.FindObjectsByType<GuestReceptionInteraction>(FindObjectsSortMode.None).Single(item => item.GuestId == guest.GuestId);
            yield return TakeRoomKeyFromActualRack(0, 106);
            yield return CarryRackKeyToReceptionGuest(0, 106, interaction);
            yield return GiveHeldKeyByInstantUse(0, guest, 106);
            yield return WaitForCondition(() => guest.Agent.HasReachedRoom, 55,
                "The reassigned guest must use the real door/room route after receiving key 106.");
            Assert.That(guest.RoomId, Is.EqualTo(106));
            Assert.That(model.Keys.Find(106).GuestId, Is.EqualTo(guest.GuestId));
            Assert.That(model.Keys.Find(101).Location, Is.EqualTo(RoomKeyLocation.OnRack));
            Assert.That(session.Rooms.Single(room => room.Profile.Id == 106).GuestId, Is.EqualTo(guest.GuestId));
            Assert.That(session.Simulation, Is.SameAs(model)); Assert.That(session.Phase, Is.EqualTo(DayPhase.Service));
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTest, Category("ContinuousOperations")]
        public IEnumerator ControllerReadsPublishedReportWithoutPostingCostsAgainOrClosingHotel()
        {
            StartContinuousCalendarFixture(); // Labelled 24-second day isolates the real report/menu pipeline.
            var session = GameSession.Instance;
            yield return WaitForCondition(() => session.Report != null, 5, "The running hotel must publish an accounting report.");
            var report = session.Report;
            int cash = session.Simulation.Economy.Cash;
            float before = session.Simulation.Elapsed;
            ManagementUI.Instance.Open(0);
            yield return ChooseOperationsOption("Daily reports");
            yield return ChooseOperationsOption("Operating report 1");
            yield return new WaitForSecondsRealtime(.3f);
            Assert.That(session.Report, Is.SameAs(report));
            Assert.That(session.Reports.Count, Is.EqualTo(1));
            Assert.That(session.Simulation.Economy.Cash, Is.EqualTo(cash), "Reading the accounts cannot settle the same costs again.");
            Assert.That(session.Simulation.Elapsed, Is.GreaterThan(before));
            Assert.That(session.Phase, Is.EqualTo(DayPhase.Service));
            Assert.That(ManagementUI.Instance.IsOperationsOpen, Is.True);
            ManagementUI.Instance.Close();
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest, Category("ContinuousOperations"), Category("AutomaticSales")]
        public IEnumerator ContinuousSessionMirrorPreservesCalendarBookingsAndOpenMenuAcrossMidnight()
        {
            var session = GameSession.Instance;
            var source = session.Simulation;
            Assert.That(source.AutomaticBookingsEnabled, Is.True);
            session.AdvanceTime(source.Calendar.At(1, 19.6f) - source.Elapsed);
            var booked = source.Reservations.First(item => item.Offer.ArrivalDay == 2 && item.Active);
            var offer = booked.Offer;
            int price = booked.Price;
            var first = JsonUtility.FromJson<LanHotelFrame>(JsonUtility.ToJson(session.CaptureLanFrame(701, 1)));
            session.AdvanceTime(source.Calendar.At(2, 8.1f) - source.Elapsed);
            Assert.That(session.Day, Is.EqualTo(2));
            var second = JsonUtility.FromJson<LanHotelFrame>(JsonUtility.ToJson(session.CaptureLanFrame(701, 2)));
            session.PrepareLanReplica();
            var initial = session.ApplyLanFrame(first);
            Assert.That(initial.Success, Is.True, initial.Message);
            var mirror = session.Simulation;
            Assert.That(mirror.ContinuousOperations && mirror.IsReadOnlyMirror, Is.True);
            Assert.That(mirror.FindReservation(offer.Id).Price, Is.EqualTo(price));
            Assert.That(mirror.FindReservation(offer.Id).IsAutomatic, Is.True);
            ManagementUI.Instance.Open(1);
            Assert.That(ManagementUI.Instance.IsOperationsOpen, Is.True);
            float firstTime = mirror.Elapsed;
            yield return new WaitForSecondsRealtime(.35f);
            Assert.That(mirror.Elapsed, Is.EqualTo(firstTime), "Client updates cannot locally advance the host calendar.");
            var next = session.ApplyLanFrame(second);
            Assert.That(next.Success, Is.True, next.Message);
            Assert.That(session.Simulation, Is.SameAs(mirror), "Midnight cannot replace the replica hotel.");
            Assert.That(session.Day, Is.EqualTo(2));
            Assert.That(session.Phase, Is.EqualTo(DayPhase.Service));
            Assert.That(session.Reports.Count, Is.EqualTo(1));
            Assert.That(ManagementUI.Instance.IsOperationsOpen && ManagementUI.Instance.Owner == 1, Is.True,
                "A report or date boundary cannot reopen or steal an existing local ledger.");
            Assert.That(mirror.FindReservation(offer.Id).Status, Is.EqualTo(ReservationStatus.Reserved));
            Assert.That(session.CancelBooking(1, offer.Id, mirror.FindReservation(offer.Id).Revision).Success, Is.False,
                "A read-only replica cannot directly mutate host reservations.");
            Assert.That(session.ApplyLanFrame(first).Success, Is.False, "Older snapshots remain rejected across midnight.");
            ManagementUI.Instance.Close();
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest, Category("ContinuousOperations"), Category("AutomaticSales")]
        public IEnumerator ProductionClientCanMirrorAnExplicitLegacyFixtureWithoutActivatingItsCalendar()
        {
            var session = GameSession.Instance;
            var production = session.config;
            Assert.That(production.continuousOperations, Is.True);
            // Labelled compatibility fixture: old regression hosts deliberately select legacy mode.
            waitScenarioSessionConfig = Object.Instantiate(production);
            waitScenarioSessionConfig.continuousOperations = false;
            session.config = waitScenarioSessionConfig;
            session.NewGame();
            var frame = JsonUtility.FromJson<LanHotelFrame>(JsonUtility.ToJson(session.CaptureLanFrame(702, 1)));
            Assert.That(frame.model.HasOperations, Is.False);
            session.config = production;
            session.PrepareLanReplica();
            var applied = session.ApplyLanFrame(frame);
            Assert.That(applied.Success, Is.True, applied.Message);
            Assert.That(session.Simulation.ContinuousOperations, Is.False,
                "The host's explicit mode must take precedence over the client's production defaults.");
            Assert.That(session.Phase, Is.EqualTo(DayPhase.Planning));
            ManagementUI.Instance.Close();
            yield return null;
            LogAssert.NoUnexpectedReceived();
        }
    }
}
