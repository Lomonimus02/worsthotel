using System;

namespace WorstHotel
{
    /// <summary>Small services are opt-in. Eligibility is sampled once from booking identity, never by a chore timer.</summary>
    public sealed class GuestServiceSettings
    {
        public int MaxCasesPerShift { get; }
        public int MaxCasesPerGuest { get; }
        public int BlanketStock { get; }
        public int BulbStock { get; }
        public float Eligibility { get; }
        public float SoloFrequencyMultiplier { get; }
        public float ObservationSeconds { get; }
        public float MildColdMinimum { get; }
        public float MildColdMaximum { get; }
        public float BlanketComfortBonus { get; }
        public float ReplySeconds { get; }
        public float WakeLeadSeconds { get; }
        public float WakeToleranceSeconds { get; }
        public float WakeMissSeconds { get; }
        public float LateCheckoutExtension { get; }
        public float LateCheckoutRequestLead { get; }
        public float FulfilledBonus { get; }
        public float DeclinedPenalty { get; }
        public float BrokenPromisePenalty { get; }
        public float MaximumScoreAdjustment { get; }

        public GuestServiceSettings(int maxCasesPerShift = 3, int maxCasesPerGuest = 2, int blanketStock = 3,
            int bulbStock = 3, float eligibility = .55f, float soloFrequencyMultiplier = .8f,
            float observationSeconds = 7, float mildColdMinimum = .035f, float mildColdMaximum = .32f,
            float blanketComfortBonus = 2, float replySeconds = 70, float wakeLeadSeconds = 30,
            float wakeToleranceSeconds = 8, float wakeMissSeconds = 25, float lateCheckoutExtension = 30,
            float lateCheckoutRequestLead = 125, float fulfilledBonus = 1.5f, float declinedPenalty = .25f,
            float brokenPromisePenalty = 3, float maximumScoreAdjustment = 6)
        {
            foreach (float value in new[] { eligibility, soloFrequencyMultiplier, observationSeconds, mildColdMinimum,
                mildColdMaximum, blanketComfortBonus, replySeconds, wakeLeadSeconds, wakeToleranceSeconds,
                wakeMissSeconds, lateCheckoutExtension, lateCheckoutRequestLead, fulfilledBonus, declinedPenalty,
                brokenPromisePenalty, maximumScoreAdjustment })
                if (!Number.IsFinite(value) || value < 0) throw new ArgumentException("Service tuning must be finite and nonnegative.");
            if (maxCasesPerShift < 0 || maxCasesPerShift > 32 || maxCasesPerGuest < 1 || maxCasesPerGuest > 8 ||
                blanketStock < 0 || blanketStock > 6 || bulbStock < 0 || bulbStock > 6 || eligibility > 1 ||
                soloFrequencyMultiplier > 2 || observationSeconds <= 0 || mildColdMinimum >= mildColdMaximum ||
                mildColdMaximum > 1 || blanketComfortBonus > 8 || replySeconds <= 0 || wakeLeadSeconds <= wakeMissSeconds ||
                wakeToleranceSeconds <= 0 || wakeMissSeconds <= wakeToleranceSeconds || lateCheckoutExtension <= 0 ||
                lateCheckoutRequestLead <= lateCheckoutExtension || maximumScoreAdjustment > 15)
                throw new ArgumentException("Service budgets, time windows or comfort thresholds are invalid.");
            MaxCasesPerShift = maxCasesPerShift; MaxCasesPerGuest = maxCasesPerGuest; BlanketStock = blanketStock; BulbStock = bulbStock;
            Eligibility = eligibility; SoloFrequencyMultiplier = soloFrequencyMultiplier; ObservationSeconds = observationSeconds;
            MildColdMinimum = mildColdMinimum; MildColdMaximum = mildColdMaximum; BlanketComfortBonus = blanketComfortBonus;
            ReplySeconds = replySeconds; WakeLeadSeconds = wakeLeadSeconds; WakeToleranceSeconds = wakeToleranceSeconds;
            WakeMissSeconds = wakeMissSeconds; LateCheckoutExtension = lateCheckoutExtension; LateCheckoutRequestLead = lateCheckoutRequestLead;
            FulfilledBonus = fulfilledBonus; DeclinedPenalty = declinedPenalty; BrokenPromisePenalty = brokenPromisePenalty;
            MaximumScoreAdjustment = maximumScoreAdjustment;
        }
    }
}
