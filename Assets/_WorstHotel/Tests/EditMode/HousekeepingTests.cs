using System;
using System.Linq;
using NUnit.Framework;

namespace WorstHotel.Tests
{
    public sealed class HousekeepingTests
    {
        private sealed class Fixture
        {
            public SessionSettings Settings;
            public RoomState[] Rooms;
            public HotelSimulation Simulation;
            public RoomState Room(int id) => Rooms.Single(room => room.Profile.Id == id);
        }

        private static Fixture Create(params int[] initiallyDirty)
        {
            var profiles = new[]
            {
                new GuestProfile(GuestKind.Budget, "Budget", "", 180, 0.85f, 18, 0.7f, 28, 90, 0.55f),
                new GuestProfile(GuestKind.ColdSensitive, "Cold-sensitive", "", 300, 1.05f, 20, 1.35f, 16, 65, 0.4f),
                new GuestProfile(GuestKind.Business, "Business", "", 450, 1, 19.5f, 1, 10, 45, 0.25f)
            };
            var settings = new SessionSettings(profiles, Enumerable.Range(101, 6).Select(id => new RoomProfile(id,
                "Room " + id, noise: 0, cleanliness: initiallyDirty.Contains(id) ? Cleanliness.Dirty : Cleanliness.Clean,
                temperature: 22.5f)), new BoilerSettings(), new EconomySettings(), temperatureBase: 22.5f, heatTemperatureGain: 0);
            var rooms = settings.Rooms.Select(profile => new RoomState(profile)).ToArray();
            var simulation = new HotelSimulation(settings, rooms, new LivingHotelSettings(firstArrivalSeconds: 0.2f,
                arrivalSpacingSeconds: 1, arrivalJitterSeconds: 0, firstActivityDelay: 1000),
                new NeedSettings(), new NoiseSettings(), new HeaterSettings(), new ElectricitySettings(), new HousekeepingSettings());
            return new Fixture { Settings = settings, Rooms = rooms, Simulation = simulation };
        }

        private static void Start(Fixture fixture, int count = 1, int offerDay = 1)
        {
            var offers = GuestSystem.GenerateApplications(offerDay, fixture.Settings.GuestArchetypes).Take(count).ToArray();
            Assert.That(offers.Length, Is.EqualTo(count));
            Assert.That(fixture.Simulation.StartShift(offers.Select((offer, index) =>
                new BookingAssignment(101 + index, offer.Id, offer.ReferencePrice, index % 2)), offers).Success, Is.True);
        }

        private static void AdvanceTo(Fixture fixture, float until)
        {
            for (int i = 0; i < 2000 && fixture.Simulation.Elapsed + 0.00001f < until; i++)
                fixture.Simulation.Tick(Math.Min(0.2f, until - fixture.Simulation.Elapsed));
            Assert.That(fixture.Simulation.Elapsed, Is.EqualTo(until).Within(0.002f));
        }

        private static void Admit(Fixture fixture, int index = 0, bool reachRoom = true)
        {
            var guest = fixture.Simulation.Guests[index];
            AdvanceTo(fixture, Math.Max(fixture.Simulation.Elapsed, guest.Agent.ArrivalTime + 0.2f));
            Assert.That(fixture.Simulation.SignalGuestReachedReception(guest.GuestId).Success, Is.True);
            Assert.That(ModelKeyHandoff.CheckIn(fixture.Simulation, index % 2, guest.GuestId).Success, Is.True);
            if (reachRoom) Assert.That(fixture.Simulation.SignalGuestReachedRoom(guest.GuestId).Success, Is.True);
        }

