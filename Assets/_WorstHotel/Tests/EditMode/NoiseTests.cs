using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;

namespace WorstHotel.Tests
{
    public sealed class NoiseTests
    {
        private sealed class Fixture
        {
            public HotelSimulation Simulation;
            public RoomState[] Rooms;
            public RoomState Room(int id) => Rooms.Single(room => room.Profile.Id == id);
            public GuestStay Guest(int room) => Simulation.Guests.Single(guest => guest.RoomId == room);
        }

        private static SessionSettings Settings(float ambient = 0.1f) => new SessionSettings(new[]
        {
            new GuestProfile(GuestKind.Budget, "Budget", "", 180, 0.85f, 18, 0.7f, 28, 90, 0.55f),
            new GuestProfile(GuestKind.ColdSensitive, "Cold-sensitive", "", 300, 1.05f, 20, 1.35f, 16, 65, 0.4f),
            new GuestProfile(GuestKind.Business, "Business", "", 450, 1, 19.5f, 1, 10, 45, 0.25f)
        }, Enumerable.Range(101, 6).Select(id => new RoomProfile(id, "Room " + id, noise: ambient, temperature: 22.5f)),
            new BoilerSettings(), new EconomySettings());

        private static Fixture Create(SessionSettings settings = null, LivingHotelSettings living = null,
            NeedSettings needs = null, NoiseSettings noise = null)
        {
            settings = settings ?? Settings();
            var rooms = settings.Rooms.Select(profile => new RoomState(profile)).ToArray();
            return new Fixture { Rooms = rooms, Simulation = new HotelSimulation(settings, rooms,
                living ?? new LivingHotelSettings(firstArrivalSeconds: 0.2f, arrivalJitterSeconds: 0,
                    firstActivityDelay: 1000, activityDurationMin: 40, activityDurationMax: 40),
                needs ?? new NeedSettings(), noise ?? new NoiseSettings()) };
        }

        private static void Book(Fixture fixture, SessionSettings settings, params (int room, int offer)[] bookings)
        {
            var offers = GuestSystem.GenerateApplications(1, settings.GuestArchetypes);
            var chosen = bookings.Select(pair => offers[pair.offer]).ToArray();
            Assert.That(fixture.Simulation.StartShift(bookings.Select((pair, index) => new BookingAssignment(pair.room,
                chosen[index].Id, chosen[index].ReferencePrice, index % 2)), chosen).Success, Is.True);
        }

        private static void AdvanceTo(Fixture fixture, float time, bool receiveArrivals = false)
        {
            for (int step = 0; step < 2000 && fixture.Simulation.Elapsed + 0.00001f < time; step++)
            {
                fixture.Simulation.Tick(Math.Min(0.2f, time - fixture.Simulation.Elapsed));
                CompleteTemporaryTripBoundaries(fixture);
                if (receiveArrivals) ReceiveArrivals(fixture);
            }
            Assert.That(fixture.Simulation.Elapsed, Is.EqualTo(time).Within(0.002f));
        }

        private static void ReceiveArrivals(Fixture fixture)
        {
            CompleteTemporaryTripBoundaries(fixture);
            foreach (var guest in fixture.Simulation.Guests)
            {
                if (guest.Agent.State != GuestAgentState.Arriving) continue;
                Assert.That(fixture.Simulation.SignalGuestReachedReception(guest.GuestId).Success, Is.True);
                Assert.That(ModelKeyHandoff.CheckIn(fixture.Simulation, 0, guest.GuestId).Success, Is.True);
                Assert.That(fixture.Simulation.SignalGuestReachedRoom(guest.GuestId).Success, Is.True);
            }
        }

        // This suite isolates acoustic propagation; rendered route durations belong to PlayMode.
        private static void CompleteTemporaryTripBoundaries(Fixture fixture)
        {
            foreach (var guest in fixture.Simulation.Guests)
            {
                if (guest.Agent.State == GuestAgentState.LeavingRoom)
                    Assert.That(fixture.Simulation.SignalGuestLeftRoom(guest.GuestId).Success, Is.True);
                if (guest.Agent.State == GuestAgentState.ReturningToRoom)
                    Assert.That(fixture.Simulation.SignalGuestReturnedRoom(guest.GuestId).Success, Is.True);
            }
        }

