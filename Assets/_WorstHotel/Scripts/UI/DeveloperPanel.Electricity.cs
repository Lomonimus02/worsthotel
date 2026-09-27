#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using UnityEngine;

namespace WorstHotel
{
    public sealed partial class DeveloperPanel
    {
        readonly Dictionary<string, string> circuitLoadInputs = new Dictionary<string, string>();

        void DrawElectricityDebug()
        {
            var simulation = Session.Simulation;
            if (simulation.Electrical == null) return;
            GUILayout.Space(8);
            GUILayout.Label("ELECTRICITY / REQUESTED vs DELIVERED", heading);
            if (simulation.ContinuousOperations) GUILayout.Label("Capacity upgrade: " +
                (string.IsNullOrEmpty(simulation.Electrical.UpgradedCircuitId) ? "available for one circuit" : "installed on circuit " + simulation.Electrical.UpgradedCircuitId), body);
            foreach (var circuit in simulation.Electrical.Circuits)
            {
                string id = circuit.Id;
                bool overridden = circuit.LoadOverride.HasValue;
                GUILayout.Label("Circuit " + id + " [" + string.Join(", ", circuit.RoomIds) + "] / capacity " + circuit.Capacity.ToString("F2") +
                    "\nActual consumers: requested " + circuit.ActualRequestedLoad.ToString("F2") + " / delivered " + circuit.ActualDeliveredLoad.ToString("F2") +
                    (overridden ? "\nDIAGNOSTIC TOTAL OVERRIDE: requested " + circuit.RequestedLoad.ToString("F2") +
                        " / delivered " + circuit.DeliveredLoad.ToString("F2") + " — not the physical consumer sum" : "\nBreaker follows actual demand") +
                    " / overload " + circuit.OverloadSeconds.ToString("F1") + "s / " + (circuit.Tripped ? "TRIPPED" : circuit.Warning ? "WARNING" : "ON") +
                    " / trip count " + circuit.TripCount + (simulation.ContinuousOperations ?
                        "\nLoad ratio " + CapacityLabels.Percent(circuit.LoadRatio) + " / reserve " + CapacityLabels.Reserve(circuit.Reserve) + " u" +
                        " / stress " + CapacityLabels.Percent(circuit.Stress01) + " / " + CapacityLabels.Band(circuit.CapacityBand) : ""), body);
                if (!circuitLoadInputs.TryGetValue(id, out var input)) input = "5";
                NumericRow("Diagnostic total " + id, ref input, "Set override", value => simulation.DebugSetCircuitLoad(id, value));
                circuitLoadInputs[id] = input;
                if (Button("Clear " + id + " override / use actual consumers", overridden))
                    Apply(() => simulation.DebugSetCircuitLoad(id, null));
                GUILayout.BeginHorizontal();
                if (Button("DEBUG trip " + id, !circuit.Tripped)) Apply(() => simulation.DebugTripCircuit(id));
                if (Button("Reset " + id + " (staff 1)", circuit.Tripped)) Apply(() => simulation.ResetCircuit(0, id));
                GUILayout.EndHorizontal();
                foreach (var consumer in simulation.Electrical.Consumers)
                    if (consumer.CircuitId == id)
                        GUILayout.Label("  " + consumer.Id + " / room " + consumer.RoomId + ": actual " +
                            consumer.RequestedLoad.ToString("F2") + " requested / " + consumer.DeliveredLoad.ToString("F2") + " delivered", body);
            }
        }
    }
}
#endif
