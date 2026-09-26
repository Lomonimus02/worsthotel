using UnityEngine;

namespace WorstHotel
{
    [CreateAssetMenu(menuName = "Worst Hotel/Electricity configuration")]
    public sealed class ElectricityConfig : ScriptableObject
    {
        [Min(0.1f)] public float circuitCapacity = 4;
        [Min(0)] public float occupiedRoomLoad = 0.85f;
        [Min(0)] public float loudActivityLoad = 0.25f;
        [Min(0.1f)] public float warningSeconds = 6;
        [Min(0.1f)] public float tripSeconds = 18;
        [Range(0, 1)] public float powerLossConditionSeverity = 0.65f;
        public ElectricitySettings ToData() => new ElectricitySettings(circuitCapacity, occupiedRoomLoad, loudActivityLoad,
            warningSeconds, tripSeconds, powerLossConditionSeverity);
    }
}
