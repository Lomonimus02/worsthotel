using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace WorstHotel.Tests
{
    public sealed partial class Phase1PlayModeTests
    {
        [UnityTest, Category("ContinuousOperations")]
        public IEnumerator ContinuousReceptionForecastTracksCandidateRoomAndBookingsWithoutPricingInventingDemand()
        {
            var session = GameSession.Instance; var model = session.Simulation;
            Assert.That(model.ContinuousOperations, Is.True);
            // Labelled initial commitments isolate actual candidate inspection/navigation.
            // No guests are spawned, and no income or artificial demand is granted by this setup.
            var offers = model.BookingOffers.Where(value => value.ArrivalDay == session.Day + 1).OrderBy(value => value.ArrivalAt).Take(4).ToArray();
            Assert.That(offers.Length, Is.EqualTo(4));
            for (int index = 0; index < 3; index++)
                Assert.That(session.AcceptBooking(0, offers[index].Id, 101 + index, session.Economy.MinPrice).Success, Is.True);
            long booked = model.UnpaidBookedRevenue;
            int cash = model.Economy.Cash;
            var threeStays = model.ForecastBookingLoad(offers[0].Id, 101);
            Assert.That(threeStays.Available, Is.True, threeStays.Reason);
            Assert.That(threeStays.MaxConcurrentGuests, Is.EqualTo(3));
            var terminal = Object.FindAnyObjectByType<ReceptionTerminal>();
            yield return FaceStation(bootstrap.Players[0], padA, terminal, terminal.transform.position + Vector3.up * .35f);
            QueueUse(padA, true);
            yield return WaitForCondition(() => ManagementUI.Instance.IsOperationsOpen, 2, "Real reception input opens the forecast workflow.");
            QueueUse(padA, false); yield return null; yield return null;
            yield return ChooseOperationsOption("Tomorrow's bookings");
            yield return ChooseOperationsOption(offers[3].Application.GuestName + " · ");
            var ui = ManagementUI.Instance;
            var candidate = ui.DisplayedBookingForecast;
            Assert.That(candidate, Is.Not.Null);
            Assert.That(candidate.Available, Is.True, candidate.Reason);
            Assert.That(candidate.RoomId, Is.EqualTo(104));
            Assert.That(candidate.MaxConcurrentGuests, Is.EqualTo(4));
            Assert.That(candidate.TypicalDemand, Is.GreaterThan(threeStays.TypicalDemand));
            Assert.That(candidate.OneShowerPeakDemand, Is.GreaterThan(candidate.TypicalDemand));
            Assert.That(ui.OperationsOptionTitles.Any(title => title.StartsWith("Forecast · ")), Is.True);
            int revision = model.EventRevision;
            float time = model.Elapsed, demandNow = model.Boiler.Load;
            yield return ChooseOperationsOption("Forecast · ");
            Assert.That(ui.DisplayedBookingForecast.MaxConcurrentGuests, Is.EqualTo(4));
            Assert.That(ui.OperationsOptionTitles.Contains("Back to booking"), Is.True);
            yield return ChooseOperationsOption("Back to booking");
            int originalPrice = ui.DisplayedBookingPrice;
            yield return ChooseOperationsOption("+ $");
            Assert.That(ui.DisplayedBookingPrice, Is.EqualTo(originalPrice + session.Economy.PriceStep));
            Assert.That(ui.DisplayedBookingForecast.TypicalDemand, Is.EqualTo(candidate.TypicalDemand).Within(.0001f));
            Assert.That(ui.DisplayedBookingForecast.OneShowerPeakDemand, Is.EqualTo(candidate.OneShowerPeakDemand).Within(.0001f));
            Assert.That(model.EventRevision, Is.EqualTo(revision), "Reading a forecast and editing a local quote cannot publish a hotel event.");
            Assert.That(model.Economy.Cash, Is.EqualTo(cash));
            Assert.That(model.UnpaidBookedRevenue, Is.EqualTo(booked), "A draft price is not an accepted booking or paid revenue.");
            Assert.That(model.Boiler.Load, Is.EqualTo(demandNow).Within(.0001f));
            Assert.That(model.Guests, Is.Empty);
            Assert.That(model.Elapsed, Is.GreaterThan(time), "The hotel continues normally while the player reads the estimate.");

            yield return ChooseOperationsOption("Room 106 · ");
            var moved = ui.DisplayedBookingForecast;
            Assert.That(moved.Available && moved.RoomId == 106, Is.True);
            Assert.That(moved.MaxConcurrentGuests, Is.EqualTo(4));
            Assert.That(moved.TypicalDemand, Is.EqualTo(model.ForecastBookingLoad(offers[3].Id, 106).TypicalDemand).Within(.0001f));
            Assert.That(Mathf.Abs(moved.TypicalDemand - candidate.TypicalDemand), Is.GreaterThan(.0001f),
                "A selected room's different heat loss must refresh the estimate instead of keeping stale data.");
            Assert.That(model.EventRevision, Is.EqualTo(revision));
            yield return ChooseOperationsOption("Accept booking");
            var reservation = model.FindReservation(offers[3].Id);
            Assert.That(reservation.Status, Is.EqualTo(ReservationStatus.Reserved));
            Assert.That(reservation.RoomId, Is.EqualTo(106));
            Assert.That(reservation.Price, Is.EqualTo(originalPrice + session.Economy.PriceStep));
            Assert.That(ui.DisplayedBookingForecast.MaxConcurrentGuests, Is.EqualTo(4), "The accepted candidate must be included exactly once.");
            Assert.That(model.UnpaidBookedRevenue, Is.EqualTo(booked + reservation.Price));
            Assert.That(model.Economy.Cash, Is.EqualTo(cash), "Revenue is still unpaid until checkout.");
            yield return ChooseOperationsOption("Cancel reservation");
            Assert.That(ui.DisplayedBookingForecast.Available, Is.False);
            Assert.That(ui.OperationsOptionTitles.Any(title => title.StartsWith("Forecast unavailable")), Is.True);
            Assert.That(model.ForecastBookingLoad(offers[0].Id, 101).MaxConcurrentGuests, Is.EqualTo(3));
            Assert.That(model.UnpaidBookedRevenue, Is.EqualTo(booked));
            Assert.That(model.Economy.Cash, Is.EqualTo(cash));
            Assert.That(model.PeriodCheckoutIncome, Is.Zero);
            Assert.That(model.PeriodMaintenanceSpend + model.PeriodCapitalSpend, Is.Zero);
            Assert.That(model.Guests, Is.Empty);
            yield return ChooseOperationsOption("Forecast unavailable");
            Assert.That(ui.OperationsOptionTitles.Contains("Back to booking"), Is.True, "An unavailable estimate must still have a working controller exit.");
            yield return ChooseOperationsOption("Back to booking");
            yield return ChooseOperationsOption("Back to bookings");
            yield return ChooseOperationsOption("Back to operations");
            yield return ChooseOperationsOption("Daily reports");
            revision = model.EventRevision;
            yield return null; yield return null;
            Assert.That(model.EventRevision, Is.EqualTo(revision));
            Assert.That(model.Economy.Cash, Is.EqualTo(cash), "Reading current accounts cannot post future booked charges or repeat costs.");
            Assert.That(session.Phase, Is.EqualTo(DayPhase.Service));
            ManagementUI.Instance.Close();
            LogAssert.NoUnexpectedReceived();
        }
    }
}
