using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace WorstHotel.Tests
{
    public sealed class OperationalChaosTests
    {
        static void Good(CommandResult result) => Assert.That(result.Success, Is.True, result.Message);
        static HotelSimulation Create(out RoomState[] rooms, int count = 6)
        {
            var profiles = Enum.GetValues(typeof(GuestKind)).Cast<GuestKind>().Select(kind =>
                new GuestProfile(kind, kind.ToString(), "", 180, 1, 18, 1, 10, 60, .9f,
                    needs: new NeedProfile(0, 40, -10, 50, .9f, 1, 60))).ToArray();
            var settings = new SessionSettings(profiles, Enumerable.Range(101, count).Select(id => new RoomProfile(id, "Room " + id)),
                new BoilerSettings(baseWearPerMinute: 0, overloadWearPerMinute: 0), new EconomySettings(startingCash: 20000));
            rooms = settings.Rooms.Select(r => new RoomState(r)).ToArray();
            var h = new HotelSimulation(settings, rooms, new LivingHotelSettings(firstActivityDelay: 1000),
                needs: new NeedSettings(earlyCheckout: new EarlyCheckoutSettings(enabled: true)),
                housekeeping: new HousekeepingSettings(cleanLinenPerDay: 10), services: new GuestServiceSettings(eligibility: 0),
                operations: new OperationsSettings(sales: new SalesSettings(enabled: false)));
            Good(h.StartOperations()); if (count > 6) Good(h.RestoreNorthWing(0)); return h;
        }
        static void To(HotelSimulation h, float time, Action routeAdapter = null)
        { while (time - h.Elapsed > .001f) { h.Tick(Math.Min(.25f, time - h.Elapsed)); routeAdapter?.Invoke(); } }
        static GuestStay Arrival(HotelSimulation h, int room = 102)
        {
            var offer = h.BookingOffers.First(o => o.ArrivalDay == 1 && o.ArrivalAt > h.Elapsed);
            Good(h.AcceptBooking(0, offer.Id, room, 180)); To(h, offer.ArrivalAt + .25f);
            var g = h.Guests.Single(s => s.GuestId == offer.Id); Good(h.SignalGuestReachedReception(g.GuestId)); return g;
        }
        static void CheckIn(HotelSimulation h, GuestStay g)
        { Good(h.Keys.PickUp(0, g.RoomId)); Good(h.CheckIn(0, g.GuestId)); Good(h.SignalGuestReachedRoom(g.GuestId)); }
        static void Prepare(HotelSimulation h, int room)
        {
            var t = h.Housekeeping.Find(room); Good(h.PickUpLinen(0, t.DirtyLinenId)); Good(h.DepositDirtyLinen(0, t.DirtyLinenId));
            var clean = h.Housekeeping.Linens.First(l => l.Kind == LinenKind.Clean && l.Location == LinenLocation.OnShelf);
            Good(h.PickUpLinen(0, clean.Id)); Good(h.BeginMakeBed(0, room, clean.Id));
            Good(h.AdvanceMakeBed(0, room, h.Housekeeping.Settings.MakeBedSeconds));
        }
        static void Mirror(HotelSimulation host, int count = 6)
        {
            var mirror = Create(out _, count); mirror.EnableReadOnlyMirror();
            var packet = JsonUtility.FromJson<HotelModelSnapshot>(JsonUtility.ToJson(host.CaptureSnapshot(966, 1)));
            Good(mirror.ApplySnapshot(packet));
            Assert.That(JsonUtility.ToJson(mirror.CaptureSnapshot(966, 1)), Is.EqualTo(JsonUtility.ToJson(packet)));
            Assert.That(mirror.Housekeeping.ResetRoomElement(101, RoomDisorder.Waste).Success, Is.False);
        }
        [Test]
        public void StartHasOneUsedBedAndItSurvivesTwoCalendarBoundaries()
        {
            var h = Create(out var rooms);
            Assert.That(h.Housekeeping.Tasks.Count, Is.EqualTo(1));
            Assert.That(h.Housekeeping.FindLinen("dirty:101").Location, Is.EqualTo(LinenLocation.OnBed));
            To(h, h.Calendar.At(2, 7));
            Assert.That(rooms[0].Cleanliness, Is.EqualTo(Cleanliness.Dirty));
            Assert.That(h.Housekeeping.Tasks.Count, Is.EqualTo(1));
            Prepare(h, 101); Assert.That(rooms[0].UsedHours, Is.Zero); Mirror(h);
        }
        [Test]
        public void RealShowerAndRoomTimeCreateDeeperWorkThatBedCannotBypass()
        {
            var h = Create(out var rooms); var g = Arrival(h); CheckIn(h, g);
            Good(h.ForceActivity(g.GuestId, GuestActivity.Shower)); To(h, h.Elapsed + 15);
            Good(h.ForceActivity(g.GuestId, GuestActivity.Shower)); To(h, h.Elapsed + 15);
            // Ordinary room time, not a turnover-type injection.
            To(h, h.Elapsed + 125, () => { if (g.Agent.InAssignedRoom) Good(h.ForceActivity(g.GuestId, GuestActivity.QuietRest)); });
            Assert.That(rooms[1].Disorder.HasFlag(RoomDisorder.Waste), Is.True);
            Assert.That(rooms[1].Disorder.HasFlag(RoomDisorder.Towels), Is.True);
            Assert.That(h.Housekeeping.ResetRoomElement(102, RoomDisorder.Towels).Success, Is.False, "Occupied rooms cannot be reset under their guest.");
            Good(h.DebugCheckoutGuest(g.GuestId)); To(h, h.Elapsed + .25f);
            Good(h.SignalGuestVacatedRoom(g.GuestId, 102));
            Good(h.PickUpLinen(0, "dirty:102")); Good(h.DepositDirtyLinen(0, "dirty:102"));
            Good(h.PickUpLinen(0, "clean:0")); Assert.That(h.BeginMakeBed(0, 102, "clean:0").Success, Is.False);
            Good(h.Housekeeping.ResetRoomElement(102, RoomDisorder.Waste)); Good(h.Housekeeping.ResetRoomElement(102, RoomDisorder.Towels));
            if (rooms[1].Disorder.HasFlag(RoomDisorder.Chair)) Good(h.Housekeeping.ResetRoomElement(102, RoomDisorder.Chair));
            Good(h.BeginMakeBed(0, 102, "clean:0")); Good(h.AdvanceMakeBed(0, 102, h.Housekeeping.Settings.MakeBedSeconds));
            Assert.That(rooms[1].Cleanliness, Is.EqualTo(Cleanliness.Clean)); Mirror(h);
        }
        [Test]
        public void IgnoredUnpreparedArrivalLeavesWithNoChargeAndRoomStaysDirty()
        {
            var h = Create(out var rooms); var g = Arrival(h, 101); int cash = h.Economy.Cash;
            Assert.That(h.CheckIn(0, g.GuestId).Success, Is.False);
            To(h, h.Elapsed + h.CheckInPatience(g) * 3 + 1);
            Assert.That(g.AbandonedCheckIn && g.ReceiptPosted, Is.True);
            Assert.That(h.CurrentReceipts.Single().Net, Is.Zero); Assert.That(h.Economy.Cash, Is.EqualTo(cash));
            Assert.That(rooms[0].Cleanliness, Is.EqualTo(Cleanliness.Dirty)); Mirror(h);
        }
        [TestCase(false)] [TestCase(true)]
        public void ForgottenKeyUsesStaffKeyOrExistingEarlyCheckoutRefund(bool ignore)
        {
            var h = Create(out _); var g = Arrival(h); CheckIn(h, g); To(h, h.Elapsed + 2);
            // Isolate the rare opportunity; all subsequent outing/return/door/receipt transitions are production code.
            typeof(RoomKeySystem).GetMethod("LeaveInside", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(h.Keys, new object[] { g.GuestId, 102 });
            typeof(GuestStay).GetProperty("KeyLossConsidered").SetValue(g, true);
            Good(h.ForceLeaveRoom(g.GuestId)); Good(h.SignalGuestLeftRoom(g.GuestId)); Good(h.ForceReturnRoom(g.GuestId));
            Good(h.SignalGuestReachedReception(g.GuestId)); Assert.That(g.LockedOut, Is.True); Mirror(h);
            if (ignore)
            {
                To(h, h.Elapsed + h.CheckInPatience(g) * 4 + 1);
                Assert.That(g.ReceiptPosted, Is.True); Assert.That(h.CurrentReceipts.Single().EarlyCheckout, Is.True);
                Assert.That(h.CurrentReceipts.Single().Compensation, Is.GreaterThan(0)); Mirror(h); return;
            }
            Assert.That(h.UnlockForGuest(0, 102).Success, Is.False);
            Good(h.Keys.PickUp(0, 0)); Assert.That(h.UnlockForGuest(1, 102).Success, Is.False);
            Good(h.UnlockForGuest(0, 102)); Assert.That(g.LockedOut, Is.False);
            Assert.That(h.Keys.Find(102).Location, Is.EqualTo(RoomKeyLocation.LeftInside));
            Good(h.SignalGuestReturnedRoom(g.GuestId)); Assert.That(h.Keys.Find(102).Location, Is.EqualTo(RoomKeyLocation.HeldByGuest)); Mirror(h);
        }
        [TestCase(2)] [TestCase(6)] [TestCase(10)]
        public void MoreActualStaysLeaveMoreRoomsToPrepare(int occupied)
        {
            var h = Create(out var rooms, occupied); Prepare(h, 101);
            foreach (var pair in h.BookingOffers.Where(o => o.ArrivalDay == 1).Take(occupied).Select((o, i) => (o, i)).ToArray())
                Good(h.AcceptBooking(0, pair.o.Id, 101 + pair.i, 180));
            // Explicit model travel adapter, not a claim of a human playthrough.
            To(h, h.Calendar.At(2, 10) + 4, () =>
            {
                foreach (var g in h.Guests.ToArray())
                {
                    if (g.Agent.State == GuestAgentState.Arriving) Good(h.SignalGuestReachedReception(g.GuestId));
                    if (g.Agent.State == GuestAgentState.WaitingForCheckIn && !g.LockedOut) CheckIn(h, g);
                    if (g.Agent.State == GuestAgentState.LeavingRoom) Good(h.SignalGuestLeftRoom(g.GuestId));
                    if (g.Agent.State == GuestAgentState.ReturningToRoom) Good(h.SignalGuestReturnedRoom(g.GuestId));
                    if (g.LockedOut) { Good(h.Keys.PickUp(0, 0)); Good(h.UnlockForGuest(0, g.RoomId)); Good(h.Keys.Drop(0, 0)); }
                    if (rooms.Any(r => r.DepartingGuestId == g.GuestId)) Good(h.SignalGuestVacatedRoom(g.GuestId, g.RoomId));
                    if (g.Agent.State == GuestAgentState.Leaving) Good(h.SignalGuestLeft(g.GuestId));
                }
            });
            Assert.That(h.Housekeeping.Tasks.Count, Is.EqualTo(occupied));
            Assert.That(rooms.Count(r => r.Disorder != RoomDisorder.None), Is.EqualTo(occupied));
            TestContext.Out.WriteLine(occupied + " stays -> " + h.Housekeeping.Tasks.Count + " linen changes; " +
                rooms.Sum(r => (r.Disorder.HasFlag(RoomDisorder.Waste) ? 1 : 0) + (r.Disorder.HasFlag(RoomDisorder.Towels) ? 1 : 0) + (r.Disorder.HasFlag(RoomDisorder.Chair) ? 1 : 0)) + " additional physical resets");
            Mirror(h, occupied);
        }

        [Test]
        public void PromisedBaggageHasConsequencesAndStoredBaggageNeedsPhysicalLostPropertyWork()
        {
            var h = Create(out _); var g = Arrival(h, 101);
            // The dirty starting room gives a concrete reason to accept storage.
            Good(h.Services.OfferLuggage(0, g.GuestId, true));
            var bag = h.Services.Items.First(i => i.Kind == ServiceItemKind.Luggage && i.GuestId == g.GuestId);
            Good(h.Services.TakeItem(0, bag.Id)); Good(h.Services.DropItem(0, bag.Id)); Good(h.Services.PlaceLuggage(bag.Id, true));
            To(h, h.Elapsed + h.CheckInPatience(g) * 3 + 1); Good(h.SignalGuestLeft(g.GuestId));
            To(h, h.Calendar.At(5, 7));
            Assert.That(h.Services.FindItem(bag.Id), Is.Not.Null); Assert.That(bag.Location, Is.EqualTo(ServiceItemLocation.Stored));
            Good(h.Services.TakeItem(0, bag.Id)); Assert.That(h.Services.FileLostProperty(1, bag.Id).Success, Is.False);
            Good(h.Services.FileLostProperty(0, bag.Id)); Assert.That(bag.Location, Is.EqualTo(ServiceItemLocation.LostProperty)); Mirror(h);
        }
    }
}
