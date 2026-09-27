using UnityEngine;

namespace WorstHotel
{
    /// <summary>One physical inspection surface; choosing work never starts or pays for it.</summary>
    public sealed class BoilerServiceInteraction : HotelInteractable
    {
        public static BoilerServiceInteraction Instance { get; private set; }
        public const float SetupHoldSeconds = 1.5f;
        public Vector3 InteractionPoint => useCollider is BoxCollider box ? box.transform.TransformPoint(box.center) :
            useCollider && useCollider.enabled ? useCollider.bounds.center : transform.position;

        sealed class Selection
        {
            public HotelSimulation simulation;
            public BoilerServiceKind kind;
            public int revision;
            public float seconds;
            public bool holding, completing, consumed;
        }
        readonly Selection[] selections = { new Selection(), new Selection() };
        Collider useCollider;

        void Awake() { Instance = this; useCollider = GetComponent<Collider>(); }

        public CommandResult Select(int actorId, BoilerServiceKind kind, int expectedRevision)
        {
            var session = GameSession.Instance;
            if (!isActiveAndEnabled || !session || session.Simulation == null || session.Simulation.IsReadOnlyMirror ||
                session.Phase != DayPhase.Service || !TryGetActor(actorId, out _) || LocalCoopBootstrap.Instance.IsPaused)
                return CommandResult.Fail("An active staff member must select boiler service.");
            var allowed = session.Simulation.CanBeginBoilerMaintenance(actorId, kind, expectedRevision);
            if (!allowed.Success) return allowed;
            if (expectedRevision < 0 || expectedRevision != session.Simulation.Boiler.MaintenanceRevision)
                return CommandResult.Fail("The boiler changed. Inspect it again before selecting service.");
            Clear(actorId);
            var selection = selections[actorId];
            selection.simulation = session.Simulation; selection.kind = kind; selection.revision = expectedRevision;
            return CommandResult.Ok(BoilerMaintenanceLabels.ServiceName(kind) + " selected. No charge yet. Close menu; hold at the boiler plate.");
        }

        public bool TryGetSelection(int actorId, out BoilerServiceKind kind, out int revision)
        {
            kind = BoilerServiceKind.None; revision = -1;
            var session = GameSession.Instance;
            if (actorId < 0 || actorId >= selections.Length || !session || session.Simulation == null) return false;
            var selection = selections[actorId];
            if (selection.kind == BoilerServiceKind.None || !ReferenceEquals(selection.simulation, session.Simulation) ||
                selection.revision != session.Simulation.Boiler.MaintenanceRevision || session.Simulation.Boiler.MaintenanceInProgress) return false;
            kind = selection.kind; revision = selection.revision; return true;
        }

        public float SetupProgress(int actorId) => TryGetSelection(actorId, out _, out _) ?
            Mathf.Clamp01(selections[actorId].seconds / SetupHoldSeconds) : 0;

        public void ClearSelection(int actorId)
        { if (actorId >= 0 && actorId < selections.Length) Clear(actorId); }

        // This capability exists only while this component calls the session synchronously.
        // Neither a network command nor a previous completed hold can manufacture/reuse it.
        public bool TryConsumePreparedSetup(int actorId, BoilerServiceKind kind, int expectedRevision)
        {
            if (!TryGetSelection(actorId, out var selectedKind, out var revision) || selectedKind != kind || revision != expectedRevision)
                return false;
            var selection = selections[actorId];
            if (!selection.completing || selection.consumed || selection.seconds < SetupHoldSeconds ||
                !TryGetActor(actorId, out var actor) || !ValidPhysicalHold(actor)) return false;
            selection.consumed = true; return true;
        }

        public override bool CanInteract(PlayerInteractor actor) => base.CanInteract(actor) && actor &&
            GameSession.Instance && GameSession.Instance.Phase == DayPhase.Service &&
            GameSession.Instance.Simulation != null && GameSession.Instance.Simulation.ContinuousOperations;

