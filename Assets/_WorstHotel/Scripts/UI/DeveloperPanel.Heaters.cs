#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

namespace WorstHotel
{
    public sealed partial class DeveloperPanel
    {
        void DrawHeaterDebug()
        {
            var heaters = Session.Simulation.Heaters;
            if (heaters == null) return;
            GUILayout.Space(8);
            GUILayout.Label("PORTABLE HEATER", heading);
            foreach (var heater in heaters.Items)
            {
                GUILayout.Label(heater.Id + " / room " + (heater.RoomId.HasValue ? heater.RoomId.Value.ToString() : "none / held or outside") +
                    " / switch " + (heater.SwitchedOn ? "ON" : "OFF") + " / power " + (heater.Powered ? "available" : "lost") +
                    "\nSupplemental target heat " + heater.EffectiveHeatOutput.ToString("F1") + "°C / requested electric load " +
                    heater.DemandedElectricalLoad.ToString("F2"), body);
                bool canTurnOn = Session.Phase == DayPhase.Service && heater.RoomId.HasValue;
                if (Button(heater.SwitchedOn ? "Switch OFF (staff 1)" : "Switch ON (staff 1)", heater.SwitchedOn || canTurnOn))
                {
                    string id = heater.Id;
                    bool switchedOn = !heater.SwitchedOn;
                    Apply(() => Session.SetHeaterSwitch(0, id, switchedOn));
                }
                if (!heater.SwitchedOn && !canTurnOn)
                    GUILayout.Label("To switch on: start service and place the real heater fully inside a room. Held/outside placement supplies no heat or demand.", body);
            }
        }
    }
}
#endif