        [Test]
        public void ActualCheckoutCreatesOneDirtyBundleButDepartureMustBeAcknowledgedBeforeRemoval()
        {
            var f = Create(); Start(f); Admit(f); var sim = f.Simulation; var guest = sim.Guests.Single();
            AdvanceTo(f, guest.Agent.CheckoutTime + .2f);
            var room = f.Room(101); var task = sim.Housekeeping.Find(101);
            Assert.That(room.Cleanliness, Is.EqualTo(Cleanliness.Dirty));
            Assert.That(room.Occupied || room.Reserved, Is.False);
            Assert.That(room.DepartingGuestId, Is.EqualTo(guest.GuestId));
            Assert.That(task.Step, Is.EqualTo(RoomPreparationStep.DirtyLinenOnBed));
            Assert.That(sim.Housekeeping.Linens.Count(item => item.Kind == LinenKind.Dirty && item.SourceRoomId == 101), Is.EqualTo(1));
            Assert.That(sim.PickUpLinen(0, task.DirtyLinenId).Success, Is.False);
            var report = sim.EndShift(); Assert.That(sim.EndShift(), Is.SameAs(report));
            sim.AdvancePreparation(60);
            Assert.That(sim.Housekeeping.Find(101), Is.SameAs(task));
            Assert.That(task.ProgressSeconds, Is.Zero);
            Assert.That(sim.Housekeeping.WorkerAvailable, Is.False);
            Assert.That(sim.SignalHousekeeperReachedRoom(101).Success, Is.False);
            Assert.That(sim.SignalGuestVacatedRoom(guest.GuestId, 101).Success, Is.True);
            Assert.That(sim.PickUpLinen(0, task.DirtyLinenId).Success, Is.True);
            Assert.That(task.Step, Is.EqualTo(RoomPreparationStep.DeliverDirtyLinen));
            sim.AdvancePreparation(60);
            Assert.That(room.Cleanliness, Is.EqualTo(Cleanliness.Dirty), "Carrying a bundle and waiting cannot clean its room.");
            Assert.That(task.ProgressSeconds, Is.Zero);
        }

        [Test]
        public void FutureAndWaitingNoShowReservationsDoNotDirtyUnusedRoomsOrCreateDepartureTokens()
        {
            var f = Create(); Start(f, 2); var arrived = f.Simulation.Guests[0];
            AdvanceTo(f, arrived.Agent.ArrivalTime + .2f);
            Assert.That(f.Simulation.SignalGuestReachedReception(arrived.GuestId).Success, Is.True);
            Assert.That(f.Simulation.Guests[1].Agent.State, Is.EqualTo(GuestAgentState.Scheduled));
            f.Simulation.EndShift();
            Assert.That(f.Rooms.All(room => room.Cleanliness == Cleanliness.Clean && room.DepartingGuestId == null), Is.True);
            Assert.That(f.Simulation.Housekeeping.Tasks, Is.Empty);
            Assert.That(f.Simulation.Housekeeping.HasPendingWork, Is.False);
        }

        [Test]
        public void InterruptedInitialRoomTravelKeepsRoomCleanButBlocksRecheckinUntilOldPhysicalIdentityVacates()
        {
            var f = Create(); Start(f); Admit(f, reachRoom: false); var sim = f.Simulation;
            var old = sim.Guests.Single(); sim.EndShift();
            Assert.That(f.Room(101).Cleanliness, Is.EqualTo(Cleanliness.Clean));
            Assert.That(f.Room(101).DepartingGuestId, Is.EqualTo(old.GuestId));
            Assert.That(sim.Housekeeping.Tasks, Is.Empty);
            Assert.That(sim.ApplyMaintenance(0, MaintenanceChoice.Defer).Success, Is.True);
            Start(f, offerDay: 2); var next = sim.Guests.Single();
            Assert.That(next.GuestId, Is.Not.EqualTo(old.GuestId));
            AdvanceTo(f, next.Agent.ArrivalTime + .2f);
            Assert.That(sim.SignalGuestReachedReception(next.GuestId).Success, Is.True);
            ModelKeyHandoff.TakeKey(sim, 0, 101);
            Assert.That(sim.CheckIn(0, next.GuestId).Success, Is.False);
            Assert.That(sim.SignalGuestVacatedRoom(next.GuestId, 101).Success, Is.False);
            Assert.That(sim.SignalGuestVacatedRoom(old.GuestId, 101).Success, Is.True);
            Assert.That(sim.CheckIn(0, next.GuestId).Success, Is.True);
            Assert.That(sim.SignalGuestVacatedRoom(old.GuestId, 101).Success, Is.False);
            Assert.That(f.Room(101).GuestId, Is.EqualTo(next.GuestId));
        }

