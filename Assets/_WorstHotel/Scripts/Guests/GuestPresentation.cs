using System.Collections.Generic;
using UnityEngine;

namespace WorstHotel
{
    /// <summary>Authored-route presentation of authoritative living guests; it only reports physical arrivals.</summary>
    public sealed partial class GuestPresentation : MonoBehaviour
    {
        [Min(.1f)] public float walkingSpeed = 1.35f;
        public Transform arrivalSpawn;
        public Transform[] receptionPlaces;
        public GuestRoomMarkers[] roomMarkers;
        public int VisibleGuestCount => guests.Count;

        enum RoutePurpose { Reception, Room, Activity, Exit, Transfer, Away, Return, ServiceReception, ServiceReturn }
        sealed class VisualGuest
        {
            public string Id;
            public GuestStay Stay;
            public Transform Root, Body, LeftLeg, RightLeg, LeftArm, RightArm, Phone;
            public GuestRoomMarkers Room, TransferDestination;
            public RoomState ModelRoom;
            public AuthoredGuestRoute Route;
            public RoutePurpose Purpose;
            public int Waypoint;
            public float AnimationTime;
            public float StepDistance, NextStepTime;
            public GuestAgentState State;
            public GuestActivity Activity;
            public bool InsideRoom, RouteComplete;
            public int AppearanceIndex, ReceptionSlot = -1;
            public bool WaitingForReception, Detouring;
            public float TrafficSeconds;
            public float PoseBlend, SettlingTime, BlockedSeconds;
            public int RecoveryCount;
            public string PathStatus = "Following authored route";
            public bool DoorClosedAfterCrossing;
            public string ResponseActionId;
            public int ResponseActionVersion;
            public bool ResponseArrivalReported;
            public float ResponseRetryAfter;
        }

        readonly Dictionary<string, VisualGuest> guests = new Dictionary<string, VisualGuest>();
        readonly Dictionary<string, Transform> lanReplicaRoots = new Dictionary<string, Transform>();
        readonly List<string> removedGuests = new List<string>();
        readonly HashSet<string> currentGuestIds = new HashSet<string>();
        readonly HashSet<string> modelGuestIds = new HashSet<string>();
        readonly Dictionary<int, GuestRoomMarkers> rooms = new Dictionary<int, GuestRoomMarkers>();
        readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        GameSession session;
        HotelSimulation simulation;
        Transform visualRoot;
        bool refreshPending = true, subscribed;
        float lastSimulationTime, travelBudget;

        public bool TryGetGuestTransform(string id, out Transform result)
        {
            if (guests.TryGetValue(id, out var guest)) { result = guest.Root; return result != null; }
            return lanReplicaRoots.TryGetValue(id, out result) && result != null;
        }

        public bool TryGetGuestReceptionSlot(string id, out int slot)
        {
            if (guests.TryGetValue(id, out var guest)) { slot = guest.ReceptionSlot; return slot >= 0; }
            slot = -1; return false;
        }

        static bool NeedsReceptionSlot(GuestAgent agent) => agent != null &&
            (agent.State == GuestAgentState.Arriving || agent.State == GuestAgentState.WaitingForCheckIn ||
             agent.State == GuestAgentState.GoingToServiceReception || agent.State == GuestAgentState.WaitingAtServiceReception);

        int FreeReceptionSlot()
        {
            for (int slot = 0; slot < receptionPlaces.Length; slot++)
            {
                if (!receptionPlaces[slot]) continue;
                bool used = false;
                foreach (var guest in guests.Values)
                    if (guest.ReceptionSlot == slot) { used = true; break; }
                if (!used) return slot;
            }
            return -1;
        }

        static int StableAppearanceIndex(string id)
        {
            uint value = 2166136261;
            unchecked { foreach (char character in id) value = (value ^ character) * 16777619; }
            return (int)(value % 1024);
        }

        // LAN views share the authored original kit, but never create interaction colliders,
        // schedules or arrival/departure callbacks on the replica computer.
        public LanGuestView CreateLanReplica(Transform parent, LanWorldGuest pose)
        {
            var guest = new VisualGuest { Root = new GameObject(pose.name).transform };
            guest.Root.SetParent(parent, false);
            BuildAppearance(guest, pose.kind, pose.appearanceIndex, pose.specialKind);
            lanReplicaRoots[pose.id] = guest.Root;
            var view = new LanGuestView { root = guest.Root, body = guest.Body, leftArm = guest.LeftArm,
                rightArm = guest.RightArm, leftLeg = guest.LeftLeg, rightLeg = guest.RightLeg, phone = guest.Phone };
            view.Apply(pose);
            return view;
        }
        public void ReleaseLanReplica(string id, Transform root)
        {
            if (lanReplicaRoots.TryGetValue(id, out var current) && current == root) lanReplicaRoots.Remove(id);
        }

        public LanWorldGuest[] CaptureLanGuests()
        {
            var result = new List<LanWorldGuest>(guests.Count);
            foreach (var guest in guests.Values)
            {
                if (!guest.Root || !guest.Root.gameObject.activeInHierarchy) continue;
                result.Add(new LanWorldGuest { id = guest.Id, name = guest.Stay.Name,
                    kind = guest.Stay.Application.Archetype.Kind, specialKind = guest.Stay.Application.SpecialKind, appearanceIndex = guest.AppearanceIndex,
                    position = guest.Root.position, rotation = guest.Root.rotation,
                    bodyOffset = guest.Body.localPosition, bodyRotation = guest.Body.localRotation,
                    bodyVisible = guest.Body.gameObject.activeSelf, phoneVisible = guest.Phone && guest.Phone.gameObject.activeSelf,
                    leftArm = guest.LeftArm.localRotation, rightArm = guest.RightArm.localRotation,
                    leftLeg = guest.LeftLeg.localRotation, rightLeg = guest.RightLeg.localRotation });
            }
            return result.ToArray();
        }

