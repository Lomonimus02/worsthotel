using System;
using System.Linq;
using NUnit.Framework;

namespace WorstHotel.Tests
{
    public sealed class RoomKeyTests
    {
        sealed class Fixture
        {
            internal SessionSettings Settings;
            internal HotelSimulation Simulation;
            internal RoomState[] Rooms;
            internal GuestStay Guest => Simulation.Guests[0];
            internal RoomState Room(int id) => Rooms.Single(room => room.Profile.Id == id);
        }

        static Fixture Create(int bookings = 1)
        {
            var profiles = new[] {
                new GuestProfile(GuestKind.Budget, "Budget", "", 180, .85f, 18, .7f, 28, 90, .55f),
                new GuestProfile(GuestKind.ColdSensitive, "Cold-sensitive", "", 300, 1.05f, 20, 1.35f, 16, 65, .4f),
                new GuestProfile(GuestKind.Business, "Business", "", 450, 1, 19.5f, 1, 10, 45, .25f) };
            var settings = new SessionSettings(profiles,
                Enumerable.Range(101, 6).Select(id => new RoomProfile(id, "Room " + id)),
                new BoilerSettings(), new EconomySettings());
            var rooms = settings.Rooms.Select(profile => new RoomState(profile)).ToArray();
            var simulation = new HotelSimulation(settings, rooms,
                new LivingHotelSettings(firstArrivalSeconds: .2f, arrivalJitterSeconds: 0, firstActivityDelay: 1000));
            var offers = GuestSystem.GenerateApplications(1, profiles).Take(bookings).ToArray();
            Assert.That(simulation.StartShift(offers.Select((offer, i) =>
                new BookingAssignment(101 + i, offer.Id, offer.ReferencePrice, 0)), offers).Success, Is.True);
            var fixture = new Fixture { Settings = settings, Simulation = simulation, Rooms = rooms };
            foreach (var guest in simulation.Guests)
            {
                while (simulation.Elapsed < guest.Agent.ArrivalTime + .2f) simulation.Tick(.2f);
                Assert.That(simulation.SignalGuestReachedReception(guest.GuestId).Success, Is.True);
            }
            return fixture;
        }

        static void Admit(Fixture fixture, int actor = 0)
        {
            Assert.That(ModelKeyHandoff.CheckIn(fixture.Simulation, actor, fixture.Guest.GuestId).Success, Is.True);
            Assert.That(fixture.Simulation.SignalGuestReachedRoom(fixture.Guest.GuestId).Success, Is.True);
        }

        [Test]
        public void RackHasExactlyOneNumberedKeyPerRoomAndRejectsInvalidRegistrations()
        {
            var keys = new RoomKeySystem(Enumerable.Range(101, 6).Reverse());
            Assert.That(keys.Items.Select(key => key.RoomId), Is.EqualTo(Enumerable.Range(101, 6)));
            Assert.That(keys.Items.All(key => key.Location == RoomKeyLocation.OnRack && key.PlayerId == null && key.GuestId == null), Is.True);
            Assert.Throws<ArgumentNullException>(() => new RoomKeySystem(null));
            Assert.Throws<ArgumentException>(() => new RoomKeySystem(new[] { 101, 101 }));
            Assert.Throws<ArgumentException>(() => new RoomKeySystem(new[] { 0 }));
            Assert.Throws<ArgumentException>(() => new RoomKeySystem(Array.Empty<int>()));
        }

        [Test]
        public void PhysicalKeyOwnershipPreventsTheftDuplicatesAndMultipleKeysPerPlayer()
        {
            var keys = new RoomKeySystem(Enumerable.Range(101, 6));
            Assert.That(keys.PickUp(-1, 101).Success, Is.False);
            Assert.That(keys.PickUp(0, 999).Success, Is.False);
            Assert.That(keys.PickUp(17, 101).Success, Is.True, "The model identity must not be hardwired to two local actors.");
            Assert.That(keys.PickUp(17, 102).Success, Is.False);
            Assert.That(keys.PickUp(0, 101).Success, Is.False);
            Assert.That(keys.Drop(0, 101).Success, Is.False);
            Assert.That(keys.ReturnToRack(101).Success, Is.False);
            Assert.That(keys.Find(101).PlayerId, Is.EqualTo(17));
            Assert.That(keys.Find(102).Location, Is.EqualTo(RoomKeyLocation.OnRack));
            Assert.That(keys.Drop(17, 101).Success, Is.True);
            Assert.That(keys.Find(101).PlayerId, Is.Null);
            Assert.That(keys.PickUp(0, 101).Success, Is.True);
            Assert.That(keys.Find(101).PlayerId, Is.EqualTo(0));
        }

