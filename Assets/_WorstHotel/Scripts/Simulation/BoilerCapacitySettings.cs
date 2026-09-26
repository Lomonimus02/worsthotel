using System;

namespace WorstHotel
{
    /// <summary>Continuous-operation tuning. Wear is per hotel day and stress is per hotel hour.</summary>
    public sealed class BoilerCapacitySettings
    {
        public float ConditionCapacityFloor { get; }
        public float StrainedLoadRatio { get; }
        public float CriticalStress { get; }
        public float StressGainPerHotelHour { get; }
        public float StressRecoveryPerHotelHour { get; }
        public float PoorConditionStressPenalty { get; }
        public float RunningWearPerHotelDay { get; }
        public float OverloadWearPerHotelDay { get; }
        public float PressureStressFactor { get; }

        public BoilerCapacitySettings(float conditionCapacityFloor = .85f, float strainedLoadRatio = .85f,
            float criticalStress = .8f, float stressGainPerHotelHour = .25f, float stressRecoveryPerHotelHour = .25f,
            float poorConditionStressPenalty = .5f, float runningWearPerHotelDay = 3,
            float overloadWearPerHotelDay = 12, float pressureStressFactor = 30)
        {
            foreach (float value in new[] { conditionCapacityFloor, strainedLoadRatio, criticalStress,
                stressGainPerHotelHour, stressRecoveryPerHotelHour, poorConditionStressPenalty,
                runningWearPerHotelDay, overloadWearPerHotelDay, pressureStressFactor })
                if (!Number.IsFinite(value) || value < 0)
                    throw new ArgumentException("Boiler capacity tuning must be finite and nonnegative.");
            if (conditionCapacityFloor <= 0 || conditionCapacityFloor > 1 || strainedLoadRatio <= 0 ||
                strainedLoadRatio >= 1 || criticalStress <= 0 || criticalStress > 1)
                throw new ArgumentException("Capacity floor and critical stress must be normalized, and strain must begin below capacity.");
            ConditionCapacityFloor = conditionCapacityFloor; StrainedLoadRatio = strainedLoadRatio;
            CriticalStress = criticalStress; StressGainPerHotelHour = stressGainPerHotelHour;
            StressRecoveryPerHotelHour = stressRecoveryPerHotelHour; PoorConditionStressPenalty = poorConditionStressPenalty;
            RunningWearPerHotelDay = runningWearPerHotelDay; OverloadWearPerHotelDay = overloadWearPerHotelDay;
            PressureStressFactor = pressureStressFactor;
        }
    }
}
