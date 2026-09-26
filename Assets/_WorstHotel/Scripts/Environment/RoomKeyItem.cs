using UnityEngine;

namespace WorstHotel
{
    [RequireComponent(typeof(Rigidbody), typeof(PhysicsPickup))]
    public sealed class RoomKeyItem : PhysicalCarryItem
    {
        public int roomId;
        public Transform rackAnchor;
        public Rigidbody Body { get; private set; }
        public RoomKeyState State => simulation?.Keys.Find(roomId);
        public HotelSimulation BoundSimulation => simulation;
        HotelSimulation simulation;
        PlayerInteractor carrier;
        Renderer[] visuals;
        Collider[] colliders;
        RoomKeyLocation? shownLocation;

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
            // No lost-key system: an accidental fall outside the playable building returns the same key.
            if (state.Location == RoomKeyLocation.Dropped && Body.position.y < -3)
                simulation.Keys.ReturnToRack(roomId);
        }

        void ShowState(RoomKeyState state)
        {
            bool guestOwned = state.Location == RoomKeyLocation.HeldByGuest;
            bool onRack = state.Location == RoomKeyLocation.OnRack || state.Location == RoomKeyLocation.Returned;
            foreach (var visual in visuals) visual.enabled = true;
            foreach (var shape in colliders) shape.enabled = !guestOwned;
            Body.isKinematic = guestOwned;
            Body.useGravity = !guestOwned && !onRack;
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
            if (current == null || current != simulation || player == null) return false;
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
