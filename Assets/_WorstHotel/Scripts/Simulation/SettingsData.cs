using System;
using System.Collections.Generic;
using System.Linq;

namespace WorstHotel
{
    public sealed class BoilerSettings
    {
        public float InitialCondition { get; }
        public float SafeLoad { get; }
        public float BaseWearPerMinute { get; }
        public float OverloadWearPerMinute { get; }
        public float PressureBase { get; }
        public float PressureOverloadFactor { get; }
        public float PressureConditionThreshold { get; }
        public float PressureConditionFactor { get; }
        public float PressureTimeConstant { get; }
        public float FailurePressure { get; }
        public float FailureExposureSeconds { get; }
        public float WarningPressure { get; }
        public float HeatOverloadLoss { get; }
        public float HeatConditionThreshold { get; }
        public float HeatConditionLoss { get; }
        public float MinimumHeatOutput { get; }
        public float FailedHeatOutput { get; }
        public float StartPressure { get; }
        public float RepairSafeMin { get; }
        public float RepairSafeMax { get; }
        public float ReliefTarget { get; }
        public float ReliefRate { get; }
        public float FailedPressureRise { get; }
        public float LatchTravelSeconds { get; }
        public float RestartPressure { get; }
        public float MaxPressure { get; }
        public BoilerCapacitySettings Capacity { get; }

        public BoilerSettings(float initialCondition = 85, float safeLoad = 4.2f, float baseWearPerMinute = 4,
            float overloadWearPerMinute = 16, float pressureBase = 40, float pressureOverloadFactor = 70,
            float pressureConditionThreshold = 65, float pressureConditionFactor = 0.85f, float pressureTimeConstant = 12,
            float failurePressure = 85, float failureExposureSeconds = 8, float warningPressure = 70,
            float heatOverloadLoss = 0.45f, float heatConditionThreshold = 70, float heatConditionLoss = 0.004f,
            float minimumHeatOutput = 0.25f, float failedHeatOutput = 0.1f, float startPressure = 20,
            float repairSafeMin = 35, float repairSafeMax = 55, float reliefTarget = 43, float reliefRate = 4,
            float failedPressureRise = 4, float latchTravelSeconds = 2.5f, float restartPressure = 40, float maxPressure = 120,
            BoilerCapacitySettings capacity = null)
        {
            var values = new[] { initialCondition, safeLoad, baseWearPerMinute, overloadWearPerMinute, pressureBase,
                pressureOverloadFactor, pressureConditionThreshold, pressureConditionFactor, pressureTimeConstant,
                failurePressure, failureExposureSeconds, warningPressure, heatOverloadLoss, heatConditionThreshold,
                heatConditionLoss, minimumHeatOutput, failedHeatOutput, startPressure, repairSafeMin, repairSafeMax,
                reliefTarget, reliefRate, failedPressureRise, latchTravelSeconds, restartPressure, maxPressure };
            if (values.Any(value => !Number.IsFinite(value) || value < 0) || initialCondition > 100 || safeLoad <= 0 ||
                pressureTimeConstant <= 0 || failureExposureSeconds <= 0 || minimumHeatOutput > 1 || failedHeatOutput > 1 ||
                repairSafeMin >= repairSafeMax || reliefTarget < repairSafeMin || reliefTarget > repairSafeMax || latchTravelSeconds <= 0 || maxPressure <= failurePressure)
                throw new ArgumentException("Boiler settings contain invalid values.");
            InitialCondition = initialCondition; SafeLoad = safeLoad; BaseWearPerMinute = baseWearPerMinute;
            OverloadWearPerMinute = overloadWearPerMinute; PressureBase = pressureBase; PressureOverloadFactor = pressureOverloadFactor;
            PressureConditionThreshold = pressureConditionThreshold; PressureConditionFactor = pressureConditionFactor;
            PressureTimeConstant = pressureTimeConstant; FailurePressure = failurePressure; FailureExposureSeconds = failureExposureSeconds;
            WarningPressure = warningPressure; HeatOverloadLoss = heatOverloadLoss; HeatConditionThreshold = heatConditionThreshold;
            HeatConditionLoss = heatConditionLoss; MinimumHeatOutput = minimumHeatOutput; FailedHeatOutput = failedHeatOutput;
            StartPressure = startPressure; RepairSafeMin = repairSafeMin; RepairSafeMax = repairSafeMax;
            ReliefTarget = reliefTarget; ReliefRate = reliefRate; FailedPressureRise = failedPressureRise;
            LatchTravelSeconds = latchTravelSeconds; RestartPressure = restartPressure;
            MaxPressure = maxPressure;
            Capacity = capacity ?? new BoilerCapacitySettings();
        }
    }

