using UnityEngine;

namespace WorstHotel
{
    [RequireComponent(typeof(Rigidbody), typeof(PhysicsPickup))]
    public sealed class RoomKeyItem : PhysicalCarryItem
    {
        public int roomId;
        public Transform rackAnchor;
        public Transform leftInsideAnchor;
        public Rigidbody Body { get; private set; }
        public RoomKeyState State => simulation?.Keys.Find(roomId);
        public HotelSimulation BoundSimulation => simulation;
        HotelSimulation simulation;
        PlayerInteractor carrier;
        Renderer[] visuals;
        Collider[] colliders;
        RoomKeyLocation? shownLocation;
        bool wingLocked;

        void Awake()
        {
            Body = GetComponent<Rigidbody>();
            visuals = GetComponentsInChildren<Renderer>(true);
            colliders = GetComponentsInChildren<Collider>(true);
        }

        void LateUpdate()
        {
            var current = GameSession.Instance ? GameSession.Instance.Simulation : null;
            if (current != simulation)
            {
                if (carrier && carrier.HeldBody == Body) carrier.ReleaseGrab();
                carrier = null;
                simulation = current;
                shownLocation = null;
            }
            var state = State;
            if (state == null) return;
            bool locked = roomId != 0 && !simulation.IsRoomOperational(roomId);
            if (locked)
            {
                foreach (var visual in visuals) visual.enabled = false;
                foreach (var shape in colliders) shape.enabled = false;
                Body.useGravity = false; Body.constraints = RigidbodyConstraints.FreezeAll;
                wingLocked = true; return;
            }
            if (wingLocked) { wingLocked = false; shownLocation = null; }
            if (shownLocation != state.Location)
            {
                // A checkout/reset may return an object. Dispose any joint before changing its body.
                if (state.Location != RoomKeyLocation.HeldByPlayer && carrier && carrier.HeldBody == Body)
                    carrier.ReleaseGrab();
                ShowState(state);
                shownLocation = state.Location;
            }
            if (state.Location == RoomKeyLocation.HeldByGuest)
            {
                var presentation = RoomKeyRack.Instance ? RoomKeyRack.Instance.Guests : null;
                Transform pose = null;
                if (presentation && presentation.TryGetGuestTransform(state.GuestId, out var guestRoot))
                    pose = guestRoot.Find("Body");
                bool visible = pose && pose.gameObject.activeInHierarchy;
                foreach (var visual in visuals) visual.enabled = visible;
                if (visible)
                {
                    Body.position = pose.TransformPoint(new Vector3(.34f, .94f, .21f));
                    Body.rotation = pose.rotation;
                }
            }
            // Recover physics falls outside the building; guest keys left inside stay on their table.
            if (state.Location == RoomKeyLocation.Dropped && Body.position.y < -3)
                simulation.Keys.ReturnToRack(roomId);
        }

        void ShowState(RoomKeyState state)
        {
            bool leftInside = state.Location == RoomKeyLocation.LeftInside;
            bool guestOwned = state.Location == RoomKeyLocation.HeldByGuest;
            bool onRack = state.Location == RoomKeyLocation.OnRack || state.Location == RoomKeyLocation.Returned;
            foreach (var visual in visuals) visual.enabled = true;
            foreach (var shape in colliders) shape.enabled = !guestOwned && !leftInside;
            Body.isKinematic = guestOwned || leftInside;
            Body.useGravity = !guestOwned && !onRack && !leftInside;
            if (leftInside && leftInsideAnchor) { Body.position = leftInsideAnchor.position; Body.rotation = leftInsideAnchor.rotation; }
            Body.constraints = onRack ? RigidbodyConstraints.FreezeAll : RigidbodyConstraints.None;
            if (onRack && rackAnchor)
            {
                Body.linearVelocity = Body.angularVelocity = Vector3.zero;
                Body.position = rackAnchor.position;
                Body.rotation = rackAnchor.rotation;
            }
        }

        public override bool TryBeginCarry(PlayerInteractor player)
        {
            var current = GameSession.Instance ? GameSession.Instance.Simulation : null;
            if (current == null || current != simulation || player == null || roomId != 0 && !current.IsRoomOperational(roomId)) return false;
            var result = simulation.Keys.PickUp(player.ActorId, roomId);
            if (!result.Success) return false;
            Body.constraints = RigidbodyConstraints.None;
            Body.useGravity = true;
            carrier = player;
            shownLocation = RoomKeyLocation.HeldByPlayer;
            return true;
        }

        public override void EndCarry(PlayerInteractor player)
        {
            if (player != carrier) return;
            carrier = null;
            if (GameSession.Instance && GameSession.Instance.Simulation == simulation &&
                State?.Location == RoomKeyLocation.HeldByPlayer && State.PlayerId == player.ActorId)
                simulation.Keys.Drop(player.ActorId, roomId);
        }

        void OnDisable()
        {
            if (carrier && carrier.HeldBody == Body) carrier.ReleaseGrab();
            carrier = null;
        }
    }
}
