using System;
using System.Linq;
using NUnit.Framework;

namespace WorstHotel.Tests
{
    public sealed class ProgressionTests
    {
        private SessionSettings settings;
        private RoomState[] rooms;
        private HotelSimulation simulation;

        [SetUp]
        public void Setup()
        {
            var profiles = new[]
            {
                new GuestProfile(GuestKind.Budget, "Budget", "", 180, 0.85f, 18, 0.7f, 28, 90, 0.55f),
                new GuestProfile(GuestKind.ColdSensitive, "Cold-sensitive", "", 300, 1.05f, 20, 1.35f, 16, 65, 0.4f),
                new GuestProfile(GuestKind.Business, "Business", "", 450, 1, 19.5f, 1, 10, 45, 0.25f)
            };
            var roomProfiles = Enumerable.Range(101, 6).Select(id => new RoomProfile(id, "Room " + id)).ToArray();
            settings = new SessionSettings(profiles, roomProfiles, new BoilerSettings(), new EconomySettings());
            rooms = roomProfiles.Select(profile => new RoomState(profile)).ToArray();
            simulation = new HotelSimulation(settings, rooms);
        }

        private CommandResult StartDay(int day, bool highestOffers = false, int guestCount = 4)
        {
            var offers = GuestSystem.GenerateApplications(day, settings.GuestArchetypes);
            var chosen = (highestOffers ? offers.OrderByDescending(offer => offer.ReferencePrice) : offers.AsEnumerable()).Take(guestCount).ToArray();
            var assignments = chosen.Select((offer, index) => new BookingAssignment(101 + index, offer.Id,
                (int)Math.Round(offer.ReferencePrice / 10.0, MidpointRounding.AwayFromZero) * 10, index % 2)).ToArray();
            return simulation.StartShift(assignments, offers);
        }

        private DayReport FinishDay()
        {
            while (!simulation.IsServiceComplete) simulation.Tick(0.2f);
            return simulation.EndShift();
        }

        [Test]
        public void ThreeDaysCarryCashConditionAndReviewsThroughTwoDistinctMaintenanceDecisions()
        {
            Assert.That(StartDay(1).Success, Is.True);
            var first = FinishDay();
            Assert.That(first.Cash, Is.EqualTo(1010));
            Assert.That(simulation.Boiler.Condition, Is.EqualTo(65).Within(0.03f));
            Assert.That(StartDay(2, true).Success, Is.False, "Starting a new shift must not silently skip maintenance.");
            Assert.That(simulation.ApplyMaintenance(0, MaintenanceChoice.CheapPatch).Success, Is.True);
            Assert.That(simulation.Economy.Cash, Is.EqualTo(810));
            Assert.That(simulation.Boiler.Condition, Is.EqualTo(80).Within(0.03f));
            Assert.That(StartDay(2, true).Success, Is.True);
            var second = FinishDay();
            Assert.That(second.Cash, Is.EqualTo(2010));
            Assert.That(simulation.ApplyMaintenance(1, MaintenanceChoice.ProperRepair).Success, Is.True);
            Assert.That(simulation.Economy.Cash, Is.EqualTo(510));
            Assert.That(simulation.Boiler.Condition, Is.EqualTo(95));
            Assert.That(StartDay(3).Success, Is.True);
            var third = FinishDay();
            Assert.That(third.DayNumber, Is.EqualTo(3));
            Assert.That(simulation.DayReports.Count, Is.EqualTo(3));
            Assert.That(simulation.DayReports.Sum(report => report.Receipts.Count), Is.EqualTo(12));
            Assert.That(simulation.DayReports.SelectMany(report => report.Receipts).All(receipt => !string.IsNullOrWhiteSpace(receipt.Review)), Is.True);
            Assert.That(simulation.MaintenanceDecisions.Count, Is.EqualTo(2));
            Assert.That(simulation.MaintenanceDecisions.Sum(decision => decision.Cost), Is.EqualTo(1700));
            Assert.That(simulation.MaintenanceRequired, Is.False);
            Assert.That(simulation.ApplyMaintenance(0, MaintenanceChoice.Defer).Success, Is.False);
            Assert.That(StartDay(3).Success, Is.False, "There is no fourth service in this prototype.");
        }

        [Test]
        public void UnaffordableInvalidAndRepeatedMaintenanceCannotDebitOrRepair()
        {
            StartDay(1); FinishDay();
            int cash = simulation.Economy.Cash;
            float condition = simulation.Boiler.Condition;
            Assert.That(simulation.ApplyMaintenance(0, MaintenanceChoice.ProperRepair).Success, Is.False);
            Assert.That(simulation.ApplyMaintenance(2, MaintenanceChoice.CheapPatch).Success, Is.False);
            Assert.That(simulation.ApplyMaintenance(0, (MaintenanceChoice)99).Success, Is.False);
            Assert.That(simulation.Economy.Cash, Is.EqualTo(cash));
            Assert.That(simulation.Boiler.Condition, Is.EqualTo(condition));
            Assert.That(simulation.MaintenanceRequired, Is.True);
            Assert.That(simulation.ApplyMaintenance(1, MaintenanceChoice.CheapPatch).Success, Is.True);
            Assert.That(simulation.ApplyMaintenance(0, MaintenanceChoice.CheapPatch).Success, Is.False);
            Assert.That(simulation.Economy.Cash, Is.EqualTo(cash - 200));
            Assert.That(simulation.MaintenanceDecisions.Single().ActorId, Is.EqualTo(1));
            Assert.That(simulation.EndShift().Cash, Is.EqualTo(cash), "The immutable daily report remains the pre-maintenance settlement ledger.");
            Assert.That(simulation.DayReports.Count, Is.EqualTo(1));
        }

