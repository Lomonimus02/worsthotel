using System;
using System.Linq;
using NUnit.Framework;

namespace WorstHotel.Tests
{
    public sealed class NeedsSituationTests
    {
        private SessionSettings settings;
        private NeedSettings needs;
        private LivingHotelSettings living;

        private sealed class StayFixture
        {
            public HotelSimulation Simulation;
            public RoomState Room;
            public GuestStay Guest;
        }

        [SetUp]
        public void Setup()
        {
            var profiles = new[]
            {
                new GuestProfile(GuestKind.Budget, "Budget", "", 180, 0.85f, 18, 0.7f, 28, 90, 0.55f),
                new GuestProfile(GuestKind.ColdSensitive, "Cold-sensitive", "", 300, 1.05f, 20, 1.35f, 16, 65, 0.4f),
                new GuestProfile(GuestKind.Business, "Business", "", 450, 1, 19.5f, 1, 10, 45, 0.25f)
            };
            settings = new SessionSettings(profiles,
                Enumerable.Range(101, 6).Select(id => new RoomProfile(id, "Room " + id, temperature: 22.5f)),
                new BoilerSettings(), new EconomySettings());
            needs = new NeedSettings();
            // Preserve production living mode while isolating need transitions from activity transitions.
            living = new LivingHotelSettings(firstArrivalSeconds: 0.2f, arrivalJitterSeconds: 0, firstActivityDelay: 1000,
                activityDurationMin: 120, activityDurationMax: 120);
        }

        private StayFixture Create(GuestKind kind = GuestKind.Budget, bool checkIn = true)
        {
            var rooms = settings.Rooms.Select(profile => new RoomState(profile)).ToArray();
            var simulation = new HotelSimulation(settings, rooms, living, needs);
            var offer = GuestSystem.GenerateApplications(1, settings.GuestArchetypes).First(candidate => candidate.Archetype.Kind == kind);
            Assert.That(simulation.StartShift(new[] { new BookingAssignment(101, offer.Id, offer.ReferencePrice, 0) }, new[] { offer }).Success, Is.True);
            var fixture = new StayFixture { Simulation = simulation, Room = rooms[0], Guest = simulation.Guests.Single() };
            TickFor(fixture, fixture.Guest.Agent.ArrivalTime + 0.2f);
            Assert.That(simulation.SignalGuestReachedReception(offer.Id).Success, Is.True);
            if (checkIn)
            {
                Assert.That(ModelKeyHandoff.CheckIn(simulation, 0, offer.Id).Success, Is.True);
                Assert.That(simulation.SignalGuestReachedRoom(offer.Id).Success, Is.True);
            }
            return fixture;
        }

        private static void TickFor(StayFixture fixture, float seconds, float? temperature = null, float? noise = null)
        {
            for (int step = 0; step < 2000 && seconds > 0.00001f; step++)
            {
                if (temperature.HasValue) fixture.Room.Temperature = temperature.Value;
                // Diagnostic displayed noise is deliberately not a causal source in Guest Agency.
                if (noise.HasValue) Assert.That(fixture.Simulation.SetRoomNoise(fixture.Room.Profile.Id, noise.Value).Success, Is.True);
                float delta = Math.Min(0.2f, seconds);
                fixture.Simulation.Tick(delta);
                seconds -= delta;
            }
            Assert.That(seconds, Is.LessThanOrEqualTo(0.00001f), "The bounded fixture failed to spend its requested hotel time.");
        }

        private static HotelIncident Situation(StayFixture fixture, IncidentReason reason) =>
            fixture.Simulation.Incidents.Items.SingleOrDefault(item => item.GuestId == fixture.Guest.GuestId && item.Reason == reason);

        private static HotelIncident ReachStage(StayFixture fixture, IncidentReason reason, SituationStage stage,
            float maximumSeconds, float? temperature = null)
        {
            for (int step = 0; step < maximumSeconds * 5 + 2; step++)
            {
                var existing = Situation(fixture, reason);
                if (existing != null && existing.Stage == stage) return existing;
                TickFor(fixture, 0.2f, temperature);
            }
            Assert.Fail("Guest never reached " + reason + " / " + stage + " in the bounded scenario.");
            return null;
        }

