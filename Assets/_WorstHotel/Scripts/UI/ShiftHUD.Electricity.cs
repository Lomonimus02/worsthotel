using UnityEngine;
using static WorstHotel.HotelTheme;

namespace WorstHotel
{
    public sealed partial class ShiftHUD
    {
        static void DrawElectricalStatus(GameSession session, float x)
        {
            if (session.Simulation.Electrical == null) return;
            string text = "POWER   ";
            bool attention = false;
            foreach (var circuit in session.Simulation.Electrical.Circuits)
            {
                text += circuit.Id + " " + circuit.RequestedLoad.ToString("F1") + "/" + circuit.Capacity.ToString("F0") +
                    (circuit.Tripped ? " OFF" : circuit.Warning ? " OVERLOAD" : " on") + "    ";
                attention |= circuit.Tripped || circuit.Warning;
            }
            Label(new Rect(x + 14, 120, 730, 25), text, Small, attention ? new Color(1, .68f, .32f) : Paper);
        }
    }
}
