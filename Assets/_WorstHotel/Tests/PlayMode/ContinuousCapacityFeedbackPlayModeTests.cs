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
        [UnityTest, Category("ContinuousOperations")]
        public IEnumerator ActualOwnedRoomDemandChangesBoilerHumBeforeFailureAndReducingDemandRestoresIt()
        {
            var session = GameSession.Instance; var model = session.Simulation;
            Assert.That(model.ContinuousOperations, Is.True);
            ManagementUI.Instance.Close();
            yield return PositionEmptyActorForLinen(0, new Vector3(0, .08f, 34), new Vector3(-.52f, 2.15f, 36.12f));
            session.AdvanceTime(60); // Let actual empty-building pressure settle before comparing capacity bands.
            var feedback = HotelFeedback.Instance;
            var hum = feedback.transform.Find("Boiler hum").GetComponent<AudioSource>();
            var gauge = Object.FindAnyObjectByType<BoilerReadout>();
            Assert.That(gauge.capacityReadout && gauge.warningLight, Is.True);
            feedback.SendMessage("Update");
            Assert.That(model.Boiler.CapacityBand, Is.EqualTo(CapacityBand.Comfortable));
            Assert.That(hum.volume, Is.GreaterThan(0), "The source must be audible at this physical boiler-room position.");
            float comfortablePitch = hum.pitch;

            // Explicit initial-occupancy adapter: real dated diagnostic bookings, owned room
            // keys and room-arrival acknowledgements isolate the sound from travel timing.
            // No load override, pressure assignment, forced fault or paused schedule is used.
            var kinds = new[] { GuestKind.Budget, GuestKind.ColdSensitive, GuestKind.Business,
                GuestKind.Budget, GuestKind.ColdSensitive };
            for (int index = 0; index < kinds.Length; index++)
                Assert.That(model.DebugSpawnGuest(kinds[index], 101 + index).Success, Is.True);
            session.AdvanceTime(1.4f);
            bool observedBusy = false;
            foreach (var guest in model.Guests.OrderBy(value => value.RoomId))
            {
                Assert.That(session.ReportGuestReachedReception(guest.GuestId).Success, Is.True);
                Assert.That(CheckInWithModelKeyFixture(session, 0, guest.GuestId).Success, Is.True);
                Assert.That(session.ReportGuestReachedRoom(guest.GuestId).Success, Is.True);
                if (model.HeatingDemands.Count(row => row.GuestId != null) == 4)
                {
                    observedBusy = true;
                    // The fourth actual owned-room demand reaches Busy without a load override.
                    // Busy is numeric 4 for wire compatibility, but it precedes Strained in severity.
                    Assert.That(model.Boiler.LoadOverride, Is.Null);
                    Assert.That(model.Boiler.CapacityBand, Is.EqualTo(CapacityBand.Busy));
                    Assert.That(model.Boiler.Load, Is.EqualTo(model.HeatingDemands.Sum(row => row.Total)).Within(.0001f));
                    Assert.That(model.Boiler.Pressure, Is.LessThan(session.BoilerSettings.WarningPressure));
                    string beforeBusyPresentation = JsonUtility.ToJson(model.CaptureSnapshot(909, 1));
                    feedback.SendMessage("Update"); gauge.SendMessage("Update");
                    Assert.That(JsonUtility.ToJson(model.CaptureSnapshot(909, 1)), Is.EqualTo(beforeBusyPresentation),
                        "A Busy display must not mutate demand, stress, pressure or the hotel clock.");
                    Assert.That(gauge.capacityReadout.text, Does.Contain("BUSY"));
                    Assert.That(gauge.warningLight.intensity, Is.Zero,
                        "Busy demand with normal pressure must not illuminate the strained/severe warning.");
                    Assert.That(hum.pitch, Is.EqualTo(comfortablePitch).Within(.01f),
                        "Busy alone must not add the strained hum.");
                }
            }
            Assert.That(observedBusy, Is.True, "The four-room Busy presentation check must actually run.");
            Assert.That(model.HeatingDemands.Count(row => row.GuestId != null), Is.EqualTo(5));
            Assert.That(model.Boiler.LoadOverride, Is.Null);
            Assert.That(model.Boiler.CapacityBand, Is.EqualTo(CapacityBand.Strained));
            Assert.That(model.Boiler.LoadRatio, Is.LessThan(1), "This cue precedes actual overload.");
            Assert.That(model.Boiler.Failed, Is.False);
            Assert.That(model.Boiler.Pressure, Is.LessThan(session.BoilerSettings.WarningPressure));
            string beforePresentation = JsonUtility.ToJson(model.CaptureSnapshot(909, 1));
            feedback.SendMessage("Update");
            Assert.That(JsonUtility.ToJson(model.CaptureSnapshot(909, 1)), Is.EqualTo(beforePresentation),
                "Playing the capacity cue cannot manufacture pressure, stress, faults or guest state changes.");
            float strainedPitch = hum.pitch;
            Assert.That(strainedPitch, Is.GreaterThan(comfortablePitch + .04f), "Quiet near-capacity operation needs an audible change before the pressure alarm.");
            feedback.SendMessage("Update");
            Assert.That(hum.pitch, Is.EqualTo(strainedPitch), "Repeated presentation updates must not accumulate pitch.");

            // Actual room valves remove their attributed heat demand; occupancy remains five.
            Assert.That(model.RequestStaffRoomAccess(0, 101).Success, Is.True);
            Assert.That(model.RequestStaffRoomAccess(0, 102).Success, Is.True);
            Assert.That(model.SetRadiatorSetting(0, 101, 0).Success, Is.True);
            Assert.That(model.SetRadiatorSetting(0, 102, 0).Success, Is.True);
            Assert.That(model.Boiler.CapacityBand, Is.EqualTo(CapacityBand.Comfortable));
            feedback.SendMessage("Update");
            Assert.That(hum.pitch, Is.LessThan(strainedPitch - .04f));
            Assert.That(hum.pitch, Is.EqualTo(comfortablePitch).Within(.01f));
            Assert.That(model.Boiler.Failed, Is.False);
            Assert.That(model.Guests.Count(guest => guest.Agent.CheckedIn), Is.EqualTo(5));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest, Category("ContinuousOperations")]
        public IEnumerator ContinuousPhysicalRadiatorChangesOnlyItsAttributedSpaceDemandAndCapacityDisplay()
        {
            var session = GameSession.Instance; var model = session.Simulation; var actor = bootstrap.Players[0];
            Assert.That(model.ContinuousOperations && model.Boiler.CapacityModelEnabled, Is.True);
            Assert.That(model.Guests, Is.Empty);
            ManagementUI.Instance.Close();
            var valve = Object.FindObjectsByType<RadiatorValveInteraction>(FindObjectsSortMode.None).Single(value => value.roomId == 106);
            var gauge = Object.FindAnyObjectByType<BoilerReadout>();
            Assert.That(gauge.capacityReadout && gauge.capacityDisplay, Is.True, "The authored boiler must have its separate capacity display.");
            // Phase3 deliberately made this plate the real inspection/service surface.
            // Preserve the no-obstruction requirement without forbidding its required hitbox.
            var plateShapes = gauge.capacityDisplay.GetComponentsInChildren<Collider>(true);
            Assert.That(plateShapes.Length, Is.EqualTo(1));
            Assert.That(plateShapes[0].GetComponent<BoilerServiceInteraction>(), Is.SameAs(BoilerServiceInteraction.Instance));
            foreach (var control in Object.FindObjectsByType<RepairControl>(FindObjectsSortMode.None))
                foreach (var shape in control.GetComponentsInChildren<Collider>(true).Where(value => value.enabled))
                    Assert.That(plateShapes[0].bounds.Intersects(shape.bounds), Is.False,
                        "The inspection plate must leave emergency control " + control.kind + " clear.");
            yield return PositionEmptyActorForLinen(0, new Vector3(8, .08f, valve.transform.position.z), valve.transform.position);
            yield return AimAtKeyScenarioPoint(actor, padA, () => valve.transform.position);
            Assert.That(actor.Interactor.Focused, Is.SameAs(valve), "Controller input must hit the real radiator valve.");
            var original = model.HeatingDemands.ToDictionary(row => row.RoomId, row => row.SpaceHeating);
            Assert.That(original.Count, Is.EqualTo(6));
            Assert.That(original[106], Is.GreaterThan(0), "Even a vacant room's open radiator consumes heat.");
            float baseline = model.Boiler.Load;
            float power = model.Electrical.Find("B").ActualRequestedLoad;
            for (int expected = 2; expected <= 3; expected++)
            {
                QueueUse(padA, true); yield return null; yield return null;
                QueueUse(padA, false); yield return null; yield return null;
                Assert.That(valve.State.RadiatorSetting, Is.EqualTo(expected));
            }
            var high = model.HeatingDemands;
            Assert.That(high.Single(row => row.RoomId == 106).SpaceHeating, Is.EqualTo(original[106] * 1.5f).Within(.0001f));
            foreach (var row in high.Where(row => row.RoomId != 106))
                Assert.That(row.SpaceHeating, Is.EqualTo(original[row.RoomId]).Within(.0001f), "A valve cannot change another room's attribution.");
            Assert.That(high.All(row => row.HotWater == 0 && row.GuestId == null), Is.True);
            Assert.That(model.Boiler.Load, Is.EqualTo(high.Sum(row => row.Total)).Within(.0001f));
            Assert.That(model.Boiler.Load, Is.GreaterThan(baseline));
            Assert.That(model.Electrical.Find("B").ActualRequestedLoad, Is.EqualTo(power).Within(.0001f));
            yield return WaitForCondition(() => gauge.capacityDisplay.activeInHierarchy &&
                gauge.capacityReadout.text.Contains("DEMAND " + model.Boiler.Load.ToString("F2")), 2,
                "The physical capacity display must follow the actual changed room demand.");
            Assert.That(gauge.capacityReadout.text, Does.Contain("EFFECTIVE").And.Contain("RATED").And.Contain("STRESS"));
            var pressureRotation = Quaternion.Euler(0, 0, Mathf.Lerp(120, -120, model.Boiler.Pressure / session.BoilerSettings.MaxPressure));
            Assert.That(Quaternion.Angle(gauge.needle.localRotation, pressureRotation), Is.LessThan(1),
                "The analog needle remains calibrated to pressure, independently of utilization.");
            for (int expected = 2; expected >= 0; expected--)
            {
                InputSystem.QueueStateEvent(padA, new GamepadState().WithButton(GamepadButton.West)); yield return null; yield return null;
                QueueUse(padA, false); yield return null; yield return null;
                Assert.That(valve.State.RadiatorSetting, Is.EqualTo(expected));
            }
            Assert.That(model.HeatingDemands.Single(row => row.RoomId == 106).SpaceHeating, Is.Zero);
            Assert.That(model.Boiler.Load, Is.EqualTo(baseline - original[106]).Within(.0001f));
            Assert.That(valve.settingLabel.text, Does.Contain("OFF"));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest, Category("ContinuousOperations")]
        public IEnumerator ContinuousActualShowerAnchorAddsHotWaterAndTrippedPanelRetainsRequestedLoad()
        {
            var session = GameSession.Instance;
            // Labelled initial booking/key adapter isolates actual in-room staging. The shower
            // arrival below is reported only by the real walking guest, never by this fixture.
            waitScenarioSessionConfig = Object.Instantiate(session.config);
            waitScenarioSessionConfig.continuousOperations = true;
            waitScenarioSessionConfig.hotelDaySeconds = 720;
            waitScenarioSessionConfig.openingHour = 8; waitScenarioSessionConfig.reportHour = 6;
            waitScenarioLivingConfig = Object.Instantiate(session.config.living);
            waitScenarioLivingConfig.firstActivityDelay = 1000;
            waitScenarioLivingConfig.activityDurationMin = waitScenarioLivingConfig.activityDurationMax = 120;
            waitScenarioSessionConfig.living = waitScenarioLivingConfig;
            serviceUIConfig = Object.Instantiate(session.config.services);
            serviceUIConfig.eligibility = 0;
            serviceUIConfig.selfResponseObserveSeconds = serviceUIConfig.toleranceSeconds = 1000;
            waitScenarioSessionConfig.services = serviceUIConfig;
            session.config = waitScenarioSessionConfig; session.NewGame(); ManagementUI.Instance.Close();
            var model = session.Simulation;
            var offer = model.BookingOffers.First(value => value.ArrivalDay == 1);
            Assert.That(session.AcceptBooking(0, offer.Id, 101, session.Economy.MinPrice).Success, Is.True);
            session.AdvanceTime(offer.ArrivalAt - model.Elapsed + .2f);
            var guest = model.Guests.Single();
            Assert.That(session.ReportGuestReachedReception(guest.GuestId).Success, Is.True);
            Assert.That(CheckInWithModelKeyFixture(session, 0, guest.GuestId).Success, Is.True);
            Assert.That(session.ReportGuestReachedRoom(guest.GuestId).Success, Is.True);
            session.RaiseChanged(); yield return null; yield return null;
            var presentation = Object.FindAnyObjectByType<GuestPresentation>();
            var markers = presentation.roomMarkers.Single(room => room.roomId == 101);
            Assert.That(presentation.TryGetGuestTransform(guest.GuestId, out var root), Is.True);
            Assert.That(guest.Agent.RequiresActivityStaging, Is.True);
            var sleep = model.ForceSleep(guest.GuestId);
            Assert.That(sleep.Success, Is.True, sleep.Message);
            session.RaiseChanged();
            yield return WaitForCondition(() => guest.Agent.ActivityStaged, 20, "The guest must actually reach their bed first.");
            Assert.That(model.HeatingDemands.Single(row => row.RoomId == 101).HotWater, Is.Zero);
            float baseline = model.Boiler.Load;
            var shower = model.ForceActivity(guest.GuestId, GuestActivity.Shower);
            Assert.That(shower.Success, Is.True, shower.Message);
            session.RaiseChanged();
            yield return new WaitForSecondsRealtime(.25f);
            Assert.That(guest.Agent.ActivityStaged, Is.False, "Getting out of bed and approaching the shower takes physical time.");
            Assert.That(model.HeatingDemands.Single(row => row.RoomId == 101).HotWater, Is.Zero);
            Assert.That(model.Boiler.Load, Is.EqualTo(baseline).Within(.0001f));
            yield return WaitForCondition(() => guest.Agent.ActivityStaged, 20, "The guest must walk to the authored shower anchor.");
            yield return null; yield return null;
            Assert.That(HorizontalDistance(root.position, markers.shower.position), Is.LessThan(.03f));
            Assert.That(markers.showerWater.activeSelf && markers.showerCurtain.activeSelf, Is.True);
            Assert.That(root.Find("Body").gameObject.activeSelf, Is.False, "The shower enclosure preserves privacy.");
            var demand = model.HeatingDemands.Single(row => row.RoomId == 101);
            Assert.That(demand.GuestId, Is.EqualTo(guest.GuestId));
            Assert.That(demand.HotWater, Is.GreaterThan(0));
            Assert.That(model.HeatingDemands.Where(row => row.RoomId != 101).All(row => row.HotWater == 0), Is.True);
            Assert.That(model.Boiler.Load, Is.EqualTo(model.HeatingDemands.Sum(row => row.Total)).Within(.0001f));
            Assert.That(model.Boiler.Load, Is.GreaterThan(baseline));
            var gauge = Object.FindAnyObjectByType<BoilerReadout>();
            yield return WaitForCondition(() => gauge.capacityReadout.text.Contains("DEMAND " + model.Boiler.Load.ToString("F2")), 2,
                "Physical boiler readout must include the staged shower's measured demand.");

            // Explicit failure fixture only: verify the physical panel distinguishes an
            // occupied room's still-requested power from zero delivery after its breaker trips.
            var circuit = model.Electrical.CircuitForRoom(101);
            Assert.That(circuit.ActualRequestedLoad, Is.GreaterThan(0));
            Assert.That(model.Electrical.ForceTrip(circuit.Id).Success, Is.True);
            var view = Object.FindAnyObjectByType<ElectricalPanelPresentation>().circuits.Single(value => value.circuitId == circuit.Id);
            yield return WaitForCondition(() => view.readout.text.Contains("TRIPPED") &&
                view.readout.text.Contains("DELIVERED " + 0f.ToString("F2") + " u"), 2,
                "The tripped physical panel must show zero delivered power without erasing requested demand.");
            Assert.That(circuit.ActualRequestedLoad, Is.GreaterThan(0));
            Assert.That(circuit.ActualDeliveredLoad, Is.Zero);
            Assert.That(view.readout.text, Does.Contain("REQUESTED " + circuit.RequestedLoad.ToString("F2")));
            Assert.That(view.readout.text, Does.Contain("STRESS"));
            LogAssert.NoUnexpectedReceived();
        }
    }
}
