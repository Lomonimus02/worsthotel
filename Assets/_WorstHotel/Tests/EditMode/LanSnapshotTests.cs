using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace WorstHotel.Tests
{
    public sealed class LanSnapshotTests
    {
        sealed class Fixture
        {
            public SessionConfig Asset;
            public SessionSettings Settings;
            public RoomState[] Rooms;
            public HotelSimulation Simulation;
        }

        static Fixture Create(bool populated)
        {
            var asset = AssetDatabase.LoadAssetAtPath<SessionConfig>("Assets/_WorstHotel/ScriptableObjects/PrototypeSession.asset");
            Assert.That(asset, Is.Not.Null);
            var settings = asset.ToData();
            var rooms = settings.Rooms.Select(profile => new RoomState(profile)).ToArray();
            var sim = new HotelSimulation(settings, rooms, asset.living.ToData(), asset.needs.ToData(),
                asset.noise.ToData(), asset.heater.ToData(), asset.electricity.ToData(), asset.housekeeping.ToData());
            var result = new Fixture { Asset = asset, Settings = settings, Rooms = rooms, Simulation = sim };
            if (!populated) return result;
            var offer = GuestSystem.GenerateApplications(1, settings.GuestArchetypes).First();
            Assert.That(sim.StartShift(new[] { new BookingAssignment(101, offer.Id, offer.ReferencePrice, 0) }, new[] { offer }).Success, Is.True);
            var guest = sim.Guests.Single();
            while (sim.Elapsed < guest.Agent.ArrivalTime + .2f) sim.Tick(.2f);
            Assert.That(sim.SignalGuestReachedReception(guest.GuestId).Success, Is.True);
            Assert.That(ModelKeyHandoff.CheckIn(sim, 0, guest.GuestId).Success, Is.True);
            Assert.That(sim.SignalGuestReachedRoom(guest.GuestId).Success, Is.True);
            // Explicit diagnostic conditions give the snapshot real needs, a response, and history.
            for (int tick = 0; tick < 150; tick++) { sim.SetRoomTemperature(101, 5); sim.Tick(.2f); }
            Assert.That(sim.Requests.Items.Any(request => !request.Resolved), Is.True);
            Assert.That(sim.OfferCompensation(guest.GuestId).Success, Is.True);
            Assert.That(sim.DebugMarkRoomDirty(102).Success, Is.True);
            Assert.That(sim.PickUpLinen(1, "dirty:102").Success, Is.True);
            Assert.That(sim.DepositDirtyLinen(1, "dirty:102").Success, Is.True);
            Assert.That(sim.PickUpLinen(1, "clean:0").Success, Is.True);
            Assert.That(sim.BeginMakeBed(1, 102, "clean:0").Success, Is.True);
            Assert.That(sim.AdvanceMakeBed(1, 102, .4f).Success, Is.True);
            Assert.That(sim.Keys.PickUp(7, 103).Success, Is.True);
            Assert.That(sim.Heaters.Register("lan-snapshot-heater").Success, Is.True);
            Assert.That(sim.Heaters.AssignRoom("lan-snapshot-heater", 101).Success, Is.True);
            Assert.That(sim.Heaters.SetSwitchedOn("lan-snapshot-heater", true).Success, Is.True);
            Assert.That(sim.DebugTripCircuit("A").Success, Is.True);
            return result;
        }

        static HotelSimulation Mirror(Fixture fixture)
        {
            fixture.Simulation.EnableReadOnlyMirror();
            return fixture.Simulation;
        }
        static string State(HotelSimulation sim) => JsonUtility.ToJson(sim.CaptureSnapshot(41, 1));

        [Test]
        public void InitialJsonWireSnapshotAcceptsUnplacedHeaterAndGuestBeforeItsFirstNeedTick()
        {
            var fixture = Create(false);
            var host = fixture.Simulation;
            Assert.That(host.Heaters.Register("unplaced-wire-heater").Success, Is.True);
            host.RefreshElectrical();
            var original = host.CaptureSnapshot(41, 1);
            var consumer = original.Consumers.Single(item => item.Id == "heater:unplaced-wire-heater");
            Assert.That(consumer.RoomId, Is.Zero);
            Assert.That(consumer.CircuitId, Is.Null);
            Assert.That(original.Guests, Is.Empty);
            Assert.That(original.Rooms.All(room => room.GuestId == null && room.ReservedGuestId == null && room.DepartingGuestId == null), Is.True);
            var packet = JsonUtility.FromJson<HotelModelSnapshot>(JsonUtility.ToJson(original));
            string wireCircuit = packet.Consumers.Single(item => item.Id == consumer.Id).CircuitId;
            TestContext.WriteLine("Initial wire optional strings: unplaced circuit=" + (wireCircuit == null ? "<null>" : "'" + wireCircuit + "'") +
                "; vacant room owner=" + (packet.Rooms[0].GuestId == null ? "<null>" : "'" + packet.Rooms[0].GuestId + "'"));
            var replica = Mirror(Create(false));
            var applied = replica.ApplySnapshot(packet);
            Assert.That(applied.Success, Is.True, applied.Message);
            Assert.That(replica.Heaters.Find("unplaced-wire-heater").RoomId, Is.Null);
            Assert.That(replica.Heaters.Find("unplaced-wire-heater").DemandedElectricalLoad, Is.Zero);
            Assert.That(State(replica), Is.EqualTo(State(host)));

            var offer = GuestSystem.GenerateApplications(1, fixture.Settings.GuestArchetypes).First();
            Assert.That(host.StartShift(new[] { new BookingAssignment(101, offer.Id, offer.ReferencePrice, 0) }, new[] { offer }).Success, Is.True);
            Assert.That(host.Guests.Single().Needs, Is.Null, "Capture immediately after commitment, before a simulation tick.");
            packet = JsonUtility.FromJson<HotelModelSnapshot>(JsonUtility.ToJson(host.CaptureSnapshot(41, 2)));
            Assert.That(packet.Guests.Single().HasNeeds, Is.False);
            applied = replica.ApplySnapshot(packet);
            Assert.That(applied.Success, Is.True, applied.Message);
            Assert.That(replica.Guests.Single().Needs, Is.Null);
            Assert.That(replica.Guests.Single().Agent.State, Is.EqualTo(GuestAgentState.Scheduled));
            Assert.That(State(replica), Is.EqualTo(State(host)));
        }

        [Test]
        public void CheckoutJsonWireSnapshotAcceptsDirtyBedWithoutCleanLinenAndPreservesDepartureOwnership()
        {
            var host = Create(true).Simulation;
            string leavingGuest = host.Guests.Single().GuestId;
            host.EndShift();
            var original = host.CaptureSnapshot(41, 1);
            var dirtyTask = original.Turnover.Single(task => task.RoomId == 101);
            Assert.That(dirtyTask.Step, Is.EqualTo(RoomPreparationStep.DirtyLinenOnBed));
            Assert.That(dirtyTask.CleanLinenId, Is.Null);
            Assert.That(original.Rooms.Single(room => room.Id == 101).DepartingGuestId, Is.EqualTo(leavingGuest));
            Assert.That(original.Keys.Single(key => key.RoomId == 101).GuestId, Is.Null);
            var packet = JsonUtility.FromJson<HotelModelSnapshot>(JsonUtility.ToJson(original));
            string wireClean = packet.Turnover.Single(task => task.RoomId == 101).CleanLinenId;
            TestContext.WriteLine("Checkout wire optional strings: no chosen clean linen=" + (wireClean == null ? "<null>" : "'" + wireClean + "'") +
                "; returned key guest=" + (packet.Keys.Single(key => key.RoomId == 101).GuestId == null ? "<null>" :
                    "'" + packet.Keys.Single(key => key.RoomId == 101).GuestId + "'"));
            var replicaFixture = Create(false);
            var replica = Mirror(replicaFixture);
            var applied = replica.ApplySnapshot(packet);
            Assert.That(applied.Success, Is.True, applied.Message);
            Assert.That(replica.Housekeeping.Find(101).Step, Is.EqualTo(RoomPreparationStep.DirtyLinenOnBed));
            Assert.That(string.IsNullOrEmpty(replica.Housekeeping.Find(101).CleanLinenId), Is.True);
            Assert.That(replicaFixture.Rooms.Single(room => room.Profile.Id == 101).DepartingGuestId, Is.EqualTo(leavingGuest));
            Assert.That(replica.Keys.Find(101).Location, Is.EqualTo(RoomKeyLocation.Returned));
            Assert.That(replica.DayReports.Count, Is.EqualTo(1));
            Assert.That(State(replica), Is.EqualTo(State(host)));
        }

        [Test]
        public void RichSnapshotRoundTripIsDeeplyIsolatedAndEmitsNoGameplayCallbacks()
        {
            var host = Create(true).Simulation;
            var replica = Mirror(Create(false));
            int callbacks = 0;
            replica.Incidents.OnSituationChanged += _ => callbacks++;
            replica.Requests.OnRequestCreated += _ => callbacks++;
            replica.Requests.OnRequestResolved += _ => callbacks++;
            replica.Housekeeping.Changed += (_, __) => callbacks++;
            var captured = host.CaptureSnapshot(41, 1);
            var packet = JsonUtility.FromJson<HotelModelSnapshot>(JsonUtility.ToJson(captured));
            Assert.That(replica.ApplySnapshot(packet).Success, Is.True);
            Assert.That(State(replica), Is.EqualTo(State(host)), "Every exposed aggregate should survive the wire representation.");
            Assert.That(callbacks, Is.Zero, "Restoring a snapshot is not another gameplay action.");
            Assert.That(replica.Guests.Single(), Is.Not.SameAs(host.Guests.Single()));
            Assert.That(replica.Housekeeping.FindLinen("clean:0"), Is.Not.SameAs(host.Housekeeping.FindLinen("clean:0")));
            Assert.That(replica.Keys.Find(103).PlayerId, Is.EqualTo(7));
            Assert.That(replica.Housekeeping.Find(102).ProgressSeconds, Is.EqualTo(.4f).Within(.0001f));
            Assert.That(replica.Electrical.Find("A").Tripped, Is.True);
            Assert.That(replica.Heaters.Find("lan-snapshot-heater").EffectiveHeatOutput, Is.Zero);
            string before = State(replica);
            packet.Rooms[0].Temperature = -50;
            packet.Guests[0].Application.GuestName = "mutated transport object";
            packet.Guests[0].Agent.Schedule[0].Duration += 200;
            packet.Guests[0].Temperature.Dissatisfaction = 1;
            packet.Linens[0].Generation += 100;
            packet.Turnover[0].ProgressSeconds = 1.4f;
            captured.Guests[0].Agent.Schedule[0].Duration += 300;
            captured.Rooms[0].Temperature = -40;
            Assert.That(State(replica), Is.EqualTo(before));
            Assert.That(State(host), Is.EqualTo(before), "Capture must not expose mutable host-owned objects either.");
        }

        [Test]
        public void SameSessionPreservesPresentationIdentitiesButNewEpochReplacesReusedGuestAndTaskIds()
        {
            var host = Create(true).Simulation;
            var replica = Mirror(Create(false));
            Assert.That(replica.ApplySnapshot(host.CaptureSnapshot(41, 1)).Success, Is.True);
            var guest = replica.Guests.Single(); var agent = guest.Agent;
            var task = replica.Housekeeping.Find(102); var key = replica.Keys.Find(103);
            var linen = replica.Housekeeping.FindLinen("clean:0");
            Assert.That(host.AdvanceMakeBed(1, 102, .2f).Success, Is.True);
            host.Tick(.2f);
            Assert.That(replica.ApplySnapshot(host.CaptureSnapshot(41, 2)).Success, Is.True);
            Assert.That(replica.Guests.Single(), Is.SameAs(guest));
            Assert.That(replica.Guests.Single().Agent, Is.SameAs(agent));
            Assert.That(replica.Housekeeping.Find(102), Is.SameAs(task));
            Assert.That(task.ProgressSeconds, Is.EqualTo(.6f).Within(.0001f));
            Assert.That(replica.Keys.Find(103), Is.SameAs(key));
            Assert.That(replica.Housekeeping.FindLinen("clean:0"), Is.SameAs(linen));
            var restarted = Create(true).Simulation;
            Assert.That(restarted.Guests.Single().GuestId, Is.EqualTo(guest.GuestId), "The test must exercise reused day-one identifiers.");
            Assert.That(replica.ApplySnapshot(restarted.CaptureSnapshot(42, 1)).Success, Is.True);
            Assert.That(replica.Guests.Single(), Is.Not.SameAs(guest));
            Assert.That(replica.Housekeeping.Find(102), Is.Not.SameAs(task));
            Assert.That(replica.Housekeeping.Find(102).ProgressSeconds, Is.EqualTo(.4f).Within(.0001f));
            Assert.That(replica.ApplySnapshot(host.CaptureSnapshot(41, 999)).Success, Is.False);
            Assert.That(replica.AppliedSnapshotEpoch, Is.EqualTo(42));
        }

        [Test]
        public void MirrorCannotAdvanceTimeMakeDecisionsOrCompleteManualWorkLocally()
        {
            var host = Create(true).Simulation;
            var replica = Mirror(Create(false));
            Assert.That(replica.ApplySnapshot(host.CaptureSnapshot(41, 1)).Success, Is.True);
            string before = State(replica);
            replica.Tick(20); replica.AdvancePreparation(20); replica.Clock.Advance(20); replica.Clock.SetSpeed(8);
            Assert.That(replica.Keys.PickUp(0, 104).Success, Is.False);
            Assert.That(replica.AdvanceMakeBed(1, 102, 2).Success, Is.False);
            Assert.That(replica.Housekeeping.RefillForDay(2).Success, Is.False);
            Assert.That(replica.Heaters.SetSwitchedOn("lan-snapshot-heater", false).Success, Is.False);
            Assert.That(replica.SetRoomNoise(101, 0).Success, Is.False);
            Assert.That(replica.ResetCircuit(0, "A").Success, Is.False);
            Assert.That(replica.OfferCompensation(replica.Guests.Single().GuestId).Success, Is.False);
            Assert.That(replica.DebugCheckoutGuest(replica.Guests.Single().GuestId).Success, Is.False);
            Assert.Throws<InvalidOperationException>(() => replica.EndShift());
            Assert.That(State(replica), Is.EqualTo(before), "A connected client must wait for host snapshots, even while its UI is open.");
            Assert.That(host.ApplySnapshot(host.CaptureSnapshot(41, 2)).Success, Is.False, "The authority cannot overwrite itself with a replica packet.");
        }

        [Test]
        public void InvalidAndStaleSnapshotsAreRejectedAtomicallyWithoutConsumingSequence()
        {
            var host = Create(true).Simulation;
            var replica = Mirror(Create(false));
            Assert.That(replica.ApplySnapshot(host.CaptureSnapshot(41, 10)).Success, Is.True);
            string before = State(replica);
            var invalid = new Action<HotelModelSnapshot>[]
            {
                s => s.Version++, s => s.Rooms[0].Temperature = float.NaN,
                s => s.Guests[0].Agent.Schedule[0].Duration = float.PositiveInfinity,
                s => s.Keys[1].RoomId = s.Keys[0].RoomId,
                s => s.Linens[0].Id = "unknown-slot",
                s => s.Turnover[0].WorkingPlayerId = -2,
                s => s.Requests[0].Id = "unknown-situation",
                s => s.Guests[0].Noise = null,
                s => s.Rooms = s.Rooms.Concat(new[] { s.Rooms[0] }).ToArray()
            };
            foreach (var corrupt in invalid)
            {
                var packet = host.CaptureSnapshot(41, 11); corrupt(packet);
                Assert.That(replica.ApplySnapshot(packet).Success, Is.False);
                Assert.That(State(replica), Is.EqualTo(before));
                Assert.That(replica.AppliedSnapshotSequence, Is.EqualTo(10));
            }
            Assert.That(replica.ApplySnapshot(host.CaptureSnapshot(41, 10)).Success, Is.False);
            Assert.That(replica.ApplySnapshot(host.CaptureSnapshot(41, 9)).Success, Is.False);
            Assert.That(replica.ApplySnapshot(host.CaptureSnapshot(40, 999)).Success, Is.False);
            Assert.That(replica.ApplySnapshot(host.CaptureSnapshot(41, 11)).Success, Is.True);
        }

        [Test]
        public void SettledReportsRoundTripWithoutRechargingAndReceiptDtosAreIndependent()
        {
            var host = Create(true).Simulation;
            var report = host.EndShift();
            var replica = Mirror(Create(false));
            var packet = host.CaptureSnapshot(41, 1);
            Assert.That(replica.ApplySnapshot(packet).Success, Is.True);
            Assert.That(replica.LastReport.Cash, Is.EqualTo(report.Cash));
            Assert.That(replica.LastReport.Receipts.Single().Review, Is.EqualTo(report.Receipts.Single().Review));
            Assert.That(replica.Economy.Cash, Is.EqualTo(host.Economy.Cash));
            Assert.Throws<InvalidOperationException>(() => replica.EndShift());
            packet.Reports[0].Receipts[0].Compensation = 0;
            packet.Reports[0].Receipts[0].Review = "changed packet";
            Assert.That(replica.LastReport.Receipts.Single().Review, Is.EqualTo(report.Receipts.Single().Review));
            Assert.That(replica.ApplySnapshot(host.CaptureSnapshot(41, 2)).Success, Is.True);
            Assert.That(replica.DayReports.Count, Is.EqualTo(1));
            Assert.That(replica.Economy.Cash, Is.EqualTo(report.Cash));
        }

        [Test]
        public void PlanningReplicaIsDeeplyCopiedAndRejectsAllLocalBookingMutations()
        {
            var fixture = Create(false);
            var offers = GuestSystem.GenerateApplications(1, fixture.Settings.GuestArchetypes);
            var plan = new PlanningSystem(fixture.Rooms, offers, fixture.Settings.Economy, fixture.Settings.Boiler);
            Assert.That(plan.Assign(0, offers[0].Id, 101, offers[0].ReferencePrice).Success, Is.True);
            var packet = plan.CaptureSnapshot();
            var replica = PlanningSystem.FromSnapshot(fixture.Rooms, fixture.Settings.Economy, fixture.Settings.Boiler, packet);
            Assert.That(replica.IsReadOnlyMirror, Is.True);
            int price = replica.Assignments.Single().Price;
            string guestName = replica.Applications[0].GuestName;
            packet.Assignments[0].Price += 50; packet.Applications[0].GuestName = "changed";
            Assert.That(replica.Assignments.Single().Price, Is.EqualTo(price));
            Assert.That(replica.Applications[0].GuestName, Is.EqualTo(guestName));
            Assert.That(plan.Assignments.Single().Price, Is.EqualTo(price));
            Assert.That(replica.Assign(1, offers[1].Id, 102, offers[1].ReferencePrice).Success, Is.False);
            Assert.That(replica.SetPrice(1, 101, price + fixture.Settings.Economy.PriceStep).Success, Is.False);
            Assert.That(replica.Remove(1, 101).Success, Is.False);
            Assert.That(replica.TryCommit(1, out _, out _), Is.False);
            Assert.That(replica.IsCommitted, Is.False);
            var bad = plan.CaptureSnapshot(); bad.Assignments[0].BookingId = "missing";
            Assert.Throws<ArgumentException>(() => PlanningSystem.FromSnapshot(fixture.Rooms, fixture.Settings.Economy, fixture.Settings.Boiler, bad));
        }
    }
}
