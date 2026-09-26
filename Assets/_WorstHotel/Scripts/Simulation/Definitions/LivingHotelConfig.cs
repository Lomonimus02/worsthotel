using UnityEngine;

namespace WorstHotel
{
    [CreateAssetMenu(menuName = "Worst Hotel/Living hotel configuration")]
    public sealed class LivingHotelConfig : ScriptableObject
    {
        public int seed = 1947;
        public float firstArrivalSeconds = 8;
        public float arrivalSpacingSeconds = 14;
        public float arrivalJitterSeconds = 4;
        [UnityEngine.Serialization.FormerlySerializedAs("checkInHoldSeconds")]
        [Tooltip("Only a pacing estimate for automated travel adapters. Giving a physical key is immediate.")]
        [Min(0.1f)] public float keyRetrievalEstimateSeconds = 5;
        public float firstActivityDelay = 8;
        public float activityDurationMin = 16;
        public float activityDurationMax = 28;
        public float quietDurationMin = 12;
        public float quietDurationMax = 22;
        public float quietDemandMultiplier = 0.85f;
        public float showerDemandMultiplier = 1.6f;
        public float loudDemandMultiplier = 1;
        [Range(0, 1)] public float quietNoiseOutput = 0.08f;
        [Range(0, 1)] public float showerNoiseOutput = 0.22f;
        [Range(0, 1)] public float loudNoiseOutput = 0.75f;
        [Range(0, 1)] public float sleepStartFraction = 0.80f;
        [Range(0, 1)] public float checkoutFraction = 0.94f;
        public float checkoutInteractionSeconds = 2;
        public float waitingPatienceMultiplier = 1;
        [Range(0, 1)] public float noisyLoudProbability = 0.75f;
        [Range(0, 1)] public float normalLoudProbability = 0.35f;
        [Min(1)] public float awayDurationMin = 28;
        [Min(1)] public float awayDurationMax = 48;
        [Min(1)] public float coldShowerDurationMultiplier = 1.35f;
        [Range(0, .2f)] public float businessSleepAdvanceFraction = .06f;
        [Range(0, 1)] public float phoneNoiseOutput = .38f;
        [Range(0, 1)] public float noisyPhoneNoiseOutput = .68f;
        [Range(0, 1)] public float quietTVNoiseOutput = .18f;

        public LivingHotelSettings ToData() => new LivingHotelSettings(seed, firstArrivalSeconds, arrivalSpacingSeconds,
            arrivalJitterSeconds, keyRetrievalEstimateSeconds, firstActivityDelay, activityDurationMin, activityDurationMax,
            quietDurationMin, quietDurationMax, quietDemandMultiplier, showerDemandMultiplier, loudDemandMultiplier,
            quietNoiseOutput, showerNoiseOutput, loudNoiseOutput, sleepStartFraction, checkoutFraction,
            checkoutInteractionSeconds, waitingPatienceMultiplier, noisyLoudProbability, normalLoudProbability,
            awayDurationMin, awayDurationMax, coldShowerDurationMultiplier, businessSleepAdvanceFraction,
            phoneNoiseOutput, noisyPhoneNoiseOutput, quietTVNoiseOutput);
    }
}
