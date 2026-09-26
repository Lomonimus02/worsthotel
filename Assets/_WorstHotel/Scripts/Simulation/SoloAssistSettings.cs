using System;

namespace WorstHotel
{
    /// <summary>Only the explicitly cooperative repair gains an alternate solo mechanism.</summary>
    public sealed class SoloAssistSettings
    {
        public float SafeValveHoldSeconds { get; }
        public float ValveLatchSeconds { get; }
        public SoloAssistSettings(float safeValveHoldSeconds = 2, float valveLatchSeconds = 18)
        {
            if (!Number.IsFinite(safeValveHoldSeconds) || safeValveHoldSeconds <= 0 ||
                !Number.IsFinite(valveLatchSeconds) || valveLatchSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(safeValveHoldSeconds), "Solo repair durations must be positive and finite.");
            SafeValveHoldSeconds = safeValveHoldSeconds; ValveLatchSeconds = valveLatchSeconds;
        }
    }
}