        private static void ReceiveAll(Fixture fixture) =>
            AdvanceTo(fixture, fixture.Simulation.Guests.Max(guest => guest.Agent.ArrivalTime) + 0.4f, true);

        [Test]
        public void AuthoredGraphDistinguishesSharedWallsFromCorridorLinksAndRejectsAmbiguousEdges()
        {
            var graph = RoomAdjacencyGraph.Prototype();
            Assert.That(graph.RoomIds, Is.EqualTo(Enumerable.Range(101, 6).ToArray()));
            Assert.That(graph.Links.Count(link => link.Kind == RoomNoiseLinkKind.SharedWall), Is.EqualTo(4));
            Assert.That(graph.Links.Count(link => link.Kind == RoomNoiseLinkKind.Corridor), Is.EqualTo(7));
            Assert.That(graph.Links.Single(link => link.RoomA == 102 && link.RoomB == 103).Kind,
                Is.EqualTo(RoomNoiseLinkKind.Corridor));
            Assert.That(graph.Links.Any(link => link.RoomA == 102 && link.RoomB == 105), Is.False);
            Assert.Throws<ArgumentException>(() => new RoomNoiseLink(101, 101, RoomNoiseLinkKind.SharedWall));
            Assert.Throws<ArgumentException>(() => new RoomAdjacencyGraph(new[] { 101, 102 }, new[]
            {
                new RoomNoiseLink(101, 102, RoomNoiseLinkKind.SharedWall),
                new RoomNoiseLink(102, 101, RoomNoiseLinkKind.Corridor)
            }));
            Assert.Throws<ArgumentException>(() => new RoomAdjacencyGraph(new[] { 101 }, new[]
                { new RoomNoiseLink(101, 102, RoomNoiseLinkKind.Corridor) }));
        }

        [Test]
        public void ActualLoudActivityHasDirectAttenuatedNeighborsAndCannotComplainAboutItsOwnSource()
        {
            var settings = Settings(0);
            var fixture = Create(settings);
            Book(fixture, settings, (101, 0));
            ReceiveAll(fixture);
            var guest = fixture.Guest(101);
            Assert.That(fixture.Simulation.ForceActivity(guest.GuestId, GuestActivity.LoudRoom).Success, Is.True);
            float output = fixture.Simulation.LivingSettings.LoudNoiseOutput;
            Assert.That(fixture.Room(101).SourceNoise, Is.EqualTo(output).Within(0.0001f));
            Assert.That(fixture.Room(103).ReceivedNoise, Is.EqualTo(output * 0.7f).Within(0.0001f));
            Assert.That(fixture.Room(102).ReceivedNoise, Is.EqualTo(output * 0.3f).Within(0.0001f));
            Assert.That(fixture.Room(104).ReceivedNoise, Is.EqualTo(output * 0.3f).Within(0.0001f));
            Assert.That(fixture.Room(105).ReceivedNoise, Is.Zero, "Received sound must not retransmit through room 103.");
            Assert.That(fixture.Room(101).ReceivedNoise, Is.Zero);
            Assert.That(fixture.Room(101).Noise, Is.Zero, "The source is not its own external disturbance.");
            AdvanceTo(fixture, fixture.Simulation.Elapsed + 30);
            Assert.That(guest.Needs.Noise.ExposureSeconds, Is.Zero);
            Assert.That(fixture.Simulation.Requests.Items.Any(request => request.Reason == IncidentReason.Noise), Is.False);
        }