        void OnEnable() => Bind();
        void Start() => Bind();
        void OnDisable()
        {
            if (session != null) session.Changed -= MarkDirty;
            subscribed = false;
            if (visualRoot != null) visualRoot.gameObject.SetActive(false);
            RebaseClock();
            ResetActivitySources();
        }

        void OnDestroy()
        {
            ClearGuests(true);
            if (visualRoot != null) Destroy(visualRoot.gameObject);
            foreach (var material in materials.Values) if (material != null) Destroy(material);
            materials.Clear();
        }

        void MarkDirty() => refreshPending = true;

        void Bind()
        {
            if (session != GameSession.Instance)
            {
                if (session != null) session.Changed -= MarkDirty;
                session = GameSession.Instance;
                subscribed = false;
            }
            if (!subscribed && session != null) { session.Changed += MarkDirty; subscribed = true; RebaseClock(); }
            if (visualRoot == null)
            {
                visualRoot = new GameObject("Guest visual presentation").transform;
                visualRoot.SetParent(transform, false);
            }
            visualRoot.gameObject.SetActive(true);
            rooms.Clear();
            if (roomMarkers != null)
                foreach (var room in roomMarkers)
                    if (room != null && room.door != null && room.roomTarget != null) rooms[room.roomId] = room;
            refreshPending = true;
        }

        void RebaseClock()
        {
            travelBudget = 0;
            lastSimulationTime = simulation != null ? simulation.Clock.SimulationTime : 0;
        }

        void Refresh()
        {
            refreshPending = false;
            var next = session != null ? session.Simulation : null;
            if (!ReferenceEquals(next, simulation))
            {
                ClearGuests(); simulation = next; travelBudget = 0;
                lastSimulationTime = simulation != null ? simulation.Clock.SimulationTime : 0;
            }
            if (simulation == null || !simulation.LivingEnabled) { ClearGuests(); return; }
            currentGuestIds.Clear();
            modelGuestIds.Clear();
            int index = 0;
            foreach (var stay in simulation.Guests)
            {
                var agent = stay.Agent;
                modelGuestIds.Add(stay.GuestId);
                bool visible = agent != null && agent.State != GuestAgentState.Scheduled && agent.State != GuestAgentState.Left;
                if (session.Phase != DayPhase.Service)
                    visible &= agent != null && (agent.State == GuestAgentState.Leaving || agent.State == GuestAgentState.CheckingOut);
                if (visible)
                {
                    currentGuestIds.Add(stay.GuestId);
                    if (!guests.ContainsKey(stay.GuestId)) CreateGuest(stay, index);
                }
                index++;
            }
            removedGuests.Clear();
            foreach (var pair in guests)
                if (!currentGuestIds.Contains(pair.Key))
                {
                    var state = pair.Value.Stay.Agent.State;
                    // StartShift replaces the model roster. Old bodies still own their departure token
                    // until they physically leave, even if the next day's guests have already arrived.
                    bool oldDeparture = !modelGuestIds.Contains(pair.Key) &&
                        (state == GuestAgentState.Leaving || state == GuestAgentState.CheckingOut);
                    if (!oldDeparture) removedGuests.Add(pair.Key);
                }
            foreach (var id in removedGuests) RemoveGuest(id);
        }

