using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace WorstHotel.Tests
{
    public sealed partial class Phase1PlayModeTests
    {
        IEnumerator StartSoloFixture()
        {
            ManagementUI.Instance?.Close();
            bootstrap.ConfigureSolo();
            // Remove only this fixture's unused virtual device. There is no dummy staff actor.
            InputSystem.RemoveDevice(padB); padB = null;
            var session = GameSession.Instance;
            waitScenarioSessionConfig = Object.Instantiate(session.config);
            waitScenarioLivingConfig = Object.Instantiate(session.config.living);
            waitScenarioLivingConfig.firstArrivalSeconds = 250;
            waitScenarioLivingConfig.arrivalJitterSeconds = 0;
            waitScenarioSessionConfig.living = waitScenarioLivingConfig;
            session.config = waitScenarioSessionConfig;
            session.NewGame();
            yield return null; yield return null;
            var offer = session.Plan.Applications.First();
            session.Assign(0, offer.Id, 101, session.Economy.MinPrice);
            session.CommitPlan(0);
            ManagementUI.Instance?.Close();
            yield return null;
            Assert.That(bootstrap.Players[1], Is.Null);
            Assert.That(bootstrap.IsSolo && session.IsSolo, Is.True);
        }

        [UnityTest]
        public IEnumerator SoloUsesOneActualRigAndOneWaitConsentAndKeepsModeOnReset()
        {
            yield return StartSoloFixture();
            var actor = bootstrap.Players[0];
            Assert.That(Object.FindObjectsByType<FirstPersonController>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length, Is.EqualTo(1));
            Assert.That(bootstrap.GetComponentsInChildren<Camera>(true).Length, Is.EqualTo(1));
            Assert.That(bootstrap.GetComponentsInChildren<AudioListener>(true).Length, Is.EqualTo(1));
            Assert.That(actor.PlayerCamera.rect, Is.EqualTo(new Rect(0, 0, 1, 1)));
            Assert.That(actor.Input.Gamepad, Is.SameAs(padA));
            Assert.That(bootstrap.RequiredStaffCount, Is.EqualTo(1));
            Assert.That(bootstrap.IsLocalActor(1), Is.False);
            QueueWait(padA, false); yield return null; yield return null;
            float physicsStep = Time.fixedDeltaTime;
            QueueWait(padA, true);
            yield return WaitForCondition(() => Waiter.IsWaiting, WaitHoldSeconds + 1, "One actual solo input must authorize WAIT");
            Assert.That(Waiter.HasVoted(0), Is.True);
            Assert.That(Waiter.HasVoted(1), Is.False, "SOLO must not fabricate the missing staff consent.");
            Assert.That(Time.timeScale, Is.EqualTo(1));
            Assert.That(Time.fixedDeltaTime, Is.EqualTo(physicsStep));
            GameSession.Instance.Simulation.SignalEvent("Solo meaningful hotel event");
            Waiter.ObserveSimulationEvents();
            Assert.That(Waiter.IsWaiting, Is.False);
            yield return new WaitForSecondsRealtime(WaitHoldSeconds + .1f);
            Assert.That(Waiter.IsWaiting, Is.False, "Held consent needs a fresh release after an event.");
            QueueWait(padA, false);
            GameSession.Instance.NewGame();
            yield return null; yield return null;
            Assert.That(bootstrap.IsSolo && GameSession.Instance.Simulation.Boiler.SoloAssistEnabled, Is.True);
            Assert.That(bootstrap.Players[1], Is.Null);
            Assert.That(GameSession.Instance.Day, Is.EqualTo(1));
            bootstrap.SendMessage("OnApplicationFocus", false);
            Assert.That(bootstrap.IsPaused, Is.True, "Solo focus loss pauses its own hotel.");
            bootstrap.SendMessage("OnApplicationFocus", true);
            Assert.That(bootstrap.IsPaused, Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator SoloRepairsUsingOnePhysicalValveCatchWalkAndAllFiveRemainingControls()
        {
            yield return StartSoloFixture();
            var session = GameSession.Instance;
            var boiler = session.Simulation.Boiler;
            // Explicit failure setup isolates the physical sequence; this is not natural overload evidence.
            boiler.ForceFailure();
            yield return HoldPhysicalRelief();
            yield return WaitForCondition(() => boiler.SoloValveLatched, 22, "Real solo relief hold must secure the catch in the safe band");
            Assert.That(RepairController.Status, Does.Contain("SOLO CATCH"));
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That(boiler.ReliefActorId, Is.EqualTo(-1));
            Assert.That(boiler.SoloValveLatched, Is.True);
            Vector3 valvePosition = bootstrap.Players[0].transform.position;
            yield return SoloWalkToControl(RepairControlKind.Panel);
            Assert.That(HorizontalDistance(valvePosition, bootstrap.Players[0].transform.position), Is.GreaterThan(4));
            yield return SoloUseControl(RepairControlKind.Panel, RepairStep.Breaker);
            yield return SoloWalkToControl(RepairControlKind.Breaker);
            yield return SoloUseControl(RepairControlKind.Breaker, RepairStep.LatchA);
            yield return SoloWalkToControl(RepairControlKind.LatchA);
            yield return SoloUseControl(RepairControlKind.LatchA, RepairStep.LatchB);
            yield return SoloWalkToControl(RepairControlKind.LatchB);
            yield return SoloUseControl(RepairControlKind.LatchB, RepairStep.Restart);
            yield return SoloWalkToControl(RepairControlKind.Restart);
            yield return SoloUseControl(RepairControlKind.Restart, RepairStep.Complete);
            Assert.That(boiler.Failed, Is.False);
            Assert.That(boiler.SoloLatchSecondsRemaining, Is.Zero);
            Assert.That(bootstrap.Players[1], Is.Null);

            // A fresh catch must not survive a local menu pause or model replacement.
            boiler.ForceFailure();
            yield return HoldPhysicalRelief();
            yield return WaitForCondition(() => boiler.SoloValveLatched, 22, "Second genuine relief hold arms a new catch");
            bootstrap.SetPaused(true); yield return null;
            Assert.That(boiler.SoloValveLatched, Is.False);
            Assert.That(Control(RepairControlKind.ReliefValve).HoldingActor, Is.Null);
            QueueUse(padA, false); bootstrap.SetPaused(false);
            session.NewGame(); yield return null;
            Assert.That(session.Simulation.Boiler.SoloAssistEnabled, Is.True);
            Assert.That(session.Simulation.Boiler.SoloValveLatched, Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        IEnumerator SoloWalkToControl(RepairControlKind kind)
        {
            var actor = bootstrap.Players[0];
            Vector3 target = Control(kind).transform.position;
            Vector3 destination = new Vector3(target.x, actor.transform.position.y, target.z - 1.45f);
            float deadline = Time.realtimeSinceStartup + 4;
            while (HorizontalDistance(actor.transform.position, destination) > .07f && Time.realtimeSinceStartup < deadline)
            {
                Vector3 delta = destination - actor.transform.position; delta.y = 0;
                Vector3 direction = actor.transform.InverseTransformDirection(delta.normalized);
                float axis = Mathf.Clamp(delta.magnitude * 2, .25f, 1);
                InputSystem.QueueStateEvent(padA, new GamepadState { leftStick = new Vector2(direction.x, direction.z) * axis });
                yield return null;
            }
            QueueUse(padA, false); yield return null;
            Assert.That(HorizontalDistance(actor.transform.position, destination), Is.LessThan(.15f), "Physical utility lane blocked at " + kind);
            // Only real right-stick look and the current raycast may acquire the next control.
            deadline = Time.realtimeSinceStartup + 3;
            while (Time.realtimeSinceStartup < deadline)
            {
                Vector3 delta = target - actor.PlayerCamera.transform.position;
                float yaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
                float pitch = -Mathf.Atan2(delta.y, new Vector2(delta.x, delta.z).magnitude) * Mathf.Rad2Deg;
                float x = Mathf.DeltaAngle(actor.transform.eulerAngles.y, yaw);
                float y = Mathf.DeltaAngle(actor.PlayerCamera.transform.localEulerAngles.x, pitch);
                if (Mathf.Abs(x) < 1.5f && Mathf.Abs(y) < 1.5f) break;
                InputSystem.QueueStateEvent(padA, new GamepadState { rightStick = new Vector2(SoloLook(x), -SoloLook(y)) });
                yield return null;
            }
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That(actor.Interactor.Focused, Is.SameAs(Control(kind)), "Real raycast must acquire " + kind);
        }

        static float SoloLook(float error) => Mathf.Abs(error) < 1.5f ? 0 : Mathf.Sign(error) * Mathf.Clamp(Mathf.Abs(error) / 45, .22f, .7f);

        IEnumerator SoloUseControl(RepairControlKind kind, RepairStep expected)
        {
            QueueUse(padA, true);
            yield return WaitForCondition(() => RepairController.Step == expected, 3.5f, "Solo control " + kind);
            QueueUse(padA, false); yield return null; yield return null;
        }
    }
}
