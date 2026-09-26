using System;
using System.Linq;
using NUnit.Framework;

namespace WorstHotel.Tests
{
    /// <summary>Headless causal tests. Reception/route acknowledgements are explicit adapters; sources, keys and situations use production commands.</summary>
    public sealed class AgencySituationTests
    {
        sealed class Fixture
        {
            public HotelSimulation Hotel;
            public RoomState[] Rooms;
            public GuestStay Source => Hotel.Guests.Single(g => g.GuestId == "a-source");
            public GuestStay Affected => Hotel.Guests.Single(g => g.GuestId == "b-affected");
            public RoomState Room(int id) => Rooms.Single(r => r.Profile.Id == id);
            public HotelIncident Noise => Hotel.Incidents.Items.SingleOrDefault(i => i.GuestId == Affected.GuestId && i.Reason == IncidentReason.Noise);
        }

        static NeedSettings Tuning(int historyCapacity = 12) => new NeedSettings(buildupPerSecond: .5f,
            complaintExposureSeconds: 2, escalatedExposureSeconds: 6, criticalExposureSeconds: 12,
            recoverySeconds: 2, reopenCooldownSeconds: 2, compensationReliefSeconds: 5,
            repeatPatienceReduction: .25f, minimumRepeatPatienceMultiplier: .5f, historyCapacity: historyCapacity);

        static Fixture Create(float ambient = 0, NeedSettings needs = null, int sourceRoom = 103, NeedProfile affectedNeeds = null)
        {
            var budget = new GuestProfile(GuestKind.Budget, "Budget", "", 180, .85f, 18, .7f, 28, 90, .55f);
            var cold = new GuestProfile(GuestKind.ColdSensitive, "Cold", "", 300, 1.05f, 20, 1.35f, 16, 65, .4f);
            var business = new GuestProfile(GuestKind.Business, "Business", "", 450, 1, 19.5f, 1, 10, 45, .2f,
                needs: affectedNeeds ?? new NeedProfile(21, 25, 18, 28, .08f, .2f, 45));
            var settings = new SessionSettings(new[] { budget, cold, business },
                Enumerable.Range(101, 6).Select(id => new RoomProfile(id, "Room " + id, noise: ambient, temperature: 22.5f)),
                new BoilerSettings(), new EconomySettings(), serviceSeconds: 600);
            var rooms = settings.Rooms.Select(r => new RoomState(r)).ToArray();
            var living = new LivingHotelSettings(firstArrivalSeconds: .2f, arrivalSpacingSeconds: .2f, arrivalJitterSeconds: 0,
                firstActivityDelay: 1000, activityDurationMin: 120, activityDurationMax: 120, awayDurationMin: 60, awayDurationMax: 60);
            var hotel = new HotelSimulation(settings, rooms, living, needs ?? Tuning());
            var offers = new[] { new BookingApplication("a-source", "Noisy neighbour", budget, 180),
                new BookingApplication("b-affected", "Business guest", business, 450) };
            Assert.That(hotel.StartShift(new[] { new BookingAssignment(sourceRoom, offers[0].Id, 180, 0),
                new BookingAssignment(102, offers[1].Id, 450, 0) }, offers).Success, Is.True);
            hotel.Tick(.8f);
            foreach (var guest in hotel.Guests)
            {
                Assert.That(hotel.SignalGuestReachedReception(guest.GuestId).Success, Is.True);
                Assert.That(ModelKeyHandoff.CheckIn(hotel, 0, guest.GuestId).Success, Is.True);
                Assert.That(hotel.SignalGuestReachedRoom(guest.GuestId).Success, Is.True);
                Assert.That(hotel.ForceActivity(guest.GuestId, GuestActivity.QuietRest).Success, Is.True);
            }
            return new Fixture { Hotel = hotel, Rooms = rooms };
        }

        static void Advance(Fixture f, float seconds, bool cold = false)
        {
            while (seconds > .00001f)
            {
                foreach (var room in f.Rooms) room.Temperature = cold && room.Profile.Id == f.Affected.RoomId ? 5 : 22.5f;
                float step = Math.Min(.2f, seconds); f.Hotel.Tick(step); seconds -= step;
            }
        }
        static void Loud(Fixture f) => Assert.That(f.Hotel.ForceActivity(f.Source.GuestId, GuestActivity.LoudRoom).Success, Is.True);

