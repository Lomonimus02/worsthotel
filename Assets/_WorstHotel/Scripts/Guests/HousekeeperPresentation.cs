using System.Collections.Generic;
using UnityEngine;

namespace WorstHotel
{
    /// <summary>One visible worker follows the same authored door routes as guests. Only physical arrival starts cleaning.</summary>
    public sealed class HousekeeperPresentation : MonoBehaviour
    {
        public GuestPresentation guests;
        public Transform standby, worker, body, leftLeg, rightLeg, leftArm, rightArm, broom;
        public TextMesh statusLabel;
        [Min(.1f)] public float walkingSpeed = 1.5f;
        public Transform WorkerTransform => worker;
        public int? CurrentRoomId => physicalRoom != null ? physicalRoom.roomId : (int?)null;

        enum RoutePurpose { EnterRoom, LeaveRoom, Standby }
        readonly Dictionary<int, GuestRoomMarkers> rooms = new Dictionary<int, GuestRoomMarkers>();
        GameSession session;
        HotelSimulation simulation;
        GuestRoomMarkers physicalRoom;
        AuthoredGuestRoute route;
        RoutePurpose purpose;
        int waypoint, destinationRoomId;
        float lastTime, travelBudget, animationTime;
        string lastLabel;

        void OnEnable()
        {
            CacheRooms();
            Bind();
            if (worker != null) worker.gameObject.SetActive(true);
            if (simulation?.Housekeeping != null) simulation.Housekeeping.SetWorkerAvailable(true);
            RebaseClock();
        }

        void Start() { CacheRooms(); Bind(); }

        void OnDisable()
        {
            // Keep both the physical pose and task duration so a temporary disable cannot clean invisibly.
            if (simulation?.Housekeeping != null) simulation.Housekeeping.SetWorkerAvailable(false);
            if (worker != null) worker.gameObject.SetActive(false);
            RebaseClock();
        }

        void OnDestroy() { if (session != null) session.Changed -= SessionChanged; }

        void SessionChanged()
        {
            // A disabled presentation remains subscribed solely to keep a newly-created model paused.
            // It performs no movement and does not consume cleaning time while its worker is hidden.
            if (session != null && !ReferenceEquals(simulation, session.Simulation)) Bind();
        }

        void CacheRooms()
        {
            rooms.Clear();
            if (guests == null || guests.roomMarkers == null) return;
            foreach (var room in guests.roomMarkers)
                if (room != null && room.door != null && room.roomTarget != null) rooms[room.roomId] = room;
        }

        void Bind()
        {
            if (session != GameSession.Instance)
            {
                if (session != null) session.Changed -= SessionChanged;
                session = GameSession.Instance;
                if (session != null) session.Changed += SessionChanged;
            }
            var next = session != null ? session.Simulation : null;
            if (ReferenceEquals(next, simulation)) return;
            if (simulation?.Housekeeping != null) simulation.Housekeeping.SetWorkerAvailable(false);
            simulation = next;
            physicalRoom = null; route = null; destinationRoomId = 0;
            if (worker != null && standby != null) worker.SetPositionAndRotation(standby.position, standby.rotation);
            if (simulation?.Housekeeping != null) simulation.Housekeeping.SetWorkerAvailable(isActiveAndEnabled);
            RebaseClock();
        }

        void RebaseClock()
        {
            travelBudget = 0;
            lastTime = simulation != null ? simulation.Clock.SimulationTime : 0;
        }

        void LateUpdate()
        {
            if (session != GameSession.Instance || (session != null && !ReferenceEquals(simulation, session.Simulation))) Bind();
            if (simulation?.Housekeeping == null || worker == null || standby == null) return;
            var coop = LocalCoopBootstrap.Instance;
            if (coop != null && coop.IsPaused) return;
            float now = simulation.Clock.SimulationTime;
            // StartShift resets the clock; movement budget must not carry over or become negative.
            if (now < lastTime) travelBudget = 0;
            travelBudget += Mathf.Max(0, now - lastTime); lastTime = now;
            float delta = Mathf.Min(travelBudget, Time.deltaTime * Mathf.Max(1, simulation.Clock.Speed));
            travelBudget -= delta;
            if (session.Phase != DayPhase.Planning && session.Phase != DayPhase.Service) delta = 0;
            SynchronizeRoute();
            bool moving = AdvanceRoute(delta);
            var task = simulation.Housekeeping.CurrentTask;
            bool cleaning = task != null && task.State == HousekeepingState.Cleaning &&
                physicalRoom != null && task.RoomId == physicalRoom.roomId;
            Animate(moving, cleaning, delta);
            string label = task == null ? "HOUSEKEEPING\nReady" : "HOUSEKEEPING / " + task.RoomId +
                (cleaning ? "\nCleaning " + Mathf.FloorToInt(task.Progress01 * 100) + "%" : "\nOn the way");
            if (statusLabel != null && label != lastLabel) { statusLabel.text = label; lastLabel = label; }
        }

