using UnityEngine;

namespace WorstHotel
{
    public readonly struct GuestVisualDebugSnapshot
    {
        public readonly string CurrentState, CurrentActivity, Destination, PrivacyState, PathStatus, NextScheduledActivity;
        public readonly int AssignedRoom;
        public readonly bool AtActivityAnchor, BodyVisible;

        public GuestVisualDebugSnapshot(string state, string activity, string destination, int room,
            string privacy, string path, string next, bool atAnchor, bool visible)
        {
            CurrentState = state; CurrentActivity = activity; Destination = destination; AssignedRoom = room;
            PrivacyState = privacy; PathStatus = path; NextScheduledActivity = next;
            AtActivityAnchor = atAnchor; BodyVisible = visible;
        }
    }

    public sealed partial class GuestPresentation
    {
        public bool TryGetGuestDebugSnapshot(string id, out GuestVisualDebugSnapshot snapshot)
        {
            if (!guests.TryGetValue(id, out var guest)) { snapshot = default; return false; }
            var agent = guest.Stay.Agent;
            string destination = guest.Purpose.ToString();
            if (guest.Purpose == RoutePurpose.Activity)
                destination = agent.State == GuestAgentState.Sleeping ? "BedAnchor" :
                    agent.Activity == GuestActivity.Shower ? "ShowerAnchor" :
                    agent.Activity == GuestActivity.Work ? "DeskAnchor" :
                    agent.Activity == GuestActivity.AdjustRadiator ? "RadiatorAnchor" :
                    agent.Activity == GuestActivity.CallReception ? "RoomPhoneAnchor" :
                    agent.Activity == GuestActivity.PhoneCall ? "PhoneAnchor" :
                    agent.Activity == GuestActivity.Unpack || agent.Activity == GuestActivity.Pack ? "UnpackAnchor" :
                    agent.Activity == GuestActivity.LoudRoom || agent.Activity == GuestActivity.WatchTV ? "Radio / RestAnchor" : "RestAnchor";
            if (guest.Route != null && guest.Waypoint < guest.Route.Points.Count)
                destination += " " + guest.Route.Points[guest.Waypoint].ToString("F1");
            string next = agent.State == GuestAgentState.Sleeping ?
                (agent.TemporarySleep ? "QuietRest (resume schedule)" : "Checkout") : agent.NextPlannedActivity.ToString();
            snapshot = new GuestVisualDebugSnapshot(agent.State.ToString(),
                agent.State == GuestAgentState.Sleeping ? "Sleep" : agent.Activity.ToString(), destination,
                guest.Stay.RoomId, guest.ModelRoom != null ? guest.ModelRoom.PrivacyState.ToString() : "Public",
                guest.PathStatus + (guest.RecoveryCount > 0 ? " / recoveries " + guest.RecoveryCount : ""),
                next, guest.InsideRoom && guest.RouteComplete && agent.ActivityStaged, guest.Body.gameObject.activeSelf);
            return true;
        }

        void UpdateStaging(VisualGuest guest, bool atAnchor)
        {
            var agent = guest.Stay.Agent;
            if (atAnchor && guest.Purpose == RoutePurpose.Activity)
            {
                guest.SettlingTime += Time.deltaTime;
                if (guest.State == GuestAgentState.Sleeping)
                    guest.PoseBlend = Mathf.MoveTowards(guest.PoseBlend, 1, Time.deltaTime / .95f);
                float delay = guest.State == GuestAgentState.Sleeping ? .95f : guest.Activity == GuestActivity.Shower ? .65f : .3f;
                bool responseActivity = !string.IsNullOrEmpty(guest.ResponseActionId) &&
                    (guest.Activity == GuestActivity.AdjustRadiator || guest.Activity == GuestActivity.CallReception);
                if (responseActivity && guest.SettlingTime >= .65f)
                    ReportResponseArrival(guest, guest.Activity == GuestActivity.AdjustRadiator ? GuestResponseAnchor.Radiator : GuestResponseAnchor.RoomPhone);
                else if (!responseActivity && !agent.ActivityStaged && guest.SettlingTime >= delay)
                {
                    var result = simulation.SignalGuestActivityReady(guest.Id, guest.State, guest.Activity);
                    if (result.Success) session.RaiseChanged();
                }
                guest.PathStatus = agent.ActivityStaged ? "Activity at anchor" : "Settling at anchor";
            }
            bool hidden = agent.State == GuestAgentState.GuestAway ||
                atAnchor && agent.ActivityStaged && guest.Activity == GuestActivity.Shower;
            if (guest.Body.gameObject.activeSelf == hidden) guest.Body.gameObject.SetActive(!hidden);
            // The normal upright check-in raycast capsule must not remain as an invisible person
            // beside the shower or bed. Room conversations go through the occupied door.
            var capsule = guest.Root.GetComponent<CapsuleCollider>();
            var interaction = guest.Root.GetComponent<GuestReceptionInteraction>();
            if (interaction != null) interaction.PresentationTargetVisible = !hidden && guest.PoseBlend <= .05f;
            if (capsule != null && (hidden || guest.PoseBlend > .05f)) capsule.enabled = false;
        }

        static void ClosePassedDoor(VisualGuest guest)
        {
            var route = guest.Route;
            if (route == null || route.Door == null || route.DoorCrossing < 0 || guest.DoorClosedAfterCrossing ||
                guest.Waypoint <= route.DoorCrossing) return;
            route.Door.CloseAfterGuestPassage(guest.Id);
            guest.DoorClosedAfterCrossing = true;
        }