        [Test]
        public void DroppedKeyCanBeRecoveredOrDockedButCannotBeReturnedWhileHeld()
        {
            var keys = new RoomKeySystem(new[] { 101 });
            Assert.That(keys.ReturnToRack(101).Success, Is.False);
            Assert.That(keys.PickUp(0, 101).Success, Is.True);
            Assert.That(keys.Drop(0, 101).Success, Is.True);
            Assert.That(keys.Find(101).Location, Is.EqualTo(RoomKeyLocation.Dropped));
            Assert.That(keys.Drop(0, 101).Success, Is.False);
            Assert.That(keys.ReturnToRack(101).Success, Is.True);
            Assert.That(keys.Find(101).Location, Is.EqualTo(RoomKeyLocation.OnRack));
            Assert.That(keys.ReturnToRack(101).Success, Is.False);
        }

        [Test]
        public void CheckInRequiresMatchingPhysicalKeyFromTheActingPlayerAndHandsItOverOnce()
        {
            var f = Create(); var sim = f.Simulation; var guest = f.Guest;
            Assert.That(sim.CheckIn(0, guest.GuestId).Success, Is.False);
            Assert.That(sim.Keys.PickUp(0, 102).Success, Is.True);
            int revision = sim.EventRevision;
            Assert.That(sim.CheckIn(0, guest.GuestId).Success, Is.False);
            Assert.That(sim.EventRevision, Is.EqualTo(revision));
            Assert.That(sim.Keys.Find(102).PlayerId, Is.EqualTo(0));
            Assert.That(f.Room(101).ReservedGuestId, Is.EqualTo(guest.GuestId));
            Assert.That(f.Room(101).Occupied, Is.False);
            Assert.That(sim.Keys.PickUp(7, 101).Success, Is.True);
            Assert.That(sim.CheckIn(0, guest.GuestId).Success, Is.False, "Another staff member's key cannot authenticate this handoff.");
            bool observedCompleteOwnership = false;
            sim.Keys.Changed += (key, _) => {
                if (key.RoomId != 101 || key.Location != RoomKeyLocation.HeldByGuest) return;
                observedCompleteOwnership = f.Room(101).GuestId == guest.GuestId && guest.Agent.CheckedIn;
            };
            Assert.That(sim.CheckIn(7, guest.GuestId).Success, Is.True);
            Assert.That(observedCompleteOwnership, Is.True, "Key observers must not see half-committed room ownership.");
            Assert.That(sim.Keys.Find(101).GuestId, Is.EqualTo(guest.GuestId));
            Assert.That(sim.Keys.Find(101).PlayerId, Is.Null);
            Assert.That(sim.CheckIn(7, guest.GuestId).Success, Is.False);
            Assert.That(sim.Keys.Drop(7, 101).Success, Is.False, "A delayed physical release cannot turn an already given key into a drop.");
            Assert.That(sim.Keys.PickUp(1, 101).Success, Is.False);
            Assert.That(sim.Keys.Items.Count(key => key.GuestId == guest.GuestId), Is.EqualTo(1));
        }

        [Test]
        public void CorrectKeyCannotBypassDirtyRoomOrPhysicalDepartureGuard()
        {
            var f = Create(); var sim = f.Simulation;
            Assert.That(sim.Keys.PickUp(0, 101).Success, Is.True);
            f.Room(101).Cleanliness = Cleanliness.Dirty;
            Assert.That(sim.CheckIn(0, f.Guest.GuestId).Success, Is.False);
            Assert.That(sim.Keys.Find(101).PlayerId, Is.EqualTo(0));
            f.Room(101).Cleanliness = Cleanliness.Clean;
            var previous = f.Guest;
            Assert.That(sim.CheckIn(0, previous.GuestId).Success, Is.True);
            // Close during actual room travel: the room was never used, but the prior physical
            // identity must still report leaving before a new stay can receive this returned key.
            sim.EndShift();
            Assert.That(f.Room(101).Cleanliness, Is.EqualTo(Cleanliness.Clean));
            Assert.That(f.Room(101).DepartingGuestId, Is.EqualTo(previous.GuestId));
            Assert.That(sim.ApplyMaintenance(0, MaintenanceChoice.Defer).Success, Is.True);
            var nextOffer = GuestSystem.GenerateApplications(2, f.Settings.GuestArchetypes).First();
            Assert.That(sim.StartShift(new[] { new BookingAssignment(101, nextOffer.Id, nextOffer.ReferencePrice, 0) },
                new[] { nextOffer }).Success, Is.True);
            while (sim.Elapsed < f.Guest.Agent.ArrivalTime + .2f) sim.Tick(.2f);
            Assert.That(sim.SignalGuestReachedReception(f.Guest.GuestId).Success, Is.True);
            Assert.That(sim.Keys.PickUp(0, 101).Success, Is.True);
            Assert.That(sim.CheckIn(0, f.Guest.GuestId).Success, Is.False);
            Assert.That(sim.Keys.Find(101).PlayerId, Is.EqualTo(0));
            Assert.That(f.Guest.Agent.CheckedIn, Is.False);
            Assert.That(f.Room(101).ReservedGuestId, Is.EqualTo(f.Guest.GuestId));
            Assert.That(sim.SignalGuestVacatedRoom(previous.GuestId, 101).Success, Is.True);
            Assert.That(sim.CheckIn(0, f.Guest.GuestId).Success, Is.True);
        }

