using System;

namespace WorstHotel
{
    /// <summary>A pure measurement of the same thermal step used by RoomSystem. Demand is not a temperature term.</summary>
    public readonly struct RoomThermalBreakdown
    {
        public int RoomId { get; }
        public float CurrentTemperature { get; }
        public float TemperatureBase { get; }
        public float BoilerOutput { get; }
        public int RadiatorSetting { get; }
        public float RadiatorMultiplier { get; }
        public float HeatingContribution { get; }
        public float HeatLoss { get; }
        public float SupplementalHeat { get; }
        public float BaselineTarget { get; }
        public double TargetTemperature => BaselineTarget + (double)SupplementalHeat;
        public float TimeConstant { get; }
        public bool BoilerFailed { get; }
        public bool BoilerMaintenance { get; }

        internal RoomThermalBreakdown(RoomState room, float temperatureBase, float heatGain, float output,
            float radiatorMultiplier, float supplement, float timeConstant, bool failed, bool maintenance)
        {
            RoomId = room.Profile.Id; CurrentTemperature = room.Temperature;
            TemperatureBase = temperatureBase; BoilerOutput = Number.Clamp(output, 0, 1);
            RadiatorSetting = room.RadiatorSetting; RadiatorMultiplier = radiatorMultiplier;
            HeatingContribution = heatGain * BoilerOutput * radiatorMultiplier;
            HeatLoss = room.EffectiveHeatLoss; SupplementalHeat = supplement; TimeConstant = timeConstant;
            // Preserve the complete production expression. The displayed contribution is rounded
            // independently; feeding that stored float back here changes the original rounding.
            BaselineTarget = temperatureBase + heatGain * Number.Clamp(output, 0, 1) *
                radiatorMultiplier - room.EffectiveHeatLoss;
            BoilerFailed = failed; BoilerMaintenance = maintenance;
        }

        public float TemperatureAfter(float dt)
        {
            if (!Number.IsFinite(dt) || dt < 0 || TimeConstant <= 0)
                throw new ArgumentOutOfRangeException(nameof(dt));
            float blend = 1 - (float)Math.Exp(-dt / TimeConstant);
            // The unassisted simulation deliberately retains its original float arithmetic.
            if (SupplementalHeat == 0)
                return CurrentTemperature + (BaselineTarget - CurrentTemperature) * blend;
            double target = BaselineTarget + (double)SupplementalHeat;
            double temperature = CurrentTemperature + (target - CurrentTemperature) * blend;
            return (float)Math.Max(-float.MaxValue, Math.Min(float.MaxValue, temperature));
        }
    }
}