        [Test]
        public void TransferDirtiesOnlyTheUsedOriginAndNeverTheUnreachedReservedDestination()
        {
            var f = Create(); Start(f); Admit(f); var guest = f.Simulation.Guests.Single();
            Assert.That(ModelKeyHandoff.MoveGuest(f.Simulation, 0, guest.GuestId, 102).Success, Is.True);
            Assert.That(f.Room(101).Cleanliness, Is.EqualTo(Cleanliness.Dirty));
            Assert.That(f.Room(101).DepartingGuestId, Is.EqualTo(guest.GuestId));
            Assert.That(f.Room(102).Cleanliness, Is.EqualTo(Cleanliness.Clean));
            Assert.That(f.Room(102).ReservedGuestId, Is.EqualTo(guest.GuestId));
            Assert.That(f.Room(102).DepartingGuestId, Is.Null);
            f.Simulation.EndShift();
            Assert.That(f.Room(102).Reserved, Is.False);
            Assert.That(f.Room(102).Cleanliness, Is.EqualTo(Cleanliness.Clean));
            Assert.That(f.Simulation.Housekeeping.Tasks.Select(task => task.RoomId), Is.EqualTo(new[] { 101 }));
            Assert.That(f.Simulation.SignalGuestVacatedRoom(guest.GuestId, 102).Success, Is.False);
            Assert.That(f.Simulation.SignalGuestVacatedRoom(guest.GuestId, 101).Success, Is.True);
        }

        [Test]
        public void WrongOrRepeatedDepartureCallbacksCannotUnlockLinenOrReleaseOccupiedRooms()
        {
            var f = Create(); Start(f); Admit(f); var sim = f.Simulation; var guest = sim.Guests.Single();
            Assert.That(sim.SignalGuestVacatedRoom(guest.GuestId, 101).Success, Is.False);
            Assert.That(f.Room(101).GuestId, Is.EqualTo(guest.GuestId));
            sim.EndShift(); var task = sim.Housekeeping.Find(101);
            Assert.That(sim.SignalGuestVacatedRoom("missing", 101).Success, Is.False);
            Assert.That(sim.SignalGuestVacatedRoom(guest.GuestId, 999).Success, Is.False);
            Assert.That(sim.SignalGuestVacatedRoom(guest.GuestId, 102).Success, Is.False);
            Assert.That(sim.PickUpLinen(0, task.DirtyLinenId).Success, Is.False);
            Assert.That(f.Room(101).DepartingGuestId, Is.EqualTo(guest.GuestId));
            Assert.That(sim.SignalGuestVacatedRoom(guest.GuestId, 101).Success, Is.True);
            Assert.That(sim.SignalGuestVacatedRoom(guest.GuestId, 101).Success, Is.False);
            Assert.That(sim.Housekeeping.Find(101), Is.SameAs(task));
            Assert.That(sim.Housekeeping.Tasks.Count, Is.EqualTo(1));
        }

        [Test]
        public void FiniteLinenRequiresItsRealSourceMatchingOwnerAndDirtyDeliveryBeforeBedWork()
        {
            var f = Create(101); var sim = f.Simulation; var hk = sim.Housekeeping; var task = hk.Find(101);
            var stock = hk.Linens.Where(item => item.Kind == LinenKind.Clean).ToArray();
            Assert.That(stock.Length, Is.EqualTo(6));
            Assert.That(stock.Select(item => item.Id), Is.EquivalentTo(Enumerable.Range(0, 6).Select(i => "clean:" + i)));
            Assert.That(stock.All(item => item.Location == LinenLocation.OnShelf), Is.True);
            Assert.That(sim.BeginMakeBed(0, 101, stock[0].Id).Success, Is.False, "Clean linen cannot appear from an empty hand.");
            Assert.That(sim.PickUpLinen(17, task.DirtyLinenId).Success, Is.True);
            Assert.That(sim.PickUpLinen(17, stock[0].Id).Success, Is.False, "One player cannot carry two linen bundles.");
            Assert.That(sim.PickUpLinen(0, task.DirtyLinenId).Success, Is.False);
            Assert.That(sim.DepositDirtyLinen(0, task.DirtyLinenId).Success, Is.False);
            Assert.That(sim.PickUpLinen(0, stock[0].Id).Success, Is.True);
            Assert.That(sim.BeginMakeBed(0, 101, stock[0].Id).Success, Is.False, "Taking clean stock cannot skip dirty delivery.");
            Assert.That(sim.DepositDirtyLinen(0, stock[0].Id).Success, Is.False);
            Assert.That(sim.DepositDirtyLinen(17, task.DirtyLinenId).Success, Is.True);
            Assert.That(hk.FindLinen(task.DirtyLinenId).Location, Is.EqualTo(LinenLocation.InHamper));
            Assert.That(sim.DepositDirtyLinen(17, task.DirtyLinenId).Success, Is.False);
            ManualTurnoverModelAdapter.FinishBed(sim, 101, 0, stock[0].Id);
            Assert.That(stock[0].Location, Is.EqualTo(LinenLocation.Consumed));
            Assert.That(stock.Count(item => item.Location == LinenLocation.OnShelf), Is.EqualTo(5));
            Assert.That(f.Room(101).Cleanliness, Is.EqualTo(Cleanliness.Clean));
            Assert.That(sim.AdvanceMakeBed(0, 101, .1f).Success, Is.False);
            Assert.That(sim.DropLinen(0, stock[0].Id).Success, Is.False, "A stale physical release cannot revive consumed clean linen.");
        }