        [Test]
        public void ProfileAmbientAndDebugOverrideCannotInventAnUnidentifiedNoiseComplaint()
        {
            var f = Create(.95f);
            Assert.That(f.Hotel.SetRoomNoise(102, 1).Success, Is.True);
            Advance(f, 20);
            Assert.That(f.Room(102).Noise, Is.EqualTo(1), "Override is still visible as a developer measurement.");
            Assert.That(f.Hotel.Noise.Sources, Is.Empty);
            Assert.That(f.Affected.Perception.Noise, Is.Zero);
            Assert.That(f.Affected.Needs.Noise.ExposureSeconds, Is.Zero);
            Assert.That(f.Hotel.Incidents.Items.Any(i => i.Reason == IncidentReason.Noise), Is.False);
        }

        [Test]
        public void ARealStagedSourceIsIdentifiedAndOnePersistentCaseEscalatesWithoutDuplicateNotifications()
        {
            var f = Create(); int notifications = 0;
            f.Hotel.Requests.OnRequestCreated += r => { if (r.Reason == IncidentReason.Noise) notifications++; };
            Loud(f); Advance(f, 3);
            var incident = f.Noise;
            Assert.That(incident.Stage, Is.EqualTo(SituationStage.Complaint));
            Assert.That(incident.Cause.SourceType, Is.EqualTo("Television"));
            Assert.That(incident.Cause.SourceGuestId, Is.EqualTo(f.Source.GuestId));
            Assert.That(incident.Cause.SourceRoomId, Is.EqualTo(103));
            Assert.That(incident.Cause.ReceivedIntensity, Is.EqualTo(f.Room(102).ReceivedNoise).Within(.0001f));
            Advance(f, 12);
            Assert.That(f.Noise, Is.SameAs(incident));
            Assert.That(incident.Stage, Is.EqualTo(SituationStage.Critical));
            Assert.That(notifications, Is.EqualTo(1));
            Assert.That(f.Hotel.Requests.Items.Count(r => r.Reason == IncidentReason.Noise), Is.EqualTo(1));
            Assert.That(f.Affected.Memory.NumberOfComplaints, Is.EqualTo(1));
            Assert.That(f.Affected.Memory.ProblemsIgnored, Is.EqualTo(1));
        }

        [Test]
        public void AGuestWalkingToTheirTelevisionCannotEmitBeforePhysicalStagingAcknowledgement()
        {
            var f = Create();
            Assert.That(f.Hotel.RegisterGuestPhysicalStaging(f.Source.GuestId).Success, Is.True);
            Loud(f); Advance(f, 4);
            Assert.That(f.Hotel.Noise.Sources, Is.Empty);
            Assert.That(f.Noise, Is.Null);
            Assert.That(f.Hotel.SignalGuestActivityReady(f.Source.GuestId, f.Source.Agent.State, GuestActivity.LoudRoom).Success, Is.True);
            Advance(f, 3);
            Assert.That(f.Noise, Is.Not.Null);
        }

        [Test]
        public void AskingSourceToQuietChangesOutputAndResolvesFromMeasuredRecovery()
        {
            var f = Create(); Loud(f); Advance(f, 3);
            float original = f.Room(103).SourceNoise;
            Assert.That(f.Hotel.RequestQuiet(17, f.Source.GuestId).Success, Is.True);
            Assert.That(f.Room(103).SourceNoise, Is.EqualTo(original * f.Hotel.NoiseSettings.QuietSourceMultiplier).Within(.0001f));
            Advance(f, f.Hotel.NeedsSettings.RecoverySeconds + .4f);
            Assert.That(f.Noise.Resolved, Is.True);
            Assert.That(f.Noise.ResponseAccepted, Is.False);
            Assert.That(f.Affected.Memory.ProblemsResolvedSuccessfully, Is.EqualTo(1));
            Assert.That(f.Source.Memory.PreviousNoiseWarnings, Is.EqualTo(1));
            Assert.That(f.Noise.History.Any(h => h.Reason.Contains("asked to keep it down")), Is.True);
        }