        [Test]
        public void PendingMoveReservesDestinationWhileOldRoomAndItsDiscomfortRemainActive()
        {
            var f = Create(); Admit(f); var sim = f.Simulation; var guest = f.Guest;
            Assert.That(sim.SetRoomTemperature(101, 5).Success, Is.True);
            sim.Tick(.2f);
            float exposure = guest.Needs.Temperature.ExposureSeconds;
            Assert.That(sim.RequestGuestMove(12, guest.GuestId, 102).Success, Is.True);
            Assert.That(guest.Agent.PendingMoveRoomId, Is.EqualTo(102));
            Assert.That(guest.RoomId, Is.EqualTo(101));
            Assert.That(guest.Agent.InAssignedRoom, Is.True);
            Assert.That(guest.Agent.IsRelocating, Is.False);
            Assert.That(f.Room(101).GuestId, Is.EqualTo(guest.GuestId));
            Assert.That(f.Room(102).ReservedGuestId, Is.EqualTo(guest.GuestId));
            Assert.That(sim.Keys.Find(101).GuestId, Is.EqualTo(guest.GuestId));
            sim.Tick(.2f);
            Assert.That(guest.Needs.Temperature.ExposureSeconds, Is.GreaterThan(exposure));
            Assert.That(sim.Boiler.Load, Is.GreaterThan(0));
            int revision = sim.EventRevision;
            Assert.That(sim.RequestGuestMove(0, guest.GuestId, 102).Success, Is.True);
            Assert.That(sim.EventRevision, Is.EqualTo(revision), "Repeating the same selection cannot create duplicate reservations/events.");
            Assert.That(sim.CancelGuestMove(12, guest.GuestId).Success, Is.True);
            Assert.That(guest.Agent.PendingMoveRoomId, Is.Null);
            Assert.That(f.Room(102).Reserved, Is.False);
            Assert.That(f.Room(101).GuestId, Is.EqualTo(guest.GuestId));
            Assert.That(sim.CancelGuestMove(12, guest.GuestId).Success, Is.False);
        }

        [Test]
        public void NewKeyExchangeIsAtomicAndAStaleReleaseCannotStealEitherGuestKey()
        {
            var f = Create(); Admit(f); var sim = f.Simulation; var guest = f.Guest;
            Assert.That(sim.RequestGuestMove(0, guest.GuestId, 102).Success, Is.True);
            Assert.That(sim.Keys.PickUp(0, 103).Success, Is.True);
            Assert.That(sim.MoveGuest(0, guest.GuestId, 102).Success, Is.False);
            Assert.That(guest.RoomId, Is.EqualTo(101));
            Assert.That(sim.Keys.Find(103).PlayerId, Is.EqualTo(0));
            Assert.That(sim.Keys.PickUp(9, 102).Success, Is.True);
            int notifications = 0;
            sim.Keys.Changed += (_, __) => {
                notifications++;
                Assert.That(guest.RoomId, Is.EqualTo(102));
                Assert.That(f.Room(101).Occupied, Is.False);
                Assert.That(f.Room(102).ReservedGuestId, Is.EqualTo(guest.GuestId));
                Assert.That(sim.Keys.Find(101).Location, Is.EqualTo(RoomKeyLocation.Returned));
                Assert.That(sim.Keys.Find(102).GuestId, Is.EqualTo(guest.GuestId));
            };
            Assert.That(sim.MoveGuest(9, guest.GuestId, 102).Success, Is.True);
            Assert.That(notifications, Is.EqualTo(2));
            Assert.That(guest.Agent.PendingMoveRoomId, Is.Null);
            Assert.That(guest.Agent.IsRelocating, Is.True);
            Assert.That(sim.Keys.Drop(9, 102).Success, Is.False);
            Assert.That(sim.SignalGuestReachedRoom(guest.GuestId).Success, Is.True);
            Assert.That(f.Room(102).GuestId, Is.EqualTo(guest.GuestId));
        }