        [Test]
        public void BedClaimIsExclusiveAndUnexpectedReoccupationCancelsWithoutConsumingStock()
        {
            var f = Create(101); var sim = f.Simulation; var hk = sim.Housekeeping;
            ManualTurnoverModelAdapter.StripAndDeposit(sim, 101, 0);
            string first = ManualTurnoverModelAdapter.TakeClean(sim, 0), second = ManualTurnoverModelAdapter.TakeClean(sim, 9);
            Assert.That(sim.BeginMakeBed(0, 101, first).Success, Is.True);
            Assert.That(sim.AdvanceMakeBed(0, 101, .4f).Success, Is.True);
            var task = hk.Find(101);
            Assert.That(sim.BeginMakeBed(9, 101, second).Success, Is.False);
            Assert.That(sim.AdvanceMakeBed(9, 101, .4f).Success, Is.False);
            Assert.That(sim.CancelMakeBed(9, 101).Success, Is.False);
            Assert.That(task.WorkingPlayerId, Is.EqualTo(0));
            Assert.That(task.ProgressSeconds, Is.EqualTo(.4f).Within(.001f));
            f.Room(101).GuestId = "unexpected-occupied-fixture";
            hk.Tick(100);
            Assert.That(task.ProgressSeconds, Is.Zero);
            Assert.That(task.WorkingPlayerId, Is.Null);
            Assert.That(f.Room(101).Cleanliness, Is.EqualTo(Cleanliness.Dirty));
            Assert.That(hk.FindLinen(first).Location, Is.EqualTo(LinenLocation.HeldByPlayer));
            Assert.That(sim.BeginMakeBed(0, 101, first).Success, Is.False);
        }

        [Test]
        public void DirtyReservationWaitsForManualPreparationAndCorrectKeyHandoff()
        {
            var f = Create(101); var sim = f.Simulation;
            var offers = GuestSystem.GenerateApplications(1, f.Settings.GuestArchetypes);
            var plan = new PlanningSystem(f.Rooms, offers, f.Settings.Economy, f.Settings.Boiler);
            Assert.That(plan.Assign(0, offers[0].Id, 101, offers[0].ReferencePrice).Success, Is.True);
            Assert.That(plan.TryCommit(0, out var assignments, out var error), Is.True, error);
            Assert.That(sim.StartShift(assignments, offers).Success, Is.True);
            var guest = sim.Guests.Single(); AdvanceTo(f, guest.Agent.ArrivalTime + .2f);
            Assert.That(sim.SignalGuestReachedReception(guest.GuestId).Success, Is.True);
            ModelKeyHandoff.TakeKey(sim, 0, 101);
            Assert.That(sim.CheckIn(0, guest.GuestId).Success, Is.False);
            AdvanceTo(f, sim.Elapsed + 35);
            Assert.That(f.Room(101).Cleanliness, Is.EqualTo(Cleanliness.Dirty));
            Assert.That(sim.Housekeeping.Find(101).ProgressSeconds, Is.Zero);
            ManualTurnoverModelAdapter.PrepareRoom(sim, 101, 1);
            Assert.That(f.Room(101).ReservedGuestId, Is.EqualTo(guest.GuestId));
            Assert.That(sim.CheckIn(0, guest.GuestId).Success, Is.True);
            Assert.That(sim.Housekeeping.Tasks, Is.Empty);
        }

