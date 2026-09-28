using UnityEngine;
namespace WorstHotel
{
    public sealed class HotelSubtitle : MonoBehaviour
    {
        static readonly string[] lines = new string[2];
        static readonly float[] until = new float[2];
        GUIStyle style;
        public static void Say(int actor, string speaker, string text)
        { if (actor < 0 || actor > 1) return; lines[actor] = speaker + "\n" + text; until[actor] = Time.unscaledTime + 6; }
        public static string Current(int actor) => actor >= 0 && actor < 2 && Time.unscaledTime < until[actor] ? lines[actor] : null;
        public static void Apply(int actor, string text)
        { lines[actor] = text; until[actor] = string.IsNullOrEmpty(text) ? 0 : Time.unscaledTime + .5f; }
        void OnGUI()
        {
            if (!HotelAccessibility.Subtitles) return;
            var coop = LocalCoopBootstrap.Instance;
            if (!coop || coop.IsPaused) return;
            style ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
            style.normal.textColor = Color.white;
            style.fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.height / 900f * 20, 16, 28));
            foreach (var player in coop.Players)
            {
                if (!player || !coop.IsLocalActor(player.ActorId) || player.IsUIBlocked) continue;
                string text = Current(player.ActorId); if (string.IsNullOrEmpty(text)) continue;
                var viewport = player.PlayerCamera.pixelRect;
                var rect = new Rect(viewport.center.x - Mathf.Min(320, viewport.width * .45f), Screen.height - viewport.y - 200, Mathf.Min(640, viewport.width * .9f), 78);
                GUI.color = new Color(.035f, .045f, .04f, .78f); GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = Color.white;
                GUI.Label(rect, text, style);
            }
        }
        void OnDisable() { until[0] = until[1] = 0; }
    }
}
