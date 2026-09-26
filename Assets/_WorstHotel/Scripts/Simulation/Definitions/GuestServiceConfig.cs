using UnityEngine;

namespace WorstHotel
{
    [CreateAssetMenu(menuName = "Worst Hotel/Guest service configuration")]
    public sealed class GuestServiceConfig : ScriptableObject
    {
        [Range(0, 32)] public int maxCasesPerShift = 3;
        [Range(1, 8)] public int maxCasesPerGuest = 2;
        [Range(0, 6)] public int blanketStock = 3;
        [Range(0, 6)] public int bulbStock = 3;
        [Range(0, 1)] public float eligibility = .55f;
        [Range(0, 2)] public float soloFrequencyMultiplier = .8f;
        [Min(.1f)] public float observationSeconds = 7;
        [Range(0, 1)] public float mildColdMinimum = .035f;
        [Range(0, 1)] public float mildColdMaximum = .32f;
        [Range(0, 8)] public float blanketComfortBonus = 2;
        [Min(1)] public float replySeconds = 70;
        [Min(1)] public float wakeLeadSeconds = 30;
        [Min(1)] public float wakeToleranceSeconds = 8;
        [Min(1)] public float wakeMissSeconds = 25;
        [Min(1)] public float lateCheckoutExtension = 30;
        [Min(1)] public float lateCheckoutRequestLead = 125;
        [Range(0, 5)] public float fulfilledBonus = 1.5f;
        [Range(0, 2)] public float declinedPenalty = .25f;
        [Range(0, 5)] public float brokenPromisePenalty = 3;
        [Range(0, 15)] public float maximumScoreAdjustment = 6;
        public GuestServiceSettings ToData() => new GuestServiceSettings(maxCasesPerShift, maxCasesPerGuest,
            blanketStock, bulbStock, eligibility, soloFrequencyMultiplier, observationSeconds, mildColdMinimum,
            mildColdMaximum, blanketComfortBonus, replySeconds, wakeLeadSeconds, wakeToleranceSeconds, wakeMissSeconds,
            lateCheckoutExtension, lateCheckoutRequestLead, fulfilledBonus, declinedPenalty, brokenPromisePenalty, maximumScoreAdjustment);
    }
}
