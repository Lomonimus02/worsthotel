using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace WorstHotel.Tests
{
    public sealed partial class Phase1PlayModeTests
    {
        private IEnumerator GrabPhysicalLinen(int actorId, LinenBundleItem bundle)
        {
            var actor = bootstrap.Players[actorId]; var pad = actorId == 0 ? padA : padB;
            yield return AimAtKeyScenarioPoint(actor, pad, () => bundle.Body.worldCenterOfMass);
            Assert.That(actor.Interactor.FocusedPickup, Is.SameAs(bundle.GetComponent<PhysicsPickup>()));
            QueueGrab(pad, true); yield return null; yield return null;
            QueueGrab(pad, false); yield return null; yield return null;
            Assert.That(actor.Interactor.HeldBody, Is.SameAs(bundle.Body));
            Assert.That(bundle.Body.isKinematic, Is.False);
            Assert.That(bundle.Body.GetComponent<ConfigurableJoint>(), Is.Not.Null);
        }

        private IEnumerator CarryLinenTo(int actorId, LinenBundleItem bundle, Vector3 destination)
        {
            var actor = bootstrap.Players[actorId]; var pad = actorId == 0 ? padA : padB;
            Vector3 facing = destination - actor.transform.position; facing.y = 0;
            if (facing.sqrMagnitude > .01f)
            {
                Vector3 direction = facing.normalized;
                yield return AimAtKeyScenarioPoint(actor, pad, () => actor.PlayerCamera.transform.position + direction * 5);
            }
            float deadline = Time.realtimeSinceStartup + 18;
            while (HorizontalDistance(actor.transform.position, destination) > .12f && Time.realtimeSinceStartup < deadline)
            {
                if (bundle) Assert.That(actor.Interactor.HeldBody, Is.SameAs(bundle.Body), "No route segment can complete by dropping its physical linen.");
                Vector3 delta = destination - actor.transform.position; delta.y = 0;
                Vector3 local = actor.transform.InverseTransformDirection(delta.normalized);
                float speed = Mathf.Clamp(delta.magnitude * 1.4f, .24f, .8f);
                InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = new Vector2(local.x, local.z) * speed });
                yield return null;
            }
            QueueUse(pad, false); yield return null; yield return null;
            Assert.That(HorizontalDistance(actor.transform.position, destination), Is.LessThan(.18f),
                "Linen route was blocked before " + destination + "; actor=" + actor.transform.position +
                (bundle ? "; body=" + bundle.Body.position : " (empty-handed walk)"));
            if (bundle) Assert.That(actor.Interactor.HeldBody, Is.SameAs(bundle.Body));
            yield return PhysicsSteps(8);
            if (bundle)
            {
                float distance = Vector3.Distance(bundle.Body.worldCenterOfMass, actor.PlayerCamera.transform.position);
                string diagnostic = "Linen segment " + bundle.itemId + ": destination=" + destination.ToString("F4") +
                    "; actor=" + actor.transform.position.ToString("F4") + "; camera=" + actor.PlayerCamera.transform.position.ToString("F4") +
                    "; body=" + bundle.Body.position.ToString("F4") + "; COM=" + bundle.Body.worldCenterOfMass.ToString("F4") +
                    "; grabTarget=" + actor.Interactor.GrabTargetPosition.ToString("F4") +
                    "; velocity=" + bundle.Body.linearVelocity.ToString("F4") + "; angularVelocity=" + bundle.Body.angularVelocity.ToString("F4") +
                    "; cameraDistance=" + distance.ToString("F4") +
                    "; targetError=" + Vector3.Distance(bundle.Body.worldCenterOfMass, actor.Interactor.GrabTargetPosition).ToString("F4") +
                    "; held=" + (actor.Interactor.HeldBody == bundle.Body);
                TestContext.WriteLine(diagnostic);
                Assert.That(distance, Is.LessThan(2), "The actual linen must follow its carrier through the doorway/corridor. " + diagnostic);
            }
        }

        private IEnumerator PositionEmptyActorForLinen(int actorId, Vector3 position, Vector3 target)
        {
            var actor = bootstrap.Players[actorId];
            Assert.That(actor.Interactor.HeldBody, Is.Null, "Only initial empty-handed scenario arrangement may reposition an actor.");
            var pose = new GameObject("Linen test initial approach");
            Vector3 forward = target - position; forward.y = 0;
            pose.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward));
            actor.ResetToSpawn(pose.transform);
            Object.Destroy(pose);
            yield return WaitForGroundContact(actor);
        }
    }
}
