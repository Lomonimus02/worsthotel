using System;
using System.Linq;
using NUnit.Framework;

namespace WorstHotel.Tests
{
    /// <summary>Alternative responses observe the same living guest, needs and authoritative room state.</summary>
    public sealed class AlternativeSolutionsTests
    {
        private sealed class Fixture
        {
            public SessionSettings Settings;
            public HotelSimulation Simulation;
            public RoomState[] Rooms;
            public GuestStay Guest => Simulation.Guests[0];
            public RoomState Room(int id) => Rooms.Single(room => room.Profile.Id == id);
        }

        private static Fixture Create(int bookings = 1, int checkIns = 1, bool naturallyCold = false)
        {
            var profiles = new[]
            {
                new GuestProfile(GuestKind.Budget, "Budget", "", 180, 0.85f, 18, 0.7f, 28, 90, 0.55f),
                new GuestProfile(GuestKind.ColdSensitive, "Cold-sensitive", "", 300, 1.05f, 20, 1.35f, 16, 65, 0.4f),
                new GuestProfile(GuestKind.Business, "Business", "", 450, 1, 19.5f, 1, 10, 45, 0.25f)
            };
            float baseline = naturallyCold ? 9 : 22.5f;
            var settings = new SessionSettings(profiles,
                Enumerable.Range(101, 6).Select(id => new RoomProfile(id, "Room " + id, noise: 0, temperature: baseline)),
                new BoilerSettings(), new EconomySettings(), temperatureBase: baseline, heatTemperatureGain: 0,
                temperatureTimeConstant: naturallyCold ? 5 : 45);
            var rooms = settings.Rooms.Select(profile => new RoomState(profile)).ToArray();
            var simulation = new HotelSimulation(settings, rooms,
                new LivingHotelSettings(firstArrivalSeconds: 0.2f, arrivalJitterSeconds: 0, firstActivityDelay: 1000,
                    activityDurationMin: 120, activityDurationMax: 120),
                new NeedSettings(), new NoiseSettings(), new HeaterSettings());
            var fixture = new Fixture { Settings = settings, Rooms = rooms, Simulation = simulation };
            var offers = GuestSystem.GenerateApplications(1, settings.GuestArchetypes).Take(bookings).ToArray();
            Assert.That(simulation.StartShift(offers.Select((offer, index) =>
                new BookingAssignment(101 + index, offer.Id, offer.ReferencePrice, index % 2)), offers).Success, Is.True);
            for (int i = 0; i < checkIns; i++)
            {
                var guest = simulation.Guests[i];
                AdvanceTo(fixture, guest.Agent.ArrivalTime + 0.2f);
                Assert.That(simulation.SignalGuestReachedReception(guest.GuestId).Success, Is.True);
                Assert.That(ModelKeyHandoff.CheckIn(simulation, i % 2, guest.GuestId).Success, Is.True);
                Assert.That(simulation.SignalGuestReachedRoom(guest.GuestId).Success, Is.True);
            }
            return fixture;
        }

        private static void AdvanceTo(Fixture fixture, float until)
        {
            for (int i = 0; i < 2000 && fixture.Simulation.Elapsed + 0.00001f < until; i++)
                fixture.Simulation.Tick(Math.Min(0.2f, until - fixture.Simulation.Elapsed));
            Assert.That(fixture.Simulation.Elapsed, Is.EqualTo(until).Within(0.002f));
        }

        private static void TickFor(Fixture fixture, float seconds, float? temperature = null)
        {
            int steps = (int)Math.Ceiling(seconds / 0.2f);
            for (int i = 0; i < steps; i++)
            {
                if (temperature.HasValue) fixture.Room(fixture.Guest.RoomId).Temperature = temperature.Value;
                fixture.Simulation.Tick(Math.Min(0.2f, seconds - i * 0.2f));
            }
        }

        private static HotelRequest ColdComplaint(Fixture fixture)
        {
            for (int i = 0; i < 450; i++)
            {
                var request = fixture.Simulation.Requests.Items.SingleOrDefault(item =>
                    item.GuestId == fixture.Guest.GuestId && item.Reason == IncidentReason.Temperature && !item.Resolved);
                if (request != null) return request;
                TickFor(fixture, 0.2f, 5);
            }
            Assert.Fail("The bounded cold scenario did not produce its causal complaint.");
            return null;
        }