    public sealed class EconomySettings
    {
        public int StartingCash { get; }
        public int DailyOperatingCost { get; }
        public int MinPrice { get; }
        public int MaxPrice { get; }
        public int PriceStep { get; }
        public int CheapPatchCost { get; }
        public float CheapPatchCondition { get; }
        public int ProperRepairCost { get; }
        public int BasicMaintenanceCost { get; }
        public int BoilerUpgradeCost { get; }
        public int ElectricalUpgradeCost { get; }
        public int InsulationUpgradeCost { get; }
        public int WingRestorationCost { get; }
        public float ProperRepairCondition { get; }
        public float CompensationRate { get; }
        public float CompensationGoodwill { get; }
        public float SevereRefundThreshold { get; }
        public float SevereRefundRate { get; }
        public float PartialRefundThreshold { get; }
        public float PartialRefundRate { get; }
        public float ExpectationSlope { get; }
        public float MinExpectation { get; }
        public float MaxExpectation { get; }
        public float QualityPenaltyScale { get; }
        public float PatiencePenalty { get; }
        public float InitialReputation { get; }
        public float ReputationTarget { get; }
        public float ReputationChangeFactor { get; }
        public float ColdSeverityDegrees { get; }
        public float DegradedSeverity { get; }
        public float BrokenSeverity { get; }
        public float DirtySeverity { get; }

        public EconomySettings(int startingCash = 350, int dailyOperatingCost = 450, int minPrice = 120, int maxPrice = 650,
            int priceStep = 10, int cheapPatchCost = 200, float cheapPatchCondition = 15, int properRepairCost = 1500,
            float properRepairCondition = 95, float compensationRate = 0.2f, float compensationGoodwill = 10,
            float severeRefundThreshold = 35, float severeRefundRate = 0.5f, float partialRefundThreshold = 60,
            float partialRefundRate = 0.25f, float expectationSlope = 0.75f, float minExpectation = 0.65f,
            float maxExpectation = 1.75f, float qualityPenaltyScale = 60, float patiencePenalty = 20,
            float initialReputation = 60, float reputationTarget = 70, float reputationChangeFactor = 0.15f,
            float coldSeverityDegrees = 4, float degradedSeverity = 0.15f, float brokenSeverity = 0.5f, float dirtySeverity = 0.3f,
            int boilerUpgradeCost = 1800, int electricalUpgradeCost = 1200, int basicMaintenanceCost = 350,
            int insulationUpgradeCost = 450, int wingRestorationCost = 1600)
        {
            var values = new[] { cheapPatchCondition, properRepairCondition, compensationRate, compensationGoodwill,
                severeRefundThreshold, severeRefundRate, partialRefundThreshold, partialRefundRate, expectationSlope,
                minExpectation, maxExpectation, qualityPenaltyScale, patiencePenalty, initialReputation, reputationTarget,
                reputationChangeFactor, coldSeverityDegrees, degradedSeverity, brokenSeverity, dirtySeverity };
            if (values.Any(value => !Number.IsFinite(value) || value < 0) || startingCash < 0 || dailyOperatingCost < 0 || insulationUpgradeCost < 0 || wingRestorationCost < 0 ||
                minPrice <= 0 || maxPrice < minPrice || priceStep <= 0 || cheapPatchCost < 0 || properRepairCost < 0 || boilerUpgradeCost < 0 || electricalUpgradeCost < 0 || basicMaintenanceCost < 0 ||
                compensationRate > 1 || severeRefundRate > 1 || partialRefundRate > 1 || severeRefundThreshold >= partialRefundThreshold ||
                coldSeverityDegrees <= 0 || maxExpectation < minExpectation || properRepairCondition > 100 || initialReputation > 100)
                throw new ArgumentException("Economy settings contain invalid values.");
            StartingCash = startingCash; DailyOperatingCost = dailyOperatingCost; MinPrice = minPrice; MaxPrice = maxPrice;
            PriceStep = priceStep; CheapPatchCost = cheapPatchCost; CheapPatchCondition = cheapPatchCondition;
            ProperRepairCost = properRepairCost; ProperRepairCondition = properRepairCondition;
            BasicMaintenanceCost = basicMaintenanceCost;
            BoilerUpgradeCost = boilerUpgradeCost; ElectricalUpgradeCost = electricalUpgradeCost;
            InsulationUpgradeCost = insulationUpgradeCost; WingRestorationCost = wingRestorationCost;
            CompensationRate = compensationRate; CompensationGoodwill = compensationGoodwill; SevereRefundThreshold = severeRefundThreshold;
            SevereRefundRate = severeRefundRate; PartialRefundThreshold = partialRefundThreshold; PartialRefundRate = partialRefundRate;
            ExpectationSlope = expectationSlope; MinExpectation = minExpectation; MaxExpectation = maxExpectation;
            QualityPenaltyScale = qualityPenaltyScale; PatiencePenalty = patiencePenalty; InitialReputation = initialReputation;
            ReputationTarget = reputationTarget; ReputationChangeFactor = reputationChangeFactor; ColdSeverityDegrees = coldSeverityDegrees;
            DegradedSeverity = degradedSeverity; BrokenSeverity = brokenSeverity; DirtySeverity = dirtySeverity;
        }
    }

