using System;
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
        private RoomKeyItem PhysicalKey(int roomId) => UnityEngine.Object.FindObjectsByType<RoomKeyItem>(FindObjectsSortMode.None)
            .Single(item => item.roomId == roomId);

        private IEnumerator AimAtKeyScenarioPoint(FirstPersonController actor, Gamepad pad, Func<Vector3> target)
        {
            float deadline = Time.realtimeSinceStartup + 6;
            while (Time.realtimeSinceStartup < deadline)
            {
                Vector3 delta = target() - actor.PlayerCamera.transform.position;
                float desiredYaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
                float yawError = Mathf.DeltaAngle(actor.transform.eulerAngles.y, desiredYaw);
                float desiredPitch = -Mathf.Atan2(delta.y, new Vector2(delta.x, delta.z).magnitude) * Mathf.Rad2Deg;
                float pitchError = desiredPitch - Mathf.DeltaAngle(0, actor.PlayerCamera.transform.localEulerAngles.x);
                if (Mathf.Abs(yawError) < 1.2f && Mathf.Abs(pitchError) < 1.2f) break;
                InputSystem.QueueStateEvent(pad, new GamepadState { rightStick = new Vector2(
                    Mathf.Abs(yawError) < 1.2f ? 0 : Mathf.Sign(yawError) * .35f,
                    Mathf.Abs(pitchError) < 1.2f ? 0 : -Mathf.Sign(pitchError) * .35f) });
                yield return null;
            }
            QueueUse(pad, false);
            yield return null; yield return null;
            Assert.That(Vector3.Angle(actor.PlayerCamera.transform.forward,
                target() - actor.PlayerCamera.transform.position), Is.LessThan(3), "Real look input did not acquire the intended key scenario target.");
        }

        private IEnumerator GrabFocusedRoomKey(FirstPersonController actor, Gamepad pad, RoomKeyItem item)
        {
            yield return AimAtKeyScenarioPoint(actor, pad, () => item.Body.worldCenterOfMass);
            Assert.That(actor.Interactor.FocusedPickup, Is.SameAs(item.GetComponent<PhysicsPickup>()), "A real numbered-key collider must be in the acquisition ray.");
            QueueGrab(pad, true);
            yield return null; yield return null;
            QueueGrab(pad, false);
            yield return null; yield return null;
            Assert.That(actor.Interactor.HeldBody, Is.SameAs(item.Body));
            Assert.That(item.State.Location, Is.EqualTo(RoomKeyLocation.HeldByPlayer));
            Assert.That(item.State.PlayerId, Is.EqualTo(actor.ActorId));
            Assert.That(item.Body.isKinematic, Is.False);
            Assert.That(item.Body.GetComponent<ConfigurableJoint>(), Is.Not.Null, "Key transport must use the existing physical grab joint.");
        }

        private IEnumerator TakeRoomKeyFromActualRack(int actorId, int roomId)
        {
            var actor = bootstrap.Players[actorId]; var pad = actorId == 0 ? padA : padB;
            var rack = UnityEngine.Object.FindAnyObjectByType<RoomKeyRack>();
            Assert.That(rack, Is.Not.Null);
            Assert.That(rack.keys.Select(item => item.roomId).OrderBy(id => id), Is.EqualTo(Enumerable.Range(101, 6)));
            var key = PhysicalKey(roomId);
            Assert.That(key.rackAnchor, Is.Not.Null);
            Assert.That(Vector3.Distance(key.Body.position, key.rackAnchor.position), Is.LessThan(.04f));
            var pose = new GameObject("Physical key rack approach");
            pose.transform.SetPositionAndRotation(new Vector3(key.rackAnchor.position.x, .08f,
                key.rackAnchor.position.z - 1.45f), Quaternion.identity);
            actor.ResetToSpawn(pose.transform);
            UnityEngine.Object.Destroy(pose);
            yield return WaitForGroundContact(actor);
            yield return GrabFocusedRoomKey(actor, pad, key);
        }

        private IEnumerator WalkWithKey(FirstPersonController actor, Gamepad pad, RoomKeyItem key, Vector3 destination)
        {
            float deadline = Time.realtimeSinceStartup + 8;
            while (HorizontalDistance(actor.transform.position, destination) > .11f && Time.realtimeSinceStartup < deadline)
            {
                Assert.That(actor.Interactor.HeldBody, Is.SameAs(key.Body), "The route cannot pass by releasing the carried key.");
                Vector3 delta = destination - actor.transform.position; delta.y = 0;
                Vector3 local = actor.transform.InverseTransformDirection(delta.normalized);
                float speed = Mathf.Clamp(delta.magnitude * 1.5f, .22f, .65f);
                InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = new Vector2(local.x, local.z) * speed });
                yield return null;
            }
            QueueUse(pad, false);
            yield return null; yield return null;
            Assert.That(HorizontalDistance(actor.transform.position, destination), Is.LessThan(.16f),
                "Staff could not carry the rack key along the clear reception approach; position=" + actor.transform.position);
            Assert.That(actor.Interactor.HeldBody, Is.SameAs(key.Body));
        }

        private IEnumerator CarryRackKeyToReceptionGuest(int actorId, int roomId, GuestReceptionInteraction guest)
        {
            var actor = bootstrap.Players[actorId]; var pad = actorId == 0 ? padA : padB;
            var key = PhysicalKey(roomId);
            yield return AimAtKeyScenarioPoint(actor, pad, () => actor.PlayerCamera.transform.position + Vector3.forward * 5);
            // The authored rack faces south. Walk into the open lobby and approach the actual queue
            // from south; neither a held body nor its carrier is repositioned by this route.
            Vector3 aim = guest.transform.TransformPoint(guest.GetComponent<CapsuleCollider>().center);
            yield return WalkWithKey(actor, pad, key, new Vector3(actor.transform.position.x, 0, -.15f));
            yield return WalkWithKey(actor, pad, key, new Vector3(aim.x, 0, -.15f));
            yield return AimAtKeyScenarioPoint(actor, pad, () => guest.transform.TransformPoint(guest.GetComponent<CapsuleCollider>().center));
            Assert.That(actor.Interactor.Focused, Is.SameAs(guest), "The actor's own carried key must not occlude the guest raycast.");
        }

        private IEnumerator GiveHeldKeyByInstantUse(int actorId, GuestStay guest, int roomId, bool relocating = false)
        {
            var actor = bootstrap.Players[actorId]; var pad = actorId == 0 ? padA : padB;
            var key = PhysicalKey(roomId);
            Assert.That(actor.Interactor.HeldBody, Is.SameAs(key.Body));
            float before = Time.realtimeSinceStartup;
            QueueUse(pad, true);
            yield return null; yield return null;
            QueueUse(pad, false);
            yield return WaitForCondition(() => key.State.Location == RoomKeyLocation.HeldByGuest &&
                (relocating ? guest.Agent.IsRelocating : guest.Agent.CheckedIn), .5f, "A short contextual press did not hand over the correct physical key.");
            Assert.That(Time.realtimeSinceStartup - before, Is.LessThan(1), "Key handoff must not retain the former five-second hold interaction.");
            yield return null; yield return null;
            Assert.That(actor.Interactor.HeldBody, Is.Null);
            Assert.That(key.Body.GetComponent<ConfigurableJoint>(), Is.Null);
            Assert.That(key.State.GuestId, Is.EqualTo(guest.GuestId));
            Assert.That(key.State.PlayerId, Is.Null);
        }

        [UnityTest]
        public IEnumerator WrongRoomKeyDoesNotCheckInAndDropRegrabAndNewGamePreserveUniquePhysicalKeys()
        {
            var session = GameSession.Instance;
            var offer = session.Plan.Applications.First();
            session.Assign(0, offer.Id, 101, session.Economy.MinPrice);
            session.CommitPlan(0);
            var guest = session.Simulation.Guests.Single();
            session.AdvanceTime(guest.Agent.ArrivalTime + .2f);
            yield return WaitForCondition(() => guest.Agent.State == GuestAgentState.WaitingForCheckIn, 18, "Guest did not reach reception.");
            var target = UnityEngine.Object.FindObjectsByType<GuestReceptionInteraction>(FindObjectsSortMode.None).Single(item => item.GuestId == guest.GuestId);
            Assert.That(session.Simulation.Keys.PickUp(0, 101).Success, Is.True);
            Assert.That(session.CheckInGuest(0, guest.GuestId).Success, Is.False,
                "Model ownership alone cannot bypass the runtime requirement to physically carry the actual key.");
            Assert.That(guest.Agent.CheckedIn, Is.False);
            Assert.That(session.Simulation.Keys.Drop(0, 101).Success, Is.True);
            Assert.That(session.Simulation.Keys.ReturnToRack(101).Success, Is.True);
            yield return TakeRoomKeyFromActualRack(0, 102);
            var wrong = PhysicalKey(102);
            yield return CarryRackKeyToReceptionGuest(0, 102, target);
            QueueUse(padA, true); yield return null; yield return null;
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That(guest.Agent.CheckedIn, Is.False);
            Assert.That(session.Rooms.Single(room => room.Profile.Id == 101).ReservedGuestId, Is.EqualTo(guest.GuestId));
            Assert.That(bootstrap.Players[0].Interactor.HeldBody, Is.SameAs(wrong.Body));
            Assert.That(wrong.State.PlayerId, Is.EqualTo(0));
            Assert.That(target.HoldProgress, Is.Zero);
            Assert.That(session.CheckInGuest(1, guest.GuestId).Success, Is.False, "A different actor cannot hand over a key they do not physically carry.");

            QueueGrab(padA, true); yield return null; yield return null;
            QueueGrab(padA, false);
            yield return PhysicsSteps(30);
            Assert.That(bootstrap.Players[0].Interactor.HeldBody, Is.Null);
            Assert.That(wrong.State.Location, Is.EqualTo(RoomKeyLocation.Dropped));
            Assert.That(wrong.Body.GetComponent<ConfigurableJoint>(), Is.Null);
            yield return GrabFocusedRoomKey(bootstrap.Players[0], padA, wrong);
            Assert.That(wrong.State.PlayerId, Is.EqualTo(0));
            var previous = session.Simulation;
            session.NewGame();
            yield return null; yield return null; yield return null;
            Assert.That(session.Simulation, Is.Not.SameAs(previous));
            Assert.That(bootstrap.Players.All(player => player.Interactor.HeldBody == null), Is.True);
            var keys = UnityEngine.Object.FindObjectsByType<RoomKeyItem>(FindObjectsSortMode.None);
            Assert.That(keys.Length, Is.EqualTo(6));
            Assert.That(keys.Select(key => key.roomId).Distinct().Count(), Is.EqualTo(6));
            foreach (var key in keys)
            {
                Assert.That(key.State.Location, Is.EqualTo(RoomKeyLocation.OnRack));
                Assert.That(key.State.PlayerId, Is.Null);
                Assert.That(key.State.GuestId, Is.Null);
                Assert.That(key.Body.GetComponent<ConfigurableJoint>(), Is.Null);
                Assert.That(Vector3.Distance(key.Body.position, key.rackAnchor.position), Is.LessThan(.04f));
            }
            LogAssert.NoUnexpectedReceived();
        }

        // Existing relocation route fixture arranges a dropped target key in the clear room strip.
        // Rack acquisition/transport has separate coverage; this verifies physical pickup and
        // contextual guest exchange without introducing a second long reception-to-room tour.
        private IEnumerator GiveDroppedFixtureKeyForMove(int actorId, GuestStay guest, int roomId, GuestReceptionInteraction target)
        {
            var session = GameSession.Instance; var actor = bootstrap.Players[actorId]; var pad = actorId == 0 ? padA : padB;
            yield return FaceQuietGuestFromInnerLane(actor, pad, target, target.GetComponent<CapsuleCollider>());
            var key = PhysicalKey(roomId);
            Assert.That(session.Simulation.Keys.PickUp(actorId, roomId).Success, Is.True);
            Assert.That(session.Simulation.Keys.Drop(actorId, roomId).Success, Is.True);
            yield return null; yield return null;
            Vector3 placement = actor.transform.position + actor.transform.forward * .8f;
            placement.y = .3f;
            key.Body.position = placement; key.Body.rotation = Quaternion.identity;
            key.Body.linearVelocity = key.Body.angularVelocity = Vector3.zero;
            yield return PhysicsSteps(15);
            yield return GrabFocusedRoomKey(actor, pad, key);
            yield return AimAtKeyScenarioPoint(actor, pad, () => target.transform.TransformPoint(target.GetComponent<CapsuleCollider>().center));
            Assert.That(actor.Interactor.Focused, Is.SameAs(target));
            yield return GiveHeldKeyByInstantUse(actorId, guest, roomId, relocating: true);
        }
    }
}