        [Test]
        public void RelocationAtomicallyFreesOldRoomAndReservesDestinationUntilPhysicalArrival()
        {
            var fixture = Create();
            var guest = fixture.Guest;
            fixture.Simulation.ForceActivity(guest.GuestId, GuestActivity.LoudRoom);
            var schedule = guest.Agent.Schedule;
            int revision = fixture.Simulation.EventRevision;
            Assert.That(ModelKeyHandoff.MoveGuest(fixture.Simulation, 1, guest.GuestId, 102).Success, Is.True);
            Assert.That(guest.RoomId, Is.EqualTo(102));
            Assert.That(guest.Agent.State, Is.EqualTo(GuestAgentState.GoingToRoom));
            Assert.That(guest.Agent.IsRelocating, Is.True);
            Assert.That(guest.Agent.TransferFromRoomId, Is.EqualTo(101));
            Assert.That(guest.Agent.HasReachedRoom, Is.True, "Previous real room time must remain billable history.");
            Assert.That(guest.Agent.Schedule, Is.SameAs(schedule));
            Assert.That(fixture.Room(101).Occupied || fixture.Room(101).Reserved, Is.False);
            Assert.That(fixture.Room(102).Occupied, Is.False);
            Assert.That(fixture.Room(102).ReservedGuestId, Is.EqualTo(guest.GuestId));
            Assert.That(fixture.Rooms.Count(room => room.Reserved), Is.EqualTo(1));
            Assert.That(fixture.Simulation.Boiler.Load, Is.Zero);
            Assert.That(fixture.Rooms.All(room => room.SourceNoise == 0), Is.True);
            Assert.That(fixture.Simulation.EventRevision, Is.GreaterThan(revision));
            ModelKeyHandoff.TakeKey(fixture.Simulation, 0, 103);
            Assert.That(fixture.Simulation.MoveGuest(0, guest.GuestId, 103).Success, Is.False, "A guest already in transfer cannot be redirected twice, even with another key.");
            Assert.That(fixture.Simulation.SignalGuestReachedRoom(guest.GuestId).Success, Is.True);
            Assert.That(fixture.Room(102).GuestId, Is.EqualTo(guest.GuestId));
            Assert.That(fixture.Room(102).Reserved, Is.False);
            Assert.That(guest.Agent.IsRelocating, Is.False);
            Assert.That(guest.Agent.TransferFromRoomId, Is.Null);
            Assert.That(fixture.Rooms.Count(room => room.Occupied), Is.EqualTo(1));
            Assert.That(fixture.Room(101).SourceNoise, Is.Zero);
            Assert.That(fixture.Room(102).SourceNoise, Is.Zero, "A relocated guest settles in quietly before starting an audible activity.");
            Assert.That(fixture.Simulation.ForceActivity(guest.GuestId, GuestActivity.WatchTV).Success, Is.True);
            Assert.That(fixture.Room(102).SourceNoise, Is.GreaterThan(0));
        }

        [Test]
        public void InvalidMovesCannotChangeAnyOccupancyReservationOrGuestState()
        {
            var fixture = Create(3, 2);
            fixture.Room(104).Cleanliness = Cleanliness.Dirty;
            string[] occupancy = fixture.Rooms.Select(room => room.GuestId).ToArray();
            string[] reservations = fixture.Rooms.Select(room => room.ReservedGuestId).ToArray();
            var state = fixture.Guest.Agent.State;
            foreach (var move in new[] { (-1, fixture.Guest.GuestId, 105), (-2, fixture.Guest.GuestId, 105),
                (0, "missing", 105), (0, fixture.Guest.GuestId, 101), (0, fixture.Guest.GuestId, 102),
                (0, fixture.Guest.GuestId, 103), (0, fixture.Guest.GuestId, 104), (0, fixture.Guest.GuestId, 999) })
            {
                var key = fixture.Simulation.Keys.Find(move.Item3);
                bool availableKey = move.Item1 >= 0 && key != null && key.Location == RoomKeyLocation.OnRack;
                if (availableKey) ModelKeyHandoff.TakeKey(fixture.Simulation, move.Item1, move.Item3);
                Assert.That(fixture.Simulation.MoveGuest(move.Item1, move.Item2, move.Item3).Success, Is.False);
                Assert.That(fixture.Rooms.Select(room => room.GuestId), Is.EqualTo(occupancy));
                Assert.That(fixture.Rooms.Select(room => room.ReservedGuestId), Is.EqualTo(reservations));
                Assert.That(fixture.Guest.RoomId, Is.EqualTo(101));
                Assert.That(fixture.Guest.Agent.State, Is.EqualTo(state));
                Assert.That(fixture.Guest.Agent.IsRelocating, Is.False);
                if (availableKey)
                {
                    Assert.That(key.PlayerId, Is.EqualTo(move.Item1), "A rejected move cannot consume the held key.");
                    Assert.That(fixture.Simulation.Keys.Drop(move.Item1, move.Item3).Success, Is.True);
                    Assert.That(fixture.Simulation.Keys.ReturnToRack(move.Item3).Success, Is.True);
                }
            }
            var future = fixture.Simulation.Guests[2];
            ModelKeyHandoff.TakeKey(fixture.Simulation, 0, 105);
            Assert.That(fixture.Simulation.MoveGuest(0, future.GuestId, 105).Success, Is.False,
                "A future reservation must not bypass its physical check-in path.");
        }

