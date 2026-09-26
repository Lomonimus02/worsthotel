using UnityEngine;

namespace WorstHotel
{
    [CreateAssetMenu(menuName = "Worst Hotel/Room noise configuration")]
    public sealed class NoiseConfig : ScriptableObject
    {
        [Range(0, 1)] public float sharedWallTransmission = 0.70f;
        [Range(0, 1)] public float corridorTransmission = 0.30f;
        [Min(0.1f)] public float quietRequestSeconds = 25;
        [Range(0, 1)] public float quietSourceMultiplier = 0.15f;
        [Range(0, 1)] public float repeatedWarningDurationReduction = .2f;
        [Range(.1f, 1)] public float minimumWarningDurationMultiplier = .4f;
        public NoiseSettings ToData() => new NoiseSettings(sharedWallTransmission, corridorTransmission, quietRequestSeconds,
            quietSourceMultiplier, repeatedWarningDurationReduction, minimumWarningDurationMultiplier);
    }
}
