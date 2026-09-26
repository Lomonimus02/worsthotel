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
        public IEnumerator TwoAuthoredHeatersOverloadProductionCircuitAndActualResetRetripsUntilOneIsSwitchedOff()
        {
            var session = GameSession.Instance;
            // LABELLED MODEL FIXTURE: one quiet checked-in guest in 104, with a long stay.
            // This test verifies real device switches, cover and breaker, not guest navigation.
            // Electrical/heater tuning is the production asset; no load/capacity override is used.
            waitScenarioSessionConfig = Object.Instantiate(session.config);
            waitScenarioSessionConfig.serviceSeconds = 3000;
            waitScenarioLivingConfig = Object.Instantiate(session.config.living);
            waitScenarioLivingConfig.firstArrivalSeconds = .2f;
            waitScenarioLivingConfig.arrivalJitterSeconds = 0;
            waitScenarioLivingConfig.firstActivityDelay = 1000;
            waitScenarioLivingConfig.activityDurationMin = 1000;
            waitScenarioLivingConfig.activityDurationMax = 1000;
            waitScenarioSessionConfig.living = waitScenarioLivingConfig;
            session.config = waitScenarioSessionConfig;
            session.NewGame(); ManagementUI.Instance.Close();
            Object.FindAnyObjectByType<GuestPresentation>().enabled = false;
            var offer = session.Plan.Applications.First();
            int min = session.Economy.MinPrice, step = session.Economy.PriceStep;
            session.Assign(0, offer.Id, 104, min + Mathf.RoundToInt((offer.ReferencePrice - min) / (float)step) * step);
            session.CommitPlan(0);
            var simulation = session.Simulation;
            var guest = simulation.Guests.Single();
            session.AdvanceTime(guest.Agent.ArrivalTime + .2f);
            Assert.That(session.ReportGuestReachedReception(guest.GuestId).Success, Is.True);
            Assert.That(CheckInWithModelKeyFixture(session, 0, guest.GuestId).Success, Is.True);
            Assert.That(session.ReportGuestReachedRoom(guest.GuestId).Success, Is.True);
            Assert.That(simulation.ForceActivity(guest.GuestId, GuestActivity.QuietRest).Success, Is.True);
            yield return null; yield return null;

            var heaters = Object.FindObjectsByType<PortableHeater>(FindObjectsSortMode.None).OrderBy(item => item.heaterId).ToArray();
            Assert.That(heaters.Select(item => item.heaterId), Is.EqualTo(new[] { "portable-heater-1", "portable-heater-2" }));
            Assert.That(heaters[0].Body, Is.Not.SameAs(heaters[1].Body));
            // LABELLED WORLD PLACEMENT: both existing bodies start in the clear inner lane.
            // No heater is instantiated and this arrangement makes no shelf-to-room carry claim.
            // Empty employee approaches below are also fixture placement; look/use is real input.
            for (int index = 0; index < heaters.Length; index++)
            {
                heaters[index].Body.position = new Vector3(3.85f, .04f, index == 0 ? 18.6f : 16.0f);
                heaters[index].Body.rotation = Quaternion.identity;
                heaters[index].Body.linearVelocity = heaters[index].Body.angularVelocity = Vector3.zero;
            }
            // Room detection reads physics bounds, whereas an interpolated Transform can still
            // show the old shelf pose for a frame after the labelled Rigidbody placement.
            // Wait for both representations before deriving an empty employee approach.
            yield return WaitForCondition(() => heaters.All(item => item.State != null && item.State.RoomId == 104 &&
                Vector3.Distance(item.transform.TransformPoint(item.placementCollider.center), item.placementCollider.bounds.center) < .05f), 3,
                "Both authored heater bodies and interpolated poses must reach room104 before aiming at their real colliders.");
            var panel = Object.FindAnyObjectByType<ElectricalPanelPresentation>();
            var circuit = simulation.Electrical.Find("B");
            var otherCircuit = simulation.Electrical.Find("A");
            var view = panel.circuits.Single(item => item.circuitId == "B");
            Assert.That(circuit.Capacity, Is.EqualTo(4f));
            Assert.That(simulation.Electrical.Settings.OccupiedRoomLoad, Is.EqualTo(.85f));
            Assert.That(circuit.LoadOverride, Is.Null);
            Assert.That(circuit.RequestedLoad, Is.EqualTo(.85f).Within(.001f));
            Assert.That(heaters.All(item => !item.State.SwitchedOn), Is.True);

            for (int index = 0; index < heaters.Length; index++)
            {
                var heater = heaters[index];
                yield return UseElectricalServiceControl(heater, heater.placementCollider.bounds.center);
                Assert.That(heater.State.SwitchedOn && heater.State.Powered, Is.True);
                Assert.That(circuit.RequestedLoad, Is.EqualTo(.85f + (index + 1) * 2f).Within(.001f));
            }
            var consumers = simulation.Electrical.Consumers.Where(item => item.Id.StartsWith("heater:") && item.CircuitId == "B").ToArray();
            Assert.That(consumers, Has.Length.EqualTo(2));
            Assert.That(consumers.All(item => item.RoomId == 104 && Mathf.Abs(item.RequestedLoad - 2f) < .001f), Is.True);
            Assert.That(view.consumers.text.Split('\n').Count(line => line.Contains("HEATER 104")), Is.EqualTo(2),
                "The physical panel must list both actual heaters as the source of the excess load.");
            Assert.That(circuit.ActualRequestedLoad, Is.EqualTo(4.85f).Within(.001f));
            float tripSeconds = simulation.Electrical.Settings.TripSeconds;
            // Diagnostic clock advance exercises the normal overload timer; it does not force a trip.
            session.AdvanceTime(simulation.Electrical.Settings.WarningSeconds + .25f);
            yield return null; yield return null;
            Assert.That(circuit.Warning && !circuit.Tripped, Is.True);
            Assert.That(view.readout.text, Does.Contain("OVERLOAD"));
            session.AdvanceTime(tripSeconds - circuit.OverloadSeconds + .25f);
            yield return null; yield return null;
            Assert.That(circuit.Tripped, Is.True);
            Assert.That(circuit.RequestedLoad, Is.EqualTo(4.85f).Within(.001f));
            Assert.That(circuit.DeliveredLoad, Is.Zero);
            Assert.That(heaters.All(item => item.State.SwitchedOn && !item.State.Powered && item.State.EffectiveHeatOutput == 0), Is.True);
            Assert.That(heaters.All(item => item.statusLabel.text.Contains("NO POWER")), Is.True);
            Assert.That(view.consumers.text.Split('\n').Count(line => line.Contains("HEATER 104")), Is.EqualTo(2));
            foreach (var binding in panel.roomLights)
                Assert.That(binding.lights.All(light => light.enabled), Is.EqualTo(binding.roomId <= 103));
            Assert.That(otherCircuit.HasPower && otherCircuit.TripCount == 0, Is.True);

            // Open the actual blocking cover through input, from outside its hinge sweep.
            Vector3 coverAim = panel.cover.transform.TransformPoint(new Vector3(1.10f, 0, 0));
            yield return PositionEmptyActorForLinen(0, new Vector3(coverAim.x, .08f, coverAim.z - 2.7f), coverAim);
            yield return AimAtKeyScenarioPoint(bootstrap.Players[0], padA, () => coverAim);
            Assert.That(bootstrap.Players[0].Interactor.Focused, Is.SameAs(panel.cover));
            QueueUse(padA, true); yield return null; yield return null;
            QueueUse(padA, false);
            yield return WaitForCondition(() => panel.cover.IsPassageOpen, 2, "The real hinged panel cover must clear its breakers.");
            var breaker = panel.GetComponentsInChildren<ElectricalBreakerControl>().Single(item => item.circuitId == "B");
            yield return UseElectricalServiceControl(breaker, breaker.transform.position);
            Assert.That(circuit.HasPower && !circuit.Tripped, Is.True, "Short use of the real reset lever must immediately restore power.");
            Assert.That(heaters.All(item => item.State.Powered), Is.True);
            Assert.That(circuit.RequestedLoad, Is.EqualTo(4.85f).Within(.001f));
            int previousTrips = circuit.TripCount;
            session.AdvanceTime(tripSeconds + .25f);
            yield return null; yield return null;
            Assert.That(circuit.Tripped, Is.True, "Reset cannot remove demand from either still-on heater.");
            Assert.That(circuit.TripCount, Is.EqualTo(previousTrips + 1));
            Assert.That(heaters.All(item => !item.State.Powered), Is.True);

            yield return UseElectricalServiceControl(heaters[1], heaters[1].placementCollider.bounds.center);
            Assert.That(heaters[1].State.SwitchedOn, Is.False);
            Assert.That(heaters[0].State.SwitchedOn, Is.True, "Only the touched heater's switch changes.");
            Assert.That(circuit.RequestedLoad, Is.EqualTo(2.85f).Within(.001f));
            Assert.That(circuit.Tripped, Is.True, "Removing load still requires a real breaker reset.");
            yield return UseElectricalServiceControl(breaker, breaker.transform.position);
            Assert.That(circuit.HasPower, Is.True);
            int repairedTrips = circuit.TripCount;
            session.AdvanceTime(tripSeconds * 2 + 1);
            yield return null; yield return null;
            Assert.That(circuit.HasPower && !circuit.Warning && !circuit.Tripped, Is.True);
            Assert.That(circuit.TripCount, Is.EqualTo(repairedTrips));
            Assert.That(circuit.LoadOverride, Is.Null);
            Assert.That(circuit.RequestedLoad, Is.EqualTo(2.85f).Within(.001f));
            Assert.That(heaters[0].State.SwitchedOn && heaters[0].State.Powered && heaters[0].State.EffectiveHeatOutput > 0, Is.True);
            Assert.That(heaters[1].State.SwitchedOn, Is.False);
            Assert.That(heaters[1].State.EffectiveHeatOutput, Is.Zero);
            foreach (var binding in panel.roomLights) Assert.That(binding.lights.All(light => light.enabled), Is.True);
            Assert.That(otherCircuit.HasPower && otherCircuit.TripCount == 0, Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        IEnumerator UseElectricalServiceControl(HotelInteractable control, Vector3 aim)
        {
            Assert.That(bootstrap.Players[0].Interactor.HeldBody, Is.Null);
            yield return FaceStation(bootstrap.Players[0], padA, control, aim);
            QueueUse(padA, true); yield return null; yield return null;
            QueueUse(padA, false); yield return null; yield return null;
        }
    }
}
