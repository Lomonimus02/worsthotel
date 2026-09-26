using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace WorstHotel.Tests
{
    // Shares the fresh-scene/virtual-device fixture with the basic movement tests. Setup positions
    // actors at stations; look and use are then driven through real Input System events/raycasts.
    // ForceFailure is explicit scenario setup, not evidence of a naturally reached overload.
    public sealed partial class Phase1PlayModeTests
    {
        private RepairSequenceController RepairController => UnityEngine.Object.FindAnyObjectByType<RepairSequenceController>();
        private RepairControl Control(RepairControlKind kind) => RepairController.controls.Single(c => c.kind == kind);

        private void StartOrdinaryTestShift()
        {
            var session = GameSession.Instance;
            Assert.That(session, Is.Not.Null);
            Assert.That(session.Phase, Is.EqualTo(DayPhase.Planning));
            var offers = session.Plan.Applications.Take(4).ToArray();
            for (int i = 0; i < offers.Length; i++)
            {
                int min = session.Economy.MinPrice, step = session.Economy.PriceStep;
                int max = min + (session.Economy.MaxPrice - min) / step * step;
                int price = Mathf.Clamp(min + Mathf.RoundToInt((offers[i].ReferencePrice - min) / (float)step) * step, min, max);
                session.Assign(i % 2, offers[i].Id, session.Rooms[i].Profile.Id, price);
            }
            Assert.That(session.Plan.Assignments.Count, Is.EqualTo(offers.Length));
            session.CommitPlan(0);
            Assert.That(session.Phase, Is.EqualTo(DayPhase.Service));
            Assert.That(session.Simulation.Running, Is.True);
        }

        private static void QueueUse(Gamepad pad, bool held, bool cancel = false)
        {
            var state = new GamepadState();
            if (held) state = state.WithButton(GamepadButton.South);
            if (cancel) state = state.WithButton(GamepadButton.East);
            InputSystem.QueueStateEvent(pad, state);
        }

        private IEnumerator FaceStation(FirstPersonController player, Gamepad pad, HotelInteractable fixture, Vector3 aimPoint)
        {
            QueueUse(pad, false);
            var pose = new GameObject("Test station approach pose");
            pose.transform.position = new Vector3(aimPoint.x, 0.08f, aimPoint.z - 1.45f);
            pose.transform.rotation = Quaternion.identity;
            player.ResetToSpawn(pose.transform);
            UnityEngine.Object.Destroy(pose);
            yield return null;
            yield return null;

            float deadline = Time.realtimeSinceStartup + 5;
            while (Time.realtimeSinceStartup < deadline)
            {
                var delta = aimPoint - player.PlayerCamera.transform.position;
                float wantedPitch = -Mathf.Atan2(delta.y, Mathf.Sqrt(delta.x * delta.x + delta.z * delta.z)) * Mathf.Rad2Deg;
                float currentPitch = Mathf.DeltaAngle(0, player.PlayerCamera.transform.localEulerAngles.x);
                float error = wantedPitch - currentPitch;
                if (Mathf.Abs(error) < 2.5f) break;
                // Exercise actual controller look; no private pitch/Focused fields are injected.
                InputSystem.QueueStateEvent(pad, new GamepadState { rightStick = new Vector2(0, -Mathf.Sign(error) * 0.35f) });
                yield return null;
            }
            QueueUse(pad, false);
            yield return null;
            yield return null;
            Assert.That(player.Interactor.Focused, Is.SameAs(fixture),
                "Station approach must hit its real collider: " + fixture.name + "; actual focus: " +
                (player.Interactor.Focused ? player.Interactor.Focused.name : "none"));
        }

        private IEnumerator FaceRepair(FirstPersonController player, Gamepad pad, RepairControlKind kind)
        {
            var control = Control(kind);
            yield return FaceStation(player, pad, control, control.transform.position);
        }

        private IEnumerator WaitForCondition(Func<bool> condition, float maximumSeconds, string reason)
        {
            float deadline = Time.realtimeSinceStartup + maximumSeconds;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, reason + (RepairController ? " / " + RepairController.Status : ""));
        }

        private IEnumerator TapOperatorControl(RepairControlKind kind, RepairStep expectedStep)
        {
            yield return FaceRepair(bootstrap.Players[1], padB, kind);
            QueueUse(padB, true);
            yield return null;
            yield return null;
            QueueUse(padB, false);
            yield return null;
            yield return null;
            Assert.That(RepairController.Step, Is.EqualTo(expectedStep), kind + ": " + RepairController.Status);
        }

        private IEnumerator HoldPhysicalRelief()
        {
            yield return FaceRepair(bootstrap.Players[0], padA, RepairControlKind.ReliefValve);
            QueueUse(padA, true);
            yield return null;
            yield return null;
            Assert.That(GameSession.Instance.Simulation.Boiler.ReliefActorId, Is.EqualTo(0));
            Assert.That(Control(RepairControlKind.ReliefValve).HoldingActor, Is.SameAs(bootstrap.Players[0].Interactor));
        }

        [UnityTest]
        public IEnumerator TwoActorsRestartByUsingTheActualSixRaycastControls()
        {
            StartOrdinaryTestShift();
            var boiler = GameSession.Instance.Simulation.Boiler;
            boiler.ForceFailure();
            yield return HoldPhysicalRelief();
            yield return WaitForCondition(() => boiler.InRepairBand, 20, "Physical relief did not lower the gauge into its green band");
            yield return TapOperatorControl(RepairControlKind.Panel, RepairStep.Breaker);
            yield return TapOperatorControl(RepairControlKind.Breaker, RepairStep.LatchA);

            yield return FaceRepair(bootstrap.Players[1], padB, RepairControlKind.LatchA);
            QueueUse(padB, true);
            yield return WaitForCondition(() => RepairController.Step == RepairStep.LatchB, 6,
                "Holding the actual first latch failed to seat it");
            QueueUse(padB, false);
            yield return null;
            yield return null;
            Assert.That(RepairController.LatchAProgress, Is.EqualTo(1));

            yield return FaceRepair(bootstrap.Players[1], padB, RepairControlKind.LatchB);
            QueueUse(padB, true);
            yield return WaitForCondition(() => RepairController.Step == RepairStep.Restart, 6,
                "Holding the actual second latch failed to seat it");
            QueueUse(padB, false);
            yield return null;
            yield return null;
            yield return TapOperatorControl(RepairControlKind.Restart, RepairStep.Complete);
            Assert.That(boiler.Failed, Is.False);
            Assert.That(boiler.HeatingOutput, Is.GreaterThan(GameSession.Instance.BoilerSettings.FailedHeatOutput));
            Assert.That(boiler.ReliefActorId, Is.EqualTo(-1));
            Assert.That(Control(RepairControlKind.ReliefValve).HoldingActor, Is.Null);
            Assert.That(RepairController.BreakerIsolated, Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator LookingAwayReleasesValveEvenWhileItsUseButtonRemainsHeld()
        {
            StartOrdinaryTestShift();
            var boiler = GameSession.Instance.Simulation.Boiler;
            boiler.ForceFailure();
            yield return HoldPhysicalRelief();
            InputSystem.QueueStateEvent(padA,
                new GamepadState { rightStick = Vector2.right }.WithButton(GamepadButton.South));
            yield return new WaitForSecondsRealtime(0.35f);
            QueueUse(padA, true); // Keep use held; only looking changed, not the release input.
            yield return null;
            yield return null;
            Assert.That(boiler.ReliefActorId, Is.EqualTo(-1));
            Assert.That(Control(RepairControlKind.ReliefValve).HoldingActor, Is.Null);
            float unsupportedPressure = boiler.Pressure;
            yield return new WaitForSecondsRealtime(0.6f);
            Assert.That(boiler.Pressure, Is.GreaterThan(unsupportedPressure));
            Assert.That(boiler.Failed, Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator PartnersServiceTerminalBlocksOnlyItsOwnerAndKeepsLiveValveSupport()
        {
            StartOrdinaryTestShift();
            var boiler = GameSession.Instance.Simulation.Boiler;
            boiler.ForceFailure();
            yield return HoldPhysicalRelief();
            var terminal = UnityEngine.Object.FindAnyObjectByType<ReceptionTerminal>();
            Assert.That(terminal, Is.Not.Null);
            yield return FaceStation(bootstrap.Players[1], padB, terminal, terminal.transform.position + Vector3.up * 0.35f);
            QueueUse(padB, true);
            yield return null;
            yield return null;
            yield return null;
            QueueUse(padB, false);
            var ledger = ManagementUI.Instance;
            Assert.That(ledger.IsOpen, Is.True);
            Assert.That(ledger.Owner, Is.EqualTo(1));
            Assert.That(bootstrap.Players[0].IsUIBlocked, Is.False);
            Assert.That(bootstrap.Players[1].IsUIBlocked, Is.True);
            Assert.That(boiler.ReliefActorId, Is.EqualTo(0));
            QueueUse(padA, true, true); // Non-owner presses Back/B while continuing to hold relief.
            yield return null;
            yield return null;
            Assert.That(ledger.IsOpen, Is.True, "The other player's menu cancel must not close the owner's ledger.");
            QueueUse(padA, true);
            QueueUse(padB, false, true);
            yield return null;
            yield return null;
            Assert.That(ledger.IsOpen, Is.False);
            Assert.That(boiler.ReliefActorId, Is.EqualTo(0));
            QueueUse(padA, false);
            yield return null;
            yield return null;
            Assert.That(boiler.ReliefActorId, Is.EqualTo(-1));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator NewGameClearsOldHoldAndBindsRepairToTheReplacementSimulation()
        {
            StartOrdinaryTestShift();
            var session = GameSession.Instance;
            var oldSimulation = session.Simulation;
            oldSimulation.Boiler.ForceFailure();
            yield return HoldPhysicalRelief();
            session.NewGame();
            yield return null;
            yield return null;
            Assert.That(session.Simulation, Is.Not.SameAs(oldSimulation));
            Assert.That(oldSimulation.Boiler.ReliefActorId, Is.EqualTo(-1));
            Assert.That(Control(RepairControlKind.ReliefValve).HoldingActor, Is.Null);
            Assert.That(RepairController.ReliefActorId, Is.EqualTo(-1));
            Assert.That(RepairController.IsRepairActive, Is.False);
            if (ManagementUI.Instance) ManagementUI.Instance.Close();
            StartOrdinaryTestShift();
            session.Simulation.Boiler.ForceFailure();
            yield return null;
            yield return null;
            Assert.That(RepairController.IsRepairActive, Is.True);
            Assert.That(RepairController.Step, Is.EqualTo(RepairStep.Panel));
            Assert.That(session.Simulation.Boiler.ReliefActorId, Is.EqualTo(-1),
                "A still-depressed input from the previous session must not resurrect a physical valve hold.");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator VirtualDisconnectPausesCancelsValveAndKeepsTheOtherPadsActor()
        {
            StartOrdinaryTestShift();
            var boiler = GameSession.Instance.Simulation.Boiler;
            boiler.ForceFailure();
            yield return HoldPhysicalRelief();
            InputSystem.RemoveDevice(padA);
            yield return null;
            yield return null;
            Assert.That(bootstrap.IsPaused, Is.True);
            Assert.That(bootstrap.Players[0].DeviceReady, Is.False);
            Assert.That(bootstrap.Players[1].Input.Gamepad, Is.SameAs(padB));
            Assert.That(boiler.ReliefActorId, Is.EqualTo(-1));
            Assert.That(Control(RepairControlKind.ReliefValve).HoldingActor, Is.Null);
            padA = InputSystem.AddDevice<Gamepad>("Phase1SmokeStaffAReplacement");
            yield return null;
            yield return null;
            Assert.That(bootstrap.IsPaused, Is.False);
            Assert.That(bootstrap.Players[0].Input.Gamepad, Is.SameAs(padA));
            Assert.That(bootstrap.Players[1].Input.Gamepad, Is.SameAs(padB));
            Assert.That(boiler.ReliefActorId, Is.EqualTo(-1));
            LogAssert.NoUnexpectedReceived();
        }
    }
}
