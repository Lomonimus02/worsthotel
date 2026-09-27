using UnityEngine;

namespace WorstHotel
{
    // AI owns the feet. A kinematic contact body receives impacts; only the visual rig falls.
    public sealed class GuestPhysicalReaction : MonoBehaviour
    {
        Transform visual;
        CapsuleCollider capsule;
        float recovery, cooldown;
        float fallSide;
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
            if (!other || other.isKinematic || !visual || !visual.gameObject.activeSelf) return;
            Impact(other.mass, collision.relativeVelocity.magnitude, transform.position - other.position);
            if (!Recovering && other.mass > 25 && other.GetComponent<LuggageCart>() && collision.relativeVelocity.magnitude > .08f)
            {
                Vector3 away = Vector3.ProjectOnPlane(transform.position - other.position, Vector3.up).normalized;
                if (TryWalk(transform, transform.position + away * Mathf.Min(.035f, collision.relativeVelocity.magnitude * Time.fixedDeltaTime), out var next))
                    transform.position = next;
            }
        }

        public void Impact(float mass, float speed, Vector3 direction, bool sprint = false)
        {
            if (!visual || !visual.gameObject.activeSelf || !capsule.enabled || Recovering || Time.time < cooldown ||
                (LocalCoopBootstrap.Instance && (!LocalCoopBootstrap.Instance.HasWorldAuthority || LocalCoopBootstrap.Instance.IsPaused))) return;
            if (mass < 3 || speed < (sprint ? 4 : 1.65f) || mass * speed < 25) return;
            recovery = 3.0f; cooldown = Time.time + 5.5f;
            fallSide = Vector3.Dot(transform.right, direction) < 0 ? 1 : -1;
        }

        public bool AnimateRecovery(float delta)
        {
            if (!Recovering) return false;
            recovery = Mathf.Max(0, recovery - delta);
            float elapsed = 3 - recovery;
            float amount = elapsed < .4f ? Mathf.SmoothStep(0, 1, elapsed / .4f) :
                recovery < .95f ? Mathf.SmoothStep(0, 1, recovery / .95f) : 1;
            // A compact sideways sit/fall avoids throwing a two-metre rig through adjacent walls.
            visual.localPosition = new Vector3(0, -.46f * amount, 0);
            visual.localRotation = Quaternion.Euler(-28 * amount, 0, fallSide * 32 * amount);
            capsule.height = Mathf.Lerp(2.05f, 1.20f, amount);
            capsule.center = Vector3.up * capsule.height * .5f;
            if (recovery <= 0) { visual.localPosition = Vector3.zero; visual.localRotation = Quaternion.identity; }
            return true;
        }

        public static bool TryWalk(Transform guest, Vector3 desired, out Vector3 next)
        {
            next = guest.position;
            if (Clear(guest, desired)) { next = desired; return true; }
            Vector3 travel = desired - guest.position;
            var side = Vector3.Cross(Vector3.up, travel.normalized) * travel.magnitude;
            foreach (float sign in new[] { 1f, -1f })
            {
                var slide = guest.position + travel * .2f + side * sign;
                if (Clear(guest, slide)) { next = slide; return true; }
            }
            return false;
        }

        static bool Clear(Transform guest, Vector3 destination)
        {
            var move = destination - guest.position;
            if (move.sqrMagnitude < .000001f) return true;
            foreach (var hit in Physics.CapsuleCastAll(guest.position + Vector3.up * .42f,
                guest.position + Vector3.up * 1.65f, .32f, move.normalized, move.magnitude + .025f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform.IsChildOf(guest)) continue;
                if (hit.collider.GetComponentInParent<DoorInteractable>()) continue;
                if (hit.rigidbody && !hit.rigidbody.isKinematic && hit.rigidbody.mass < 20) continue;
                return false;
            }
            return true;
        }
    }
}
