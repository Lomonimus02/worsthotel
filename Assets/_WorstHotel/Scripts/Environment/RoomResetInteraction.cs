using System;
using UnityEngine;

namespace WorstHotel
{
    /// <summary>One large, reachable preparation target; authenticated real hold time on the host.</summary>
    public sealed class RoomResetInteraction : HotelInteractable
    {
        public int roomId;
        public RoomDisorder element;
        public float seconds = 3;
        public GameObject disorderVisual;
        public bool hideColliderWhenTidy;
        public Transform movingProp;
        public Vector3 tidyPosition, untidyPosition;
        public Quaternion tidyRotation = Quaternion.identity, untidyRotation = Quaternion.identity;
        public float Progress01 => Mathf.Clamp01(progress / seconds);
        HotelSimulation claimSimulation;
        HousekeepingTask claimTask;
        int owner = -1;
        float progress;
        RoomState Room => GameSession.Instance ? Array.Find(GameSession.Instance.Rooms, r => r.Profile.Id == roomId) : null;
        bool Authority => !LocalCoopBootstrap.Instance || LocalCoopBootstrap.Instance.HasWorldAuthority;
        bool NeedsReset => Room != null && (Room.Disorder & element) != 0;
        public override bool CanInteract(PlayerInteractor actor) => Authority && base.CanInteract(actor) && actor && !actor.HeldBody &&
            NeedsReset && !Room.Occupied && string.IsNullOrEmpty(Room.DepartingGuestId) && Room.Cleanliness == Cleanliness.Dirty &&
            GameSession.Instance.Simulation.Housekeeping.Find(roomId) != null && (owner < 0 || owner == actor.ActorId);
        public override string GetPrompt(PlayerInteractor actor)
        {
            if (!NeedsReset) return "Prepared";
            if (Room.Occupied || !string.IsNullOrEmpty(Room.DepartingGuestId)) return "Prepare after the guest leaves";
            string action = element == RoomDisorder.Waste ? "Empty waste basket" : element == RoomDisorder.Towels ? "Collect used towels" : "Straighten armchair";
            return owner >= 0 && owner != actor?.ActorId ? "Another owner is preparing this" :
                "Hold · " + action + (owner >= 0 ? " · " + Mathf.RoundToInt(Progress01 * 100) + "%" : "");
        }
        public override void Interact(PlayerInteractor actor)
        {
            if (!CanInteract(actor) || actor.Focused != this) return;
            owner = actor.ActorId; progress = 0;
            claimSimulation = GameSession.Instance.Simulation;
            claimTask = claimSimulation.Housekeeping.Find(roomId);
        }
        public override void HoldInteract(PlayerInteractor actor, float deltaTime)
        {
            if (!actor || owner != actor.ActorId) return;
            if (!CanInteract(actor) || actor.Focused != this || !actor.IsInteracting || !Number.IsFinite(deltaTime) || deltaTime <= 0 ||
                claimSimulation != GameSession.Instance.Simulation || claimSimulation.Housekeeping.Find(roomId) != claimTask)
            { Clear(); return; }
            progress += Mathf.Min(deltaTime, .1f);
            if (progress < seconds) return;
            claimSimulation.Housekeeping.ResetRoomElement(roomId, element);
            GameSession.Instance.RaiseChanged(); Clear();
        }
        public override void EndInteract(PlayerInteractor actor) { if (actor && owner == actor.ActorId) Clear(); }
        void Clear() { owner = -1; progress = 0; claimSimulation = null; claimTask = null; }
        void LateUpdate()
        {
            if (!Authority) return; // LAN visuals come from the host's world snapshot.
            bool dirty = NeedsReset;
            if (hideColliderWhenTidy && TryGetComponent<Collider>(out var hit)) hit.enabled = dirty;
            if (disorderVisual) disorderVisual.SetActive(dirty);
            if (movingProp)
            {
                movingProp.localPosition = dirty ? untidyPosition : tidyPosition;
                movingProp.localRotation = dirty ? untidyRotation : tidyRotation;
            }
            if (claimSimulation != null && (!GameSession.Instance || GameSession.Instance.Simulation != claimSimulation || !dirty)) Clear();
        }
        void OnDisable() => Clear();
    }
}
