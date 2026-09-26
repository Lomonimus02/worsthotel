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
        bool HasAuthority => !LocalCoopBootstrap.Instance || LocalCoopBootstrap.Instance.HasWorldAuthority;

        void Awake()
        {
            Body = GetComponent<Rigidbody>(); PlacementCollider = GetComponent<Collider>();
            visuals = GetComponentsInChildren<Renderer>(true); shapes = GetComponentsInChildren<Collider>(true);
            presentation = FindAnyObjectByType<GuestPresentation>();
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
                string nextId = simulation?.ContinuousOperations == true ? ContinuousLuggageId() :
                    simulation != null && luggageSlot < simulation.Guests.Count ? "luggage:" + simulation.Guests[luggageSlot].GuestId : null;
                if (itemId != nextId) { itemId = nextId; shownLocation = null; shownGeneration = -1; }
            }
            // Replicas get physical visibility and pose from the host; local model must not move a replica body.
            if (!HasAuthority) return;
            var state = State;
            if (state == null) { SetVisible(false); Body.isKinematic = true; return; }
            bool visible = state.Location != ServiceItemLocation.Delivered;
            GuestStay guest = null;
            if (state.Kind == ServiceItemKind.Luggage)
            {
                foreach (var candidate in simulation.Guests) if (candidate.GuestId == state.GuestId) { guest = candidate; break; }
                if (state.Location == ServiceItemLocation.OnShelf)
                    visible = guest?.Agent != null && (guest.Agent.State == GuestAgentState.Arriving || guest.Agent.State == GuestAgentState.WaitingForCheckIn);
                // A departed owner cannot erase the suitcase staff are still carrying or
                // have put down. A stored suitcase is safe to recycle for a later arrival.
                if (guest?.Agent?.State == GuestAgentState.Left && state.Location == ServiceItemLocation.OnShelf) visible = false;
            }
            if (shownLocation != state.Location || shownGeneration != state.Generation || shownVisible != visible)
            {
                if (state.Location != ServiceItemLocation.HeldByPlayer && carrier && carrier.HeldBody == Body) carrier.ReleaseGrab();
                SetVisible(visible);
                bool docked = state.Location == ServiceItemLocation.OnShelf || state.Location == ServiceItemLocation.Stored ||
                    state.Location == ServiceItemLocation.AwaitingReceipt;
                Body.isKinematic = !visible;
                Body.useGravity = visible && !docked;
                Body.constraints = docked ? RigidbodyConstraints.FreezeAll : RigidbodyConstraints.None;
                if (docked) PlaceAtDock(state, guest);
                shownLocation = state.Location; shownGeneration = state.Generation;
            }
            if (visible && state.Kind == ServiceItemKind.Luggage && state.Location == ServiceItemLocation.OnShelf)
                PlaceAtDock(state, guest);
            if (identityLabel && state.Kind == ServiceItemKind.Luggage && guest != null)
                identityLabel.text = "LUGGAGE\n" + guest.RoomId;
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
                if (current.Location == ServiceItemLocation.HeldByPlayer || current.Location == ServiceItemLocation.Dropped)
                    return itemId;
                foreach (var guest in simulation.Guests)
                    if (guest.GuestId == current.GuestId && guest.Agent?.State != GuestAgentState.Left) return itemId;
            }
            foreach (var guest in simulation.Guests)
            {
                if (guest.Agent == null || guest.Agent.State != GuestAgentState.Arriving && guest.Agent.State != GuestAgentState.WaitingForCheckIn)
                    continue;
                string candidate = "luggage:" + guest.GuestId;
                if (simulation.Services?.FindItem(candidate) == null) continue;
                bool assigned = false;
                foreach (var body in luggageBodies)
                    if (body && body != this && body.gameObject.scene == gameObject.scene && body.simulation == simulation && body.itemId == candidate)
                    { assigned = true; break; }
                if (!assigned) return candidate;
            }
            return current?.Location == ServiceItemLocation.Stored ? itemId : null;
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
            carrier = player; LastCarrierId = player.ActorId; shownLocation = ServiceItemLocation.HeldByPlayer;
            return true;
        }

        public override void EndCarry(PlayerInteractor player)
        {
            if (carrier != player) return;
            carrier = null;
            var session = GameSession.Instance;
            if (HasAuthority && session && session.Simulation == simulation && (State?.Location == ServiceItemLocation.Stored || State?.Location == ServiceItemLocation.AwaitingReceipt))
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
