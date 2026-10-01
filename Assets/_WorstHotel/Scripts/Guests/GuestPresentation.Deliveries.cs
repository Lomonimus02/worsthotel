using System.Linq;
using UnityEngine;

namespace WorstHotel
{
    public sealed partial class GuestPresentation
    {
        RoomBlanketDropOffInteraction[] blanketPoints;

        void AcknowledgeDeliveredBags(VisualGuest guest)
        {
            var agent = guest.Stay.Agent;
            // The actual suitcases remain in the room. A guest notices staff-delivered
            // luggage on settling/return, not while outside or asleep. No repeated bonus.
            if (simulation.Services == null || !guest.InsideRoom || !guest.RouteComplete || !agent.IsRoomState || !agent.ActivityStaged ||
                agent.State == GuestAgentState.Sleeping || agent.Activity == GuestActivity.Shower || agent.Activity == GuestActivity.PhoneCall || agent.ResponseActionId != null) return;
            int count = simulation.Services.Items.Count(i => i.Kind == ServiceItemKind.Luggage && i.GuestId == guest.Id &&
                i.StaffHandling && i.Location == ServiceItemLocation.Delivered);
            if (count <= guest.AcknowledgedBags) return;
            var coop = LocalCoopBootstrap.Instance; bool heard = false;
            if (coop) foreach (var player in coop.Players)
                if (player && Vector3.Distance(player.transform.position, guest.Root.position) < 7)
                {
                    HotelSubtitle.Say(player.ActorId, guest.Stay.Name + " · Room " + guest.Stay.RoomId,
                        "My luggage is here. Thank you for bringing it up.");
                    heard = true;
                }
            if (heard) guest.AcknowledgedBags = count;
        }

        bool SynchronizeBlanketRoute(VisualGuest guest)
        {
            var agent = guest.Stay.Agent;
            if (guest.BlanketIntentId != null)
            {
                if (!agent.IsRoomState || agent.ResponseActionId != null)
                { guest.BlanketIntentId = null; guest.BlanketReturning = false; return false; }
                var ongoing = simulation.Services.FindIntent(guest.BlanketIntentId);
                if (!guest.BlanketReturning && (ongoing == null || !ongoing.Collecting)) BeginBlanketReturn(guest);
                return true;
            }
            var intent = simulation.Services?.DropOffIntent(guest.Id);
            if (intent?.Status != ServiceIntentStatus.AwaitingReceipt || !agent.IsRoomState || !agent.ActivityStaged) return false;
            blanketPoints ??= FindObjectsByType<RoomBlanketDropOffInteraction>(FindObjectsSortMode.None);
            var point = blanketPoints.FirstOrDefault(p => p && p.roomId == guest.Stay.RoomId);
            if (!point || !point.deliveryAnchor || !simulation.Services.BeginBlanketCollection(intent.Id, intent.Revision).Success) return false;
            guest.BlanketIntentId = intent.Id; guest.BlanketIntentRevision = intent.Revision;
            guest.BlanketPoint = point.deliveryAnchor; guest.BlanketReachTime = 0; guest.BlanketReturning = false;
            guest.State = agent.State; guest.Activity = agent.Activity;
            var route = AuthoredGuestRoute.LeaveRoom(guest.Root.position, guest.Room);
            route.Add(Mathf.Sign(point.transform.position.x) * 1.03f, point.transform.position.z);
            SetRoute(guest, route, RoutePurpose.BlanketPickup);
            session.RaiseChanged();
            return true;
        }

        void UpdateBlanketCollection(VisualGuest guest, float delta)
        {
            if (guest.Purpose != RoutePurpose.BlanketPickup || !guest.RouteComplete || guest.BlanketIntentId == null) return;
            guest.BlanketReachTime += delta;
            if (guest.BlanketReachTime < .8f) return;
            // Only this completed, door-gated physical route may acknowledge the real parcel.
            var result = simulation.Services.ReceivePhysicalBlanket(guest.BlanketIntentId, guest.BlanketIntentRevision);
            if (result.Success)
            {
                var coop = LocalCoopBootstrap.Instance;
                if (coop) foreach (var player in coop.Players)
                    if (player && Vector3.Distance(player.transform.position, guest.Root.position) < 7)
                        HotelSubtitle.Say(player.ActorId, guest.Stay.Name + " · Room " + guest.Stay.RoomId,
                            "Thank you. That's the extra blanket I needed.");
            }
            BeginBlanketReturn(guest); session.RaiseChanged();
        }

        static void BeginBlanketReturn(VisualGuest guest)
        {
            guest.BlanketReturning = true;
            SetRoute(guest, AuthoredGuestRoute.ToRoom(guest.Root.position, guest.Room, guest.InsideRoom), RoutePurpose.BlanketReturn);
        }
    }
}
