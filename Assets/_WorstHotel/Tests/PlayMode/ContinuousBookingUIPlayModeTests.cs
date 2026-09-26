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

        [UnityTest, Category("ContinuousOperations")]
        public IEnumerator PhysicalReceptionControllerBooksEditsAndCancelsFutureStayWhileHotelKeepsRunning()
        {
            var session = GameSession.Instance;
            Assert.That(session.config.continuousOperations && session.Simulation.ContinuousOperations, Is.True);
            Assert.That(session.Phase, Is.EqualTo(DayPhase.Service));
            var simulation = session.Simulation;
            var offer = simulation.BookingOffers.Where(item => item.ArrivalDay == session.Day + 1).OrderBy(item => item.ArrivalAt).First();
            var terminal = Object.FindAnyObjectByType<ReceptionTerminal>();
            yield return FaceStation(bootstrap.Players[0], padA, terminal, terminal.transform.position + Vector3.up * .35f);
            QueueUse(padA, true);
            yield return WaitForCondition(() => ManagementUI.Instance.IsOperationsOpen, 2, "Real reception interaction must open live hotel operations.");
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That(bootstrap.Players[1].Interactor.CanAct, Is.True);
            float openedAt = simulation.Elapsed;
            yield return ChooseOperationsOption("Tomorrow's bookings");
            yield return ChooseOperationsOption(offer.Application.GuestName + " · ");
            yield return ChooseOperationsOption("Accept booking");
            var reservation = simulation.FindReservation(offer.Id);
            Assert.That(reservation, Is.Not.Null);
            Assert.That(reservation.Status, Is.EqualTo(ReservationStatus.Reserved));
            Assert.That(reservation.Offer.CheckoutAt, Is.GreaterThan(simulation.Calendar.At(offer.ArrivalDay + 1, 0)));
            Assert.That(simulation.Guests, Is.Empty, "Tomorrow's reservation cannot spawn today's guest.");
            Assert.That(session.Rooms.All(room => !room.Occupied && room.ReservedGuestId == null), Is.True,
                "A future booking cannot claim today's room/key.");
            int originalPrice = reservation.Price, originalRevision = reservation.Revision;
            yield return ChooseOperationsOption("+ $");
            yield return ChooseOperationsOption("Update agreed price");
            Assert.That(reservation.Price, Is.EqualTo(originalPrice + session.Economy.PriceStep));
            Assert.That(reservation.Revision, Is.EqualTo(originalRevision + 1));
            var stale = session.CancelBooking(1, reservation.Id, originalRevision);
            Assert.That(stale.Success, Is.False, "A stale partner decision must not undo a newer price agreement.");
            Assert.That(reservation.Status, Is.EqualTo(ReservationStatus.Reserved));
            yield return ChooseOperationsOption("Cancel reservation");
            Assert.That(reservation.Status, Is.EqualTo(ReservationStatus.Cancelled));
            Assert.That(session.Phase, Is.EqualTo(DayPhase.Service));
            Assert.That(session.PlanCommitted, Is.False);
            Assert.That(session.Simulation, Is.SameAs(simulation));
            Assert.That(simulation.Elapsed, Is.GreaterThan(openedAt));
            ManagementUI.Instance.Close();
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

        [UnityTest, Category("ContinuousOperations")]
        public IEnumerator ContinuousSessionMirrorPreservesCalendarBookingsAndOpenMenuAcrossMidnight()
        {
            var session = GameSession.Instance;
            var source = session.Simulation;
            var offer = source.BookingOffers.First(item => item.ArrivalDay == 2);
            Assert.That(session.AcceptBooking(0, offer.Id, 101, session.Economy.MinPrice).Success, Is.True);
            var first = JsonUtility.FromJson<LanHotelFrame>(JsonUtility.ToJson(session.CaptureLanFrame(701, 1)));
            session.AdvanceTime(session.config.hotelDaySeconds);
            Assert.That(session.Day, Is.EqualTo(2));
            var second = JsonUtility.FromJson<LanHotelFrame>(JsonUtility.ToJson(session.CaptureLanFrame(701, 2)));
            session.PrepareLanReplica();
            var initial = session.ApplyLanFrame(first);
            Assert.That(initial.Success, Is.True, initial.Message);
            var mirror = session.Simulation;
            Assert.That(mirror.ContinuousOperations && mirror.IsReadOnlyMirror, Is.True);
            Assert.That(mirror.FindReservation(offer.Id).Price, Is.EqualTo(session.Economy.MinPrice));
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

        [UnityTest, Category("ContinuousOperations")]
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
