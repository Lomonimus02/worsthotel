using System.Linq;
using UnityEngine;

namespace WorstHotel
{
    /// <summary>One physical guest target. Keys exchange immediately; actual problems offer a small conversation.</summary>
    public sealed class GuestReceptionInteraction : HotelInteractable
    {
        public const float QuietHoldSeconds = 1.2f;
        public string GuestId { get; private set; }
        public int OwnerActorId { get; private set; } = -1;
        public float HoldProgress { get; private set; }
        public bool PresentationTargetVisible { get; set; } = true;
        GameSession session;
        GuestStay stay;
        RoomState assignedRoom;
        CapsuleCollider target;
        int displayedRoom;
        enum InteractionMode { None, RoomKey, Conversation }
        InteractionMode heldMode;

        public void Initialize(GameSession authority, GuestStay guest)
        {
            session = authority;
            stay = guest;
            GuestId = guest.GuestId;
            displayedRoom = guest.RoomId;
            assignedRoom = System.Array.Find(authority.Rooms, room => room.Profile.Id == displayedRoom);
            displayName = guest.Name + " · Room " + guest.RoomId;
            target = GetComponent<CapsuleCollider>();
            if (target == null) target = gameObject.AddComponent<CapsuleCollider>();
            target.radius = .34f; target.height = 2.05f; target.center = new Vector3(0, 1.025f, 0);
            target.enabled = false;
        }

        void IgnoreStaffCollision()
        {
            var coop = LocalCoopBootstrap.Instance;
            if (coop != null)
                foreach (var player in coop.Players)
                    if (player != null && player.BodyCollider != null)
                        Physics.IgnoreCollision(target, player.BodyCollider, true);
        }

        bool IsWaiting => session != null && session.Phase == DayPhase.Service && stay?.Agent != null &&
            stay.Agent.State == GuestAgentState.WaitingForCheckIn;
        bool IsInRoom => session != null && session.Phase == DayPhase.Service && stay?.Agent != null && stay.Agent.InAssignedRoom;
        bool AtServiceDesk => session != null && session.Phase == DayPhase.Service && stay?.Agent?.State == GuestAgentState.WaitingAtServiceReception;
        bool IsTemporarilyQuiet => session?.Simulation != null && stay?.Agent != null &&
            stay.Agent.QuietUntil > session.Simulation.Clock.SimulationTime;
        bool RoomPrepared => assignedRoom != null && assignedRoom.Cleanliness == Cleanliness.Clean &&
            string.IsNullOrEmpty(assignedRoom.DepartingGuestId) &&
            assignedRoom.TurnoverState != HousekeepingState.Moving && assignedRoom.TurnoverState != HousekeepingState.Cleaning;
        int NeededKey => stay?.Agent?.PendingMoveRoomId ?? displayedRoom;
        bool HasPendingMove => IsInRoom && stay.Agent.PendingMoveRoomId.HasValue;
        InteractionMode Mode => HasPendingMove ? InteractionMode.RoomKey : IsWaiting ? (RoomPrepared ? InteractionMode.RoomKey : InteractionMode.Conversation) :
            AtServiceDesk || IsInRoom && stay.Agent.State != GuestAgentState.Sleeping && stay.Agent.Activity != GuestActivity.Shower ? InteractionMode.Conversation : InteractionMode.None;

        void Update()
        {
            if (stay != null && displayedRoom != stay.RoomId)
            {
                displayedRoom = stay.RoomId;
                assignedRoom = System.Array.Find(session.Rooms, room => room.Profile.Id == displayedRoom);
                displayName = stay.Name + " · Room " + displayedRoom;
            }
            bool visibleTarget = PresentationTargetVisible && (IsWaiting || IsInRoom || AtServiceDesk);
            if (target != null && target.enabled != visibleTarget)
            {
                target.enabled = visibleTarget;
                if (target.enabled) IgnoreStaffCollision();
            }
            if (heldMode != Mode) ResetHold();
        }

        public override bool CanInteract(PlayerInteractor actor) => PresentationTargetVisible && base.CanInteract(actor) && Mode != InteractionMode.None &&
            actor != null && (OwnerActorId < 0 || OwnerActorId == actor.ActorId);

        bool CorrectHeldKey(PlayerInteractor actor)
        {
            var key = actor && actor.HeldBody ? actor.HeldBody.GetComponent<RoomKeyItem>() : null;
            return key && key.roomId == NeededKey && key.BoundSimulation == session.Simulation &&
                key.State?.Location == RoomKeyLocation.HeldByPlayer && key.State.PlayerId == actor.ActorId;
        }
        public override bool AllowsHeldItem(PlayerInteractor actor) => actor && actor.HeldBody &&
            actor.HeldBody.GetComponent<RoomKeyItem>() && (IsWaiting || HasPendingMove);

        public override string GetPrompt(PlayerInteractor actor)
        {
            if (OwnerActorId >= 0 && actor != null && OwnerActorId != actor.ActorId)
                return "Staff " + (OwnerActorId + 1) + " is speaking with this guest";
            if (AtServiceDesk) return "Guest waiting at reception · talk";
            if (IsWaiting && !RoomPrepared) return "Room " + displayedRoom + " awaiting preparation · " +
                (actor && actor.HeldBody && actor.HeldBody.GetComponent<RoomKeyItem>() ? "keep the key until ready" : "talk to guest");
            if (IsWaiting || HasPendingMove)
            {
                if (CorrectHeldKey(actor)) return HasPendingMove ? "Give key " + NeededKey + " · exchange rooms" : "Give room " + NeededKey + " key";
                var heldKey = actor && actor.HeldBody ? actor.HeldBody.GetComponent<RoomKeyItem>() : null;
                return heldKey ? "Needs key " + NeededKey + " · you have " + heldKey.roomId : "Talk to guest · room " + NeededKey + " key at reception";
            }
            if (!IsInRoom) return "Guest is on the way";
            if (stay.Agent.State == GuestAgentState.Sleeping) return "Sleeping · Keep voices down";
            if (stay.Agent.Activity == GuestActivity.Shower) return "Using the shower · Guest is busy";
            if (Mode == InteractionMode.Conversation) return ManagementUI.CanAskForQuiet(session.Simulation, stay) ?
                "Talk about the noise · choose a response" : "Talk to guest";
            if (IsTemporarilyQuiet) return "Volume turned down · " +
                Mathf.CeilToInt(stay.Agent.QuietUntil - session.Simulation.Clock.SimulationTime) + " hotel seconds left";
            return GuestLabels.State(stay.Agent);
        }

        public override void Interact(PlayerInteractor actor)
        {
            if (!CanInteract(actor)) return;
            // A carried room key always attempts the physical exchange first. The authority
            // rejects a wrong key or unprepared room without opening UI or dropping the body.
            if ((IsWaiting || HasPendingMove) && actor.HeldBody && actor.HeldBody.GetComponent<RoomKeyItem>())
            {
                session.GiveRoomKey(actor.ActorId, GuestId);
                return;
            }
            session.OpenGuestConversation(actor.ActorId, GuestId, false);
        }

        public override void HoldInteract(PlayerInteractor actor, float deltaTime)
        {
            // Conversation responses use the contextual menu; holding use never chooses for the player.
        }

        public override void EndInteract(PlayerInteractor actor)
        {
            if (actor != null && OwnerActorId == actor.ActorId) ResetHold();
        }

        void ResetHold() { OwnerActorId = -1; HoldProgress = 0; heldMode = InteractionMode.None; }
        void OnDisable() { ResetHold(); if (target != null) target.enabled = false; }
    }
}