        static bool SegmentClear(VisualGuest guest, Vector3 from, Vector3 to)
        {
            var direction = to - from;
            float distance = direction.magnitude;
            if (distance < .001f) return true;
            var hits = Physics.CapsuleCastAll(from + Vector3.up * .45f, from + Vector3.up * 1.60f,
                .23f, direction / distance, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            foreach (var hit in hits)
            {
                var shape = hit.collider;
                if (!shape || shape.transform.IsChildOf(guest.Root) || shape.attachedRigidbody != null ||
                    shape.GetComponentInParent<GuestReceptionInteraction>() != null ||
                    shape.GetComponentInParent<FirstPersonController>() != null ||
                    shape.GetComponentInParent<DoorInteractable>() != null) continue;
                return false;
            }
            return true;
        }

        static bool BlockedByEnvironment(VisualGuest guest, Vector3 target, float step) =>
            !SegmentClear(guest, guest.Root.position, Vector3.MoveTowards(guest.Root.position, target, step + .025f));

        void RecoverRoute(VisualGuest guest, float delta)
        {
            guest.BlockedSeconds += delta;
            guest.PathStatus = "Blocked / recalculating authored route";
            if (guest.BlockedSeconds < 2.5f || guest.Route == null || guest.Waypoint >= guest.Route.Points.Count) return;
            guest.BlockedSeconds = 0;
            guest.RecoveryCount++;
            var origin = guest.Root.position;
            var target = guest.Route.Points[guest.Waypoint];
            // Local detours are accepted only when both swept capsule segments are clear. Never
            // teleport across a wall or bypass a closed door; the original gated waypoint remains.
            foreach (float radius in new[] { .65f, 1.0f, 1.35f })
                for (int direction = 0; direction < 8; direction++)
                {
                    float angle = direction * Mathf.PI / 4;
                    var candidate = origin + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
                    if (guest.InsideRoom && !AuthoredGuestRoute.IsOnRoomSide(candidate, guest.Room)) continue;
                    if (!guest.InsideRoom && AuthoredGuestRoute.IsOnRoomSide(candidate, guest.Room)) continue;
                    if (!SegmentClear(guest, origin, candidate) || !SegmentClear(guest, candidate, target)) continue;
                    guest.Route.Points.Insert(guest.Waypoint, candidate);
                    if (guest.Route.DoorCrossing >= guest.Waypoint) guest.Route.DoorCrossing++;
                    guest.PathStatus = "Local clear detour";
                    return;
                }
            if (guest.RecoveryCount % 3 != 0) return;
            // A stale corner/waypoint must not keep an NPC walking into the same obstruction
            // forever. After three failed local searches reset at most one metre within the
            // same physical space, then rebuild the route from the actual position. This does
            // not acknowledge an arrival, clear ownership, or cross a locked doorway.
            for (int direction = 0; direction < 8; direction++)
            {
                float angle = direction * Mathf.PI / 4;
                var candidate = origin + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * .8f;
                if (AuthoredGuestRoute.IsOnRoomSide(candidate, guest.Room) != guest.InsideRoom ||
                    !SegmentClear(guest, origin, candidate) || !StandingSpaceClear(guest, candidate)) continue;
                guest.Root.position = candidate;
                break;
            }
            var purpose = guest.Purpose;
            AuthoredGuestRoute rebuilt;
            switch (purpose)
            {
                case RoutePurpose.Reception:
                    rebuilt = AuthoredGuestRoute.Arrival(receptionPlaces[guest.AppearanceIndex % receptionPlaces.Length].position);
                    break;
                case RoutePurpose.Activity:
                    rebuilt = AuthoredGuestRoute.Activity(guest.Root.position, guest.Room, guest.Activity, guest.State == GuestAgentState.Sleeping);
                    break;
                case RoutePurpose.Exit:
                    rebuilt = AuthoredGuestRoute.Exit(guest.Root.position, guest.Room, arrivalSpawn.position, guest.InsideRoom);
                    break;
                case RoutePurpose.Away:
                    rebuilt = AuthoredGuestRoute.GuestAway(guest.Root.position, guest.Room, arrivalSpawn.position);
                    break;
                case RoutePurpose.ServiceReception:
                    rebuilt = AuthoredGuestRoute.ToServiceReception(guest.Root.position, guest.Room,
                        receptionPlaces[guest.AppearanceIndex % receptionPlaces.Length].position, guest.InsideRoom);
                    break;
                case RoutePurpose.Transfer:
                    rebuilt = AuthoredGuestRoute.LeaveRoom(guest.Root.position, guest.Room);
                    break;
                default:
                    rebuilt = AuthoredGuestRoute.ToRoom(guest.Root.position, guest.Room, guest.InsideRoom);
                    break;
            }
            SetRoute(guest, rebuilt, purpose);
            guest.PathStatus = "Recovered nearby / rebuilt route";
        }

        static bool StandingSpaceClear(VisualGuest guest, Vector3 position)
        {
            foreach (var shape in Physics.OverlapCapsule(position + Vector3.up * .45f,
                position + Vector3.up * 1.60f, .23f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (shape && !shape.transform.IsChildOf(guest.Root) && !shape.attachedRigidbody &&
                    !shape.GetComponentInParent<GuestReceptionInteraction>() &&
                    !shape.GetComponentInParent<FirstPersonController>() && !shape.GetComponentInParent<DoorInteractable>()) return false;
            return true;
        }
    }
}
