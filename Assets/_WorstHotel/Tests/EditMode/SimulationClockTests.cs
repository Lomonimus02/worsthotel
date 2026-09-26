using System;
using System.Linq;
using NUnit.Framework;

namespace WorstHotel.Tests
{
    public sealed class SimulationClockTests
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
            var profilesForRooms = Enumerable.Range(101, 6).Select(id => new RoomProfile(id, "Room " + id)).ToArray();
            settings = new SessionSettings(profiles, profilesForRooms, new BoilerSettings(), new EconomySettings());
            rooms = profilesForRooms.Select(profile => new RoomState(profile)).ToArray();
            simulation = new HotelSimulation(settings, rooms);
        }

        private void Start(HotelSimulation target)
        {
            var offer = new BookingApplication("clock-guest", "Nina", settings.GuestArchetypes[1], 300);
            Assert.That(target.StartShift(new[] { new BookingAssignment(101, offer.Id, 300, 0) }, new[] { offer }).Success, Is.True);
        }

        [Test]
        public void InitialClockAndZeroAdvanceAreStableThroughReadOnlyInterface()
        {
            var clock = new HotelGameClock();
            IGameClock readOnlyClock = clock;
            Assert.That(readOnlyClock.SimulationTime, Is.Zero);
            Assert.That(readOnlyClock.Speed, Is.EqualTo(1));
            clock.Advance(0);
            Assert.That(readOnlyClock.SimulationTime, Is.Zero);
        }

        [TestCase(1f)]
        [TestCase(4f)]
        [TestCase(8f)]
        public void ExplicitSimulationStepsAreNotMultipliedBySelectedSpeed(float speed)
        {
            var clock = new HotelGameClock();
            clock.SetSpeed(speed);
            for (int i = 0; i < 5; i++) clock.Advance(0.2f);
            Assert.That(clock.Speed, Is.EqualTo(speed));
            Assert.That(clock.SimulationTime, Is.EqualTo(1).Within(0.00001f),
                "The caller budgets simulation ticks; Advance must not multiply their duration again.");
        }

        [Test]
        public void InvalidDeltaCannotCorruptPreviouslyAccumulatedTimeOrSpeed()
        {
            var clock = new HotelGameClock();
            clock.Advance(7);
            clock.SetSpeed(4);
            foreach (float delta in new[] { -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(delta));
                Assert.That(clock.SimulationTime, Is.EqualTo(7));
                Assert.That(clock.Speed, Is.EqualTo(4));
            }
        }

        [Test]
        public void FiniteDeltaThatWouldOverflowIsRejectedWithoutCorruptingClock()
        {
            var clock = new HotelGameClock();
            clock.Advance(float.MaxValue);
            Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(float.MaxValue));
            Assert.That(clock.SimulationTime, Is.EqualTo(float.MaxValue));
        }

        [Test]
        public void UnsupportedAndNonFiniteSpeedsPreserveLastValidSelection()
        {
            var clock = new HotelGameClock();
            clock.SetSpeed(4);
            foreach (float speed in new[] { 0f, -1f, 2f, 3.5f, 8.1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => clock.SetSpeed(speed));
                Assert.That(clock.Speed, Is.EqualTo(4));
                Assert.That(clock.SimulationTime, Is.Zero);
            }
        }

        [Test]
        public void ResetClearsTimeAndAccelerationAndIsIdempotent()
        {
            var clock = new HotelGameClock();
            clock.Advance(42);
            clock.SetSpeed(8);
            clock.Reset();
            Assert.That(clock.SimulationTime, Is.Zero);
            Assert.That(clock.Speed, Is.EqualTo(1));
            clock.Reset();
            Assert.That(clock.SimulationTime, Is.Zero);
            Assert.That(clock.Speed, Is.EqualTo(1));
        }

        [Test]
        public void IdenticalHotelTicksProduceIdenticalSimulationAtNormalAndFastClockSelection()
        {
            var otherRooms = settings.Rooms.Select(profile => new RoomState(profile)).ToArray();
            var other = new HotelSimulation(settings, otherRooms);
            Start(simulation);
            Start(other);
            other.Clock.SetSpeed(8);
            for (int i = 0; i < 50; i++)
            {
                simulation.Tick(0.2f);
                other.Tick(0.2f);
            }
            Assert.That(simulation.Elapsed, Is.EqualTo(10).Within(0.001f));
            Assert.That(other.Elapsed, Is.EqualTo(simulation.Elapsed));
            Assert.That(other.Boiler.Condition, Is.EqualTo(simulation.Boiler.Condition));
            Assert.That(other.Boiler.Pressure, Is.EqualTo(simulation.Boiler.Pressure));
            Assert.That(otherRooms[0].Temperature, Is.EqualTo(rooms[0].Temperature));
            Assert.That(other.Guests.Single().QualityIntegral, Is.EqualTo(simulation.Guests.Single().QualityIntegral));
            Assert.That(other.EventRevision, Is.EqualTo(simulation.EventRevision));
        }

        [Test]
        public void HealthyQuietTicksDoNotContinuouslySignalImportantEvents()
        {
            simulation.Tick(1);
            Assert.That(simulation.Elapsed, Is.Zero, "A hotel without an active shift must not advance.");
            int initialRevision = simulation.EventRevision;
            Start(simulation);
            Assert.That(simulation.EventRevision, Is.GreaterThan(initialRevision));
            int startedRevision = simulation.EventRevision;
            string startedEvent = simulation.LastEvent;
            simulation.Tick(0);
            for (int i = 0; i < 50; i++) simulation.Tick(0.2f);
            Assert.That(simulation.Requests.ActiveCount, Is.Zero);
            Assert.That(simulation.Boiler.Failed, Is.False);
            Assert.That(simulation.EventRevision, Is.EqualTo(startedRevision));
            Assert.That(simulation.LastEvent, Is.EqualTo(startedEvent).And.Not.Null.And.Not.Empty);
        }

        [Test]
        public void CrossingPressureWarningSignalsOnceWhileRemainingAboveThresholdDoesNotSpam()
        {
            Start(simulation);
            simulation.Boiler.SetCondition(30);
            simulation.Boiler.OverrideLoad(8);
            int beforeWarning = simulation.EventRevision;
            for (int i = 0; i < 100 && simulation.Boiler.Pressure < settings.Boiler.WarningPressure; i++)
                simulation.Tick(0.2f);
            Assert.That(simulation.Boiler.Pressure, Is.GreaterThanOrEqualTo(settings.Boiler.WarningPressure));
            Assert.That(simulation.Boiler.Failed, Is.False, "This fixture must observe the warning before mechanical failure.");
            Assert.That(simulation.Requests.ActiveCount, Is.Zero, "The warning must be the first important condition in this fixture.");
            Assert.That(simulation.EventRevision, Is.EqualTo(beforeWarning + 1));
            Assert.That(simulation.LastEvent, Is.Not.Null.And.Not.Empty);
            int warningRevision = simulation.EventRevision;
            simulation.Tick(0.2f);
            Assert.That(simulation.EventRevision, Is.EqualTo(warningRevision));
        }

        [Test]
        public void BoilerFailureAndSuccessfulDistinctActorRestartEachSignalOneEvent()
        {
            Start(simulation);
            int beforeFailure = simulation.EventRevision;
            simulation.Boiler.ForceFailure();
            Assert.That(simulation.EventRevision, Is.EqualTo(beforeFailure + 1));
            Assert.That(simulation.LastEvent, Is.Not.Null.And.Not.Empty);
            simulation.Boiler.ForceFailure();
            Assert.That(simulation.EventRevision, Is.EqualTo(beforeFailure + 1), "An existing failure is not a new event each frame.");
            Assert.That(simulation.Boiler.SetRelief(0, true).Success, Is.True);
            // Exercise the real boiler transition in isolation from unrelated room complaint timers.
            for (int i = 0; i < 100 && !simulation.Boiler.InRepairBand; i++) simulation.Boiler.Tick(0.2f);
            Assert.That(simulation.Boiler.InRepairBand, Is.True);
            Assert.That(simulation.Boiler.Restart(0).Success, Is.False);
            Assert.That(simulation.EventRevision, Is.EqualTo(beforeFailure + 1));
            Assert.That(simulation.Boiler.Restart(1).Success, Is.True);
            Assert.That(simulation.EventRevision, Is.EqualTo(beforeFailure + 2));
            Assert.That(simulation.LastEvent, Is.Not.Null.And.Not.Empty);
            Assert.That(simulation.Boiler.Restart(1).Success, Is.False);
            Assert.That(simulation.EventRevision, Is.EqualTo(beforeFailure + 2));
        }

        [Test]
        public void RequestCreationAndPhysicalRecoverySignalButRequestAgeDoesNot()
        {
            Start(simulation);
            Assert.That(simulation.SetRoomTemperature(101, 12).Success, Is.True);
            int beforeComplaint = simulation.EventRevision;
            for (int i = 0; i < 150 && simulation.Requests.ActiveCount == 0; i++) simulation.Tick(0.2f);
            Assert.That(simulation.Requests.ActiveCount, Is.EqualTo(1));
            Assert.That(simulation.EventRevision, Is.EqualTo(beforeComplaint + 1));
            Assert.That(simulation.LastEvent, Is.Not.Null.And.Not.Empty);
            int complaintRevision = simulation.EventRevision;
            simulation.Tick(0.2f);
            Assert.That(simulation.EventRevision, Is.EqualTo(complaintRevision));
            Assert.That(simulation.SetRoomTemperature(101, 28).Success, Is.True);
            for (int i = 0; i < 100 && simulation.Requests.ActiveCount > 0; i++) simulation.Tick(0.2f);
            Assert.That(simulation.Requests.ActiveCount, Is.Zero);
            Assert.That(simulation.Requests.Items.Single().Resolved, Is.True);
            Assert.That(simulation.EventRevision, Is.EqualTo(complaintRevision + 1));
            Assert.That(simulation.LastEvent, Is.Not.Null.And.Not.Empty);
            simulation.Tick(0.2f);
            Assert.That(simulation.EventRevision, Is.EqualTo(complaintRevision + 1));
        }

        [Test]
        public void ShiftEndClampsElapsedSignalsOnceAndNextDayResetsClock()
        {
            Start(simulation);
            simulation.Clock.SetSpeed(8);
            int beforeCompletion = simulation.EventRevision;
            for (int i = 0; i < 1600 && !simulation.IsServiceComplete; i++)
                simulation.Tick(0.2f);
            Assert.That(simulation.Elapsed, Is.EqualTo(settings.ServiceSeconds).Within(0.001f));
            Assert.That(simulation.Remaining, Is.Zero);
            Assert.That(simulation.IsServiceComplete, Is.True);
            Assert.That(simulation.EventRevision, Is.EqualTo(beforeCompletion + 1));
            int completedRevision = simulation.EventRevision;
            simulation.Tick(1);
            Assert.That(simulation.EventRevision, Is.EqualTo(completedRevision));
            var report = simulation.EndShift();
            Assert.That(simulation.Clock.Speed, Is.EqualTo(1));
            Assert.That(simulation.EventRevision, Is.EqualTo(completedRevision + 1));
            Assert.That(simulation.EndShift(), Is.SameAs(report));
            Assert.That(simulation.EventRevision, Is.EqualTo(completedRevision + 1));
            simulation.Tick(1);
            Assert.That(simulation.Elapsed, Is.EqualTo(settings.ServiceSeconds).Within(0.001f));
            Assert.That(simulation.ApplyMaintenance(0, MaintenanceChoice.Defer).Success, Is.True);
            simulation.Clock.SetSpeed(4);
            Start(simulation);
            Assert.That(simulation.Elapsed, Is.Zero);
            Assert.That(simulation.Clock.Speed, Is.EqualTo(1));
        }
    }
}
