using UnityEngine;
namespace WorstHotel
{
    public sealed class InteractionHUD : MonoBehaviour
    {
        FirstPersonController owner;
        GUIStyle prompt, economy;
        public void Initialize(FirstPersonController controller) => owner = controller;
        bool CanDraw
        {
            get
            {
                var coop = LocalCoopBootstrap.Instance;
                var menu = ManagementUI.Instance;
                return owner && owner.PlayerCamera && owner.PlayerCamera.enabled && !owner.IsUIBlocked &&
                    (!coop || !coop.IsPaused && coop.IsLocalActor(owner.ActorId)) &&
                    (!menu || !menu.IsOpen || menu.Owner != owner.ActorId);
            }
        }
        public string EconomyCaption
        {
            get
            {
                var session = GameSession.Instance;
                var model = session ? session.Simulation : null;
                if (!CanDraw || !session || session.Phase != DayPhase.Service || model == null || !model.Running || model.OwnershipLost) return null;
                // Read current authoritative/mirrored cash, never subtract a reserved quota.
                string cash = "CASH  $" + model.Economy.Cash;
                return model.ContractEnabled ? "NEXT QUOTA  $" + model.ContractDue + "\n" + cash +
                    "\nDue " + GuestLabels.HotelMoment(model, model.NextContractAt) : cash;
            }
        }
        void OnGUI()
        {
            if (!CanDraw) return;
            var area = owner.PlayerCamera.pixelRect;
            area.y = Screen.height - area.yMax;
            DrawEconomy(area);
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

        void DrawEconomy(Rect area)
        {
            string caption = EconomyCaption;
            if (string.IsNullOrEmpty(caption)) return;
            // Small cream lettering, like the interaction captions: no opaque HUD panel.
            economy ??= new GUIStyle(GUI.skin.label)
            { alignment = TextAnchor.UpperRight, wordWrap = false, richText = false };
            float scale = Mathf.Clamp(Screen.height / 900f, .8f, 1.5f);
            economy.fontSize = Mathf.RoundToInt(20 * scale);
            float margin = 22 * scale;
            float width = Mathf.Min(360 * scale, area.width - margin * 2);
            var rect = new Rect(area.xMax - margin - width, area.y + margin, width,
                economy.CalcHeight(new GUIContent(caption), width));
            economy.normal.textColor = new Color(0, 0, 0, .9f);
            GUI.Label(new Rect(rect.x + 1, rect.y + 2, rect.width, rect.height), caption, economy);
            economy.normal.textColor = HotelTheme.LightPaper;
            GUI.Label(rect, caption, economy);
        }
    }
}
