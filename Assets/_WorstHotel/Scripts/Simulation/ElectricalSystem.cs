using System;
using System.Collections.Generic;
using System.Linq;

namespace WorstHotel
{
    /// <summary>Two authored circuits. Requested demand survives a trip, while delivery and physical room power stop.</summary>
    public sealed partial class ElectricalSystem
    {
        public ElectricitySettings Settings { get; }
        public bool ContinuousStress { get; }
        public IReadOnlyList<ElectricalCircuit> Circuits { get; }
        public IReadOnlyList<PowerConsumer> Consumers { get; private set; } = Array.AsReadOnly(new PowerConsumer[0]);
        public event Action<ElectricalCircuit, string> Changed;
        private readonly Dictionary<int, ElectricalCircuit> roomCircuits = new Dictionary<int, ElectricalCircuit>();
        private readonly Dictionary<int, RoomState> rooms = new Dictionary<int, RoomState>();
        private readonly List<PowerConsumer> pending = new List<PowerConsumer>();
        private readonly List<(ElectricalCircuit circuit, string reason)> changes = new List<(ElectricalCircuit, string)>();
        private HeaterSystem lastHeaters;

        public ElectricalSystem(ElectricitySettings settings, IEnumerable<int> roomIds, bool continuousStress = false)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            ContinuousStress = continuousStress;
            if (roomIds == null) throw new ArgumentNullException(nameof(roomIds));
            int[] ids = roomIds.OrderBy(id => id).ToArray();
            if (ids.Length == 0 || ids.Any(id => id < 101 || id > 110) || ids.Distinct().Count() != ids.Length)
                throw new ArgumentException("The authored electrical panel requires unique room IDs from 101 through 110.");
            var circuits = new[]
            {
                new ElectricalCircuit("A", ids.Where(id => id % 2 == 1).ToArray(), this),
                new ElectricalCircuit("B", ids.Where(id => id % 2 == 0).ToArray(), this)
            };
            Circuits = Array.AsReadOnly(circuits);
            foreach (var circuit in circuits)
                foreach (int id in circuit.RoomIds) roomCircuits.Add(id, circuit);
        }

        public ElectricalCircuit Find(string circuitId) => Circuits.FirstOrDefault(circuit => circuit.Id == circuitId);
        public ElectricalCircuit CircuitForRoom(int roomId) => roomCircuits.TryGetValue(roomId, out var circuit) ? circuit : null;

        public void Tick(IEnumerable<GuestStay> guests, IEnumerable<RoomState> roomStates, HeaterSystem heaters, float dt)
        {
            if (ReadOnlyMirror) return;
            if (!Number.IsFinite(dt) || dt < 0) throw new ArgumentOutOfRangeException(nameof(dt));
            if (guests == null || roomStates == null || heaters == null) throw new ArgumentNullException("Electrical guests, rooms and heaters are required.");
            // Validate every identity before committing loads, timers or delivered power.
            var nextRooms = new Dictionary<int, RoomState>();
            foreach (var room in roomStates)
            {
                if (room == null || !roomCircuits.ContainsKey(room.Profile.Id) || nextRooms.ContainsKey(room.Profile.Id))
                    throw new ArgumentException("Electrical room states must uniquely match the authored circuits.");
                nextRooms.Add(room.Profile.Id, room);
            }
            if (nextRooms.Count != roomCircuits.Count) throw new ArgumentException("Every circuit room requires its authoritative room state.");
            var guestIds = new HashSet<string>(StringComparer.Ordinal);
            pending.Clear();
            foreach (var guest in guests)
            {
                if (guest == null || !guestIds.Add(guest.GuestId) || !nextRooms.ContainsKey(guest.RoomId))
                    throw new ArgumentException("Electrical guests require unique IDs and valid assigned rooms.");
                if (guest.Agent == null || !guest.Agent.InAssignedRoom || nextRooms[guest.RoomId].GuestId != guest.GuestId) continue;
                float demand = (float)Math.Min(float.MaxValue, (double)Settings.OccupiedRoomLoad +
                    (guest.Agent.ActivityStaged && (guest.Agent.Activity == GuestActivity.LoudRoom ||
                        guest.Agent.Activity == GuestActivity.WatchTV) ? Settings.LoudActivityLoad : 0));
                pending.Add(new PowerConsumer("guest:" + guest.GuestId, guest.RoomId, roomCircuits[guest.RoomId].Id, demand, 0));
            }
            foreach (var heater in heaters.Items)
            {
                if (heater.RoomId.HasValue && !nextRooms.ContainsKey(heater.RoomId.Value))
                    throw new ArgumentException("A heater cannot demand power outside the circuit room registry.");
                pending.Add(new PowerConsumer("heater:" + heater.Id, heater.RoomId,
                    heater.RoomId.HasValue ? roomCircuits[heater.RoomId.Value].Id : null, heater.DemandedElectricalLoad, 0));
            }
            pending.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
            rooms.Clear();
            foreach (var pair in nextRooms) rooms.Add(pair.Key, pair.Value);
            lastHeaters = heaters;
            changes.Clear();
            foreach (var circuit in Circuits)
            {
                double requested = 0;
                foreach (var consumer in pending) if (consumer.CircuitId == circuit.Id) requested += consumer.RequestedLoad;
                circuit.ActualRequestedLoad = (float)Math.Min(float.MaxValue, requested);
                if (circuit.RequestedLoad <= circuit.Capacity)
                {
                    if (circuit.Warning) changes.Add((circuit, "overload warning cleared"));
                    circuit.Warning = false;
                    circuit.OverloadSeconds = ContinuousStress ?
                        (float)Math.Max(0, circuit.OverloadSeconds - (double)dt * Settings.StressRecoveryPerSecond) : 0;
                    continue;
                }
                if (circuit.Tripped) continue;
                circuit.OverloadSeconds = (float)Math.Min(Settings.TripSeconds, (double)circuit.OverloadSeconds + dt);
                if (!circuit.Warning && circuit.OverloadSeconds >= Settings.WarningSeconds)
                { circuit.Warning = true; changes.Add((circuit, "overload warning")); }
                if (circuit.OverloadSeconds >= Settings.TripSeconds)
                {
                    circuit.Tripped = true; circuit.Warning = false; circuit.TripCount++;
                    changes.Add((circuit, "tripped after sustained overload"));
                }
            }
            ApplyPower(pending);
            foreach (var change in changes) Changed?.Invoke(change.circuit, change.reason);
        }