        void SynchronizeRoute()
        {
            var task = simulation.Housekeeping.CurrentTask;
            if (route != null)
            {
                bool staleEntry = purpose == RoutePurpose.EnterRoom && (task == null || task.RoomId != destinationRoomId);
                bool newTaskWhileReturning = purpose == RoutePurpose.Standby && task != null;
                if (!staleEntry && !newTaskWhileReturning) return;
                route = null;
            }
            if (task != null && task.State == HousekeepingState.Cleaning) return;
            bool inside = physicalRoom != null && AuthoredGuestRoute.IsOnRoomSide(worker.position, physicalRoom);
            if (task != null && task.State == HousekeepingState.Moving)
            {
                if (!rooms.TryGetValue(task.RoomId, out var target)) return;
                if (inside && target == physicalRoom && HorizontalDistance(worker.position, target.roomTarget.position) < .12f)
                {
                    session.ReportHousekeeperReachedRoom(target.roomId);
                    return;
                }
                if (inside) Begin(AuthoredGuestRoute.LeaveRoom(worker.position, physicalRoom), RoutePurpose.LeaveRoom);
                else
                {
                    physicalRoom = target; destinationRoomId = target.roomId;
                    Begin(AuthoredGuestRoute.ToRoom(worker.position, target, false), RoutePurpose.EnterRoom);
                }
            }
            else if (inside) Begin(AuthoredGuestRoute.LeaveRoom(worker.position, physicalRoom), RoutePurpose.LeaveRoom);
            else if (HorizontalDistance(worker.position, standby.position) > .05f)
            {
                physicalRoom = null;
                Begin(AuthoredGuestRoute.ToStandby(worker.position, standby.position), RoutePurpose.Standby);
            }
        }

        void Begin(AuthoredGuestRoute next, RoutePurpose nextPurpose)
        { route = next; purpose = nextPurpose; waypoint = 0; }

        bool AdvanceRoute(float delta)
        {
            if (route == null || delta <= 0) return false;
            bool moved = false;
            float remaining = walkingSpeed * delta;
            while (remaining > 0 && waypoint < route.Points.Count)
            {
                if (waypoint == route.DoorCrossing)
                {
                    route.Door.RequestOpen();
                    if (!route.Door.IsPassageOpen) return moved;
                }
                Vector3 destination = route.Points[waypoint], offset = destination - worker.position;
                float distance = offset.magnitude;
                if (distance < .015f) { waypoint++; continue; }
                float step = Mathf.Min(distance, remaining);
                worker.rotation = Quaternion.RotateTowards(worker.rotation, Quaternion.LookRotation(offset), 280 * delta);
                worker.position = Vector3.MoveTowards(worker.position, destination, step);
                remaining -= step; moved = true;
                if (step >= distance) waypoint++;
            }
            if (waypoint < route.Points.Count) return moved;
            route = null;
            if (purpose == RoutePurpose.LeaveRoom) physicalRoom = null;
            else if (purpose == RoutePurpose.EnterRoom)
            {
                var task = simulation.Housekeeping.CurrentTask;
                if (task != null && task.RoomId == destinationRoomId && task.State == HousekeepingState.Moving)
                    session.ReportHousekeeperReachedRoom(destinationRoomId);
            }
            return moved;
        }

        void Animate(bool moving, bool cleaning, float delta)
        {
            animationTime += delta * (moving ? 7.8f : cleaning ? 4.5f : 1.8f);
            float swing = moving ? Mathf.Sin(animationTime) : 0;
            if (leftLeg != null) leftLeg.localRotation = Quaternion.Euler(25 * swing, 0, 0);
            if (rightLeg != null) rightLeg.localRotation = Quaternion.Euler(-25 * swing, 0, 0);
            float sweep = cleaning ? Mathf.Sin(animationTime) : 0;
            if (leftArm != null) leftArm.localRotation = Quaternion.Euler(cleaning ? -48 + sweep * 12 : -18 * swing, 0, -5);
            if (rightArm != null) rightArm.localRotation = Quaternion.Euler(cleaning ? -35 + sweep * 18 : -10, 0, 5);
            if (body != null)
            {
                body.localPosition = new Vector3(0, moving ? Mathf.Abs(swing) * .025f : 0, 0);
                body.localRotation = Quaternion.Euler(cleaning ? 9 : 0, 0, cleaning ? sweep * 3 : 0);
            }
            if (broom != null) broom.localRotation = Quaternion.Euler(cleaning ? 12 + sweep * 14 : 0, 0, -8);
        }

        static float HorizontalDistance(Vector3 a, Vector3 b) { a.y = b.y; return Vector3.Distance(a, b); }
    }
}