        [Test]
        public void TravelFreezesComplaintAndExposureUntilWarmDestinationActuallyRecovers()
        {
            var fixture = Create();
            var request = ColdComplaint(fixture);
            var situation = fixture.Simulation.Incidents.Items.Single(item => item.Id == request.Id);
            float exposure = fixture.Guest.Needs.Temperature.ExposureSeconds;
            float dissatisfaction = fixture.Guest.Needs.Temperature.Dissatisfaction;
            float quality = fixture.Guest.QualityIntegral, elapsed = fixture.Guest.Elapsed;
            float age = request.Age, stageAge = situation.StageAge;
            Assert.That(ModelKeyHandoff.MoveGuest(fixture.Simulation, 0, fixture.Guest.GuestId, 102).Success, Is.True);
            TickFor(fixture, fixture.Simulation.NeedsSettings.RecoverySeconds + 3, 22.5f);
            Assert.That(request.Resolved, Is.False, "Walking outside a room is not proof that the destination is comfortable.");
            Assert.That(request.Age, Is.EqualTo(age));
            Assert.That(situation.StageAge, Is.EqualTo(stageAge));
            Assert.That(fixture.Guest.Needs.Temperature.Severity, Is.Zero);
            Assert.That(fixture.Guest.Needs.Temperature.ExposureSeconds, Is.EqualTo(exposure));
            Assert.That(fixture.Guest.Needs.Temperature.Dissatisfaction, Is.EqualTo(dissatisfaction));
            Assert.That(fixture.Guest.QualityIntegral, Is.EqualTo(quality));
            Assert.That(fixture.Guest.Elapsed, Is.EqualTo(elapsed));
            Assert.That(fixture.Simulation.SignalGuestReachedRoom(fixture.Guest.GuestId).Success, Is.True);
            Assert.That(request.RoomId, Is.EqualTo(102));
            TickFor(fixture, fixture.Simulation.NeedsSettings.RecoverySeconds * 0.5f, 22.5f);
            Assert.That(request.Resolved, Is.False, "Recovery needs the actual sustained good-condition interval.");
            TickFor(fixture, fixture.Simulation.NeedsSettings.RecoverySeconds * 0.5f + 0.4f, 22.5f);
            Assert.That(request.Resolved, Is.True);
            Assert.That(fixture.Simulation.Requests.Items.Single(), Is.SameAs(request));
            Assert.That(fixture.Guest.Needs.Temperature.ExposureSeconds, Is.EqualTo(exposure));
        }

        [Test]
        public void MovingToAnotherColdRoomRetainsHistoryAndSeparatelyIdentifiesTheNewRoomCause()
        {
            var fixture = Create();
            var request = ColdComplaint(fixture);
            float exposure = fixture.Guest.Needs.Temperature.ExposureSeconds, age = request.Age;
            Assert.That(ModelKeyHandoff.MoveGuest(fixture.Simulation, 0, fixture.Guest.GuestId, 102).Success, Is.True);
            TickFor(fixture, 10, 5);
            Assert.That(fixture.Simulation.SignalGuestReachedRoom(fixture.Guest.GuestId).Success, Is.True);
            TickFor(fixture, 2, 5);
            Assert.That(request.Resolved, Is.False);
            Assert.That(request.RoomId, Is.EqualTo(102));
            Assert.That(request.Age, Is.EqualTo(age), "The original room source is recovering rather than continuing exposure remotely.");
            Assert.That(fixture.Guest.Needs.Temperature.ExposureSeconds, Is.EqualTo(exposure + 2).Within(0.002f));
            Assert.That(fixture.Guest.Needs.Temperature.Severity, Is.GreaterThan(0));
            Assert.That(fixture.Simulation.Requests.Items.Single(), Is.SameAs(request));
            var destinationCause = fixture.Simulation.Incidents.Items.Single(item => item.GuestId == fixture.Guest.GuestId &&
                item.Reason == IncidentReason.Temperature && item.Cause.SourceRoomId == 102);
            Assert.That(destinationCause.Id, Is.Not.EqualTo(request.Id));
            TickFor(fixture, fixture.Simulation.NeedsSettings.ComplaintExposureSeconds + 1, 5);
            Assert.That(request.Resolved, Is.True);
            Assert.That(destinationCause.Active, Is.True);
            Assert.That(destinationCause.Stage, Is.GreaterThanOrEqualTo(SituationStage.Complaint));
        }

