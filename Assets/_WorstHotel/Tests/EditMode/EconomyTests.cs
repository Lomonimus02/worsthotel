using System;
using System.Linq;
using NUnit.Framework;

namespace WorstHotel.Tests
{
    public sealed class EconomyTests
    {
        private static GuestProfile[] Profiles() => new[]
        {
            new GuestProfile(GuestKind.Budget, "Budget", "", 180, 0.85f, 18, 0.7f, 28, 90, 0.55f),
            new GuestProfile(GuestKind.ColdSensitive, "Cold", "", 300, 1.05f, 20, 1.35f, 16, 65, 0.4f),
            new GuestProfile(GuestKind.Business, "Business", "", 450, 1, 19.5f, 1, 10, 45, 0.25f)
        };

        private static HotelSimulation Simulation(out RoomState[] rooms, out BookingApplication[] offers, out BookingAssignment[] assignments)
        {
            var profiles = Profiles();
            var roomProfiles = Enumerable.Range(101, 6).Select(id => new RoomProfile(id, "Room " + id)).ToArray();
            rooms = roomProfiles.Select(profile => new RoomState(profile)).ToArray();
            offers = GuestSystem.GenerateApplications(1, profiles);
            assignments = offers.Select((offer, index) => new BookingAssignment(101 + index, offer.Id, offer.ReferencePrice, index % 2)).ToArray();
            return new HotelSimulation(new SessionSettings(profiles, roomProfiles, new BoilerSettings(), new EconomySettings()), rooms);
        }

        private static GuestStay Stay(int price = 300)
        {
            var profile = Profiles()[1];
            return new GuestStay(new BookingApplication("one", "Nina", profile, 300), 101, price);
        }

        [Test]
        public void CleanFourGuestDayPaysOnceAndChargesOneOperatingCost()
        {
            var simulation = Simulation(out var rooms, out var offers, out var assignments);
            Assert.That(simulation.StartShift(assignments, offers).Success, Is.True);
            Assert.That(rooms.Count(room => room.Occupied), Is.EqualTo(4));
            for (int i = 0; i < 1500; i++) simulation.Tick(0.2f);
            simulation.Tick(1); // Clamp any float accumulation remainder to the configured service boundary.
            Assert.That(simulation.Remaining, Is.Zero);
            var report = simulation.EndShift();
            Assert.That(report.Gross, Is.EqualTo(1110));
            Assert.That(report.Compensation, Is.Zero);
            Assert.That(report.OperatingCost, Is.EqualTo(450));
            Assert.That(report.Net, Is.EqualTo(660));
            Assert.That(report.Cash, Is.EqualTo(1010));
            Assert.That(report.Reputation, Is.EqualTo(64.5f).Within(0.001f));
            Assert.That(simulation.EndShift(), Is.SameAs(report));
            Assert.That(simulation.Economy.Cash, Is.EqualTo(1010));
            Assert.That(rooms.All(room => !room.Occupied), Is.True);
        }

        [Test]
        public void VoluntaryCreditIsReservedAndCheckoutUsesLargerRefundWithoutDoubleDebit()
        {
            var economy = new EconomySystem(new EconomySettings());
            var stay = Stay();
            Assert.That(economy.ReserveCompensation(stay).Success, Is.True);
            Assert.That(stay.CompensationCredit, Is.EqualTo(60));
            Assert.That(economy.Cash, Is.EqualTo(350));
            Assert.That(economy.ReserveCompensation(stay).Success, Is.False);
            var receipt = economy.CalculateReceipt(stay, 20);
            Assert.That(receipt.Compensation, Is.EqualTo(150), "The $60 credit is part of the $150 required refund, not added to it.");
            var report = economy.Settle(1, new[] { receipt }, 300);
            Assert.That(report.Cash, Is.EqualTo(50));
            Assert.That(economy.Settle(1, new[] { receipt }, 300), Is.SameAs(report));
            Assert.That(economy.Cash, Is.EqualTo(50));
        }

        [TestCase(20, 150)]
        [TestCase(34.99f, 150)]
        [TestCase(35, 75)]
        [TestCase(59.99f, 75)]
        [TestCase(60, 0)]
        [TestCase(100, 0)]
        public void RefundThresholdsUseTheFinalGuestScore(float score, int expectedCompensation)
        {
            var economy = new EconomySystem(new EconomySettings());
            Assert.That(economy.CalculateReceipt(Stay(), score).Compensation, Is.EqualTo(expectedCompensation));
        }

        [Test]
        public void PromisedCreditIsStillHonoredWhenFinalQualityIsExcellent()
        {
            var economy = new EconomySystem(new EconomySettings());
            var stay = Stay();
            economy.ReserveCompensation(stay);
            Assert.That(economy.CalculateReceipt(stay, 100).Compensation, Is.EqualTo(60));
        }

        [Test]
        public void UnavoidableExpensesCanLeaveVisibleDebtWithoutBlockingSettlement()
        {
            var economy = new EconomySystem(new EconomySettings());
            var receipt = economy.CalculateReceipt(Stay(120), 100);
            Assert.That(economy.Settle(1, new[] { receipt }, 300).Cash, Is.EqualTo(20));
            Assert.That(economy.Settle(2, new[] { receipt }, 300).Cash, Is.EqualTo(-310));
        }

        [Test]
        public void InvalidShiftIsRejectedAtomicallyAndInvalidTickCannotPoisonState()
        {
            var simulation = Simulation(out var rooms, out var offers, out var assignments);
            assignments[1] = new BookingAssignment(101, offers[1].Id, 180, 1);
            Assert.That(simulation.StartShift(assignments, offers).Success, Is.False);
            Assert.That(simulation.Running, Is.False);
            Assert.That(rooms.All(room => !room.Occupied), Is.True);
            Assert.Throws<ArgumentOutOfRangeException>(() => simulation.Tick(float.NaN));
            Assert.That(simulation.Elapsed, Is.Zero);
        }

        [Test]
        public void ServiceTickStopsAtDurationAndCompensationRequiresAnActiveKnownGuest()
        {
            var simulation = Simulation(out _, out var offers, out var assignments);
            Assert.That(simulation.OfferCompensation(offers[0].Id).Success, Is.False);
            simulation.StartShift(assignments, offers);
            Assert.That(simulation.OfferCompensation("missing").Success, Is.False);
            Assert.That(simulation.OfferCompensation(offers[0].Id).Success, Is.True);
            Assert.That(simulation.OutstandingCompensation, Is.EqualTo(36));
            simulation.Tick(500);
            Assert.That(simulation.Elapsed, Is.EqualTo(300));
            Assert.That(simulation.Guests.All(guest => guest.Elapsed == 300), Is.True);
            simulation.EndShift();
            Assert.That(simulation.OutstandingCompensation, Is.Zero);
            Assert.That(simulation.OfferCompensation(offers[1].Id).Success, Is.False);
        }
    }
}
