using System;

namespace WorstHotel
{
    /// <summary>Shared immutable rates and episode thresholds; every running guest keeps its own needs.</summary>
    public sealed class NeedSettings
    {
        public float TemperatureSevereDelta { get; }
        public float TolerableSeverity { get; }
        public float BuildupPerSecond { get; }
        public float RecoveryPerSecond { get; }
        public float DirtySeverity { get; }
        public float DegradedSeverity { get; }
        public float BrokenSeverity { get; }
        public float ServiceExpiredSeverity { get; }
        public float ComplaintDissatisfaction { get; }
        public float EscalatedDissatisfaction { get; }
        public float CriticalDissatisfaction { get; }
        public float ComplaintExposureSeconds { get; }
        public float EscalatedExposureSeconds { get; }
        public float CriticalExposureSeconds { get; }
        public float RecoverySeconds { get; }
        public float RecoverySeverityThreshold { get; }
        public float ReopenCooldownSeconds { get; }
        public float NoiseBuildupMultiplier { get; }
        public float CompensationReliefSeconds { get; }
        public float CompensationDissatisfactionReduction { get; }
        public float RepeatPatienceReduction { get; }
        public float MinimumRepeatPatienceMultiplier { get; }
        public int MemoryCountLimit { get; }
        public int HistoryCapacity { get; }
        public EarlyCheckoutSettings EarlyCheckout { get; }

        public NeedSettings(float temperatureSevereDelta = 4, float tolerableSeverity = 0.25f,
            float buildupPerSecond = 0.02f, float recoveryPerSecond = 0.04f, float dirtySeverity = 0.45f,
            float degradedSeverity = 0.2f, float brokenSeverity = 0.65f, float serviceExpiredSeverity = 1,
            float complaintDissatisfaction = 0.30f, float escalatedDissatisfaction = 0.65f,
            float criticalDissatisfaction = 0.90f, float complaintExposureSeconds = 15,
            float escalatedExposureSeconds = 35, float criticalExposureSeconds = 65, float recoverySeconds = 8,
            float recoverySeverityThreshold = 0.03f, float reopenCooldownSeconds = 12, float noiseBuildupMultiplier = 3,
            float compensationReliefSeconds = 25, float compensationDissatisfactionReduction = 0.35f,
            float repeatPatienceReduction = 0.20f, float minimumRepeatPatienceMultiplier = 0.5f,
            int memoryCountLimit = 32, int historyCapacity = 12, EarlyCheckoutSettings earlyCheckout = null)
        {
            foreach (float value in new[] { temperatureSevereDelta, tolerableSeverity, buildupPerSecond, recoveryPerSecond,
                dirtySeverity, degradedSeverity, brokenSeverity, serviceExpiredSeverity, complaintDissatisfaction,
                escalatedDissatisfaction, criticalDissatisfaction, complaintExposureSeconds, escalatedExposureSeconds,
                criticalExposureSeconds, recoverySeconds, recoverySeverityThreshold, reopenCooldownSeconds, noiseBuildupMultiplier,
                compensationReliefSeconds, compensationDissatisfactionReduction, repeatPatienceReduction, minimumRepeatPatienceMultiplier })
                if (!Number.IsFinite(value) || value < 0) throw new ArgumentException("Need tuning must be finite and nonnegative.");
            if (temperatureSevereDelta <= 0 || buildupPerSecond <= 0 || recoveryPerSecond <= 0 ||
                tolerableSeverity > 1 || dirtySeverity > 1 || degradedSeverity > 1 || brokenSeverity > 1 ||
                degradedSeverity > brokenSeverity || serviceExpiredSeverity > 1 || complaintDissatisfaction <= 0 ||
                complaintDissatisfaction > escalatedDissatisfaction || escalatedDissatisfaction > criticalDissatisfaction ||
                criticalDissatisfaction > 1 || complaintExposureSeconds <= 0 || complaintExposureSeconds > escalatedExposureSeconds ||
                escalatedExposureSeconds > criticalExposureSeconds || recoverySeconds <= 0 ||
                recoverySeverityThreshold > 1 || noiseBuildupMultiplier <= 0 || compensationReliefSeconds <= 0 ||
                compensationDissatisfactionReduction > 1 || repeatPatienceReduction > 1 ||
                minimumRepeatPatienceMultiplier <= 0 || minimumRepeatPatienceMultiplier > 1 || memoryCountLimit < 1 || memoryCountLimit > 10000 || historyCapacity < 4 || historyCapacity > 128)
                throw new ArgumentException("Need rates, normalized severities or ordered situation thresholds are invalid.");
            TemperatureSevereDelta = temperatureSevereDelta; TolerableSeverity = tolerableSeverity;
            BuildupPerSecond = buildupPerSecond; RecoveryPerSecond = recoveryPerSecond; DirtySeverity = dirtySeverity;
            DegradedSeverity = degradedSeverity; BrokenSeverity = brokenSeverity; ServiceExpiredSeverity = serviceExpiredSeverity;
            ComplaintDissatisfaction = complaintDissatisfaction; EscalatedDissatisfaction = escalatedDissatisfaction;
            CriticalDissatisfaction = criticalDissatisfaction; ComplaintExposureSeconds = complaintExposureSeconds;
            EscalatedExposureSeconds = escalatedExposureSeconds; CriticalExposureSeconds = criticalExposureSeconds;
            RecoverySeconds = recoverySeconds; RecoverySeverityThreshold = recoverySeverityThreshold;
            ReopenCooldownSeconds = reopenCooldownSeconds;
            NoiseBuildupMultiplier = noiseBuildupMultiplier;
            CompensationReliefSeconds = compensationReliefSeconds;
            CompensationDissatisfactionReduction = compensationDissatisfactionReduction;
            RepeatPatienceReduction = repeatPatienceReduction; MinimumRepeatPatienceMultiplier = minimumRepeatPatienceMultiplier;
            MemoryCountLimit = memoryCountLimit; HistoryCapacity = historyCapacity;
            EarlyCheckout = earlyCheckout ?? new EarlyCheckoutSettings();
        }
    }
}
