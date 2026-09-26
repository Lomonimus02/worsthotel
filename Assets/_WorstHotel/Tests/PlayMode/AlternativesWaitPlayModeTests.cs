using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace WorstHotel.Tests
{
    public sealed partial class Phase1PlayModeTests
    {
        [UnityTest]
        public IEnumerator DeliberatelyAcceptedFailureAllowsFreshTwoPlayerWaitWithoutRestoringHeat()
        {
            StartQuietWaitTestShift();
            var simulation = GameSession.Instance.Simulation;
            simulation.Boiler.ForceFailure(); // Explicit diagnostic setup, not a natural failure trace.
            yield return ReleaseWaitButtons();
            QueueWait(padA, true); QueueWait(padB, true);
            yield return new WaitForSecondsRealtime(WaitHoldSeconds + .15f);
            Assert.That(Waiter.IsWaiting, Is.False);
            Assert.That(Waiter.Reason, Does.Contain("failed boiler"));
            Assert.That(simulation.AcceptBoilerConsequences(9).Success, Is.False);
            float failedOutput = simulation.Boiler.HeatingOutput;
            Assert.That(GameSession.Instance.AcceptBoilerConsequences(1).Success, Is.True);
            Assert.That(simulation.BoilerFailureAcknowledged, Is.True);
            Assert.That(simulation.Boiler.Failed, Is.True);
            Assert.That(simulation.Boiler.HeatingOutput, Is.EqualTo(failedOutput));
            yield return ConsentToWait();
            Assert.That(Time.timeScale, Is.EqualTo(1));
            simulation.SignalEvent("A different situation needs attention");
            Waiter.ObserveSimulationEvents();
            Assert.That(Waiter.IsWaiting, Is.False, "Accepting one consequence cannot skip later events.");
            Assert.That(Waiter.Reason, Does.Contain("different situation"));
            GameSession.Instance.NewGame();
            Assert.That(GameSession.Instance.Simulation.BoilerFailureAcknowledged, Is.False);
            Assert.That(Waiter.IsWaiting, Is.False);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