        [Test]
        public void CompensationLeavesSourceAndSameCaseActiveAndCannotCountAsSuccessfulRepair()
        {
            var f = Create(); Loud(f); Advance(f, 3);
            var incident = f.Noise; float output = f.Room(103).SourceNoise;
            float dissatisfaction = f.Affected.Needs.Noise.Dissatisfaction;
            Assert.That(f.Hotel.OfferCompensation(f.Affected.GuestId).Success, Is.True);
            Assert.That(f.Room(103).SourceNoise, Is.EqualTo(output));
            Assert.That(incident.Active && !incident.Resolved, Is.True);
            Assert.That(f.Affected.Needs.Noise.Dissatisfaction, Is.LessThan(dissatisfaction));
            Assert.That(f.Affected.Memory.CompensationReceived, Is.GreaterThan(0));
            Assert.That(f.Affected.Memory.ProblemsResolvedSuccessfully, Is.Zero);
            Advance(f, f.Hotel.NeedsSettings.CompensationReliefSeconds + 14);
            Assert.That(f.Noise, Is.SameAs(incident));
            Assert.That(f.Noise.Stage, Is.EqualTo(SituationStage.Critical));
            Assert.That(f.Hotel.Requests.Items.Count(r => r.Reason == IncidentReason.Noise), Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ExchangingKeysAndMovingEitherGuestChangesExposureWithoutQuestCompletion(bool moveSource)
        {
            var f = Create(sourceRoom: 101); Loud(f); Advance(f, 3);
            var incident = f.Noise;
            var moved = moveSource ? f.Source : f.Affected;
            int destination = moveSource ? 105 : 106;
            Assert.That(ModelKeyHandoff.MoveGuest(f.Hotel, 17, moved.GuestId, destination).Success, Is.True);
            Assert.That(f.Hotel.SignalGuestReachedRoom(moved.GuestId).Success, Is.True);
            if (moveSource) Loud(f);
            Advance(f, f.Hotel.NeedsSettings.RecoverySeconds + .4f);
            Assert.That(f.Room(f.Source.RoomId).SourceNoise, Is.GreaterThan(0));
            Assert.That(f.Affected.Perception.Noise, Is.Zero);
            Assert.That(incident.Resolved, Is.True);
            Assert.That(incident.ResponseAccepted, Is.False);
        }

        [Test]
        public void AwayAndReturningTravelDoNotAccumulateRoomNeedsButPhysicalReturnResumesPerception()
        {
            var f = Create(); Loud(f); Advance(f, 3, true);
            float noise = f.Affected.Needs.Noise.ExposureSeconds, temperature = f.Affected.Needs.Temperature.ExposureSeconds;
            Assert.That(f.Hotel.ForceLeaveRoom(f.Affected.GuestId).Success, Is.True);
            Assert.That(f.Hotel.SignalGuestLeftRoom(f.Affected.GuestId).Success, Is.True);
            Advance(f, 5, true);
            Assert.That(f.Affected.Needs.Noise.ExposureSeconds, Is.EqualTo(noise));
            Assert.That(f.Affected.Needs.Temperature.ExposureSeconds, Is.EqualTo(temperature));
            Assert.That(f.Affected.Perception.InAssignedRoom, Is.False);
            Assert.That(f.Hotel.ForceReturnRoom(f.Affected.GuestId).Success, Is.True);
            Advance(f, 2, true);
            Assert.That(f.Affected.Needs.Temperature.ExposureSeconds, Is.EqualTo(temperature));
            Assert.That(f.Hotel.SignalGuestReturnedRoom(f.Affected.GuestId).Success, Is.True);
            Advance(f, 1, true);
            Assert.That(f.Affected.Needs.Noise.ExposureSeconds, Is.GreaterThan(noise));
            Assert.That(f.Affected.Needs.Temperature.ExposureSeconds, Is.GreaterThan(temperature));
        }

        [Test]
        public void SecondEpisodeRemembersComplaintAndUsesConfiguredLowerPatienceAndShorterSourceAgreement()
        {
            var f = Create(); Loud(f); Advance(f, 3);
            var incident = f.Noise;
            float firstPatience = incident.Patience;
            Assert.That(f.Hotel.RequestQuiet(0, f.Source.GuestId).Success, Is.True);
            Advance(f, f.Hotel.NoiseSettings.QuietRequestSeconds + 3);
            Assert.That(f.Noise, Is.SameAs(incident));
            Assert.That(incident.EpisodeCount, Is.EqualTo(2));
            Assert.That(f.Affected.Memory.RepeatedProblemCount[IncidentReason.Noise], Is.EqualTo(1));
            Assert.That(incident.Patience, Is.EqualTo(firstPatience * (1 - f.Hotel.NeedsSettings.RepeatPatienceReduction)).Within(.001f));
            Assert.That(f.Hotel.RequestQuiet(0, f.Source.GuestId).Success, Is.True);
            Assert.That(f.Source.Memory.PreviousNoiseWarnings, Is.EqualTo(2));
            float secondAgreement = f.Source.Agent.QuietUntil - f.Hotel.Elapsed;
            Assert.That(secondAgreement, Is.EqualTo(f.Hotel.NoiseSettings.QuietRequestSeconds *
                (1 - f.Hotel.NoiseSettings.RepeatedWarningDurationReduction)).Within(.001f));
        }

        [Test]
        public void UnsupportedFixturePercentageCannotCreateConditionCaseButDirtyLinenHasConcreteCause()
        {
            var f = Create(); f.Room(102).RepairState = RepairState.Broken; Advance(f, 10);
            Assert.That(f.Affected.Needs.RoomCondition.Severity, Is.Zero);
            Assert.That(f.Hotel.Incidents.Items.Any(i => i.Reason == IncidentReason.RoomCondition), Is.False);
            f.Room(102).Cleanliness = Cleanliness.Dirty; Advance(f, 3);
            var incident = f.Hotel.Incidents.Items.Single(i => i.GuestId == f.Affected.GuestId && i.Reason == IncidentReason.RoomCondition);
            Assert.That(incident.Cause.SourceType, Is.EqualTo("Dirty linen"));
            Assert.That(incident.MeasuredCause, Does.Contain("bed has not been changed"));
            Assert.That(ModelKeyHandoff.MoveGuest(f.Hotel, 0, f.Affected.GuestId, 106).Success, Is.True);
            Assert.That(f.Hotel.SignalGuestReachedRoom(f.Affected.GuestId).Success, Is.True);
            Advance(f, 3);
            Assert.That(f.Room(102).Cleanliness, Is.EqualTo(Cleanliness.Dirty));
            Assert.That(incident.Resolved, Is.True);
            Assert.That(f.Affected.Needs.RoomCondition.Severity, Is.Zero);
        }

        [Test]
        public void IgnoringIsRememberedOnlyOnceAndHistoryRemainsConfiguredAndFinite()
        {
            var f = Create(needs: Tuning(4)); Loud(f); Advance(f, 3);
            Assert.That(f.Hotel.AcceptConsequences(0, f.Affected.GuestId).Success, Is.True);
            Assert.That(f.Affected.Memory.ProblemsIgnored, Is.EqualTo(1));
            Assert.That(f.Hotel.AcceptConsequences(0, f.Affected.GuestId).Success, Is.False);
            Advance(f, 15);
            Assert.That(f.Affected.Memory.ProblemsIgnored, Is.EqualTo(1));
            Assert.That(f.Noise.History.Count, Is.LessThanOrEqualTo(f.Hotel.NeedsSettings.HistoryCapacity));
            Assert.That(f.Noise.Active, Is.True);
        }

        [Test]
        public void SituationIdentityDependsOnAffectedGuestCategoryAndPhysicalSource()
        {
            var one = new SituationKey(IncidentReason.Noise, "guest", "source/TV");
            Assert.That(one, Is.EqualTo(new SituationKey(IncidentReason.Noise, "guest", "source/TV")));
            Assert.That(one, Is.Not.EqualTo(new SituationKey(IncidentReason.Noise, "other", "source/TV")));
            Assert.That(one, Is.Not.EqualTo(new SituationKey(IncidentReason.Noise, "guest", "source/Phone")));
            Assert.Throws<ArgumentException>(() => new SituationKey(IncidentReason.Noise, "guest", ""));
            Assert.Throws<ArgumentException>(() => new NeedSettings(minimumRepeatPatienceMultiplier: 0));
            Assert.Throws<ArgumentException>(() => new NoiseSettings(repeatedWarningDurationReduction: float.NaN));
        }

        [Test]
        public void CombinedQuietTelevisionsHaveIdentifiableCasesEvenWhenNeitherAloneCrossesTheComfortThreshold()
        {
            var f = Create(sourceRoom: 101, affectedNeeds: new NeedProfile(21, 25, 18, 28, .125f, .25f, 45));
            Assert.That(f.Hotel.ForceActivity(f.Source.GuestId, GuestActivity.WatchTV).Success, Is.True);
            var second = ModelNoiseSource.AddTelevision(f.Hotel, 104);
            Assert.That(f.Hotel.ForceActivity(second.GuestId, GuestActivity.WatchTV).Success, Is.True);
            var measured = f.Hotel.Noise.GetContributions(102);
            Assert.That(measured.Count, Is.EqualTo(2));
            Assert.That(measured.All(source => f.Hotel.NeedEvaluator.NoiseSeverity(source.ReceivedNoise,
                f.Affected.Application.Archetype.Needs) <= f.Hotel.NeedsSettings.RecoverySeverityThreshold), Is.True);
            Advance(f, 12);
            var cases = f.Hotel.Incidents.Items.Where(i => i.GuestId == f.Affected.GuestId && i.Reason == IncidentReason.Noise).ToArray();
            Assert.That(cases.Length, Is.EqualTo(2));
            Assert.That(cases.All(i => i.HasContactedStaff && i.Cause.SourceEntityId != null), Is.True);
            Assert.That(cases.Sum(i => i.Severity), Is.EqualTo(f.Affected.Needs.Noise.Severity).Within(.0001f));
            Assert.That(cases.Select(i => i.Cause.SourceGuestId), Is.EquivalentTo(new[] { f.Source.GuestId, second.GuestId }));
        }

        [Test]
        public void WeakPlumbingAccumulatesItsOwnShareInsteadOfBorrowingTheLoudTelevisionsDissatisfaction()
        {
            var f = Create(needs: new NeedSettings(complaintExposureSeconds: 20), sourceRoom: 104,
                affectedNeeds: new NeedProfile(21, 25, 18, 28, .125f, .25f, 45));
            Loud(f);
            var plumbing = ModelNoiseSource.AddTelevision(f.Hotel, 101);
            Assert.That(f.Hotel.ForceActivity(plumbing.GuestId, GuestActivity.Shower).Success, Is.True);
            Advance(f, 25);
            var cases = f.Hotel.Incidents.Items.Where(i => i.GuestId == f.Affected.GuestId && i.Reason == IncidentReason.Noise).ToArray();
            var loud = cases.Single(i => i.Cause.SourceGuestId == f.Source.GuestId);
            var weak = cases.Single(i => i.Cause.SourceGuestId == plumbing.GuestId);
            Assert.That(loud.HasContactedStaff, Is.True);
            Assert.That(weak.Stage, Is.EqualTo(SituationStage.Observed));
            Assert.That(weak.Dissatisfaction, Is.LessThan(f.Hotel.NeedsSettings.ComplaintDissatisfaction));
            Assert.That(weak.Dissatisfaction, Is.LessThan(loud.Dissatisfaction * .2f));
            float before = loud.Dissatisfaction;
            Assert.That(f.Hotel.OfferCompensation(f.Affected.GuestId).Success, Is.True);
            Assert.That(loud.Dissatisfaction, Is.LessThan(before), "Credit must relieve the per-source case as well as the aggregate need.");
            Assert.That(loud.Active, Is.True);
        }

        [Test]
        public void AResolvedSourcesCooldownElapsesWhileThatSourceIsCompletelyAbsent()
        {
            var f = Create(); Loud(f); Advance(f, 3);
            var incident = f.Noise;
            Assert.That(f.Hotel.ForceActivity(f.Source.GuestId, GuestActivity.QuietRest).Success, Is.True);
            Advance(f, f.Hotel.NeedsSettings.RecoverySeconds + f.Hotel.NeedsSettings.ReopenCooldownSeconds + 4);
            Assert.That(incident.Resolved, Is.True);
            Assert.That(f.Hotel.Noise.Sources, Is.Empty);
            Loud(f); Advance(f, .2f);
            Assert.That(incident.Active, Is.True, "An off source must not retain a frozen cooldown until it returns.");
            Assert.That(incident.EpisodeCount, Is.EqualTo(2));
            Assert.That(incident.Stage, Is.EqualTo(SituationStage.Observed));
            Assert.That(incident.HasContactedStaff, Is.False);
        }

        [Test]
        public void LeavingAnUnchangedNoisyRoomEndsExposureWithoutAwardingStaffSuccessfulRepairMemory()
        {
            var f = Create(); Loud(f); Advance(f, 3);
            Assert.That(f.Hotel.ForceLeaveRoom(f.Affected.GuestId).Success, Is.True);
            Assert.That(f.Hotel.SignalGuestLeftRoom(f.Affected.GuestId).Success, Is.True);
            Advance(f, f.Hotel.NeedsSettings.RecoverySeconds + .4f);
            Assert.That(f.Noise.Resolved, Is.True);
            Assert.That(f.Room(f.Source.RoomId).SourceNoise, Is.GreaterThan(0));
            Assert.That(f.Affected.Memory.ProblemsResolvedSuccessfully, Is.Zero);
            var receipt = f.Hotel.EndShift().Receipts.Single(item => item.GuestId == f.Affected.GuestId);
            Assert.That(receipt.Review, Does.Not.Contain("Staff improved"));
        }

        [Test]
        public void LateCompensationSuspendsOnlyExpiredServicePenaltyUntilItsFiniteGraceEnds()
        {
            var f = Create(); Loud(f);
            Advance(f, f.Affected.Application.Archetype.Needs.PatienceSeconds + f.Hotel.NeedsSettings.ComplaintExposureSeconds + 2);
            Assert.That(f.Hotel.Requests.HasExpiredRequest(f.Affected.GuestId), Is.True);
            Assert.That(f.Hotel.Requests.HasExpiredRoomRequest(f.Affected.GuestId), Is.True);
            float serviceIntegral = f.Affected.Needs.ServiceIntegral;
            float noiseExposure = f.Affected.Needs.Noise.ExposureSeconds;
            float output = f.Room(f.Source.RoomId).SourceNoise;
            Assert.That(f.Hotel.OfferCompensation(f.Affected.GuestId).Success, Is.True);
            Assert.That(f.Hotel.Requests.HasExpiredRequest(f.Affected.GuestId), Is.False);
            Assert.That(f.Hotel.Requests.HasExpiredRoomRequest(f.Affected.GuestId), Is.False);
            Advance(f, f.Hotel.NeedsSettings.CompensationReliefSeconds * .6f);
            Assert.That(f.Affected.Needs.ServiceIntegral, Is.EqualTo(serviceIntegral).Within(.0001f));
            Assert.That(f.Affected.Needs.Service.Severity, Is.Zero);
            Assert.That(f.Affected.Needs.Noise.ExposureSeconds, Is.GreaterThan(noiseExposure));
            Assert.That(f.Room(f.Source.RoomId).SourceNoise, Is.EqualTo(output));
            Assert.That(f.Noise.Active, Is.True);
            Advance(f, f.Hotel.NeedsSettings.CompensationReliefSeconds * .4f + .4f);
            Assert.That(f.Hotel.Requests.HasExpiredRoomRequest(f.Affected.GuestId), Is.True);
            Assert.That(f.Affected.Needs.ServiceIntegral, Is.GreaterThan(serviceIntegral));
        }
    }
}
