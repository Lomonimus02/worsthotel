using UnityEngine;

namespace WorstHotel
{
    [CreateAssetMenu(menuName = "Worst Hotel/Boiler configuration")]
    public sealed class BoilerConfig : ScriptableObject
    {
        [Range(0, 100)] public float initialCondition = 85;
        [Min(0.1f)] public float safeLoad = 4.6f;
        public float baseWearPerMinute = 4;
        public float overloadWearPerMinute = 16;
        public float pressureBase = 40;
        public float pressureOverloadFactor = 70;
        public float pressureConditionThreshold = 65;
        public float pressureConditionFactor = 0.85f;
        public float pressureTimeConstant = 12;
        public float failurePressure = 85;
        public float failureExposureSeconds = 8;
        public float warningPressure = 70;
        public float heatOverloadLoss = 0.45f;
        public float heatConditionThreshold = 70;
        public float heatConditionLoss = 0.004f;
        [Range(0, 1)] public float minimumHeatOutput = 0.25f;
        [Range(0, 1)] public float failedHeatOutput = 0.1f;
        public float startPressure = 20;
        public float repairSafeMin = 35;
        public float repairSafeMax = 55;
        public float reliefTarget = 43;
        public float reliefRate = 4;
        public float failedPressureRise = 4;
        public float latchTravelSeconds = 2.5f;
        public float restartPressure = 40;
        public float maxPressure = 120;

        [Header("Continuous operation: capacity and hotel-time wear")]
        [Range(.01f, 1)] public float conditionCapacityFloor = .85f;
        [Range(.01f, .99f)] public float strainedLoadRatio = .85f;
        [Range(.01f, 1)] public float criticalStress = .8f;
        [Min(0)] public float stressGainPerHotelHour = .25f;
        [Min(0)] public float stressRecoveryPerHotelHour = .25f;
        [Min(0)] public float poorConditionStressPenalty = .5f;
        [Min(0)] public float runningWearPerHotelDay = 3;
        [Min(0)] public float overloadWearPerHotelDay = 12;
        [Min(0)] public float pressureStressFactor = 30;
        [Header("Emergency patch and timed maintenance")]
        [Range(0, 100)] public float emergencyPatchCondition = 40;
        [Range(0, .99f)] public float emergencyPatchStress = .2f;
        [Min(1)] public float emergencyPatchStressMultiplier = 1.25f;
        [Range(0, 100)] public float properMaintenanceCondition = 95;
        [Min(.01f)] public float maintenanceHours = 2;
        [Header("One permanent capacity upgrade")]
        [Min(1.01f)] public float capacityUpgradeMultiplier = 1.25f;

        public BoilerSettings ToData() => new BoilerSettings(initialCondition, safeLoad, baseWearPerMinute,
            overloadWearPerMinute, pressureBase, pressureOverloadFactor, pressureConditionThreshold, pressureConditionFactor,
            pressureTimeConstant, failurePressure, failureExposureSeconds, warningPressure, heatOverloadLoss,
            heatConditionThreshold, heatConditionLoss, minimumHeatOutput, failedHeatOutput, startPressure,
            repairSafeMin, repairSafeMax, reliefTarget, reliefRate, failedPressureRise, latchTravelSeconds, restartPressure, maxPressure,
            new BoilerCapacitySettings(conditionCapacityFloor, strainedLoadRatio, criticalStress, stressGainPerHotelHour,
                stressRecoveryPerHotelHour, poorConditionStressPenalty, runningWearPerHotelDay, overloadWearPerHotelDay, pressureStressFactor,
                emergencyPatchCondition, emergencyPatchStress, emergencyPatchStressMultiplier, properMaintenanceCondition, maintenanceHours,
                capacityUpgradeMultiplier));
    }
}