        void CreateGuest(GuestStay stay, int index)
        {
            if (arrivalSpawn == null || receptionPlaces == null || receptionPlaces.Length == 0 || !rooms.TryGetValue(stay.RoomId, out var room))
            {
                Debug.LogError("Living guests require authored reception and room markers. Rebuild the prototype scene.");
                return;
            }
            bool needsPlace = NeedsReceptionSlot(stay.Agent);
            int receptionSlot = needsPlace ? AcquireReceptionSlot(stay.GuestId) : -1;
            // Only actual arrivals/waiters require a queue berth before creating their body.
            // A returning service visitor can wait inside their room until a berth is free.
            if (receptionSlot < 0 && needsPlace && stay.Agent.State != GuestAgentState.GoingToServiceReception) return;
            int appearance = simulation.ContinuousOperations ? StableAppearanceIndex(stay.GuestId) : index;
            var guest = new VisualGuest
            {
                Id = stay.GuestId, Stay = stay, Room = room,
                ModelRoom = System.Array.Find(session.Rooms, state => state.Profile.Id == stay.RoomId),
                Root = new GameObject(stay.Name + " — room " + stay.RoomId).transform,
                AnimationTime = appearance * .79f, State = stay.Agent.State, Activity = stay.Agent.Activity,
                AppearanceIndex = appearance, ReceptionSlot = receptionSlot
            };
            guest.Root.SetParent(visualRoot, false);
            guest.Root.position = new Vector3(arrivalSpawn.position.x, .01f, arrivalSpawn.position.z);
            BuildAppearance(guest, stay.Application.Archetype.Kind, appearance);
            guest.Root.gameObject.AddComponent<GuestReceptionInteraction>().Initialize(session, stay);
            guest.Root.gameObject.AddComponent<GuestPhysicalReaction>().Initialize(guest.Body);
            guests.Add(stay.GuestId, guest);
            simulation.RegisterGuestPhysicalStaging(stay.GuestId);
            Vector3 reception = receptionSlot >= 0 ? receptionPlaces[receptionSlot].position : new Vector3(.55f, .01f, .35f);
            if (stay.Agent.State == GuestAgentState.Arriving || stay.Agent.State == GuestAgentState.WaitingForCheckIn)
                SetRoute(guest, AuthoredGuestRoute.Arrival(reception), RoutePurpose.Reception);
            if (stay.Agent.State == GuestAgentState.GoingToRoom)
            {
                if (stay.Agent.IsRelocating && stay.Agent.TransferFromRoomId.HasValue &&
                    rooms.TryGetValue(stay.Agent.TransferFromRoomId.Value, out var origin))
                {
                    AssignPhysicalRoom(guest, origin);
                    guest.Root.position = origin.roomTarget.position;
                    guest.InsideRoom = true; guest.TransferDestination = room;
                    SetRoute(guest, AuthoredGuestRoute.LeaveRoom(guest.Root.position, origin), RoutePurpose.Transfer);
                }
                else
                {
                    guest.Root.position = reception;
                    SetRoute(guest, AuthoredGuestRoute.ToRoom(guest.Root.position, room, false), RoutePurpose.Room);
                }
            }
            else if (stay.Agent.State == GuestAgentState.Leaving || stay.Agent.State == GuestAgentState.CheckingOut)
            {
                // Reconstruct an interrupted transfer from the departure token, never from its unused
                // destination reservation. Already-vacated departures start at the public exit.
                var pending = System.Array.Find(session.Rooms, state => state.DepartingGuestId == stay.GuestId);
                if (pending != null && rooms.TryGetValue(pending.Profile.Id, out var origin))
                {
                    AssignPhysicalRoom(guest, origin);
                    guest.Root.position = new Vector3(origin.roomTarget.position.x, .01f, origin.roomTarget.position.z);
                    guest.InsideRoom = true;
                }
                SetRoute(guest, AuthoredGuestRoute.Exit(guest.Root.position, guest.Room, arrivalSpawn.position, guest.InsideRoom), RoutePurpose.Exit);
            }
            else if (stay.Agent.State == GuestAgentState.GuestAway || stay.Agent.State == GuestAgentState.ReturningToRoom)
            {
                guest.Root.position = new Vector3(arrivalSpawn.position.x, .01f, arrivalSpawn.position.z);
                guest.InsideRoom = false;
                if (stay.Agent.State == GuestAgentState.ReturningToRoom)
                    SetRoute(guest, AuthoredGuestRoute.ToRoom(guest.Root.position, room, false), RoutePurpose.Return);
                else { guest.Route = null; guest.RouteComplete = true; guest.PathStatus = "Away outside hotel"; guest.Body.gameObject.SetActive(false); }
            }
            else if (stay.Agent.State == GuestAgentState.LeavingRoom)
            {
                guest.Root.position = room.roomTarget.position;
                guest.InsideRoom = true;
                SetRoute(guest, AuthoredGuestRoute.GuestAway(guest.Root.position, room, arrivalSpawn.position), RoutePurpose.Away);
            }
            else if (stay.Agent.IsServiceReceptionTrip)
            {
                // Reconstruct a newly bound presentation at its known semantic location;
                // normal visits retain their existing body and follow the full physical route.
                guest.InsideRoom = stay.Agent.State == GuestAgentState.GoingToServiceReception;
                guest.Root.position = guest.InsideRoom ? room.roomTarget.position : reception;
            }
            else if (stay.Agent.HasReachedRoom)
            {
                guest.Root.position = new Vector3(room.roomTarget.position.x, .01f, room.roomTarget.position.z);
                guest.InsideRoom = true;
                SetRoute(guest, stay.Agent.State == GuestAgentState.Leaving ?
                    AuthoredGuestRoute.Exit(guest.Root.position, room, arrivalSpawn.position, true) :
                    AuthoredGuestRoute.Activity(guest.Root.position, room, stay.Agent.Activity, stay.Agent.State == GuestAgentState.Sleeping),
                    stay.Agent.State == GuestAgentState.Leaving ? RoutePurpose.Exit : RoutePurpose.Activity);
            }
            SynchronizeResponseRoute(guest);
        }

        void LateUpdate()
        {
            if (session != GameSession.Instance) Bind();
            PruneReceptionRequests();
            if (refreshPending) Refresh();
            if (session == null || simulation == null) return;
            var coop = LocalCoopBootstrap.Instance;
            if (coop != null && coop.IsPaused) return;
            float time = simulation.Clock.SimulationTime;
            if (time < lastSimulationTime) travelBudget = 0;
            travelBudget += Mathf.Max(0, time - lastSimulationTime);
            lastSimulationTime = time;
            // Spend only time that the hotel actually advanced, smoothing the fixed simulation ticks.
            float delta = Mathf.Min(travelBudget, Time.deltaTime * Mathf.Max(1, simulation.Clock.Speed));
            travelBudget -= delta;
            if (session.Phase != DayPhase.Service && session.Phase != DayPhase.Planning) delta = Time.deltaTime;
            foreach (var room in rooms.Values) { room.showerActive = false; room.loudActive = false; }
            removedGuests.Clear();
            foreach (var guest in guests.Values)
            {
                if (guest.Root == null || guest.Stay.Agent == null) continue;
                SynchronizeState(guest);
                if (guest.Root.GetComponent<GuestPhysicalReaction>().AnimateRecovery(Time.deltaTime)) continue;
                // Get out of the bed before starting another walk. This blend is real time so WAIT
                // does not turn a visible wake-up into a single-frame teleport.
                if (guest.State != GuestAgentState.Sleeping && guest.PoseBlend > 0)
                    guest.PoseBlend = Mathf.MoveTowards(guest.PoseBlend, 0, Time.deltaTime / .85f);
                Vector3 previousPosition = guest.Root.position;
                bool moving = guest.PoseBlend > 0 && guest.State != GuestAgentState.Sleeping ? false : AdvanceRoute(guest, delta);
                if (moving)
                {
                    guest.StepDistance += Vector3.Distance(previousPosition, guest.Root.position);
                    if (guest.StepDistance >= .9f && Time.unscaledTime >= guest.NextStepTime)
                    {
                        guest.StepDistance %= .9f;
                        guest.NextStepTime = Time.unscaledTime + .22f;
                        HotelFeedback.PlayGuestFootstep(guest.Root.position);
                    }
                }
                ReportVacatedRooms(guest, false);
                if (guest.RouteComplete) OnRouteComplete(guest);
                ReleaseClearedReceptionSlot(guest);
                RetryCompletedResponseRoute(guest);
                bool doingActivity = guest.InsideRoom && guest.RouteComplete && guest.Stay.Agent.IsRoomState;
                UpdateStaging(guest, doingActivity);
                if (doingActivity)
                {
                    guest.Room.showerActive |= guest.Activity == GuestActivity.Shower && guest.Stay.Agent.ActivityStaged;
                    guest.Room.loudActive |= (guest.Activity == GuestActivity.LoudRoom || guest.Activity == GuestActivity.WatchTV) && guest.Stay.Agent.ActivityStaged && guest.ModelRoom != null && guest.ModelRoom.HasPower;
                }
                if (!moving) FaceActivity(guest, delta);
                Animate(guest, moving, doingActivity, delta);
            }
            foreach (var id in removedGuests) RemoveGuest(id);
            foreach (var room in rooms.Values)
            {
                if (room.showerWater != null && room.showerWater.activeSelf != room.showerActive) room.showerWater.SetActive(room.showerActive);
                if (room.showerCurtain != null && room.showerCurtain.activeSelf != room.showerActive) room.showerCurtain.SetActive(room.showerActive);
                if (room.loudIndicator != null && room.loudIndicator.activeSelf != room.loudActive) room.loudIndicator.SetActive(room.loudActive);
            }
        }

