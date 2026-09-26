using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace WorstHotel.Tests
{
    public sealed class GuestPresenceTests
    {
        SessionConfig asset;
        RoomState[] rooms;
        HotelSimulation simulation;
        GuestStay guest;

        [SetUp]
        public void SetUp()
        {
            asset = AssetDatabase.LoadAssetAtPath<SessionConfig>("Assets/_WorstHotel/ScriptableObjects/PrototypeSession.asset");
            simulation = Create(out rooms);
            var offer = GuestSystem.GenerateApplications(1, asset.ToData().GuestArchetypes).First();
            Assert.That(simulation.StartShift(new[] { new BookingAssignment(101, offer.Id, offer.ReferencePrice, 0) }, new[] { offer }).Success, Is.True);
            guest = simulation.Guests.Single();
            while (simulation.Elapsed < guest.Agent.ArrivalTime + .2f) simulation.Tick(.2f);
            Assert.That(simulation.SignalGuestReachedReception(guest.GuestId).Success, Is.True);
            Assert.That(ModelKeyHandoff.CheckIn(simulation, 0, guest.GuestId).Success, Is.True);
            Assert.That(simulation.SignalGuestReachedRoom(guest.GuestId).Success, Is.True);
        }

        HotelSimulation Create(out RoomState[] states)
        {
            var settings = asset.ToData();
            states = settings.Rooms.Select(profile => new RoomState(profile)).ToArray();
            return new HotelSimulation(settings, states, asset.living.ToData(), asset.needs.ToData(), asset.noise.ToData(),
                asset.heater.ToData(), asset.electricity.ToData(), asset.housekeeping.ToData());
        }

        [Test]
        public void ShowerRequestsWaitForPhysicalStagingBeforeDemandNoiseAndDurationBegin()
        {
            Assert.That(simulation.RegisterGuestPhysicalStaging(guest.GuestId).Success, Is.True);
            Assert.That(simulation.ForceActivity(guest.GuestId, GuestActivity.Shower).Success, Is.True);
            var agent = guest.Agent;
            float quietLoad = simulation.Boiler.Load;
            Assert.That(agent.ActivityStaged, Is.False);
            Assert.That(float.IsPositiveInfinity(agent.NextActivityTime), Is.True);
            simulation.Tick(5);
            Assert.That(simulation.Boiler.Load, Is.EqualTo(quietLoad).Within(.001f));
            Assert.That(agent.NoiseOutput, Is.EqualTo(simulation.LivingSettings.QuietNoiseOutput));
            Assert.That(simulation.SignalGuestActivityReady(guest.GuestId, GuestAgentState.Sleeping, GuestActivity.Shower).Success, Is.False);
            Assert.That(simulation.SignalGuestActivityReady(guest.GuestId, agent.State, agent.Activity).Success, Is.True);
            Assert.That(agent.ActivityStaged, Is.True);
            Assert.That(agent.NextActivityTime, Is.EqualTo(simulation.Elapsed + simulation.LivingSettings.ActivityDurationMin).Within(.001f));
            Assert.That(simulation.Boiler.Load, Is.GreaterThan(quietLoad));
            Assert.That(agent.NoiseOutput, Is.EqualTo(simulation.LivingSettings.ShowerNoiseOutput));
            Assert.That(simulation.SignalGuestActivityReady(guest.GuestId, agent.State, agent.Activity).Success, Is.False, "A repeated anchor callback cannot restart the timer.");
        }

        [Test]
        public void PrivateSleepAndShowerRefuseCasualEntryButRestAllowsARequest()
        {
            var room = rooms.Single(item => item.Profile.Id == guest.RoomId);
            Assert.That(room.AssignedGuest, Is.EqualTo(guest.GuestId));
            Assert.That(room.OccupancyState, Is.EqualTo(RoomOccupancyState.GuestInside));
            Assert.That(room.PrivacyState, Is.EqualTo(RoomPrivacyState.SemiPrivate));
            Assert.That(simulation.RequestStaffRoomAccess(0, 101).Success, Is.True);
            Assert.That(simulation.ForceSleep(guest.GuestId).Success, Is.True);
            Assert.That(room.PrivacyState, Is.EqualTo(RoomPrivacyState.Private));
            Assert.That(room.DoorState, Is.EqualTo(RoomDoorState.Locked));
            Assert.That(simulation.RequestStaffRoomAccess(0, 101).Success, Is.False);
            Assert.That(simulation.ForceActivity(guest.GuestId, GuestActivity.Shower).Success, Is.True);
            Assert.That(simulation.RequestStaffRoomAccess(1, 101).Success, Is.False);
            Assert.That(simulation.ForceActivity(guest.GuestId, GuestActivity.QuietRest).Success, Is.True);
            Assert.That(room.DoorState, Is.EqualTo(RoomDoorState.Closed));
            Assert.That(simulation.RequestStaffRoomAccess(1, 101).Success, Is.True);
        }

        [Test]
        public void BriefAbsenceRetainsRoomAndKeyThenReturnsWithoutANewStay()
        {
            var room = rooms.Single(item => item.Profile.Id == guest.RoomId);
            Assert.That(simulation.ForceReturnRoom(guest.GuestId).Success, Is.False);
            Assert.That(simulation.ForceLeaveRoom(guest.GuestId).Success, Is.True);
            Assert.That(guest.Agent.State, Is.EqualTo(GuestAgentState.LeavingRoom));
            Assert.That(simulation.Boiler.Load, Is.Zero);
            Assert.That(simulation.SignalGuestLeftRoom(guest.GuestId).Success, Is.True);
            Assert.That(room.OccupancyState, Is.EqualTo(RoomOccupancyState.GuestAway));
            Assert.That(room.GuestId, Is.EqualTo(guest.GuestId));
            Assert.That(simulation.Keys.Find(101).GuestId, Is.EqualTo(guest.GuestId));
            Assert.That(simulation.RequestStaffRoomAccess(0, 101).Success, Is.False);
            Assert.That(simulation.ForceSleep(guest.GuestId).Success, Is.False);
            simulation.Tick(simulation.LivingSettings.AwayDurationMin + .1f);
            Assert.That(guest.Agent.State, Is.EqualTo(GuestAgentState.ReturningToRoom));
            Assert.That(simulation.SignalGuestReturnedRoom(guest.GuestId).Success, Is.True);
            Assert.That(room.OccupancyState, Is.EqualTo(RoomOccupancyState.GuestInside));
            Assert.That(simulation.Guests.Single(), Is.SameAs(guest));
            Assert.That(room.Cleanliness, Is.EqualTo(Cleanliness.Clean));
        }

        [Test]
        public void HotelTripRequiresExitAndReturnAcknowledgementsWithoutAmbientNotificationsOrLosingOwnership()
        {
            int revision = simulation.EventRevision;
            Assert.That(guest.Agent.CurrentLocation, Is.EqualTo(GuestLocation.AssignedRoom));
            Assert.That(simulation.ForceLeaveRoom(guest.GuestId).Success, Is.True);
            Assert.That(guest.Agent.CurrentLocation, Is.EqualTo(GuestLocation.Travelling));
            Assert.That(guest.Agent.InAssignedRoom, Is.False);
            Assert.That(simulation.ForceReturnRoom(guest.GuestId).Success, Is.False, "Logical away waits for the exterior arrival callback.");
            Assert.That(simulation.SignalGuestLeftRoom(guest.GuestId).Success, Is.True);
            Assert.That(guest.Agent.CurrentLocation, Is.EqualTo(GuestLocation.Away));
            Assert.That(simulation.ForceReturnRoom(guest.GuestId).Success, Is.True);
            Assert.That(guest.Agent.CurrentLocation, Is.EqualTo(GuestLocation.Travelling));
            Assert.That(guest.Agent.InAssignedRoom, Is.False, "Passing the lobby does not expose a traveller to room conditions.");
            Assert.That(simulation.SignalGuestReturnedRoom(guest.GuestId).Success, Is.True);
            Assert.That(guest.Agent.CurrentLocation, Is.EqualTo(GuestLocation.AssignedRoom));
            Assert.That(rooms[0].GuestId, Is.EqualTo(guest.GuestId));
            Assert.That(simulation.Keys.Find(101).GuestId, Is.EqualTo(guest.GuestId));
            Assert.That(simulation.EventRevision, Is.EqualTo(revision), "Routine departure and return are background life, not new staff requests.");
        }

        [Test]
        public void CheckoutCancelsAnUnfinishedActivityAndClearsPrivacyOnlyAfterOwnershipEnds()
        {
            simulation.RegisterGuestPhysicalStaging(guest.GuestId);
            simulation.ForceActivity(guest.GuestId, GuestActivity.Shower);
            Assert.That(simulation.DebugCheckoutGuest(guest.GuestId).Success, Is.True);
            var room = rooms.Single(item => item.Profile.Id == guest.RoomId);
            Assert.That(room.OccupancyState, Is.EqualTo(RoomOccupancyState.CheckoutPending));
            Assert.That(room.PrivacyState, Is.EqualTo(RoomPrivacyState.Public));
            Assert.That(simulation.SignalGuestActivityReady(guest.GuestId, GuestAgentState.PerformingActivity, GuestActivity.Shower).Success, Is.False);
            Assert.That(simulation.Boiler.Load, Is.Zero);
            Assert.That(simulation.SignalGuestVacatedRoom(guest.GuestId, 101).Success, Is.True);
            Assert.That(room.OccupancyState, Is.EqualTo(RoomOccupancyState.Vacant));
        }

        [Test]
        public void JsonHostSnapshotPreservesPrivatePendingActivityAndRejectsClientMutations()
        {
            simulation.RegisterGuestPhysicalStaging(guest.GuestId);
            simulation.ForceSleep(guest.GuestId);
            var mirror = Create(out var replicaRooms);
            mirror.EnableReadOnlyMirror();
            var wire = JsonUtility.FromJson<HotelModelSnapshot>(JsonUtility.ToJson(simulation.CaptureSnapshot(2, 1)));
            Assert.That(mirror.ApplySnapshot(wire).Success, Is.True);
            Assert.That(replicaRooms[0].PrivacyState, Is.EqualTo(RoomPrivacyState.Private));
            Assert.That(replicaRooms[0].DoorState, Is.EqualTo(RoomDoorState.Locked));
            Assert.That(mirror.Guests.Single().Agent.RequiresActivityStaging, Is.True);
            Assert.That(mirror.Guests.Single().Agent.ActivityStaged, Is.False);
            Assert.That(mirror.ForceSleep(guest.GuestId).Success, Is.False);
            Assert.That(mirror.ForceLeaveRoom(guest.GuestId).Success, Is.False);
            Assert.That(mirror.ForceReturnRoom(guest.GuestId).Success, Is.False);
            Assert.That(mirror.RegisterGuestPhysicalStaging(guest.GuestId).Success, Is.False);
            Assert.That(mirror.SignalGuestActivityReady(guest.GuestId, guest.Agent.State, guest.Agent.Activity).Success, Is.False);
            Assert.That(mirror.RequestStaffRoomAccess(0, 101).Success, Is.False);
            Assert.That(mirror.ReportRoomDoorState(101, true).Success, Is.False);
            wire.Sequence = 2; wire.Rooms[0].PrivacyState = (RoomPrivacyState)99;
            Assert.That(mirror.ApplySnapshot(wire).Success, Is.False);
            Assert.That(mirror.AppliedSnapshotSequence, Is.EqualTo(1));
        }
    }
}
