using UnityEngine;

namespace WorstHotel
{
    // Authored walking gives way to swept, directional displacement during contact.
    public sealed class GuestPhysicalReaction : MonoBehaviour
    {
        Transform visual;
        CapsuleCollider capsule;
        Vector3 velocity, localImpact;
        float recovery, duration, cooldown;
        int level;
        public bool Recovering => recovery > 0;

        public void Initialize(Transform body)
        {
            visual = body; capsule = GetComponent<CapsuleCollider>();
            var contact = gameObject.AddComponent<Rigidbody>();
            contact.isKinematic = true; contact.useGravity = false;
            contact.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }

        void OnCollisionEnter(Collision collision) => Contact(collision);
        void OnCollisionStay(Collision collision) => Contact(collision);
        void Contact(Collision collision)
        {
            var other = collision.rigidbody;
            if (!other || other.isKinematic) return;
            var direction = Vector3.ProjectOnPlane(transform.position - other.worldCenterOfMass, Vector3.up).normalized;
            float speed = Mathf.Max(0, Vector3.Dot(other.linearVelocity, direction));
            // Contact resolution may already have slowed a moving object this frame.
            speed = Mathf.Max(speed, collision.relativeVelocity.magnitude);
            Impact(other.mass, speed, direction);
        }

        public void StaffContact(Vector3 direction, bool running, float speed)
        {
            if (speed < .1f) return;
            Begin(running ? 2 : 1, direction, running ? 2.1f : .85f, !running);
        }

        public void Impact(float mass, float speed, Vector3 direction, bool sprint = false)
        {
            if (mass < 2 || speed < .12f) return;
            float momentum = mass * speed;
            int strength = !sprint && ((mass >= 45 && speed >= 2.05f) || (mass >= 7 && momentum >= 36)) ? 3 :
                sprint || (speed >= .8f && momentum >= 5) ? 2 : 1;
            Begin(strength, direction, strength == 3 ? Mathf.Clamp(speed * 1.25f, 2.8f, 4) : strength == 2 ? 1.65f : .65f, strength == 1);
        }

        void Begin(int strength, Vector3 direction, float speed, bool sidestep)
        {
            if (!visual || !visual.gameObject.activeInHierarchy || !capsule.enabled ||
                (LocalCoopBootstrap.Instance && !LocalCoopBootstrap.Instance.HasWorldAuthority) || Time.timeScale == 0) return;
            if (Recovering && strength <= level || Time.time < cooldown && strength <= level) return;
            direction = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
            if (direction.sqrMagnitude < .1f) return;
            if (sidestep)
            {
                var side = Vector3.Cross(Vector3.up, direction);
                if (Vector3.Dot(transform.right, direction) < 0) side = -side;
                direction = (direction * .65f + side * .75f).normalized;
            }
            level = strength;
            duration = recovery = strength == 3 ? 2.8f : strength == 2 ? .85f : .3f;
            cooldown = Time.time + (strength == 3 ? 3.4f : strength == 2 ? 1.1f : .32f);
            velocity = direction * speed;
            localImpact = transform.InverseTransformDirection(direction);
        }

        public bool AnimateRecovery(float deltaTime)
        {
            if (!Recovering) return false;
            float dt = Mathf.Min(deltaTime, .05f);
            if (TryWalk(transform, transform.position + velocity * dt, out var next)) transform.position = next;
            velocity = Vector3.MoveTowards(velocity, Vector3.zero, (level == 3 ? 4 : level == 2 ? 3 : 2) * dt);
            recovery = Mathf.Max(0, recovery - dt);
            float elapsed = duration - recovery;
            float amount = Mathf.Min(Mathf.SmoothStep(0, 1, elapsed / .16f), Mathf.SmoothStep(0, 1, recovery / (level == 3 ? .85f : .3f)));
            float angle = level == 3 ? 65 : level == 2 ? 19 : 5;
            visual.localPosition = Vector3.down * amount * (level == 3 ? .65f : level == 2 ? .10f : .02f);
            visual.localRotation = Quaternion.Euler(localImpact.z * angle * amount, 0, -localImpact.x * angle * amount);
            capsule.height = Mathf.Lerp(2.05f, level == 3 ? 1.05f : 1.85f, amount);
            capsule.center = Vector3.up * capsule.height * .5f;
            if (!Recovering) { visual.localPosition = Vector3.zero; visual.localRotation = Quaternion.identity; capsule.height = 2.05f; capsule.center = Vector3.up * 1.025f; }
            return true;
        }

        public static bool TryWalk(Transform guest, Vector3 desired, out Vector3 next)
        {
            next = guest.position;
            var travel = desired - next;
            if (Clear(guest, travel)) { next = desired; return true; }
            var side = Vector3.Cross(Vector3.up, travel);
            foreach (float sign in new[] { 1f, -1f })
            {
                var slide = travel * .2f + side * sign;
                if (Clear(guest, slide)) { next += slide; return true; }
            }
            return false;
        }

        static bool Clear(Transform guest, Vector3 move)
        {
            if (move.sqrMagnitude < .000001f) return true;
            foreach (var hit in Physics.CapsuleCastAll(guest.position + Vector3.up * .42f,
                guest.position + Vector3.up * 1.65f, .32f, move.normalized, move.magnitude + .025f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform.IsChildOf(guest)) continue;
                // Escape an existing contact, but never step farther into it.
                if (hit.distance <= .001f && Vector3.Dot(move, guest.position + Vector3.up - hit.collider.bounds.center) > 0) continue;
                var door = hit.collider.GetComponentInParent<DoorInteractable>();
                if (door && door.IsPassageOpen) continue;
                if (hit.rigidbody && !hit.rigidbody.isKinematic && hit.rigidbody.mass < 20) continue;
                return false;
            }
            return true;
        }
    }
}
