using NUnit.Framework;

namespace WorstHotel.Tests
{
    public sealed class ReviewTests
    {
        private static GuestStay Stay(int price = 180)
        {
            var budget = new GuestProfile(GuestKind.Budget, "Budget", "", 180, 0.85f, 18, 0.7f, 28, 90, 0.55f);
            return new GuestStay(new BookingApplication("guest", "Mara", budget, 180), 101, price);
        }

        [Test]
        public void WarmQuietStayDoesNotInventColdNoiseComplaintsOrCompensation()
        {
            var settings = new EconomySettings();
            var satisfaction = new GuestSatisfactionSystem(settings);
            var stay = Stay();
            satisfaction.Accumulate(stay, new RoomState(new RoomProfile(101, "Quiet")), 300);
            string review = ReviewSystem.Build(stay, satisfaction.Evaluate(stay), 0);
            Assert.That(review, Does.Contain("Comfortable and quiet"));
            Assert.That(review, Does.Not.Contain("cold"));
            Assert.That(review, Does.Not.Contain("compensation"));
            Assert.That(review, Does.Not.Contain("patience"));
        }

        [Test]
        public void ReviewReportsActualColdDurationRateAndCompensationResponse()
        {
            var settings = new EconomySettings();
            var satisfaction = new GuestSatisfactionSystem(settings);
            var stay = Stay(300);
            var room = new RoomState(new RoomProfile(101, "Cold", temperature: 14));
            satisfaction.Accumulate(stay, room, 100, true);
            room.Temperature = 21;
            satisfaction.Accumulate(stay, room, 200);
            var economy = new EconomySystem(settings);
            economy.ReserveCompensation(stay);
            var receipt = economy.CalculateReceipt(stay, satisfaction.Evaluate(stay));
            Assert.That(receipt.Review, Does.Contain("cold for 33%"));
            Assert.That(receipt.Review, Does.Contain("At $300"));
            Assert.That(receipt.Review, Does.Contain("outlasted my patience"));
            Assert.That(receipt.Review, Does.Contain("Staff offered compensation"));
            Assert.That(receipt.Review, Does.Not.Contain("noise"));
            Assert.That(stay.ColdExposureSeconds, Is.EqualTo(100));
        }

        [Test]
        public void ExposedNoiseAndFixturesAppearWithoutInventingHeatingFailure()
        {
            var settings = new EconomySettings();
            var satisfaction = new GuestSatisfactionSystem(settings);
            var stay = Stay();
            var room = new RoomState(new RoomProfile(101, "Noisy", noise: 0.8f, repairState: RepairState.Degraded));
            satisfaction.Accumulate(stay, room, 300);
            string review = ReviewSystem.Build(stay, satisfaction.Evaluate(stay), 0);
            Assert.That(review, Does.Contain("Noise"));
            Assert.That(review, Does.Contain("fixture"));
            Assert.That(review, Does.Not.Contain("cold"));
            Assert.That(review, Does.Not.Contain("boiler"));
        }

        [Test]
        public void ZeroDurationDebugCheckoutDoesNotClaimAnObservedStayQuality()
        {
            Assert.That(ReviewSystem.Build(Stay(), 100, 0), Does.Contain("before there was enough time"));
        }
    }
}
