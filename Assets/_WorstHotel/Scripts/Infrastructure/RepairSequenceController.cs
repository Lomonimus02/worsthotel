using System;
using UnityEngine;

namespace WorstHotel
{
    /// <summary>Authenticates physical control holds before any boiler/sequence command.</summary>
    [DefaultExecutionOrder(-450)]
    public sealed class RepairSequenceController : MonoBehaviour
    {
        public RepairControl[] controls = Array.Empty<RepairControl>();
        public RepairStep Step => sequence != null ? sequence.Step : RepairStep.Idle;
        public bool IsRepairActive => CanUseControls;
        public float LatchAProgress => sequence != null ? sequence.LatchAProgress : 0;
        public float LatchBProgress => sequence != null ? sequence.LatchBProgress : 0;
        public bool PanelOpen => sequence != null && sequence.PanelOpen;
        public bool BreakerIsolated => sequence != null && sequence.BreakerIsolated;
        public int ReliefActorId => boiler != null ? boiler.ReliefActorId : -1;
        public bool SoloAssistEnabled => boiler != null && boiler.SoloAssistEnabled;
        public float SoloLatchSecondsRemaining => boiler != null ? boiler.SoloLatchSecondsRemaining : 0;
        public float SoloValveHoldProgress => boiler != null ? boiler.SoloValveHoldProgress : 0;
        public float SoloValveRequiredHoldSeconds => boiler != null ? boiler.SoloValveRequiredHoldSeconds : 0;
        public bool CanUseControls => isActiveAndEnabled && boiler != null && boiler.Failed &&
            GameSession.Instance != null && GameSession.Instance.Phase == DayPhase.Service;
        public string Status => (SoloLatchSecondsRemaining > 0 ? "SOLO CATCH " + Mathf.CeilToInt(SoloLatchSecondsRemaining) + "s · " : "") +
            (Time.unscaledTime < feedbackUntil ? feedback : sequence != null ? sequence.Status : "Emergency controls await a running service.");

        private HotelSimulation boundSimulation;
        private BoilerSystem boiler;
        private RepairSequence sequence;
        private string feedback;
        private float feedbackUntil;
        private readonly RaycastHit[] aimHits = new RaycastHit[64];

        private void Update()
        {
            EnsureBinding();
            var coop = LocalCoopBootstrap.Instance;
            if (!CanUseControls || !coop || coop.IsPaused || coop.WaitingForDevices)
            {
                CancelPhysicalHolds();
                if (boiler != null) boiler.CancelSoloLatch();
                if (sequence != null && boiler.Failed && coop && coop.IsPaused) sequence.CancelAttempt();
                return;
            }
            bool wasLatched = boiler.SoloValveLatched;
            boiler.AdvanceSoloLatch(Time.deltaTime);
            if (wasLatched && !boiler.SoloValveLatched)
            {
                sequence.CancelAttempt();
                ShowFeedback("Solo catch expired. Return to relief and secure pressure again.");
                foreach (var control in controls)
                    if (control && control.kind != RepairControlKind.ReliefValve) control.CancelPhysicalHold();
            }
            foreach (var control in controls)
                if (control && control.HoldingActor != null && !Authenticate(control, control.HoldingActor))
                    control.CancelPhysicalHold();
            ValidateLiveRelief();
            RefreshSequence();
        }

        private void EnsureBinding()
        {
            var session = GameSession.Instance;
            var simulation = session != null ? session.Simulation : null;
            if (ReferenceEquals(simulation, boundSimulation)) return;
            Unbind();
            boundSimulation = simulation;
            if (simulation == null) return;
            boiler = simulation.Boiler;
            sequence = new RepairSequence(boiler, session.BoilerSettings);
            boiler.OnFailureStarted += OnFailureStarted;
            boiler.OnFailureResolved += OnFailureResolved;
            boiler.OnPressureChanged += OnPressureChanged;
            feedbackUntil = 0;
        }

        private void Unbind()
        {
            CancelPhysicalHolds();
            if (boiler != null)
            {
                boiler.OnFailureStarted -= OnFailureStarted;
                boiler.OnFailureResolved -= OnFailureResolved;
                boiler.OnPressureChanged -= OnPressureChanged;
                if (boiler.ReliefActorId >= 0) boiler.SetRelief(boiler.ReliefActorId, false);
                boiler.CancelSoloLatch();
            }
            sequence = null; boiler = null; boundSimulation = null;
        }