        [Test]
        public void ComfortableRoomHasFourQuietNeedDimensionsAndDoesNotInventSituations()
        {
            var fixture = Create();
            TickFor(fixture, 10, 22.5f, 0);
            var snapshot = fixture.Guest.Needs;
            Assert.That(snapshot, Is.Not.Null);
            Assert.That(snapshot.Temperature.Severity, Is.Zero);
            Assert.That(snapshot.Noise.Severity, Is.Zero);
            Assert.That(snapshot.RoomCondition.Severity, Is.Zero);
            Assert.That(snapshot.Service.Severity, Is.Zero);
            Assert.That(fixture.Simulation.Incidents.Items, Is.Empty);
            Assert.That(fixture.Simulation.Requests.Items, Is.Empty);
        }

        [Test]
        public void SameMeasuredTemperatureAndNoiseRespectDifferentGuestProfiles()
        {
            var budget = Create(GuestKind.Budget);
            var cold = Create(GuestKind.ColdSensitive);
            var business = Create(GuestKind.Business);
            foreach (var fixture in new[] { budget, cold, business })
            {
                fixture.Room.Temperature = 19;
                ModelNoiseSource.AddTelevision(fixture.Simulation, 103);
                fixture.Simulation.NeedEvaluator.Tick(fixture.Guest, fixture.Room, 1);
            }
            Assert.That(cold.Guest.Needs.Temperature.Severity, Is.GreaterThan(budget.Guest.Needs.Temperature.Severity));
            Assert.That(business.Guest.Needs.Noise.Severity, Is.GreaterThan(budget.Guest.Needs.Noise.Severity));
            Assert.That(cold.Guest.Needs.Temperature.Dissatisfaction, Is.GreaterThan(budget.Guest.Needs.Temperature.Dissatisfaction));
        }

        [Test]
        public void AllFourNeedsAccumulateFromTheirOwnMeasuredCauses()
        {
            var fixture = Create();
            fixture.Room.Temperature = 5;
            ModelNoiseSource.AddTelevision(fixture.Simulation, 103);
            fixture.Room.Cleanliness = Cleanliness.Dirty;
            fixture.Room.RepairState = RepairState.Broken;
            // Unit input represents an existing expired room response; it is not a fabricated quest.
            fixture.Simulation.NeedEvaluator.Tick(fixture.Guest, fixture.Room, 2, true);
            var snapshot = fixture.Guest.Needs;
            Assert.That(snapshot.Temperature.Severity, Is.GreaterThan(0));
            Assert.That(snapshot.Noise.Severity, Is.GreaterThan(0));
            Assert.That(snapshot.RoomCondition.Severity, Is.GreaterThan(0));
            Assert.That(snapshot.Service.Severity, Is.GreaterThan(0));
            Assert.That(snapshot.Temperature.ExposureSeconds, Is.EqualTo(2).Within(0.001f));
            Assert.That(snapshot.Noise.ExposureSeconds, Is.EqualTo(2).Within(0.001f));
            Assert.That(snapshot.RoomCondition.ExposureSeconds, Is.EqualTo(2).Within(0.001f));
            Assert.That(snapshot.Service.ExposureSeconds, Is.EqualTo(2).Within(0.001f));
            Assert.That(snapshot.CombinedRoomDeficit, Is.GreaterThan(0));
        }

        [Test]
        public void ShortSevereExposureIsObservedWithoutAnInstantComplaintOrRepeatedRequest()
        {
            var fixture = Create();
            TickFor(fixture, 0.2f, 5);
            var observed = Situation(fixture, IncidentReason.Temperature);
            Assert.That(observed, Is.Not.Null);
            Assert.That(observed.Stage, Is.EqualTo(SituationStage.Observed));
            Assert.That(fixture.Simulation.Requests.ActiveCount, Is.Zero);
            TickFor(fixture, needs.ComplaintExposureSeconds * 0.5f, 5);
            Assert.That(observed.Stage, Is.EqualTo(SituationStage.Observed));
            Assert.That(fixture.Simulation.Requests.Items, Is.Empty);
            Assert.That(fixture.Simulation.Incidents.Items.Count(item => item.Reason == IncidentReason.Temperature), Is.EqualTo(1));
        }

