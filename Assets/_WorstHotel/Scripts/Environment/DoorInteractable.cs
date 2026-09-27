using UnityEngine;

namespace WorstHotel
{
    /// <summary>A solid, reversible hinged door operated by either local player.</summary>
    public sealed class DoorInteractable : HotelInteractable
    {
        public Transform doorPivot;
        public float openAngle = -105f;
        public float degreesPerSecond = 150f;
        public bool startsOpen;
        [Tooltip("Room doors open away from the interacting player so their solid leaf cannot sweep a carried item behind it.")]
        public bool openAwayFromPlayer;
        public int roomId;

        public bool IsOpen { get; private set; }
        public bool IsPassageOpen => doorPivot != null && Quaternion.Angle(closedRotation, doorPivot.localRotation) >= 80f;
        Quaternion closedRotation;
        float activeOpenAngle;
        Rigidbody doorBody;
        RoomNoiseInteraction conversation;
        RoomState room;
        HotelSimulation boundSimulation;
        RoomPrivacyState previousPrivacy;
        string previousGuest;
        float closeAt;
        public bool IsLocked => room != null && room.PrivacyState == RoomPrivacyState.Private && !IsOpen && Quaternion.Angle(closedRotation, doorPivot.localRotation) < 2;
        bool Authority => !LocalCoopBootstrap.Instance || LocalCoopBootstrap.Instance.HasWorldAuthority;
        public RoomNoiseInteraction Conversation => conversation ? conversation : conversation = GetComponentInChildren<RoomNoiseInteraction>();

        void Awake()
        {
            if (doorPivot == null) doorPivot = transform;
            closedRotation = doorPivot.localRotation;
            activeOpenAngle = openAngle;
            doorBody = doorPivot.GetComponent<Rigidbody>();
            IsOpen = startsOpen;
            // Older authored doors still bind safely before the scene is regenerated.
            if (roomId == 0 && Conversation) roomId = Conversation.roomId;
            if (startsOpen) doorPivot.localRotation = closedRotation * Quaternion.Euler(0, openAngle, 0);
        }

