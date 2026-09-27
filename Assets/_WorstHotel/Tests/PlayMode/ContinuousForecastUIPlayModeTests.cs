using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace WorstHotel.Tests
{
    public sealed partial class Phase1PlayModeTests
    {
        [UnityTest, Category("ContinuousOperations"), Category("AutomaticSales")]
        public IEnumerator ContinuousReceptionForecastTracksRealAutomaticBookingAndAlternateRoomWithoutRepricingContract()
        {
            var session = GameSession.Instance; var model = session.Simulation;
            Assert.That(model.AutomaticBookingsEnabled, Is.True);
            // Advance the real calendar through existing enquiry slots. No reservations,
            // guest callbacks, prices or income are inserted by this UI fixture.
            session.AdvanceTime(model.Calendar.At(1, 19.6f) - model.Elapsed);
            var tomorrow = model.Reservations.Where(row => row.Offer.ArrivalDay == 2 && row.Active).OrderBy(row => row.Offer.ArrivalAt).ToArray();
            Assert.That(tomorrow.Length, Is.GreaterThanOrEqualTo(2));
            var reservation = tomorrow.Last();
            int originalRoom = reservation.RoomId, price = reservation.Price;
            long booked = model.UnpaidBookedRevenue; int cash = model.Economy.Cash;
            var original = model.ForecastBookingLoad(reservation.Id, originalRoom);
            Assert.That(original.Available, Is.True, original.Reason);
            Assert.That(original.MaxConcurrentGuests, Is.EqualTo(tomorrow.Length));
            yield return WaitForCondition(() => model.Guests.All(guest => guest.Agent.State == GuestAgentState.WaitingForCheckIn), 30,
                "Let today's actual routes finish before isolating pure forecast-menu reads.");
            var terminal = Object.FindAnyObjectByType<ReceptionTerminal>();
            yield return FaceStation(bootstrap.Players[0], padA, terminal, terminal.transform.position + Vector3.up * .35f);
            QueueUse(padA, true);
            yield return WaitForCondition(() => ManagementUI.Instance.IsOperationsOpen, 2, "Real reception input opens the forecast workflow.");
            QueueUse(padA, false); yield return null; yield return null;
            yield return ChooseOperationsOption("Tomorrow's bookings");
            yield return ChooseOperationsOption(reservation.Offer.Application.GuestName + " · ");
            var ui = ManagementUI.Instance;
            Assert.That(ui.DisplayedBookingPrice, Is.EqualTo(price));
            Assert.That(ui.OperationsOptionTitles.Any(title => title.StartsWith("Accept booking") || title.StartsWith("Update agreed price") || title.StartsWith("+ $")), Is.False,
                "An automatically agreed contract cannot be approved or repriced through its detail page.");
            Assert.That(ui.DisplayedBookingForecast.MaxConcurrentGuests, Is.EqualTo(tomorrow.Length));
            yield return ChooseOperationsOption("Forecast · ");
            Assert.That(ui.OperationsOptionTitles.Contains("Back to booking"), Is.True);
            yield return ChooseOperationsOption("Back to booking");
            int revision = model.EventRevision; float time = model.Elapsed, load = model.Boiler.Load;
            yield return ChooseOperationsOption("Room 106 · ");
            var candidate = ui.DisplayedBookingForecast;
            Assert.That(candidate.Available && candidate.RoomId == 106, Is.True);
            Assert.That(candidate.MaxConcurrentGuests, Is.EqualTo(tomorrow.Length), "The existing reservation replaces itself in a different room.");
            Assert.That(candidate.TypicalDemand, Is.EqualTo(model.ForecastBookingLoad(reservation.Id, 106).TypicalDemand).Within(.0001f));
            Assert.That(Mathf.Abs(candidate.TypicalDemand - original.TypicalDemand), Is.GreaterThan(.0001f));
            Assert.That(model.EventRevision, Is.EqualTo(revision), "A preview cannot publish a hotel action.");
            Assert.That(model.Economy.Cash, Is.EqualTo(cash)); Assert.That(model.UnpaidBookedRevenue, Is.EqualTo(booked));
            Assert.That(model.Boiler.Load, Is.EqualTo(load).Within(.0001f));
            Assert.That(model.Elapsed, Is.GreaterThan(time));
            yield return ChooseOperationsOption("Reassign booking · room 106");
            Assert.That(reservation.RoomId, Is.EqualTo(106)); Assert.That(reservation.Price, Is.EqualTo(price));
            Assert.That(ui.DisplayedBookingForecast.MaxConcurrentGuests, Is.EqualTo(tomorrow.Length));
            Assert.That(model.UnpaidBookedRevenue, Is.EqualTo(booked));
            yield return ChooseOperationsOption("Cancel reservation");
            Assert.That(ui.DisplayedBookingForecast.Available, Is.False);
            Assert.That(ui.OperationsOptionTitles.Any(title => title.StartsWith("Forecast unavailable")), Is.True);
            Assert.That(model.UnpaidBookedRevenue, Is.EqualTo(booked - price)); Assert.That(model.Economy.Cash, Is.EqualTo(cash));
            Assert.That(model.PeriodCheckoutIncome, Is.Zero);
            yield return ChooseOperationsOption("Forecast unavailable");
            Assert.That(ui.OperationsOptionTitles.Contains("Back to booking"), Is.True);
            yield return ChooseOperationsOption("Back to booking");
            yield return ChooseOperationsOption("Back to bookings");
            yield return ChooseOperationsOption("Back to operations");
            yield return ChooseOperationsOption("Daily reports");
            cash = model.Economy.Cash; yield return null; yield return null;
            Assert.That(model.Economy.Cash, Is.EqualTo(cash), "Reading accounts cannot post future booked charges.");
            Assert.That(session.Phase, Is.EqualTo(DayPhase.Service));
            ManagementUI.Instance.Close(); LogAssert.NoUnexpectedReceived();
        }
    }
}
