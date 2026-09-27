using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace WorstHotel.Tests
{
    public sealed partial class Phase1PlayModeTests
    {
        [UnityTest, Category("ContinuousOperations"), Category("AutomaticSales"), Category("StaffSleep")]
        public IEnumerator BothStaffWalkIntoActualBedNicheAndBackWithoutBlockingUtilityControls()
        {
            var session = GameSession.Instance; var model = session.Simulation;
            Assert.That(model.ContinuousOperations && model.AutomaticBookingsEnabled && !bootstrap.IsSolo, Is.True);
            // Labelled quiet-night geometry fixture: close ordinary future sales and
            // advance the normal calendar. No guest, failure or sleep state is injected.
            foreach (var policy in model.RoomSalesPolicies)
                Assert.That(model.SetRoomSalesPolicy(0, policy.RoomId, false, policy.Price, policy.Revision).Success, Is.True);
            session.AdvanceTime(model.Calendar.At(1, 23) - model.Elapsed);
            var beds = Object.FindObjectsByType<StaffBedInteraction>(FindObjectsSortMode.None).OrderBy(bed => bed.bedId).ToArray();
            Assert.That(beds.Select(bed => bed.bedId), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(beds.All(bed => bed.standingAnchor && bed.interactionTarget), Is.True);
            var actorA = bootstrap.Players[0]; var actorB = bootstrap.Players[1];
            // Initial empty-handed approaches only. Every claimed niche/utility route
            // below uses actual controller movement, including both exits.
            yield return PositionEmptyActorForLinen(0, new Vector3(.5f, .08f, 29.5f), new Vector3(.5f, 1, 33));
            yield return PositionEmptyActorForLinen(1, new Vector3(1.3f, .08f, 29.5f), new Vector3(1.3f, 1, 33));
            yield return WalkStaffGeometry(actorA, padA, new Vector3(.5f, 0, 33));
            yield return WalkStaffGeometry(actorA, padA, new Vector3(-2.5f, 0, 34.4f));
            yield return WalkStaffGeometry(actorA, padA, new Vector3(-4.1f, 0, 34.4f));
            yield return WalkStaffGeometry(actorA, padA, beds[0].standingAnchor.position);
            yield return FocusStaffGeometry(actorA, padA, beds[0], () => beds[0].InteractionPoint);
            QueueUse(padA, true); yield return null; yield return null;
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That(Waiter.HasSleepConsent(0), Is.True, "Using the actual first bed must latch the first actor's consent.");
            Assert.That(Waiter.SleepBedId(0), Is.EqualTo(0));
            Assert.That(Waiter.HasSleepConsent(1) || Waiter.IsSleeping, Is.False);
            Assert.That(model.Clock.Speed, Is.EqualTo(1));

            yield return WalkStaffGeometry(actorB, padB, new Vector3(1.3f, 0, 33));
            yield return WalkStaffGeometry(actorB, padB, new Vector3(-2.5f, 0, 34.4f));
            yield return WalkStaffGeometry(actorB, padB, new Vector3(-4.1f, 0, 34.4f));
            yield return WalkStaffGeometry(actorB, padB, beds[1].standingAnchor.position);
            yield return FocusStaffGeometry(actorB, padB, beds[1], () => beds[1].InteractionPoint);
            Assert.That(Waiter.HasSleepConsent(0), Is.True,
                "The unready partner must be able to walk to the second bed without cancelling the first actor's consent.");
            Assert.That(HorizontalDistance(actorA.transform.position, actorB.transform.position), Is.GreaterThan(.67f));
            Assert.That(actorA.BodyCollider.isGrounded && actorB.BodyCollider.isGrounded, Is.True);
            Assert.That(HorizontalDistance(actorA.transform.position, beds[0].standingAnchor.position), Is.LessThan(.14f));
            Assert.That(HorizontalDistance(actorB.transform.position, beds[1].standingAnchor.position), Is.LessThan(.14f));

            // Cancel by genuinely moving the consenting actor. Let the other actor exit
            // first so the test does not ask two capsules to pass within the narrow aisle.
            yield return WaitForCondition(() => Waiter.IsSleepCancellationArmed(0), 2, "Neutral input must arm normal sleep cancellation.");
            yield return WalkStaffGeometry(actorA, padA, new Vector3(-5.85f, 0, 34.1f));
            Assert.That(Waiter.HasSleepConsent(0) || Waiter.IsSleeping, Is.False);
            yield return WalkStaffGeometry(actorB, padB, new Vector3(-4.1f, 0, 34.4f));
            yield return WalkStaffGeometry(actorB, padB, new Vector3(-2.5f, 0, 34.4f));
            yield return WalkStaffGeometry(actorB, padB, new Vector3(1.3f, 0, 33));
            yield return WalkStaffGeometry(actorB, padB, new Vector3(1.3f, 0, 29.5f));
            yield return WalkStaffGeometry(actorA, padA, new Vector3(-4.1f, 0, 34.1f));
            yield return WalkStaffGeometry(actorA, padA, new Vector3(-2.5f, 0, 34.4f));

            // The same bed is within use range here, but behind the solid entry partition.
            yield return WalkStaffGeometry(actorA, padA, new Vector3(-3.25f, 0, 33.25f));
            yield return AimAtKeyScenarioPoint(actorA, padA, () => beds[0].InteractionPoint);
            var obstruction = StaffGeometryFirstHit(actorA);
            Assert.That(obstruction, Is.Not.Null);
            Assert.That(obstruction.name, Is.EqualTo("Staff room entry partition"));
            Assert.That(actorA.Interactor.Focused, Is.Null, "The first solid wall must block the bed, not permit use through it.");
            QueueUse(padA, true); yield return null; yield return null;
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That(Waiter.HasSleepConsent(0), Is.False);

            yield return WalkStaffGeometry(actorA, padA, new Vector3(-2.5f, 0, 34.4f));
            yield return WalkStaffGeometry(actorA, padA, new Vector3(-3.1f, 0, 35.05f));
            var relief = Control(RepairControlKind.ReliefValve);
            yield return FocusStaffGeometry(actorA, padA, relief, () => relief.transform.position);
            yield return WalkStaffGeometry(actorA, padA, new Vector3(-.52f, 0, 34.4f));
            var inspection = BoilerServiceInteraction.Instance;
            yield return FocusStaffGeometry(actorA, padA, inspection, () => inspection.InteractionPoint);
            QueueUse(padA, true); yield return null; yield return null;
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That(ManagementUI.Instance.IsOperationsOpen, Is.True, "Boiler inspection must still open through its actual physical surface.");
            // Maintenance page -> overview -> close, all through normal menu input.
            for (int index = 0; index < 2; index++)
            {
                QueueUse(padA, false, true); yield return null; yield return null;
                QueueUse(padA, false); yield return null; yield return null;
            }
            Assert.That(ManagementUI.Instance.IsOpen, Is.False);
            yield return WalkStaffGeometry(actorA, padA, new Vector3(1.75f, 0, 35.03f));
            var panel = Control(RepairControlKind.Panel);
            yield return FocusStaffGeometry(actorA, padA, panel, () => panel.transform.position);

            yield return WalkStaffGeometry(actorA, padA, new Vector3(.5f, 0, 33));
            yield return WalkStaffGeometry(actorA, padA, new Vector3(.5f, 0, 29.5f));
            var linen = Object.FindAnyObjectByType<LinenStorage>().cleanStock.First();
            yield return WalkStaffGeometry(actorA, padA, new Vector3(linen.sourceAnchor.position.x, 0, 29.5f));
            yield return AimAtKeyScenarioPoint(actorA, padA, () => linen.Body.worldCenterOfMass);
            Assert.That(actorA.Interactor.FocusedPickup, Is.SameAs(linen.GetComponent<PhysicsPickup>()),
                "The finite clean-linen stock must remain reachable along its original south approach.");
            Assert.That(session.Simulation, Is.SameAs(model));
            Assert.That(model.Clock.Speed, Is.EqualTo(1));
            Assert.That(Time.timeScale, Is.EqualTo(1));
            LogAssert.NoUnexpectedReceived();
        }

        IEnumerator WalkStaffGeometry(FirstPersonController actor, Gamepad pad, Vector3 destination)
        {
            Assert.That(actor.Interactor.HeldBody, Is.Null);
            float deadline = Time.realtimeSinceStartup + 12;
            while (HorizontalDistance(actor.transform.position, destination) > .065f && Time.realtimeSinceStartup < deadline)
            {
                Vector3 delta = destination - actor.transform.position; delta.y = 0;
                Vector3 local = actor.transform.InverseTransformDirection(delta.normalized);
                float speed = Mathf.Clamp(delta.magnitude * 1.5f, .14f, .65f);
                InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = new Vector2(local.x, local.z) * speed });
                yield return null;
            }
            QueueUse(pad, false); yield return null; yield return null;
            Assert.That(HorizontalDistance(actor.transform.position, destination), Is.LessThan(.14f),
                "Physical staff route blocked: target=" + destination.ToString("F3") + " actual=" + actor.transform.position.ToString("F3"));
        }

        IEnumerator FocusStaffGeometry(FirstPersonController actor, Gamepad pad, HotelInteractable expected, Func<Vector3> point)
        {
            yield return AimAtKeyScenarioPoint(actor, pad, point);
            var first = StaffGeometryFirstHit(actor);
            Assert.That(first, Is.Not.Null);
            Assert.That(first.GetComponentInParent<HotelInteractable>(), Is.SameAs(expected),
                "Actual first surface obstructed " + expected.name + ": " + first.name);
            Assert.That(actor.Interactor.Focused, Is.SameAs(expected));
        }

        static Collider StaffGeometryFirstHit(FirstPersonController actor) =>
            Physics.RaycastAll(actor.PlayerCamera.transform.position, actor.PlayerCamera.transform.forward,
                actor.Interactor.reach, ~0, QueryTriggerInteraction.Ignore)
                .Where(hit => !hit.collider.transform.IsChildOf(actor.transform)).OrderBy(hit => hit.distance)
                .Select(hit => hit.collider).FirstOrDefault();
    }
}
