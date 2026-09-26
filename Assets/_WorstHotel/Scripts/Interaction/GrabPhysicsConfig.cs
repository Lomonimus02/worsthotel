using System;
using UnityEngine;

namespace WorstHotel
{
    [Serializable]
    public sealed class GrabPhysicsSettings
    {
        [Header("Damped joint response")]
        [Range(0.5f, 8)] public float positionFrequency = 3.5f;
        [Range(0.7f, 2)] public float positionDampingRatio = 1.1f;
        [Range(0.5f, 6)] public float angularFrequency = 2.5f;
        [Range(0.7f, 2)] public float angularDampingRatio = 1.1f;
        [Min(10)] public float maximumLinearAcceleration = 45;
        [Min(1)] public float maximumAngularAcceleration = 80;
        [Header("Physical hand motion")]
        [Min(0.04f)] public float targetSmoothTime = 0.12f;
        [Min(0.1f)] public float maximumTargetSpeed = 6;
        [Min(1)] public float maximumTargetAngularSpeed = 240;
        [Min(0)] public float targetPositionDeadZone = 0.002f;
        [Min(0)] public float targetAngleDeadZone = 0.15f;
        [Header("Temporary held-body properties")]
        [Min(0)] public float linearDamping = 1;
        [Min(0)] public float angularDamping = 3;
        [Min(0.1f)] public float maximumLinearSpeed = 7;
        [Min(0.1f)] public float maximumAngularSpeed = 6;
        [Min(0.1f)] public float maximumDepenetrationSpeed = 2;
        [Range(1, 30)] public int solverIterations = 12;
        [Range(1, 30)] public int solverVelocityIterations = 8;
        [Header("Reach and collision")]
        [Min(0.01f)] public float minimumCastRadius = 0.08f;
        [Min(0)] public float wallClearance = 0.06f;
        [Min(0.1f)] public float minimumHoldDistance = 0.6f;
        [Min(1)] public float releaseDistance = 4.4f;

        public GrabPhysicsSettings ValidatedCopy()
        {
            var copy = (GrabPhysicsSettings)MemberwiseClone();
            var defaults = new GrabPhysicsSettings();
            copy.positionFrequency = FiniteRange(positionFrequency, 0.5f, 8, defaults.positionFrequency);
            copy.positionDampingRatio = FiniteRange(positionDampingRatio, 0.7f, 2, defaults.positionDampingRatio);
            copy.angularFrequency = FiniteRange(angularFrequency, 0.5f, 6, defaults.angularFrequency);
            copy.angularDampingRatio = FiniteRange(angularDampingRatio, 0.7f, 2, defaults.angularDampingRatio);
            copy.maximumLinearAcceleration = FiniteRange(maximumLinearAcceleration, 10, 150, defaults.maximumLinearAcceleration);
            copy.maximumAngularAcceleration = FiniteRange(maximumAngularAcceleration, 1, 300, defaults.maximumAngularAcceleration);
            copy.targetSmoothTime = FiniteRange(targetSmoothTime, 0.04f, 1, defaults.targetSmoothTime);
            copy.maximumTargetSpeed = FiniteRange(maximumTargetSpeed, 0.1f, 15, defaults.maximumTargetSpeed);
            copy.maximumTargetAngularSpeed = FiniteRange(maximumTargetAngularSpeed, 1, 720, defaults.maximumTargetAngularSpeed);
            copy.targetPositionDeadZone = FiniteRange(targetPositionDeadZone, 0, 0.02f, defaults.targetPositionDeadZone);
            copy.targetAngleDeadZone = FiniteRange(targetAngleDeadZone, 0, 2, defaults.targetAngleDeadZone);
            copy.linearDamping = FiniteRange(linearDamping, 0, 10, defaults.linearDamping);
            copy.angularDamping = FiniteRange(angularDamping, 0, 20, defaults.angularDamping);
            copy.maximumLinearSpeed = FiniteRange(maximumLinearSpeed, 0.1f, 20, defaults.maximumLinearSpeed);
            copy.maximumAngularSpeed = FiniteRange(maximumAngularSpeed, 0.1f, 20, defaults.maximumAngularSpeed);
            copy.maximumDepenetrationSpeed = FiniteRange(maximumDepenetrationSpeed, 0.1f, 10, defaults.maximumDepenetrationSpeed);
            copy.solverIterations = Mathf.Clamp(solverIterations, 1, 30);
            copy.solverVelocityIterations = Mathf.Clamp(solverVelocityIterations, 1, 30);
            copy.minimumCastRadius = FiniteRange(minimumCastRadius, 0.01f, 0.5f, defaults.minimumCastRadius);
            copy.wallClearance = FiniteRange(wallClearance, 0, 0.3f, defaults.wallClearance);
            copy.minimumHoldDistance = FiniteRange(minimumHoldDistance, 0.1f, 2, defaults.minimumHoldDistance);
            copy.releaseDistance = FiniteRange(releaseDistance, 1, 8, defaults.releaseDistance);
            return copy;
        }

        private static float FiniteRange(float value, float minimum, float maximum, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, minimum, maximum);
    }

    [CreateAssetMenu(menuName = "Worst Hotel/Grab physics configuration")]
    public sealed class GrabPhysicsConfig : ScriptableObject
    {
        public GrabPhysicsSettings settings = new GrabPhysicsSettings();
        public GrabPhysicsSettings ToSettings() => (settings ?? new GrabPhysicsSettings()).ValidatedCopy();

        // Older authored scenes remain usable before their pickup references are upgraded.
        public static GrabPhysicsSettings Resolve(GrabPhysicsConfig config) => config
            ? config.ToSettings() : new GrabPhysicsSettings().ValidatedCopy();
    }
}
