namespace WorstHotel
{
    public sealed partial class HousekeepingSystem
    {
        public CommandResult ResetRoomElement(int roomId, RoomDisorder element)
        {
            if (ReadOnlyMirror) return CommandResult.Fail(HotelSimulation.MirrorMessage);
            if (element != RoomDisorder.Waste && element != RoomDisorder.Towels && element != RoomDisorder.Chair)
                return CommandResult.Fail("Unknown room preparation action.");
            if (!rooms.TryGetValue(roomId, out var room) || !Eligible(room) || Find(roomId) == null)
                return CommandResult.Fail("Wait until the room is physically vacant.");
            if ((room.Disorder & element) == 0) return CommandResult.Fail("This part of the room is already prepared.");
            room.Disorder &= ~element;
            Changed?.Invoke(Find(roomId), element + " reset by staff");
            return CommandResult.Ok("Room " + roomId + ": " + element + " prepared.");
        }
    }
}
