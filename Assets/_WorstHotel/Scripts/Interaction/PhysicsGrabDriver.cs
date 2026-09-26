using System;
using UnityEngine;

namespace WorstHotel
{
    /// <summary>A force-limited, damped physical grip. Only its kinematic hand is moved explicitly.</summary>
    public sealed class PhysicsGrabDriver : IDisposable
    {
        public Rigidbody Body { get; }
        public GrabPhysicsSettings Settings { get; }
        public Vector3 TargetPosition => hand ? hand.position : Vector3.zero;
        public Quaternion TargetRotation => hand ? hand.rotation : Quaternion.identity;
        public float CollisionRadius { get; }
        public bool IsValid => !disposed && Body && hand && joint && !Body.isKinematic;

        private readonly Rigidbody hand;
        private readonly ConfigurableJoint joint;
        private readonly Collider playerCollider;
        private readonly Collider[] bodyColliders;
        private readonly bool[] previousIgnorePairs;
        private readonly bool[] changedIgnorePairs;
        private readonly BodyState original;
        private readonly Quaternion viewToBody;
        private Vector3 targetVelocity;
        private bool disposed;

        public PhysicsGrabDriver(Rigidbody body, Quaternion viewRotation, Collider ignorePlayer, GrabPhysicsSettings settings)
        {
            if (!body || body.isKinematic) throw new ArgumentException("A dynamic body is required for a physical grip.", nameof(body));
            Body = body;
            Settings = (settings ?? new GrabPhysicsSettings()).ValidatedCopy();
            original = new BodyState(body);
            playerCollider = ignorePlayer;
            bodyColliders = body.GetComponentsInChildren<Collider>();
            previousIgnorePairs = new bool[bodyColliders.Length];
            changedIgnorePairs = new bool[bodyColliders.Length];
            float radius = Settings.minimumCastRadius;
            for (int i = 0; i < bodyColliders.Length; i++)
            {
                var collider = bodyColliders[i];
                if (!collider || !collider.enabled || collider.isTrigger || collider.attachedRigidbody != body) continue;
                radius = Mathf.Max(radius, Vector3.Distance(collider.bounds.center, body.worldCenterOfMass) + collider.bounds.extents.magnitude);
                if (!playerCollider) continue;
                previousIgnorePairs[i] = Physics.GetIgnoreCollision(collider, playerCollider);
                Physics.IgnoreCollision(collider, playerCollider, true);
                changedIgnorePairs[i] = true;
            }
            CollisionRadius = radius;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.linearDamping = Mathf.Max(body.linearDamping, Settings.linearDamping);
            body.angularDamping = Mathf.Max(body.angularDamping, Settings.angularDamping);
            body.maxLinearVelocity = Mathf.Min(body.maxLinearVelocity, Settings.maximumLinearSpeed);
            body.maxAngularVelocity = Mathf.Min(body.maxAngularVelocity, Settings.maximumAngularSpeed);
            body.maxDepenetrationVelocity = Mathf.Min(body.maxDepenetrationVelocity, Settings.maximumDepenetrationSpeed);
            body.solverIterations = Mathf.Max(body.solverIterations, Settings.solverIterations);
            body.solverVelocityIterations = Mathf.Max(body.solverVelocityIterations, Settings.solverVelocityIterations);

            var target = new GameObject("Damped physical grip");
            // A new kinematic anchor starts at the existing COM pose; acquiring cannot teleport the suitcase.
            target.transform.SetPositionAndRotation(body.worldCenterOfMass, body.rotation);
            hand = target.AddComponent<Rigidbody>();
            hand.isKinematic = true;
            hand.useGravity = false;
            hand.interpolation = RigidbodyInterpolation.Interpolate;
            viewToBody = Quaternion.Inverse(viewRotation) * body.rotation;
            joint = body.gameObject.AddComponent<ConfigurableJoint>();
            joint.connectedBody = hand;
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = body.centerOfMass;
            joint.connectedAnchor = Vector3.zero;
            joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Free;
            joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Free;
            joint.projectionMode = JointProjectionMode.None;
            joint.enableCollision = false;
            joint.enablePreprocessing = true;
            float omega = 2 * Mathf.PI * Settings.positionFrequency;
            var translation = new JointDrive
            {
                positionSpring = body.mass * omega * omega,
                positionDamper = 2 * body.mass * omega * Settings.positionDampingRatio,
                maximumForce = body.mass * Settings.maximumLinearAcceleration,
                useAcceleration = false
            };
            joint.xDrive = joint.yDrive = joint.zDrive = translation;
            float inertia = Mathf.Max(body.inertiaTensor.x, body.inertiaTensor.y, body.inertiaTensor.z);
            float angularOmega = 2 * Mathf.PI * Settings.angularFrequency;
            joint.rotationDriveMode = RotationDriveMode.Slerp;
            joint.targetRotation = Quaternion.identity;
            joint.slerpDrive = new JointDrive
            {
                positionSpring = inertia * angularOmega * angularOmega,
                positionDamper = 2 * inertia * angularOmega * Settings.angularDampingRatio,
                maximumForce = inertia * Settings.maximumAngularAcceleration,
                useAcceleration = false
            };
            body.WakeUp();
        }

