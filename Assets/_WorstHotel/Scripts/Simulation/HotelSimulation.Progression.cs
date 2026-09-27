using System.Linq;

namespace WorstHotel
{
    public sealed partial class HotelSimulation
    {
        public bool NorthWingRestored { get; private set; }
        public bool Room102Insulated { get; private set; }
        public int OperationalRoomCount => rooms.Values.Count(room => room.Operational);
        public bool IsRoomOperational(int id) => rooms.TryGetValue(id, out var room) && room.Operational;

        void ApplyProgressionToRooms()
        {
            foreach (var room in rooms.Values)
            {
                room.Operational = !ContinuousOperations || room.Profile.Id <= 106 || NorthWingRestored;
                room.WindowInsulated = room.Profile.Id == 102 && Room102Insulated;
            }
        }

        public CommandResult PurchaseInsulation(int actorId)
        {
            int cost = settings.Economy.InsulationUpgradeCost;
            var allowed = CanPayForEquipment(actorId, cost);
            if (!allowed.Success) return allowed;
            if (Room102Insulated || !rooms.ContainsKey(102)) return CommandResult.Fail("Room 102 window work is already complete or unavailable.");
            var paid = Economy.TrySpend(cost); if (!paid.Success) return paid;
            PeriodCapitalSpend += cost;
            Room102Insulated = true; ApplyProgressionToRooms(); RefreshGuestLoad();
            SignalEvent("Room 102: sealed double windows installed. Heat loss -80%; space heating demand reduced permanently.");
            return CommandResult.Ok("Room 102 insulation installed. Guests keep their room; no temporary service interruption.");
        }

        public CommandResult RestoreNorthWing(int actorId)
        {
            int cost = settings.Economy.WingRestorationCost;
            var allowed = CanPayForEquipment(actorId, cost);
            if (!allowed.Success) return allowed;
            if (NorthWingRestored || !rooms.ContainsKey(110)) return CommandResult.Fail("The North Wing is already open or unavailable.");
            var paid = Economy.TrySpend(cost); if (!paid.Success) return paid;
            PeriodCapitalSpend += cost;
            NorthWingRestored = true; ApplyProgressionToRooms(); RefreshGuestLoad(); RefreshElectrical();
            SignalEvent("NORTH WING OPEN: rooms 107-110 restored. Choose rooms to open for sale in Reception > Room sales.");
            return CommandResult.Ok("North Wing restored. Barrier removed, keys released, heating connected. New rooms remain CLOSED FOR SALE until you choose.");
        }
    }
}