        void SynchronizeState(VisualGuest guest)
        {
            var agent = guest.Stay.Agent;
            // During transfer Room remains the physical origin until its doorway has been exited.
            // A checkout can interrupt either leg and must use that physical room, not the reservation.
            if (agent.State == GuestAgentState.Leaving || !modelGuestIds.Contains(guest.Id))
            {
                if (guest.State != GuestAgentState.Leaving)
                {
                    guest.TransferDestination = null;
                    SetRoute(guest, AuthoredGuestRoute.Exit(guest.Root.position, guest.Room, arrivalSpawn.position, guest.InsideRoom), RoutePurpose.Exit);
                }
                guest.State = GuestAgentState.Leaving; guest.Activity = agent.Activity;
                return;
            }
            if (agent.State == GuestAgentState.CheckingOut)
            {
                guest.State = agent.State; guest.Activity = agent.Activity;
                guest.TransferDestination = null; guest.Route = null; guest.RouteComplete = true;
                return;
            }
            if (guest.Room.roomId != guest.Stay.RoomId && guest.TransferDestination == null &&
                rooms.TryGetValue(guest.Stay.RoomId, out var reassigned))
            {
                guest.InsideRoom |= AuthoredGuestRoute.IsOnRoomSide(guest.Root.position, guest.Room);
                if (guest.InsideRoom)
                {
                    guest.TransferDestination = reassigned;
                    SetRoute(guest, AuthoredGuestRoute.LeaveRoom(guest.Root.position, guest.Room), RoutePurpose.Transfer);
                }
                else
                {
                    AssignPhysicalRoom(guest, reassigned);
                    SetRoute(guest, AuthoredGuestRoute.ToRoom(guest.Root.position, reassigned, false), RoutePurpose.Room);
                }
                guest.State = agent.State; guest.Activity = agent.Activity;
                return;
            }
            if (SynchronizeResponseRoute(guest)) return;
            if (guest.State == agent.State && guest.Activity == agent.Activity) return;
            guest.State = agent.State; guest.Activity = agent.Activity;
            switch (agent.State)
            {
                case GuestAgentState.GoingToRoom:
                    SetRoute(guest, AuthoredGuestRoute.ToRoom(guest.Root.position, guest.Room, guest.InsideRoom), RoutePurpose.Room);
                    break;
                case GuestAgentState.ReturningToRoom:
                    SetRoute(guest, AuthoredGuestRoute.ToRoom(guest.Root.position, guest.Room, guest.InsideRoom), RoutePurpose.Return);
                    break;
                case GuestAgentState.LeavingRoom:
                    SetRoute(guest, AuthoredGuestRoute.GuestAway(guest.Root.position, guest.Room, arrivalSpawn.position), RoutePurpose.Away);
                    break;
                case GuestAgentState.GuestAway:
                    guest.Route = null; guest.RouteComplete = true;
                    break;
                case GuestAgentState.InRoom:
                case GuestAgentState.PerformingActivity:
                case GuestAgentState.Sleeping:
                    if (guest.InsideRoom)
                        SetRoute(guest, AuthoredGuestRoute.Activity(guest.Root.position, guest.Room, agent.Activity,
                            agent.State == GuestAgentState.Sleeping), RoutePurpose.Activity);
                    break;
            }
        }

        void AssignPhysicalRoom(VisualGuest guest, GuestRoomMarkers room)
        {
            guest.Room = room;
            guest.ModelRoom = System.Array.Find(session.Rooms, state => state.Profile.Id == room.roomId);
            guest.InsideRoom = false;
        }

        static void SetRoute(VisualGuest guest, AuthoredGuestRoute route, RoutePurpose purpose)
        {
            guest.Route = route; guest.Purpose = purpose; guest.Waypoint = 0; guest.RouteComplete = false;
            guest.SettlingTime = 0; guest.BlockedSeconds = 0; guest.TrafficSeconds = 0;
            guest.Detouring = false; guest.DoorClosedAfterCrossing = false;
            guest.PathStatus = "Following authored route";
        }