        [Test]
        public void CheckoutDuringTransferReleasesDestinationWithoutErasingEarlierBillableStay()
        {
            var fixture = Create();
            TickFor(fixture, 2);
            Assert.That(ModelKeyHandoff.MoveGuest(fixture.Simulation, 0, fixture.Guest.GuestId, 102).Success, Is.True);
            AdvanceTo(fixture, fixture.Guest.Agent.CheckoutTime + 0.2f);
            Assert.That(fixture.Rooms.Any(room => room.Occupied || room.Reserved), Is.False);
            Assert.That(fixture.Rooms.All(room => room.SourceNoise == 0), Is.True);
            Assert.That(fixture.Simulation.SignalGuestReachedRoom(fixture.Guest.GuestId).Success, Is.False);
            var report = fixture.Simulation.EndShift();
            Assert.That(report.Receipts.Single().Price, Is.EqualTo(fixture.Guest.Price));
            Assert.That(report.Receipts.Count, Is.EqualTo(1));
        }

        [Test]
        public void CompensationGivesFiniteGraceWhileSameColdCaseRemainsActiveWithoutErasingHistoryOrAddingCredit()
        {
            var fixture = Create();
            var request = ColdComplaint(fixture);
            float quality = fixture.Guest.QualityIntegral;
            float exposure = fixture.Guest.Needs.Temperature.ExposureSeconds;
            float dissatisfaction = fixture.Guest.Needs.Temperature.Dissatisfaction;
            float severity = fixture.Guest.Needs.Temperature.Severity;
            float temperature = fixture.Room(101).Temperature;
            int cash = fixture.Simulation.Economy.Cash;
            Assert.That(fixture.Simulation.OfferCompensation(fixture.Guest.GuestId).Success, Is.True);
            int credit = fixture.Guest.CompensationCredit;
            Assert.That(credit, Is.GreaterThan(0));
            Assert.That(request.Resolved, Is.False);
            Assert.That(request.ResponseAccepted, Is.True);
            Assert.That(request.ResolutionReason, Is.Null, "Credit is not a physical recovery.");
            Assert.That(request.Compensated, Is.True);
            Assert.That(request.ResponseReliefRemainingSeconds, Is.EqualTo(fixture.Simulation.NeedsSettings.CompensationReliefSeconds));
            Assert.That(fixture.Guest.Needs.Temperature.Dissatisfaction,
                Is.EqualTo(Math.Max(0, dissatisfaction - fixture.Simulation.NeedsSettings.CompensationDissatisfactionReduction)).Within(.0001f));
            Assert.That(fixture.Guest.QualityIntegral, Is.EqualTo(quality));
            Assert.That(fixture.Guest.Needs.Temperature.ExposureSeconds, Is.EqualTo(exposure));
            Assert.That(fixture.Guest.Needs.Temperature.Severity, Is.EqualTo(severity));
            Assert.That(fixture.Room(101).Temperature, Is.EqualTo(temperature));
            Assert.That(fixture.Simulation.Economy.Cash, Is.EqualTo(cash), "The existing credit is charged at checkout, not twice.");
            Assert.That(fixture.Simulation.OfferCompensation(fixture.Guest.GuestId).Success, Is.False);
            Assert.That(fixture.Guest.CompensationCredit, Is.EqualTo(credit));
            TickFor(fixture, fixture.Simulation.NeedsSettings.CompensationReliefSeconds - .4f, 5);
            Assert.That(request.Resolved, Is.False, "The physical cause remains visible throughout compensation grace.");
            Assert.That(request.ResponseReliefRemainingSeconds, Is.EqualTo(.4f).Within(.002f));
            Assert.That(fixture.Simulation.Requests.ActiveCount, Is.EqualTo(1));
            TickFor(fixture, .6f, 5);
            var situation = fixture.Simulation.Incidents.Items.Single(item => item.Id == request.Id);
            Assert.That(situation.Stage, Is.GreaterThanOrEqualTo(SituationStage.Complaint));
            Assert.That(situation.ExposureSeconds, Is.GreaterThan(exposure), "The physical exposure continues during grace.");
            Assert.That(request.ResponseAccepted && request.AttentionAcknowledged, Is.True);
            Assert.That(request.ResponseReliefRemainingSeconds, Is.Zero);
            Assert.That(fixture.Simulation.Requests.ActiveCount, Is.EqualTo(1), "Expiry continues the same case instead of manufacturing a fresh one.");
            TickFor(fixture, fixture.Simulation.NeedsSettings.ComplaintExposureSeconds + .4f, 5);
            Assert.That(request.Resolved, Is.False, "The unchanged cold must become a complaint again after fresh exposure.");
            Assert.That(fixture.Simulation.Requests.Items.Single(item => item.Id == request.Id), Is.SameAs(request));
            Assert.That(fixture.Simulation.OfferCompensation(fixture.Guest.GuestId).Success, Is.False);
            Assert.That(fixture.Guest.CompensationCredit, Is.EqualTo(credit));
            Assert.That(fixture.Guest.QualityIntegral, Is.GreaterThan(quality));
            Assert.That(fixture.Guest.Needs.Temperature.ExposureSeconds, Is.GreaterThan(exposure));
            Assert.That(fixture.Guest.Needs.Temperature.Severity, Is.GreaterThan(0));
            var report = fixture.Simulation.EndShift();
            var receipt = report.Receipts.Single();
            Assert.That(receipt.Compensation, Is.GreaterThanOrEqualTo(credit).And.LessThanOrEqualTo(receipt.Price));
            Assert.That(receipt.Satisfaction, Is.LessThan(100));
            Assert.That(report.Cash, Is.EqualTo(cash + receipt.Price - receipt.Compensation - fixture.Settings.Economy.DailyOperatingCost));
            Assert.That(fixture.Simulation.EndShift(), Is.SameAs(report));
            Assert.That(fixture.Simulation.Economy.Cash, Is.EqualTo(report.Cash));
        }

