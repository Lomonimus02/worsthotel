using System;
using System.Collections.Generic;

namespace WorstHotel
{
    public sealed class PortableHeaterState
    {
        public string Id { get; }
        public HeaterSettings Settings { get; }
        public int? RoomId { get; internal set; }
        public bool SwitchedOn { get; internal set; }
        public bool Powered { get; internal set; } = true;
        // Demand survives a power cut: restoring a still-overloaded circuit must not hide its consumers.
        public float DemandedElectricalLoad => SwitchedOn && RoomId.HasValue ? Settings.ElectricalLoad : 0;
        public float EffectiveHeatOutput => Powered && SwitchedOn && RoomId.HasValue ? Settings.HeatOutput : 0;
        internal PortableHeaterState(string id, HeaterSettings settings) { Id = id; Settings = settings; }
    }

    public sealed partial class HeaterSystem
    {
        public HeaterSettings Settings { get; }
        public IReadOnlyList<PortableHeaterState> Items { get; }
        private readonly HashSet<int> roomIds;
        private readonly Dictionary<string, PortableHeaterState> heaters = new Dictionary<string, PortableHeaterState>(StringComparer.Ordinal);
        private readonly List<PortableHeaterState> ordered = new List<PortableHeaterState>();

        public HeaterSystem(HeaterSettings settings, IEnumerable<int> roomIds)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            if (roomIds == null) throw new ArgumentNullException(nameof(roomIds));
            this.roomIds = new HashSet<int>();
            foreach (int roomId in roomIds)
                if (roomId <= 0 || !this.roomIds.Add(roomId)) throw new ArgumentException("Heaters require unique positive room IDs.");
            if (this.roomIds.Count == 0) throw new ArgumentException("Heaters require at least one room.");
            Items = ordered.AsReadOnly();
        }

        public PortableHeaterState Find(string id) => id != null && heaters.TryGetValue(id, out var heater) ? heater : null;

        public CommandResult Register(string id, HeaterSettings overrideSettings = null)
        {
            if (ReadOnlyMirror) return CommandResult.Fail(HotelSimulation.MirrorMessage);
            if (string.IsNullOrWhiteSpace(id)) return CommandResult.Fail("Heater registration requires a stable ID.");
            if (heaters.ContainsKey(id)) return CommandResult.Fail("This heater ID is already registered.");
            var heater = new PortableHeaterState(id, overrideSettings ?? Settings);
            heaters.Add(id, heater); ordered.Add(heater);
            ordered.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
            return CommandResult.Ok("Portable heater registered.");
        }

        public CommandResult Unregister(string id)
        {
            if (ReadOnlyMirror) return CommandResult.Fail(HotelSimulation.MirrorMessage);
            var heater = Find(id);
            if (heater == null) return CommandResult.Fail("Heater not found.");
            heater.RoomId = null; heater.SwitchedOn = false; heater.Powered = false;
            heaters.Remove(id); ordered.Remove(heater);
            return CommandResult.Ok("Portable heater removed.");
        }

        public void SwitchAllOff()
        {
            if (ReadOnlyMirror) return;
            foreach (var heater in ordered) heater.SwitchedOn = false;
        }

        public CommandResult AssignRoom(string id, int? roomId)
        {
            if (ReadOnlyMirror) return CommandResult.Fail(HotelSimulation.MirrorMessage);
            var heater = Find(id);
            if (heater == null) return CommandResult.Fail("Heater not found.");
            if (roomId.HasValue && !roomIds.Contains(roomId.Value)) return CommandResult.Fail("Heater placement requires a known room or no room.");
            heater.RoomId = roomId;
            return CommandResult.Ok(roomId.HasValue ? "Heater placed in room " + roomId.Value + "." : "Heater is carried or outside a valid room.");
        }

        public CommandResult SetSwitchedOn(string id, bool switchedOn)
        {
            if (ReadOnlyMirror) return CommandResult.Fail(HotelSimulation.MirrorMessage);
            var heater = Find(id);
            if (heater == null) return CommandResult.Fail("Heater not found.");
            heater.SwitchedOn = switchedOn;
            return CommandResult.Ok(switchedOn ? "Heater switched on." : "Heater switched off.");
        }

        public CommandResult SetPowered(string id, bool powered)
        {
            if (ReadOnlyMirror) return CommandResult.Fail(HotelSimulation.MirrorMessage);
            var heater = Find(id);
            if (heater == null) return CommandResult.Fail("Heater not found.");
            heater.Powered = powered;
            return CommandResult.Ok(powered ? "Heater has power." : "Heater has no power.");
        }

        public float HeatForRoom(int roomId)
        {
            double total = 0;
            foreach (var heater in ordered) if (heater.RoomId == roomId) total += heater.EffectiveHeatOutput;
            return (float)Math.Min(float.MaxValue, total);
        }

        public float DemandForRoom(int roomId)
        {
            double total = 0;
            foreach (var heater in ordered) if (heater.RoomId == roomId) total += heater.DemandedElectricalLoad;
            return (float)Math.Min(float.MaxValue, total);
        }
    }
}


