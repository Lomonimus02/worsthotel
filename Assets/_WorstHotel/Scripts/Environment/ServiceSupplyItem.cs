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
        SpecialLuggageAppearance appearance;
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
            appearance = GetComponent<SpecialLuggageAppearance>();
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
                if (HasAuthority) DetachCart();
                if (carrier && carrier.HeldBody == Body) carrier.ReleaseGrab();
                carrier = null; simulation = current; shownLocation = null; shownGeneration = -1; LastCarrierId = null;
                if (HasAuthority && luggageSlot >= 0 && simulation?.ContinuousOperations == true) itemId = null;
            }
            if (HasAuthority && luggageSlot >= 0)
            {
                string nextId = simulation != null ? ContinuousLuggageId() : null;
                if (itemId != nextId) { DetachCart(); itemId = nextId; shownLocation = null; shownGeneration = -1; luggageRest = 0; }
            }
            // Replicas get physical visibility and pose from the host; local model must not move a replica body.
            if (!HasAuthority) { if (appearance && State != null) appearance.ConfigureShape(State); return; }
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
            bool visible = state.Location != ServiceItemLocation.LostProperty && guest?.Agent != null && (guest.Agent.State != GuestAgentState.Left ||
                state.Location == ServiceItemLocation.Dropped || state.Location == ServiceItemLocation.HeldByPlayer || state.Location == ServiceItemLocation.Stored);
            if (ownCarry) visible &= presentation && presentation.TryGetGuestTransform(state.GuestId, out _);
            if (shownLocation != state.Location || shownGeneration != state.Generation || shownVisible != visible)
            {
                SetVisible(visible);
                Body.isKinematic = !visible;
                Body.useGravity = visible && !ownCarry;
                Body.constraints = ownCarry ? RigidbodyConstraints.FreezeAll : RigidbodyConstraints.None;
                Body.mass = state.Payload == LuggagePayload.InstrumentCase ? 12 : state.Payload == LuggagePayload.Amplifier ? 10 : state.LuggageIndex > 0 ? 8 : 5;
                if (appearance) appearance.ConfigureShape(state);
                Body.linearDamping = .5f; Body.angularDamping = 2.2f;
                shownLocation = state.Location; shownGeneration = state.Generation;
            }
            if (!visible) return;
            if (ownCarry && presentation.TryGetGuestTransform(state.GuestId, out var owner))
            {
                int count = GuestServiceSystem.LuggageCount(guest.Application), index = state.LuggageIndex;
                float side = count > 1 ? (index % 2 == 0 ? -.44f : .44f) : .15f;
                float behind = .65f + index / 2 * .62f;
                if (guest.Application.SpecialKind == SpecialGuestKind.TouringMusician)
                { side = index == 1 ? 0 : index == 0 ? -.46f : .46f; behind = index == 1 ? 1.30f : .65f; }
                // A short two-abreast train brings every bag through the same entrance route.
                // It becomes independent rigid bodies when staff agree to take responsibility.
                if (guest.Application.Special != null && guest.Agent.InAssignedRoom)
                {
                    // An unassisted guest still brings the actual parcels across the room.
                    // Finish only at the physical delivery mat, never when check-in is clicked.
                    foreach (var zone in deliveryZones)
                    {
                        if (!zone || zone.roomId != guest.RoomId) continue;
                        Vector3 offset = count == 3 ? new Vector3(side, .44f, index == 1 ? .4f : -.3f) :
                            new Vector3(count > 1 ? (index % 2 == 0 ? -.41f : .41f) : 0, .44f, -.5f + index / 2 * .5f);
                        Vector3 target = zone.transform.TransformPoint(offset);
                        Body.position = Vector3.MoveTowards(Body.position, target, Time.deltaTime * 1.6f);
                        Body.rotation = Quaternion.RotateTowards(Body.rotation, zone.transform.rotation, Time.deltaTime * 160);
                        if (Vector3.Distance(Body.position, target) < .04f) simulation.Services.SettleOwnLuggage(state.Id);
                        break;
                    }
                }
                else
                {
                    bool waiting = guest.Agent.State == GuestAgentState.WaitingForCheckIn;
                    // Reception faces a solid counter. Stage parcels on its lobby side,
                    // independent of the guest's conversational facing rotation.
                    Vector3 direction = waiting ? Vector3.forward : owner.forward;
                    Vector3 lateral = waiting ? Vector3.right : owner.right;
                    // Leave the authored reception-to-corridor lane at z=.35 clear.
                    float waitingClearance = waiting && guest.Application.Special != null ? 1.25f : 0;
                    Vector3 target = owner.position - direction * (behind + waitingClearance) + lateral * side + Vector3.up * .44f;
                    Body.position = waiting ? Vector3.MoveTowards(Body.position, target, Time.deltaTime * 2.4f) : target;
                    Body.rotation = waiting ? Quaternion.identity : owner.rotation;
                }
                Body.linearVelocity = Body.angularVelocity = Vector3.zero;
                if (guest.Agent.InAssignedRoom && guest.Application.Special == null)
                    simulation.Services.SettleOwnLuggage(state.Id);
            }
            string status = state.Location == ServiceItemLocation.Stored ? "STORED" : state.Location == ServiceItemLocation.Delivered ? "DELIVERED" :
                state.StaffHandling ? "TO ROOM " + guest.RoomId : "WITH GUEST";
            if (identityLabel) identityLabel.text = "ROOM " + guest.RoomId + "\n" + guest.Name;
            GetComponent<PhysicsPickup>().itemName = (state.Payload == LuggagePayload.InstrumentCase ? "Instrument case · heavy\n" : state.Payload == LuggagePayload.Amplifier ? "Amplifier\n" : "Suitcase " + (state.LuggageIndex + 1) + "\n") + guest.Name + " · room " + guest.RoomId + "\n" + status +
                (state.StaffHandling ? "" : " · offer help to the owner");
            if (state.Payload == LuggagePayload.Amplifier && state.StaffHandling && state.Location == ServiceItemLocation.Delivered)
            {
                bool inside = false;
                foreach (var zone in deliveryZones) if (zone && zone.roomId == guest.RoomId && zone.Contains(Body.worldCenterOfMass)) inside = true;
                if (!inside) { state.Location = ServiceItemLocation.Dropped; GameSession.Instance.RaiseChanged(); }
            }
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

        public static Transform FindLuggageTransform(string id)
        {
            foreach (var body in luggageBodies) if (body && body.itemId == id && body.State != null) return body.transform;
            return null;
        }
        public void ApplyReplicaItemId(string id)
        {
            if (HasAuthority || luggageSlot < 0) return;
            itemId = id;
        }
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
            if (appearance) appearance.Show(State?.Payload ?? LuggagePayload.Suitcase, visible);
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
