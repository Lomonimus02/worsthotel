using System;
using System.Collections.Generic;

namespace WorstHotel
{
    public sealed class ElectricalCircuit
    {
        public string Id { get; }
        public IReadOnlyList<int> RoomIds { get; }
        public float Capacity { get; }
        public float RatedCapacity => Capacity;
        public float LoadRatio => (float)Math.Min(float.MaxValue, (double)RequestedLoad / Capacity);
        public float Reserve => Capacity - RequestedLoad;
        public float Stress01 => Math.Min(1, OverloadSeconds / settings.TripSeconds);
        public CapacityBand CapacityBand => Tripped || Stress01 >= settings.CriticalStress ? CapacityBand.Critical :
            LoadRatio > 1 ? CapacityBand.Overloaded : LoadRatio >= settings.StrainedLoadRatio ? CapacityBand.Strained : CapacityBand.Comfortable;
        private readonly ElectricitySettings settings;
        /// <summary>Unmodified sum of the real registered consumers, including during a diagnostic override.</summary>
        public float ActualRequestedLoad { get; internal set; }
        public float ActualDeliveredLoad => HasPower ? ActualRequestedLoad : 0;
        /// <summary>Runtime-only breaker experiment. It neither invents nor rescales physical consumers.</summary>
        public float? LoadOverride { get; internal set; }
        public float RequestedLoad => LoadOverride ?? ActualRequestedLoad;
        /// <summary>Effective diagnostic total when overridden; use ActualDeliveredLoad for physical delivery.</summary>
        public float DeliveredLoad => HasPower ? RequestedLoad : 0;
        public float OverloadSeconds { get; internal set; }
        public bool Warning { get; internal set; }
        public bool Tripped { get; internal set; }
        public bool HasPower => !Tripped;
        public int TripCount { get; internal set; }
        internal ElectricalCircuit(string id, int[] rooms, ElectricitySettings settings)
        { Id = id; RoomIds = Array.AsReadOnly(rooms); this.settings = settings; Capacity = settings.CircuitCapacity; }
    }

    /// <summary>A diagnostic snapshot. IDs identify real guests and registered devices, never duplicated synthetic loads.</summary>
    public sealed class PowerConsumer
    {
        public string Id { get; }
        public int? RoomId { get; }
        public string CircuitId { get; }
        public float RequestedLoad { get; }
        public float DeliveredLoad { get; }
        internal PowerConsumer(string id, int? roomId, string circuitId, float requested, float delivered)
        { Id = id; RoomId = roomId; CircuitId = circuitId; RequestedLoad = requested; DeliveredLoad = delivered; }
    }
}
