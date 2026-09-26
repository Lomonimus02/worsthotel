using System;

namespace WorstHotel
{
    public sealed class ElectricitySettings
    {
        public float CircuitCapacity { get; }
        public float OccupiedRoomLoad { get; }
        public float LoudActivityLoad { get; }
        public float WarningSeconds { get; }
        public float TripSeconds { get; }
        public float PowerLossConditionSeverity { get; }

        public ElectricitySettings(float circuitCapacity = 4, float occupiedRoomLoad = 0.85f, float loudActivityLoad = 0.25f,
            float warningSeconds = 6, float tripSeconds = 18, float powerLossConditionSeverity = 0.65f)
        {
            foreach (float value in new[] { circuitCapacity, occupiedRoomLoad, loudActivityLoad, warningSeconds, tripSeconds, powerLossConditionSeverity })
                if (!Number.IsFinite(value) || value < 0) throw new ArgumentException("Electrical tuning must be finite and nonnegative.");
            if (circuitCapacity <= 0 || warningSeconds <= 0 || tripSeconds <= warningSeconds || powerLossConditionSeverity > 1)
                throw new ArgumentException("Circuit capacity must be positive, warning must precede trip, and power-loss severity must be normalized.");
            CircuitCapacity = circuitCapacity; OccupiedRoomLoad = occupiedRoomLoad; LoudActivityLoad = loudActivityLoad;
            WarningSeconds = warningSeconds; TripSeconds = tripSeconds; PowerLossConditionSeverity = powerLossConditionSeverity;
        }
    }
}