    public sealed class SessionSettings
    {
        public IReadOnlyList<GuestProfile> GuestArchetypes { get; }
        public IReadOnlyList<RoomProfile> Rooms { get; }
        public BoilerSettings Boiler { get; }
        public EconomySettings Economy { get; }
        public float TickRate { get; }
        public float ServiceSeconds { get; }
        public int TotalDays { get; }
        public int Day3BusinessReferencePrice { get; }
        public float TemperatureBase { get; }
        public float HeatTemperatureGain { get; }
        public float TemperatureTimeConstant { get; }
        public float IncidentDelaySeconds { get; }
        public float ResolutionDelaySeconds { get; }
        public float ColdResolutionHysteresis { get; }
        public float OvernightSeconds { get; }

        public SessionSettings(IEnumerable<GuestProfile> guestArchetypes, IEnumerable<RoomProfile> rooms,
            BoilerSettings boiler, EconomySettings economy, float tickRate = 5, float serviceSeconds = 300,
            int totalDays = 3, int day3BusinessReferencePrice = 525, float temperatureBase = 9, float heatTemperatureGain = 13,
            float temperatureTimeConstant = 45, float incidentDelaySeconds = 15, float resolutionDelaySeconds = 10,
            float coldResolutionHysteresis = 0.5f, float overnightSeconds = 180)
        {
            if (guestArchetypes == null || rooms == null) throw new ArgumentNullException("Session definitions are required.");
            var values = new[] { tickRate, serviceSeconds, temperatureBase, heatTemperatureGain, temperatureTimeConstant,
                incidentDelaySeconds, resolutionDelaySeconds, coldResolutionHysteresis, overnightSeconds };
            if (values.Any(value => !Number.IsFinite(value)) || tickRate <= 0 || serviceSeconds <= 0 || totalDays != 3 ||
                day3BusinessReferencePrice <= 0 || temperatureTimeConstant <= 0 || incidentDelaySeconds < 0 || resolutionDelaySeconds < 0 || coldResolutionHysteresis < 0 || overnightSeconds < 0)
                throw new ArgumentException("Invalid session settings. The prototype contains three days.");
            var guestArray = guestArchetypes.ToArray();
            var roomArray = rooms.ToArray();
            if (guestArray.Length != 3 || guestArray.Select(guest => guest.Kind).Distinct().Count() != 3 ||
                roomArray.Length < 1 || roomArray.Length > 10 || roomArray.Select(room => room.Id).Distinct().Count() != roomArray.Length)
                throw new ArgumentException("The prototype requires three different archetypes and up to ten uniquely numbered rooms.");
            GuestArchetypes = Array.AsReadOnly(guestArray); Rooms = Array.AsReadOnly(roomArray);
            Boiler = boiler ?? throw new ArgumentNullException(nameof(boiler));
            Economy = economy ?? throw new ArgumentNullException(nameof(economy));
            TickRate = tickRate; ServiceSeconds = serviceSeconds; TotalDays = totalDays;
            Day3BusinessReferencePrice = day3BusinessReferencePrice; TemperatureBase = temperatureBase;
            HeatTemperatureGain = heatTemperatureGain; TemperatureTimeConstant = temperatureTimeConstant;
            IncidentDelaySeconds = incidentDelaySeconds; ResolutionDelaySeconds = resolutionDelaySeconds;
            ColdResolutionHysteresis = coldResolutionHysteresis;
            OvernightSeconds = overnightSeconds;
        }
    }
}