        [Test]
        public void EqualReceivedSoundProducesDifferentNeedsForBusinessAndBudgetGuests()
        {
            var settings = Settings();
            var fixture = Create(settings);
            Book(fixture, settings, (103, 0), (104, 1), (102, 3));
            ReceiveAll(fixture);
            Assert.That(fixture.Simulation.ForceActivity(fixture.Guest(103).GuestId, GuestActivity.LoudRoom).Success, Is.True);
            fixture.Simulation.Tick(0.2f);
            Assert.That(fixture.Room(102).Noise, Is.EqualTo(fixture.Room(104).Noise).Within(0.0001f));
            Assert.That(fixture.Guest(102).Needs.Noise.Severity, Is.GreaterThan(fixture.Guest(104).Needs.Noise.Severity));
            Assert.That(fixture.Guest(102).Needs.Noise.Dissatisfaction, Is.GreaterThan(fixture.Guest(104).Needs.Noise.Dissatisfaction));
        }

        [Test]
        public void SameSeedNormalSchedulesCreateBusinessNoiseComplaintOnlyBesideTheNoisyBooking()
        {
            const string directory = "Assets/_WorstHotel/ScriptableObjects/";
            var sessionAsset = AssetDatabase.LoadAssetAtPath<SessionConfig>(directory + "PrototypeSession.asset");
            var livingAsset = AssetDatabase.LoadAssetAtPath<LivingHotelConfig>(directory + "LivingHotel.asset");
            var needsAsset = AssetDatabase.LoadAssetAtPath<NeedConfig>(directory + "GuestNeeds.asset");
            var noiseGuids = AssetDatabase.FindAssets("t:NoiseConfig", new[] { directory.TrimEnd('/') });
            Assert.That(sessionAsset, Is.Not.Null);
            Assert.That(livingAsset, Is.Not.Null);
            Assert.That(needsAsset, Is.Not.Null);
            Assert.That(noiseGuids.Length, Is.EqualTo(1), "Build the authored noise configuration before this acceptance test.");
            var noiseAsset = AssetDatabase.LoadAssetAtPath<NoiseConfig>(AssetDatabase.GUIDToAssetPath(noiseGuids[0]));
            var settings = sessionAsset.ToData();
            var adjacent = Create(settings, livingAsset.ToData(), needsAsset.ToData(), noiseAsset.ToData());
            var separated = Create(settings, livingAsset.ToData(), needsAsset.ToData(), noiseAsset.ToData());
            // 104 shares a wall with 102; 105 has no acoustic link to 102.
            // A corridor neighbour can stay below complaint duration with the new quiet-life pacing.
            // The second offered budget guest's seeded TV window overlaps the business
            // guest's time inside; the first budget guest's TV overlaps their excursion.
            Book(adjacent, settings, (104, 1), (102, 3));
            Book(separated, settings, (105, 1), (102, 3));
            var source = adjacent.Guest(104);
            var separatedSource = separated.Guest(105);
            Assert.That(source.Agent.ArrivalTime, Is.EqualTo(separatedSource.Agent.ArrivalTime));
            Assert.That(source.Agent.Schedule.Activities.Select(entry => entry.Activity),
                Is.EqualTo(separatedSource.Agent.Schedule.Activities.Select(entry => entry.Activity)));
            Assert.That(source.Agent.Schedule.Activities.Select(entry => entry.Duration),
                Is.EqualTo(separatedSource.Agent.Schedule.Activities.Select(entry => entry.Duration)));
            bool hadLoudActivity = false;
            for (int step = 0; step < 1600 && !adjacent.Simulation.IsServiceComplete; step++)
            {
                adjacent.Simulation.Tick(0.2f); separated.Simulation.Tick(0.2f);
                ReceiveArrivals(adjacent); ReceiveArrivals(separated);
                hadLoudActivity |= source.Agent.InAssignedRoom && source.Agent.Activity == GuestActivity.LoudRoom;
            }
            Assert.That(adjacent.Simulation.IsServiceComplete && separated.Simulation.IsServiceComplete, Is.True);
            Assert.That(hadLoudActivity, Is.True, "The normal seeded schedule must actually contain a noisy activity.");
            string businessId = adjacent.Guest(102).GuestId;
            Assert.That(adjacent.Simulation.Requests.Items.Any(request => request.GuestId == businessId && request.Reason == IncidentReason.Noise), Is.True);
            Assert.That(separated.Simulation.Requests.Items.Any(request => request.GuestId == businessId && request.Reason == IncidentReason.Noise), Is.False);
            Assert.That(adjacent.Guest(102).NoiseExposureSeconds, Is.GreaterThan(separated.Guest(102).NoiseExposureSeconds));
            Assert.That(adjacent.Rooms.Concat(separated.Rooms).All(room => !room.Occupied), Is.True);
            TestContext.WriteLine("Normal seed " + livingAsset.seed + ": Business 102 noise exposure with Budget 104 = " +
                adjacent.Guest(102).NoiseExposureSeconds + "; with Budget 105 = " + separated.Guest(102).NoiseExposureSeconds +
                ". No forced activities, noise override, automatic quiet request or changed guest schedule.");
        }

