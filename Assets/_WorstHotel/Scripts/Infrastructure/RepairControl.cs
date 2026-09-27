using UnityEngine;

namespace WorstHotel
{
    /// <summary>One readable physical control, with its own moving mechanical part.</summary>
    public sealed class RepairControl : HotelInteractable
    {
        public RepairSequenceController controller;
        public RepairControlKind kind;
        public Transform movingPart;
        public PlayerInteractor HoldingActor { get; private set; }
        private Quaternion restRotation;
        private Vector3 restPosition;
        private float angle;
        private float buttonPressedUntil;
        private bool visualReady;

        private void Start() => CacheVisual();

        private void CacheVisual()
        {
            if (visualReady || !movingPart) return;
            restRotation = movingPart.localRotation;
            restPosition = movingPart.localPosition;
            visualReady = true;
        }

        public override bool CanInteract(PlayerInteractor actor) => base.CanInteract(actor) && controller && controller.CanUseControls;

        public override string GetPrompt(PlayerInteractor actor)
        {
            var session = GameSession.Instance;
            if (session && session.Simulation.ContinuousOperations && controller && !controller.CanUseControls)
                return session.Simulation.Boiler.MaintenanceInProgress ? controller.Status :
                    BoilerMaintenanceLabels.State(session.Simulation) + " · Proper maintenance is available in Hotel operations";
            if (!controller || !controller.CanUseControls) return "No active service fault. Restore wear with between-day maintenance.";
            if (controller.SoloAssistEnabled && kind == RepairControlKind.ReliefValve)
                return controller.SoloLatchSecondsRemaining > 0 ?
                    "Valve catch secured · " + Mathf.CeilToInt(controller.SoloLatchSecondsRemaining) + "s left\nPanel → boiler isolation → A → B → restart" :
                    "Hold relief in green for " + controller.SoloValveRequiredHoldSeconds.ToString("0.#") + "s to secure catch\nSafe hold " + Mathf.RoundToInt(controller.SoloValveHoldProgress * 100) + "% · temporary SOLO support";
            if (!controller.SoloAssistEnabled && kind != RepairControlKind.ReliefValve && actor != null && controller.ReliefActorId == actor.ActorId)
                return "Keep holding relief; your partner operates these controls";
            switch (kind)
            {
                case RepairControlKind.ReliefValve: return "Hold relief valve — keep pressure inside the green band";
                case RepairControlKind.Panel: return "Open hinged service panel";
                case RepairControlKind.Breaker: return "Isolate boiler with red lever\nRoom circuits A/B stay unchanged";
                case RepairControlKind.LatchA: return "Turn latch A fully (hold); an unseated latch springs back";
                case RepairControlKind.LatchB: return "Turn latch B fully (hold); keep relief supported";
                case RepairControlKind.Restart:
                    if (session && session.Simulation.ContinuousOperations)
                        return "Emergency patch · $" + session.Economy.CheapPatchCost + " · " +
                            session.BoilerSettings.Capacity.EmergencyPatchCondition.ToString("F0") + "% condition\n" +
                            (controller.SoloAssistEnabled ? "Both latches seated · solo catch secured" : "Both latches seated · partner holds relief");
                    return controller.SoloAssistEnabled ? "Restart after both latches while the solo catch is secured" : "Press restart after both latches, while partner holds relief";
                default: return instruction;
            }
        }

        public override void Interact(PlayerInteractor actor)
        {
            if (HoldingActor != null && HoldingActor != actor) return;
            if (controller && controller.PressControl(this, actor))
            {
                // Successful restart clears failure synchronously; do not leave a new ghost
                // button holder after the controller's failure-resolved cancellation.
                if (controller.CanUseControls) HoldingActor = actor;
            }
        }

        public override void HoldInteract(PlayerInteractor actor, float deltaTime)
        {
            if (HoldingActor == actor && controller) controller.HoldControl(this, actor, deltaTime);
        }

        public override void EndInteract(PlayerInteractor actor)
        {
            if (HoldingActor == actor) CancelPhysicalHold();
        }

        public void CancelPhysicalHold()
        {
            var actor = HoldingActor;
            HoldingActor = null;
            if (actor != null && controller) controller.ReleaseControl(this, actor);
        }

        public void ShowPress() => buttonPressedUntil = Time.unscaledTime + 0.30f;

        private void Update()
        {
            CacheVisual();
            if (!visualReady || !controller) return;
            switch (kind)
            {
                case RepairControlKind.ReliefValve:
                    if (HoldingActor != null && controller.IsRepairActive && controller.ReliefActorId == HoldingActor.ActorId)
                        angle += 110 * Time.deltaTime;
                    movingPart.localRotation = restRotation * Quaternion.AngleAxis(angle, Vector3.forward);
                    break;
                case RepairControlKind.Panel:
                    angle = Mathf.MoveTowards(angle, controller.PanelOpen ? 110 : 0, 200 * Time.deltaTime);
                    movingPart.localRotation = restRotation * Quaternion.AngleAxis(angle, Vector3.up);
                    break;
                case RepairControlKind.Breaker:
                    angle = Mathf.MoveTowards(angle, controller.BreakerIsolated ? -55 : 0, 220 * Time.deltaTime);
                    movingPart.localRotation = restRotation * Quaternion.AngleAxis(angle, Vector3.right);
                    break;
                case RepairControlKind.LatchA:
                case RepairControlKind.LatchB:
                    float progress = kind == RepairControlKind.LatchA ? controller.LatchAProgress : controller.LatchBProgress;
                    angle = Mathf.MoveTowards(angle, progress * 90, 220 * Time.deltaTime);
                    movingPart.localRotation = restRotation * Quaternion.AngleAxis(angle, Vector3.forward);
                    break;
                case RepairControlKind.Restart:
                    var target = restPosition + Vector3.forward * (Time.unscaledTime < buttonPressedUntil ? 0.075f : 0);
                    movingPart.localPosition = Vector3.MoveTowards(movingPart.localPosition, target, 0.7f * Time.deltaTime);
                    break;
            }
        }

        private void OnDisable() => CancelPhysicalHold();
    }
}
