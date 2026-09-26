using NUnit.Framework;

namespace WorstHotel.Tests
{
    public sealed class SatisfactionTests
    {
        private static GuestProfile Budget() => new GuestProfile(GuestKind.Budget, "Budget", "", 180, 0.85f, 18, 0.7f, 28, 90, 0.55f);
        private static GuestStay Stay(int price = 180, GuestProfile guest = null) =>
            new GuestStay(new BookingApplication("guest", "Mara", guest ?? Budget(), (guest ?? Budget()).ReferencePrice), 101, price);

        [Test]
        public void IdenticalDefectiveRoomCostsMoreSatisfactionAtHigherPrice()
        {
            var system = new GuestSatisfactionSystem(new EconomySettings());
            var room = new RoomState(new RoomProfile(101, "Cold and degraded", temperature: 16, repairState: RepairState.Degraded));
            var cheap = Stay(180); var expensive = Stay(360);
            system.Accumulate(cheap, room, 300); system.Accumulate(expensive, room, 300);
            Assert.That(system.PriceExpectation(180, 180), Is.EqualTo(1).Within(0.0001f));
            Assert.That(system.PriceExpectation(360, 180), Is.EqualTo(1.75f).Within(0.0001f));
            Assert.That(system.Evaluate(cheap), Is.EqualTo(70).Within(0.001f));
            Assert.That(system.Evaluate(expensive), Is.LessThan(system.Evaluate(cheap)));
        }

        [Test]
        public void FasterPhysicalRecoveryPreservesMoreOfTheStayScore()
        {
            var system = new GuestSatisfactionSystem(new EconomySettings());
            var room = new RoomState(new RoomProfile(101, "Cold", temperature: 14));
            var fastResponse = Stay(); var ignored = Stay();
            system.Accumulate(fastResponse, room, 100);
            system.Accumulate(ignored, room, 300);
            room.Temperature = 21;
            system.Accumulate(fastResponse, room, 200);
            Assert.That(system.Evaluate(fastResponse), Is.EqualTo(86).Within(0.001f));
            Assert.That(system.Evaluate(ignored), Is.EqualTo(58).Within(0.001f));
        }

        [Test]
        public void ColdSensitiveGuestReactsMoreStronglyToTheSameRoom()
        {
            var cold = new GuestProfile(GuestKind.ColdSensitive, "Cold-sensitive", "", 300, 1.05f, 20, 1.35f, 16, 65, 0.4f);
            var business = new GuestProfile(GuestKind.Business, "Business", "", 450, 1, 19.5f, 1, 10, 45, 0.25f);
            var system = new GuestSatisfactionSystem(new EconomySettings());
            var room = new RoomState(new RoomProfile(101, "Cool", temperature: 18));
            var coldStay = Stay(300, cold); var businessStay = Stay(450, business);
            system.Accumulate(coldStay, room, 300); system.Accumulate(businessStay, room, 300);
            Assert.That(system.Evaluate(coldStay), Is.LessThan(system.Evaluate(businessStay)));
        }

        [Test]
        public void DisplayedNoiseCleanlinessAndFixtureStateAllAffectActualQuality()
        {
            var system = new GuestSatisfactionSystem(new EconomySettings());
            var room = new RoomState(new RoomProfile(101, "Room"));
            Assert.That(system.QualityDeficit(room, Budget()), Is.Zero);
            room.Noise = 0.8f;
            Assert.That(system.QualityDeficit(room, Budget()), Is.EqualTo(0.25f).Within(0.0001f));
            room.Cleanliness = Cleanliness.Dirty;
            Assert.That(system.QualityDeficit(room, Budget()), Is.EqualTo(0.55f).Within(0.0001f));
            room.RepairState = RepairState.Degraded;
            Assert.That(system.QualityDeficit(room, Budget()), Is.EqualTo(0.7f).Within(0.0001f));
            room.RepairState = RepairState.Broken;
            Assert.That(system.QualityDeficit(room, Budget()), Is.EqualTo(1.05f).Within(0.0001f));
        }

        [Test]
        public void TickSubdivisionDoesNotChangeTimeWeightedSatisfaction()
        {
            var system = new GuestSatisfactionSystem(new EconomySettings());
            var room = new RoomState(new RoomProfile(101, "Cool", temperature: 16));
            var frequent = Stay(); var slow = Stay();
            for (int i = 0; i < 1500; i++) system.Accumulate(frequent, room, 0.2f, i >= 500);
            for (int i = 0; i < 300; i++) system.Accumulate(slow, room, 1, i >= 100);
            Assert.That(system.Evaluate(frequent), Is.EqualTo(system.Evaluate(slow)).Within(0.01f));
            Assert.That(frequent.ExpiredComplaintSeconds, Is.LessThanOrEqualTo(frequent.Elapsed));
        }

        [Test]
        public void ZeroDurationAndCompensationRemainFiniteAndBounded()
        {
            var settings = new EconomySettings();
            var satisfaction = new GuestSatisfactionSystem(settings);
            var stay = Stay();
            Assert.That(satisfaction.Evaluate(stay), Is.EqualTo(100));
            new EconomySystem(settings).ReserveCompensation(stay);
            Assert.That(satisfaction.Evaluate(stay), Is.EqualTo(100));
            Assert.That(satisfaction.PriceExpectation(1, 180), Is.EqualTo(settings.MinExpectation));
            Assert.That(satisfaction.PriceExpectation(650, 180), Is.EqualTo(settings.MaxExpectation));
        }
    }
}
