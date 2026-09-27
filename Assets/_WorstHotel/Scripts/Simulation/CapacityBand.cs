using System;

namespace WorstHotel
{
    /// <summary>Current utilization plus stored equipment stress; independent of the physical pressure gauge.</summary>
    public enum CapacityBand { Comfortable = 0, Strained = 1, Overloaded = 2, Critical = 3, Busy = 4 }

    /// <summary>Semantic severity order. The appended Busy value must never be compared numerically.</summary>
    public static class CapacityBands
    {
        public static bool AtLeast(CapacityBand band, CapacityBand threshold) => Rank(band) >= Rank(threshold);

        public static CapacityBand ForLoad(float ratio, float busyThreshold, float strainedThreshold)
        {
            if (!Number.IsFinite(ratio) || ratio < 0) throw new ArgumentOutOfRangeException(nameof(ratio));
            if (!Number.IsFinite(busyThreshold) || !Number.IsFinite(strainedThreshold) ||
                busyThreshold <= 0 || busyThreshold >= strainedThreshold || strainedThreshold >= 1)
                throw new ArgumentException("Capacity thresholds must satisfy 0 < Busy < Strained < 1.");
            return ratio > 1 ? CapacityBand.Overloaded : ratio >= strainedThreshold ? CapacityBand.Strained :
                ratio >= busyThreshold ? CapacityBand.Busy : CapacityBand.Comfortable;
        }

        static int Rank(CapacityBand band)
        {
            switch (band)
            {
                case CapacityBand.Comfortable: return 0;
                case CapacityBand.Busy: return 1;
                case CapacityBand.Strained: return 2;
                case CapacityBand.Overloaded: return 3;
                case CapacityBand.Critical: return 4;
                default: throw new ArgumentOutOfRangeException(nameof(band));
            }
        }
    }
}