        [Test]
        public void ExplicitNoiseOverrideIsAtomicBoundedAndCanReturnToMeasuredSources()
        {
            var settings = Settings();
            var fixture = Create(settings);
            Book(fixture, settings, (103, 0));
            ReceiveAll(fixture);
            fixture.Simulation.ForceActivity(fixture.Guest(103).GuestId, GuestActivity.LoudRoom);
            float measured = fixture.Room(102).Noise;
            float received = fixture.Room(102).ReceivedNoise;
            Assert.That(fixture.Simulation.SetRoomNoise(102, 0.9f).Success, Is.True);
            fixture.Simulation.Tick(0.2f);
            Assert.That(fixture.Room(102).Noise, Is.EqualTo(0.9f));
            Assert.That(fixture.Room(102).ReceivedNoise, Is.EqualTo(received));
            foreach (float invalid in new[] { -0.1f, 1.1f, float.NaN, float.NegativeInfinity, float.PositiveInfinity })
                Assert.That(fixture.Simulation.Noise.SetNoiseOverride(102, invalid).Success, Is.False);
            Assert.That(fixture.Simulation.Noise.SetNoiseOverride(999, 0.5f).Success, Is.False);
            Assert.That(fixture.Simulation.Noise.GetNoiseOverride(102), Is.EqualTo(0.9f));
            Assert.That(fixture.Simulation.ClearRoomNoiseOverride(102).Success, Is.True);
            Assert.That(fixture.Room(102).Noise, Is.EqualTo(measured).Within(0.0001f));
            Assert.That(fixture.Simulation.Noise.GetNoiseOverride(102), Is.Null);
        }

        [Test]
        public void MultipleActualSourcesRetainMeasuredSumWhileGuestFacingNoiseStaysNormalized()
        {
            var settings = Settings();
            var fixture = Create(settings);
            Book(fixture, settings, (102, 0), (106, 1), (103, 2), (105, 3));
            ReceiveAll(fixture);
            foreach (var guest in fixture.Simulation.Guests)
                Assert.That(fixture.Simulation.ForceActivity(guest.GuestId, GuestActivity.LoudRoom).Success, Is.True);
            float expected = fixture.Simulation.LivingSettings.LoudNoiseOutput *
                (2 * fixture.Simulation.NoiseSettings.SharedWallTransmission + 2 * fixture.Simulation.NoiseSettings.CorridorTransmission);
            Assert.That(expected, Is.GreaterThan(1));
            Assert.That(fixture.Room(104).ReceivedNoise, Is.EqualTo(expected).Within(0.0001f));
            Assert.That(fixture.Room(104).Noise, Is.EqualTo(1));
            Assert.That(fixture.Rooms.All(room => room.Noise >= 0 && room.Noise <= 1), Is.True);
        }

