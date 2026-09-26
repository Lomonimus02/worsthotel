using UnityEngine;

namespace WorstHotel
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class PhysicsPickup : MonoBehaviour
    {
        public string itemName = "Maintenance equipment";
        [Min(0.1f)] public float maxGrabMass = 30f;
        [Range(0.8f, 2.5f)] public float holdDistance = 1.7f;
        [Tooltip("Shared grab tuning. Unassigned older scenes use the validated default configuration.")]
        public GrabPhysicsConfig grabConfig;
        public GrabPhysicsSettings GrabSettings => GrabPhysicsConfig.Resolve(grabConfig);
    }
}
