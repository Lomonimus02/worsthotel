using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace WorstHotel.Tests
{
    public sealed partial class Phase1PlayModeTests
    {
        private WaitController Waiter => GameSession.Instance.Wait;
        private float WaitHoldSeconds => WaitConfig.Resolve(Waiter.config).HoldSeconds;
        private SessionConfig waitScenarioSessionConfig;
        private LivingHotelConfig waitScenarioLivingConfig;

        private void StartQuietWaitTestShift()
        {
            var session = GameSession.Instance;
            Assert.That(session.config.living, Is.Not.Null);
            waitScenarioSessionConfig = UnityEngine.Object.Instantiate(session.config);
            waitScenarioLivingConfig = UnityEngine.Object.Instantiate(session.config.living);
            // Isolate consent/cancellation from unrelated arrivals. This remains a real living hotel;
            // separate guest integration tests exercise WAIT stopping on its actual first arrival.
            waitScenarioLivingConfig.firstArrivalSeconds = 60;
            waitScenarioLivingConfig.arrivalJitterSeconds = 0;
            waitScenarioSessionConfig.living = waitScenarioLivingConfig;
            session.config = waitScenarioSessionConfig;
            session.NewGame();
            StartOrdinaryTestShift();
            Assert.That(session.Simulation.LivingEnabled, Is.True);
        }

        [UnityTearDown]
        public IEnumerator DestroyWaitScenarioConfiguration()
        {
            if (waitScenarioSessionConfig) UnityEngine.Object.Destroy(waitScenarioSessionConfig);
            if (waitScenarioLivingConfig) UnityEngine.Object.Destroy(waitScenarioLivingConfig);
            waitScenarioSessionConfig = null;
            waitScenarioLivingConfig = null;
            yield return null;
        }

        private static void QueueWait(Gamepad pad, bool held, Vector2 move = default, Vector2 look = default, bool use = false)
        {
            var state = new GamepadState { leftStick = move, rightStick = look };
            if (held) state = state.WithButton(GamepadButton.North);
            if (use) state = state.WithButton(GamepadButton.South);
            InputSystem.QueueStateEvent(pad, state);
        }

        private IEnumerator ReleaseWaitButtons()
        {
            QueueWait(padA, false); QueueWait(padB, false);
            yield return null; yield return null;
        }

        private IEnumerator ConsentToWait()
        {
            yield return ReleaseWaitButtons();
            QueueWait(padA, true); QueueWait(padB, true);
            yield return WaitForCondition(() => Waiter.IsWaiting, WaitHoldSeconds + 1,
                "Two independent stationary staff should be allowed to wait: " + Waiter.Reason);
            Assert.That(Waiter.HasVoted(0) && Waiter.HasVoted(1), Is.True);
        }

        [UnityTest]
        public IEnumerator WaitNeedsBothIndependentVotesAndAcceleratesOnlyHotelTime()
        {
            StartQuietWaitTestShift();
            Assert.That(Waiter, Is.Not.Null, "The generated scene must bind WaitController on GameSession.");
            yield return ReleaseWaitButtons();
            float physicsStep = Time.fixedDeltaTime;
            QueueWait(padA, true);
            yield return new WaitForSecondsRealtime(WaitHoldSeconds + 0.15f);
            Assert.That(Waiter.HasVoted(0), Is.True);
            Assert.That(Waiter.HasVoted(1), Is.False);
            Assert.That(Waiter.IsWaiting, Is.False, "One pad cannot authorize both actors.");
            Assert.That(GameSession.Instance.Simulation.Clock.Speed, Is.EqualTo(1));
            QueueWait(padB, true);
            yield return new WaitForSecondsRealtime(WaitHoldSeconds * 0.3f);
            Assert.That(Waiter.IsWaiting, Is.False, "The second hold must also reach its duration.");
            yield return WaitForCondition(() => Waiter.IsWaiting, WaitHoldSeconds + 1, "Second actor consent was not registered.");
            var clock = GameSession.Instance.Simulation.Clock;
            Assert.That(clock.Speed, Is.EqualTo(WaitConfig.Resolve(Waiter.config).Speed));
            Assert.That(bootstrap.Players[0].Input.WaitHeld && bootstrap.Players[1].Input.WaitHeld, Is.True);
            Assert.That(bootstrap.Players[0].Input.WaitLabel, Is.EqualTo("Y / Triangle"));
            float simulationStart = clock.SimulationTime, realStart = Time.realtimeSinceStartup;
            yield return new WaitForSecondsRealtime(0.35f);
            float realDuration = Time.realtimeSinceStartup - realStart;
            Assert.That(clock.SimulationTime - simulationStart, Is.GreaterThan(realDuration * 4),
                "Simulation time should advance faster than elapsed real time.");
            Assert.That(Time.timeScale, Is.EqualTo(1), "WAIT must never accelerate Unity rigidbody time.");
            Assert.That(Time.fixedDeltaTime, Is.EqualTo(physicsStep), "WAIT must not change the physics step.");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator MeaningfulEventStopsImmediatelyAndHeldVotesCannotSkipNextEvent()
        {
            StartQuietWaitTestShift();
            yield return ConsentToWait();
            var simulation = GameSession.Instance.Simulation;
            simulation.SignalEvent("Test guest reached a meaningful activity");
            Waiter.ObserveSimulationEvents();
            Assert.That(simulation.Clock.Speed, Is.EqualTo(1), "The per-tick observer must stop synchronously, before the next accelerated tick.");
            Assert.That(Waiter.Reason, Does.Contain("meaningful activity"));
            Assert.That(Waiter.HasVoted(0) || Waiter.HasVoted(1), Is.False);
            yield return new WaitForSecondsRealtime(WaitHoldSeconds + 0.15f);
            Assert.That(Waiter.IsWaiting, Is.False, "Continuously held WAIT must not consume the next event.");

            QueueWait(padA, false); yield return null; yield return null;
            QueueWait(padA, true);
            yield return new WaitForSecondsRealtime(WaitHoldSeconds + 0.15f);
            Assert.That(Waiter.HasVoted(0), Is.True);
            Assert.That(Waiter.HasVoted(1), Is.False, "Each actor must release their own physical button.");
            Assert.That(Waiter.IsWaiting, Is.False);
            QueueWait(padB, false); yield return null; yield return null;
            QueueWait(padB, true);
            yield return WaitForCondition(() => Waiter.IsWaiting, WaitHoldSeconds + 1, "Both fresh votes should allow the next quiet interval.");

            QueueWait(padB, true, move: Vector2.right);
            yield return null; yield return null;
            Assert.That(Waiter.IsWaiting, Is.False, "Either actor's movement cancels WAIT.");
            Assert.That(Waiter.HasVoted(0) || Waiter.HasVoted(1), Is.False);
            yield return ConsentToWait();
            QueueWait(padA, true, look: Vector2.left);
            yield return null; yield return null;
            Assert.That(Waiter.IsWaiting, Is.False, "Either actor's look cancels WAIT before hotel Update.");
            Assert.That(Time.timeScale, Is.EqualTo(1));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator CarriedSuitcaseAndRealRepairHoldPreventWaiting()
        {
            StartQuietWaitTestShift();
            yield return PrepareRealSuitcase();
            Assert.That(bootstrap.Players[0].Interactor.HeldBody, Is.SameAs(suitcaseBody));
            QueueWait(padA, true); QueueWait(padB, true);
            yield return new WaitForSecondsRealtime(WaitHoldSeconds + 0.15f);
            Assert.That(Waiter.IsWaiting, Is.False);
            Assert.That(Waiter.Reason, Does.Contain("carried"));
            Assert.That(Waiter.HasVoted(0) || Waiter.HasVoted(1), Is.False);
            bootstrap.Players[0].Interactor.ReleaseGrab();
            yield return ConsentToWait();
            QueueWait(padA, true, use: true);
            yield return null; yield return null;
            Assert.That(Waiter.IsWaiting, Is.False, "Primary hold must block before PlayerInteractor starts that frame's action.");
            Assert.That(Waiter.Reason, Does.Contain("interaction"));

            yield return ReleaseWaitButtons();
            GameSession.Instance.Simulation.Boiler.ForceFailure(); // Explicit repair scenario setup.
            yield return HoldPhysicalRelief();
            Assert.That(Control(RepairControlKind.ReliefValve).HoldingActor, Is.SameAs(bootstrap.Players[0].Interactor));
            QueueWait(padA, true, use: true); QueueWait(padB, true);
            yield return new WaitForSecondsRealtime(WaitHoldSeconds + 0.15f);
            Assert.That(GameSession.Instance.Simulation.Boiler.ReliefActorId, Is.EqualTo(0));
            Assert.That(Waiter.IsWaiting, Is.False, "A real authenticated repair hold cannot coexist with WAIT.");
            Assert.That(GameSession.Instance.Simulation.Clock.Speed, Is.EqualTo(1));
            yield return ReleaseWaitButtons();
            QueueWait(padA, true); QueueWait(padB, true);
            yield return new WaitForSecondsRealtime(WaitHoldSeconds + 0.15f);
            Assert.That(Waiter.IsWaiting, Is.False, "Releasing the valve does not make a failed boiler safe to skip.");
            Assert.That(Waiter.Reason, Does.Contain("failed boiler"));
            Assert.That(Time.timeScale, Is.EqualTo(1));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator PauseAndControllerLossClearConsentAndReturnHotelClockToNormal()
        {
            StartQuietWaitTestShift();
            yield return ConsentToWait();
            bootstrap.SetPaused(true);
            yield return null; yield return null;
            Assert.That(Waiter.IsWaiting, Is.False);
            Assert.That(Waiter.HasVoted(0) || Waiter.HasVoted(1), Is.False);
            Assert.That(GameSession.Instance.Simulation.Clock.Speed, Is.EqualTo(1));
            Assert.That(Time.timeScale, Is.Zero, "The existing pause still pauses normal physics.");
            bootstrap.SetPaused(false);
            yield return new WaitForSecondsRealtime(WaitHoldSeconds + 0.15f);
            Assert.That(Waiter.IsWaiting, Is.False, "Unpausing cannot reuse the buttons held before pause.");
            yield return ConsentToWait();
            InputSystem.RemoveDevice(padB);
            yield return null; yield return null;
            Assert.That(bootstrap.WaitingForDevices && bootstrap.IsPaused, Is.True);
            Assert.That(Waiter.IsWaiting, Is.False);
            Assert.That(Waiter.HasVoted(0) || Waiter.HasVoted(1), Is.False);
            Assert.That(GameSession.Instance.Simulation.Clock.Speed, Is.EqualTo(1));
            padB = InputSystem.AddDevice<Gamepad>("WaitReconnectedStaffB");
            yield return null; yield return null;
            Assert.That(bootstrap.IsPaused, Is.False);
            Assert.That(Waiter.IsWaiting, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1));
            LogAssert.NoUnexpectedReceived();
        }
    }
}
