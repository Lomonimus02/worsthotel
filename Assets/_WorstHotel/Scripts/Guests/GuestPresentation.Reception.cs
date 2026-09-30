using System.Collections.Generic;
using UnityEngine;

namespace WorstHotel
{
    public sealed partial class GuestPresentation
    {
        readonly List<string> receptionRequests = new List<string>();

        int AcquireReceptionSlot(string guestId)
        {
            if (!receptionRequests.Contains(guestId)) receptionRequests.Add(guestId);
            if (receptionRequests[0] != guestId) return -1;
            int slot = FreeReceptionSlot();
            if (slot >= 0) receptionRequests.RemoveAt(0);
            return slot;
        }

        void PruneReceptionRequests()
        {
            if (simulation == null) { receptionRequests.Clear(); return; }
            receptionRequests.RemoveAll(id =>
            {
                foreach (var stay in simulation.Guests)
                    if (stay.GuestId == id) return !NeedsReceptionSlot(stay.Agent);
                return true;
            });
            // Retry outside arrivals even if no model state changed since a berth was cleared.
            if (receptionRequests.Count > 0) refreshPending = true;
        }

        bool HasReceptionSlot(VisualGuest guest) => receptionPlaces != null && guest.ReceptionSlot >= 0 &&
            guest.ReceptionSlot < receptionPlaces.Length && receptionPlaces[guest.ReceptionSlot];

        void ReleaseClearedReceptionSlot(VisualGuest guest)
        {
            if (!HasReceptionSlot(guest) || NeedsReceptionSlot(guest.Stay.Agent)) return;
            // Keep the berth through the departure turn, so a new arrival cannot walk into
            // the checked-in guest. Room occupants, sleeping/away guests own no queue space.
            Vector3 position = guest.Root.position;
            foreach (var place in receptionPlaces)
                if (place && Mathf.Abs(position.x - place.position.x) < .9f &&
                    position.z > place.position.z - 1.15f && position.z < place.position.z + .8f) return;
            guest.ReceptionSlot = -1;
            refreshPending = true;
        }

        void RecoverTraffic(VisualGuest guest, float delta)
        {
            guest.TrafficSeconds += delta;
            guest.PathStatus = "Yielding to a person / cart";
            // Stagger retries to avoid two opposing bodies choosing the same detour frame.
            if (guest.TrafficSeconds < 1.5f + guest.AppearanceIndex % 4 * .25f || guest.Route == null ||
                guest.Waypoint >= guest.Route.Points.Count) return;
            guest.TrafficSeconds = 0;
            if (guest.Detouring)
            {
                // A person can move into an accepted detour. Drop only that temporary
                // point, retain the original door gate and retry rather than growing a path.
                guest.Route.Points.RemoveAt(guest.Waypoint);
                if (guest.Route.DoorCrossing > guest.Waypoint) guest.Route.DoorCrossing--;
                guest.Detouring = false;
                return;
            }
            Vector3 origin = guest.Root.position, target = guest.Route.Points[guest.Waypoint];
            foreach (float radius in new[] { .8f, 1.2f })
                for (int direction = 0; direction < 8; direction++)
                {
                    float angle = (direction + guest.AppearanceIndex % 8) * Mathf.PI / 4;
                    var candidate = origin + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
                    // Public detours stay in the public corridor, never in an unrelated room.
                    if (!guest.InsideRoom && candidate.z >= 6 && Mathf.Abs(candidate.x) > 1.3f) continue;
                    if (AuthoredGuestRoute.IsOnRoomSide(candidate, guest.Room) != guest.InsideRoom) continue;
                    if (!GuestPhysicalReaction.SegmentClear(guest.Root, origin, candidate) ||
                        !GuestPhysicalReaction.SegmentClear(guest.Root, candidate, target)) continue;
                    guest.Route.Points.Insert(guest.Waypoint, candidate);
                    if (guest.Route.DoorCrossing >= guest.Waypoint) guest.Route.DoorCrossing++;
                    guest.Detouring = true;
                    guest.PathStatus = "Walking around local traffic";
                    return;
                }
        }
    }
}
