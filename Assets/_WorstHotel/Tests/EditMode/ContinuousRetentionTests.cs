using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace WorstHotel.Tests
{
    public sealed class ContinuousRetentionTests
    {
        sealed class Fixture
        {
            public readonly HotelSimulation Hotel;
            public readonly RoomState[] Rooms;
            public Fixture()
            {
                var comfort = new NeedProfile(5, 40, 0, 45, .8f, 1, 90);
                var profiles = new[] {
                    new GuestProfile(GuestKind.Budget, "Budget", "", 180, .85f, 18, .7f, 28, 90, .55f, needs: comfort),
                    new GuestProfile(GuestKind.ColdSensitive, "Cold", "", 300, 1.05f, 20, 1.35f, 16, 65, .4f, needs: comfort),
                    new GuestProfile(GuestKind.Business, "Business", "", 450, 1, 19.5f, 1, 10, 45, .25f, needs: comfort) };
                var settings = new SessionSettings(profiles, Enumerable.Range(101, 6).Select(id => new RoomProfile(id, "Room " + id, noise: 0)),
                    new BoilerSettings(safeLoad: 100, baseWearPerMinute: 0, overloadWearPerMinute: 0), new EconomySettings());
                Rooms = settings.Rooms.Select(room => new RoomState(room)).ToArray();
                Hotel = new HotelSimulation(settings, Rooms, new LivingHotelSettings(firstActivityDelay: 10000,
                    quietDurationMin: 10000, quietDurationMax: 10000),
                    services: new GuestServiceSettings(eligibility: 0, naturalCommunicationEnabled: true),
                    infrastructure: new RoomInfrastructureSettings(lampWearPerSecond: 0), operations: new OperationsSettings());
                Require(Hotel.StartOperations());
            }
        }
        static void Require(CommandResult result) => Assert.That(result.Success, Is.True, result.Message);
        static void Prune(HotelSimulation hotel) => typeof(HotelSimulation).GetMethod("PruneCompletedOperatingHistory",
            BindingFlags.Instance | BindingFlags.NonPublic).Invoke(hotel, null);
        static void Advance(Fixture fixture, float until, string keepBody = null)
        {
            var hotel = fixture.Hotel;
            while (hotel.Elapsed < until)
            {
                hotel.Tick(Math.Min(1, until - hotel.Elapsed));
                // Explicit model adapters for real scene route completion. They do not stand
                // in for physical integration tests or claim to measure human pacing.
                foreach (var guest in hotel.Guests.ToArray())
                {
                    if (guest.Agent.State == GuestAgentState.Arriving)
                    {
                        Require(hotel.SignalGuestReachedReception(guest.GuestId));
                        Require(ModelKeyHandoff.CheckIn(hotel, 0, guest.GuestId));
                        Require(hotel.SignalGuestReachedRoom(guest.GuestId));
                    }
                    if (guest.Agent.State == GuestAgentState.LeavingRoom) Require(hotel.SignalGuestLeftRoom(guest.GuestId));
                    if (guest.Agent.State == GuestAgentState.ReturningToRoom) Require(hotel.SignalGuestReturnedRoom(guest.GuestId));
                    if (guest.Agent.State == GuestAgentState.Leaving && guest.GuestId != keepBody)
                    {
                        var room = fixture.Rooms.Single(item => item.Profile.Id == guest.RoomId);
                        if (room.DepartingGuestId == guest.GuestId) Require(hotel.SignalGuestVacatedRoom(guest.GuestId, guest.RoomId));
                        Require(hotel.SignalGuestLeft(guest.GuestId));
                    }
                }
            }
        }
        static void Prepare(Fixture fixture)
        {
            var hotel = fixture.Hotel;
            foreach (var room in fixture.Rooms.Where(item => item.Cleanliness == Cleanliness.Dirty))
            {
                string dirty = "dirty:" + room.Profile.Id;
                Require(hotel.PickUpLinen(1, dirty)); Require(hotel.DepositDirtyLinen(1, dirty));
                string clean = hotel.Housekeeping.Linens.First(item => item.Kind == LinenKind.Clean && item.Location == LinenLocation.OnShelf).Id;
                Require(hotel.PickUpLinen(1, clean)); Require(hotel.BeginMakeBed(1, room.Profile.Id, clean));
                Require(hotel.AdvanceMakeBed(1, room.Profile.Id, hotel.Housekeeping.Settings.MakeBedSeconds));
            }
        }
        static void Mirror(HotelSimulation source, HotelSimulation mirror, long sequence)
        {
            var snapshot = JsonUtility.FromJson<HotelModelSnapshot>(JsonUtility.ToJson(source.CaptureSnapshot(304, sequence)));
            Require(mirror.ApplySnapshot(snapshot));
            Assert.That(mirror.Guests.Select(item => item.GuestId), Is.EqualTo(source.Guests.Select(item => item.GuestId)));
            Assert.That(mirror.Economy.Cash, Is.EqualTo(source.Economy.Cash));
        }

        [Test]
        public void ThirtyDaysOfSixStaysRetireOldRecordsAndStillAcceptFreshServiceAndWireSnapshots()
        {
            var fixture = new Fixture(); var hotel = fixture.Hotel;
            var replica = new Fixture().Hotel; replica.EnableReadOnlyMirror();
            string firstReceiptId = null;
            for (int day = 1; day <= 30; day++)
            {
                Prepare(fixture);
                var offers = hotel.BookingOffers.Where(item => item.ArrivalDay == day).Take(6).ToArray();
                Assert.That(offers.Length, Is.EqualTo(6));
                for (int index = 0; index < offers.Length; index++)
                    Require(hotel.AcceptBooking(0, offers[index].Id, 101 + index, offers[index].Application.ReferencePrice));
                firstReceiptId = firstReceiptId ?? offers[0].Id;
                Advance(fixture, hotel.Calendar.At(day + 1, 9));
                foreach (var guest in hotel.Guests.Where(item => !item.ReceiptPosted))
                {
                    Assert.That(guest.Agent.InAssignedRoom, Is.True, "Day " + day + " " + guest.GuestId + ": " + guest.Agent.State);
                    Require(hotel.ForceActivity(guest.GuestId, GuestActivity.QuietRest));
                    Require(hotel.DebugForceService(guest.GuestId, ServiceKind.LateCheckout));
                    Assert.That(hotel.Services.Responses.Any(item => item.GuestId == guest.GuestId), Is.True, "Fresh response on day " + day);
                }
                Mirror(hotel, replica, day * 2);
                Advance(fixture, hotel.Calendar.At(day + 1, 11));
                Assert.That(hotel.Guests.Count, Is.LessThanOrEqualTo(18), "No lifetime guest accumulation on day " + day);
                Assert.That(hotel.Reservations.Count, Is.LessThanOrEqualTo(18));
                Assert.That(hotel.Services.Cases.Count, Is.LessThanOrEqualTo(256));
                Assert.That(hotel.Services.Items.Count, Is.LessThanOrEqualTo(140));
                Assert.That(hotel.Services.Responses.Count, Is.LessThanOrEqualTo(512));
                Mirror(hotel, replica, day * 2 + 1);
            }
            Assert.That(hotel.Guests.Any(item => item.GuestId == firstReceiptId), Is.False);
            Assert.That(hotel.DayReports.Any(report => report.Receipts.Any(receipt => receipt.GuestId == firstReceiptId)), Is.True,
                "Published receipts remain readable without retaining departed simulation entities.");
        }

        [Test]
        public void OldPhysicalDeparturePinsIdentityUntilItsActualExitThenPrunesQuietly()
        {
            var fixture = new Fixture(); var hotel = fixture.Hotel;
            var offer = hotel.BookingOffers.First();
            Require(hotel.AcceptBooking(0, offer.Id, 101, offer.Application.ReferencePrice));
            Advance(fixture, hotel.Calendar.At(8, 8), offer.Id);
            var guest = hotel.Guests.Single();
            Assert.That(guest.Agent.State, Is.EqualTo(GuestAgentState.Leaving));
            Assert.That(fixture.Rooms[0].DepartingGuestId, Is.EqualTo(guest.GuestId));
            var replica = new Fixture().Hotel; replica.EnableReadOnlyMirror(); Mirror(hotel, replica, 1);
            Require(hotel.SignalGuestVacatedRoom(guest.GuestId, guest.RoomId));
            Require(hotel.SignalGuestLeft(guest.GuestId));
            int revision = hotel.EventRevision, cash = hotel.Economy.Cash;
            Prune(hotel); Prune(hotel);
            Assert.That(hotel.Guests, Is.Empty);
            Assert.That(hotel.Reservations, Is.Empty);
            Assert.That(hotel.EventRevision, Is.EqualTo(revision));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(cash));
            Assert.That(fixture.Rooms[0].Cleanliness, Is.EqualTo(Cleanliness.Dirty));
            Mirror(hotel, replica, 2);
        }
    }
}
