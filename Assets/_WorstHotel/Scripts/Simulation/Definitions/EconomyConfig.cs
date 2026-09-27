using UnityEngine;

namespace WorstHotel
{
    [CreateAssetMenu(menuName = "Worst Hotel/Economy configuration")]
    public sealed class EconomyConfig : ScriptableObject
    {
        public int startingCash = 750;
        public int dailyOperatingCost = 450;
        public int minPrice = 120;
        public int maxPrice = 650;
        public int priceStep = 10;
        public int cheapPatchCost = 200;
        public float cheapPatchCondition = 15;
        public int properRepairCost = 1500;
        public int basicMaintenanceCost = 350;
        public int boilerUpgradeCost = 1800;
        public int electricalUpgradeCost = 1200;
        public int insulationUpgradeCost = 450;
        public int wingRestorationCost = 1600;
        public float properRepairCondition = 95;
        public float compensationRate = 0.2f;
        public float compensationGoodwill = 10;
        public float severeRefundThreshold = 35;
        public float severeRefundRate = 0.5f;
        public float partialRefundThreshold = 60;
        public float partialRefundRate = 0.25f;
        public float expectationSlope = 0.75f;
        public float minExpectation = 0.65f;
        public float maxExpectation = 1.75f;
        public float qualityPenaltyScale = 60;
        public float patiencePenalty = 20;
        public float initialReputation = 60;
        public float reputationTarget = 70;
        public float reputationChangeFactor = 0.15f;
        public float coldSeverityDegrees = 4;
        public float degradedSeverity = 0.15f;
        public float brokenSeverity = 0.5f;
        public float dirtySeverity = 0.3f;

        public EconomySettings ToData() => new EconomySettings(startingCash, dailyOperatingCost, minPrice, maxPrice,
            priceStep, cheapPatchCost, cheapPatchCondition, properRepairCost, properRepairCondition, compensationRate,
            compensationGoodwill, severeRefundThreshold, severeRefundRate, partialRefundThreshold, partialRefundRate,
            expectationSlope, minExpectation, maxExpectation, qualityPenaltyScale, patiencePenalty, initialReputation,
            reputationTarget, reputationChangeFactor, coldSeverityDegrees, degradedSeverity, brokenSeverity, dirtySeverity,
            boilerUpgradeCost, electricalUpgradeCost, basicMaintenanceCost, insulationUpgradeCost, wingRestorationCost);
    }
}
