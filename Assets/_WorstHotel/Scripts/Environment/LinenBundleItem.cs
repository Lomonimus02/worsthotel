using UnityEngine;

namespace WorstHotel
{
    /// <summary>One persistent physical bundle per stock slot or room; ownership lives in the hotel model.</summary>
    [RequireComponent(typeof(Rigidbody), typeof(PhysicsPickup))]
    public sealed class LinenBundleItem : PhysicalCarryItem
    {
        public string itemId;
        public Transform sourceAnchor;
        public int sourceRoomId;
        public Rigidbody Body { get; private set; }
        public LinenBundleState State => simulation?.Housekeeping?.FindLinen(itemId);
        public HotelSimulation BoundSimulation => simulation;
        HotelSimulation simulation;
        PlayerInteractor carrier;
        Renderer[] visuals;
        Collider[] shapes;
        LinenLocation? shownLocation;
        int shownGeneration = -1;

        void Awake()
        {
            Body = GetComponent<Rigidbody>();
            visuals = GetComponentsInChildren<Renderer>(true);
            shapes = GetComponentsInChildren<Collider>(true);
        }

        void LateUpdate()
        {
            var current = GameSession.Instance ? GameSession.Instance.Simulation : null;
            if (current != simulation)
            {
                if (carrier && carrier.HeldBody == Body) carrier.ReleaseGrab();
                carrier = null; simulation = current; shownLocation = null; shownGeneration = -1;
            }
            var state = State;
            if (state == null)
            {
                SetVisible(false); Body.isKinematic = true;
                return;
            }
            if (shownLocation != state.Location || shownGeneration != state.Generation)
            {
                if (state.Location != LinenLocation.HeldByPlayer && carrier && carrier.HeldBody == Body)
                    carrier.ReleaseGrab();
                ShowState(state);
                shownLocation = state.Location; shownGeneration = state.Generation;
            }
            // An out-of-bounds drop is recovered as the same world item, never created in a hand.
            if (state.Location == LinenLocation.Dropped && Body.position.y < -3 && sourceAnchor)
            {
                Body.linearVelocity = Body.angularVelocity = Vector3.zero;
                Body.position = sourceAnchor.position; Body.rotation = sourceAnchor.rotation;
            }
        }

        void ShowState(LinenBundleState state)
        {
            bool visible = state.Location != LinenLocation.InHamper && state.Location != LinenLocation.Consumed;
            bool docked = state.Location == LinenLocation.OnShelf || state.Location == LinenLocation.OnBed;
            SetVisible(visible);
            Body.isKinematic = !visible;
            Body.useGravity = visible && !docked;
            Body.constraints = docked ? RigidbodyConstraints.FreezeAll : RigidbodyConstraints.None;
            if (docked && sourceAnchor)
            {
                Body.linearVelocity = Body.angularVelocity = Vector3.zero;
                Body.position = sourceAnchor.position; Body.rotation = sourceAnchor.rotation;
            }
        }

        void SetVisible(bool visible)
        {
            foreach (var visual in visuals) visual.enabled = visible;
            foreach (var shape in shapes) shape.enabled = visible;
        }

        public override bool TryBeginCarry(PlayerInteractor player)
        {
            var session = GameSession.Instance;
            if (!session || session.Simulation != simulation || !player ||
                (session.Phase != DayPhase.Planning && session.Phase != DayPhase.Service)) return false;
            if (!simulation.PickUpLinen(player.ActorId, itemId).Success) return false;
            Body.constraints = RigidbodyConstraints.None; Body.useGravity = true;
            carrier = player; shownLocation = LinenLocation.HeldByPlayer;
            return true;
        }

        public override void EndCarry(PlayerInteractor player)
        {
            if (carrier != player) return;
            carrier = null;
            if (GameSession.Instance && GameSession.Instance.Simulation == simulation &&
                State?.Location == LinenLocation.HeldByPlayer && State.PlayerId == player.ActorId)
                simulation.DropLinen(player.ActorId, itemId);
        }

        void OnDisable()
        {
            if (carrier && carrier.HeldBody == Body) carrier.ReleaseGrab();
            carrier = null;
        }
    }
}