        [Test]
        public void StartingServiceResetsClockButDoesNotSpendTimeOrReplaceManualLinenWork()
        {
            var f = Create(101); var sim = f.Simulation;
            ManualTurnoverModelAdapter.StripAndDeposit(sim, 101, 0);
            string clean = ManualTurnoverModelAdapter.TakeClean(sim, 0);
            Assert.That(sim.BeginMakeBed(0, 101, clean).Success, Is.True);
            Assert.That(sim.AdvanceMakeBed(0, 101, .4f).Success, Is.True);
            var task = sim.Housekeeping.Find(101);
            sim.AdvancePreparation(8);
            Assert.That(task.ProgressSeconds, Is.EqualTo(.4f).Within(.001f));
            Assert.That(sim.Elapsed, Is.EqualTo(8));
            Start(f);
            Assert.That(sim.Elapsed, Is.Zero);
            Assert.That(sim.Housekeeping.Find(101), Is.SameAs(task));
            Assert.That(task.CleanLinenId, Is.EqualTo(clean));
            sim.Tick(1);
            Assert.That(task.ProgressSeconds, Is.EqualTo(.4f).Within(.001f));
            Assert.That(sim.AdvanceMakeBed(0, 101, .1f).Success, Is.True);
            Assert.That(task.ProgressSeconds, Is.EqualTo(.5f).Within(.001f));
        }

        [Test]
        public void SixDirtyRoomsNeedSixManualChangesAndPreparationHasNoHiddenBusinessCharges()
        {
            var f = Create(); Start(f, 6, 2); var sim = f.Simulation;
            for (int i = 0; i < 6; i++) Admit(f, i);
            sim.Tick(1); sim.EndShift();
            Assert.That(f.Rooms.All(room => room.Cleanliness == Cleanliness.Dirty && room.DepartingGuestId != null), Is.True);
            Assert.That(sim.Housekeeping.Tasks.Count, Is.EqualTo(6));
            Assert.That(sim.ApplyMaintenance(0, MaintenanceChoice.Defer).Success, Is.True);
            float condition = sim.Boiler.Condition, pressure = sim.Boiler.Pressure;
            int cash = sim.Economy.Cash, reports = sim.DayReports.Count;
            var quality = sim.Guests.Select(guest => guest.QualityIntegral).ToArray();
            var exposure = sim.Guests.Select(guest => guest.Elapsed).ToArray();
            sim.AdvancePreparation(40);
            Assert.That(sim.Housekeeping.Tasks.All(task => task.ProgressSeconds == 0), Is.True);
            foreach (var guest in sim.Guests)
                Assert.That(sim.SignalGuestVacatedRoom(guest.GuestId, guest.RoomId).Success, Is.True);
            foreach (var room in f.Rooms) ManualTurnoverModelAdapter.PrepareRoom(sim, room.Profile.Id);
            Assert.That(sim.Housekeeping.HasPendingWork, Is.False);
            Assert.That(f.Rooms.All(room => room.Cleanliness == Cleanliness.Clean && room.DepartingGuestId == null), Is.True);
            Assert.That(sim.Housekeeping.Linens.Count(item => item.Kind == LinenKind.Clean && item.Location == LinenLocation.Consumed), Is.EqualTo(6));
            Assert.That(sim.Boiler.Condition, Is.EqualTo(condition)); Assert.That(sim.Boiler.Pressure, Is.EqualTo(pressure));
            Assert.That(sim.Economy.Cash, Is.EqualTo(cash)); Assert.That(sim.DayReports.Count, Is.EqualTo(reports));
            Assert.That(sim.Guests.Select(guest => guest.QualityIntegral), Is.EqualTo(quality));
            Assert.That(sim.Guests.Select(guest => guest.Elapsed), Is.EqualTo(exposure));
            Start(f, 4, 2); Assert.That(sim.Elapsed, Is.Zero); Admit(f);
            Assert.That(sim.Guests[0].Agent.InAssignedRoom, Is.True);
        }

