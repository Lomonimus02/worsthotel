using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace WorstHotel.Tests
{
    public sealed partial class Phase1PlayModeTests
    {
        [UnityTest]
        public IEnumerator PhysicalBreakerRetripsWithHeaterLoadThenStaysPoweredAfterActualHeaterSwitchOff()
        {
            var session = GameSession.Instance;
            // Explicit diagnostic setup, not a claim of an unassisted playthrough: put three real
            // registered guests in Circuit B. All subsequent heater and breaker commands use input/raycast.
            waitScenarioSessionConfig = Object.Instantiate(session.config);
            // Keep the deliberately 1000-second source activity well before sleep/packing;
            // this physical breaker fixture is independent of natural end-of-stay scheduling.
            waitScenarioSessionConfig.serviceSeconds = 3000;
            waitScenarioLivingConfig = Object.Instantiate(session.config.living);
            waitScenarioLivingConfig.firstArrivalSeconds = .2f;
            waitScenarioLivingConfig.arrivalJitterSeconds = 0;
            waitScenarioLivingConfig.firstActivityDelay = 1000;
            waitScenarioLivingConfig.activityDurationMin = 1000;
            waitScenarioLivingConfig.activityDurationMax = 1000;
            waitScenarioSessionConfig.living = waitScenarioLivingConfig;
            session.config = waitScenarioSessionConfig; session.NewGame();
            var offers = session.Plan.Applications.Take(3).ToArray();
            Assert.That(offers, Has.Length.EqualTo(3));
            for (int i = 0; i < offers.Length; i++)
            {
                int min = session.Economy.MinPrice, step = session.Economy.PriceStep;
                int price = min + Mathf.RoundToInt((offers[i].ReferencePrice - min) / (float)step) * step;
                session.Assign(0, offers[i].Id, 104 + i, price);
            }
            session.CommitPlan(0);
            var simulation = session.Simulation;
            session.AdvanceTime(simulation.Guests.Max(guest => guest.Agent.ArrivalTime) + .2f);
            foreach (var guest in simulation.Guests)
            {
                Assert.That(session.ReportGuestReachedReception(guest.GuestId).Success, Is.True);
                Assert.That(CheckInWithModelKeyFixture(session, 0, guest.GuestId).Success, Is.True);
                Assert.That(session.ReportGuestReachedRoom(guest.GuestId).Success, Is.True);
            }
            var noisyGuest = simulation.Guests.Single(guest => guest.RoomId == 104);
            Assert.That(simulation.ForceActivity(noisyGuest.GuestId, GuestActivity.LoudRoom).Success, Is.True);
            session.RaiseChanged();
            var heater = Object.FindAnyObjectByType<PortableHeater>();
            var panel = Object.FindAnyObjectByType<ElectricalPanelPresentation>();
            Assert.That(heater, Is.Not.Null); Assert.That(panel, Is.Not.Null);
            heater.Body.position = new Vector3(3.85f, .04f, 18.6f);
            heater.Body.rotation = Quaternion.identity;
            heater.Body.linearVelocity = heater.Body.angularVelocity = Vector3.zero;
            yield return WaitForCondition(() => heater.State != null && heater.State.RoomId == 104, 3,
                "The diagnostic heater placement must be recognised from its actual room bounds.");
            var a = simulation.Electrical.Find("A");
            var b = simulation.Electrical.Find("B");
            Assert.That(a, Is.Not.Null); Assert.That(b, Is.Not.Null);
            Assert.That(b.RequestedLoad, Is.LessThan(b.Capacity), "Occupied rooms must be stable before the heater is switched on.");
            var presentation = Object.FindAnyObjectByType<GuestPresentation>();
            var room104 = presentation.roomMarkers.Single(room => room.roomId == 104);
            yield return WaitForCondition(() => room104.loudIndicator.activeSelf, 15,
                "The powered radio should visibly play after its guest reaches the authored activity pose.");
            yield return FaceStation(bootstrap.Players[0], padA, heater, heater.transform.TransformPoint(heater.placementCollider.center));
            QueueUse(padA, true);
            yield return null; yield return null;
            QueueUse(padA, false);
            yield return null; yield return null;
            Assert.That(heater.State.SwitchedOn && heater.State.Powered, Is.True);
            Assert.That(b.RequestedLoad, Is.GreaterThan(b.Capacity));
            float warningSeconds = simulation.Electrical.Settings.WarningSeconds;
            float tripSeconds = simulation.Electrical.Settings.TripSeconds;
            session.AdvanceTime(warningSeconds + .4f);
            yield return null; yield return null;
            Assert.That(b.Warning, Is.True); Assert.That(b.Tripped, Is.False, "A warning must precede the actual trip.");
            Assert.That(panel.circuits.Single(view => view.circuitId == "B").readout.text, Does.Contain("OVERLOAD"));
            session.AdvanceTime(tripSeconds - b.OverloadSeconds + .4f);
            yield return null; yield return null;
            Assert.That(b.Tripped, Is.True);
            Assert.That(b.RequestedLoad, Is.GreaterThan(b.Capacity), "A cut breaker must retain requested load for an honest reset.");
            Assert.That(b.DeliveredLoad, Is.Zero);
            Assert.That(heater.State.SwitchedOn, Is.True);
            Assert.That(heater.State.Powered, Is.False);
            Assert.That(heater.State.EffectiveHeatOutput, Is.Zero);
            Assert.That(heater.statusLabel.text, Does.Contain("NO POWER"));
            Assert.That(room104.loudIndicator.activeSelf, Is.False);
            foreach (var binding in panel.roomLights)
                Assert.That(binding.lights.All(light => light.enabled), Is.EqualTo(binding.roomId <= 103),
                    "Only the lights belonging to Circuit B should lose power.");
            Assert.That(a.Tripped, Is.False); Assert.That(a.TripCount, Is.Zero);

            // A physical cover blocks the breakers until the player opens it.
            Vector3 coverAim = panel.cover.transform.TransformPoint(new Vector3(1.10f, 0, 0));
            var coverApproach = new GameObject("Electrical cover approach outside hinge sweep");
            coverApproach.transform.SetPositionAndRotation(new Vector3(coverAim.x, .08f, coverAim.z - 2.7f), Quaternion.identity);
            bootstrap.Players[0].ResetToSpawn(coverApproach.transform);
            Object.Destroy(coverApproach);
            yield return WaitForGroundContact(bootstrap.Players[0]);
            yield return AimSuitcasePitch(() => coverAim);
            Assert.That(bootstrap.Players[0].Interactor.Focused, Is.SameAs(panel.cover));
            QueueUse(padA, true);
            yield return null; yield return null;
            QueueUse(padA, false);
            yield return WaitForCondition(() => panel.cover.IsPassageOpen, 2, "The real hinged electrical cover did not open.");
            var breaker = panel.GetComponentsInChildren<ElectricalBreakerControl>().Single(control => control.circuitId == "B");
            yield return FaceStation(bootstrap.Players[0], padA, breaker, breaker.transform.position);
            QueueUse(padA, true);
            yield return null; yield return null;
            QueueUse(padA, false);
            yield return null; yield return null;
            Assert.That(b.Tripped, Is.False, "The actual reset control must restore the circuit first.");
            Assert.That(heater.State.Powered, Is.True);
            int firstTrips = b.TripCount;
            session.AdvanceTime(tripSeconds + .5f);
            yield return null; yield return null;
            Assert.That(b.Tripped, Is.True); Assert.That(b.TripCount, Is.EqualTo(firstTrips + 1));
            Assert.That(heater.State.Powered, Is.False, "An unchanged overload must disconnect the heater again.");

            yield return FaceStation(bootstrap.Players[0], padA, heater, heater.transform.TransformPoint(heater.placementCollider.center));
            QueueUse(padA, true);
            yield return null; yield return null;
            QueueUse(padA, false);
            yield return null; yield return null;
            Assert.That(heater.State.SwitchedOn, Is.False);
            Assert.That(b.RequestedLoad, Is.LessThan(b.Capacity));
            yield return FaceStation(bootstrap.Players[0], padA, breaker, breaker.transform.position);
            QueueUse(padA, true);
            yield return null; yield return null;
            QueueUse(padA, false);
            yield return null; yield return null;
            int tripsAfterRepair = b.TripCount;
            session.AdvanceTime(tripSeconds + 1);
            yield return null; yield return null;
            Assert.That(b.HasPower, Is.True); Assert.That(b.Warning || b.Tripped, Is.False);
            Assert.That(b.TripCount, Is.EqualTo(tripsAfterRepair));
            Assert.That(heater.State.EffectiveHeatOutput, Is.Zero, "Reset must not silently turn the heater switch back on.");
            foreach (var binding in panel.roomLights) Assert.That(binding.lights.All(light => light.enabled), Is.True);
            Assert.That(room104.loudIndicator.activeSelf, Is.True);
            Assert.That(a.HasPower, Is.True); Assert.That(a.TripCount, Is.Zero);
            Assert.That(Time.timeScale, Is.EqualTo(1));
            LogAssert.NoUnexpectedReceived();
        }
    }
}
