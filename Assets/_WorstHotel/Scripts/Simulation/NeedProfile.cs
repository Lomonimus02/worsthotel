using System;

namespace WorstHotel
{
    /// <summary>Immutable comfort preferences carried by one guest archetype snapshot.</summary>
    public sealed class NeedProfile
    {
        public float PreferredTemperatureMin { get; }
        public float PreferredTemperatureMax { get; }
        public float ToleranceTemperatureMin { get; }
        public float ToleranceTemperatureMax { get; }
        public float PreferredNoise { get; }
        public float NoiseTolerance { get; }
        public float PatienceSeconds { get; }

        public NeedProfile(float preferredTemperatureMin, float preferredTemperatureMax,
            float toleranceTemperatureMin, float toleranceTemperatureMax, float preferredNoise,
            float noiseTolerance, float patienceSeconds)
        {
            foreach (float value in new[] { preferredTemperatureMin, preferredTemperatureMax, toleranceTemperatureMin,
                toleranceTemperatureMax, preferredNoise, noiseTolerance, patienceSeconds })
                if (!Number.IsFinite(value)) throw new ArgumentException("Guest need preferences must be finite.");
            if (!Number.IsFinite(toleranceTemperatureMax - toleranceTemperatureMin) ||
                toleranceTemperatureMin > preferredTemperatureMin || preferredTemperatureMin > preferredTemperatureMax ||
                preferredTemperatureMax > toleranceTemperatureMax || preferredNoise < 0 || preferredNoise > noiseTolerance ||
                noiseTolerance > 1 || patienceSeconds <= 0)
                throw new ArgumentException("Guest comfort ranges, noise preferences or patience are invalid.");
            PreferredTemperatureMin = preferredTemperatureMin; PreferredTemperatureMax = preferredTemperatureMax;
            ToleranceTemperatureMin = toleranceTemperatureMin; ToleranceTemperatureMax = toleranceTemperatureMax;
            PreferredNoise = preferredNoise; NoiseTolerance = noiseTolerance; PatienceSeconds = patienceSeconds;
        }

        public static NeedProfile DefaultFor(float coldThreshold, float noiseTolerance, float patience) =>
            new NeedProfile(coldThreshold + 2, coldThreshold + 5, coldThreshold, coldThreshold + 8,
                noiseTolerance * 0.5f, noiseTolerance, patience);
    }
}