        [Test]
        public void SevereColdReachesComplaintBeforeMildColdThroughDissatisfactionAccumulation()
        {
            var severe = Create();
            var mild = Create();
            var severeIncident = ReachStage(severe, IncidentReason.Temperature, SituationStage.Complaint, 100, 5);
            var mildIncident = ReachStage(mild, IncidentReason.Temperature, SituationStage.Complaint, 160, 17.5f);
            Assert.That(severe.Simulation.Elapsed, Is.LessThan(mild.Simulation.Elapsed));
            Assert.That(severeIncident.ExposureSeconds, Is.GreaterThanOrEqualTo(needs.ComplaintExposureSeconds));
            Assert.That(mildIncident.Dissatisfaction, Is.GreaterThanOrEqualTo(needs.ComplaintDissatisfaction));
        }

        [Test]
        public void UnresolvedCauseEscalatesTheSameRequestThroughEveryStage()
        {
            var fixture = Create();
            var complaint = ReachStage(fixture, IncidentReason.Temperature, SituationStage.Complaint, 30, 5);
            var request = fixture.Simulation.Requests.Items.Single();
            int complaintRevision = fixture.Simulation.EventRevision;
            Assert.That(ReachStage(fixture, IncidentReason.Temperature, SituationStage.Escalated, 50, 5), Is.SameAs(complaint));
            Assert.That(fixture.Simulation.EventRevision, Is.GreaterThan(complaintRevision));
            int escalatedRevision = fixture.Simulation.EventRevision;
            Assert.That(ReachStage(fixture, IncidentReason.Temperature, SituationStage.Critical, 80, 5), Is.SameAs(complaint));
            Assert.That(fixture.Simulation.EventRevision, Is.GreaterThan(escalatedRevision));
            Assert.That(fixture.Simulation.Requests.Items.Count, Is.EqualTo(1));
            Assert.That(fixture.Simulation.Requests.Items.Single(), Is.SameAs(request));
            Assert.That(request.Stage, Is.EqualTo(SituationStage.Critical));
        }

        [Test]
        public void PhysicalRecoveryKeepsHistoryAndNewExposureCannotImmediatelyReopenAComplaint()
        {
            var fixture = Create();
            var incident = ReachStage(fixture, IncidentReason.Temperature, SituationStage.Critical, 90, 5);
            var request = fixture.Simulation.Requests.Items.Single();
            float oldLifetimeExposure = fixture.Guest.Needs.Temperature.ExposureSeconds;
            TickFor(fixture, needs.RecoverySeconds * 0.5f, 22.5f);
            Assert.That(request.Resolved, Is.False, "A single good reading must not erase a sustained problem.");
            TickFor(fixture, needs.RecoverySeconds * 0.5f + 0.4f, 22.5f);
            Assert.That(incident.Stage, Is.EqualTo(SituationStage.Resolved));
            Assert.That(request.Resolved, Is.True);
            Assert.That(fixture.Simulation.Incidents.Items.Single(), Is.SameAs(incident));
            Assert.That(fixture.Guest.Needs.Temperature.ExposureSeconds, Is.EqualTo(oldLifetimeExposure).Within(0.001f));
            TickFor(fixture, 1, 5);
            Assert.That(request.Resolved, Is.True, "Old dissatisfaction cannot bypass cooldown and a fresh episode's exposure gate.");
            var reopened = ReachStage(fixture, IncidentReason.Temperature, SituationStage.Complaint,
                needs.ReopenCooldownSeconds + needs.ComplaintExposureSeconds + 20, 5);
            Assert.That(reopened, Is.SameAs(incident));
            Assert.That(fixture.Simulation.Requests.Items.Single(), Is.SameAs(request));
            Assert.That(request.Resolved, Is.False);
            Assert.That(fixture.Guest.Needs.Temperature.ExposureSeconds, Is.GreaterThan(oldLifetimeExposure));
        }

