using System.Collections.Generic;
using UnityEngine;

namespace WorstHotel
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class LuggageCart : HotelInteractable
    {
        public Transform handle;
        Rigidbody body;
        PhysicsMaterial wheelContact;
        PlayerInteractor driver;
        readonly Dictionary<ServiceSupplyItem, float> resting = new Dictionary<ServiceSupplyItem, float>();
        readonly List<ServiceSupplyItem> cargo = new List<ServiceSupplyItem>();
        static readonly List<LuggageCart> carts = new List<LuggageCart>();
        public static bool IsGuiding(int actor) => carts.Exists(cart => cart && cart.driver && cart.driver.ActorId == actor);
        bool Authority => !LocalCoopBootstrap.Instance || LocalCoopBootstrap.Instance.HasWorldAuthority;

        void Awake()
        {
            body = GetComponent<Rigidbody>(); carts.Add(this);
            body.centerOfMass = new Vector3(0, .25f, 0);
            body.solverIterations = 12; body.solverVelocityIterations = 6;
            wheelContact = new PhysicsMaterial("Cart rolling contact")
                { dynamicFriction = .04f, staticFriction = .06f, bounciness = 0, frictionCombine = PhysicsMaterialCombine.Minimum };
            GetComponent<BoxCollider>().sharedMaterial = wheelContact;
        }

        public override bool CanInteract(PlayerInteractor actor) => base.CanInteract(actor) && actor && !actor.HeldBody &&
            (!driver || driver == actor) && Vector3.Distance(actor.transform.position, handle.position) < 2.1f;
        public override string GetPrompt(PlayerInteractor actor) => driver && driver != actor ? "Other employee is guiding the cart" :
            driver == actor ? "Release handle · move to push, look to steer" : "Take handle · move to push, look to steer";
        public override void Interact(PlayerInteractor actor)
        { if (Authority && CanInteract(actor)) driver = driver == actor ? null : actor; }

        void FixedUpdate()
        {
            if (!Authority || body.isKinematic) return;
            if (driver && (!driver.CanAct || driver.HeldBody || Vector3.Distance(driver.transform.position, handle.position) > 2.8f)) driver = null;
            if (LocalCoopBootstrap.Instance && LocalCoopBootstrap.Instance.IsPaused) return;
            if (driver)
            {
                var player = driver.GetComponent<FirstPersonController>();
                Vector2 input = player.Input.Move;
                Vector3 forward = Vector3.ProjectOnPlane(player.PlayerCamera.transform.forward, Vector3.up).normalized;
                Vector3 desired = (forward * input.y + Vector3.Cross(Vector3.up, forward) * input.x) * (player.Input.SprintHeld ? 2.7f : 1.75f);
                Vector3 velocity = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
                body.AddForce(Vector3.ClampMagnitude((desired - velocity) * 5, 7), ForceMode.Acceleration);
                float turn = Vector3.SignedAngle(transform.forward, forward, Vector3.up) * Mathf.Deg2Rad;
                body.AddTorque(Vector3.up * Mathf.Clamp(turn * 7 - body.angularVelocity.y * 4, -5, 5), ForceMode.Acceleration);
            }
            cargo.RemoveAll(item => !item || !item.CargoJoint || item.CargoJoint.connectedBody != body);
            var nearby = Physics.OverlapBox(transform.TransformPoint(new Vector3(0, .88f, 0)), new Vector3(.71f, .50f, .46f),
                transform.rotation, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            var seen = new HashSet<ServiceSupplyItem>();
            foreach (var shape in nearby)
            {
                var bag = shape.GetComponentInParent<ServiceSupplyItem>();
                if (!bag || !seen.Add(bag) || !bag.Body || bag.Body.isKinematic || bag.State?.Kind != ServiceItemKind.Luggage ||
                    bag.State.Location != ServiceItemLocation.Dropped || bag.OnCart || !bag.State.StaffHandling) continue;
                var local = transform.InverseTransformPoint(bag.Body.worldCenterOfMass);
                bool inside = Mathf.Abs(local.x) < .67f && Mathf.Abs(local.z) < .43f && local.y > .48f && local.y < 1.3f;
                bool settled = inside && (bag.Body.linearVelocity - body.GetPointVelocity(bag.Body.position)).sqrMagnitude < .25f &&
                    bag.Body.angularVelocity.sqrMagnitude < 1;
                resting.TryGetValue(bag, out float elapsed);
                resting[bag] = settled ? elapsed + Time.fixedDeltaTime : 0;
                if (resting[bag] < .55f || cargo.Count >= 4) continue;
                // Preserve the exact resting pose. A normal grab removes this joint before carrying.
                var joint = bag.gameObject.AddComponent<FixedJoint>(); joint.connectedBody = body;
                joint.enableCollision = false; joint.breakForce = 2800; joint.breakTorque = 1600;
                bag.CargoJoint = joint; cargo.Add(bag); resting[bag] = 0;
            }
            foreach (var item in new List<ServiceSupplyItem>(resting.Keys))
                if (!item || !seen.Contains(item)) resting.Remove(item);
        }
        void OnDisable() { driver = null; }
        void OnDestroy()
        {
            carts.Remove(this);
            if (wheelContact) Destroy(wheelContact);
            foreach (var bag in cargo) if (bag) bag.DetachCart();
        }
    }
}
