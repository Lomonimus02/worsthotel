using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WorstHotel
{
    public sealed partial class GuestPresentation
    {
        sealed class VisitorView
        {
            public VisualGuest Visual;
            public HotelVisitorState State = (HotelVisitorState)(-1);
            public bool Introduced;
        }
        readonly Dictionary<string, VisitorView> visitorViews = new Dictionary<string, VisitorView>();

        void UpdateVisitors(float delta)
        {
            var director = simulation.Director;
            foreach (var id in visitorViews.Keys.ToArray())
                if (director?.FindVisitor(id) == null || director.FindVisitor(id).State == HotelVisitorState.Left)
                { Destroy(visitorViews[id].Visual.Root.gameObject); visitorViews.Remove(id); }
            if (director == null) return;
            foreach (var visitor in director.Visitors)
            {
                if (visitor.State == HotelVisitorState.Left || !rooms.TryGetValue(visitor.RoomId, out var room)) continue;
                if (!visitorViews.TryGetValue(visitor.Id, out var view))
                {
                    int slot = FreeReceptionSlot(); if (slot < 0) continue;
                    var root = new GameObject("Visitor for room " + visitor.RoomId).transform;
                    root.SetParent(visualRoot, false); root.position = arrivalSpawn.position; root.position = new Vector3(root.position.x, .01f, root.position.z);
                    var visual = new VisualGuest { Id = visitor.Id, Root = root, Room = room, ReceptionSlot = slot,
                        AppearanceIndex = StableAppearanceIndex(visitor.Id), State = GuestAgentState.InRoom };
                    BuildAppearance(visual, GuestKind.Budget, visual.AppearanceIndex);
                    if (visual.Phone) visual.Phone.gameObject.SetActive(false);
                    var hit = root.gameObject.AddComponent<CapsuleCollider>(); hit.radius = .3f; hit.height = 2.05f; hit.center = Vector3.up * 1.025f;
                    root.gameObject.AddComponent<VisitorInteraction>().VisitorId = visitor.Id;
                    root.gameObject.AddComponent<GuestPhysicalReaction>().Initialize(visual.Body);
                    view = new VisitorView { Visual = visual }; visitorViews.Add(visitor.Id, view);
                }
                var v = view.Visual;
                if (view.State != visitor.State)
                {
                    view.State = visitor.State;
                    AuthoredGuestRoute route = null;
                    if (visitor.State == HotelVisitorState.Arriving) route = AuthoredGuestRoute.Arrival(receptionPlaces[v.ReceptionSlot].position);
                    if (visitor.State == HotelVisitorState.GoingToRoom) route = AuthoredGuestRoute.ToRoom(v.Root.position, room, false);
                    if (visitor.State == HotelVisitorState.Leaving) route = AuthoredGuestRoute.Exit(v.Root.position, room, arrivalSpawn.position,
                        AuthoredGuestRoute.IsOnRoomSide(v.Root.position, room));
                    if (route != null) SetRoute(v, route, RoutePurpose.Room);
                    else { v.Route = null; v.RouteComplete = true; }
                }
                bool moving = false;
                if (!v.Root.GetComponent<GuestPhysicalReaction>().AnimateRecovery(Time.deltaTime) && v.Route != null && delta > 0)
                {
                    float remaining = walkingSpeed * delta;
                    while (remaining > 0 && v.Waypoint < v.Route.Points.Count)
                    {
                        if (v.Waypoint == v.Route.DoorCrossing)
                        { v.Route.Door.RequestVisitorOpen(visitor.Id); if (!v.Route.Door.IsPassageOpen) break; }
                        var target = v.Route.Points[v.Waypoint]; var offset = target - v.Root.position;
                        if (offset.magnitude < .02f) { v.Waypoint++; continue; }
                        float step = Mathf.Min(.12f, remaining, offset.magnitude);
                        if (!GuestPhysicalReaction.TryWalk(v.Root, Vector3.MoveTowards(v.Root.position, target, step), out var next)) break;
                        v.Root.rotation = Quaternion.RotateTowards(v.Root.rotation, Quaternion.LookRotation(offset), delta * 280);
                        v.Root.position = next; remaining -= step; moving = true;
                        if (v.Route.Door && v.Route.DoorCrossing >= 0 && v.Waypoint > v.Route.DoorCrossing + 1 && !v.DoorClosedAfterCrossing)
                        { v.Route.Door.CloseAfterGuestPassage(visitor.HostGuestId); v.DoorClosedAfterCrossing = true; }
                    }
                    if (v.Waypoint >= v.Route.Points.Count)
                    { v.Route = null; director.SignalVisitorReached(visitor.Id, visitor.State); session.RaiseChanged(); }
                }
                if (visitor.State != HotelVisitorState.Arriving && visitor.State != HotelVisitorState.WaitingAtReception && v.Root.position.z > 5) v.ReceptionSlot = -1;
                v.AnimationTime += delta * (moving ? 7.8f : 1.8f);
                float swing = moving ? Mathf.Sin(v.AnimationTime) : 0;
                v.LeftLeg.localRotation = Quaternion.Euler(swing * 25, 0, 0);
                v.RightLeg.localRotation = Quaternion.Euler(-swing * 25, 0, 0);
                v.LeftArm.localRotation = Quaternion.Euler(-swing * 18, 0, -5);
                v.RightArm.localRotation = Quaternion.Euler(swing * 18, 0, 5);
                if (!view.Introduced && visitor.State == HotelVisitorState.WaitingAtReception)
                {
                    var coop = LocalCoopBootstrap.Instance;
                    if (coop) foreach (var player in coop.Players)
                        if (player && Vector3.Distance(player.transform.position, v.Root.position) < 8)
                        { HotelSubtitle.Say(player.ActorId, "Visitor", "Hello. I'm here to visit my friend in room " + visitor.RoomId + ". Is that all right?"); view.Introduced = true; }
                }
            }
        }
        void CaptureLanVisitors(List<LanWorldGuest> result)
        {
            foreach (var pair in visitorViews)
            {
                var v = pair.Value.Visual;
                result.Add(new LanWorldGuest { id = pair.Key, name = v.Root.name, kind = GuestKind.Budget, appearanceIndex = v.AppearanceIndex,
                    position = v.Root.position, rotation = v.Root.rotation, bodyOffset = v.Body.localPosition, bodyRotation = v.Body.localRotation,
                    bodyVisible = true, leftArm = v.LeftArm.localRotation, rightArm = v.RightArm.localRotation,
                    leftLeg = v.LeftLeg.localRotation, rightLeg = v.RightLeg.localRotation });
            }
        }
        void ClearVisitors()
        { foreach (var view in visitorViews.Values) if (view.Visual.Root) Destroy(view.Visual.Root.gameObject); visitorViews.Clear(); }
    }
}