        public CommandResult ResetCircuit(int actorId, string circuitId)
        {
            if (ReadOnlyMirror) return CommandResult.Fail(HotelSimulation.MirrorMessage);
            if (actorId < 0 || actorId > 1) return CommandResult.Fail("Unknown staff actor.");
            var circuit = Find(circuitId);
            if (circuit == null) return CommandResult.Fail("Unknown electrical circuit.");
            if (!circuit.Tripped) return CommandResult.Fail("This circuit is already supplying power.");
            circuit.Tripped = false; circuit.Warning = false; circuit.OverloadSeconds = 0;
            ApplyPower(Consumers);
            Changed?.Invoke(circuit, "reset; power restored");
            return CommandResult.Ok("Circuit " + circuit.Id + " reset. Remaining demand can overload it again.");
        }

        public CommandResult ForceTrip(string circuitId)
        {
            if (ReadOnlyMirror) return CommandResult.Fail(HotelSimulation.MirrorMessage);
            var circuit = Find(circuitId);
            if (circuit == null) return CommandResult.Fail("Unknown electrical circuit.");
            if (circuit.Tripped) return CommandResult.Fail("This circuit is already tripped.");
            circuit.Tripped = true; circuit.Warning = false; circuit.TripCount++;
            ApplyPower(Consumers);
            Changed?.Invoke(circuit, "tripped by developer command");
            return CommandResult.Ok("Developer command tripped circuit " + circuit.Id + ".");
        }

        /// <summary>Override the total seen by the breaker without fabricating devices or changing their actual demand.</summary>
        public CommandResult DebugOverrideLoad(string circuitId, float? total)
        {
            if (ReadOnlyMirror) return CommandResult.Fail(HotelSimulation.MirrorMessage);
            var circuit = Find(circuitId);
            if (circuit == null) return CommandResult.Fail("Unknown electrical circuit.");
            if (total.HasValue && (!Number.IsFinite(total.Value) || total.Value < 0))
                return CommandResult.Fail("Diagnostic circuit demand must be a finite nonnegative total, or cleared.");
            if (circuit.LoadOverride == total) return CommandResult.Ok("Diagnostic circuit demand is unchanged.");
            circuit.LoadOverride = total;
            // Changing a diagnostic input must not erase continuous stored stress without elapsed recovery time.
            // Clearing the override never repairs an existing trip or advances the hotel clock.
            if (circuit.RequestedLoad <= circuit.Capacity)
            { circuit.Warning = false; if (!ContinuousStress) circuit.OverloadSeconds = 0; }
            Changed?.Invoke(circuit, total.HasValue ? "diagnostic load override applied; actual consumers retained" :
                "diagnostic load override cleared; actual consumer demand restored");
            return CommandResult.Ok(total.HasValue ? "Circuit " + circuit.Id + " uses a diagnostic total; actual consumers are unchanged." :
                "Circuit " + circuit.Id + " follows actual consumer demand. A tripped breaker still needs reset.");
        }

        private void ApplyPower(IEnumerable<PowerConsumer> inputs)
        {
            var snapshot = new List<PowerConsumer>();
            foreach (var input in inputs)
            {
                var circuit = Find(input.CircuitId);
                snapshot.Add(new PowerConsumer(input.Id, input.RoomId, input.CircuitId, input.RequestedLoad,
                    circuit != null && circuit.HasPower ? input.RequestedLoad : 0));
            }
            Consumers = snapshot.AsReadOnly();
            foreach (var room in rooms.Values)
            {
                var circuit = roomCircuits[room.Profile.Id];
                room.CircuitId = circuit.Id; room.HasPower = circuit.HasPower && room.Operational;
                room.PowerLossConditionSeverity = Settings.PowerLossConditionSeverity;
            }
            if (lastHeaters != null)
                foreach (var heater in lastHeaters.Items)
                    lastHeaters.SetPowered(heater.Id, heater.RoomId.HasValue && roomCircuits[heater.RoomId.Value].HasPower);
        }
    }
}