        bool AdvanceRoute(VisualGuest guest, float delta)
        {
            if (guest.Route == null || guest.RouteComplete || delta <= 0) return false;
            float remaining = walkingSpeed * delta;
            bool moved = false;
            while (remaining > 0 && guest.Waypoint < guest.Route.Points.Count)
            {
                if (guest.Waypoint == guest.Route.DoorCrossing)
                {
                    guest.Route.Door.RequestGuestOpen(guest.Id);
                    if (!guest.Route.Door.IsPassageOpen) { guest.PathStatus = "Waiting for doorway"; return moved; }
                }
                Vector3 destination = guest.Route.Points[guest.Waypoint];
                Vector3 offset = destination - guest.Root.position;
                float distance = offset.magnitude;
                if (distance < .015f) { guest.Waypoint++; guest.Detouring = false; guest.TrafficSeconds = 0; continue; }
                float step = Mathf.Min(distance, remaining, .15f);
                if (BlockedByEnvironment(guest, destination, step))
                {
                    RecoverRoute(guest, delta);
                    return moved;
                }
                guest.PathStatus = "Following authored route";
                guest.Root.rotation = Quaternion.RotateTowards(guest.Root.rotation, Quaternion.LookRotation(offset), 280 * delta);
                var nextPosition = Vector3.MoveTowards(guest.Root.position, destination, step);
                if (!GuestPhysicalReaction.TryWalk(guest.Root, nextPosition, out nextPosition))
                { RecoverTraffic(guest, delta); return moved; }
                guest.Root.position = nextPosition;
                guest.BlockedSeconds = 0;
                if (guest.Purpose == RoutePurpose.Room || guest.Purpose == RoutePurpose.Transfer || guest.Purpose == RoutePurpose.Exit ||
                    guest.Purpose == RoutePurpose.Away || guest.Purpose == RoutePurpose.Return ||
                    guest.Purpose == RoutePurpose.ServiceReception || guest.Purpose == RoutePurpose.ServiceReturn)
                    guest.InsideRoom = AuthoredGuestRoute.IsOnRoomSide(guest.Root.position, guest.Room);
                // Sideways sliding can move a body without making useful forward progress.
                if (distance - Vector3.Distance(nextPosition, destination) < step * .35f)
                { RecoverTraffic(guest, delta); return true; }
                guest.TrafficSeconds = 0;
                remaining -= step; moved = true;
                if (Vector3.Distance(guest.Root.position, destination) < .015f) { guest.Waypoint++; guest.Detouring = false; }
                ClosePassedDoor(guest);
            }
            guest.RouteComplete = guest.Waypoint >= guest.Route.Points.Count;
            if (guest.RouteComplete) guest.PathStatus = "At destination";
            return moved;
        }

        void OnRouteComplete(VisualGuest guest)
        {
            // Completion signals are idempotent from presentation: consume the route before raising Changed.
            if (guest.Route == null) return;
            guest.Route = null;
            switch (guest.Purpose)
            {
                case RoutePurpose.Reception:
                    if (guest.Stay.Agent.State == GuestAgentState.Arriving && session.ReportGuestReachedReception(guest.Id).Success)
                        HotelFeedback.PlayReceptionArrival();
                    break;
                case RoutePurpose.Room:
                    guest.InsideRoom = true;
                    guest.Room.door.CloseAfterGuestPassage(guest.Id);
                    session.ReportGuestReachedRoom(guest.Id);
                    break;
                case RoutePurpose.Return:
                    guest.InsideRoom = true;
                    guest.Room.door.CloseAfterGuestPassage(guest.Id);
                    simulation.SignalGuestReturnedRoom(guest.Id); session.RaiseChanged();
                    break;
                case RoutePurpose.ServiceReception:
                    guest.InsideRoom = false;
                    guest.Room.door.CloseAfterGuestPassage(guest.Id);
                    ReportResponseArrival(guest, GuestResponseAnchor.Reception);
                    break;
                case RoutePurpose.ServiceReturn:
                    guest.InsideRoom = true;
                    guest.Room.door.CloseAfterGuestPassage(guest.Id);
                    ReportResponseArrival(guest, GuestResponseAnchor.AssignedRoom);
                    break;
                case RoutePurpose.Away:
                    guest.InsideRoom = false;
                    guest.Room.door.CloseAfterGuestPassage(guest.Id);
                    simulation.SignalGuestLeftRoom(guest.Id); session.RaiseChanged();
                    break;
                case RoutePurpose.Transfer:
                    if (guest.TransferDestination != null) AssignPhysicalRoom(guest, guest.TransferDestination);
                    guest.TransferDestination = null;
                    SetRoute(guest, AuthoredGuestRoute.ToRoom(guest.Root.position, guest.Room, false), RoutePurpose.Room);
                    break;
                case RoutePurpose.Exit:
                    if (modelGuestIds.Contains(guest.Id)) session.ReportGuestLeft(guest.Id);
                    removedGuests.Add(guest.Id);
                    break;
            }
        }