        public override string GetPrompt(PlayerInteractor actor)
        {
            var session = GameSession.Instance;
            if (!session || session.Simulation == null) return "Boiler inspection unavailable";
            var boiler = session.Simulation.Boiler;
            if (boiler.MaintenanceInProgress)
                return BoilerMaintenanceLabels.ServiceName(boiler.ActiveServiceKind) + " · heat off\n" +
                    BoilerMaintenanceLabels.Remaining(session.Simulation) + " remaining · Use: inspect";
            if (actor && TryGetSelection(actor.ActorId, out var kind, out _))
                return "Hold: start " + BoilerMaintenanceLabels.ServiceName(kind) + " · " + Mathf.RoundToInt(SetupProgress(actor.ActorId) * 100) + "%\n" +
                    "Heating stops when setup finishes · Q / X: inspect";
            return "Use: inspect boiler / select service\n" + BoilerMaintenanceLabels.Condition(boiler) + " condition · " +
                BoilerMaintenanceLabels.Load(boiler, session.BoilerSettings.Capacity) + " load · " + BoilerMaintenanceLabels.Stress(boiler) + " stress";
        }

        public override void Interact(PlayerInteractor actor)
        {
            if (!CanInteract(actor)) return;
            if (!TryGetSelection(actor.ActorId, out _, out _)) { GameSession.Instance.OpenBoilerInspection(actor.ActorId); return; }
            var selection = selections[actor.ActorId]; selection.seconds = 0; selection.holding = true;
        }

        public override void SecondaryInteract(PlayerInteractor actor)
        {
            if (!CanInteract(actor)) return;
            EndInteract(actor); GameSession.Instance.OpenBoilerInspection(actor.ActorId);
        }

        public override void HoldInteract(PlayerInteractor actor, float deltaTime)
        {
            if (!actor || !TryGetSelection(actor.ActorId, out var kind, out var revision)) return;
            var selection = selections[actor.ActorId];
            if (!selection.holding || !ValidPhysicalHold(actor)) { EndInteract(actor); return; }
            // Hotel WAIT speed never accelerates physical work; a stalled frame cannot complete it.
            selection.seconds += Mathf.Min(Time.unscaledDeltaTime, .1f);
            if (selection.seconds < SetupHoldSeconds) return;
            selection.completing = true;
            try { GameSession.Instance.BeginBoilerMaintenance(actor.ActorId, kind, revision); }
            finally { Clear(actor.ActorId); }
        }

        public override void EndInteract(PlayerInteractor actor)
        {
            if (!actor || actor.ActorId < 0 || actor.ActorId >= selections.Length) return;
            var selection = selections[actor.ActorId]; selection.holding = false; selection.seconds = 0;
        }

        bool ValidPhysicalHold(PlayerInteractor actor)
        {
            if (!CanInteract(actor) || !actor.CanAct || actor.HeldBody || !actor.IsInteracting || actor.Focused != this ||
                !TryGetActor(actor.ActorId, out var current) || current != actor) return false;
            var camera = actor.PlayerCamera;
            // The normal focus query already rejects nearer surfaces. Recheck the first hit at
            // completion so direct method calls cannot substitute stale focus for current reach.
            var hits = Physics.RaycastAll(camera.transform.position, camera.transform.forward, actor.reach, ~0, QueryTriggerInteraction.Ignore);
            Collider first = null; float nearest = float.MaxValue;
            foreach (var hit in hits)
                if (!hit.collider.transform.IsChildOf(actor.transform) && hit.distance < nearest)
                { first = hit.collider; nearest = hit.distance; }
            return first && first.GetComponentInParent<BoilerServiceInteraction>() == this;
        }

        static bool TryGetActor(int actorId, out PlayerInteractor actor)
        {
            actor = null; var staff = LocalCoopBootstrap.Instance;
            if (!staff || actorId < 0 || actorId >= staff.Players.Length) return false;
            var player = staff.Players[actorId];
            if (!player || !player.isActiveAndEnabled || !player.DeviceReady) return false;
            actor = player.Interactor;
            return actor && actor.isActiveAndEnabled && actor.HasWorldAuthority;
        }

        void Update()
        {
            for (int actorId = 0; actorId < selections.Length; actorId++)
            {
                if (!TryGetSelection(actorId, out _, out _) || !TryGetActor(actorId, out var actor)) { Clear(actorId); continue; }
                if (!actor.CanAct || !actor.IsInteracting || actor.Focused != this) EndInteract(actor);
            }
        }

        void Clear(int actorId)
        {
            var selection = selections[actorId]; selection.simulation = null; selection.kind = BoilerServiceKind.None;
            selection.revision = -1; selection.seconds = 0; selection.holding = selection.completing = selection.consumed = false;
        }
        void OnDisable() { for (int actorId = 0; actorId < selections.Length; actorId++) Clear(actorId); }
        void OnDestroy() { if (Instance == this) Instance = null; }
    }
}
