using UnityEngine;

namespace WorstHotel
{
    [CreateAssetMenu(menuName = "Worst Hotel/Housekeeping configuration")]
    public sealed class HousekeepingConfig : ScriptableObject
    {
        [Tooltip("Real seconds of uninterrupted player interaction after bringing clean linen to the bed.")]
        [Min(0.1f)] public float makeBedSeconds = 1.5f;
        [Tooltip("Physical shelf slots. Consumed slots are replenished once before each new planning day.")]
        [Range(1, 6)] public int cleanLinenPerDay = 6;
        public HousekeepingSettings ToData() => new HousekeepingSettings(makeBedSeconds, cleanLinenPerDay);
    }
}
