using UnityEngine;

namespace WorstHotel
{
    /// <summary>Active staff consent controls hotel ticks; player and rigidbody time remain unchanged.</summary>
    [DefaultExecutionOrder(-425)]
    [RequireComponent(typeof(GameSession))]
    public sealed partial class WaitController : MonoBehaviour
    {
        public WaitConfig config;
        public bool IsWaiting => Mode == HotelAdvanceMode.Wait;
        public string Reason { get; private set; } = "Both staff hold WAIT to advance to the next hotel event.";
        public bool HasVoted(int actorId) => actorId >= 0 && actorId < 2 && votes[actorId];
        public float VoteProgress(int actorId) => actorId >= 0 && actorId < 2 ? Mathf.Clamp01(heldSeconds[actorId] / settings.HoldSeconds) : 0;
        public bool IsAwaitingRelease(int actorId) => actorId >= 0 && actorId < 2 && mustRelease[actorId];

        private GameSession session;
        private HotelSimulation observedSimulation;
        private int observedRevision;
        private WaitSettings settings;
        private readonly bool[] votes = new bool[2];
        private readonly bool[] mustRelease = new bool[2];
        private readonly float[] heldSeconds = new float[2];
        private GUIStyle bannerStyle;
        private bool HasConsentInProgress => votes[0] || votes[1] || heldSeconds[0] > 0 || heldSeconds[1] > 0;

        private void Awake()
        {
            session = GetComponent<GameSession>();
            settings = WaitConfig.Resolve(config);
        }

        private void Update()
        {
            if (session && session.IsLanReplica) return;
            ObserveSimulationEvents();
            var coop = LocalCoopBootstrap.Instance;
            if (!coop || coop.Players[0] == null || !coop.IsSolo && coop.Players[1] == null)
            { Stop("Two connected staff are required to wait."); return; }
            if (UpdateSleep(coop)) return;
            for (int i = 0; i < coop.RequiredStaffCount; i++)
                if (!coop.Players[i].Input.WaitHeld) mustRelease[i] = false;

            string blocked = BlockingReason(coop);
            if (blocked != null) { Stop(blocked); return; }
            for (int i = 0; i < coop.RequiredStaffCount; i++)
            {
                var input = coop.Players[i].Input;
                if (input.Move.sqrMagnitude > 0.0001f || input.Look.sqrMagnitude > 0.0001f ||
                    input.GrabPressed || input.SecondaryPressed || input.MenuConfirmPressed || input.MenuCancelPressed)
                {
                    if (IsWaiting || HasConsentInProgress || input.WaitHeld) Stop("Staff " + (i + 1) + " resumed activity.");
                    return;
                }
            }
            if (IsWaiting) return;
            for (int i = 0; i < coop.RequiredStaffCount; i++)
            {
                if (votes[i] || mustRelease[i]) continue;
                if (!coop.Players[i].Input.WaitHeld) { heldSeconds[i] = 0; continue; }
                heldSeconds[i] = Mathf.Min(settings.HoldSeconds, heldSeconds[i] + Time.unscaledDeltaTime);
                if (heldSeconds[i] >= settings.HoldSeconds) votes[i] = true;
            }
            if (votes[0] && (coop.IsSolo || votes[1]))
            {
                observedRevision = session.Simulation.EventRevision;
                BeginWaitMode();
                session.Simulation.Clock.SetSpeed(settings.Speed);
                Reason = "Waiting for the next hotel event. Move or look to stop.";
            }
        }

        private string BlockingReason(LocalCoopBootstrap coop)
        {
            if (!session || session.Simulation == null) return "Hotel is not ready.";
            bool preparing = !session.Simulation.ContinuousOperations && session.Phase == DayPhase.Planning;
            if (preparing && !System.Array.Exists(session.Rooms, room => !string.IsNullOrEmpty(room.DepartingGuestId)))
                return session.Simulation.Housekeeping != null && session.Simulation.Housekeeping.HasPendingWork ?
                    "Prepare the rooms yourselves: dirty linen to hamper, clean linen from the shelf, then make the bed." :
                    (coop.IsSolo ? "Rooms are ready. Commit the plan when ready." : "Rooms are ready. Commit the plan when both staff are ready.");
            if (!preparing && (session.Phase != DayPhase.Service || !session.Simulation.Running))
                return "WAIT is available during preparation and service.";
            if (coop.IsPaused || coop.WaitingForDevices) return "Hotel paused or a staff controller is disconnected.";
            if (!coop.IsSolo && coop.DebugKeyboardOnly) return "WAIT needs two real device assignments; debug actor switching cannot consent.";
            for (int i = 0; i < coop.RequiredStaffCount; i++)
            {
                var player = coop.Players[i];
                if (!player.DeviceReady) return "Both staff controllers must be connected.";
                if (player.IsUIBlocked) return "Close the hotel ledger before waiting.";
                if (player.Interactor.HeldBody) return "Put down carried objects before waiting.";
                // Read the physical button here, before PlayerInteractor.Update can start a repair this frame.
                if (player.Input.PrimaryHeld || player.Interactor.IsInteracting) return "Finish the physical interaction before waiting.";
            }
            if (preparing) return null;
            if (session.Simulation.Boiler.Failed && !session.Simulation.Boiler.MaintenanceInProgress && !session.Simulation.BoilerFailureAcknowledged) return "Respond to the failed boiler or accept its consequences in the ledger.";
            foreach (var situation in session.Simulation.Incidents.Items)
                if (situation.Active && situation.HasContactedStaff && !situation.PausedForTransfer &&
                    !situation.AttentionAcknowledged && situation.Stage == SituationStage.Critical)
                    return "Room " + situation.RoomId + " has a critical situation.";
            foreach (var request in session.Simulation.Requests.Items)
                if (!request.Resolved && !request.PausedForTransfer && !request.AttentionAcknowledged &&
                    (session.Simulation.LivingEnabled || !request.Compensated) && request.RemainingPatience <= 0)
                    return "Room " + request.RoomId + " needs a response before waiting.";
            return null;
        }

