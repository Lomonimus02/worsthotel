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
    public sealed class GrabWallContactProbe : MonoBehaviour
    {
        public Collider Wall;
        public int Contacts;
        private void OnCollisionEnter(Collision collision) { if (collision.collider == Wall) Contacts++; }
        private void OnCollisionStay(Collision collision) { if (collision.collider == Wall) Contacts++; }
    }

    public sealed partial class Phase1PlayModeTests
    {
        private Rigidbody suitcaseBody;
        private PhysicsPickup suitcasePickup;
        private float earlySpeed, earlyAngularSpeed, lateSpeed, lateAngularSpeed, latePositionError, lateAngleError;

        private static IEnumerator PhysicsSteps(int count)
        {
            for (int i = 0; i < count; i++) yield return new WaitForFixedUpdate();
        }

        private static void QueueGrab(Gamepad pad, bool pressed) => InputSystem.QueueStateEvent(pad,
            pressed ? new GamepadState().WithButton(GamepadButton.RightShoulder) : new GamepadState());

        private IEnumerator PrepareRealSuitcase(float mass = 2.5f, Action<Rigidbody> beforeGrab = null, bool offCentre = true)
        {
            var player = bootstrap.Players[0];
            player.Interactor.ReleaseGrab();
            player.SetUIBlocked(false);
            player.Interactor.enabled = true;
            QueueGrab(padA, false);
            QueueGrab(padB, false);
            suitcasePickup = UnityEngine.Object.FindObjectsByType<PhysicsPickup>(FindObjectsSortMode.None)
                .First(pickup => pickup.itemName == "Guest suitcase");
            suitcaseBody = suitcasePickup.GetComponent<Rigidbody>();
            // Scenario arrangement occurs before acquisition; production code never teleports a held dynamic body.
            suitcaseBody.position = new Vector3(0, 0.42f, 3);
            suitcaseBody.rotation = Quaternion.identity;
            suitcaseBody.linearVelocity = Vector3.zero;
            suitcaseBody.angularVelocity = Vector3.zero;
            suitcaseBody.mass = mass;
            suitcaseBody.ResetInertiaTensor();
            beforeGrab?.Invoke(suitcaseBody);
            var pose = new GameObject("Suitcase test approach");
            pose.transform.SetPositionAndRotation(new Vector3(0, 0.08f, 1), Quaternion.identity);
            player.ResetToSpawn(pose.transform);
            pose.transform.position = new Vector3(7, 0.08f, 2);
            bootstrap.Players[1].ResetToSpawn(pose.transform);
            UnityEngine.Object.Destroy(pose);
            yield return PhysicsSteps(8);
            float lateralHitOffset = offCentre ? 0.22f : 0;
            Vector3 aim = suitcaseBody.worldCenterOfMass + Vector3.right * lateralHitOffset;
            Vector3 flat = aim - player.PlayerCamera.transform.position; flat.y = 0;
            player.transform.rotation = Quaternion.LookRotation(flat, Vector3.up);
            yield return AimSuitcasePitch(() => suitcaseBody.worldCenterOfMass + Vector3.right * lateralHitOffset);
            Assert.That(player.Interactor.FocusedPickup, Is.SameAs(suitcasePickup), "The real pickup collider must be acquired through the player's raycast.");
            QueueGrab(padA, true);
            yield return null;
            yield return null;
            QueueGrab(padA, false);
            yield return null;
            Assert.That(player.Interactor.HeldBody, Is.SameAs(suitcaseBody));
            Assert.That(suitcaseBody.isKinematic, Is.False);
            var grip = suitcaseBody.GetComponent<ConfigurableJoint>();
            Assert.That(grip, Is.Not.Null);
            Assert.That(Vector3.Distance(grip.anchor, suitcaseBody.centerOfMass), Is.LessThan(0.0001f),
                "An off-centre ray hit must not create a freely rotating pendulum grip.");
            yield return AimSuitcasePitch(() => player.PlayerCamera.transform.position + player.transform.forward * 4);
            yield return PhysicsSteps(60);
        }

        private IEnumerator AimSuitcasePitch(Func<Vector3> target)
        {
            var player = bootstrap.Players[0];
            float deadline = Time.realtimeSinceStartup + 5;
            while (Time.realtimeSinceStartup < deadline)
            {
                Vector3 delta = target() - player.PlayerCamera.transform.position;
                float desired = -Mathf.Atan2(delta.y, new Vector2(delta.x, delta.z).magnitude) * Mathf.Rad2Deg;
                float current = Mathf.DeltaAngle(0, player.PlayerCamera.transform.localEulerAngles.x);
                float error = desired - current;
                if (Mathf.Abs(error) < 1.5f) break;
                InputSystem.QueueStateEvent(padA, new GamepadState { rightStick = new Vector2(0, -Mathf.Sign(error) * 0.45f) });
                yield return null;
            }
            QueueGrab(padA, false);
            yield return null;
            yield return null;
        }

        private IEnumerator MeasureStoppedSuitcase(string label)
        {
            earlySpeed = earlyAngularSpeed = lateSpeed = lateAngularSpeed = latePositionError = lateAngleError = 0;
            float lateLinearSquares = 0, lateAngularSquares = 0;
            int lateSamples = 0;
            var actor = bootstrap.Players[0].Interactor;
            for (int tick = 0; tick < 150; tick++)
            {
                yield return new WaitForFixedUpdate();
                Assert.That(actor.HeldBody, Is.SameAs(suitcaseBody), "A stability check cannot pass by silently dropping the suitcase.");
                float speed = suitcaseBody.linearVelocity.magnitude, angular = suitcaseBody.angularVelocity.magnitude;
                Assert.That(float.IsNaN(speed) || float.IsInfinity(speed) || float.IsNaN(angular) || float.IsInfinity(angular), Is.False);
                if (tick < 25) { earlySpeed = Mathf.Max(earlySpeed, speed); earlyAngularSpeed = Mathf.Max(earlyAngularSpeed, angular); }
                if (tick < 100) continue;
                lateSamples++;
                lateLinearSquares += speed * speed;
                lateAngularSquares += angular * angular;
                latePositionError = Mathf.Max(latePositionError, Vector3.Distance(suitcaseBody.worldCenterOfMass, actor.GrabTargetPosition));
                lateAngleError = Mathf.Max(lateAngleError, Quaternion.Angle(suitcaseBody.rotation, actor.GrabTargetRotation));
            }
            lateSpeed = Mathf.Sqrt(lateLinearSquares / lateSamples);
            lateAngularSpeed = Mathf.Sqrt(lateAngularSquares / lateSamples);
            TestContext.WriteLine(label + ": mass=" + suitcaseBody.mass.ToString("F2") +
                ", early peak speed=" + earlySpeed.ToString("F4") + ", late RMS speed=" + lateSpeed.ToString("F4") +
                ", early peak angular=" + earlyAngularSpeed.ToString("F4") + ", late RMS angular=" + lateAngularSpeed.ToString("F4") +
                ", late maximum COM error=" + latePositionError.ToString("F4") + ", late maximum angle error=" + lateAngleError.ToString("F3"));
            Assert.That(lateSpeed, Is.LessThan(0.06f), label + ": persistent translation after stopping.");
            Assert.That(lateAngularSpeed, Is.LessThan(0.06f), label + ": persistent rotation after stopping.");
            Assert.That(latePositionError, Is.LessThan(0.09f), label + ": suitcase never reached its physical hand, allowing gravity sag.");
            Assert.That(lateAngleError, Is.LessThan(3), label + ": suitcase never reached its intended carry orientation.");
        }

        [UnityTest]
        public IEnumerator HeldSuitcaseSidewaysShakeDecaysAcrossLightNormalAndHeavyMasses()
        {
            foreach (float mass in new[] { 0.5f, 2.5f, 12f })
            {
                yield return PrepareRealSuitcase(mass);
                InputSystem.QueueStateEvent(padA, new GamepadState { leftStick = Vector2.right });
                yield return PhysicsSteps(18);
                InputSystem.QueueStateEvent(padA, new GamepadState { leftStick = Vector2.left });
                yield return PhysicsSteps(36);
                InputSystem.QueueStateEvent(padA, new GamepadState { leftStick = Vector2.right });
                yield return PhysicsSteps(18);
                QueueGrab(padA, false);
                yield return null;
                yield return MeasureStoppedSuitcase("Sideways shake");
                Assert.That(earlySpeed, Is.GreaterThan(0.10f), "The suitcase must actually be excited before decay is measured.");
                Assert.That(lateSpeed, Is.LessThan(earlySpeed * 0.2f), "Oscillation amplitude must visibly decay, not merely stay bounded.");
                bootstrap.Players[0].Interactor.ReleaseGrab();
                yield return null;
            }
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator HeldSuitcaseRapidLookRotationDampsToItsCarryOrientation()
        {
            yield return PrepareRealSuitcase();
            InputSystem.QueueStateEvent(padA, new GamepadState { rightStick = Vector2.right });
            yield return PhysicsSteps(20);
            InputSystem.QueueStateEvent(padA, new GamepadState { rightStick = Vector2.left });
            yield return PhysicsSteps(40);
            InputSystem.QueueStateEvent(padA, new GamepadState { rightStick = Vector2.right });
            yield return PhysicsSteps(20);
            QueueGrab(padA, false);
            yield return null;
            yield return MeasureStoppedSuitcase("Rapid camera rotation");
            Assert.That(earlyAngularSpeed, Is.GreaterThan(0.10f), "The actual camera input must excite rotation.");
            Assert.That(lateAngularSpeed, Is.LessThan(earlyAngularSpeed * 0.2f));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator HeldSuitcaseTouchesWallWithoutTunnellingOrUnboundedEnergyThenSettles()
        {
            yield return PrepareRealSuitcase(offCentre: false);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Suitcase physical wall test";
            wall.transform.position = new Vector3(0, 1.5f, 4.2f);
            wall.transform.localScale = new Vector3(5, 3, 0.2f);
            var wallCollider = wall.GetComponent<Collider>();
            var probe = suitcaseBody.gameObject.AddComponent<GrabWallContactProbe>(); probe.Wall = wallCollider;
            float maximumSpeed = 0, maximumAngularSpeed = 0;
            InputSystem.QueueStateEvent(padA, new GamepadState { leftStick = Vector2.up });
            for (int tick = 0; tick < 70; tick++)
            {
                yield return new WaitForFixedUpdate();
                Assert.That(bootstrap.Players[0].Interactor.HeldBody, Is.SameAs(suitcaseBody));
                maximumSpeed = Mathf.Max(maximumSpeed, suitcaseBody.linearVelocity.magnitude);
                maximumAngularSpeed = Mathf.Max(maximumAngularSpeed, suitcaseBody.angularVelocity.magnitude);
                var shell = suitcaseBody.GetComponentsInChildren<Collider>().First(c => c.enabled && !c.isTrigger);
                Assert.That(shell.bounds.max.z, Is.LessThan(wallCollider.bounds.min.z + 0.07f), "Suitcase tunnelled through the solid wall.");
            }
            QueueGrab(padA, false);
            yield return PhysicsSteps(30);
            Assert.That(probe.Contacts, Is.GreaterThan(0), "This must exercise a real wall contact, not just unobstructed following.");
            Assert.That(maximumSpeed, Is.LessThan(8.5f));
            Assert.That(maximumAngularSpeed, Is.LessThan(8));
            InputSystem.QueueStateEvent(padA, new GamepadState { leftStick = Vector2.down });
            yield return PhysicsSteps(35);
            QueueGrab(padA, false);
            yield return null;
            yield return MeasureStoppedSuitcase("After physical wall contact");
            TestContext.WriteLine("Wall contact count=" + probe.Contacts + ", peak speed=" + maximumSpeed.ToString("F3") +
                ", peak angular=" + maximumAngularSpeed.ToString("F3"));
            UnityEngine.Object.Destroy(wall);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator DropBlockAndDisableRestoreBodyPropertiesAndPreexistingCollisionPairs()
        {
            for (int exitPath = 0; exitPath < 3; exitPath++)
            {
                Collider normal = null, trigger = null, disabled = null;
                bool triggerIgnored = false, disabledIgnored = false;
                var capsule = bootstrap.Players[0].BodyCollider;
                yield return PrepareRealSuitcase(2.5f, body =>
                {
                    body.linearDamping = 0.17f; body.angularDamping = 0.23f;
                    body.maxLinearVelocity = 11; body.maxAngularVelocity = 9; body.maxDepenetrationVelocity = 4;
                    body.solverIterations = 4; body.solverVelocityIterations = 2;
                    body.interpolation = RigidbodyInterpolation.None; body.collisionDetectionMode = CollisionDetectionMode.Discrete;
                    body.constraints = RigidbodyConstraints.FreezeRotationZ;
                    normal = body.GetComponentsInChildren<Collider>().First(c => c.enabled && !c.isTrigger);
                    Physics.IgnoreCollision(normal, capsule, false);
                    trigger = body.gameObject.AddComponent<BoxCollider>(); trigger.isTrigger = true;
                    ((BoxCollider)trigger).size = Vector3.one * 0.1f;
                    Physics.IgnoreCollision(trigger, capsule, true);
                    triggerIgnored = Physics.GetIgnoreCollision(trigger, capsule);
                    disabled = body.gameObject.AddComponent<BoxCollider>();
                    ((BoxCollider)disabled).size = Vector3.one * 0.1f;
                    Physics.IgnoreCollision(disabled, capsule, true); disabled.enabled = false;
                    disabledIgnored = Physics.GetIgnoreCollision(disabled, capsule);
                }, false);
                var grip = suitcaseBody.GetComponent<ConfigurableJoint>();
                var hand = grip.connectedBody;
                Assert.That(Physics.GetIgnoreCollision(normal, capsule), Is.True);
                Assert.That(suitcaseBody.mass, Is.EqualTo(2.5f));
                Assert.That(suitcaseBody.useGravity, Is.True);
                Assert.That(suitcaseBody.constraints, Is.EqualTo(RigidbodyConstraints.FreezeRotationZ));
                if (exitPath == 0) { QueueGrab(padA, true); yield return null; yield return null; QueueGrab(padA, false); }
                else if (exitPath == 1) bootstrap.Players[0].SetUIBlocked(true);
                else bootstrap.Players[0].Interactor.enabled = false;
                yield return null;
                yield return null;
                Assert.That(bootstrap.Players[0].Interactor.HeldBody, Is.Null);
                Assert.That(suitcaseBody.GetComponent<ConfigurableJoint>(), Is.Null);
                Assert.That(hand == null, Is.True, "The kinematic hand must not leak after release.");
                Assert.That(suitcaseBody.linearDamping, Is.EqualTo(0.17f));
                Assert.That(suitcaseBody.angularDamping, Is.EqualTo(0.23f));
                Assert.That(suitcaseBody.maxLinearVelocity, Is.EqualTo(11));
                Assert.That(suitcaseBody.maxAngularVelocity, Is.EqualTo(9));
                Assert.That(suitcaseBody.maxDepenetrationVelocity, Is.EqualTo(4));
                Assert.That(suitcaseBody.solverIterations, Is.EqualTo(4));
                Assert.That(suitcaseBody.solverVelocityIterations, Is.EqualTo(2));
                Assert.That(suitcaseBody.interpolation, Is.EqualTo(RigidbodyInterpolation.None));
                Assert.That(suitcaseBody.collisionDetectionMode, Is.EqualTo(CollisionDetectionMode.Discrete));
                Assert.That(suitcaseBody.mass, Is.EqualTo(2.5f));
                Assert.That(suitcaseBody.useGravity, Is.True);
                Assert.That(suitcaseBody.constraints, Is.EqualTo(RigidbodyConstraints.FreezeRotationZ));
                Assert.That(Physics.GetIgnoreCollision(normal, capsule), Is.False);
                Assert.That(Physics.GetIgnoreCollision(trigger, capsule), Is.EqualTo(triggerIgnored));
                Assert.That(Physics.GetIgnoreCollision(disabled, capsule), Is.EqualTo(disabledIgnored));
                UnityEngine.Object.Destroy(trigger); UnityEngine.Object.Destroy(disabled);
                yield return null;
                bootstrap.Players[0].SetUIBlocked(false);
                bootstrap.Players[0].Interactor.enabled = true;
            }
            LogAssert.NoUnexpectedReceived();
        }
    }
}
