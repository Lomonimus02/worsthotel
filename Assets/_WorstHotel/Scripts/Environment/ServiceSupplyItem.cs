using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorstHotel
{
    /// <summary>One authored body per stock slot. Carrying uses the existing physics grab, never an inventory icon.</summary>
    [DefaultExecutionOrder(120), RequireComponent(typeof(Rigidbody), typeof(PhysicsPickup))]
    public sealed class ServiceSupplyItem : PhysicalCarryItem
    {
        public string itemId;
        public int luggageSlot = -1;
        public Transform sourceAnchor;
        public LuggageStorageZone storageZone;
        public TextMesh identityLabel;
        public string ItemId => itemId;
        public Transform SourceAnchor => sourceAnchor;
        public Rigidbody Body { get; private set; }
        public HotelSimulation BoundSimulation => simulation;
        public ServiceItemState State => simulation?.Services?.FindItem(itemId);
        public int? LastCarrierId { get; private set; }
        public Collider PlacementCollider { get; private set; }
        HotelSimulation simulation;
        PlayerInteractor carrier;
        Renderer[] visuals;
        Collider[] shapes;
        ServiceItemLocation? shownLocation;
        int shownGeneration = -1;
        bool shownVisible;
        GuestPresentation presentation;
        static readonly List<ServiceSupplyItem> luggageBodies = new List<ServiceSupplyItem>();
        LuggageDeliveryZone[] deliveryZones;
        float luggageRest;
        public FixedJoint CargoJoint { get; set; }
        public bool OnCart => CargoJoint;
        bool HasAuthority => !LocalCoopBootstrap.Instance || LocalCoopBootstrap.Instance.HasWorldAuthority;

        void Awake()
        {
            Body = GetComponent<Rigidbody>(); PlacementCollider = GetComponent<Collider>();
            visuals = GetComponentsInChildren<Renderer>(true); shapes = GetComponentsInChildren<Collider>(true);
            presentation = FindAnyObjectByType<GuestPresentation>();
            deliveryZones = FindObjectsByType<LuggageDeliveryZone>(FindObjectsSortMode.None);
            if (luggageSlot >= 0) luggageBodies.Add(this);
        }

        void LateUpdate()
        {
            var current = GameSession.Instance ? GameSession.Instance.Simulation : null;
            if (simulation != current)
            {
                if (carrier && carrier.HeldBody == Body) carrier.ReleaseGrab();
                carrier = null; simulation = current; shownLocation = null; shownGeneration = -1; LastCarrierId = null;
                if (luggageSlot >= 0 && simulation?.ContinuousOperations == true) itemId = null;
            }
            if (luggageSlot >= 0)
            {
                string nextId = simulation != null ? ContinuousLuggageId() : null;
                if (itemId != nextId) { DetachCart(); itemId = nextId; shownLocation = null; shownGeneration = -1; luggageRest = 0; }
            }
            // Replicas get physical visibility and pose from the host; local model must not move a replica body.
            if (!HasAuthority) return;
            var state = State;
            if (state == null) { SetVisible(false); Body.isKinematic = true; return; }
            if (state.Kind == ServiceItemKind.Luggage) { TickLuggage(state); return; }
            bool visible = state.Location != ServiceItemLocation.Delivered;
            if (shownLocation != state.Location || shownGeneration != state.Generation || shownVisible != visible)
            {
                if (state.Location != ServiceItemLocation.HeldByPlayer && carrier && carrier.HeldBody == Body) carrier.ReleaseGrab();
                SetVisible(visible);
                bool docked = state.Location == ServiceItemLocation.OnShelf || state.Location == ServiceItemLocation.Stored ||
                    state.Location == ServiceItemLocation.AwaitingReceipt;
                Body.isKinematic = !visible;
                Body.useGravity = visible && !docked;
                Body.constraints = docked ? RigidbodyConstraints.FreezeAll : RigidbodyConstraints.None;
                if (docked) PlaceAtDock(state, null);
                shownLocation = state.Location; shownGeneration = state.Generation;
            }
            if (state.Location == ServiceItemLocation.Dropped && Body.position.y < -3 && sourceAnchor)
            {
                Body.linearVelocity = Body.angularVelocity = Vector3.zero;
                Body.position = sourceAnchor.position; Body.rotation = sourceAnchor.rotation;
            }
        }

        string ContinuousLuggageId()
        {
            var current = State;
            if (current != null)
            {
                // A roster prune or new arrival must never rebind a physical suitcase held
                // or put down by staff. Its stable item identity outlives the guest body.
                if (current.Location == ServiceItemLocation.HeldByPlayer || current.Location == ServiceItemLocation.Dropped || current.Location == ServiceItemLocation.Stored)
                    return itemId;
                foreach (var guest in simulation.Guests)
                    if (guest.GuestId == current.GuestId && guest.Agent?.State != GuestAgentState.Left) return itemId;
            }
            foreach (var item in simulation.Services.Items)
            {
                if (item.Kind != ServiceItemKind.Luggage) continue;
                GuestStay guest = null;
                foreach (var stay in simulation.Guests) if (stay.GuestId == item.GuestId) { guest = stay; break; }
                if (guest == null) continue;
                if (guest.Agent == null || guest.Agent.State != GuestAgentState.Arriving && guest.Agent.State != GuestAgentState.WaitingForCheckIn)
                    continue;
                string candidate = item.Id;
                bool assigned = false;
                foreach (var body in luggageBodies)
                    if (body && body != this && body.gameObject.scene == gameObject.scene && body.simulation == simulation && body.itemId == candidate)
                    { assigned = true; break; }
                if (!assigned) return candidate;
            }
            return current?.Location == ServiceItemLocation.Stored ? itemId : null;
        }

        void TickLuggage(ServiceItemState state)
        {
            GuestStay guest = null;
            foreach (var stay in simulation.Guests) if (stay.GuestId == state.GuestId) { guest = stay; break; }
            bool ownCarry = state.Location == ServiceItemLocation.OnShelf;
            bool visible = guest?.Agent != null && (guest.Agent.State != GuestAgentState.Left ||
                state.Location == ServiceItemLocation.Dropped || state.Location == ServiceItemLocation.HeldByPlayer || state.Location == ServiceItemLocation.Stored);
            if (ownCarry) visible &= presentation && presentation.TryGetGuestTransform(state.GuestId, out _);
            if (shownLocation != state.Location || shownGeneration != state.Generation || shownVisible != visible)
            {
                SetVisible(visible);
                Body.isKinematic = !visible;
                Body.useGravity = visible && !ownCarry;
                Body.constraints = ownCarry ? RigidbodyConstraints.FreezeAll : RigidbodyConstraints.None;
                Body.mass = state.Id.EndsWith(":2", StringComparison.Ordinal) ? 8 : 5;
                Body.linearDamping = .5f; Body.angularDamping = 2.2f;
                shownLocation = state.Location; shownGeneration = state.Generation;
            }
            if (!visible) return;
            if (ownCarry && presentation.TryGetGuestTransform(state.GuestId, out var owner))
            {
                float side = LuggageCountForOwner(state.GuestId) > 1 ? (state.Id.EndsWith(":2", StringComparison.Ordinal) ? -.38f : .38f) : .15f;
                Body.position = owner.position - owner.forward * .58f + owner.right * side + Vector3.up * .39f;
                Body.rotation = owner.rotation;
                Body.linearVelocity = Body.angularVelocity = Vector3.zero;
                if (guest.Agent.InAssignedRoom)
                    simulation.Services.SettleOwnLuggage(state.Id);
            }
            string status = state.Location == ServiceItemLocation.Stored ? "STORED" : state.Location == ServiceItemLocation.Delivered ? "DELIVERED" :
                state.StaffHandling ? "TO ROOM " + guest.RoomId : "WITH GUEST";
            if (identityLabel) identityLabel.text = "ROOM " + guest.RoomId + "\n" + guest.Name;
            GetComponent<PhysicsPickup>().itemName = guest.Name + " · room " + guest.RoomId + "\n" + status +
                (state.StaffHandling ? "" : " · offer help to the owner");
            if (ownCarry || state.Location == ServiceItemLocation.HeldByPlayer || state.Location == ServiceItemLocation.Delivered || OnCart) { luggageRest = 0; return; }
            if (LocalCoopBootstrap.Instance && LocalCoopBootstrap.Instance.IsPaused) return;
            luggageRest = Body.linearVelocity.sqrMagnitude < .08f && Body.angularVelocity.sqrMagnitude < .3f ? luggageRest + Time.deltaTime : 0;
            if (luggageRest < .45f) return;
            if (storageZone && storageZone.Contains(Body.worldCenterOfMass))
            {
                if (state.Location != ServiceItemLocation.Stored && simulation.Services.PlaceLuggage(itemId, true).Success)
                    GameSession.Instance.RaiseChanged();
            }
            else
            {
                if (state.Location == ServiceItemLocation.Stored)
                { state.Location = ServiceItemLocation.Dropped; GameSession.Instance.RaiseChanged(); }
                foreach (var zone in deliveryZones)
                    if (zone && zone.roomId == guest.RoomId && zone.Contains(Body.worldCenterOfMass) &&
                        simulation.Services.PlaceLuggage(itemId, false).Success)
                    { GameSession.Instance.RaiseChanged(); break; }
            }
        }

        static int LuggageCountForOwner(string id) => GuestServiceSystem.LuggageCount(id);
        public void DetachCart()
        {
            if (CargoJoint) Destroy(CargoJoint);
            CargoJoint = null;
        }

        void PlaceAtDock(ServiceItemState state, GuestStay guest)
        {
            Transform dock = state.Location == ServiceItemLocation.Stored && storageZone ? storageZone.StorageAnchor(luggageSlot) : sourceAnchor;
            if (state.Location == ServiceItemLocation.AwaitingReceipt)
            {
                // Pending delivery belongs to this precise parcel/point. It cannot refill a shelf,
                // follow a relocated guest, or receive comfort through presentation alone.
                dock = null;
                var intent = simulation?.Services?.PendingDeliveryForItem(itemId);
                if (intent != null && intent.ItemGeneration == state.Generation)
                    foreach (var point in FindObjectsByType<RoomBlanketDropOffInteraction>(FindObjectsSortMode.None))
                        if (point.gameObject.scene == gameObject.scene && point.DeliveryPointId == intent.DeliveryPointId)
                        { dock = point.deliveryAnchor; break; }
            }
            if (state.Kind == ServiceItemKind.Luggage && state.Location == ServiceItemLocation.OnShelf && guest != null &&
                presentation && presentation.TryGetGuestTransform(guest.GuestId, out var guestTransform))
            {
                Body.position = guestTransform.position + guestTransform.right * .68f + Vector3.up * .40f;
                Body.rotation = guestTransform.rotation;
            }
            else if (dock) { Body.position = dock.position; Body.rotation = dock.rotation; }
            if (!Body.isKinematic) Body.linearVelocity = Body.angularVelocity = Vector3.zero;
        }

        void SetVisible(bool visible)
        {
            shownVisible = visible;
            foreach (var visual in visuals) if (visual) visual.enabled = visible;
            foreach (var shape in shapes) if (shape) shape.enabled = visible;
        }

        public override bool TryBeginCarry(PlayerInteractor player)
        {
            var session = GameSession.Instance;
            if (!HasAuthority || !session || !player || session.Simulation != simulation ||
                !session.TakeServiceItem(player.ActorId, this).Success) return false;
            Body.constraints = RigidbodyConstraints.None; Body.useGravity = true;
            DetachCart(); luggageRest = 0;
            carrier = player; LastCarrierId = player.ActorId; shownLocation = ServiceItemLocation.HeldByPlayer;
            return true;
        }

        public override void EndCarry(PlayerInteractor player)
        {
            if (carrier != player) return;
            carrier = null;
            var session = GameSession.Instance;
            if (HasAuthority && session && session.Simulation == simulation && State?.Kind != ServiceItemKind.Luggage && (State?.Location == ServiceItemLocation.Stored || State?.Location == ServiceItemLocation.AwaitingReceipt))
            {
                // The short placement uses the actual body after its physics joint is released.
                // Do it before a later calendar prune or arrival can recycle the authored slot.
                Body.useGravity = false; Body.constraints = RigidbodyConstraints.FreezeAll;
                PlaceAtDock(State, null);
            }
            if (HasAuthority && session && session.Simulation == simulation &&
                State?.Location == ServiceItemLocation.HeldByPlayer && State.PlayerId == player.ActorId)
                session.DropServiceItem(player.ActorId, this);
        }

        void OnDisable()
        {
            if (carrier && carrier.HeldBody == Body) carrier.ReleaseGrab();
            carrier = null;
        }
        void OnDestroy() => luggageBodies.Remove(this);
    }
}
