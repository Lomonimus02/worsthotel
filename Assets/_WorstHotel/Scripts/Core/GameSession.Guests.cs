using System.Linq;

namespace WorstHotel
{
    public sealed partial class GameSession
    {
        public CommandResult RequestQuiet(int playerId, string guestId)
        {
            if (ForwardLan(LanCommandKind.RequestQuiet, guestId)) return CommandResult.Ok(LastMessage);
            if (HasGuestConversation(playerId, guestId))
                return GuestCommand(Simulation.RequestQuiet(playerId, guestId));
            if (Phase != DayPhase.Service || !PlayerInteractor.TryGetPlayer(playerId, out var player) || !player.CanAct)
                return CommandResult.Fail("Visit the noisy guest to ask for quiet.");
            var conversation = player.Focused as GuestReceptionInteraction;
            var door = player.Focused as RoomNoiseInteraction;
            if (!door && player.Focused is DoorInteractable roomDoor) door = roomDoor.Conversation;
            bool guestAnswered = conversation && conversation.GuestId == guestId && conversation.OwnerActorId == playerId &&
                conversation.HoldProgress >= 1;
            bool doorAnswered = door && door.CanRequestQuietFor(playerId, guestId);
            if (!guestAnswered && !doorAnswered) return CommandResult.Fail("Knock at the noisy room or speak with its guest first.");
            return GuestCommand(Simulation.RequestQuiet(playerId, guestId));
        }
        public CommandResult CheckInGuest(int actorId, string guestId)
        {
            if (Phase != DayPhase.Service) return CommandResult.Fail("Check-in is available during service.");
            return GiveRoomKey(actorId, guestId);
        }
        public CommandResult GiveRoomKey(int playerId, string guestId)
        {
            if (Phase != DayPhase.Service || !PlayerInteractor.TryGetPlayer(playerId, out var player) ||
                !player.CanAct || !player.HeldBody)
                return GuestCommand(CommandResult.Fail("Carry the assigned room key to this guest."));
            var recipient = player.Focused as GuestReceptionInteraction;
            var key = player.HeldBody.GetComponent<RoomKeyItem>();
            var guest = Simulation.Guests.FirstOrDefault(stay => stay.GuestId == guestId);
            if (!recipient || recipient.GuestId != guestId || !key || key.BoundSimulation != Simulation || guest?.Agent == null)
                return GuestCommand(CommandResult.Fail("Aim at the guest while holding their room key."));
            int destination = guest.Agent.PendingMoveRoomId ?? guest.RoomId;
            if (key.roomId != destination)
                return GuestCommand(CommandResult.Fail("This guest needs key " + destination + "; you are carrying " + key.roomId + "."));
            var result = guest.Agent.PendingMoveRoomId.HasValue ?
                Simulation.MoveGuest(playerId, guestId, destination) : Simulation.CheckIn(playerId, guestId);
            if (result.Success) player.ReleaseGrab();
            return GuestCommand(result);
        }
        public CommandResult ReportGuestReachedReception(string guestId) => GuestCommand(Simulation.SignalGuestReachedReception(guestId));
        public CommandResult ReportGuestReachedRoom(string guestId) => GuestCommand(Simulation.SignalGuestReachedRoom(guestId));
        public CommandResult ReportGuestLeft(string guestId) => GuestCommand(Simulation.SignalGuestLeft(guestId));
        CommandResult GuestCommand(CommandResult result)
        {
            LastMessage = result.Message;
            RaiseChanged();
            return result;
        }
    }
}
