using System.Linq;
using UnityEngine;
using static WorstHotel.HotelTheme;

namespace WorstHotel
{
    public sealed partial class ManagementUI
    {
        void DrawPlanningElectricity()
        {
            var electrical = Session.Simulation.Electrical;
            if (electrical == null) return;
            string forecast = "POWER ";
            bool risk = false;
            foreach (var circuit in electrical.Circuits)
            {
                int bookings = Session.Plan.Assignments.Count(a => circuit.RoomIds.Contains(a.RoomId));
                float baseline = bookings * Session.Simulation.ElectricitySettings.OccupiedRoomLoad;
                forecast += circuit.Id + " " + baseline.ToString("F1") + "/" + circuit.Capacity.ToString("F0") + (circuit.Tripped ? " OFF" : "") + "  ";
                risk |= circuit.Tripped || baseline > circuit.Capacity;
            }
            Label(new Rect(1135, 674, 352, 47), forecast + "\nHeater +" + Session.Simulation.HeaterSettings.ElectricalLoad.ToString("F0") +
                " · TV/music +" + Session.Simulation.ElectricitySettings.LoudActivityLoad.ToString("F2"), Small, risk ? Red : Muted);
        }

        static string RoomPowerDescription(RoomState room) => "Circuit " + room.CircuitId + (room.HasPower ? " powered" : " POWER OFF");
    }
}
