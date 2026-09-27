namespace WorstHotel
{
    /// <summary>A fresh, read-only estimate using current equipment, valves and accepted stay intervals.
    /// Typical demand assumes quiet rooms; peak adds one largest shower. Neither predicts schedules or failures.</summary>
    public sealed class BookingLoadForecast
    {
        public bool Available { get; }
        public string Reason { get; }
        public string OfferId { get; }
        public int RoomId { get; }
        public float ArrivalAt { get; }
        public float CheckoutAt { get; }
        public float CurrentDemand { get; }
        public CapacityBand CurrentBand { get; }
        public float EffectiveCapacity { get; }
        public float TypicalDemand { get; }
        public float OneShowerPeakDemand { get; }
        public float TypicalRatio { get; }
        public float PeakRatio { get; }
        public CapacityBand TypicalBand { get; }
        public string CircuitId { get; }
        public float CircuitCapacity { get; }
        public float TypicalCircuitDemand { get; }
        public float CircuitReserve { get; }
        public int MaxConcurrentGuests { get; }

        internal BookingLoadForecast(string offerId, int roomId, string reason)
        { OfferId = offerId; RoomId = roomId; Reason = reason; }

        internal BookingLoadForecast(string offerId, int roomId, float arrivalAt, float checkoutAt,
            float currentDemand, CapacityBand currentBand, float capacity, float typical, float peak,
            CapacityBand typicalBand, string circuitId, float circuitCapacity, float circuitDemand, int maxGuests)
        {
            Available = true; Reason = string.Empty; OfferId = offerId; RoomId = roomId;
            ArrivalAt = arrivalAt; CheckoutAt = checkoutAt; CurrentDemand = currentDemand; CurrentBand = currentBand;
            EffectiveCapacity = capacity; TypicalDemand = typical; OneShowerPeakDemand = peak;
            TypicalRatio = (float)System.Math.Min(float.MaxValue, (double)typical / capacity);
            PeakRatio = (float)System.Math.Min(float.MaxValue, (double)peak / capacity);
            TypicalBand = typicalBand; CircuitId = circuitId; CircuitCapacity = circuitCapacity;
            TypicalCircuitDemand = circuitDemand; CircuitReserve = circuitCapacity - circuitDemand;
            MaxConcurrentGuests = maxGuests;
        }
    }
}
