using System;

namespace WorstHotel
{
    /// <summary>Calendar-hour thresholds. Historical pure-model fixtures opt in explicitly;
    /// the production NeedConfig enables this policy for continuous hotel operations.</summary>
    public sealed class EarlyCheckoutSettings
    {
        public bool Enabled { get; }
        public float SevereThreshold { get; }
        public float RecoveryThreshold { get; }
        public float SevereHours { get; }
        public float GraceHours { get; }
        public float RecoveryHours { get; }
        public float MinimumRemainingStayHours { get; }
        public float ReferencePatienceSeconds { get; }
        public float PatientMultiplier { get; }
        public float ImpatientMultiplier { get; }
        public float MinimumPatienceMultiplier { get; }
        public float MaximumPatienceMultiplier { get; }

        public EarlyCheckoutSettings(bool enabled = false, float severeThreshold = .5f, float recoveryThreshold = .35f,
            float severeHours = 3, float graceHours = 1, float recoveryHours = .25f, float minimumRemainingStayHours = 1,
            float referencePatienceSeconds = 65, float patientMultiplier = 1.15f, float impatientMultiplier = .9f,
            float minimumPatienceMultiplier = .75f, float maximumPatienceMultiplier = 1.75f)
        {
            foreach (float value in new[] { severeThreshold, recoveryThreshold, severeHours, graceHours, recoveryHours,
                minimumRemainingStayHours, referencePatienceSeconds, patientMultiplier, impatientMultiplier,
                minimumPatienceMultiplier, maximumPatienceMultiplier })
                if (!Number.IsFinite(value) || value < 0) throw new ArgumentException("Early-checkout tuning must be finite and nonnegative.");
            if (severeThreshold <= 0 || severeThreshold >= 1 || recoveryThreshold >= severeThreshold ||
                severeHours <= 0 || graceHours <= 0 || recoveryHours <= 0 || referencePatienceSeconds <= 0 ||
                patientMultiplier <= 0 || impatientMultiplier <= 0 || minimumPatienceMultiplier <= 0 ||
                maximumPatienceMultiplier < minimumPatienceMultiplier)
                throw new ArgumentException("Early-checkout severity, duration and patience bounds are invalid.");
            Enabled = enabled; SevereThreshold = severeThreshold; RecoveryThreshold = recoveryThreshold;
            SevereHours = severeHours; GraceHours = graceHours; RecoveryHours = recoveryHours;
            MinimumRemainingStayHours = minimumRemainingStayHours; ReferencePatienceSeconds = referencePatienceSeconds;
            PatientMultiplier = patientMultiplier; ImpatientMultiplier = impatientMultiplier;
            MinimumPatienceMultiplier = minimumPatienceMultiplier; MaximumPatienceMultiplier = maximumPatienceMultiplier;
        }
    }
}
