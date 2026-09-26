using System;

namespace WorstHotel
{
    /// <summary>Immutable tuning snapshot; no guest runtime state belongs in shared assets.</summary>
    public sealed class LivingHotelSettings
    {
        public int Seed { get; }
        public float FirstArrivalSeconds { get; }
        public float ArrivalSpacingSeconds { get; }
        public float ArrivalJitterSeconds { get; }
        public float KeyRetrievalEstimateSeconds { get; }
        public float FirstActivityDelay { get; }
        public float ActivityDurationMin { get; }
        public float ActivityDurationMax { get; }
        public float QuietDurationMin { get; }
        public float QuietDurationMax { get; }
        public float QuietDemandMultiplier { get; }
        public float ShowerDemandMultiplier { get; }
        public float LoudDemandMultiplier { get; }
        public float QuietNoiseOutput { get; }
        public float ShowerNoiseOutput { get; }
        public float LoudNoiseOutput { get; }
        public float SleepStartFraction { get; }
        public float CheckoutFraction { get; }
        public float CheckoutInteractionSeconds { get; }
        public float WaitingPatienceMultiplier { get; }
        public float NoisyLoudProbability { get; }
        public float NormalLoudProbability { get; }
        public float AwayDurationMin { get; }
        public float AwayDurationMax { get; }
        public float ColdShowerDurationMultiplier { get; }
        public float BusinessSleepAdvanceFraction { get; }
        public float PhoneNoiseOutput { get; }
        public float NoisyPhoneNoiseOutput { get; }
        public float QuietTVNoiseOutput { get; }

        public LivingHotelSettings(int seed = 1947, float firstArrivalSeconds = 8, float arrivalSpacingSeconds = 14,
            float arrivalJitterSeconds = 4, float keyRetrievalEstimateSeconds = 5, float firstActivityDelay = 8,
            float activityDurationMin = 16, float activityDurationMax = 28, float quietDurationMin = 12,
            float quietDurationMax = 22, float quietDemandMultiplier = 0.85f, float showerDemandMultiplier = 1.6f,
            float loudDemandMultiplier = 1, float quietNoiseOutput = 0.08f, float showerNoiseOutput = 0.22f,
            float loudNoiseOutput = 0.75f, float sleepStartFraction = 0.80f, float checkoutFraction = 0.94f,
            float checkoutInteractionSeconds = 2, float waitingPatienceMultiplier = 1,
            float noisyLoudProbability = 0.75f, float normalLoudProbability = 0.35f,
            float awayDurationMin = 28, float awayDurationMax = 48, float coldShowerDurationMultiplier = 1.35f,
            float businessSleepAdvanceFraction = .06f, float phoneNoiseOutput = .38f,
            float noisyPhoneNoiseOutput = .68f, float quietTVNoiseOutput = .18f)
        {
            var values = new[] { firstArrivalSeconds, arrivalSpacingSeconds, arrivalJitterSeconds, keyRetrievalEstimateSeconds,
                firstActivityDelay, activityDurationMin, activityDurationMax, quietDurationMin, quietDurationMax,
                quietDemandMultiplier, showerDemandMultiplier, loudDemandMultiplier, quietNoiseOutput,
                showerNoiseOutput, loudNoiseOutput, sleepStartFraction, checkoutFraction,
                checkoutInteractionSeconds, waitingPatienceMultiplier, noisyLoudProbability, normalLoudProbability,
                awayDurationMin, awayDurationMax, coldShowerDurationMultiplier, businessSleepAdvanceFraction,
                phoneNoiseOutput, noisyPhoneNoiseOutput, quietTVNoiseOutput };
            foreach (float value in values)
                if (!Number.IsFinite(value) || value < 0) throw new ArgumentException("Living hotel tuning must be finite and nonnegative.");
            if (arrivalSpacingSeconds <= 0 || arrivalJitterSeconds >= arrivalSpacingSeconds || keyRetrievalEstimateSeconds <= 0 ||
                activityDurationMin <= 0 || activityDurationMax < activityDurationMin || quietDurationMin <= 0 ||
                quietDurationMax < quietDurationMin || quietNoiseOutput > 1 || showerNoiseOutput > 1 || loudNoiseOutput > 1 ||
                sleepStartFraction <= 0 || sleepStartFraction >= checkoutFraction || checkoutFraction >= 1 ||
                checkoutInteractionSeconds <= 0 || waitingPatienceMultiplier <= 0 || noisyLoudProbability > 1 || normalLoudProbability > 1 ||
                awayDurationMin <= 0 || awayDurationMax < awayDurationMin || coldShowerDurationMultiplier < 1 ||
                businessSleepAdvanceFraction >= sleepStartFraction || phoneNoiseOutput > 1 || noisyPhoneNoiseOutput > 1 || quietTVNoiseOutput > 1)
                throw new ArgumentException("Living hotel duration, staggering, noise or day fractions are invalid.");
            Seed = seed; FirstArrivalSeconds = firstArrivalSeconds; ArrivalSpacingSeconds = arrivalSpacingSeconds;
            ArrivalJitterSeconds = arrivalJitterSeconds; KeyRetrievalEstimateSeconds = keyRetrievalEstimateSeconds;
            FirstActivityDelay = firstActivityDelay; ActivityDurationMin = activityDurationMin; ActivityDurationMax = activityDurationMax;
            QuietDurationMin = quietDurationMin; QuietDurationMax = quietDurationMax; QuietDemandMultiplier = quietDemandMultiplier;
            ShowerDemandMultiplier = showerDemandMultiplier; LoudDemandMultiplier = loudDemandMultiplier;
            QuietNoiseOutput = quietNoiseOutput; ShowerNoiseOutput = showerNoiseOutput; LoudNoiseOutput = loudNoiseOutput;
            SleepStartFraction = sleepStartFraction; CheckoutFraction = checkoutFraction;
            CheckoutInteractionSeconds = checkoutInteractionSeconds; WaitingPatienceMultiplier = waitingPatienceMultiplier;
            NoisyLoudProbability = noisyLoudProbability; NormalLoudProbability = normalLoudProbability;
            AwayDurationMin = awayDurationMin; AwayDurationMax = awayDurationMax;
            ColdShowerDurationMultiplier = coldShowerDurationMultiplier; BusinessSleepAdvanceFraction = businessSleepAdvanceFraction;
            PhoneNoiseOutput = phoneNoiseOutput; NoisyPhoneNoiseOutput = noisyPhoneNoiseOutput; QuietTVNoiseOutput = quietTVNoiseOutput;
        }
    }
}
