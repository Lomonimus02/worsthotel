using UnityEngine;

namespace WorstHotel
{
    [CreateAssetMenu(menuName = "Worst Hotel/Guest needs configuration")]
    public sealed class NeedConfig : ScriptableObject
    {
        [Min(0.01f)] public float temperatureSevereDelta = 4;
        [Range(0, 1)] public float tolerableSeverity = 0.25f;
        [Min(0.001f)] public float buildupPerSecond = 0.02f;
        [Min(0.001f)] public float recoveryPerSecond = 0.04f;
        [Range(0, 1)] public float dirtySeverity = 0.45f;
        [Range(0, 1)] public float degradedSeverity = 0.2f;
        [Range(0, 1)] public float brokenSeverity = 0.65f;
        [Range(0, 1)] public float serviceExpiredSeverity = 1;
        [Range(0, 1)] public float complaintDissatisfaction = 0.30f;
        [Range(0, 1)] public float escalatedDissatisfaction = 0.65f;
        [Range(0, 1)] public float criticalDissatisfaction = 0.90f;
        [Min(0.1f)] public float complaintExposureSeconds = 15;
        [Min(0.1f)] public float escalatedExposureSeconds = 35;
        [Min(0.1f)] public float criticalExposureSeconds = 65;
        [Min(0.1f)] public float recoverySeconds = 8;
        [Range(0, 1)] public float recoverySeverityThreshold = 0.03f;
        [Min(0)] public float reopenCooldownSeconds = 12;
        [Min(0.01f)] public float noiseBuildupMultiplier = 3;
        [Tooltip("Hotel seconds of renewed patience for currently accepted situations; unchanged causes can return afterward.")]
        [Min(0.1f)] public float compensationReliefSeconds = 25;
        [Tooltip("One immediate reduction of current dissatisfaction for the accepted situations; exposure and quality history remain.")]
        [Range(0, 1)] public float compensationDissatisfactionReduction = 0.35f;
        [Range(0, 1)] public float repeatPatienceReduction = 0.20f;
        [Range(0.1f, 1)] public float minimumRepeatPatienceMultiplier = 0.5f;
        [Range(1, 10000)] public int memoryCountLimit = 32;
        [Range(4, 128)] public int historyCapacity = 12;

        public NeedSettings ToData() => new NeedSettings(temperatureSevereDelta, tolerableSeverity, buildupPerSecond,
            recoveryPerSecond, dirtySeverity, degradedSeverity, brokenSeverity, serviceExpiredSeverity,
            complaintDissatisfaction, escalatedDissatisfaction, criticalDissatisfaction, complaintExposureSeconds,
            escalatedExposureSeconds, criticalExposureSeconds, recoverySeconds, recoverySeverityThreshold,
            reopenCooldownSeconds, noiseBuildupMultiplier, compensationReliefSeconds, compensationDissatisfactionReduction,
            repeatPatienceReduction, minimumRepeatPatienceMultiplier, memoryCountLimit, historyCapacity);
    }
}
