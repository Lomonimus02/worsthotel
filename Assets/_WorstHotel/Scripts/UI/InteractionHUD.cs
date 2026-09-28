using UnityEngine;
namespace WorstHotel
{
    public sealed class InteractionHUD : MonoBehaviour
    {
        FirstPersonController owner;
        GUIStyle prompt;
        public void Initialize(FirstPersonController controller) => owner = controller;
        void OnGUI()
        {
            var coop = LocalCoopBootstrap.Instance;
            if (!owner || !owner.PlayerCamera || owner.IsUIBlocked || coop && (coop.IsPaused || !coop.IsLocalActor(owner.ActorId))) return;
            var area = owner.PlayerCamera.pixelRect;
            area.y = Screen.height - area.yMax;
            GUI.color = new Color(1, .95f, .8f, .8f);
            GUI.DrawTexture(new Rect(area.center.x - 1.5f, area.center.y - 1.5f, 3, 3), Texture2D.whiteTexture);
            GUI.color = Color.white;
            if (!HotelAccessibility.Prompts) return;
            var actor = owner.Interactor;
            string caption = actor.HasWorldAuthority ? InteractionWords.Caption(owner, HotelAccessibility.EnhancedLabels) :
                HotelAccessibility.EnhancedLabels ? actor.ReplicaDetailedCaption ?? actor.ReplicaCaption : actor.ReplicaCaption;
            if (string.IsNullOrEmpty(caption)) return;
            prompt ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
            prompt.fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.height / 900f * 18, 14, 26));
            var rect = new Rect(area.center.x - Mathf.Min(270, area.width * .45f), area.yMax - 96, Mathf.Min(540, area.width * .9f), 64);
            prompt.normal.textColor = new Color(0, 0, 0, .9f);
            GUI.Label(new Rect(rect.x + 1, rect.y + 2, rect.width, rect.height), caption, prompt);
            prompt.normal.textColor = new Color(1, .96f, .85f);
            GUI.Label(rect, caption, prompt);
        }
    }
}
