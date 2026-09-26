using System;
using UnityEngine;

namespace WorstHotel
{
    /// <summary>Authored interior volumes. A portable tool belongs to a room only when its entire collider fits.</summary>
    public sealed class RoomVolumeRegistry : MonoBehaviour
    {
        [Serializable]
        public struct RoomVolume
        {
            public int roomId;
            public Bounds bounds;
            public RoomVolume(int id, Bounds volume) { roomId = id; bounds = volume; }
        }

        public RoomVolume[] volumes = Array.Empty<RoomVolume>();

        public int? ResolveRoom(Bounds toolBounds)
        {
            if (!Finite(toolBounds.center) || !Finite(toolBounds.size) || toolBounds.size.x <= 0 ||
                toolBounds.size.y <= 0 || toolBounds.size.z <= 0 || volumes == null) return null;
            int? result = null;
            foreach (var volume in volumes)
            {
                if (volume.roomId <= 0 || !volume.bounds.Contains(toolBounds.min) || !volume.bounds.Contains(toolBounds.max)) continue;
                if (!result.HasValue || volume.roomId < result.Value) result = volume.roomId;
            }
            return result;
        }

        static bool Finite(Vector3 value) => Number.IsFinite(value.x) && Number.IsFinite(value.y) && Number.IsFinite(value.z);

        void OnDrawGizmosSelected()
        {
            if (volumes == null) return;
            Gizmos.color = new Color(.25f, .8f, .7f, .4f);
            foreach (var volume in volumes) Gizmos.DrawWireCube(volume.bounds.center, volume.bounds.size);
        }
    }
}