        void BindRoom()
        {
            var session = GameSession.Instance;
            var simulation = session ? session.Simulation : null;
            if (boundSimulation != simulation)
            {
                boundSimulation = simulation;
                room = session ? System.Array.Find(session.Rooms, item => item.Profile.Id == roomId) : null;
                previousGuest = null; previousPrivacy = RoomPrivacyState.Public; closeAt = 0;
                if (Authority && roomId != 0) IsOpen = false;
            }
        }
        bool Inside(PlayerInteractor actor) => actor && transform.InverseTransformPoint(actor.transform.position).z > .35f;
        bool NeedsPermission(PlayerInteractor actor) => room != null && room.Occupied && !Inside(actor) &&
            !HasDeliveryAccess(actor);
        bool HasDeliveryAccess(PlayerInteractor actor)
        {
            if (!actor || boundSimulation == null) return false;
            var bag = actor.HeldBody ? actor.HeldBody.GetComponent<ServiceSupplyItem>() : null;
            if (bag && bag.BoundSimulation == boundSimulation && bag.State?.Location == ServiceItemLocation.HeldByPlayer &&
                bag.State.PlayerId == actor.ActorId && boundSimulation.HasLuggageStaffAccess(roomId, bag.ItemId)) return true;
            return LuggageCart.HasDeliveryForRoom(actor, boundSimulation, roomId);
        }
        public override string GetPrompt(PlayerInteractor actor)
        {
            BindRoom();
            if (!IsOpen && room != null && room.Occupied && !Inside(actor) && HasDeliveryAccess(actor))
                return "Agreed luggage delivery · Open " + displayName;
            return !IsOpen && NeedsPermission(actor) && Conversation ? Conversation.GetPrompt(actor) : (IsOpen ? "Close " : "Open ") + displayName;
        }
        public override bool AllowsHeldItem(PlayerInteractor actor) => true;
        public override void Interact(PlayerInteractor actor)
        {
            if (!Authority) return;
            BindRoom();
            if (IsOpen) { IsOpen = false; return; }
            if (NeedsPermission(actor)) { if (Conversation) Conversation.Interact(actor); return; }
            OpenForStaff(actor);
        }
        public void OpenForStaff(PlayerInteractor actor)
        {
            if (!Authority || !actor || !actor.CanAct) return;
            OpenForAuthorizedConversation(actor);
        }
        // GameSession calls this only after validating a live nearby conversation and guest permission.
        internal void OpenForAuthorizedConversation(PlayerInteractor actor)
        {
            if (!Authority || !actor) return;
            BindRoom();
            // Latch the side while fully closed. Reopening during a closing swing must keep
            // its current arc instead of sweeping through the opposite half of the doorway.
            if (Quaternion.Angle(closedRotation, doorPivot.localRotation) < 2)
                activeOpenAngle = openAwayFromPlayer && actor ?
                    (transform.InverseTransformPoint(actor.transform.position).z >= 0 ? 1 : -1) * Mathf.Abs(openAngle) : openAngle;
            IsOpen = true;
            closeAt = room != null && room.Occupied ? Time.time + 5 : 0;
        }
        public override void SecondaryInteract(PlayerInteractor actor) { BindRoom(); if (NeedsPermission(actor) && Conversation) Conversation.SecondaryInteract(actor); }
        public override void HoldInteract(PlayerInteractor actor, float deltaTime) { if (Conversation) Conversation.HoldInteract(actor, deltaTime); }
        public override void EndInteract(PlayerInteractor actor) { if (Conversation) Conversation.EndInteract(actor); }
        public void RequestOpen()
        {
            if (!Authority) return;
            if (!IsOpen && Quaternion.Angle(closedRotation, doorPivot.localRotation) < 2) activeOpenAngle = openAngle;
            IsOpen = true;
        }
        public bool RequestGuestOpen(string guestId)
        {
            BindRoom();
            if (!Authority || room == null || string.IsNullOrEmpty(guestId) ||
                (room.GuestId != guestId && room.DepartingGuestId != guestId && room.ReservedGuestId != guestId)) return false;
            closeAt = 0;
            RequestOpen();
            return true;
        }
        public void RequestClose() { if (Authority) { IsOpen = false; closeAt = 0; } }
        public void CloseAfterGuestPassage(string guestId)
        {
            BindRoom();
            if (room != null && (room.GuestId == guestId || room.DepartingGuestId == guestId || room.ReservedGuestId == guestId || !room.Occupied)) RequestClose();
        }
        public void ApplyReplicaState(bool open)
        {
            if (LocalCoopBootstrap.Instance && !LocalCoopBootstrap.Instance.HasWorldAuthority) IsOpen = open;
        }

        void FixedUpdate()
        {
            BindRoom();
            if (Authority && room != null)
            {
                if (room.Occupied && (room.GuestId != previousGuest || room.PrivacyState != previousPrivacy) &&
                    room.OccupancyState == RoomOccupancyState.GuestInside) closeAt = Time.time + .2f;
                previousGuest = room.GuestId; previousPrivacy = room.PrivacyState;
                if (closeAt > 0 && Time.time >= closeAt && !StaffInThreshold()) RequestClose();
                boundSimulation.ReportRoomDoorState(roomId, IsOpen || Quaternion.Angle(closedRotation, doorPivot.localRotation) >= 2);
            }
            var target = closedRotation * Quaternion.Euler(0, IsOpen ? activeOpenAngle : 0, 0);
            var next = Quaternion.RotateTowards(doorPivot.localRotation, target, degreesPerSecond * Time.fixedDeltaTime);
            if (doorBody != null)
                doorBody.MoveRotation((doorPivot.parent ? doorPivot.parent.rotation : Quaternion.identity) * next);
            else
                doorPivot.localRotation = next;
        }
        bool StaffInThreshold()
        {
            for (int id = 0; id < 2; id++)
                if (PlayerInteractor.TryGetPlayer(id, out var actor))
                {
                    var local = transform.InverseTransformPoint(actor.transform.position);
                    if (Mathf.Abs(local.x) < 1.25f && Mathf.Abs(local.z) < 1) return true;
                }
            return false;
        }
    }
}