        static void FaceActivity(VisualGuest guest, float delta)
        {
            Vector3 direction;
            if (guest.State == GuestAgentState.Sleeping && guest.InsideRoom && guest.RouteComplete && guest.Room.bedApproach)
            {
                guest.Root.rotation = Quaternion.RotateTowards(guest.Root.rotation, guest.Room.bedApproach.rotation, 160 * delta);
                return;
            }
            if (!guest.InsideRoom) direction = Vector3.back;
            else if (guest.Activity == GuestActivity.Shower) direction = new Vector3(Mathf.Sign(guest.Room.door.transform.position.x), 0, 0);
            else if (guest.Activity == GuestActivity.LoudRoom || guest.Activity == GuestActivity.WatchTV) direction = new Vector3(Mathf.Sign(guest.Room.door.transform.position.x), 0, .6f);
            else if (guest.Activity == GuestActivity.Work) direction = Vector3.forward;
            else if (guest.Activity == GuestActivity.AdjustRadiator && guest.Room.radiatorTarget) direction = guest.Room.radiatorTarget.position - guest.Root.position;
            else if (guest.Activity == GuestActivity.CallReception && guest.Room.roomPhoneTarget) direction = guest.Room.roomPhoneTarget.position - guest.Root.position;
            else if (guest.Activity == GuestActivity.Unpack || guest.Activity == GuestActivity.Pack) direction = Vector3.back;
            else direction = guest.Room.door.transform.position - guest.Root.position;
            direction.y = 0;
            if (direction.sqrMagnitude > .01f)
                guest.Root.rotation = Quaternion.RotateTowards(guest.Root.rotation, Quaternion.LookRotation(direction), 100 * delta);
        }

        static void Animate(VisualGuest guest, bool walking, bool activity, float delta)
        {
            guest.AnimationTime += delta * (walking ? 7.8f : 1.8f);
            float swing = walking ? Mathf.Sin(guest.AnimationTime) : 0;
            guest.LeftLeg.localRotation = Quaternion.Euler(swing * 25, 0, 0);
            guest.RightLeg.localRotation = Quaternion.Euler(-swing * 25, 0, 0);
            float left = -swing * 18, right = swing * 18;
            if (!walking && (guest.State == GuestAgentState.WaitingForCheckIn || guest.State == GuestAgentState.WaitingAtServiceReception)) { left = -32; right = -55; }
            if (activity && guest.Activity == GuestActivity.Shower) { left = -135 + Mathf.Sin(guest.AnimationTime) * 12; right = -135 - Mathf.Sin(guest.AnimationTime) * 12; }
            if (activity && guest.Activity == GuestActivity.LoudRoom) { left = -35 + Mathf.Sin(guest.AnimationTime * 2) * 22; right = -35 - Mathf.Sin(guest.AnimationTime * 2) * 22; }
            if (activity && (guest.Activity == GuestActivity.PhoneCall || guest.Activity == GuestActivity.CallReception)) { left = -18; right = -155 + Mathf.Sin(guest.AnimationTime) * 4; }
            if (activity && guest.Activity == GuestActivity.AdjustRadiator) { left = -24; right = -78 + Mathf.Sin(guest.AnimationTime * 4) * 8; }
            if (activity && guest.Activity == GuestActivity.Work) { left = -55 + Mathf.Sin(guest.AnimationTime * 3) * 4; right = -55 - Mathf.Sin(guest.AnimationTime * 3) * 4; }
            if (activity && (guest.Activity == GuestActivity.Unpack || guest.Activity == GuestActivity.Pack))
            { left = -35 + Mathf.Sin(guest.AnimationTime) * 9; right = -35 - Mathf.Sin(guest.AnimationTime) * 9; }
            if (guest.Phone) guest.Phone.gameObject.SetActive(activity &&
                (guest.Stay.Agent.ActivityStaged && guest.Activity == GuestActivity.PhoneCall || guest.Activity == GuestActivity.CallReception));
            var needs = guest.Stay.Needs;
            bool resting = !walking && guest.Stay.Agent.InAssignedRoom && guest.Activity == GuestActivity.QuietRest;
            if (resting && needs != null && needs.Temperature.Severity > .3f && guest.ModelRoom != null &&
                guest.ModelRoom.Temperature < guest.Stay.Application.Archetype.Needs.PreferredTemperatureMin)
            {
                float shiver = Mathf.Sin(guest.AnimationTime * 12) * 4 * needs.Temperature.Severity;
                left = -65 + shiver; right = -65 - shiver;
            }
            else if (resting && needs != null && needs.Noise.Severity > .3f)
            { left = -145; right = -145; }
            guest.LeftArm.localRotation = Quaternion.Euler(left, 0, -5);
            guest.RightArm.localRotation = Quaternion.Euler(right, 0, 5);
            var upright = new Vector3(0, walking ? Mathf.Abs(swing) * .035f : Mathf.Sin(guest.AnimationTime) * .012f, 0);
            guest.Body.localPosition = upright;
            guest.Body.localRotation = Quaternion.identity;
            if (guest.PoseBlend > 0 && guest.Room.bedAnchor)
            {
                float blend = Mathf.SmoothStep(0, 1, guest.PoseBlend);
                guest.Body.position = Vector3.Lerp(guest.Root.TransformPoint(upright), guest.Room.bedAnchor.position, blend);
                guest.Body.rotation = Quaternion.Slerp(guest.Root.rotation, guest.Room.bedAnchor.rotation, blend);
                guest.LeftArm.localRotation = Quaternion.Euler(-8, 0, -8);
                guest.RightArm.localRotation = Quaternion.Euler(-8, 0, 8);
            }
        }

        void ResetActivitySources()
        {
            foreach (var room in rooms.Values)
            {
                if (room.showerWater != null && room.showerWater.activeSelf) room.showerWater.SetActive(false);
                if (room.showerCurtain != null && room.showerCurtain.activeSelf) room.showerCurtain.SetActive(false);
                if (room.loudIndicator != null && room.loudIndicator.activeSelf) room.loudIndicator.SetActive(false);
            }
        }

        void RemoveGuest(string id)
        {
            if (!guests.TryGetValue(id, out var guest)) return;
            if (guest.Root != null)
            {
                guest.Root.gameObject.SetActive(false);
                ReportVacatedRooms(guest, true);
                Destroy(guest.Root.gameObject);
            }
            guests.Remove(id);
            receptionRequests.Remove(id);
            refreshPending = true;
        }

