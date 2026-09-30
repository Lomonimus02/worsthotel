using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace WorstHotel.Tests
{
    public sealed class OperatingSuppliesTests
    {
        static HotelSimulation Create(int cash = 10000, bool living = true)
        {
            var profiles = new[] {
                new GuestProfile(GuestKind.Budget, "Budget", "", 180, .85f, 18, .7f, 28, 90, .55f),
                new GuestProfile(GuestKind.ColdSensitive, "Cold", "", 300, 1.05f, 20, 1.35f, 16, 65, .4f),
                new GuestProfile(GuestKind.Business, "Business", "", 450, 1, 19.5f, 1, 10, 45, .25f) };
            var settings = new SessionSettings(profiles, Enumerable.Range(101, 6).Select(id => new RoomProfile(id, "Room " + id)),
                new BoilerSettings(), new EconomySettings(startingCash: cash));
            var hotel = new HotelSimulation(settings, settings.Rooms.Select(room => new RoomState(room)).ToArray(),
                living ? new LivingHotelSettings(firstActivityDelay: 1000) : null,
                housekeeping: new HousekeepingSettings(cleanLinenPerDay: 2),
                services: living ? new GuestServiceSettings(eligibility: 0) : null,
                operations: new OperationsSettings(sales: new SalesSettings(enabled: false)));
            Good(hotel.StartOperations());
            return hotel;
        }

        static void Good(CommandResult result) => Assert.That(result.Success, Is.True, result.Message);
        static void Advance(HotelSimulation hotel, float target)
        { while (hotel.Elapsed < target) hotel.Tick(Math.Min(1, target - hotel.Elapsed)); }
        static HotelModelSnapshot Wire(HotelSimulation hotel, long sequence) =>
            JsonUtility.FromJson<HotelModelSnapshot>(JsonUtility.ToJson(hotel.CaptureSnapshot(965, sequence)));
        static string State(HotelSimulation hotel) => JsonUtility.ToJson(hotel.CaptureSnapshot(965, 1));

        static void Deposit(HotelSimulation hotel, int room)
        {
            Good(hotel.DebugMarkRoomDirty(room)); // Labelled vacant-room setup; use real bundle/bed commands below.
            string id = hotel.Housekeeping.Find(room).DirtyLinenId;
            Good(hotel.PickUpLinen(0, id));
            Good(hotel.DepositDirtyLinen(0, id));
            Assert.That(hotel.DepositDirtyLinen(0, id).Success, Is.False, "A deposited generation cannot be counted twice.");
        }

        static void MakeBed(HotelSimulation hotel, int room)
        {
            var clean = hotel.Housekeeping.Linens.First(l => l.Kind == LinenKind.Clean && l.Location == LinenLocation.OnShelf);
            Good(hotel.PickUpLinen(0, clean.Id));
            Good(hotel.BeginMakeBed(0, room, clean.Id));
            Good(hotel.AdvanceMakeBed(0, room, hotel.Housekeeping.Settings.MakeBedSeconds));
        }

        [Test]
        public void LaundryCostsOnceReturnsLaterAndKeepsOverflowUntilAPhysicalSlotIsFree()
        {
            var h = Create();
            Assert.That(h.NextSupplyDeliveryAt, Is.EqualTo(float.PositiveInfinity));
            Assert.That(h.LastSupplyDeliveryAt, Is.EqualTo(-1));
            Deposit(h, 101);
            int revision = h.SupplyRevision, cash = h.Economy.Cash;
            Good(h.OrderLaundry(0, revision));
            Assert.That(h.Economy.Cash, Is.EqualTo(cash - 18));
            Assert.That(h.LinenAtLaundry, Is.EqualTo(1));
            Assert.That(h.DirtyLinenWaiting, Is.Zero);
            Assert.That(h.OrderLaundry(1, revision).Success, Is.False);
            float due = h.NextSupplyDeliveryAt;
            Assert.That(due, Is.EqualTo(h.Calendar.At(2, 6)));
            Advance(h, due - .25f);
            Assert.That(h.LinenAtLaundry, Is.EqualTo(1));
            Assert.That(h.CleanLinenReserve, Is.Zero);
            Advance(h, due);
            Assert.That(h.LinenAtLaundry, Is.Zero);
            Assert.That(h.CleanLinenAvailable, Is.EqualTo(2));
            Assert.That(h.CleanLinenReserve, Is.EqualTo(1), "Paid sets cannot disappear when the shelf is full.");
            Assert.That(h.LastSupplyDeliveryAt, Is.EqualTo(due));
            int deliveredCash = h.Economy.Cash;
            MakeBed(h, 101);
            Assert.That(h.CleanLinenReserve, Is.Zero);
            Assert.That(h.CleanLinenAvailable, Is.EqualTo(2));
            Advance(h, due + 1);
            Assert.That(h.Economy.Cash, Is.EqualTo(deliveredCash));
            Assert.That(h.NextSupplyDeliveryAt, Is.EqualTo(float.PositiveInfinity));
            var mirror = Create(); mirror.EnableReadOnlyMirror();
            Good(mirror.ApplySnapshot(Wire(h, 1)));
            Assert.That(State(mirror), Is.EqualTo(State(h)));
        }

        [Test]
        public void LinenCanRunOutAndMidnightDoesNotPrepareRoomsOrReplenishIt()
        {
            var h = Create();
            Deposit(h, 101); MakeBed(h, 101);
            Deposit(h, 102); MakeBed(h, 102);
            Deposit(h, 103);
            Assert.That(h.CleanLinenAvailable, Is.Zero);
            Advance(h, h.Calendar.At(2, 7));
            Assert.That(h.CleanLinenAvailable, Is.Zero);
            Assert.That(h.DirtyLinenWaiting, Is.EqualTo(3));
            Assert.That(h.Housekeeping.Find(103).Step, Is.EqualTo(RoomPreparationStep.NeedsCleanLinen));
            Assert.That(h.Housekeeping.RefillForDay(3).Success, Is.False);
            Assert.That(h.PickUpLinen(0, "clean:0").Success, Is.False);
            Good(h.OrderLaundry(0, h.SupplyRevision));
            Advance(h, h.NextSupplyDeliveryAt);
            Assert.That(h.CleanLinenAvailable, Is.EqualTo(2));
            Assert.That(h.CleanLinenReserve, Is.EqualTo(1));
            MakeBed(h, 103);
            Assert.That(h.Housekeeping.Find(103), Is.Null);
            Assert.That(h.CleanLinenAvailable, Is.EqualTo(2));
            Assert.That(h.CleanLinenReserve, Is.Zero);
        }

        [Test]
        public void BulbOrdersReserveWholePacksAndNeverOverwriteHeldStockOrRefillForFree()
        {
            var h = Create();
            Good(h.BreakRoomLamp(101));
            Good(h.TakeServiceItem(0, "bulb:0")); Good(h.ReplaceRoomBulb(0, 101));
            Good(h.TakeServiceItem(1, "bulb:1"));
            int heldGeneration = h.Services.FindItem("bulb:1").Generation;
            Advance(h, h.Calendar.At(2, 7));
            Assert.That(h.Services.FindItem("bulb:0").Location, Is.EqualTo(ServiceItemLocation.Delivered));
            int cash = h.Economy.Cash;
            Good(h.OrderBulbs(0, h.SupplyRevision));
            Assert.That(h.Economy.Cash, Is.EqualTo(cash - 45));
            Assert.That(h.BulbsInTransit, Is.EqualTo(3));
            string before = State(h);
            Assert.That(h.OrderBulbs(0, h.SupplyRevision).Success, Is.False);
            Assert.That(State(h), Is.EqualTo(before));
            Advance(h, h.NextSupplyDeliveryAt);
            Assert.That(h.BulbsInTransit, Is.Zero);
            Assert.That(h.Services.BulbsAvailable, Is.EqualTo(4));
            Assert.That(h.Services.FindItem("bulb:1").PlayerId, Is.EqualTo(1));
            Assert.That(h.Services.FindItem("bulb:1").Generation, Is.EqualTo(heldGeneration));
        }

        [Test]
        public void SharedDeliveryIsStrictlyFutureAndMirrorCanPreviewButCannotPurchase()
        {
            var h = Create(); Deposit(h, 101);
            var mirror = Create(); mirror.EnableReadOnlyMirror();
            Good(mirror.ApplySnapshot(Wire(h, 1)));
            Good(mirror.CanOrderLaundry(1, mirror.SupplyRevision));
            string laundryBefore = State(mirror);
            Assert.That(mirror.OrderLaundry(1, mirror.SupplyRevision).Success, Is.False);
            Assert.That(State(mirror), Is.EqualTo(laundryBefore));
            Good(h.OrderLaundry(0, h.SupplyRevision));
            Good(mirror.ApplySnapshot(Wire(h, 2)));
            Good(mirror.CanOrderBulbs(1, mirror.SupplyRevision));
            string before = State(mirror);
            Assert.That(mirror.OrderBulbs(1, mirror.SupplyRevision).Success, Is.False);
            Assert.That(State(mirror), Is.EqualTo(before));
            float first = h.NextSupplyDeliveryAt;
            Good(h.OrderBulbs(1, h.SupplyRevision));
            Assert.That(h.NextSupplyDeliveryAt, Is.EqualTo(first));
            Advance(h, first);
            Deposit(h, 102);
            Good(h.OrderLaundry(0, h.SupplyRevision));
            Assert.That(h.NextSupplyDeliveryAt, Is.EqualTo(h.Calendar.At(3, 6)));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UsedBlanketReturnsOnlyAfterCompletedTurnoverEvenWhenGuestHistoryWasPruned(bool pruneHistory)
        {
            var h = Create();
            Good(h.DebugSpawnGuest(GuestKind.Budget, 101));
            Advance(h, h.Elapsed + 1.25f);
            var guest = h.Guests.Single();
            // Explicit headless arrival adapters; delivery and turnover still use normal model commands.
            Good(h.SignalGuestReachedReception(guest.GuestId));
            Good(ModelKeyHandoff.CheckIn(h, 0, guest.GuestId));
            Good(h.SignalGuestReachedRoom(guest.GuestId));
            Good(h.RegisterGuestPhysicalStaging(guest.GuestId));
            Good(h.SignalGuestActivityReady(guest.GuestId, GuestAgentState.InRoom, GuestActivity.QuietRest));
            Good(h.TakeServiceItem(0, "blanket:0"));
            Good(h.DeliverBlanket(0, guest.GuestId));
            var blanket = h.Services.FindItem("blanket:0");
            Assert.That(h.Services.BlanketsAvailable, Is.EqualTo(2));
            Good(h.DebugCheckoutGuest(guest.GuestId));
            Good(h.SignalGuestVacatedRoom(guest.GuestId, 101));
            if (pruneHistory)
            {
                // Exercise the actual retention hook without waiting many unrelated report periods.
                var prune = typeof(GuestServiceSystem).GetMethod("PruneCompletedStays", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(prune, Is.Not.Null);
                prune.Invoke(h.Services, new object[] { new HashSet<string>(), new HashSet<string>(), new HashSet<string>() });
                Assert.That(blanket.GuestId, Is.Null);
                Assert.That(blanket.RoomId, Is.EqualTo(101), "Pruning must retain the turnover recovery association.");
            }
            int generation = blanket.Generation, cash = h.Economy.Cash;
            string dirty = h.Housekeeping.Find(101).DirtyLinenId;
            Good(h.PickUpLinen(0, dirty)); Good(h.DepositDirtyLinen(0, dirty));
            Good(h.PickUpLinen(0, "clean:0")); Good(h.BeginMakeBed(0, 101, "clean:0"));
            float half = h.Housekeeping.Settings.MakeBedSeconds / 2;
            Good(h.AdvanceMakeBed(0, 101, half));
            Assert.That(blanket.Location, Is.EqualTo(ServiceItemLocation.Delivered));
            Assert.That(h.Services.BlanketsAvailable, Is.EqualTo(2));
            Good(h.AdvanceMakeBed(0, 101, half));
            Assert.That(blanket.Location, Is.EqualTo(ServiceItemLocation.OnShelf));
            Assert.That(blanket.RoomId, Is.Null);
            Assert.That(blanket.GuestId, Is.Null);
            Assert.That(blanket.Generation, Is.EqualTo(generation + 1));
            Assert.That(h.Services.BlanketsAvailable, Is.EqualTo(3));
            Assert.That(h.Economy.Cash, Is.EqualTo(cash), "Reusing a blanket does not purchase a replacement.");
        }

        [Test]
        public void FailedOrdersDoNotChargeOrChangeStock()
        {
            var h = Create(cash: 17); Deposit(h, 101);
            string before = State(h);
            Assert.That(h.OrderLaundry(0, h.SupplyRevision).Success, Is.False);
            Assert.That(h.OrderBulbs(0, h.SupplyRevision).Success, Is.False);
            Assert.That(h.OrderLaundry(2, h.SupplyRevision).Success, Is.False);
            Assert.That(h.OrderLaundry(0, h.SupplyRevision - 1).Success, Is.False);
            Assert.That(State(h), Is.EqualTo(before));
        }

        [Test]
        public void ContinuousHotelWithoutLivingSubsystemHasAnEmptySupplyRegistry()
        {
            var host = Create(living: false); var mirror = Create(living: false);
            mirror.EnableReadOnlyMirror();
            var packet = host.CaptureSnapshot(965, 1);
            Assert.That(packet.Operations.Supplies.Deposits, Is.Empty);
            Good(mirror.ApplySnapshot(packet));
            Assert.That(State(mirror), Is.EqualTo(State(host)));
        }

        [TestCase("duplicate-deposit")]
        [TestCase("future-generation")]
        [TestCase("extra-linen")]
        [TestCase("bulb-overbook")]
        [TestCase("past-delivery")]
        [TestCase("config")]
        public void InvalidSupplySnapshotsRejectBeforeMutationOrSequenceConsumption(string change)
        {
            var host = Create(); Deposit(host, 101); Good(host.OrderLaundry(0, host.SupplyRevision));
            var mirror = Create(); mirror.EnableReadOnlyMirror();
            Good(mirror.ApplySnapshot(Wire(host, 1)));
            var packet = Wire(host, 2); var supply = packet.Operations.Supplies;
            switch (change)
            {
                case "duplicate-deposit": supply.Deposits[1].RoomId = supply.Deposits[0].RoomId; break;
                case "future-generation": supply.Deposits[0].Generation = int.MaxValue; break;
                case "extra-linen": supply.CleanLinenReserve++; break;
                case "bulb-overbook": supply.BulbsInTransit = 6; break;
                case "past-delivery": supply.NextDeliveryAt = packet.Time; break;
                case "config": supply.DeliveryHour = 7; break;
            }
            string before = State(mirror);
            Assert.That(mirror.ApplySnapshot(packet).Success, Is.False);
            Assert.That(State(mirror), Is.EqualTo(before));
            Assert.That(mirror.AppliedSnapshotSequence, Is.EqualTo(1));
            Good(mirror.ApplySnapshot(Wire(host, 2)));
        }
    }
}
