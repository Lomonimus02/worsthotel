using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace WorstHotel.Tests
{
    public sealed partial class Phase1PlayModeTests
    {
        [UnityTest]
        public IEnumerator ElectricalWarningAndTripEachStopFreshIndependentWaitConsent()
        {
            StartQuietWaitTestShift();
            var simulation = GameSession.Instance.Simulation;
            // Explicit diagnostic load; ordinary three-guest/heater capacity is covered separately.
            const string source = "wait-electrical-diagnostic";
            Assert.That(simulation.Heaters.Register(source, new HeaterSettings(14, 5)).Success, Is.True);
            Assert.That(simulation.Heaters.AssignRoom(source, 104).Success, Is.True);
            Assert.That(simulation.Heaters.SetSwitchedOn(source, true).Success, Is.True);
            simulation.RefreshElectrical();
            var circuit = simulation.Electrical.Find("B");
            float physicsStep = Time.fixedDeltaTime;
            yield return ConsentToWait();
            yield return WaitForCondition(() => !Waiter.IsWaiting, 3, "Electrical overload warning did not end WAIT.");
            Assert.That(circuit.Warning, Is.True);
            Assert.That(circuit.Tripped, Is.False, "A single accelerated batch must stop at the warning before the later trip.");
            Assert.That(circuit.OverloadSeconds, Is.LessThan(simulation.ElectricitySettings.WarningSeconds + 1));
            Assert.That(simulation.Clock.Speed, Is.EqualTo(1));
            Assert.That(Waiter.HasVoted(0) || Waiter.HasVoted(1), Is.False);
            yield return ConsentToWait();
            yield return WaitForCondition(() => !Waiter.IsWaiting, 3, "Electrical trip did not end a fresh WAIT interval.");
            Assert.That(circuit.Tripped, Is.True);
            Assert.That(circuit.DeliveredLoad, Is.Zero);
            Assert.That(circuit.RequestedLoad, Is.EqualTo(5));
            Assert.That(simulation.Heaters.Find(source).EffectiveHeatOutput, Is.Zero);
            Assert.That(simulation.Clock.Speed, Is.EqualTo(1));
            Assert.That(Time.timeScale, Is.EqualTo(1));
            Assert.That(Time.fixedDeltaTime, Is.EqualTo(physicsStep));
            Assert.That(simulation.Heaters.Unregister(source).Success, Is.True);
            simulation.RefreshElectrical();
            LogAssert.NoUnexpectedReceived();
        }
    }
}
