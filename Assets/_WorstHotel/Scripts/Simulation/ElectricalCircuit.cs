using System;
using System.Collections.Generic;

namespace WorstHotel
{
    public sealed class ElectricalCircuit
    {
        public string Id { get; }
        public IReadOnlyList<int> RoomIds { get; }
        public float Capacity { get; }
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
        internal ElectricalCircuit(string id, int[] rooms, float capacity)
        { Id = id; RoomIds = Array.AsReadOnly(rooms); Capacity = capacity; }
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