        public void Stop(string reason)
        {
            StopAdvanceView();
            if (session && session.Simulation != null) session.Simulation.Clock.SetSpeed(1);
            for (int i = 0; i < 2; i++)
            {
                votes[i] = false;
                heldSeconds[i] = 0;
                mustRelease[i] = true;
            }
            Reason = string.IsNullOrWhiteSpace(reason) ? "Waiting stopped." : reason;
        }

        public void ApplyLanView(string reason, bool[] consent, float[] progress)
        {
            if (!session || !session.IsLanReplica || consent == null || progress == null || consent.Length != 2 || progress.Length != 2) return;
            Reason = reason ?? "";
            for (int i = 0; i < 2; i++)
            { votes[i] = consent[i]; heldSeconds[i] = Mathf.Clamp01(progress[i]) * settings.HoldSeconds; mustRelease[i] = false; }
        }

        /// <summary>Also called after every simulation tick, so an event aborts the remaining accelerated batch.</summary>
        public void ObserveSimulationEvents()
        {
            if (session && session.IsLanReplica) return;
            if (!session || session.Simulation == null) return;
            var simulation = session.Simulation;
            if (!ReferenceEquals(observedSimulation, simulation))
            {
                if (observedSimulation != null) observedSimulation.Clock.SetSpeed(1);
                observedSimulation = simulation;
                observedRevision = simulation.EventRevision;
                RevokeSleep(StaffWakeReason.SessionChanged);
                Stop(LocalCoopBootstrap.Instance && LocalCoopBootstrap.Instance.IsSolo ? "Hold WAIT to advance to the next hotel event." : "Both staff hold WAIT to advance to the next hotel event.");
                return;
            }
            if (HasSleepConsentInProgress)
            {
                observedRevision = simulation.EventRevision;
                ObserveSleepAfterTick();
                return;
            }
            if (observedRevision != simulation.EventRevision)
            {
                observedRevision = simulation.EventRevision;
                Stop(simulation.LastEvent);
                return;
            }
            // Request patience can expire without creating another request/event. Abort that same tick.
            if (IsWaiting || HasConsentInProgress)
            {
                var coop = LocalCoopBootstrap.Instance;
                if (coop && coop.Players[0] != null && (coop.IsSolo || coop.Players[1] != null))
                {
                    string blocked = BlockingReason(coop);
                    if (blocked != null) Stop(blocked);
                }
            }
        }

        private void OnDisable() => Stop("Waiting stopped.");

        private void OnGUI()
        {
            if (!session || session.Simulation == null || (session.Phase != DayPhase.Service && (session.Phase != DayPhase.Planning ||
                session.Simulation.Housekeeping == null || !session.Simulation.Housekeeping.HasPendingWork))) return;
            if (ManagementUI.Instance && ManagementUI.Instance.IsOpen) return;
            var coop = LocalCoopBootstrap.Instance;
            if (!coop || coop.IsPaused || coop.Players[0] == null || !coop.IsSolo && coop.Players[1] == null) return;
            if (bannerStyle == null)
            {
                bannerStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleCenter, wordWrap = true };
                bannerStyle.normal.textColor = new Color(1, 0.94f, 0.79f);
            }
            var oldMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(Screen.width / 1600f, Screen.height / 900f, 1));
            const float width = 1120, x = 240;
            var oldColor = GUI.color;
            GUI.color = new Color(0.045f, 0.075f, 0.085f, 0.88f);
            GUI.DrawTexture(new Rect(x, 732, width, 46), Texture2D.whiteTexture);
            GUI.color = Color.white;
            bool bedConsent = HasSleepConsentInProgress;
            string a = bedConsent ? SleepVoteText(0) : VoteText(0, coop.Players[0].Input.WaitLabel);
            string b = coop.IsSolo ? "" : "     |     " + (bedConsent ? SleepVoteText(1) : VoteText(1, coop.Players[1].Input.WaitLabel));
            string debugLabel = Mode == HotelAdvanceMode.None && session.Simulation.Clock.Speed > 1 ? "  DEBUG" : "";
            GUI.Label(new Rect(x + 8, 733, width - 16, 44),
                "HOTEL TIME  " + session.Simulation.Clock.Speed + "×" + debugLabel + "     " + a + b + "\n" + Reason, bannerStyle);
            GUI.color = oldColor;
            GUI.matrix = oldMatrix;
        }

        private string VoteText(int actorId, string button) => "Staff " + (actorId + 1) + " " + button + ": " +
            (votes[actorId] ? "READY" : mustRelease[actorId] ? "release to rearm" : "hold " + Mathf.RoundToInt(VoteProgress(actorId) * 100) + "%");
        private string SleepVoteText(int actorId) => "Staff " + (actorId + 1) + ": " +
            (HasSleepConsent(actorId) ? "BED " + (SleepBedId(actorId) + 1) + " READY" : "use the other staff bed");
    }
}