        public void FixedStep(Vector3 desiredPosition, Quaternion viewRotation, float dt)
        {
            if (!IsValid || dt <= 0) return;
            Vector3 error = desiredPosition - hand.position;
            if (error.sqrMagnitude <= Settings.targetPositionDeadZone * Settings.targetPositionDeadZone)
            {
                desiredPosition = hand.position;
                targetVelocity = Vector3.zero;
            }
            Vector3 next = Vector3.SmoothDamp(hand.position, desiredPosition, ref targetVelocity,
                Settings.targetSmoothTime, Settings.maximumTargetSpeed, dt);
            hand.MovePosition(next);
            Quaternion desiredRotation = viewRotation * viewToBody;
            if (Quaternion.Angle(hand.rotation, desiredRotation) > Settings.targetAngleDeadZone)
                hand.MoveRotation(Quaternion.RotateTowards(hand.rotation, desiredRotation, Settings.maximumTargetAngularSpeed * dt));
            // No dynamic-body pose/velocity assignment, energy injection or per-frame angular hard-stop.
            // Linear gravity support remains a real spring equilibrium; angular motion has its own damping.
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (joint)
            {
                // Destroy is deferred until frame end: remove all drives first so release cannot add a final impulse.
                joint.xDrive = joint.yDrive = joint.zDrive = new JointDrive();
                joint.slerpDrive = new JointDrive();
                UnityEngine.Object.Destroy(joint);
            }
            if (hand) UnityEngine.Object.Destroy(hand.gameObject);
            if (Body) original.Restore(Body);
            if (playerCollider)
                for (int i = 0; i < bodyColliders.Length; i++)
                    if (changedIgnorePairs[i] && bodyColliders[i]) Physics.IgnoreCollision(bodyColliders[i], playerCollider, previousIgnorePairs[i]);
        }

        private readonly struct BodyState
        {
            private readonly float linearDamping, angularDamping, maxLinearVelocity, maxAngularVelocity, maxDepenetrationVelocity;
            private readonly int solverIterations, solverVelocityIterations;
            private readonly RigidbodyInterpolation interpolation;
            private readonly CollisionDetectionMode collisionMode;

            public BodyState(Rigidbody body)
            {
                linearDamping = body.linearDamping; angularDamping = body.angularDamping;
                maxLinearVelocity = body.maxLinearVelocity; maxAngularVelocity = body.maxAngularVelocity;
                maxDepenetrationVelocity = body.maxDepenetrationVelocity;
                solverIterations = body.solverIterations; solverVelocityIterations = body.solverVelocityIterations;
                interpolation = body.interpolation; collisionMode = body.collisionDetectionMode;
            }

            public void Restore(Rigidbody body)
            {
                body.linearDamping = linearDamping; body.angularDamping = angularDamping;
                body.maxLinearVelocity = maxLinearVelocity; body.maxAngularVelocity = maxAngularVelocity;
                body.maxDepenetrationVelocity = maxDepenetrationVelocity;
                body.solverIterations = solverIterations; body.solverVelocityIterations = solverVelocityIterations;
                body.interpolation = interpolation; body.collisionDetectionMode = collisionMode;
            }
        }
    }
}
