namespace WorstHotel
{
    public sealed partial class HotelSimulation
    {
        /// <summary>Read current sources on either authority or mirror; never advance thermal state or history.</summary>
        public bool TryGetRoomThermalBreakdown(int roomId, out RoomThermalBreakdown thermal, out RoomHeatingDemand demand)
        {
            thermal = default; demand = default;
            if (!rooms.TryGetValue(roomId, out var room)) return false;
            thermal = roomSystem.ThermalBreakdownForRoom(room, Boiler.HeatingOutput, Heaters.HeatForRoom(roomId),
                Boiler.Failed, Boiler.MaintenanceInProgress);
            demand = ActualHeatingDemand(room);
            return true;
        }
    }
}