        void ReportVacatedRooms(VisualGuest guest, bool bodyRemoved)
        {
            // Only the captured simulation owns these tokens. A NewGame must never acknowledge an old
            // body against its fresh rooms, whose guest ids can legitimately be reused.
            if (simulation == null || session == null || !ReferenceEquals(simulation, session.Simulation)) return;
            foreach (var room in session.Rooms)
            {
                if (room.DepartingGuestId != guest.Id) continue;
                // Removal already proves vacancy, including scene teardown where doors may die first.
                if (bodyRemoved) { session.ReportGuestVacatedRoom(guest.Id, room.Profile.Id); continue; }
                if (!guest.Root || !rooms.TryGetValue(room.Profile.Id, out var marker) || !marker.door) continue;
                Vector3 position = guest.Root.position, door = marker.door.transform.position;
                bool overlapsRoom = Mathf.Abs(position.z - door.z) < 3.5f &&
                    position.x * Mathf.Sign(door.x) > Mathf.Abs(door.x) - .52f;
                if (!overlapsRoom) session.ReportGuestVacatedRoom(guest.Id, room.Profile.Id);
            }
        }
        void BuildAppearance(VisualGuest guest, GuestKind kind, int index, SpecialGuestKind replicaKind = SpecialGuestKind.None)
        {
            var root = guest.Root;
            guest.Body = Pivot("Body", root, Vector3.zero);
            string outfit = kind == GuestKind.Budget ? "Moss coat" : kind == GuestKind.ColdSensitive ? "Claret coat" : "Blue suit";
            var special = guest.Stay?.Application.SpecialKind ?? replicaKind;
            if (special == SpecialGuestKind.TouringMusician) outfit = "Ink";
            else if (special == SpecialGuestKind.Overpacker) outfit = "Ochre";
            else if (special == SpecialGuestKind.NightOwl) outfit = "Night violet";
            string skin = "Skin " + (index % 3);
            Shape("Jacket", PrimitiveType.Capsule, guest.Body, new Vector3(0, 1.02f, 0), new Vector3(.64f, .48f, .43f), outfit);
            Shape("Shirt", PrimitiveType.Cube, guest.Body, new Vector3(0, 1.18f, .219f), new Vector3(.18f, .45f, .035f), "Ivory");
            Shape("Head", PrimitiveType.Sphere, guest.Body, new Vector3(0, 1.68f, .01f), new Vector3(.61f, .68f, .56f), skin);
            Shape("Hair", PrimitiveType.Sphere, guest.Body, new Vector3(0, 1.94f, -.035f), new Vector3(.63f, .28f, .57f), index % 2 == 0 ? "Brown hair" : "Dark hair");
            foreach (int sign in new[] { -1, 1 })
            {
                Shape("Eye", PrimitiveType.Sphere, guest.Body, new Vector3(sign * .143f, 1.72f, .268f), new Vector3(.16f, .19f, .093f), "Ivory");
                Shape("Pupil", PrimitiveType.Sphere, guest.Body, new Vector3(sign * .143f, 1.72f, .315f), new Vector3(.055f, .08f, .03f), "Ink");
                Shape("Ear", PrimitiveType.Sphere, guest.Body, new Vector3(sign * .29f, 1.66f, .025f), new Vector3(.14f, .21f, .15f), skin);
            }
            Shape("Nose", PrimitiveType.Sphere, guest.Body, new Vector3(0, 1.64f, .30f), new Vector3(.13f, .14f, .14f), skin);
            Shape("Mouth", PrimitiveType.Sphere, guest.Body, new Vector3(0, 1.52f, .255f), new Vector3(.14f, .033f, .025f), "Brown hair");
            guest.LeftLeg = MakeLeg(guest.Body, -.17f);
            guest.RightLeg = MakeLeg(guest.Body, .17f);
            guest.LeftArm = MakeArm(guest.Body, -.37f, outfit, skin);
            guest.RightArm = MakeArm(guest.Body, .37f, outfit, skin);
            guest.Phone = Pivot("Guest phone", guest.RightArm, new Vector3(0, -.53f, .025f));
            Shape("Phone handset", PrimitiveType.Cube, guest.Phone, Vector3.zero, new Vector3(.13f, .22f, .055f), "Ink");
            Shape("Phone screen", PrimitiveType.Cube, guest.Phone, new Vector3(0, 0, .03f), new Vector3(.095f, .16f, .012f), "Ivory");
            guest.Phone.gameObject.SetActive(false);
            if (special == SpecialGuestKind.TouringMusician)
            {
                Shape("Musician scarf", PrimitiveType.Cube, guest.Body, new Vector3(.12f, 1.23f, .255f), new Vector3(.13f, .53f, .065f), "Claret coat");
                Shape("Musician hat brim", PrimitiveType.Sphere, guest.Body, new Vector3(0, 1.99f, 0), new Vector3(.79f, .055f, .67f), "Ink");
                Shape("Musician hat crown", PrimitiveType.Capsule, guest.Body, new Vector3(0, 2.05f, -.025f), new Vector3(.55f, .12f, .49f), "Ink");
            }
            else if (special == SpecialGuestKind.Overpacker)
            {
                Shape("Travel vest", PrimitiveType.Cube, guest.Body, new Vector3(0, 1.05f, .225f), new Vector3(.53f, .51f, .09f), "Moss coat");
                foreach (int side in new[] { -1, 1 })
                    Shape("Travel vest pocket", PrimitiveType.Cube, guest.Body, new Vector3(side * .17f, .93f, .285f), new Vector3(.17f, .19f, .075f), "Ochre");
                Shape("Travel hat", PrimitiveType.Sphere, guest.Body, new Vector3(0, 2.0f, .0f), new Vector3(.74f, .22f, .65f), "Ivory");
            }
            else if (special == SpecialGuestKind.NightOwl)
            {
                foreach (int side in new[] { -1, 1 })
                {
                    Shape("Tired eye", PrimitiveType.Sphere, guest.Body, new Vector3(side * .14f, 1.615f, .279f), new Vector3(.15f, .035f, .026f), "Night violet");
                    Shape("Headphones", PrimitiveType.Sphere, guest.Body, new Vector3(side * .26f, 1.4f, .07f), new Vector3(.17f, .21f, .15f), "Ink");
                }
            }
            else if (kind == GuestKind.Budget)
            {
                Shape("Travel backpack", PrimitiveType.Capsule, guest.Body, new Vector3(0, 1.03f, -.29f), new Vector3(.47f, .29f, .24f), "Ochre");
                Shape("Backpack clasp", PrimitiveType.Cube, guest.Body, new Vector3(0, .97f, -.42f), new Vector3(.11f, .16f, .035f), "Ivory");
                Shape("Cap crown", PrimitiveType.Sphere, guest.Body, new Vector3(0, 1.98f, .01f), new Vector3(.65f, .24f, .58f), "Ochre");
                Shape("Cap peak", PrimitiveType.Sphere, guest.Body, new Vector3(0, 1.93f, .28f), new Vector3(.50f, .065f, .32f), "Ochre");
            }
            else if (kind == GuestKind.ColdSensitive)
            {
                Shape("Thick scarf", PrimitiveType.Capsule, guest.Body, new Vector3(0, 1.42f, 0), new Vector3(.51f, .13f, .46f), "Ochre");
                Shape("Scarf tail", PrimitiveType.Cube, guest.Body, new Vector3(.13f, 1.16f, .27f), new Vector3(.16f, .49f, .10f), "Ochre");
                Shape("Winter hat", PrimitiveType.Sphere, guest.Body, new Vector3(0, 1.99f, -.015f), new Vector3(.65f, .28f, .59f), "Claret coat");
                Shape("Hat pompom", PrimitiveType.Sphere, guest.Body, new Vector3(0, 2.15f, -.015f), Vector3.one * .18f, "Ivory");
            }
            else
            {
                Shape("Tie", PrimitiveType.Cube, guest.Body, new Vector3(0, 1.16f, .251f), new Vector3(.072f, .32f, .033f), "Claret coat");
                Shape("Tie knot", PrimitiveType.Sphere, guest.Body, new Vector3(0, 1.35f, .26f), Vector3.one * .09f, "Claret coat");
                Shape("Briefcase", PrimitiveType.Cube, guest.LeftArm, new Vector3(0, -.76f, 0), new Vector3(.17f, .36f, .48f), "Brown hair");
            }
        }