        [Test]
        public void CompensationReliefFreezesDuringTransferAndClearsOnCheckout()
        {
            var fixture = Create();
            var request = ColdComplaint(fixture);
            Assert.That(fixture.Simulation.OfferCompensation(fixture.Guest.GuestId).Success, Is.True);
            TickFor(fixture, 3, 5);
            float remaining = request.ResponseReliefRemainingSeconds;
            float exposure = fixture.Guest.Needs.Temperature.ExposureSeconds;
            Assert.That(ModelKeyHandoff.MoveGuest(fixture.Simulation, 7, fixture.Guest.GuestId, 102).Success, Is.True);
            TickFor(fixture, 12, 5);
            Assert.That(request.ResponseReliefRemainingSeconds, Is.EqualTo(remaining));
            Assert.That(fixture.Guest.Needs.Temperature.ExposureSeconds, Is.EqualTo(exposure));
            Assert.That(!request.Resolved && request.ResponseAccepted, Is.True);
            Assert.That(fixture.Simulation.SignalGuestReachedRoom(fixture.Guest.GuestId).Success, Is.True);
            TickFor(fixture, 2, 5);
            Assert.That(request.ResponseReliefRemainingSeconds, Is.EqualTo(remaining).Within(.002f),
                "The old room's cause is recovering; new room cold is a separate physical source.");
            Assert.That(fixture.Simulation.DebugCheckoutGuest(fixture.Guest.GuestId).Success, Is.True);
            Assert.That(request.ResponseReliefRemainingSeconds, Is.Zero);
            Assert.That(fixture.Guest.Needs.Temperature.ExposureSeconds, Is.EqualTo(exposure + 2).Within(.002f));
        }

