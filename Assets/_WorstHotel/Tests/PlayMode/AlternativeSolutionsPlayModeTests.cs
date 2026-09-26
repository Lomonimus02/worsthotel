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
        [UnityTest]
        public IEnumerator RealHeaterCanBeCarriedThroughDoorPlacedAndSwitchedToChangeRoomHeat()
        {
            StartQuietWaitTestShift();
            var session = GameSession.Instance;
            var heater = Object.FindAnyObjectByType<PortableHeater>();
            Assert.That(heater, Is.Not.Null);
            yield return null; yield return null;
            Assert.That(heater.State, Is.Not.Null);
            var body = heater.Body;
            // Arrange the real tool in the corridor before acquisition; the subsequent room transfer
            // uses the actual grab joint, player movement, doorway and dynamic body throughout.
            body.position = new Vector3(-.5f, .04f, 10);
            body.rotation = Quaternion.identity;
            body.linearVelocity = body.angularVelocity = Vector3.zero;
            var actor = bootstrap.Players[0];
            var pose = new GameObject("Heater corridor approach");
            pose.transform.SetPositionAndRotation(new Vector3(1.1f, .08f, 10), Quaternion.Euler(0, -90, 0));
            actor.ResetToSpawn(pose.transform);
            Object.Destroy(pose);
            var door = GameObject.Find("Door101").GetComponent<DoorInteractable>();
            door.RequestOpen();
            yield return WaitForCondition(() => door.IsPassageOpen, 2, "The actual room doorway did not open.");
            yield return PhysicsSteps(8);
            yield return AimSuitcasePitch(() => body.worldCenterOfMass);
            Assert.That(actor.Interactor.Focused, Is.SameAs(heater));
            Assert.That(actor.Interactor.FocusedPickup, Is.SameAs(heater.GetComponent<PhysicsPickup>()));
            QueueUse(padA, true);
            yield return null; yield return null;
            QueueUse(padA, false);
            yield return null; yield return null;
            Assert.That(heater.State.SwitchedOn, Is.True, "The real primary input must operate the heater switch.");
            Assert.That(heater.State.RoomId, Is.Null);
            Assert.That(heater.State.EffectiveHeatOutput, Is.Zero, "A switched-on corridor heater must not heat an arbitrary room.");

            QueueGrab(padA, true);
            yield return null; yield return null;
            QueueGrab(padA, false);
            yield return null;
            Assert.That(actor.Interactor.HeldBody, Is.SameAs(body));
            Assert.That(body.GetComponent<ConfigurableJoint>(), Is.Not.Null);
            Assert.That(body.isKinematic, Is.False);
            yield return AimSuitcasePitch(() => actor.PlayerCamera.transform.position + actor.transform.forward * 4);
            InputSystem.QueueStateEvent(padA, new GamepadState { leftStick = Vector2.up });
            yield return WaitForCondition(() => actor.transform.position.x < -2.9f, 5,
                "Staff could not carry the actual heater through the open room doorway.");
            QueueGrab(padA, false);
            yield return PhysicsSteps(30);
            Assert.That(actor.Interactor.HeldBody, Is.SameAs(body), "The room transfer must not succeed by dropping the heater en route.");
            Assert.That(heater.roomVolumes.ResolveRoom(heater.placementCollider.bounds), Is.EqualTo(101),
                "The whole physical heater must reach the room interior before carried heat is checked.");
            Assert.That(heater.IsCarried, Is.True);
            Assert.That(heater.State.RoomId, Is.Null, "Even an entirely in-room heater is disconnected while being carried.");
            Assert.That(heater.State.EffectiveHeatOutput, Is.Zero);
            Assert.That(heater.State.DemandedElectricalLoad, Is.Zero);

            QueueGrab(padA, true);
            yield return null; yield return null;
            QueueGrab(padA, false);
            yield return WaitForCondition(() => !heater.IsCarried && heater.State.RoomId == 101 && body.linearVelocity.sqrMagnitude < .04f,
                8, "The released dynamic heater did not settle wholly inside room101.");
            Assert.That(heater.State.EffectiveHeatOutput, Is.GreaterThan(0));
            Assert.That(heater.State.DemandedElectricalLoad, Is.EqualTo(heater.State.Settings.ElectricalLoad));
            session.Simulation.Boiler.ForceFailure();
            Assert.That(session.Simulation.SetRoomTemperature(101, 8).Success, Is.True);
            Assert.That(session.Simulation.SetRoomTemperature(105, 8).Success, Is.True);
            session.AdvanceTime(8);
            var warmed = session.Rooms.Single(room => room.Profile.Id == 101);
            var unheated = session.Rooms.Single(room => room.Profile.Id == 105);
            float expectedLocalGain = heater.State.Settings.HeatOutput * (1 - Mathf.Exp(-8 / session.config.temperatureTimeConstant));
            Assert.That(warmed.Temperature - unheated.Temperature, Is.GreaterThan(expectedLocalGain * .7f),
                "The placed heater must add meaningful local heat beyond the same failed boiler's neighbouring room.");

            yield return FaceStation(actor, padA, heater, heater.transform.TransformPoint(heater.placementCollider.center));
            QueueUse(padA, true);
            yield return null; yield return null;
            QueueUse(padA, false);
            yield return null; yield return null;
            Assert.That(heater.State.SwitchedOn, Is.False);
            Assert.That(heater.State.EffectiveHeatOutput, Is.Zero);
            Assert.That(heater.State.DemandedElectricalLoad, Is.Zero);
            string id = heater.heaterId;
            heater.enabled = false;
            yield return null;
            Assert.That(session.Simulation.Heaters.Find(id), Is.Null, "Disabling the physical tool must remove its model source.");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator RelocationUsesBothRealDoorsAndCheckoutMidTransferExitsItsPhysicalOrigin()
        {
            var session = GameSession.Instance;
            waitScenarioSessionConfig = Object.Instantiate(session.config);
            waitScenarioLivingConfig = Object.Instantiate(session.config.living);
            waitScenarioLivingConfig.firstArrivalSeconds = .2f;
            waitScenarioLivingConfig.arrivalJitterSeconds = 0;
            waitScenarioLivingConfig.firstActivityDelay = 1000;
            waitScenarioSessionConfig.living = waitScenarioLivingConfig;
            session.config = waitScenarioSessionConfig; session.NewGame();
            var offer = session.Plan.Applications.First(application => application.Archetype.Kind == GuestKind.ColdSensitive);
            int min = session.Economy.MinPrice, step = session.Economy.PriceStep;
            int price = min + Mathf.RoundToInt((offer.ReferencePrice - min) / (float)step) * step;
            session.Assign(0, offer.Id, 101, price); session.CommitPlan(0);
            var simulation = session.Simulation;
            var guest = simulation.Guests.Single();
            // Adapter setup stops at the first room. The relocation and interrupted exit below
            // receive every completion signal from the real scene presentation.
            session.AdvanceTime(guest.Agent.ArrivalTime + .2f);
            Assert.That(session.ReportGuestReachedReception(guest.GuestId).Success, Is.True);
            Assert.That(CheckInWithModelKeyFixture(session, 0, guest.GuestId).Success, Is.True);
            Assert.That(session.ReportGuestReachedRoom(guest.GuestId).Success, Is.True);
            yield return null;
            var presentation = Object.FindAnyObjectByType<GuestPresentation>();
            Assert.That(presentation.TryGetGuestTransform(guest.GuestId, out var visual), Is.True);
            var oldRoom = presentation.roomMarkers.Single(room => room.roomId == 101);
            var newRoom = presentation.roomMarkers.Single(room => room.roomId == 102);
            yield return WaitForCondition(() => HorizontalDistance(visual.position, oldRoom.rest.position) < .08f, 15,
                "Initial guest did not reach their physical rest pose.");
            Assert.That(simulation.SetRoomTemperature(101, 5).Success, Is.True);
            Assert.That(simulation.SetRoomTemperature(102, 24).Success, Is.True);
            session.AdvanceTime(.4f);
            Assert.That(guest.Needs.Temperature.Severity, Is.GreaterThan(.3f));
            float exposure = guest.Needs.Temperature.ExposureSeconds;
            Assert.That(session.MoveGuest(0, guest.GuestId, 102).Success, Is.True);
            Assert.That(guest.Agent.IsRelocating, Is.False, "A ledger selection cannot move the guest before the new physical key arrives.");
            Assert.That(guest.RoomId, Is.EqualTo(101));
            yield return GiveDroppedFixtureKeyForMove(0, guest, 102, visual.GetComponent<GuestReceptionInteraction>());
            Assert.That(guest.Agent.IsRelocating, Is.True);
            Assert.That(simulation.Keys.Find(101).Location, Is.EqualTo(RoomKeyLocation.Returned));
            Assert.That(Vector3.Distance(PhysicalKey(101).Body.position, PhysicalKey(101).rackAnchor.position), Is.LessThan(.04f));
            Assert.That(session.Rooms.Single(room => room.Profile.Id == 102).ReservedGuestId, Is.EqualTo(guest.GuestId));
            bool exitedOld = false, enteredNew = false;
            Vector3 previous = visual.position;
            float deadline = Time.realtimeSinceStartup + 30;
            while (guest.Agent.IsRelocating && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                Vector3 current = visual.position;
                if (previous.x < oldRoom.door.transform.position.x && current.x >= oldRoom.door.transform.position.x)
                {
                    exitedOld = true;
                    Assert.That(Mathf.Abs(current.z - oldRoom.door.transform.position.z), Is.LessThan(.12f));
                    Assert.That(oldRoom.door.IsPassageOpen, Is.True, "The old room door must physically clear before crossing.");
                }
                if (previous.x < newRoom.door.transform.position.x && current.x >= newRoom.door.transform.position.x)
                {
                    enteredNew = true;
                    Assert.That(Mathf.Abs(current.z - newRoom.door.transform.position.z), Is.LessThan(.12f));
                    Assert.That(newRoom.door.IsPassageOpen, Is.True, "The destination door must physically clear before crossing.");
                }
                previous = current;
            }
            Assert.That(guest.Agent.IsRelocating, Is.False, "The physical guest never completed relocation.");
            Assert.That(exitedOld && enteredNew, Is.True, "Room reassignment must include both actual doorway crossings.");
            Assert.That(HorizontalDistance(visual.position, newRoom.roomTarget.position), Is.LessThan(.25f));
            Assert.That(session.Rooms.Single(room => room.Profile.Id == 102).GuestId, Is.EqualTo(guest.GuestId));
            session.AdvanceTime(.4f);
            Assert.That(guest.Needs.Temperature.Severity, Is.LessThan(.03f), "The guest must assess the new warmer room after arrival.");
            Assert.That(guest.Needs.Temperature.ExposureSeconds, Is.GreaterThanOrEqualTo(exposure));

            // Interrupt a second move while the NPC still physically occupies room102. Reservation103
            // must never make the exit jump to its other row/opposite corridor side.
            Assert.That(MoveWithModelKeyFixture(session, 1, guest.GuestId, 103).Success, Is.True);
            yield return null;
            Assert.That(visual.position.x, Is.GreaterThan(3));
            session.EndShift();
            Assert.That(simulation.Keys.Find(103).Location, Is.EqualTo(RoomKeyLocation.Returned));
            bool exitedPhysicalOrigin = false;
            previous = visual.position;
            deadline = Time.realtimeSinceStartup + 25;
            while (guest.Agent.State != GuestAgentState.Left && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                if (visual == null) break;
                Vector3 current = visual.position;
                if (previous.x > newRoom.door.transform.position.x && current.x <= newRoom.door.transform.position.x)
                {
                    exitedPhysicalOrigin = true;
                    Assert.That(Mathf.Abs(current.z - newRoom.door.transform.position.z), Is.LessThan(.12f));
                    Assert.That(newRoom.door.IsPassageOpen, Is.True);
                }
                Assert.That(current.x, Is.GreaterThan(-1), "Checkout must not detour through reserved room103's walls.");
                previous = current;
            }
            Assert.That(exitedPhysicalOrigin, Is.True);
            Assert.That(guest.Agent.State, Is.EqualTo(GuestAgentState.Left));
            Assert.That(session.Rooms.Any(room => room.GuestId == guest.GuestId || room.ReservedGuestId == guest.GuestId), Is.False);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
