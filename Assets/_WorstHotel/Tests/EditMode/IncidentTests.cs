using System.Linq;
using NUnit.Framework;

namespace WorstHotel.Tests
{
    public sealed class IncidentTests
    {
        private SessionSettings settings;
        private GuestStay guest;
        private RoomState room;
        private IncidentSystem incidents;
        private RequestSystem requests;

        [SetUp]
        public void Setup()
        {
            var profiles = new[]
            {
                new GuestProfile(GuestKind.Budget, "Budget", "", 180, 0.85f, 18, 0.7f, 28, 90, 0.55f),
                new GuestProfile(GuestKind.ColdSensitive, "Cold", "", 300, 1.05f, 20, 1.35f, 16, 65, 0.4f),
                new GuestProfile(GuestKind.Business, "Business", "", 450, 1, 19.5f, 1, 10, 45, 0.25f)
            };
            var rooms = Enumerable.Range(101, 6).Select(id => new RoomProfile(id, "Room " + id)).ToArray();
            settings = new SessionSettings(profiles, rooms, new BoilerSettings(), new EconomySettings());
            guest = new GuestStay(new BookingApplication("test-guest", "Mara", profiles[0], 180), 101, 180);
            room = new RoomState(rooms[0]);
            incidents = new IncidentSystem(settings);
            requests = new RequestSystem(incidents);
        }

        private void Tick(float dt)
        {
            incidents.Tick(new[] { guest }, new[] { room }, dt);
            requests.Tick();
        }

        [Test]
        public void ComfortableStateDoesNotCreateRequestsEvenAfterLongService()
        {
            for (int i = 0; i < 1500; i++) Tick(0.2f);
            Assert.That(incidents.Items, Is.Empty);
            Assert.That(requests.Items, Is.Empty);
        }

        [Test]
        public void ColdRequiresSustainedMeasuredConditionAndDeduplicatesEveryLaterTick()
        {
            room.Temperature = 17;
            Tick(14);
            Assert.That(requests.Items, Is.Empty);
            Tick(1);
            Assert.That(requests.ActiveCount, Is.EqualTo(1));
            var request = requests.Items.Single();
            Assert.That(request.Reason, Is.EqualTo(IncidentReason.Cold));
            Assert.That(request.MeasuredCause, Does.Contain("17"));
            for (int i = 0; i < 100; i++) Tick(1);
            Assert.That(requests.Items.Count, Is.EqualTo(1));
            Assert.That(requests.Items.Single(), Is.SameAs(request));
            Assert.That(request.Urgency, Is.EqualTo(RequestUrgency.High));
            Assert.That(request.RemainingPatience, Is.Zero);
        }

        [Test]
        public void RecoveryUsesTemperatureHysteresisAndContinuousRecoveryDuration()
        {
            room.Temperature = 17; Tick(15);
            room.Temperature = 18.2f; Tick(30);
            Assert.That(requests.ActiveCount, Is.EqualTo(1), "Crossing the complaint threshold alone is not enough to resolve cold.");
            room.Temperature = 18.5f; Tick(9);
            Assert.That(requests.ActiveCount, Is.EqualTo(1));
            room.Temperature = 18.3f; Tick(1);
            room.Temperature = 18.5f; Tick(9);
            Assert.That(requests.ActiveCount, Is.EqualTo(1), "A dip out of the recovery band must reset the recovery timer.");
            Tick(1);
            Assert.That(requests.ActiveCount, Is.Zero);
            Assert.That(incidents.Items.Single().Resolved, Is.True);
            room.Temperature = 17; Tick(15);
            Assert.That(requests.ActiveCount, Is.EqualTo(1));
            Assert.That(requests.Items.Count, Is.EqualTo(1), "A recurrence reopens its original source request.");
        }

        [Test]
        public void CompensationMarksResponseWithoutPretendingTheColdRoomIsFixed()
        {
            room.Temperature = 17; Tick(15);
            requests.SetCompensated(guest.GuestId, true);
            Assert.That(requests.Items.Single().Compensated, Is.True);
            Assert.That(requests.Items.Single().Resolved, Is.False);
            Assert.That(incidents.Items.Single().Active, Is.True);
        }

        [Test]
        public void MultipleSymptomsNeverMultiplyExpiredPatienceBeyondElapsedStay()
        {
            room.Temperature = 17; room.Noise = 0.8f; Tick(15); Tick(90);
            Assert.That(requests.ActiveCount, Is.EqualTo(2));
            var satisfaction = new GuestSatisfactionSystem(settings.Economy);
            for (int i = 0; i < 10; i++)
                satisfaction.Accumulate(guest, room, 1, requests.HasExpiredRequest(guest.GuestId));
            Assert.That(guest.ExpiredComplaintSeconds, Is.EqualTo(10));
            Assert.That(guest.ExpiredComplaintSeconds, Is.EqualTo(guest.Elapsed));
        }

        [Test]
        public void HeatingFailureCausallyCoolsRoomsAndCreatesColdRequests()
        {
            var rooms = settings.Rooms.Select(profile => new RoomState(profile)).ToArray();
            var simulation = new HotelSimulation(settings, rooms);
            var offer = new BookingApplication("cold-guest", "Nina", settings.GuestArchetypes[1], 300);
            Assert.That(simulation.StartShift(new[] { new BookingAssignment(101, offer.Id, 300, 0) }, new[] { offer }).Success, Is.True);
            Assert.That(simulation.Boiler.Load, Is.EqualTo(1.05f));
            simulation.Boiler.ForceFailure();
            for (int i = 0; i < 200; i++) simulation.Tick(0.2f);
            Assert.That(rooms[0].Temperature, Is.LessThan(20));
            Assert.That(simulation.Requests.Items.Any(request => request.Reason == IncidentReason.Cold), Is.True);
            Assert.That(simulation.Guests.Single().QualityIntegral, Is.GreaterThan(0));
        }

        [Test]
        public void TemperatureResponseMatchesAcrossTickSubdivisions()
        {
            var oneStep = new RoomState(settings.Rooms[0]);
            var manySteps = new RoomState(settings.Rooms[0]);
            var system = new RoomSystem(settings);
            system.TickTemperature(new[] { oneStep }, 0.4f, 60);
            for (int i = 0; i < 300; i++) system.TickTemperature(new[] { manySteps }, 0.4f, 0.2f);
            Assert.That(manySteps.Temperature, Is.EqualTo(oneStep.Temperature).Within(0.001f));
        }
    }
}
