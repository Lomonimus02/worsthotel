using System;
using System.Collections.Generic;
using System.Linq;

namespace WorstHotel
{
    public sealed partial class HotelSimulation
    {
        public const int InfrastructureHistoryLimit = 64;
        readonly List<InfrastructureHistoryEntry> infrastructureHistory = new List<InfrastructureHistoryEntry>(InfrastructureHistoryLimit);
        readonly Dictionary<(InfrastructureChangeKind, string), float> infrastructureValues =
            new Dictionary<(InfrastructureChangeKind, string), float>();
        readonly HashSet<(InfrastructureChangeKind, string)> infrastructureSeen = new HashSet<(InfrastructureChangeKind, string)>();
        public IReadOnlyList<InfrastructureHistoryEntry> InfrastructureHistory => infrastructureHistory.AsReadOnly();
        bool infrastructureHistoryReady, infrastructurePriming;
        int infrastructureDiagnosticDepth, infrastructureTickDepth;
        long infrastructureSequence;

        void InitializeInfrastructureHistory()
        {
            infrastructureHistoryReady = true; infrastructurePriming = true;
            ObserveInfrastructureChanges();
            infrastructurePriming = false;
        }

        /// <summary>Labels only actual changes during a developer command; it never signals a gameplay event.</summary>
        public IDisposable BeginDiagnosticInfrastructureChange()
        {
            if (IsReadOnlyMirror) return new DiagnosticInfrastructureScope(null);
            // Do not mislabel an earlier ordinary change as belonging to this developer command.
            ObserveInfrastructureChanges();
            infrastructureDiagnosticDepth++;
            return new DiagnosticInfrastructureScope(this);
        }

        sealed class DiagnosticInfrastructureScope : IDisposable
        {
            HotelSimulation owner;
            public DiagnosticInfrastructureScope(HotelSimulation owner) { this.owner = owner; }
            public void Dispose()
            {
                var model = owner; owner = null;
                if (model == null) return;
                try { model.ObserveInfrastructureChanges(); }
                finally { model.infrastructureDiagnosticDepth--; }
            }
        }

        void BeginInfrastructureTick()
        {
            ObserveInfrastructureChanges();
            infrastructureTickDepth++;
        }

        void EndInfrastructureTick(bool completed)
        {
            infrastructureTickDepth--;
            if (completed) ObserveInfrastructureChanges();
        }

        // Read APIs do not call this. Tick and canonical successful mutation boundaries do.
        internal void ObserveInfrastructureChanges()
        {
            if (!infrastructureHistoryReady || IsReadOnlyMirror || infrastructureTickDepth > 0) return;
            infrastructureSeen.Clear();
            foreach (var room in rooms.Values.OrderBy(item => item.Profile.Id))
                ObserveInfrastructureValue(InfrastructureChangeKind.RadiatorSetting, "room/" + room.Profile.Id + "/radiator",
                    room.Profile.Id, room.RadiatorSetting, room.RadiatorSetting);
            foreach (var heater in Heaters.Items)
            {
                string id = "heater/" + heater.Id;
                ObserveInfrastructureValue(InfrastructureChangeKind.HeaterRoom, id, heater.RoomId, heater.RoomId ?? -1, -1);
                ObserveInfrastructureValue(InfrastructureChangeKind.HeaterSwitch, id, heater.RoomId, heater.SwitchedOn ? 1 : 0, 0);
            }
            ObserveInfrastructureValue(InfrastructureChangeKind.BoilerService, "boiler", null, (int)Boiler.ActiveServiceKind, 0);
            ObserveInfrastructureValue(InfrastructureChangeKind.BoilerPatch, "boiler", null, Boiler.EmergencyPatchActive ? 1 : 0, 0);
            ObserveInfrastructureValue(InfrastructureChangeKind.BoilerBand, "boiler", null, (int)Boiler.CapacityBand, (int)Boiler.CapacityBand);
            ObserveInfrastructureValue(InfrastructureChangeKind.BoilerStressCritical, "boiler", null,
                Boiler.Stress01 >= settings.Boiler.Capacity.CriticalStress ? 1 : 0, 0);
            ObserveInfrastructureValue(InfrastructureChangeKind.BoilerFailure, "boiler", null, Boiler.Failed ? 1 : 0, 0);
            if (Electrical != null)
                foreach (var circuit in Electrical.Circuits)
                {
                    string id = "circuit/" + circuit.Id;
                    ObserveInfrastructureValue(InfrastructureChangeKind.CircuitBand, id, null,
                        (int)circuit.CapacityBand, (int)circuit.CapacityBand, circuit);
                    ObserveInfrastructureValue(InfrastructureChangeKind.CircuitWarning, id, null, circuit.Warning ? 1 : 0, 0, circuit);
                    ObserveInfrastructureValue(InfrastructureChangeKind.CircuitTrip, id, null, circuit.Tripped ? 1 : 0, 0, circuit);
                }
            // A circuit's actual trip/reset is recorded before its resulting heater heat loss/return.
            foreach (var heater in Heaters.Items)
                ObserveInfrastructureValue(InfrastructureChangeKind.HeaterPower, "heater/" + heater.Id, heater.RoomId,
                    heater.Powered ? 1 : 0, heater.Powered ? 1 : 0);
            if (infrastructureValues.Count != infrastructureSeen.Count)
                foreach (var key in infrastructureValues.Keys.Where(key => !infrastructureSeen.Contains(key)).ToArray())
                    infrastructureValues.Remove(key);
        }

        void ObserveInfrastructureValue(InfrastructureChangeKind kind, string entityId, int? roomId,
            float value, float initialValue, ElectricalCircuit circuit = null)
        {
            var key = (kind, entityId); infrastructureSeen.Add(key);
            if (!infrastructureValues.TryGetValue(key, out float previous)) previous = initialValue;
            infrastructureValues[key] = value;
            if (previous == value || infrastructurePriming) return;
            if (circuit == null && roomId.HasValue) circuit = Electrical?.CircuitForRoom(roomId.Value);
            if (infrastructureHistory.Count == InfrastructureHistoryLimit) infrastructureHistory.RemoveAt(0);
            infrastructureHistory.Add(new InfrastructureHistoryEntry(++infrastructureSequence, Elapsed, kind, entityId,
                roomId, previous, value, infrastructureDiagnosticDepth > 0, Boiler, circuit));
        }
    }
}