        [Test]
        public void InvalidCommandsAndNonfiniteTimeCannotAdvanceManualWorkOrCreateStock()
        {
            Assert.Throws<ArgumentException>(() => new HousekeepingSettings(0));
            Assert.Throws<ArgumentException>(() => new HousekeepingSettings(float.NaN));
            Assert.Throws<ArgumentException>(() => new HousekeepingSettings(float.PositiveInfinity));
            Assert.Throws<ArgumentException>(() => new HousekeepingSettings(cleanLinenPerDay: 0));
            Assert.Throws<ArgumentException>(() => new HousekeepingSettings(cleanLinenPerDay: 7));
            var f = Create(101); var sim = f.Simulation; var task = sim.Housekeeping.Find(101);
            Assert.That(sim.PickUpLinen(-1, task.DirtyLinenId).Success, Is.False);
            Assert.That(sim.PickUpLinen(0, "missing").Success, Is.False);
            Assert.That(sim.DropLinen(0, task.DirtyLinenId).Success, Is.False);
            Assert.That(sim.DepositDirtyLinen(0, task.DirtyLinenId).Success, Is.False);
            Assert.That(sim.BeginMakeBed(0, 999, "clean:0").Success, Is.False);
            Assert.That(sim.AdvanceMakeBed(0, 101, float.NaN).Success, Is.False);
            Assert.That(sim.AdvanceMakeBed(0, 101, -1).Success, Is.False);
            Assert.That(sim.AdvanceMakeBed(0, 101, float.PositiveInfinity).Success, Is.False);
            Assert.That(sim.CancelMakeBed(0, 101).Success, Is.False);
            Assert.That(sim.PrioritizeCleaning(0, 101).Success, Is.False, "There is no starting NPC cleaner to prioritize.");
            Assert.That(sim.SignalHousekeeperReachedRoom(101).Success, Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() => sim.AdvancePreparation(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => sim.AdvancePreparation(float.NaN));
            Assert.That(sim.Elapsed, Is.Zero); Assert.That(task.ProgressSeconds, Is.Zero);
            Assert.That(sim.Housekeeping.Tasks.Count, Is.EqualTo(1));
            Start(f); Assert.Throws<InvalidOperationException>(() => sim.AdvancePreparation(1));
        }

        [Test]
        public void CancelDropAndTimeAloneCannotConsumeLinenWhileSeparatePlayersCanMakeSeparateBeds()
        {
            var f = Create(101, 102); var sim = f.Simulation; var hk = sim.Housekeeping;
            ManualTurnoverModelAdapter.StripAndDeposit(sim, 101, 0);
            ManualTurnoverModelAdapter.StripAndDeposit(sim, 102, 1);
            string a = ManualTurnoverModelAdapter.TakeClean(sim, 0), b = ManualTurnoverModelAdapter.TakeClean(sim, 1);
            Assert.That(sim.BeginMakeBed(0, 101, a).Success, Is.True);
            Assert.That(sim.BeginMakeBed(1, 102, b).Success, Is.True);
            Assert.That(sim.AdvanceMakeBed(0, 101, .3f).Success, Is.True);
            Assert.That(sim.AdvanceMakeBed(1, 102, .4f).Success, Is.True);
            hk.SetWorkerAvailable(true); hk.Tick(100);
            Assert.That(hk.WorkerAvailable, Is.False);
            Assert.That(hk.Find(101).ProgressSeconds, Is.EqualTo(.3f).Within(.001f));
            Assert.That(hk.Find(102).ProgressSeconds, Is.EqualTo(.4f).Within(.001f));
            Assert.That(sim.CancelMakeBed(0, 101).Success, Is.True);
            Assert.That(hk.Find(101).ProgressSeconds, Is.Zero);
            Assert.That(hk.FindLinen(a).Location, Is.EqualTo(LinenLocation.HeldByPlayer));
            Assert.That(sim.DropLinen(1, b).Success, Is.True);
            Assert.That(hk.Find(102).ProgressSeconds, Is.Zero);
            Assert.That(hk.Find(102).WorkingPlayerId, Is.Null);
            Assert.That(hk.FindLinen(b).Location, Is.EqualTo(LinenLocation.Dropped));
            Assert.That(sim.PickUpLinen(8, b).Success, Is.True);
            ManualTurnoverModelAdapter.FinishBed(sim, 102, 8, b);
            Assert.That(f.Room(102).Cleanliness, Is.EqualTo(Cleanliness.Clean));
            Assert.That(f.Room(101).Cleanliness, Is.EqualTo(Cleanliness.Dirty));
        }

        [Test]
        public void DiagnosticCheckoutPreservesRealTurnoverAndPhysicalDepartureTokens()
        {
            var f = Create(); Start(f, 4); Admit(f, 0); Admit(f, 1, reachRoom: false); var sim = f.Simulation;
            var served = sim.Guests[0]; var travelling = sim.Guests[1]; var future = sim.Guests[2]; var visible = sim.Guests[3];
            Assert.That(sim.DebugCheckoutGuest(served.GuestId).Success, Is.True);
            Assert.That(served.Agent.State, Is.EqualTo(GuestAgentState.CheckingOut));
            Assert.That(f.Room(101).Cleanliness, Is.EqualTo(Cleanliness.Dirty));
            Assert.That(f.Room(101).DepartingGuestId, Is.EqualTo(served.GuestId));
            Assert.That(sim.DebugCheckoutGuest(served.GuestId).Success, Is.False);
            Assert.That(sim.DebugCheckoutGuest(travelling.GuestId).Success, Is.True);
            Assert.That(f.Room(102).Cleanliness, Is.EqualTo(Cleanliness.Clean));
            Assert.That(f.Room(102).DepartingGuestId, Is.EqualTo(travelling.GuestId));
            Assert.That(sim.DebugCheckoutGuest(future.GuestId).Success, Is.True);
            Assert.That(future.Agent.State, Is.EqualTo(GuestAgentState.Left));
            Assert.That(f.Room(103).Cleanliness, Is.EqualTo(Cleanliness.Clean)); Assert.That(f.Room(103).DepartingGuestId, Is.Null);
            AdvanceTo(f, visible.Agent.ArrivalTime + .2f);
            Assert.That(sim.DebugCheckoutGuest(visible.GuestId).Success, Is.True);
            Assert.That(visible.Agent.State, Is.EqualTo(GuestAgentState.Leaving)); Assert.That(f.Room(104).DepartingGuestId, Is.Null);
            Assert.That(sim.Boiler.Load, Is.Zero); Assert.That(f.Rooms.All(room => room.SourceNoise == 0), Is.True);
            Assert.That(sim.Electrical.Circuits.All(circuit => circuit.RequestedLoad == 0), Is.True);
            Assert.That(sim.Housekeeping.CurrentTask, Is.Null);
            Assert.That(sim.DebugCheckoutGuest("missing").Success, Is.False);
            sim.EndShift(); Assert.That(sim.DebugCheckoutGuest(served.GuestId).Success, Is.False);
        }

        [Test]
        public void DiagnosticDirtCreatesOneGenerationAndCannotAffectOccupiedOrDepartingRooms()
        {
            var f = Create(); var sim = f.Simulation;
            Assert.That(sim.DebugMarkRoomDirty(101).Success, Is.True);
            var task = sim.Housekeeping.Find(101); int generation = task.Generation;
            Assert.That(sim.DebugMarkRoomDirty(101).Success, Is.False);
            Assert.That(sim.DebugMarkRoomDirty(999).Success, Is.False);
            Assert.That(sim.Housekeeping.Find(101), Is.SameAs(task)); Assert.That(task.Generation, Is.EqualTo(generation));
            Start(f, 2); string reservation = f.Room(102).ReservedGuestId;
            Assert.That(sim.DebugMarkRoomDirty(102).Success, Is.True);
            Assert.That(f.Room(102).ReservedGuestId, Is.EqualTo(reservation)); Assert.That(sim.Housekeeping.Tasks.Count, Is.EqualTo(2));
            var protectedRoom = Create(); Start(protectedRoom); Admit(protectedRoom, reachRoom: false);
            Assert.That(protectedRoom.Simulation.DebugMarkRoomDirty(101).Success, Is.False);
            var guest = protectedRoom.Simulation.Guests.Single();
            Assert.That(protectedRoom.Simulation.DebugCheckoutGuest(guest.GuestId).Success, Is.True);
            Assert.That(protectedRoom.Room(101).DepartingGuestId, Is.EqualTo(guest.GuestId));
            Assert.That(protectedRoom.Simulation.DebugMarkRoomDirty(101).Success, Is.False);
            Assert.That(protectedRoom.Simulation.Housekeeping.Tasks, Is.Empty);
        }

        [Test]
        public void RefillRestocksOnlyConsumedSlotsOncePerDayAndDoesNotReclaimHeldOrDroppedLinen()
        {
            var f = Create(101); var sim = f.Simulation; var hk = sim.Housekeeping;
            ManualTurnoverModelAdapter.PrepareRoom(sim, 101);
            var consumed = hk.Linens.Single(item => item.Kind == LinenKind.Clean && item.Location == LinenLocation.Consumed);
            int generation = consumed.Generation;
            string held = ManualTurnoverModelAdapter.TakeClean(sim, 4), dropped = ManualTurnoverModelAdapter.TakeClean(sim, 7);
            Assert.That(sim.DropLinen(7, dropped).Success, Is.True);
            Assert.That(hk.RefillForDay(1).Success, Is.True);
            Assert.That(consumed.Location, Is.EqualTo(LinenLocation.Consumed));
            Assert.That(hk.RefillForDay(2).Success, Is.True);
            Assert.That(consumed.Location, Is.EqualTo(LinenLocation.OnShelf));
            Assert.That(consumed.Generation, Is.GreaterThan(generation));
            Assert.That(hk.FindLinen(held).PlayerId, Is.EqualTo(4));
            Assert.That(hk.FindLinen(dropped).Location, Is.EqualTo(LinenLocation.Dropped));
            int refillGeneration = consumed.Generation;
            Assert.That(hk.RefillForDay(2).Success, Is.True);
            Assert.That(consumed.Generation, Is.EqualTo(refillGeneration));
            Assert.That(hk.Linens.Count(item => item.Kind == LinenKind.Clean), Is.EqualTo(6));
            Assert.That(hk.Linens.Select(item => item.Id).Distinct().Count(), Is.EqualTo(hk.Linens.Count));
        }

        [Test]
        public void NewDirtyEpisodeReusesOneWorldIdentityButRejectsOldDeliveryAndOldTaskProgress()
        {
            var f = Create(101); var sim = f.Simulation; var hk = sim.Housekeeping; var previous = hk.Find(101);
            string dirtyId = previous.DirtyLinenId; int generation = previous.Generation;
            ManualTurnoverModelAdapter.PrepareRoom(sim, 101);
            Assert.That(sim.DebugMarkRoomDirty(101).Success, Is.True);
            var current = hk.Find(101);
            Assert.That(current, Is.Not.SameAs(previous)); Assert.That(current.Generation, Is.GreaterThan(generation));
            Assert.That(current.DirtyLinenId, Is.EqualTo(dirtyId));
            Assert.That(hk.FindLinen(dirtyId).Generation, Is.EqualTo(current.Generation));
            Assert.That(sim.DepositDirtyLinen(0, dirtyId).Success, Is.False);
            Assert.That(sim.AdvanceMakeBed(0, 101, .2f).Success, Is.False);
            Assert.That(current.Step, Is.EqualTo(RoomPreparationStep.DirtyLinenOnBed));
            Assert.That(current.ProgressSeconds, Is.Zero);
        }

        [Test]
        public void FreshSessionClearsTurnoverAndLegacyKernelDoesNotInventManualChores()
        {
            var previous = Create(); Start(previous); Admit(previous); previous.Simulation.EndShift();
            Assert.That(previous.Simulation.Housekeeping.HasPendingWork, Is.True);
            var fresh = Create();
            Assert.That(fresh.Simulation.Housekeeping.Tasks, Is.Empty);
            Assert.That(fresh.Rooms.All(room => room.DepartingGuestId == null && room.Cleanliness == Cleanliness.Clean), Is.True);
            Assert.That(fresh.Simulation.Housekeeping.Linens.Where(item => item.Kind == LinenKind.Clean).All(item => item.Location == LinenLocation.OnShelf), Is.True);
            var legacyRooms = fresh.Settings.Rooms.Select(profile => new RoomState(profile)).ToArray();
            var legacy = new HotelSimulation(fresh.Settings, legacyRooms);
            var offer = GuestSystem.GenerateApplications(1, fresh.Settings.GuestArchetypes).First();
            Assert.That(legacy.StartShift(new[] { new BookingAssignment(101, offer.Id, offer.ReferencePrice, 0) }, new[] { offer }).Success, Is.True);
            legacy.Tick(1); legacy.EndShift();
            Assert.That(legacy.Housekeeping, Is.Null);
            Assert.That(legacyRooms.All(room => room.Cleanliness == Cleanliness.Clean && room.DepartingGuestId == null), Is.True);
            float time = legacy.Elapsed; legacy.AdvancePreparation(30); Assert.That(legacy.Elapsed, Is.EqualTo(time));
        }
    }
}