        [Test]
        public void TwoPendingMovesCannotReserveTheSameKeyDestination()
        {
            var f = Create(2); Admit(f); var sim = f.Simulation;
            var other = sim.Guests[1];
            Assert.That(ModelKeyHandoff.CheckIn(sim, 1, other.GuestId).Success, Is.True);
            Assert.That(sim.SignalGuestReachedRoom(other.GuestId).Success, Is.True);
            Assert.That(sim.RequestGuestMove(0, f.Guest.GuestId, 103).Success, Is.True);
            Assert.That(sim.RequestGuestMove(1, other.GuestId, 103).Success, Is.False);
            Assert.That(other.Agent.PendingMoveRoomId, Is.Null);
            Assert.That(f.Room(103).ReservedGuestId, Is.EqualTo(f.Guest.GuestId));
            Assert.That(sim.RequestGuestMove(-1, other.GuestId, 104).Success, Is.False);
            Assert.That(sim.RequestGuestMove(0, "missing", 104).Success, Is.False);
            Assert.That(sim.RequestGuestMove(0, other.GuestId, 999).Success, Is.False);
        }

        [Test]
        public void CheckoutReturnsGuestKeyOnceAndCancelsPendingDestinationWithoutReclaimingStaffKey()
        {
            var f = Create(); Admit(f); var sim = f.Simulation; var guest = f.Guest;
            Assert.That(sim.RequestGuestMove(0, guest.GuestId, 102).Success, Is.True);
            Assert.That(sim.Keys.PickUp(0, 102).Success, Is.True);
            Assert.That(sim.DebugCheckoutGuest(guest.GuestId).Success, Is.True);
            Assert.That(sim.Keys.Find(101).Location, Is.EqualTo(RoomKeyLocation.Returned));
            Assert.That(sim.Keys.Find(102).PlayerId, Is.EqualTo(0));
            Assert.That(guest.Agent.PendingMoveRoomId, Is.Null);
            Assert.That(f.Room(102).Reserved, Is.False);
            Assert.That(f.Room(101).DepartingGuestId, Is.EqualTo(guest.GuestId), "Returning a key does not teleport its departing owner.");
            Assert.That(sim.Keys.PickUp(1, 101).Success, Is.True);
            Assert.That(sim.Keys.ReturnGuestKeys(guest.GuestId).Success, Is.True);
            Assert.That(sim.Keys.Find(101).PlayerId, Is.EqualTo(1), "A duplicate checkout callback cannot steal a reissued staff key.");
            Assert.That(sim.DebugCheckoutGuest(guest.GuestId).Success, Is.False);
        }

        [Test]
        public void SettlementReturnsKeyDuringTransferAndFreshSimulationResetsAllOwnership()
        {
            var f = Create(); Admit(f); var sim = f.Simulation; var guest = f.Guest;
            Assert.That(ModelKeyHandoff.MoveGuest(sim, 0, guest.GuestId, 102).Success, Is.True);
            sim.EndShift();
            Assert.That(sim.Keys.Find(101).Location, Is.EqualTo(RoomKeyLocation.Returned));
            Assert.That(sim.Keys.Find(102).Location, Is.EqualTo(RoomKeyLocation.Returned));
            Assert.That(sim.Keys.Items.All(key => key.GuestId == null), Is.True);
            Assert.That(f.Rooms.Any(room => room.Occupied || room.Reserved), Is.False);
            var fresh = Create();
            Assert.That(fresh.Guest.GuestId, Is.EqualTo(guest.GuestId), "A new run deliberately reuses deterministic day-one IDs.");
            Assert.That(fresh.Simulation.Keys.Items.All(key => key.Location == RoomKeyLocation.OnRack), Is.True);
            Assert.That(fresh.Simulation.Keys, Is.Not.SameAs(sim.Keys));
        }
    }
}