        [Test]
        public void QuietRequestReducesActualSourceTemporarilyWithoutStackingAndExpiryRestoresIt()
        {
            var settings = Settings();
            var fixture = Create(settings);
            Book(fixture, settings, (103, 0));
            ReceiveAll(fixture);
            var guest = fixture.Guest(103);
            Assert.That(fixture.Simulation.RequestQuiet(0, guest.GuestId).Success, Is.False, "Quiet rest is not a noisy activity.");
            Assert.That(fixture.Simulation.ForceActivity(guest.GuestId, GuestActivity.LoudRoom).Success, Is.True);
            float output = fixture.Room(103).SourceNoise, received = fixture.Room(102).ReceivedNoise;
            Assert.That(fixture.Simulation.RequestQuiet(-1, guest.GuestId).Success, Is.False);
            Assert.That(fixture.Simulation.RequestQuiet(0, "missing").Success, Is.False);
            int revision = fixture.Simulation.EventRevision;
            Assert.That((guest.Application.Archetype.Traits & GuestTraits.Noisy) != 0, Is.True);
            Assert.That(fixture.Simulation.RequestQuiet(7, guest.GuestId).Success, Is.True,
                "Logical staff identities must not be limited to the two local controller slots.");
            float until = guest.Agent.QuietUntil;
            Assert.That(until - fixture.Simulation.Elapsed, Is.EqualTo(fixture.Simulation.NoiseSettings.QuietRequestSeconds).Within(0.001f));
            Assert.That(fixture.Simulation.EventRevision, Is.GreaterThan(revision));
            Assert.That(fixture.Room(103).SourceNoise, Is.EqualTo(output * fixture.Simulation.NoiseSettings.QuietSourceMultiplier).Within(0.0001f));
            Assert.That(fixture.Room(102).ReceivedNoise, Is.EqualTo(received * fixture.Simulation.NoiseSettings.QuietSourceMultiplier).Within(0.0001f));
            Assert.That(fixture.Simulation.RequestQuiet(0, guest.GuestId).Success, Is.False);
            Assert.That(guest.Agent.QuietUntil, Is.EqualTo(until));
            AdvanceTo(fixture, until - 0.4f);
            revision = fixture.Simulation.EventRevision;
            AdvanceTo(fixture, until + 0.2f);
            Assert.That(guest.Agent.Activity, Is.EqualTo(GuestActivity.LoudRoom), "The source must still exist when its agreement expires.");
            Assert.That(guest.Agent.QuietUntil, Is.Zero);
            Assert.That(fixture.Room(103).SourceNoise, Is.EqualTo(output).Within(0.0001f));
            Assert.That(fixture.Room(102).ReceivedNoise, Is.EqualTo(received).Within(0.0001f));
            Assert.That(fixture.Simulation.EventRevision, Is.EqualTo(revision), "A quiet agreement expiring is ambient life, not a new complaint toast.");
        }

