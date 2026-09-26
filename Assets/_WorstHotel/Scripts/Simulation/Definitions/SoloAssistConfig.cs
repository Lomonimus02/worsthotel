using UnityEngine;

namespace WorstHotel
{
    [CreateAssetMenu(menuName = "Worst Hotel/Solo repair assistance")]
    public sealed class SoloAssistConfig : ScriptableObject
    {
        [Min(.1f)] public float safeValveHoldSeconds = 2;
        [Min(1)] public float valveLatchSeconds = 18;
        public SoloAssistSettings ToData() => new SoloAssistSettings(safeValveHoldSeconds, valveLatchSeconds);
    }
}
