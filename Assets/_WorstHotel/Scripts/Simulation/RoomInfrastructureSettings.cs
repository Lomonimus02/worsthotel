using System;

namespace WorstHotel
{
    /// <summary>Small local controls, preserving the existing central-heat baseline at valve 1.</summary>
    public sealed class RoomInfrastructureSettings
    {
        public float RadiatorHeatStep { get; }
        public float RadiatorDemandStep { get; }
        public float LampWearPerSecond { get; }
        public RoomInfrastructureSettings(float radiatorHeatStep = .12f, float radiatorDemandStep = .25f,
            float lampWearPerSecond = .035f)
        {
            if (!Number.IsFinite(radiatorHeatStep) || radiatorHeatStep < 0 || radiatorHeatStep > 1 ||
                !Number.IsFinite(radiatorDemandStep) || radiatorDemandStep < 0 ||
                !Number.IsFinite(lampWearPerSecond) || lampWearPerSecond < 0)
                throw new ArgumentException("Infrastructure tuning must be finite and nonnegative.");
            RadiatorHeatStep = radiatorHeatStep; RadiatorDemandStep = radiatorDemandStep; LampWearPerSecond = lampWearPerSecond;
        }
        public float HeatMultiplier(int setting) => setting <= 0 ? 0 : 1 + (Math.Min(3, setting) - 1) * RadiatorHeatStep;
    }
}
