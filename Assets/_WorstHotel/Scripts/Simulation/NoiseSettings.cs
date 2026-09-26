using System;

namespace WorstHotel
{
    public sealed class NoiseSettings
    {
        public float SharedWallTransmission { get; }
        public float CorridorTransmission { get; }
        public float QuietRequestSeconds { get; }
        public float QuietSourceMultiplier { get; }
        public float RepeatedWarningDurationReduction { get; }
        public float MinimumWarningDurationMultiplier { get; }

        public NoiseSettings(float sharedWallTransmission = 0.70f, float corridorTransmission = 0.30f,
            float quietRequestSeconds = 25, float quietSourceMultiplier = 0.15f,
            float repeatedWarningDurationReduction = .2f, float minimumWarningDurationMultiplier = .4f)
        {
            foreach (float value in new[] { sharedWallTransmission, corridorTransmission, quietRequestSeconds, quietSourceMultiplier,
                repeatedWarningDurationReduction, minimumWarningDurationMultiplier })
                if (!Number.IsFinite(value) || value < 0) throw new ArgumentException("Noise tuning must be finite and nonnegative.");
            if (sharedWallTransmission > 1 || corridorTransmission > sharedWallTransmission || quietRequestSeconds <= 0 || quietSourceMultiplier > 1 ||
                repeatedWarningDurationReduction > 1 || minimumWarningDurationMultiplier <= 0 || minimumWarningDurationMultiplier > 1)
                throw new ArgumentException("Noise transmission must be normalized, corridor transmission cannot exceed a shared wall, and quiet duration must be positive.");
            SharedWallTransmission = sharedWallTransmission; CorridorTransmission = corridorTransmission;
            QuietRequestSeconds = quietRequestSeconds; QuietSourceMultiplier = quietSourceMultiplier;
            RepeatedWarningDurationReduction = repeatedWarningDurationReduction;
            MinimumWarningDurationMultiplier = minimumWarningDurationMultiplier;
        }
    }
}
