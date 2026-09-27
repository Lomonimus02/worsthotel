using System;

namespace WorstHotel
{
    public sealed partial class BoilerSystem
    {
        private readonly float? operatingSecondsPerDay;
        public bool CapacityModelEnabled => operatingSecondsPerDay.HasValue;
        public float RatedCapacity => CapacityUpgradePurchased ? (float)((double)settings.SafeLoad * settings.Capacity.CapacityUpgradeMultiplier) : settings.SafeLoad;
        public float EffectiveCapacity => CapacityModelEnabled ? (float)Math.Max(float.Epsilon,
            (double)RatedCapacity * (settings.Capacity.ConditionCapacityFloor +
                (1d - settings.Capacity.ConditionCapacityFloor) * Condition / 100d)) : RatedCapacity;
        public float LoadRatio => (float)Math.Min(float.MaxValue, (double)Load / EffectiveCapacity);
        /// <summary>Signed unused capacity in the same demand units as Load. A negative reserve means overload.</summary>
        public float Reserve => EffectiveCapacity - Load;
        public float Stress01 { get; private set; }
        internal CommandResult DebugSetStress(float value)
        {
            if (ReadOnlyMirror) return CommandResult.Fail(HotelSimulation.MirrorMessage);
            if (!CapacityModelEnabled || !Number.IsFinite(value) || value < 0 || value > 1)
                return CommandResult.Fail("Continuous boiler stress must be finite and between 0 and 1.");
            // No time passes, no failure/repair is manufactured, and pressure/condition/history
            // remain untouched. The next ordinary capacity tick evaluates actual demand.
            Stress01 = value;
            return CommandResult.Ok("Diagnostic boiler stress changed. Actual load and ordinary ticks still determine failure or recovery.");
        }
        public CapacityBand CapacityBand => Failed || Stress01 >= settings.Capacity.CriticalStress ? CapacityBand.Critical :
            CapacityBands.ForLoad(LoadRatio, settings.Capacity.BusyLoadRatio, settings.Capacity.StrainedLoadRatio);
        private float CapacityHeatOutput
        {
            get
            {
                if (Load == 0) return 1;
                double ratio = (double)Load / EffectiveCapacity;
                return (float)((1 - settings.Capacity.MaximumStrainedHeatLoss * StrainFraction(ratio)) * Math.Min(1d, 1d / ratio));
            }
        }

        private double StrainFraction(double ratio) => Math.Max(0, Math.Min(1,
            (ratio - settings.Capacity.StrainedLoadRatio) / (1d - settings.Capacity.StrainedLoadRatio)));

        private void TickCapacity(float dt)
        {
            if (MaintenanceInProgress) return;
            if (Failed)
            {
                // Relief and the solo catch deliberately retain their physical, seconds-based behavior.
                if (ReliefActorId >= 0 || SoloValveLatched)
                {
                    double difference = settings.ReliefTarget - Pressure;
                    SetPressure((float)(Pressure + Math.Sign(difference) * Math.Min(Math.Abs(difference), (double)settings.ReliefRate * dt)));
                }
                else SetPressure((float)Math.Min(settings.MaxPressure, Pressure + (double)settings.FailedPressureRise * dt));
                return;
            }

            var tuning = settings.Capacity;
            double days = (double)dt / operatingSecondsPerDay.Value;
            double ratio = (double)Load / EffectiveCapacity;
            double overload = Math.Max(0, ratio - 1);
            double strain = StrainFraction(ratio);
            double poorCondition = (1 - Condition / 100d) * tuning.PoorConditionStressPenalty;
            double stressChange = (strain * tuning.StrainedStressGainPerHotelHour + overload * tuning.StressGainPerHotelHour) *
                (1 + poorCondition) * (EmergencyPatchActive ? tuning.EmergencyPatchStressMultiplier : 1) -
                Math.Max(0, (tuning.StrainedLoadRatio - ratio) / tuning.StrainedLoadRatio) * tuning.StressRecoveryPerHotelHour;
            Stress01 = (float)Math.Max(0, Math.Min(1, Stress01 + stressChange * days * 24));
            double wear = (tuning.RunningWearPerHotelDay * Math.Min(1, ratio) +
                tuning.StrainedWearPerHotelDay * strain + tuning.OverloadWearPerHotelDay * overload) * days;
            SetCondition((float)Math.Max(0, Condition - wear));

            double target = settings.PressureBase + (double)settings.PressureOverloadFactor * overload +
                tuning.PressureStressFactor * (double)Stress01;
            double pressure = Pressure + (target - Pressure) * (1 - Math.Exp(-(double)dt / settings.PressureTimeConstant));
            SetPressure((float)Math.Min(settings.MaxPressure, pressure));
            // Dwell is diagnostic only; momentarily safe load recovers stored stress gradually.
            FailureExposure = overload > 0 ? (float)Math.Min(float.MaxValue, (double)FailureExposure + dt) : 0;
            if (overload > 0 && Stress01 >= 1) ForceFailure();
        }
    }
}
