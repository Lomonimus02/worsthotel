using UnityEngine;

namespace WorstHotel
{
    public sealed class InteractionHUD : MonoBehaviour
    {
        private FirstPersonController owner;
        private GUIStyle label, small, prompt, crosshair;
        public void Initialize(FirstPersonController controller) => owner = controller;

        private void OnGUI()
        {
            if (!owner || !owner.PlayerCamera) return;
            var coop = LocalCoopBootstrap.Instance;
            if (coop && !coop.IsLocalActor(owner.ActorId)) return;
            if (label == null) CreateStyles();
            var area = coop ? coop.ViewportForActor(owner.ActorId) : owner.PlayerCamera.rect;
            var oldMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(Screen.width / 1600f, Screen.height / 900f, 1));
            Rect viewport = new Rect(area.x * 1600, area.y * 900, area.width * 1600, area.height * 900);
            GUI.BeginGroup(viewport);
            bool service = GameSession.Instance && GameSession.Instance.Phase == DayPhase.Service;
            float badgeHeight = service ? 32 : 62;
            Color accent = owner.ActorId == 0 ? new Color(0.36f, 0.84f, 0.82f) : new Color(1, 0.64f, 0.36f);
            GUI.color = new Color(0.085f, 0.07f, 0.065f, 0.88f);
            GUI.DrawTexture(new Rect(16, 16, Mathf.Min(viewport.width - 32, 262), badgeHeight), Texture2D.whiteTexture);
            GUI.color = accent;
            GUI.DrawTexture(new Rect(16, 16, 4, badgeHeight), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(30, 20, viewport.width - 50, 29), coop && coop.IsSolo ? "SOLO  ·  HOTEL OWNER" :
                owner.ActorId == 0 ? "01  ·  FRONT DESK" : "02  ·  MAINTENANCE", label);
            if (!service) GUI.Label(new Rect(30, 48, viewport.width - 50, 25), owner.Input.DeviceLabel, small);
            if (!owner.IsUIBlocked && (!LocalCoopBootstrap.Instance || !LocalCoopBootstrap.Instance.IsPaused))
            {
                var actor = owner.Interactor;
                bool replica = !actor.HasWorldAuthority;
                bool usable = replica ? actor.ReplicaUsable : actor.Focused && actor.Focused.CanInteract(actor);
                GUI.color = usable || (replica ? actor.ReplicaPickup : actor.FocusedPickup) ? accent : new Color(1, 0.94f, 0.8f, 0.7f);
                GUI.Label(new Rect(viewport.width / 2 - 12, viewport.height / 2 - 15, 24, 30), usable ? "○" : "+", crosshair);
                GUI.color = Color.white;
                string caption = null;
                if (replica) caption = actor.ReplicaCaption;
                else if (actor.HeldBody && actor.Focused && actor.Focused.AllowsHeldItem(actor))
                    caption = actor.Focused.displayName + "\n" + (usable ? "[" + owner.Input.PrimaryLabel + "]  " : "") +
                        actor.Focused.GetPrompt(actor) + "\n[" + owner.Input.GrabLabel + "]  Put down";
                else if (actor.HeldBody) caption = "[" + owner.Input.GrabLabel + "]  Put down  ·  " + actor.HeldBody.name;
                else if (usable) caption = actor.Focused.displayName + "\n[" + owner.Input.PrimaryLabel + "]  " + actor.Focused.GetPrompt(actor);
                else if (actor.Focused) caption = actor.Focused.displayName + "\n" + actor.Focused.GetPrompt(actor);
                else if (actor.FocusedPickup) caption = actor.FocusedPickup.itemName + "\n[" + owner.Input.GrabLabel + "]  Carry";
                if (!string.IsNullOrEmpty(caption))
                {
                    float width = Mathf.Min(480, viewport.width - 40);
                    GUI.color = new Color(0.085f, 0.06f, 0.045f, 0.91f);
                    GUI.DrawTexture(new Rect((viewport.width - width) / 2, viewport.height - 119, width, 82), Texture2D.whiteTexture);
                    GUI.color = Color.white;
                    GUI.Label(new Rect((viewport.width - width) / 2 + 12, viewport.height - 115, width - 24, 74), caption, prompt);
                }
                else
                {
                    GUI.color = new Color(.085f, .07f, .065f, .88f);
                    GUI.DrawTexture(new Rect(16, viewport.height - 42, viewport.width - 32, 30), Texture2D.whiteTexture);
                    GUI.color = Color.white;
                    GUI.Label(new Rect(20, viewport.height - 40, viewport.width - 40, 28),
                        owner.Input.PrimaryLabel + " use   ·   " + owner.Input.SecondaryLabel + " alternate   ·   " + owner.Input.GrabLabel + " carry", small);
                }
            }
            GUI.color = new Color(0.1f, 0.055f, 0.045f, 1);
            GUI.DrawTexture(new Rect(viewport.width - 2, 0, 2, viewport.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.EndGroup();
            GUI.matrix = oldMatrix;
        }

        private void CreateStyles()
        {
            label = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold };
            label.normal.textColor = new Color(1, 0.92f, 0.75f);
            small = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
            small.normal.textColor = new Color(0.88f, 0.83f, 0.73f);
            prompt = new GUIStyle(label) { fontSize = 17, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            crosshair = new GUIStyle(label) { fontSize = 23, alignment = TextAnchor.MiddleCenter };
        }
    }
}
