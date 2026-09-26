using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Linq;
#endif

namespace WorstHotel
{
    /// <summary>Opt-in developer measurements at real room doors. Never created or shown by a guest situation.</summary>
    public sealed class NoisePropagationDebug : MonoBehaviour
    {
        public static bool Visible { get; set; }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        DoorInteractable[] doors;
        GUIStyle style;
        void OnGUI()
        {
            var session = GameSession.Instance;
            var coop = LocalCoopBootstrap.Instance;
            if (!Visible || !session || session.Simulation?.Noise == null || !coop ||
                coop.LanRole != LanRole.Offline || GetComponent<DeveloperPanel>().IsVisible) return;
            var player = coop.Players[coop.LocalActorId];
            if (!player || !player.PlayerCamera) return;
            if (doors == null) doors = FindObjectsByType<DoorInteractable>(FindObjectsSortMode.None).Where(d => d.roomId > 0).ToArray();
            if (style == null) style = new GUIStyle(GUI.skin.box) { fontSize = 14, alignment = TextAnchor.MiddleLeft, wordWrap = true };
            var camera = player.PlayerCamera;
            foreach (var door in doors)
            {
                if (!door) continue;
                var point = camera.WorldToScreenPoint(door.transform.position + Vector3.up * 2.7f);
                if (point.z <= 0 || point.z > 28 || point.x < 0 || point.x > Screen.width || point.y < 0 || point.y > Screen.height) continue;
                var room = session.Rooms.FirstOrDefault(r => r.Profile.Id == door.roomId);
                if (room == null) continue;
                string sources = string.Join(", ", session.Simulation.Noise.Sources.Where(s => s.SourceRoomId == door.roomId)
                    .Select(s => s.Label + " " + (s.NoiseOutput * 100).ToString("F0")));
                GUI.Box(new Rect(point.x - 133, Screen.height - point.y, 266, 61), "DEV ROOM " + door.roomId +
                    "\nSOURCE: " + (sources.Length > 0 ? sources : "none") +
                    "\nRECEIVED: " + (room.ReceivedNoise * 100).ToString("F0"), style);
            }
        }
        void OnDestroy() => Visible = false;
#endif
    }
}