        [Test]
        public void DirtyLinenProducesConcreteConditionSituationWhileUnsupportedBrokenFlagCannotKeepItOpen()
        {
            var fixture = Create();
            fixture.Room.Cleanliness = Cleanliness.Dirty;
            fixture.Room.RepairState = RepairState.Broken;
            ReachStage(fixture, IncidentReason.RoomCondition, SituationStage.Complaint, 100, 22.5f);
            Assert.That(fixture.Simulation.Requests.Items.Count, Is.EqualTo(1));
            Assert.That(fixture.Simulation.Requests.Items.Single().Reason, Is.EqualTo(IncidentReason.RoomCondition));
            fixture.Room.Cleanliness = Cleanliness.Clean;
            TickFor(fixture, needs.RecoverySeconds + 1, 22.5f);
            Assert.That(fixture.Simulation.Requests.Items.Single().Resolved, Is.True, "An unsupported broken-fixture flag cannot invent a remaining physical cause.");
            fixture.Room.RepairState = RepairState.Working;
            TickFor(fixture, needs.RecoverySeconds + 1, 22.5f);
            Assert.That(fixture.Simulation.Requests.Items.Single().Resolved, Is.True);
        }

        [Test]
        public void ReceptionDelayAffectsServiceHistoryWithoutInventingAFourthSituationFamily()
        {
            var fixture = Create(checkIn: false);
            TickFor(fixture, fixture.Guest.Application.Archetype.Needs.PatienceSeconds * living.WaitingPatienceMultiplier + 10);
            Assert.That(fixture.Guest.Needs.Service.Severity, Is.GreaterThan(0));
            Assert.That(fixture.Guest.Needs.ServiceIntegral, Is.GreaterThan(0));
            Assert.That(fixture.Simulation.Requests.Items, Is.Empty);
            Assert.That(fixture.Simulation.Requests.HasExpiredRoomRequest(fixture.Guest.GuestId), Is.False,
                "A service response cannot masquerade as the expired room complaint that caused it.");
            Assert.That(fixture.Guest.Needs.Temperature.ExposureSeconds + fixture.Guest.Needs.Noise.ExposureSeconds +
                fixture.Guest.Needs.RoomCondition.ExposureSeconds, Is.Zero);
            Assert.That(ModelKeyHandoff.CheckIn(fixture.Simulation, 0, fixture.Guest.GuestId).Success, Is.True);
            Assert.That(fixture.Simulation.SignalGuestReachedRoom(fixture.Guest.GuestId).Success, Is.True);
            TickFor(fixture, needs.RecoverySeconds + 1, 22.5f, 0);
            Assert.That(fixture.Simulation.Requests.Items, Is.Empty);
            Assert.That(fixture.Simulation.Requests.ActiveCount, Is.Zero);
            Assert.That(fixture.Guest.Needs.Service.Severity, Is.Zero);
            float serviceFraction = fixture.Guest.Needs.ServiceIntegral /
                Math.Max(1, fixture.Guest.Elapsed + fixture.Guest.CheckInWaitingSeconds);
            Assert.That(fixture.Guest.QualityIntegral, Is.Zero);
            Assert.That(fixture.Simulation.Satisfaction.Evaluate(fixture.Guest),
                Is.EqualTo(100 - settings.Economy.PatiencePenalty * serviceFraction).Within(0.001f),
                "The same reception wait must not be charged again by the legacy check-in-delay penalty.");
        }

