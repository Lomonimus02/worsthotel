namespace WorstHotel
{
    public sealed partial class HotelSimulation
    {
        public CommandResult DebugCheckoutGuest(string guestId)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            var guest = FindLivingGuest(guestId);
            if (guest == null) return CommandResult.Fail("Developer checkout requires a real guest during living service.");
            var agent = guest.Agent;
            if (agent.State == GuestAgentState.CheckingOut || agent.State == GuestAgentState.Leaving || agent.State == GuestAgentState.Left)
                return CommandResult.Fail("This guest is already checking out or has left.");
            bool notYetArrived = agent.State == GuestAgentState.Scheduled;
            bool alreadyAway = agent.State == GuestAgentState.GuestAway;
            ReleaseRoom(guest);
            if (alreadyAway) SignalGuestVacatedRoom(guest.GuestId, guest.RoomId);
            agent.HeatingDemandMultiplier = agent.NoiseOutput = 0;
            agent.NextActivityTime = agent.ActivityEndsAt = float.PositiveInfinity;
            agent.QuietUntil = 0;
            // A visible guest still takes the normal physical exit route. An unspawned future booking has no body to move.
            Transition(guest, notYetArrived || alreadyAway ? GuestAgentState.Left : agent.CheckedIn ? GuestAgentState.CheckingOut : GuestAgentState.Leaving,
                Elapsed, guest.Name + ": checkout requested by developer command");
            RefreshGuestLoad();
            RefreshElectrical();
            return CommandResult.Ok(notYetArrived ? "Future booking cancelled without charging unused room time." :
                "Developer checkout started. Physical departure, room turnover and the final bill follow the normal lifecycle.");
        }

        public CommandResult DebugMarkRoomDirty(int roomId)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            if (OwnershipLost) return CommandResult.Fail("Ownership revoked. Start a new hotel.");
            if (Housekeeping == null) return CommandResult.Fail("Living housekeeping is not active.");
            if (!rooms.TryGetValue(roomId, out var room)) return CommandResult.Fail("Unknown room.");
            if (room.Occupied || !string.IsNullOrEmpty(room.DepartingGuestId))
                return CommandResult.Fail("Developer dirt requires a physically vacant room; let the guest depart first.");
            if (room.Cleanliness == Cleanliness.Dirty) return CommandResult.Fail("This room is already dirty and needs its existing cleaning task.");
            Housekeeping.MarkDirty(roomId);
            return CommandResult.Ok("Developer command marked room " + roomId + " dirty and queued normal housekeeping.");
        }
    }
}

