using System.Collections.Generic;
using UnityEngine;

namespace WorstHotel
{
    public sealed class PlayerInteractor : MonoBehaviour
    {
        public int ActorId => owner.ActorId;
        public Camera PlayerCamera => owner.PlayerCamera;
        public bool DeviceReady => owner != null && owner.DeviceReady;
        public bool IsUIBlocked => owner != null && owner.IsUIBlocked;
        public bool CanAct => CanUseWorld;
        public bool IsInteracting => owner != null && owner.Input.PrimaryHeld && CanUseWorld;
        public HotelInteractable Focused { get; private set; }
        public Rigidbody HeldBody { get; private set; }
        public Vector3 GrabTargetPosition => grabDriver != null ? grabDriver.TargetPosition : Vector3.zero;
        public Quaternion GrabTargetRotation => grabDriver != null ? grabDriver.TargetRotation : Quaternion.identity;
        public PhysicsPickup FocusedPickup { get; private set; }
        public bool HasWorldAuthority { get; private set; } = true;
        public string ReplicaCaption { get; private set; }
        public bool ReplicaUsable { get; private set; }
        public bool ReplicaPickup { get; private set; }
        [Range(1, 5)] public float reach = 3.1f;

        private static readonly Dictionary<Rigidbody, PlayerInteractor> Carriers = new Dictionary<Rigidbody, PlayerInteractor>();
        private static readonly Dictionary<int, PlayerInteractor> Players = new Dictionary<int, PlayerInteractor>();
        private FirstPersonController owner;
        private HotelInteractable heldInteraction;
        private PhysicsGrabDriver grabDriver;
        private float grabDistance;
        private RaycastHit focusedHit;
        private readonly RaycastHit[] hits = new RaycastHit[32];
        private bool CanUseWorld => HasWorldAuthority && owner != null && owner.DeviceReady && !owner.IsUIBlocked &&
            (!LocalCoopBootstrap.Instance || !LocalCoopBootstrap.Instance.IsPaused);

        public void Initialize(FirstPersonController controller)
        {
            owner = controller;
            Players[ActorId] = this;
        }
        public static bool TryGetPlayer(int playerId, out PlayerInteractor player)
        {
            return Players.TryGetValue(playerId, out player) && player && player.isActiveAndEnabled;
        }
        public void SetUIBlocked(bool blocked) => owner.SetUIBlocked(blocked);

        public void SetWorldAuthority(bool authority)
        {
            if (HasWorldAuthority && !authority) CancelInteraction();
            HasWorldAuthority = authority;
            if (authority) { ReplicaCaption = null; ReplicaUsable = ReplicaPickup = false; }
        }

        public void ApplyReplicaPrompt(string caption, bool usable, bool pickup)
        {
            if (HasWorldAuthority) return;
            ReplicaCaption = caption; ReplicaUsable = usable; ReplicaPickup = pickup;
        }

        private void Update()
        {
            if (!CanUseWorld)
            {
                if (heldInteraction || grabDriver != null) CancelInteraction();
                Focused = null;
                FocusedPickup = null;
                return;
            }
            UpdateFocus();
            if (heldInteraction && (heldInteraction != Focused || !owner.Input.PrimaryHeld || !heldInteraction.CanInteract(this)))
                EndHeldInteraction();
            if (owner.Input.GrabPressed)
            {
                if (HeldBody) ReleaseGrab();
                else if (FocusedPickup) BeginGrab(FocusedPickup);
            }
            if (Focused && Focused.CanInteract(this) && (!HeldBody || Focused.AllowsHeldItem(this)))
            {
                if (owner.Input.PrimaryPressed)
                {
                    heldInteraction = Focused;
                    Focused.Interact(this);
                }
                if (CanUseWorld && owner.Input.SecondaryPressed && Focused) Focused.SecondaryInteract(this);
            }
            if (heldInteraction && IsInteracting) heldInteraction.HoldInteract(this, Time.deltaTime);
        }

        private void UpdateFocus()
        {
            Focused = null;
            FocusedPickup = null;
            float nearest = float.MaxValue;
            int count = Physics.RaycastNonAlloc(PlayerCamera.transform.position, PlayerCamera.transform.forward,
                hits, reach, ~0, QueryTriggerInteraction.Ignore);
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                if (hits[i].collider.transform.IsChildOf(transform) ||
                    (HeldBody && hits[i].rigidbody == HeldBody) || hits[i].distance >= nearest) continue;
                nearest = hits[i].distance;
                focusedHit = hits[i];
                found = true;
            }
            if (!found) return;
            // Only the first physical surface is eligible: controls cannot be used through walls.
            Focused = focusedHit.collider.GetComponentInParent<HotelInteractable>();
            FocusedPickup = focusedHit.collider.GetComponentInParent<PhysicsPickup>();
        }

        private void BeginGrab(PhysicsPickup pickup)
        {
            var body = pickup.GetComponent<Rigidbody>();
            if (!body || body.isKinematic || body.mass > pickup.maxGrabMass) return;
            if (Carriers.TryGetValue(body, out var carrier) && carrier && carrier != this) return;
            var item = pickup.GetComponent<PhysicalCarryItem>();
            if (item && !item.TryBeginCarry(this)) return;
            EndHeldInteraction();
            grabDistance = pickup.holdDistance;
            grabDriver = new PhysicsGrabDriver(body, PlayerCamera.transform.rotation, owner.BodyCollider, pickup.GrabSettings);
            HeldBody = body;
            Carriers[body] = this;
        }

        private void FixedUpdate()
        {
            if (grabDriver == null) return;
            if (!grabDriver.IsValid || !CanUseWorld ||
                Vector3.Distance(HeldBody.worldCenterOfMass, PlayerCamera.transform.position) > grabDriver.Settings.releaseDistance)
            {
                ReleaseGrab();
                return;
            }
            Vector3 origin = PlayerCamera.transform.position;
            Vector3 forward = PlayerCamera.transform.forward;
            float distance = grabDistance;
            int count = Physics.SphereCastNonAlloc(origin, grabDriver.CollisionRadius, forward, hits, grabDistance, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (hits[i].collider.transform.IsChildOf(transform) || hits[i].rigidbody == HeldBody) continue;
                distance = Mathf.Min(distance, Mathf.Max(grabDriver.Settings.minimumHoldDistance, hits[i].distance - grabDriver.Settings.wallClearance));
            }
            grabDriver.FixedStep(origin + forward * distance, PlayerCamera.transform.rotation, Time.fixedDeltaTime);
        }

        public void ReleaseGrab()
        {
            var releasedItem = HeldBody ? HeldBody.GetComponent<PhysicalCarryItem>() : null;
            grabDriver?.Dispose();
            grabDriver = null;
            if (!ReferenceEquals(HeldBody, null))
            {
                if (Carriers.TryGetValue(HeldBody, out var carrier) && carrier == this) Carriers.Remove(HeldBody);
            }
            HeldBody = null;
            if (releasedItem) releasedItem.EndCarry(this);
        }

        private void EndHeldInteraction()
        {
            var previous = heldInteraction;
            heldInteraction = null;
            if (previous) previous.EndInteract(this);
        }

        public void CancelInteraction()
        {
            EndHeldInteraction();
            ReleaseGrab();
            Focused = null;
            FocusedPickup = null;
        }

        private void OnDisable() => CancelInteraction();
        private void OnDestroy()
        {
            CancelInteraction();
            if (owner != null && Players.TryGetValue(ActorId, out var player) && player == this) Players.Remove(ActorId);
        }
    }
}
