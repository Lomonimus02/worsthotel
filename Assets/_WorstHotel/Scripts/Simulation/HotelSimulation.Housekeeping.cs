using System;

namespace WorstHotel
{
    public sealed partial class HotelSimulation
    {
        public CommandResult PickUpLinen(int playerId, string id) => Housekeeping != null ?
            Housekeeping.PickUpLinen(playerId, id) : CommandResult.Fail("Manual turnover is not active.");
        public CommandResult DropLinen(int playerId, string id) => Housekeeping != null ?
            Housekeeping.DropLinen(playerId, id) : CommandResult.Fail("Manual turnover is not active.");
        public CommandResult DepositDirtyLinen(int playerId, string id) => Housekeeping != null ?
            Housekeeping.DepositDirtyLinen(playerId, id) : CommandResult.Fail("Manual turnover is not active.");
        public CommandResult BeginMakeBed(int playerId, int roomId, string cleanId) => Housekeeping != null ?
            Housekeeping.BeginMakeBed(playerId, roomId, cleanId) : CommandResult.Fail("Manual turnover is not active.");
        public CommandResult AdvanceMakeBed(int playerId, int roomId, float realDt) => Housekeeping != null ?
            Housekeeping.AdvanceMakeBed(playerId, roomId, realDt) : CommandResult.Fail("Manual turnover is not active.");
        public CommandResult CancelMakeBed(int playerId, int roomId) => Housekeeping != null ?
            Housekeeping.CancelMakeBed(playerId, roomId) : CommandResult.Fail("Manual turnover is not active.");

        public void AdvancePreparation(float dt)
        {
            if (IsReadOnlyMirror) return;
            if (!Number.IsFinite(dt) || dt < 0 || !Number.IsFinite(Clock.SimulationTime + dt))
                throw new ArgumentOutOfRangeException(nameof(dt));
            if (Running) throw new InvalidOperationException("Preparation time cannot advance a running guest shift.");
            if (Housekeeping == null) return;
            Housekeeping.Tick(dt);
            Clock.Advance(dt);
        }

        public CommandResult PrioritizeCleaning(int actorId, int roomId) => Housekeeping != null ?
            Housekeeping.PrioritizeRoom(actorId, roomId) : CommandResult.Fail("Living housekeeping is not active.");

        public CommandResult SignalHousekeeperReachedRoom(int roomId) => Housekeeping != null ?
            Housekeeping.SignalReachedRoom(roomId) : CommandResult.Fail("Living housekeeping is not active.");

        public CommandResult SignalGuestVacatedRoom(string guestId, int roomId)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            if (Housekeeping == null) return CommandResult.Fail("Living housekeeping is not active.");
            if (string.IsNullOrEmpty(guestId) || !rooms.TryGetValue(roomId, out var room) || room.DepartingGuestId != guestId)
                return CommandResult.Fail("This guest has no matching pending departure from this room.");
            // A previous day's visual guest can finish leaving after StartShift replaces the active booking list.
            room.DepartingGuestId = null;
            RefreshRoomPresence();
            SignalEvent("Room " + roomId + ": previous guest physically departed");
            Housekeeping.Tick(0);
            return CommandResult.Ok("Room " + roomId + " is physically vacant.");
        }

        private static bool RoomTurnoverProtected(RoomState room) => !string.IsNullOrEmpty(room.DepartingGuestId) ||
            room.TurnoverState == HousekeepingState.Moving || room.TurnoverState == HousekeepingState.Cleaning;

        private void ReleaseOwnedRoom(GuestStay guest, RoomState room)
        {
            if (room.GuestId != guest.GuestId) return;
            if (Housekeeping != null && guest.Agent != null && guest.Agent.CheckedIn)
            {
                // A checked-in traveller may already be beyond the door before the room-arrival callback.
                // Its body must leave before reuse, but an unserved room is not dirtied just by a booking.
                if (string.IsNullOrEmpty(room.DepartingGuestId)) room.DepartingGuestId = guest.GuestId;
                if (guest.Agent.HasReachedRoom) Housekeeping.MarkDirty(room.Profile.Id);
            }
            room.GuestId = null;
        }
    }
}