        public bool PressControl(RepairControl control, PlayerInteractor actor)
        {
            EnsureBinding();
            if (!Authenticate(control, actor)) return false;
            control.ShowPress();
            if (control.kind == RepairControlKind.ReliefValve)
            {
                var relief = boiler.SetRelief(actor.ActorId, true);
                if (!relief.Success) ShowFeedback(relief.Message);
                return relief.Success;
            }
            ValidateLiveRelief();
            var result = sequence.Press(control.kind, actor.ActorId);
            if (!result.Success) ShowFeedback(result.Message);
            else feedbackUntil = 0;
            return result.Success;
        }

        public void HoldControl(RepairControl control, PlayerInteractor actor, float dt)
        {
            if (!Authenticate(control, actor)) { control.CancelPhysicalHold(); return; }
            if (control.kind == RepairControlKind.ReliefValve)
            {
                // This path can refresh a real hold only; a released target has no HoldingActor.
                if (control.HoldingActor == actor)
                {
                    boiler.SetRelief(actor.ActorId, true);
                    if (boiler.SoloAssistEnabled) boiler.HoldSoloValve(actor.ActorId, dt);
                }
                return;
            }
            if (control.kind != RepairControlKind.LatchA && control.kind != RepairControlKind.LatchB) return;
            ValidateLiveRelief();
            sequence.HoldLatch(control.kind, actor.ActorId, dt);
        }

        public void ReleaseControl(RepairControl control, PlayerInteractor actor)
        {
            if (boiler == null || actor == null) return;
            if (control.kind == RepairControlKind.ReliefValve) boiler.SetRelief(actor.ActorId, false);
            else if (sequence != null) sequence.ReleaseLatch(control.kind, actor.ActorId);
        }

        private bool Authenticate(RepairControl control, PlayerInteractor actor)
        {
            if (!CanUseControls || !control || control.controller != this || !control.isActiveAndEnabled || !actor || !actor.isActiveAndEnabled) return false;
            var coop = LocalCoopBootstrap.Instance;
            if (!coop || coop.IsPaused) return false;
            int id = actor.ActorId;
            if (id < 0 || id >= coop.Players.Length) return false;
            var player = coop.Players[id];
            if (!player || player.Interactor != actor || !player.DeviceReady || player.IsUIBlocked || !player.Input.PrimaryHeld) return false;
            if (actor.Focused != control || !actor.PlayerCamera) return false;

            // Re-raycast every hold validation; a previous frame's Focused field alone cannot
            // keep a valve active after walking away, turning aside or moving behind geometry.
            var cameraTransform = actor.PlayerCamera.transform;
            int count = Physics.RaycastNonAlloc(cameraTransform.position, cameraTransform.forward,
                aimHits, actor.reach, ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            RepairControl nearestControl = null;
            for (int i = 0; i < count; i++)
            {
                var hit = aimHits[i];
                if (hit.collider.transform.IsChildOf(actor.transform) || hit.distance >= nearest) continue;
                nearest = hit.distance;
                nearestControl = hit.collider.GetComponentInParent<RepairControl>();
            }
            return nearestControl == control;
        }

        private void ValidateLiveRelief()
        {
            if (boiler == null || boiler.ReliefActorId < 0) return;
            foreach (var control in controls)
                if (control && control.kind == RepairControlKind.ReliefValve && control.HoldingActor != null &&
                    control.HoldingActor.ActorId == boiler.ReliefActorId && Authenticate(control, control.HoldingActor)) return;
            boiler.SetRelief(boiler.ReliefActorId, false);
        }

        private void RefreshSequence()
        {
            if (sequence == null) return;
            var previous = sequence.Step;
            sequence.Refresh();
            if (previous != RepairStep.Panel && sequence.Step == RepairStep.Panel)
            {
                foreach (var control in controls)
                    if (control && control.kind != RepairControlKind.ReliefValve) control.CancelPhysicalHold();
                feedbackUntil = 0;
            }
        }

        private void OnPressureChanged(float pressure)
        {
            // Restart sets Failed=false before emitting its pressure event. Let the in-flight
            // sequence command finish before observing running state, preserving seated latches.
            if (boiler != null && boiler.Failed) RefreshSequence();
        }
        private void OnFailureStarted() { CancelPhysicalHolds(); sequence.ResetForFailure(); feedbackUntil = 0; }
        private void OnFailureResolved() { CancelPhysicalHolds(); feedbackUntil = 0; }
        private void ShowFeedback(string message) { feedback = message; feedbackUntil = Time.unscaledTime + 3.5f; }

        private void CancelPhysicalHolds()
        {
            if (controls == null) return;
            foreach (var control in controls) if (control) control.CancelPhysicalHold();
        }

        private void OnDisable() { CancelPhysicalHolds(); if (boiler != null) boiler.CancelSoloLatch(); if (sequence != null) sequence.CancelAttempt(); }
        private void OnDestroy() => Unbind();
    }
}
