namespace WorstHotel
{
    public sealed partial class HotelSimulation
    {
        public RoomKeySystem Keys { get; private set; }

        void InitializeRoomKeys()
        {
            Keys = new RoomKeySystem(rooms.Keys);
            Keys.Changed += (key, reason) => SignalEvent("Room " + key.RoomId + " key: " + reason);
        }

        // Cancelling a proposed move never releases another guest's reservation or changes the
        // current room/key/activity. Checkout uses the same cleanup before returning owned keys.
        void ReleasePendingMove(GuestStay guest)
        {
            if (guest.Agent == null || !guest.Agent.PendingMoveRoomId.HasValue) return;
            int destinationId = guest.Agent.PendingMoveRoomId.Value;
            if (rooms.TryGetValue(destinationId, out var destination) && destination.ReservedGuestId == guest.GuestId)
                destination.ReservedGuestId = null;
            guest.Agent.PendingMoveRoomId = null;
        }
    }
}