        [Test]
        public void LivingQualityUsesTheMeasuredRoomDeficitOncePerSimulationSecond()
        {
            var fixture = Create();
            fixture.Room.Temperature = 5;
            fixture.Room.Noise = 1;
            fixture.Simulation.NeedEvaluator.Tick(fixture.Guest, fixture.Room, 1);
            float deficit = fixture.Guest.Needs.CombinedRoomDeficit;
            Assert.That(deficit, Is.GreaterThan(0));
            float qualityBefore = fixture.Guest.QualityIntegral;
            float elapsedBefore = fixture.Guest.Elapsed;
            fixture.Simulation.Satisfaction.AccumulateLiving(fixture.Guest, fixture.Room, 1);
            Assert.That(fixture.Guest.QualityIntegral - qualityBefore, Is.EqualTo(deficit).Within(0.001f),
                "Need accumulation and situation stages must not apply the legacy room deficit a second time.");
            Assert.That(fixture.Guest.Elapsed - elapsedBefore, Is.EqualTo(1).Within(0.001f));
            Assert.That(fixture.Guest.ExpiredComplaintSeconds, Is.Zero);
            qualityBefore = fixture.Guest.QualityIntegral;
            elapsedBefore = fixture.Guest.Elapsed;
            TickFor(fixture, 0.2f, 5, 1);
            Assert.That(fixture.Guest.QualityIntegral - qualityBefore,
                Is.EqualTo(fixture.Guest.Needs.CombinedRoomDeficit * 0.2f).Within(0.001f),
                "The complete hotel tick must also call exactly one quality accumulation path.");
            Assert.That(fixture.Guest.Elapsed - elapsedBefore, Is.EqualTo(0.2f).Within(0.001f));
        }

        [Test]
        public void RecoveryReducesDissatisfactionWithoutErasingCumulativeExposureOrMutatingAnEarlierSnapshot()
        {
            var fixture = Create();
            fixture.Room.Temperature = 5;
            fixture.Simulation.NeedEvaluator.Tick(fixture.Guest, fixture.Room, 10);
            var before = fixture.Guest.Needs.Temperature;
            float oldDissatisfaction = before.Dissatisfaction;
            fixture.Room.Temperature = 22.5f;
            fixture.Simulation.NeedEvaluator.Tick(fixture.Guest, fixture.Room, 1);
            Assert.That(fixture.Guest.Needs.Temperature.Severity, Is.Zero);
            Assert.That(fixture.Guest.Needs.Temperature.Dissatisfaction, Is.LessThan(oldDissatisfaction));
            Assert.That(fixture.Guest.Needs.Temperature.ExposureSeconds, Is.EqualTo(before.ExposureSeconds));
            Assert.That(before.Dissatisfaction, Is.EqualTo(oldDissatisfaction));
        }

        [Test]
        public void InvalidNeedProfilesThresholdOrderingAndNonFiniteTuningAreRejected()
        {
            Assert.Throws<ArgumentException>(() => new NeedProfile(23, 20, 18, 25, 0.1f, 0.5f, 90));
            Assert.Throws<ArgumentException>(() => new NeedProfile(20, 23, 21, 25, 0.1f, 0.5f, 90));
            Assert.Throws<ArgumentException>(() => new NeedProfile(20, 23, 18, 25, 0.7f, 0.5f, 90));
            Assert.Throws<ArgumentException>(() => new NeedProfile(20, 23, 18, 25, 0.1f, 0.5f, 0));
            Assert.Throws<ArgumentException>(() => new NeedSettings(temperatureSevereDelta: 0));
            Assert.Throws<ArgumentException>(() => new NeedSettings(buildupPerSecond: float.NaN));
            Assert.Throws<ArgumentException>(() => new NeedSettings(recoverySeconds: float.PositiveInfinity));
            Assert.Throws<ArgumentException>(() => new NeedSettings(complaintDissatisfaction: 0.8f, escalatedDissatisfaction: 0.6f));
            Assert.Throws<ArgumentException>(() => new NeedSettings(complaintExposureSeconds: 40, escalatedExposureSeconds: 30));
            Assert.Throws<ArgumentException>(() => new NeedSettings(reopenCooldownSeconds: -1));
        }
    }
}
