using UnityEngine;

namespace WorstHotel
{
    [CreateAssetMenu(menuName = "Worst Hotel/Room")]
    public sealed class RoomDefinition : ScriptableObject
    {
        public int id = 101;
        public string label = "Warm and quiet";
        [Min(0)] public float heatLoss;
        [Range(0, 1)] public float noise = 0.1f;
        public Cleanliness cleanliness = Cleanliness.Clean;
        public RepairState repairState = RepairState.Working;
        public float temperature = 21;

        public RoomProfile ToData() => new RoomProfile(id, label, heatLoss, noise, cleanliness, repairState, temperature);
    }
}
