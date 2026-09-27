using System;
using System.Linq;

namespace WorstHotel
{
    public sealed partial class HotelSimulation
    {
        // Scene actors opt into a physical acknowledgement. Pure model adapters continue
        // to report their own travel completion without depending on Unity or NavMesh.
        public CommandResult RegisterGuestPhysicalStaging(string guestId)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            var guest = FindLivingGuest(guestId);
            if (guest?.Agent == null) return CommandResult.Fail("Unknown active guest.");
            if (guest.Agent.RequiresActivityStaging) return CommandResult.Ok();
            guest.Agent.RequiresActivityStaging = true;
            if (guest.Agent.IsRoomState)
            {
                guest.Agent.PendingActivityDuration = Math.Max(0, guest.Agent.ActivityEndsAt - Elapsed);
                guest.Agent.ActivityStaged = false;
                guest.Agent.NextActivityTime = guest.Agent.ActivityEndsAt = float.PositiveInfinity;
                SetActivityOutput(guest.Agent, false);
                RefreshGuestLoad(); RefreshElectrical();
            }
            return CommandResult.Ok("Guest activities now require their physical room anchor.");
        }

        public CommandResult SignalGuestActivityReady(string guestId, GuestAgentState expectedState, GuestActivity expectedActivity)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            var guest = FindLivingGuest(guestId);
            var agent = guest?.Agent;
            if (agent == null || !agent.IsRoomState || !agent.RequiresActivityStaging || agent.ActivityStaged ||
                !string.IsNullOrEmpty(agent.ResponseActionId) ||
                agent.State != expectedState || agent.Activity != expectedActivity || rooms[guest.RoomId].GuestId != guestId)
                return CommandResult.Fail("Guest activity changed before its physical staging completed.");
            agent.ActivityStaged = true;
            agent.ActivityEndsAt = agent.NextActivityTime = Elapsed + agent.PendingActivityDuration;
            SetActivityOutput(agent, true);
            RefreshRoomPresence(); RefreshGuestLoad(); RefreshElectrical();
            return CommandResult.Ok("Guest reached the activity anchor; room activity started.");
        }

        public CommandResult ForceSleep(string guestId)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            var guest = FindLivingGuest(guestId);
            if (guest?.Agent == null || !guest.Agent.IsRoomState) return CommandResult.Fail("Sleep requires a guest inside their assigned room.");
            if (Services?.DirectIntent(guestId) != null) return CommandResult.Fail("The guest is waiting for a direct service decision.");
            if (!string.IsNullOrEmpty(guest.Agent.ResponseActionId)) return CommandResult.Fail("Let the current guest response finish or cancel it first.");
            SetActivity(guest, GuestActivity.QuietRest, Elapsed, LivingSettings.ActivityDurationMin);
            guest.Agent.TemporarySleep = true;
            guest.Agent.SleepStarted = true;
            Transition(guest, GuestAgentState.Sleeping, Elapsed, null);
            RefreshGuestLoad(); RefreshElectrical();
            return CommandResult.Ok("Guest will walk to the bed, sleep, then resume their schedule.");
        }

        public CommandResult ForceLeaveRoom(string guestId)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            var guest = FindLivingGuest(guestId);
            if (guest?.Agent == null || !guest.Agent.IsRoomState) return CommandResult.Fail("Only a guest in their assigned room can leave it.");
            if (Services?.DirectIntent(guestId) != null) return CommandResult.Fail("The guest is waiting for a direct service decision.");
            if (!string.IsNullOrEmpty(guest.Agent.ResponseActionId)) return CommandResult.Fail("Let the current guest response finish or cancel it first.");
            StartGuestHotelTrip(guest, LivingSettings.AwayDurationMin);
            return CommandResult.Ok("Guest will leave through the lobby, keeping their room and key until returning.");
        }

        void StartGuestHotelTrip(GuestStay guest, float duration, float plannedReturnAt = -1)
        {
            guest.Agent.Activity = GuestActivity.LeaveHotel;
            guest.Agent.PendingActivityDuration = duration;
            guest.Agent.AwayReturnTime = plannedReturnAt >= 0 ? plannedReturnAt : float.PositiveInfinity;
            guest.Agent.ActivityStaged = false;
            guest.Agent.HeatingDemandMultiplier = guest.Agent.NoiseOutput = 0;
            guest.Agent.NextActivityTime = guest.Agent.ActivityEndsAt = float.PositiveInfinity;
            guest.Agent.TemporarySleep = false;
            Transition(guest, GuestAgentState.LeavingRoom, Elapsed, null);
            RefreshGuestLoad(); RefreshElectrical();
        }

        public CommandResult SignalGuestLeftRoom(string guestId)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            var guest = FindLivingGuest(guestId);
            if (guest?.Agent == null || guest.Agent.State != GuestAgentState.LeavingRoom)
                return CommandResult.Fail("Guest is not leaving their room.");
            guest.Agent.AwayReturnTime = Number.IsFinite(guest.Agent.AwayReturnTime) ?
                Math.Max(Elapsed, guest.Agent.AwayReturnTime) : Elapsed + Math.Max(1, guest.Agent.PendingActivityDuration);
            Transition(guest, GuestAgentState.GuestAway, Elapsed, null);
            return CommandResult.Ok("Guest reached the exterior hotel exit and is now away.");
        }

        public CommandResult ForceReturnRoom(string guestId)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            var guest = FindLivingGuest(guestId);
            if (guest?.Agent == null || guest.Agent.State != GuestAgentState.GuestAway || rooms[guest.RoomId].GuestId != guestId)
                return CommandResult.Fail("Only an away guest who still owns their room can return.");
            Transition(guest, GuestAgentState.ReturningToRoom, Elapsed, null);
            return CommandResult.Ok("Guest is returning to their room.");
        }

        public CommandResult SignalGuestReturnedRoom(string guestId)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            var guest = FindLivingGuest(guestId);
            if (guest?.Agent == null || guest.Agent.State != GuestAgentState.ReturningToRoom || rooms[guest.RoomId].GuestId != guestId)
                return CommandResult.Fail("Guest is not returning to their own room.");
            SetActivity(guest, GuestActivity.QuietRest, Elapsed, LivingSettings.FirstActivityDelay);
            RefreshGuestLoad(); RefreshElectrical();
            return CommandResult.Ok("Guest returned; their existing stay and schedule continue.");
        }

        // The scene must also establish that this particular suitcase is being delivered
        // by the employee at the door. An outstanding job alone never unlocks a room.
        public bool HasLuggageStaffAccess(int roomId, string itemId)
        {
            var item = Services?.FindItem(itemId);
            return item != null && item.Kind == ServiceItemKind.Luggage && item.RoomId == roomId && item.StaffHandling &&
                item.Location != ServiceItemLocation.Delivered && rooms.TryGetValue(roomId, out var room) &&
                room.Occupied && room.GuestId == item.GuestId &&
                guests.Any(guest => guest.GuestId == item.GuestId && !guest.ReceiptPosted);
        }

        public CommandResult RequestStaffRoomAccess(int actorId, int roomId)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            if (actorId < 0 || actorId > 1 || !rooms.TryGetValue(roomId, out var room)) return CommandResult.Fail("Unknown staff or room.");
            RefreshRoomPresence();
            if (!room.Occupied) return CommandResult.Ok("The room is vacant.");
            if (room.OccupancyState != RoomOccupancyState.GuestInside) return CommandResult.Fail("No answer. The guest is away; the room remains private.");
            if (room.PrivacyState == RoomPrivacyState.Private) return CommandResult.Fail("Not now, please. I need some privacy.");
            return CommandResult.Ok("Yes, you may come in.");
        }

        public CommandResult ReportRoomDoorState(int roomId, bool open)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            if (!rooms.TryGetValue(roomId, out var room)) return CommandResult.Fail("Unknown room.");
            room.DoorState = open ? RoomDoorState.Open : room.PrivacyState == RoomPrivacyState.Private ? RoomDoorState.Locked : RoomDoorState.Closed;
            return CommandResult.Ok();
        }

        void SetActivityOutput(GuestAgent agent, bool staged)
        {
            var activity = staged ? agent.Activity : GuestActivity.QuietRest;
            var stay = guests.FirstOrDefault(guest => guest.GuestId == agent.GuestId);
            bool noisy = stay != null && (stay.Application.Archetype.Traits & GuestTraits.Noisy) != 0;
            agent.HeatingDemandMultiplier = activity == GuestActivity.Shower ? LivingSettings.ShowerDemandMultiplier :
                activity == GuestActivity.LoudRoom ? LivingSettings.LoudDemandMultiplier : LivingSettings.QuietDemandMultiplier;
            agent.NoiseOutput = activity == GuestActivity.Shower ? LivingSettings.ShowerNoiseOutput :
                activity == GuestActivity.LoudRoom ? LivingSettings.LoudNoiseOutput :
                activity == GuestActivity.PhoneCall ? noisy ? LivingSettings.NoisyPhoneNoiseOutput : LivingSettings.PhoneNoiseOutput :
                activity == GuestActivity.WatchTV ? LivingSettings.QuietTVNoiseOutput : LivingSettings.QuietNoiseOutput;
        }

        void RefreshRoomPresence()
        {
            foreach (var room in rooms.Values)
            {
                var guest = guests.FirstOrDefault(item => item.GuestId == room.GuestId);
                var agent = guest?.Agent;
                room.OccupancyState = !room.Occupied ? string.IsNullOrEmpty(room.DepartingGuestId) ? RoomOccupancyState.Vacant : RoomOccupancyState.CheckoutPending :
                    agent != null && agent.IsRoomState ? RoomOccupancyState.GuestInside :
                    agent != null && (agent.IsServiceReceptionTrip || agent.State == GuestAgentState.GuestAway || agent.State == GuestAgentState.ReturningToRoom) ? RoomOccupancyState.GuestAway : RoomOccupancyState.Occupied;
                room.PrivacyState = !room.Occupied ? RoomPrivacyState.Public : agent != null && agent.IsRoomState &&
                    (agent.State == GuestAgentState.Sleeping || agent.Activity == GuestActivity.Shower) ? RoomPrivacyState.Private : RoomPrivacyState.SemiPrivate;
                if (room.DoorState != RoomDoorState.Open)
                    room.DoorState = room.PrivacyState == RoomPrivacyState.Private ? RoomDoorState.Locked : RoomDoorState.Closed;
            }
        }
    }
}
