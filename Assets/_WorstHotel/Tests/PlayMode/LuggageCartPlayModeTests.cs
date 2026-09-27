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
        IEnumerator ApproachCart(LuggageCart cart)
        {
            var actor = bootstrap.Players[0];
            var pose = new GameObject("Cart test approach fixture");
            pose.transform.SetPositionAndRotation(cart.transform.TransformPoint(new Vector3(0, .08f, -1.6f)), cart.transform.rotation);
            actor.ResetToSpawn(pose.transform);
            Object.Destroy(pose);
            yield return WaitForGroundContact(actor);
            yield return AimAtKeyScenarioPoint(actor, padA, () => cart.handle.position);
            Assert.That(actor.Interactor.Focused, Is.SameAs(cart), "The actual handle must be usable through its collider.");
            QueueUse(padA, true); yield return null; yield return null;
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That(LuggageCart.IsGuiding(0), Is.True);
        }

        IEnumerator WalkBesideCart(Vector3 destination)
        {
            var actor = bootstrap.Players[0];
            float deadline = Time.realtimeSinceStartup + 6;
            while (HorizontalDistance(actor.transform.position, destination) > .12f && Time.realtimeSinceStartup < deadline)
            {
                var delta = destination - actor.transform.position; delta.y = 0;
                var local = actor.transform.InverseTransformDirection(delta.normalized);
                InputSystem.QueueStateEvent(padA, new GamepadState { leftStick = new Vector2(local.x, local.z) * Mathf.Clamp(delta.magnitude, .3f, .7f) });
                yield return null;
            }
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That(HorizontalDistance(actor.transform.position, destination), Is.LessThan(.2f), "Walk beside the cart through its actual free aisle.");
        }

        [UnityTest]
        public IEnumerator CartHandleDrivesTurnsReversesAndReleasesWithoutInvalidPhysics()
        {
            var cart = Object.FindAnyObjectByType<LuggageCart>();
            Assert.That(cart, Is.Not.Null, "The authored hotel must contain the functional cart.");
            var body = cart.GetComponent<Rigidbody>();
            Assert.That(body.isKinematic, Is.False);
            TestContext.Out.WriteLine("Authored cart: position=" + body.position + ", inertia=" + body.inertiaTensor +
                ", inertiaRotation=" + body.inertiaTensorRotation + ", angularVelocity=" + body.angularVelocity);
            // Labelled route fixture: start on the real corridor floor. From handle acquisition
            // onward only ordinary look/move/use input drives both actor and dynamic cart.
            body.position = new Vector3(0, .05f, 8);
            body.rotation = Quaternion.identity;
            body.linearVelocity = body.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            yield return ApproachCart(cart);
            var origin = body.position;
            InputSystem.QueueStateEvent(padA, new GamepadState { leftStick = Vector2.up });
            yield return new WaitForSeconds(2);
            QueueUse(padA, false); yield return null;
            Assert.That(LuggageCart.IsGuiding(0), Is.True, "Ordinary pushing must retain the handle.");
            float travel = body.position.z - origin.z;
            Assert.That(travel, Is.GreaterThan(1.5f), "The cart must physically advance, not just display a driving prompt.");
            TestContext.Out.WriteLine("Cart forward travel=" + travel.ToString("F3") + ", velocity=" + body.linearVelocity);

            var reverseStart = body.position;
            InputSystem.QueueStateEvent(padA, new GamepadState { leftStick = Vector2.down });
            yield return new WaitForSeconds(4);
            QueueUse(padA, false); yield return null;
            Assert.That(reverseStart.z - body.position.z, Is.GreaterThan(4.5f), "Sustained pulling must move the real cart without losing the handle.");
            Assert.That(LuggageCart.IsGuiding(0), Is.True);

            float yaw = body.rotation.eulerAngles.y;
            var actor = bootstrap.Players[0];
            Vector3 heading = Quaternion.Euler(0, 30, 0) * Vector3.forward;
            yield return AimAtKeyScenarioPoint(actor, padA, () => actor.PlayerCamera.transform.position + heading * 5);
            yield return new WaitForSeconds(1.8f);
            float turned = Mathf.DeltaAngle(yaw, body.rotation.eulerAngles.y);
            TestContext.Out.WriteLine("Cart actual yaw change=" + turned.ToString("F2") + ", angularVelocity=" + body.angularVelocity +
                ", guiding=" + LuggageCart.IsGuiding(0) + ", inertia=" + body.inertiaTensor + ", actorYaw=" + actor.transform.eulerAngles.y +
                ", cart=" + body.position + ", actor=" + actor.transform.position);
            Assert.That(turned, Is.InRange(12f, 45f), "Look steering must rotate the dynamic chassis.");
            Assert.That(Vector3.Dot(cart.transform.up, Vector3.up), Is.GreaterThan(.99f));
            Assert.That(float.IsNaN(body.angularVelocity.y) || float.IsInfinity(body.angularVelocity.y), Is.False);

            yield return AimAtKeyScenarioPoint(actor, padA, () => cart.handle.position);
            Assert.That(actor.Interactor.Focused, Is.SameAs(cart));
            QueueUse(padA, true); yield return null; yield return null;
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That(LuggageCart.IsGuiding(0), Is.False, "Use must release the handle.");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator CartSecuresTwoPhysicalSuitcasesTransportsThemAndReleasesOnGrab()
        {
            StartOrdinaryTestShift();
            var session = GameSession.Instance;
            var model = session.Simulation;
            // Labelled model fixture for two accepted bags; this scenario measures real
            // gravity, automatic cart joints, travel and F-grab, not guest check-in routes.
            Object.FindAnyObjectByType<GuestPresentation>().enabled = false;
            session.AdvanceTime(model.Guests.Max(guest => guest.Agent.ArrivalTime) + .1f);
            foreach (var guest in model.Guests)
            {
                if (guest.Agent.State == GuestAgentState.Arriving) session.ReportGuestReachedReception(guest.GuestId);
                model.DebugMarkRoomDirty(guest.RoomId);
                if (model.Services.CanOfferLuggage(guest.GuestId, true))
                    Assert.That(model.Services.OfferLuggage(0, guest.GuestId, true).Success, Is.True);
            }
            yield return null; yield return null;
            var bags = Object.FindObjectsByType<ServiceSupplyItem>(FindObjectsSortMode.None)
                .Where(item => item.State?.Kind == ServiceItemKind.Luggage && item.State.StaffHandling).Take(2).ToArray();
            Assert.That(bags.Length, Is.EqualTo(2));
            var cart = Object.FindAnyObjectByType<LuggageCart>();
            var body = cart.GetComponent<Rigidbody>();
            body.position = new Vector3(0, .05f, 8); body.rotation = Quaternion.identity;
            body.linearVelocity = body.angularVelocity = Vector3.zero;
            // Drop fixtures just above the actual platform. No joint, parenting or
            // attachment state is manufactured by the test.
            for (int i = 0; i < bags.Length; i++)
            {
                bags[i].Body.position = body.position + new Vector3(i == 0 ? -.30f : .30f, 1.0f, 0);
                bags[i].Body.rotation = Quaternion.Euler(0, 90, 0);
                bags[i].Body.constraints = RigidbodyConstraints.None;
                bags[i].Body.useGravity = true;
                bags[i].Body.linearVelocity = bags[i].Body.angularVelocity = Vector3.zero;
            }
            Physics.SyncTransforms();
            yield return WaitForCondition(() => bags.All(bag => bag.OnCart), 6, "Both settled suitcases must secure themselves to the cart.");
            foreach (var bag in bags) Assert.That(bag.CargoJoint.connectedBody, Is.SameAs(body));
            var offsets = bags.Select(bag => cart.transform.InverseTransformPoint(bag.Body.position)).ToArray();
            yield return ApproachCart(cart);
            var start = body.position;
            InputSystem.QueueStateEvent(padA, new GamepadState { leftStick = Vector2.up });
            yield return new WaitForSeconds(2.5f);
            QueueUse(padA, false); yield return null;
            Assert.That(body.position.z - start.z, Is.GreaterThan(2), "Loaded cart must actually travel.");
            var actor = bootstrap.Players[0];
            var heading = Quaternion.Euler(0, 20, 0) * Vector3.forward;
            yield return AimAtKeyScenarioPoint(actor, padA, () => actor.PlayerCamera.transform.position + heading * 5);
            yield return new WaitForSeconds(1.5f);
            TestContext.Out.WriteLine("Loaded cart yaw=" + body.rotation.eulerAngles.y + ", angularVelocity=" + body.angularVelocity +
                ", cart=" + body.position + ", actor=" + actor.transform.position);
            Assert.That(Mathf.DeltaAngle(0, body.rotation.eulerAngles.y), Is.InRange(8f, 32f), "A loaded cart must also steer.");
            for (int i = 0; i < bags.Length; i++)
            {
                Assert.That(bags[i].OnCart, Is.True, "Cargo joint must survive normal movement.");
                Assert.That(Vector3.Distance(offsets[i], cart.transform.InverseTransformPoint(bags[i].Body.position)), Is.LessThan(.12f));
            }
            TestContext.Out.WriteLine("Loaded cart travel=" + (body.position.z - start.z).ToString("F3") + "; two physical bags retained.");
            yield return AimAtKeyScenarioPoint(actor, padA, () => cart.handle.position);
            QueueUse(padA, true); yield return null; yield return null;
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That(LuggageCart.IsGuiding(0), Is.False);
            yield return new WaitForSeconds(.6f);
            // The handle collider intentionally occupies the rear. Walk around it to
            // grab the suitcase from the side rather than asserting a through-handle ray.
            yield return WalkBesideCart(cart.transform.TransformPoint(new Vector3(-1.35f, 0, -1.3f)));
            yield return WalkBesideCart(cart.transform.TransformPoint(new Vector3(-1.35f, 0, 0)));
            yield return GrabServiceSupply(bags[0]);
            Assert.That(bags[0].OnCart, Is.False, "Real F-grab must detach the cart joint.");
            Assert.That(bags[1].OnCart, Is.True, "The other suitcase stays aboard.");
            LogAssert.NoUnexpectedReceived();
        }
    }
}
