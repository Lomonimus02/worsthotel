using UnityEngine;

namespace WorstHotel
{
    [CreateAssetMenu(menuName = "Worst Hotel/Room infrastructure")]
    public sealed class RoomInfrastructureConfig : ScriptableObject
    {
        [Range(0, 1)] public float radiatorHeatStep = .12f;
        [Min(0)] public float radiatorDemandStep = .25f;
        [Min(0)] public float lampWearPerSecond = .035f;
        [Min(0)] public float vacantRadiatorDemand = .08f;
        [Min(0)] public float heatLossDemandFactor = .04f;
        public RoomInfrastructureSettings ToData() => new RoomInfrastructureSettings(radiatorHeatStep, radiatorDemandStep,
            lampWearPerSecond, vacantRadiatorDemand, heatLossDemandFactor);
    }
}