        Transform MakeLeg(Transform parent, float x)
        {
            var leg = Pivot("Leg", parent, new Vector3(x, .69f, 0));
            Shape("Trouser", PrimitiveType.Capsule, leg, new Vector3(0, -.26f, 0), new Vector3(.23f, .28f, .25f), "Blue suit");
            Shape("Shoe", PrimitiveType.Sphere, leg, new Vector3(0, -.57f, .09f), new Vector3(.27f, .20f, .42f), "Ink");
            return leg;
        }

        Transform MakeArm(Transform parent, float x, string outfit, string skin)
        {
            var arm = Pivot("Arm", parent, new Vector3(x, 1.29f, 0));
            Shape("Sleeve", PrimitiveType.Capsule, arm, new Vector3(0, -.21f, 0), new Vector3(.20f, .24f, .20f), outfit);
            Shape("Hand", PrimitiveType.Sphere, arm, new Vector3(0, -.48f, .02f), new Vector3(.22f, .25f, .20f), skin);
            return arm;
        }

        static Transform Pivot(string name, Transform parent, Vector3 position)
        {
            var obj = new GameObject(name).transform; obj.SetParent(parent, false); obj.localPosition = position; return obj;
        }

        void Shape(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, string materialName)
        {
            var obj = GameObject.CreatePrimitive(type);
            obj.name = name; obj.transform.SetParent(parent, false); obj.transform.localPosition = position; obj.transform.localScale = scale;
            var collider = obj.GetComponent<Collider>(); collider.enabled = false; Destroy(collider);
            obj.GetComponent<Renderer>().sharedMaterial = MaterialFor(materialName);
        }

        Material MaterialFor(string key)
        {
            if (materials.TryGetValue(key, out var result)) return result;
            Color color;
            switch (key)
            {
                case "Skin 0": color = new Color(.78f, .48f, .29f); break;
                case "Skin 1": color = new Color(.96f, .73f, .51f); break;
                case "Skin 2": color = new Color(.40f, .23f, .14f); break;
                case "Night violet": color = new Color(.29f, .19f, .39f); break;
                case "Moss coat": color = new Color(.24f, .41f, .28f); break;
                case "Claret coat": color = new Color(.43f, .09f, .14f); break;
                case "Blue suit": color = new Color(.08f, .18f, .27f); break;
                case "Ochre": color = new Color(.82f, .53f, .17f); break;
                case "Ivory": color = new Color(.97f, .92f, .79f); break;
                case "Brown hair": color = new Color(.23f, .11f, .06f); break;
                default: color = new Color(.025f, .035f, .038f); break;
            }
            result = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Guest " + key };
            result.SetColor("_BaseColor", color); result.SetFloat("_Smoothness", .12f);
            materials.Add(key, result);
            return result;
        }

        void ClearGuests(bool acknowledgeRemoval = false)
        {
            foreach (var guest in guests.Values)
                if (guest.Root != null)
                {
                    guest.Root.gameObject.SetActive(false);
                    if (acknowledgeRemoval) ReportVacatedRooms(guest, true);
                    Destroy(guest.Root.gameObject);
                }
            guests.Clear();
            receptionRequests.Clear();
        }
    }
}