        [Test]
        public void PaidMaintenanceClearsFailureAndWarmsRoomsWithoutExtraWear()
        {
            StartDay(1); FinishDay();
            simulation.Boiler.SetCondition(30);
            simulation.Boiler.ForceFailure();
            foreach (var room in rooms) room.Temperature = 12;
            Assert.That(simulation.ApplyMaintenance(0, MaintenanceChoice.CheapPatch).Success, Is.True);
            Assert.That(simulation.Boiler.Condition, Is.EqualTo(45));
            Assert.That(simulation.Boiler.Failed, Is.False);
            Assert.That(simulation.Boiler.Pressure, Is.EqualTo(settings.Boiler.StartPressure));
            Assert.That(rooms.All(room => room.Temperature > 19), Is.True);
            Assert.That(simulation.Boiler.Condition, Is.EqualTo(45), "Overnight preparation must not advance operating wear.");
        }

        [Test]
        public void DeferredFailedBoilerKeepsItsFailureWearAndColdRoomsEvenInDebt()
        {
            StartDay(1); simulation.Boiler.ForceFailure(); FinishDay();
            simulation.DebugSetCash(-100);
            float condition = simulation.Boiler.Condition;
            Assert.That(simulation.ApplyMaintenance(0, MaintenanceChoice.Defer).Success, Is.True);
            Assert.That(simulation.Economy.Cash, Is.EqualTo(-100));
            Assert.That(simulation.Boiler.Condition, Is.EqualTo(condition));
            Assert.That(simulation.Boiler.Failed, Is.True);
            Assert.That(rooms.All(room => room.Temperature < 12), Is.True);
            Assert.That(StartDay(2).Success, Is.True);
            Assert.That(simulation.Boiler.Failed, Is.True);
            Assert.That(simulation.Boiler.Condition, Is.EqualTo(condition));
        }

        [Test]
        public void ProperRepairPreservesConditionAlreadyAboveItsRestorationTarget()
        {
            StartDay(1); FinishDay();
            simulation.DebugSetCash(3000); simulation.Boiler.SetCondition(99); simulation.Boiler.ForceFailure();
            Assert.That(simulation.ApplyMaintenance(0, MaintenanceChoice.ProperRepair).Success, Is.True);
            Assert.That(simulation.Boiler.Condition, Is.EqualTo(99));
            Assert.That(simulation.Boiler.Failed, Is.False);
            Assert.That(simulation.Economy.Cash, Is.EqualTo(1500));
        }

        [Test]
        public void WorkingDeferredBoilerPreparesRoomsUsingItsCurrentCondition()
        {
            StartDay(1); FinishDay();
            simulation.Boiler.SetCondition(50);
            foreach (var room in rooms) room.Temperature = 12;
            Assert.That(simulation.ApplyMaintenance(0, MaintenanceChoice.Defer).Success, Is.True);
            float target = settings.TemperatureBase + settings.HeatTemperatureGain * simulation.Boiler.HeatingOutput;
            Assert.That(rooms[0].Temperature, Is.GreaterThan(20).And.LessThan(target));
            Assert.That(simulation.Boiler.Condition, Is.EqualTo(50));
        }

        [Test]
        public void DebugCashRejectsNonFiniteValuesAndRealGuestSpawnIsAtomic()
        {
            Assert.That(simulation.DebugSetCash(float.NaN).Success, Is.False);
            Assert.That(simulation.DebugSetCash(float.PositiveInfinity).Success, Is.False);
            Assert.That(simulation.DebugSetCash(float.MaxValue).Success, Is.False);
            Assert.That(simulation.Economy.Cash, Is.EqualTo(350));
            Assert.That(simulation.DebugSetCash(312.5f).Success, Is.True);
            Assert.That(simulation.Economy.Cash, Is.EqualTo(313));
            Assert.That(simulation.DebugSpawnGuest(GuestKind.Business, 102).Success, Is.False);
            StartDay(1, guestCount: 1);
            float demand = simulation.Boiler.Load;
            Assert.That(simulation.DebugSpawnGuest(GuestKind.Business, 102).Success, Is.True);
            Assert.That(simulation.Guests.Count, Is.EqualTo(2));
            Assert.That(simulation.Boiler.Load, Is.EqualTo(demand + 1).Within(0.0001f));
            Assert.That(rooms[1].Occupied, Is.True);
            Assert.That(simulation.DebugSpawnGuest(GuestKind.Business, 102).Success, Is.False);
            Assert.That(simulation.DebugSpawnGuest((GuestKind)99, 103).Success, Is.False);
            Assert.That(simulation.DebugSpawnGuest(GuestKind.Budget, 999).Success, Is.False);
            Assert.That(simulation.Guests.Count, Is.EqualTo(2));
            simulation.Tick(10);
            Assert.That(simulation.Guests.Last().Elapsed, Is.EqualTo(10));
        }
    }
}
