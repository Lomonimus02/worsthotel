namespace WorstHotel
{
    public sealed partial class HotelSimulation
    {
        public CommandResult RequestGuestMove(int actorId, string guestId, int targetRoomId)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            var valid = ValidateMoveDestination(actorId, guestId, targetRoomId, out var guest, out _, out var destination);
            if (!valid.Success) return valid;
            if (guest.Agent.PendingMoveRoomId == targetRoomId)
                return CommandResult.Ok("This destination is already reserved. Bring its key to the guest.");
            var intentPermission = Services?.CanBeginRoomMoveIntent(guest) ?? CommandResult.Ok();
            if (!intentPermission.Success) return intentPermission;
            destination.ReservedGuestId = guestId;
            guest.Agent.PendingMoveRoomId = targetRoomId;
            Services?.BeginRoomMoveIntent(guest, targetRoomId);
            SignalEvent(guest.Name + ": room " + targetRoomId + " reserved pending physical key exchange");
            return CommandResult.Ok("Bring the room " + targetRoomId + " key to " + guest.Name + ". The guest stays in the current room until handoff.");
        }

        public CommandResult CancelGuestMove(int actorId, string guestId)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            if (actorId < 0) return CommandResult.Fail("Unknown player identity.");
            var guest = FindLivingGuest(guestId);
            if (guest == null || !guest.Agent.PendingMoveRoomId.HasValue)
                return CommandResult.Fail("This guest has no pending room-key exchange.");
            int target = guest.Agent.PendingMoveRoomId.Value;
            ReleasePendingMove(guest);
            Services?.FinishRoomMoveIntent(guest, false);
            SignalEvent(guest.Name + ": proposed move to room " + target + " cancelled");
            return CommandResult.Ok("Move cancelled. Current room, key and stay conditions are unchanged.");
        }

        public CommandResult MoveGuest(int actorId, string guestId, int targetRoomId)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            var valid = ValidateMoveDestination(actorId, guestId, targetRoomId, out var guest, out var source, out var destination);
            if (!valid.Success) return valid;
            var exchange = Keys.CanHandToGuest(actorId, targetRoomId, guestId, guest.RoomId);
            if (!exchange.Success) return exchange;

            // Validation includes both keys and both rooms. No state changes if a key is wrong,
            // another player holds it, or a pending destination has become unavailable.
            int previousRoom = guest.RoomId;
            Services?.FinishCompensationDiscussion(guest, false, "Room keys exchanged instead", false);
            if (guest.Agent.ResponseActionId != null) ClearGuestResponseAction(guest, false);
            Keys.CommitHandToGuest(targetRoomId, guestId, previousRoom);
            Services?.FinishRoomMoveIntent(guest, true);
            ReleaseOwnedRoom(guest, source);
            destination.ReservedGuestId = guestId;
            guest.RoomId = targetRoomId;
            SyncReservationRoom(guest);
            Services?.SyncGuestRoom(guest);
            guest.Agent.PendingMoveRoomId = null;
            guest.Agent.IsRelocating = true;
            guest.Agent.TransferFromRoomId = previousRoom;
            guest.Agent.HeatingDemandMultiplier = guest.Agent.NoiseOutput = 0;
            guest.Agent.NextActivityTime = guest.Agent.ActivityEndsAt = float.PositiveInfinity;
            NeedEvaluator.ClearInstantaneous(guest);
            Incidents.BeginTransfer(guestId, targetRoomId);
            Transition(guest, GuestAgentState.GoingToRoom, Elapsed,
                guest.Name + " exchanged keys and is moving from room " + previousRoom + " to room " + targetRoomId);
            RefreshGuestLoad();
            RefreshElectrical();
            Keys.NotifyHandToGuest(targetRoomId, previousRoom);
            return CommandResult.Ok("New key handed over. The guest is walking to room " + targetRoomId + "; conditions are assessed after arrival.");
        }

        CommandResult ValidateMoveDestination(int actorId, string guestId, int targetRoomId,
            out GuestStay guest, out RoomState source, out RoomState destination)
        {
            guest = null; source = null; destination = null;
            if (actorId < 0) return CommandResult.Fail("Unknown player identity.");
            guest = FindLivingGuest(guestId);
            if (guest == null || !guest.Agent.InAssignedRoom || guest.Agent.IsRelocating)
                return CommandResult.Fail("Choose a checked-in guest who is currently in their room.");
            var intentPermission = Services?.CanBeginRoomMoveIntent(guest) ?? CommandResult.Ok();
            if (!intentPermission.Success) return intentPermission;
            if (targetRoomId == guest.RoomId) return CommandResult.Fail("The guest is already assigned to that room.");
            if (guest.Agent.PendingMoveRoomId.HasValue && guest.Agent.PendingMoveRoomId != targetRoomId)
                return CommandResult.Fail("Cancel the existing proposed move before choosing a different room.");
            if (!rooms.TryGetValue(targetRoomId, out destination) || destination.Occupied ||
                (destination.Reserved && (destination.ReservedGuestId != guestId || guest.Agent.PendingMoveRoomId != targetRoomId)))
                return CommandResult.Fail("The destination must be a real free, unreserved room.");
            if (destination.Cleanliness != Cleanliness.Clean) return CommandResult.Fail("The destination room must be clean.");
            if (RoomTurnoverProtected(destination)) return CommandResult.Fail("The destination must be clear of the previous guest and current cleaning task.");
            if (ContinuousOperations)
            {
                var reservation = FindReservation(guestId);
                if (reservation != null && reservation.Revision >= int.MaxValue - 1)
                    return CommandResult.Fail("The booking must retain a revision for checkout.");
                var datedAvailability = CanReserveInterval(targetRoomId, Elapsed, guest.Agent.CheckoutTime, guest.GuestId);
                if (!datedAvailability.Success) return datedAvailability;
            }
            source = rooms[guest.RoomId];
            if (source.GuestId != guestId) return CommandResult.Fail("The source room no longer belongs to this guest.");
            return CommandResult.Ok();
        }

        public CommandResult AcceptConsequences(int actorId, string guestId)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            if (actorId < 0 || actorId > 1) return CommandResult.Fail("Unknown staff actor.");
            var guest = FindLivingGuest(guestId);
            if (guest == null || (ContinuousOperations ? Services?.CanResolveCompensationDiscussion(guest) != true : !guest.Agent.InAssignedRoom))
                return CommandResult.Fail("Choose a checked-in guest who is currently in their room.");
            if (Incidents.AcknowledgeAttention(guestId) == 0)
                return CommandResult.Fail("This guest has no new active situation to acknowledge.");
            Incidents.RecordIgnored(guest);
            Services?.FinishCompensationDiscussion(guest, false, "Staff left the problem unresolved");
            SignalEvent("Staff accepted the consequences for " + guest.Name + "; discomfort and its costs continue");
            return CommandResult.Ok("Current situations acknowledged. Room conditions, dissatisfaction and financial consequences continue.");
        }
    }
}

