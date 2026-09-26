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
        ServiceSupplyItem PhysicalSupply(string id) => Object.FindObjectsByType<ServiceSupplyItem>(FindObjectsSortMode.None).Single(item => item.ItemId == id);

        IEnumerator GrabServiceSupply(ServiceSupplyItem item)
        {
            var actor = bootstrap.Players[0];
            yield return AimAtKeyScenarioPoint(actor, padA, () => item.Body.worldCenterOfMass);
            Assert.That(actor.Interactor.FocusedPickup, Is.SameAs(item.GetComponent<PhysicsPickup>()));
            QueueGrab(padA, true); yield return null; yield return null;
            QueueGrab(padA, false); yield return null; yield return null;
            Assert.That(actor.Interactor.HeldBody, Is.SameAs(item.Body));
            Assert.That(item.State.Location, Is.EqualTo(ServiceItemLocation.HeldByPlayer));
            Assert.That(item.Body.GetComponent<ConfigurableJoint>(), Is.Not.Null, "Service items must use the existing physics grab.");
        }

        IEnumerator CarryServiceSupply(ServiceSupplyItem item, Vector3 destination)
        {
            var actor = bootstrap.Players[0];
            Vector3 direction = destination - actor.transform.position; direction.y = 0;
            if (direction.sqrMagnitude > .01f)
                yield return AimAtKeyScenarioPoint(actor, padA, () => actor.PlayerCamera.transform.position + direction.normalized * 5);
            float deadline = Time.realtimeSinceStartup + 20;
            while (HorizontalDistance(actor.transform.position, destination) > .13f && Time.realtimeSinceStartup < deadline)
            {
                Assert.That(actor.Interactor.HeldBody, Is.SameAs(item.Body), "No teleport or dropped body can complete a service route.");
                Vector3 delta = destination - actor.transform.position; delta.y = 0;
                Vector3 local = actor.transform.InverseTransformDirection(delta.normalized);
                InputSystem.QueueStateEvent(padA, new GamepadState { leftStick = new Vector2(local.x, local.z) * Mathf.Clamp(delta.magnitude, .25f, .8f) });
                yield return null;
            }
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That(HorizontalDistance(actor.transform.position, destination), Is.LessThan(.20f), "Service route blocked at " + destination + "; actor " + actor.transform.position);
            Assert.That(actor.Interactor.HeldBody, Is.SameAs(item.Body));
            Assert.That(Vector3.Distance(item.Body.worldCenterOfMass, actor.PlayerCamera.transform.position), Is.LessThan(2.4f));
        }

        IEnumerator PrepareServiceWorldGuest(bool checkIn, bool dirty = false)
        {
            // Explicit model setup isolates the physical staff delivery. Guest navigation and
            // privacy are tested separately; no held item or staff route is repositioned below.
            var session = GameSession.Instance;
            waitScenarioSessionConfig = Object.Instantiate(session.config); waitScenarioSessionConfig.serviceSeconds = 3000;
            waitScenarioLivingConfig = Object.Instantiate(session.config.living);
            waitScenarioLivingConfig.firstArrivalSeconds = .2f; waitScenarioLivingConfig.arrivalJitterSeconds = 0;
            waitScenarioLivingConfig.firstActivityDelay = 1000;
            waitScenarioSessionConfig.living = waitScenarioLivingConfig;
            session.config = waitScenarioSessionConfig; session.NewGame(); ManagementUI.Instance.Close();
            Object.FindAnyObjectByType<GuestPresentation>().enabled = false;
            if (dirty) Assert.That(session.Simulation.DebugMarkRoomDirty(106).Success, Is.True);
            var offer = session.Plan.Applications.First();
            int min = session.Economy.MinPrice, step = session.Economy.PriceStep;
            session.Assign(0, offer.Id, 106, min + Mathf.RoundToInt((offer.ReferencePrice - min) / (float)step) * step);
            session.CommitPlan(0);
            var guest = session.Simulation.Guests.Single();
            session.AdvanceTime(guest.Agent.ArrivalTime + .2f);
            Assert.That(session.ReportGuestReachedReception(guest.GuestId).Success, Is.True);
            if (checkIn)
            {
                Assert.That(CheckInWithModelKeyFixture(session, 0, guest.GuestId).Success, Is.True);
                Assert.That(session.ReportGuestReachedRoom(guest.GuestId).Success, Is.True);
                Assert.That(session.Simulation.ForceActivity(guest.GuestId, GuestActivity.QuietRest).Success, Is.True);
            }
            yield return null; yield return null;
        }

        [UnityTest]
        public IEnumerator ActualBlanketShelfCarryAndInstantBedDeliveryConsumesOneStockAndShowsTheSameGuestComfort()
        {
            yield return PrepareServiceWorldGuest(true);
            var session = GameSession.Instance; var guest = session.Simulation.Guests.Single();
            var item = PhysicalSupply("blanket:0"); var actor = bootstrap.Players[0];
            Assert.That(session.Simulation.Services.BlanketsAvailable, Is.EqualTo(3));
            Assert.That(Object.FindObjectsByType<ServiceSupplyItem>(FindObjectsSortMode.None).Count(s => s.ItemId != null && s.ItemId.StartsWith("blanket:")), Is.EqualTo(6));
            Assert.That(Vector3.Distance(item.Body.position, item.SourceAnchor.position), Is.LessThan(.04f));
            var door = GameObject.Find("Door106").GetComponent<DoorInteractable>();
            // Let the door observe its new occupied/privacy state before preparing access.
            // Otherwise its first FixedUpdate schedules an immediate privacy close.
            yield return new WaitForFixedUpdate();
            Assert.That(session.Simulation.RequestStaffRoomAccess(0, 106).Success, Is.True);
            // Labelled model permission fixture: the actual hinge opens and stays clear for
            // the carry route. Separate tests exercise player knock/context authorization.
            Assert.That(door.RequestGuestOpen(guest.GuestId), Is.True);
            yield return WaitForCondition(() => door.IsPassageOpen, 2, "Fixture door did not open.");
            yield return PositionEmptyActorForLinen(0, new Vector3(item.SourceAnchor.position.x, .08f, 29.5f), item.SourceAnchor.position);
            yield return GrabServiceSupply(item);
            Assert.That(session.Simulation.Services.BlanketsAvailable, Is.EqualTo(2));
            Assert.That(session.DeliverBlanket(1, Object.FindObjectsByType<RoomBlanketDeliveryInteraction>(FindObjectsSortMode.None).Single(v => v.roomId == 106)).Success, Is.False);
            yield return CarryServiceSupply(item, new Vector3(.25f, 0, 29.5f));
            yield return CarryServiceSupply(item, new Vector3(.25f, 0, 24));
            yield return CarryServiceSupply(item, new Vector3(3.25f, 0, 24));
            yield return CarryServiceSupply(item, new Vector3(4.65f, 0, 24.1f));
            var bed = Object.FindObjectsByType<RoomBlanketDeliveryInteraction>(FindObjectsSortMode.None).Single(v => v.roomId == 106);
            yield return AimAtKeyScenarioPoint(actor, padA, () => bed.transform.position);
            Assert.That(actor.Interactor.Focused, Is.SameAs(bed));
            float electricalLoad = session.Simulation.Electrical.Find("B").RequestedLoad;
            QueueUse(padA, true); yield return null; yield return null;
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That(item.State.Location, Is.EqualTo(ServiceItemLocation.Delivered));
            Assert.That(guest.BlanketComfortBonus, Is.GreaterThan(0));
            Assert.That(actor.Interactor.HeldBody, Is.Null);
            Assert.That(bed.deliveredBlanket.activeSelf, Is.True);
            Assert.That(item.GetComponentsInChildren<Renderer>().All(renderer => !renderer.enabled), Is.True);
            Assert.That(session.Simulation.Electrical.Find("B").RequestedLoad, Is.EqualTo(electricalLoad));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator RealBurntLampRequiresShelfBulbAndPowerLossIsNotMistakenForASecondBurntBulb()
        {
            var session = GameSession.Instance; var actor = bootstrap.Players[0];
            var lamp = Object.FindObjectsByType<RoomLampInteraction>(FindObjectsSortMode.None).Single(v => v.roomId == 106);
            Assert.That(session.Simulation.BreakRoomLamp(106).Success, Is.True);
            yield return null; yield return null;
            Assert.That(lamp.IsLit || lamp.bulbLight.enabled, Is.False);
            Assert.That(session.ReplaceRoomBulb(0, lamp).Success, Is.False);
            var bulb = PhysicalSupply("bulb:0");
            Vector3 approach = bulb.SourceAnchor.position - bulb.SourceAnchor.forward * 1.35f; approach.y = .08f;
            yield return PositionEmptyActorForLinen(0, approach, bulb.SourceAnchor.position);
            yield return GrabServiceSupply(bulb);
            var door = GameObject.Find("Door106").GetComponent<DoorInteractable>(); door.RequestOpen();
            yield return WaitForCondition(() => door.IsPassageOpen, 2, "Vacant-room preparation door did not open.");
            yield return CarryServiceSupply(bulb, new Vector3(4.75f, 0, 30.1f));
            yield return CarryServiceSupply(bulb, new Vector3(.25f, 0, 30.1f));
            yield return CarryServiceSupply(bulb, new Vector3(.25f, 0, 24));
            yield return CarryServiceSupply(bulb, new Vector3(3.25f, 0, 24));
            yield return CarryServiceSupply(bulb, new Vector3(4.45f, 0, 24.0f));
            yield return AimAtKeyScenarioPoint(actor, padA, () => lamp.GetComponent<BoxCollider>().bounds.center);
            Assert.That(actor.Interactor.Focused, Is.SameAs(lamp));
            QueueUse(padA, true); yield return null; yield return null;
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That(lamp.State.LampBroken, Is.False); Assert.That(lamp.State.LampCondition, Is.EqualTo(100));
            Assert.That(lamp.IsLit && lamp.bulbLight.enabled, Is.True);
            Assert.That(bulb.State.Location, Is.EqualTo(ServiceItemLocation.Delivered));
            Assert.That(actor.Interactor.HeldBody, Is.Null);
            Assert.That(session.Simulation.Electrical.ForceTrip("B").Success, Is.True);
            yield return null; yield return null;
            Assert.That(lamp.IsLit, Is.False); Assert.That(lamp.State.LampBroken, Is.False);
            Assert.That(lamp.GetPrompt(actor.Interactor), Does.Contain("NO POWER"));
            Assert.That(session.Simulation.ResetCircuit(0, "B").Success, Is.True);
            yield return null; yield return null;
            Assert.That(lamp.IsLit, Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator PhysicalRadiatorValveShowsZeroThroughThreeAndRaisesCentralDemandWithoutElectricity()
        {
            var session = GameSession.Instance; var actor = bootstrap.Players[0];
            var valve = Object.FindObjectsByType<RadiatorValveInteraction>(FindObjectsSortMode.None).Single(v => v.roomId == 106);
            yield return PositionEmptyActorForLinen(0, new Vector3(8.0f, .08f, valve.transform.position.z), valve.transform.position);
            yield return AimAtKeyScenarioPoint(actor, padA, () => valve.transform.position);
            Assert.That(actor.Interactor.Focused, Is.SameAs(valve));
            float baseline = session.Simulation.Boiler.Load;
            for (int expected = 2; expected <= 3; expected++)
            {
                QueueUse(padA, true); yield return null; yield return null;
                QueueUse(padA, false); yield return null; yield return null;
                Assert.That(valve.State.RadiatorSetting, Is.EqualTo(expected));
            }
            Assert.That(session.Simulation.Boiler.Load, Is.GreaterThan(baseline));
            Assert.That(valve.settingLabel.text, Does.Contain("HIGH"));
            Assert.That(session.Simulation.Electrical.Find("B").RequestedLoad, Is.Zero);
            for (int expected = 2; expected >= 0; expected--)
            {
                InputSystem.QueueStateEvent(padA, new GamepadState().WithButton(GamepadButton.West)); yield return null; yield return null;
                QueueUse(padA, false); yield return null; yield return null;
                Assert.That(valve.State.RadiatorSetting, Is.EqualTo(expected));
            }
            Assert.That(valve.settingLabel.text, Does.Contain("OFF"));
            Assert.That(Object.FindObjectsByType<PortableHeater>(FindObjectsSortMode.None).Select(heater => heater.heaterId).OrderBy(id => id),
                Is.EqualTo(new[] { "portable-heater-1", "portable-heater-2" }));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator AcceptedLuggageUsesItsActualSuitcaseCarryAndShortPlacementInReceptionStorage()
        {
            yield return PrepareServiceWorldGuest(false, true);
            var session = GameSession.Instance; var guest = session.Simulation.Guests.Single(); var actor = bootstrap.Players[0];
            Assert.That(session.Simulation.DebugForceService(guest.GuestId, ServiceKind.LuggageStorage).Success, Is.True);
            var request = session.Simulation.Services.Cases.Single(item => item.GuestId == guest.GuestId && item.Kind == ServiceKind.LuggageStorage);
            // Explicit model conversation adapter isolates the suitcase carry/placement subject.
            // The prepared guest is already waiting at reception; use real communication rules
            // rather than forging a known/accepted flag or disabling natural service globally.
            Assert.That(request.IsKnownToHotel, Is.False);
            Assert.That(session.Simulation.DebugBeginGuestContact(guest.GuestId, GuestContactChannel.Reception).Success, Is.True);
            Assert.That(session.Simulation.TalkToServiceGuest(0, guest.GuestId, request.Response.Id).Success, Is.True);
            Assert.That(request.IsKnownToHotel, Is.True);
            Assert.That(request.Status, Is.EqualTo(ServiceStatus.Requested));
            Assert.That(session.Simulation.RespondToService(0, request.Id, true).Success, Is.True);
            var suitcase = PhysicalSupply("luggage:" + guest.GuestId);
            yield return PositionEmptyActorForLinen(0, suitcase.SourceAnchor.position + new Vector3(0, -.35f, -1.3f), suitcase.Body.worldCenterOfMass);
            yield return GrabServiceSupply(suitcase);
            yield return CarryServiceSupply(suitcase, new Vector3(-2.0f, 0, -2.3f));
            yield return CarryServiceSupply(suitcase, new Vector3(-7.3f, 0, -2.3f));
            var zone = Object.FindAnyObjectByType<LuggageStorageZone>();
            yield return AimAtKeyScenarioPoint(actor, padA, () => zone.storageBounds.bounds.center);
            Assert.That(actor.Interactor.Focused, Is.SameAs(zone));
            QueueUse(padA, true); yield return null; yield return null;
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That(request.Status, Is.EqualTo(ServiceStatus.Fulfilled));
            Assert.That(suitcase.State.Location, Is.EqualTo(ServiceItemLocation.Stored));
            Assert.That(actor.Interactor.HeldBody, Is.Null);
            Assert.That(Vector3.Distance(suitcase.Body.position, zone.StorageAnchor(suitcase.luggageSlot).position), Is.LessThan(.04f));
            Assert.That(suitcase.GetComponentsInChildren<Renderer>().All(renderer => renderer.enabled), Is.True);
            Assert.That(zone.storageBounds.bounds.Contains(suitcase.PlacementCollider.bounds.min) &&
                zone.storageBounds.bounds.Contains(suitcase.PlacementCollider.bounds.max), Is.True);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
