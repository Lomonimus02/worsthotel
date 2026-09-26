using UnityEngine;

namespace WorstHotel
{
    [CreateAssetMenu(menuName = "Worst Hotel/Portable heater configuration")]
    public sealed class HeaterConfig : ScriptableObject
    {
        [Tooltip("Supplement added to this room's equilibrium temperature while the placed heater has power.")]
        [Min(0.1f)] public float heatOutput = 14;
        [Min(0.1f)] public float electricalLoad = 2;
        public HeaterSettings ToData() => new HeaterSettings(heatOutput, electricalLoad);
    }
}
