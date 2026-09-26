using UnityEngine;

namespace WorstHotel
{
    public sealed class RoomKeyRack : MonoBehaviour
    {
        public static RoomKeyRack Instance { get; private set; }
        public RoomKeyItem[] keys;
        public GuestPresentation Guests { get; private set; }
        void Awake() => Instance = this;
        void Start() => Guests = GameSession.Instance ? GameSession.Instance.GetComponent<GuestPresentation>() : null;
        public RoomKeyItem Find(int roomId)
        {
            if (keys != null) foreach (var key in keys) if (key && key.roomId == roomId) return key;
            return null;
        }
        void OnDestroy() { if (Instance == this) Instance = null; }
    }
}