        [Test]
        public void ReliefDeduplicatesReasonsAndRejectsInvalidTuningWithoutRewritingMeasuredNeed()
        {
            var fixture = Create();
            ColdComplaint(fixture);
            TickFor(fixture, 25, 5);
            var before = fixture.Guest.Needs.Temperature;
            float quality = fixture.Guest.QualityIntegral;
            Assert.That(before.Dissatisfaction, Is.GreaterThan(.7f), "The fixture must distinguish one reduction from two.");
            fixture.Simulation.NeedEvaluator.ApplyCompensationRelief(fixture.Guest,
                new[] { IncidentReason.Temperature, IncidentReason.Temperature });
            var after = fixture.Guest.Needs.Temperature;
            Assert.That(after.Dissatisfaction,
                Is.EqualTo(before.Dissatisfaction - fixture.Simulation.NeedsSettings.CompensationDissatisfactionReduction).Within(.0001f));
            Assert.That(after.Severity, Is.EqualTo(before.Severity));
            Assert.That(after.ExposureSeconds, Is.EqualTo(before.ExposureSeconds));
            Assert.That(fixture.Guest.QualityIntegral, Is.EqualTo(quality));
            Assert.Throws<ArgumentException>(() => fixture.Simulation.NeedEvaluator.ApplyCompensationRelief(fixture.Guest,
                new[] { IncidentReason.Temperature, (IncidentReason)999 }));
            Assert.That(fixture.Guest.Needs.Temperature.Dissatisfaction, Is.EqualTo(after.Dissatisfaction));
            foreach (float invalid in new[] { 0, -1, float.NaN, float.PositiveInfinity })
                Assert.Throws<ArgumentException>(() => new NeedSettings(compensationReliefSeconds: invalid));
            foreach (float invalid in new[] { -.1f, 1.1f, float.NaN, float.PositiveInfinity })
                Assert.Throws<ArgumentException>(() => new NeedSettings(compensationDissatisfactionReduction: invalid));
        }

        [Test]
        public void CompensationForCurrentColdDoesNotPreAcceptADifferentLaterNoiseProblem()
        {
            var fixture = Create();
            var cold = ColdComplaint(fixture);
            Assert.That(fixture.Simulation.OfferCompensation(fixture.Guest.GuestId).Success, Is.True);
            int credit = fixture.Guest.CompensationCredit;
            ModelNoiseSource.AddTelevision(fixture.Simulation, 103);
            TickFor(fixture, 28, 5);
            var noise = fixture.Simulation.Requests.Items.Single(request => request.Reason == IncidentReason.Noise);
            Assert.That(!cold.Resolved && cold.ResponseAccepted, Is.True);
            Assert.That(noise.Resolved, Is.False);
            Assert.That(noise.ResponseAccepted, Is.False);
            Assert.That(noise.AttentionAcknowledged, Is.False);
            Assert.That(noise.Compensated, Is.True, "Credit metadata must not be confused with accepting this new situation.");
            Assert.That(fixture.Simulation.OfferCompensation(fixture.Guest.GuestId).Success, Is.False);
            Assert.That(fixture.Guest.CompensationCredit, Is.EqualTo(credit));
        }

        [Test]
        public void AcceptingConsequencesKeepsCauseActiveAndProducesWorseReceiptThanRestoringWarmth()
        {
            var ignored = Create(); var fixedRoom = Create();
            var ignoredRequest = ColdComplaint(ignored);
            ColdComplaint(fixedRoom);
            float exposure = ignored.Guest.Needs.Temperature.ExposureSeconds;
            Assert.That(ignored.Simulation.AcceptConsequences(0, ignored.Guest.GuestId).Success, Is.True);
            Assert.That(ignoredRequest.AttentionAcknowledged, Is.True);
            Assert.That(ignoredRequest.Resolved, Is.False);
            Assert.That(ignoredRequest.ResponseAccepted, Is.False);
            Assert.That(ignored.Guest.Compensated, Is.False);
            Assert.That(ignored.Guest.Needs.Temperature.ExposureSeconds, Is.EqualTo(exposure));
            TickFor(ignored, 70, 5); TickFor(fixedRoom, 70, 22.5f);
            Assert.That(ignoredRequest.Stage, Is.EqualTo(SituationStage.Critical));
            Assert.That(ignoredRequest.Resolved, Is.False);
            Assert.That(ignored.Guest.Needs.Temperature.ExposureSeconds, Is.GreaterThan(exposure));
            var ignoredReceipt = ignored.Simulation.EndShift().Receipts.Single();
            var fixedReceipt = fixedRoom.Simulation.EndShift().Receipts.Single();
            Assert.That(ignoredReceipt.Satisfaction, Is.LessThan(fixedReceipt.Satisfaction));
            Assert.That(ignoredReceipt.Compensation, Is.GreaterThan(fixedReceipt.Compensation));
            Assert.That(ignoredReceipt.Net, Is.LessThan(fixedReceipt.Net));
        }

