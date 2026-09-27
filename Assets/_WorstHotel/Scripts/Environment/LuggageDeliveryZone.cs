using UnityEngine;

namespace WorstHotel
{
    public sealed class LuggageDeliveryZone : MonoBehaviour
    {
        public int roomId;
        public Vector3 size = new Vector3(1.25f, 1.5f, 1.9f);
        public bool Contains(Vector3 position)
        {
            Vector3 local = transform.InverseTransformPoint(position);
            return Mathf.Abs(local.x) <= size.x * .5f && Mathf.Abs(local.z) <= size.z * .5f && local.y >= 0 && local.y <= size.y;
        }
    }
}