        [Test]
        public void OrdinaryGuestKeepsMusicQuietUntilCheckoutButAgreementDoesNotMuteTheShower()
        {
            var settings = Settings(0);
            var fixture = Create(settings);
            Book(fixture, settings, (103, 3));
            ReceiveAll(fixture);
            var guest = fixture.Guest(103);
            Assert.That((guest.Application.Archetype.Traits & GuestTraits.Noisy) == 0, Is.True);
            Assert.That(fixture.Simulation.ForceActivity(guest.GuestId, GuestActivity.LoudRoom).Success, Is.True);
            Assert.That(fixture.Simulation.RequestQuiet(17, guest.GuestId).Success, Is.True);
            Assert.That(guest.Agent.QuietUntil, Is.EqualTo(guest.Agent.CheckoutTime));
            float deadline = guest.Agent.QuietUntil;
            Assert.That(fixture.Simulation.ForceActivity(guest.GuestId, GuestActivity.Shower).Success, Is.True);
            float shower = fixture.Simulation.LivingSettings.ShowerNoiseOutput;
            Assert.That(fixture.Room(103).SourceNoise, Is.EqualTo(shower).Within(.0001f));
            Assert.That(fixture.Room(102).ReceivedNoise,
                Is.EqualTo(shower * fixture.Simulation.NoiseSettings.CorridorTransmission).Within(.0001f));
            AdvanceTo(fixture, fixture.Simulation.Elapsed + fixture.Simulation.NoiseSettings.QuietRequestSeconds + 1);
            Assert.That(guest.Agent.QuietUntil, Is.EqualTo(deadline));
            Assert.That(fixture.Room(103).SourceNoise, Is.EqualTo(shower).Within(.0001f));
            Assert.That(fixture.Simulation.ForceActivity(guest.GuestId, GuestActivity.LoudRoom).Success, Is.True);
            Assert.That(fixture.Room(103).SourceNoise, Is.EqualTo(fixture.Simulation.LivingSettings.LoudNoiseOutput *
                fixture.Simulation.NoiseSettings.QuietSourceMultiplier).Within(.0001f));
            Assert.That(fixture.Simulation.RequestQuiet(18, guest.GuestId).Success, Is.False);
            Assert.That(guest.Agent.QuietUntil, Is.EqualTo(deadline));
            AdvanceTo(fixture, guest.Agent.CheckoutTime + .2f);
            Assert.That(guest.Agent.QuietUntil, Is.Zero);
            Assert.That(fixture.Room(103).SourceNoise, Is.Zero);
            Assert.That(fixture.Simulation.RequestQuiet(17, guest.GuestId).Success, Is.False);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ExchangingPhysicalRoomKeysForEitherNoisySourceOrAffectedGuestChangesRealAcousticExposure(bool moveSource)
        {
            var settings = Settings(0);
            var fixture = Create(settings, new LivingHotelSettings(firstArrivalSeconds: .2f, arrivalJitterSeconds: 0,
                firstActivityDelay: 1000, activityDurationMin: 120, activityDurationMax: 120));
            int sourceRoom = moveSource ? 103 : 101;
            int receiverRoom = moveSource ? 102 : 103;
            int destination = moveSource ? 105 : 106;
            Book(fixture, settings, (sourceRoom, 0), (receiverRoom, 3));
            ReceiveAll(fixture);
            var source = fixture.Guest(sourceRoom);
            var receiver = fixture.Guest(receiverRoom);
            Assert.That(fixture.Simulation.ForceActivity(source.GuestId, GuestActivity.LoudRoom).Success, Is.True);
            AdvanceTo(fixture, fixture.Simulation.Elapsed + 30);
            var request = fixture.Simulation.Requests.Items.Single(item => item.GuestId == receiver.GuestId && item.Reason == IncidentReason.Noise);
            Assert.That(request.Resolved, Is.False);
            Assert.That(fixture.Room(receiverRoom).ReceivedNoise, Is.GreaterThan(0));
            float exposure = receiver.Needs.Noise.ExposureSeconds;
            var moved = moveSource ? source : receiver;
            int oldRoom = moved.RoomId;
            Assert.That(ModelKeyHandoff.MoveGuest(fixture.Simulation, 12, moved.GuestId, destination).Success, Is.True);
            Assert.That(fixture.Simulation.Keys.Find(destination).GuestId, Is.EqualTo(moved.GuestId));
            Assert.That(fixture.Simulation.Keys.Find(oldRoom).Location, Is.EqualTo(RoomKeyLocation.Returned));
            Assert.That(fixture.Room(oldRoom).SourceNoise, Is.Zero);
            AdvanceTo(fixture, fixture.Simulation.Elapsed + 2);
            Assert.That(request.Resolved, Is.False, "The transfer is not itself a completed complaint response.");
            Assert.That(fixture.Simulation.SignalGuestReachedRoom(moved.GuestId).Success, Is.True);
            // Arrival has a settling-in rest. Resume the identical diagnostic source so success
            // proves the new acoustic link, rather than relying on that temporary activity pause.
            if (moveSource)
                Assert.That(fixture.Simulation.ForceActivity(source.GuestId, GuestActivity.LoudRoom).Success, Is.True);
            Assert.That(fixture.Room(receiver.RoomId).ReceivedNoise, Is.Zero,
                "A route without a graph edge must stop receiving the actual loud source.");
            Assert.That(source.Agent.Activity, Is.EqualTo(GuestActivity.LoudRoom), "The same source must actually be loud when the new room is assessed.");
            Assert.That(fixture.Room(source.RoomId).SourceNoise, Is.EqualTo(fixture.Simulation.LivingSettings.LoudNoiseOutput).Within(.0001f));
            AdvanceTo(fixture, fixture.Simulation.Elapsed + fixture.Simulation.NeedsSettings.RecoverySeconds + .4f);
            Assert.That(request.Resolved, Is.True);
            Assert.That(request.ResponseAccepted, Is.False, "The measured condition, not compensation, resolves this situation.");
            Assert.That(receiver.Needs.Noise.Severity, Is.Zero);
            Assert.That(receiver.Needs.Noise.ExposureSeconds, Is.EqualTo(exposure).Within(.002f));
            Assert.That(fixture.Simulation.Requests.Items.Single(item => item.Id == request.Id), Is.SameAs(request));
        }

        [Test]
        public void SourceBeginsOnlyInAssignedRoomAndScheduledCheckoutRemovesIt()
        {
            var settings = Settings(0);
            var fixture = Create(settings);
            Book(fixture, settings, (103, 0));
            var guest = fixture.Guest(103);
            Assert.That(fixture.Rooms.All(room => room.SourceNoise == 0 && room.ReceivedNoise == 0), Is.True);
            AdvanceTo(fixture, guest.Agent.ArrivalTime + 0.2f);
            Assert.That(fixture.Simulation.SignalGuestReachedReception(guest.GuestId).Success, Is.True);
            Assert.That(ModelKeyHandoff.CheckIn(fixture.Simulation, 0, guest.GuestId).Success, Is.True);
            fixture.Simulation.Tick(0.2f);
            Assert.That(fixture.Room(103).SourceNoise, Is.Zero, "An occupied room with a guest still walking is not an acoustic source.");
            Assert.That(fixture.Simulation.SignalGuestReachedRoom(guest.GuestId).Success, Is.True);
            Assert.That(fixture.Room(103).SourceNoise, Is.Zero, "Quiet resting creates no invisible source.");
            Assert.That(fixture.Simulation.ForceActivity(guest.GuestId, GuestActivity.WatchTV).Success, Is.True);
            Assert.That(fixture.Room(103).SourceNoise, Is.GreaterThan(0));
            AdvanceTo(fixture, guest.Agent.CheckoutTime - 0.4f);
            Assert.That(fixture.Simulation.ForceActivity(guest.GuestId, GuestActivity.LoudRoom).Success, Is.True);
            Assert.That(fixture.Room(102).ReceivedNoise, Is.GreaterThan(0));
            AdvanceTo(fixture, guest.Agent.CheckoutTime + 0.2f);
            Assert.That(guest.Agent.InAssignedRoom, Is.False);
            Assert.That(fixture.Rooms.All(room => room.SourceNoise == 0 && room.ReceivedNoise == 0 && room.Noise == 0), Is.True);
            Assert.That(fixture.Simulation.RequestQuiet(0, guest.GuestId).Success, Is.False);
        }

        [Test]
        public void SettlementImmediatelyClearsCurrentNeedsAndNoiseButPreservesHistoricalEvidence()
        {
            var settings = Settings();
            var fixture = Create(settings);
            Book(fixture, settings, (103, 0), (101, 1));
            ReceiveAll(fixture);
            var guest = fixture.Guest(103);
            var room = fixture.Room(103);
            fixture.Simulation.ForceActivity(guest.GuestId, GuestActivity.LoudRoom);
            fixture.Simulation.ForceActivity(fixture.Guest(101).GuestId, GuestActivity.LoudRoom);
            room.Temperature = 5; room.Noise = 1; room.Cleanliness = Cleanliness.Dirty; room.RepairState = RepairState.Broken;
            fixture.Simulation.NeedEvaluator.Tick(guest, room, 2, true);
            var before = new[] { guest.Needs.Temperature, guest.Needs.Noise, guest.Needs.RoomCondition, guest.Needs.Service };
            float integral = guest.Needs.ServiceIntegral;
            Assert.That(before.All(snapshot => snapshot.Severity > 0 && snapshot.ExposureSeconds > 0), Is.True);
            fixture.Simulation.EndShift();
            var after = new[] { guest.Needs.Temperature, guest.Needs.Noise, guest.Needs.RoomCondition, guest.Needs.Service };
            for (int i = 0; i < before.Length; i++)
            {
                Assert.That(after[i].Severity, Is.Zero);
                Assert.That(after[i].ExposureSeconds, Is.EqualTo(before[i].ExposureSeconds));
                Assert.That(after[i].Dissatisfaction, Is.EqualTo(before[i].Dissatisfaction));
            }
            Assert.That(guest.Needs.ServiceIntegral, Is.EqualTo(integral));
            Assert.That(guest.Needs.CombinedRoomDeficit, Is.Zero);
            Assert.That(guest.Needs.ExpiredRoomComplaint, Is.False);
            Assert.That(fixture.Rooms.All(state => state.SourceNoise == 0 && state.ReceivedNoise == 0), Is.True);
            Assert.That(room.Noise, Is.EqualTo(room.Profile.Noise));
            // The inactive evaluator itself must also preserve history rather than accumulating stale exposure.
            fixture.Simulation.NeedEvaluator.Tick(guest, room, 10, true);
            Assert.That(guest.Needs.Noise.ExposureSeconds, Is.EqualTo(before[1].ExposureSeconds));
            Assert.That(guest.Needs.ServiceIntegral, Is.EqualTo(integral));
            Assert.That(guest.Needs.Service.Severity, Is.Zero);
        }

        [Test]
        public void NoiseBuildupMultiplierDoesNotAlterSeverityExposureTemperatureOrQualityWeight()
        {
            var settings = Settings();
            var slow = Create(settings, needs: new NeedSettings(noiseBuildupMultiplier: 1));
            var fast = Create(settings, needs: new NeedSettings(noiseBuildupMultiplier: 3));
            foreach (var fixture in new[] { slow, fast })
            {
                Book(fixture, settings, (101, 0), (103, 1)); ReceiveAll(fixture);
                fixture.Simulation.ForceActivity(fixture.Guest(103).GuestId, GuestActivity.LoudRoom);
                fixture.Room(101).Temperature = 5;
                fixture.Simulation.NeedEvaluator.Tick(fixture.Guest(101), fixture.Room(101), 5);
            }
            var a = slow.Guest(101).Needs; var b = fast.Guest(101).Needs;
            Assert.That(b.Noise.Dissatisfaction, Is.EqualTo(a.Noise.Dissatisfaction * 3).Within(0.0001f));
            Assert.That(b.Noise.Severity, Is.EqualTo(a.Noise.Severity));
            Assert.That(b.Noise.ExposureSeconds, Is.EqualTo(a.Noise.ExposureSeconds));
            Assert.That(b.Temperature.Dissatisfaction, Is.EqualTo(a.Temperature.Dissatisfaction));
            Assert.That(b.CombinedRoomDeficit, Is.EqualTo(a.CombinedRoomDeficit));
        }

        [Test]
        public void InvalidNoiseTuningCannotIntroduceNonFiniteOrAmplifyingTransmission()
        {
            Assert.Throws<ArgumentException>(() => new NoiseSettings(sharedWallTransmission: float.NaN));
            Assert.Throws<ArgumentException>(() => new NoiseSettings(sharedWallTransmission: 1.01f));
            Assert.Throws<ArgumentException>(() => new NoiseSettings(corridorTransmission: 0.8f));
            Assert.Throws<ArgumentException>(() => new NoiseSettings(quietRequestSeconds: 0));
            Assert.Throws<ArgumentException>(() => new NoiseSettings(quietRequestSeconds: float.PositiveInfinity));
            Assert.Throws<ArgumentException>(() => new NoiseSettings(quietSourceMultiplier: -0.1f));
            Assert.Throws<ArgumentException>(() => new NoiseSettings(quietSourceMultiplier: 1.1f));
            Assert.Throws<ArgumentException>(() => new NeedSettings(noiseBuildupMultiplier: float.NaN));
        }
    }
}
