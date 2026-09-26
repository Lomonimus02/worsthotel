using System;

namespace WorstHotel
{
    public sealed partial class HotelSimulation
    {
        public RoomInfrastructureSettings InfrastructureSettings => roomSystem.Infrastructure;

        public CommandResult SetRadiatorSetting(int actorId, int roomId, int setting)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            if (actorId < 0 || actorId > 1 || !rooms.TryGetValue(roomId, out var room) || setting < 0 || setting > 3)
                return CommandResult.Fail("Choose a real room and radiator level 0–3.");
            if (room.RadiatorSetting == setting) return CommandResult.Ok("Radiator setting unchanged.");
            room.RadiatorSetting = setting;
            RefreshGuestLoad();
            return CommandResult.Ok("Room " + roomId + " radiator " + setting + "/3. Higher settings increase central heating demand.");
        }

        public CommandResult BreakRoomLamp(int roomId)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            if (!rooms.TryGetValue(roomId, out var room)) return CommandResult.Fail("Unknown room.");
            room.LampCondition = 0; room.LampBroken = true;
            return CommandResult.Ok("Room " + roomId + " bedside lamp burnt out by developer command.");
        }
    }
}