        [Test]
        public void NewEpisodeAfterRealRecoveryRequiresFreshAttentionDespiteEarlierAcknowledgement()
        {
            var fixture = Create();
            Assert.That(fixture.Simulation.AcceptConsequences(0, fixture.Guest.GuestId).Success, Is.False);
            var request = ColdComplaint(fixture);
            Assert.That(fixture.Simulation.AcceptConsequences(-1, fixture.Guest.GuestId).Success, Is.False);
            Assert.That(fixture.Simulation.AcceptConsequences(0, "missing").Success, Is.False);
            Assert.That(fixture.Simulation.AcceptConsequences(1, fixture.Guest.GuestId).Success, Is.True);
            TickFor(fixture, fixture.Simulation.NeedsSettings.RecoverySeconds + 0.4f, 22.5f);
            Assert.That(request.Resolved, Is.True);
            var reopened = ColdComplaint(fixture);
            Assert.That(reopened, Is.SameAs(request));
            Assert.That(reopened.AttentionAcknowledged, Is.False);
            Assert.That(reopened.ResponseAccepted, Is.False);
        }

        [Test]
        public void HeaterIdentityPlacementSwitchAndPowerProduceUniqueDemandWithoutDuplicateConsumers()
        {
            var heaters = new HeaterSystem(new HeaterSettings(), Enumerable.Range(101, 6));
            Assert.That(heaters.Register("portable-1").Success, Is.True);
            Assert.That(heaters.Register("portable-1").Success, Is.False);
            Assert.That(heaters.Items.Count, Is.EqualTo(1));
            var state = heaters.Find("portable-1");
            Assert.That(state, Is.Not.Null);
            Assert.That(state.RoomId, Is.Null);
            Assert.That(heaters.SetSwitchedOn(state.Id, true).Success, Is.True);
            Assert.That(state.EffectiveHeatOutput + state.DemandedElectricalLoad, Is.Zero);
            Assert.That(heaters.AssignRoom(state.Id, 101).Success, Is.True);
            Assert.That(heaters.HeatForRoom(101), Is.EqualTo(state.Settings.HeatOutput));
            Assert.That(heaters.DemandForRoom(101), Is.EqualTo(state.Settings.ElectricalLoad));
            Assert.That(heaters.SetSwitchedOn(state.Id, true).Success, Is.True);
            Assert.That(heaters.DemandForRoom(101), Is.EqualTo(state.Settings.ElectricalLoad));
            Assert.That(heaters.SetPowered(state.Id, false).Success, Is.True);
            Assert.That(state.EffectiveHeatOutput, Is.Zero);
            Assert.That(state.DemandedElectricalLoad, Is.EqualTo(state.Settings.ElectricalLoad),
                "A tripped circuit must not erase demanded load and conceal the cause of retripping.");
            Assert.That(heaters.AssignRoom(state.Id, 102).Success, Is.True);
            Assert.That(heaters.DemandForRoom(101), Is.Zero);
            Assert.That(heaters.DemandForRoom(102), Is.EqualTo(state.Settings.ElectricalLoad));
            Assert.That(heaters.AssignRoom(state.Id, 999).Success, Is.False);
            Assert.That(state.RoomId, Is.EqualTo(102));
            Assert.That(heaters.SetPowered(state.Id, true).Success, Is.True);
            Assert.That(heaters.HeatForRoom(102), Is.EqualTo(state.Settings.HeatOutput));
            Assert.That(heaters.SetSwitchedOn(state.Id, false).Success, Is.True);
            Assert.That(state.EffectiveHeatOutput + state.DemandedElectricalLoad, Is.Zero);
            Assert.That(heaters.SetSwitchedOn(state.Id, true).Success, Is.True);
            Assert.That(heaters.AssignRoom(state.Id, null).Success, Is.True);
            Assert.That(state.EffectiveHeatOutput + state.DemandedElectricalLoad, Is.Zero);
            Assert.That(heaters.Unregister(state.Id).Success, Is.True);
            Assert.That(heaters.Items, Is.Empty);
            Assert.That(heaters.SetPowered(state.Id, true).Success, Is.False);
            Assert.That(heaters.HeatForRoom(102) + heaters.DemandForRoom(102), Is.Zero);
        }

