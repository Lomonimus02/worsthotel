using UnityEngine;

namespace WorstHotel
{
    [CreateAssetMenu(menuName = "Worst Hotel/Guest archetype")]
    public sealed class GuestArchetypeDefinition : ScriptableObject
    {
        public GuestKind kind = GuestKind.Budget;
        public string label = "Budget traveler";
        [TextArea] public string description = "Price-conscious, easygoing, and willing to forgive an old hotel.";
        [Min(1)] public int referencePrice = 180;
        [Min(0.01f)] public float heatingDemand = 0.85f;
        public float coldThreshold = 18;
        [Min(0)] public float coldPenaltyWeight = 0.7f;
        [Min(0)] public float priceSensitivity = 28;
        [Min(1)] public float patience = 90;
        [Range(0, 1)] public float noiseTolerance = 0.55f;
        public bool overrideTraits;
        public GuestTraits traits;
        public bool overrideNeeds;
        public float preferredTemperatureMin = 20;
        public float preferredTemperatureMax = 23;
        public float toleranceTemperatureMin = 18;
        public float toleranceTemperatureMax = 26;
        [Range(0, 1)] public float preferredNoise = 0.275f;
        [Range(0, 1)] public float needNoiseTolerance = 0.55f;
        [Min(1)] public float needPatienceSeconds = 90;

        public GuestProfile ToData() => new GuestProfile(kind, label, description, referencePrice,
            heatingDemand, coldThreshold, coldPenaltyWeight, priceSensitivity, patience, noiseTolerance,
            overrideTraits ? traits : (GuestTraits?)null,
            overrideNeeds ? new NeedProfile(preferredTemperatureMin, preferredTemperatureMax, toleranceTemperatureMin,
                toleranceTemperatureMax, preferredNoise, needNoiseTolerance, needPatienceSeconds) : null);
    }
}
