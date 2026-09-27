using System;

namespace WorstHotel
{
    public sealed class GuestRhythmSettings
    {
        public bool Enabled { get; }
        public float SleepJitterHours { get; }
        public float BusinessSleepAdvanceHours { get; }
        public float WakeJitterHours { get; }
        public float BusinessWakeAdvanceHours { get; }
        public float OutingReturnHour { get; }
        public float OutingReturnJitterHours { get; }
        public float OutingTravelAllowanceSeconds { get; }
        public float BusinessOutingProbability { get; }
        public float BudgetOutingProbability { get; }
        public float ColdSensitiveOutingProbability { get; }

        public GuestRhythmSettings(bool enabled = false, float sleepJitterHours = .35f,
            float businessSleepAdvanceHours = .5f, float wakeJitterHours = .5f,
            float businessWakeAdvanceHours = 1f, float outingReturnHour = 19f,
            float outingReturnJitterHours = .35f, float outingTravelAllowanceSeconds = 12f,
            float businessOutingProbability = .25f, float budgetOutingProbability = .65f,
            float coldSensitiveOutingProbability = .40f)
        {
            foreach (float value in new[] { sleepJitterHours, businessSleepAdvanceHours, wakeJitterHours,
                businessWakeAdvanceHours, outingReturnHour, outingReturnJitterHours, outingTravelAllowanceSeconds,
                businessOutingProbability, budgetOutingProbability, coldSensitiveOutingProbability })
                if (!Number.IsFinite(value) || value < 0) throw new ArgumentException("Guest rhythm tuning must be finite and nonnegative.");
            if (outingReturnHour >= 24 || sleepJitterHours >= 12 || wakeJitterHours >= 12 ||
                businessSleepAdvanceHours >= 12 || businessWakeAdvanceHours >= 12 || outingReturnJitterHours >= 12 ||
                businessOutingProbability > 1 || budgetOutingProbability > 1 || coldSensitiveOutingProbability > 1)
                throw new ArgumentException("Guest rhythm hours or probabilities are invalid.");
            Enabled = enabled; SleepJitterHours = sleepJitterHours; BusinessSleepAdvanceHours = businessSleepAdvanceHours;
            WakeJitterHours = wakeJitterHours; BusinessWakeAdvanceHours = businessWakeAdvanceHours;
            OutingReturnHour = outingReturnHour; OutingReturnJitterHours = outingReturnJitterHours;
            OutingTravelAllowanceSeconds = outingTravelAllowanceSeconds;
            BusinessOutingProbability = businessOutingProbability; BudgetOutingProbability = budgetOutingProbability;
            ColdSensitiveOutingProbability = coldSensitiveOutingProbability;
        }
    }

    public readonly struct GuestDailyTiming
    {
        public float SleepAt { get; }
        public float WakeAt { get; }
        public float OutingReturnAt { get; }
        public GuestDailyTiming(float sleepAt, float wakeAt, float outingReturnAt)
        { SleepAt = sleepAt; WakeAt = wakeAt; OutingReturnAt = outingReturnAt; }
    }
}
