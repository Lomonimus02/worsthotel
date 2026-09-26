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
        public IEnumerator EnvironmentFeedbackFollowsRealDeliveryWarningComplaintPauseAndFreshSession()
        {
            StartQuietWaitTestShift();
            var session = GameSession.Instance;
            var simulation = session.Simulation;
            var heater = Object.FindAnyObjectByType<PortableHeater>();
            var heaterAudio = heater.GetComponent<PortableHeaterAudio>();
            var feedback = Object.FindAnyObjectByType<HotelFeedback>();
            var panel = Object.FindAnyObjectByType<ElectricalPanelPresentation>();
            var radiator = Object.FindObjectsByType<RadiatorHeatFeedback>(FindObjectsSortMode.None).Single(view => view.roomId == 105);
            Assert.That(heaterAudio, Is.Not.Null); Assert.That(feedback.phoneReceiver, Is.Not.Null);
            Assert.That(radiator.fins.Length, Is.GreaterThanOrEqualTo(8));
            var fan = heater.GetComponents<AudioSource>().Single(source => source.clip != null && source.clip.name == "Original portable heater fan");
            var fanClip = fan.clip;
            int sourcesBefore = feedback.GetComponentsInChildren<AudioSource>(true).Length;
            var block = new MaterialPropertyBlock();
            yield return null; yield return null;
            radiator.fins[0].GetPropertyBlock(block);
            Color warmRadiator = block.GetColor("_BaseColor");
            var bLight = panel.roomLights.Single(binding => binding.roomId == 105).lights[0];
            var aLight = panel.roomLights.Single(binding => binding.roomId == 101).lights[0];
            float bBase = bLight.intensity, aBase = aLight.intensity;

            // Diagnostic placement isolates feedback from the separately tested carry interaction.
            // Player1 stays in the lobby; only Player2 is close enough to hear the working tool.
            heater.Body.position = new Vector3(-4, .04f, 25.5f);
            heater.Body.rotation = Quaternion.identity;
            heater.Body.linearVelocity = heater.Body.angularVelocity = Vector3.zero;
            var near = new GameObject("Second staff heater-listening position");
            near.transform.position = new Vector3(-3.3f, .08f, 25.5f);
            bootstrap.Players[1].ResetToSpawn(near.transform); Object.Destroy(near);
            yield return WaitForCondition(() => heater.State != null && heater.State.RoomId == 105, 4,
                "Physical heater placement was not recognised by its whole-body room volume.");
            Assert.That(Vector3.Distance(bootstrap.Players[0].PlayerCamera.transform.position, heater.transform.position), Is.GreaterThan(heaterAudio.audibleRange));
            Assert.That(session.SetHeaterSwitch(1, heater.heaterId, true).Success, Is.True);
            yield return WaitForCondition(() => fan.volume > .02f, 2, "Player2 proximity must make the actually powered fan audible in the shared mix.");
            Assert.That(heater.State.EffectiveHeatOutput, Is.GreaterThan(0));
            Assert.That(heater.GetPrompt(bootstrap.Players[1].Interactor), Does.Contain("manual heat"));
            Assert.That(heater.GetPrompt(bootstrap.Players[1].Interactor).Split('\n'), Has.Length.EqualTo(2));
            simulation.Clock.SetSpeed(8);
            yield return null; yield return null;
            Assert.That(fan.pitch, Is.EqualTo(1), "WAIT must not pitch-shift the heater fan.");
            simulation.Clock.SetSpeed(1);
            bootstrap.SetPaused(true);
            float pausedVolume = fan.volume;
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(fan.volume, Is.EqualTo(pausedVolume));
            Assert.That(fan.pitch, Is.EqualTo(1));
            bootstrap.SetPaused(false);
            yield return null; yield return null;

            // Explicit F2-equivalent overload setup; the panel must identify it as an override.
            // The warning, trip, light power and heater shutdown still follow the production circuit.
            Assert.That(simulation.DebugSetCircuitLoad("B", 5).Success, Is.True);
            session.AdvanceTime(simulation.Electrical.Settings.WarningSeconds + .2f);
            yield return null; yield return null;
            var circuit = simulation.Electrical.Find("B");
            Assert.That(circuit.Warning && !circuit.Tripped, Is.True);
            Assert.That(panel.circuits.Single(view => view.circuitId == "B").readout.text, Does.Contain("OVERRIDE").And.Contain("ACTUAL"));
            float lowest = float.MaxValue, highest = 0;
            float sampleUntil = Time.realtimeSinceStartup + 1.35f;
            while (Time.realtimeSinceStartup < sampleUntil)
            {
                yield return null;
                lowest = Mathf.Min(lowest, bLight.intensity); highest = Mathf.Max(highest, bLight.intensity);
                Assert.That(bLight.enabled, Is.True, "A warning only dims; it cannot pretend to be a power cut.");
                Assert.That(aLight.intensity, Is.EqualTo(aBase).Within(.001f));
            }
            Assert.That(lowest, Is.GreaterThanOrEqualTo(bBase * .815f));
            Assert.That(highest, Is.LessThanOrEqualTo(bBase * 1.005f));
            Assert.That(highest - lowest, Is.GreaterThan(bBase * .02f));
            session.AdvanceTime(simulation.Electrical.Settings.TripSeconds - circuit.OverloadSeconds + .2f);
            yield return null; yield return null;
            Assert.That(circuit.Tripped, Is.True);
            Assert.That(heater.State.EffectiveHeatOutput, Is.Zero);
            Assert.That(fan.volume, Is.Zero, "Power loss must stop fan delivery immediately.");
            Assert.That(bLight.enabled, Is.False); Assert.That(aLight.enabled, Is.True);
            panel.enabled = false; yield return null;
            Assert.That(bLight.enabled, Is.False, "Disabling presentation must never repair a real tripped circuit.");
            panel.enabled = true;
            Assert.That(simulation.DebugSetCircuitLoad("B", null).Success, Is.True);
            Assert.That(session.ResetCircuit(1, "B").Success, Is.True);
            yield return WaitForCondition(() => fan.volume > .02f && bLight.enabled, 2, "Cleared overload and real reset must restore the powered fan.");
            Assert.That(bLight.intensity, Is.EqualTo(bBase).Within(.001f));

            simulation.Boiler.ForceFailure(); // Deliberate thermal feedback fixture, not a natural balance trace.
            session.RaiseChanged(); yield return null; yield return null;
            radiator.fins[0].GetPropertyBlock(block);
            Color coldRadiator = block.GetColor("_BaseColor");
            Assert.That(coldRadiator.r, Is.LessThan(warmRadiator.r - .1f),
                "Central radiator feedback must cool even while a separate electric heater still delivers local heat.");
            Assert.That(heater.State.EffectiveHeatOutput, Is.GreaterThan(0));
            Assert.That(fan.volume, Is.GreaterThan(0));
            heaterAudio.enabled = false; yield return null;
            Assert.That(fan.volume, Is.Zero);
            heaterAudio.enabled = true;
            yield return WaitForCondition(() => fan.volume > .02f, 2, "Re-enabling feedback must reuse the current real tool state.");
            Assert.That(fan.clip, Is.SameAs(fanClip));

            // The phone rings for Complaint, never merely Observed. Setup supplies one checked-in
            // guest; actual need accumulation and situation thresholds supply the notification.
            var guest = simulation.Guests.First();
            if (simulation.Elapsed < guest.Agent.ArrivalTime) session.AdvanceTime(guest.Agent.ArrivalTime - simulation.Elapsed + .2f);
            if (guest.Agent.State == GuestAgentState.Arriving) Assert.That(session.ReportGuestReachedReception(guest.GuestId).Success, Is.True);
            Assert.That(CheckInWithModelKeyFixture(session, 0, guest.GuestId).Success, Is.True);
            Assert.That(session.ReportGuestReachedRoom(guest.GuestId).Success, Is.True);
            Assert.That(simulation.SetRoomTemperature(guest.RoomId, 8).Success, Is.True);
            session.AdvanceTime(.2f);
            Assert.That(feedback.ReceptionPhoneRinging, Is.False);
            session.AdvanceTime(simulation.NeedsSettings.ComplaintExposureSeconds + 7);
            Assert.That(simulation.Incidents.Items.Any(incident => incident.GuestId == guest.GuestId && incident.Stage >= SituationStage.Complaint), Is.True);
            Assert.That(feedback.ReceptionPhoneRinging, Is.True, "The actual complaint event must activate the physical reception phone.");
            feedback.enabled = false;
            Assert.That(feedback.ReceptionPhoneRinging, Is.False);
            feedback.enabled = true; yield return null;
            Assert.That(feedback.ReceptionPhoneRinging, Is.False, "Re-enable must not replay old complaints.");

            session.NewGame();
            yield return null; yield return null; yield return null;
            Assert.That(fan.volume, Is.Zero);
            Assert.That(heater.State.SwitchedOn, Is.False);
            Assert.That(feedback.ReceptionPhoneRinging, Is.False);
            Assert.That(bLight.enabled && aLight.enabled, Is.True);
            Assert.That(bLight.intensity, Is.EqualTo(bBase).Within(.001f));
            radiator.fins[0].GetPropertyBlock(block);
            Assert.That(block.GetColor("_BaseColor").r, Is.GreaterThan(coldRadiator.r + .1f));
            Assert.That(fan.clip, Is.SameAs(fanClip), "NewGame must not allocate duplicate fan clips.");
            Assert.That(feedback.GetComponentsInChildren<AudioSource>(true).Length, Is.EqualTo(sourcesBefore));
            LogAssert.NoUnexpectedReceived();
        }
    }
}
