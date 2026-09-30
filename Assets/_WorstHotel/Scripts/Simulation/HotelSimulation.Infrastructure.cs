using System;

namespace WorstHotel
{
    public sealed partial class HotelSimulation
    {
        public RoomInfrastructureSettings InfrastructureSettings => roomSystem.Infrastructure;

        public CommandResult SetRadiatorSetting(int actorId, int roomId, int setting)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            if (OwnershipLost) return CommandResult.Fail("Ownership revoked. Start a new hotel.");
            if (actorId < 0 || actorId > 1 || !rooms.TryGetValue(roomId, out var room) || setting < 0 || setting > 3)
                return CommandResult.Fail("Choose a real room and radiator level 0–3.");
            if (room.RadiatorSetting == setting) return CommandResult.Ok("Radiator setting unchanged.");
            ApplyRadiatorSetting(room, setting);
            if (room.GuestId != null) Services?.RecordStaffAction(room.GuestId, IncidentReason.Temperature);
            return CommandResult.Ok("Room " + roomId + " radiator " + setting + "/3. Higher settings increase central heating demand.");
        }

        // Both staff and a physically staged guest use the same room valve and boiler load.
        // The guest response validates ownership and cause before reaching this shared mutation.
        internal void ApplyRadiatorSetting(RoomState room, int setting)
        {
            room.RadiatorSetting = setting;
            RefreshGuestLoad();
            ObserveInfrastructureChanges();
        }

        public CommandResult BreakRoomLamp(int roomId)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            if (OwnershipLost) return CommandResult.Fail("Ownership revoked. Start a new hotel.");
            if (!rooms.TryGetValue(roomId, out var room)) return CommandResult.Fail("Unknown room.");
            room.LampCondition = 0; room.LampBroken = true;
            return CommandResult.Ok("Room " + roomId + " bedside lamp burnt out by developer command.");
        }
    }
}