        [Test]
        public void PoweredLocalHeaterActuallyWarmsItsRoomAndResolvesColdWhileSwitchedOffPeerDoesNot()
        {
            var warmed = Create(naturallyCold: true); var control = Create(naturallyCold: true);
            var warmRequest = ColdComplaint(warmed); var controlRequest = ColdComplaint(control);
            foreach (var fixture in new[] { warmed, control })
            {
                Assert.That(fixture.Simulation.Heaters.Register("heater").Success, Is.True);
                Assert.That(fixture.Simulation.Heaters.AssignRoom("heater", 101).Success, Is.True);
            }
            Assert.That(warmed.Simulation.Heaters.SetSwitchedOn("heater", true).Success, Is.True);
            float initial = warmed.Room(101).Temperature;
            TickFor(warmed, 28); TickFor(control, 28);
            Assert.That(warmed.Room(101).Temperature, Is.GreaterThan(initial));
            Assert.That(warmed.Room(101).Temperature, Is.GreaterThan(control.Room(101).Temperature));
            Assert.That(warmed.Room(102).Temperature, Is.EqualTo(control.Room(102).Temperature).Within(0.0001f),
                "A portable heater affects its placed room, not the entire central plant.");
            Assert.That(warmed.Simulation.Boiler.Load, Is.EqualTo(control.Simulation.Boiler.Load).Within(0.0001f));
            Assert.That(warmRequest.Resolved, Is.True);
            Assert.That(controlRequest.Resolved, Is.False);
            Assert.That(warmed.Guest.QualityIntegral, Is.LessThan(control.Guest.QualityIntegral));
            float beforePowerLoss = warmed.Room(101).Temperature;
            // Power delivery is now owned by the actual circuit, not a writable heater fixture flag.
            Assert.That(warmed.Simulation.DebugTripCircuit("A").Success, Is.True);
            TickFor(warmed, 8);
            Assert.That(warmed.Room(101).Temperature, Is.LessThan(beforePowerLoss));
            Assert.That(warmed.Simulation.Heaters.HeatForRoom(101), Is.Zero);
            Assert.That(warmed.Simulation.Heaters.DemandForRoom(101), Is.GreaterThan(0));
            Assert.That(warmed.Simulation.ResetCircuit(0, "A").Success, Is.True);
            Assert.That(warmed.Simulation.Heaters.HeatForRoom(101), Is.GreaterThan(0));
            warmed.Simulation.EndShift();
            var heater = warmed.Simulation.Heaters.Find("heater");
            Assert.That(heater.SwitchedOn, Is.False, "Closing the shift must explicitly switch portable heaters off.");
            Assert.That(heater.RoomId, Is.EqualTo(101), "Closing policy does not teleport the physical tool.");
            Assert.That(heater.EffectiveHeatOutput + heater.DemandedElectricalLoad, Is.Zero);
            Assert.That(warmed.Simulation.ApplyMaintenance(0, MaintenanceChoice.Defer).Success, Is.True);
            Assert.That(warmed.Room(101).Temperature, Is.EqualTo(warmed.Room(102).Temperature).Within(0.001f),
                "The overnight pass must not silently keep this switched-off portable heater warming a room.");
            var next = GuestSystem.GenerateApplications(2, warmed.Settings.GuestArchetypes).First();
            Assert.That(warmed.Simulation.StartShift(new[] { new BookingAssignment(101, next.Id, next.ReferencePrice, 0) }, new[] { next }).Success, Is.True);
            Assert.That(heater.SwitchedOn, Is.False, "The new day must not reactivate last night's heater silently.");
            Assert.That(heater.DemandedElectricalLoad, Is.Zero);
        }

        [Test]
        public void InvalidHeaterSettingsAndUnknownConsumerCommandsCannotCreatePhantomLoad()
        {
            Assert.Throws<ArgumentException>(() => new HeaterSettings(heatOutput: float.NaN));
            Assert.Throws<ArgumentException>(() => new HeaterSettings(heatOutput: 0));
            Assert.Throws<ArgumentException>(() => new HeaterSettings(electricalLoad: -1));
            Assert.Throws<ArgumentException>(() => new HeaterSettings(electricalLoad: float.PositiveInfinity));
            Assert.Throws<ArgumentException>(() => new HeaterSystem(new HeaterSettings(), new[] { 101, 101 }));
            var heaters = new HeaterSystem(new HeaterSettings(), Enumerable.Range(101, 6));
            Assert.That(heaters.Register(null).Success, Is.False);
            Assert.That(heaters.Register(" ").Success, Is.False);
            Assert.That(heaters.AssignRoom("missing", 101).Success, Is.False);
            Assert.That(heaters.SetSwitchedOn("missing", true).Success, Is.False);
            Assert.That(heaters.SetPowered("missing", true).Success, Is.False);
            Assert.That(heaters.Unregister("missing").Success, Is.False);
            Assert.That(heaters.Items, Is.Empty);
            Assert.That(Enumerable.Range(101, 6).Sum(heaters.DemandForRoom), Is.Zero);
        }
    }
}
