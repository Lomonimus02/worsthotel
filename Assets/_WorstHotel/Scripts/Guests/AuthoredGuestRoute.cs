using System.Collections.Generic;
using UnityEngine;

namespace WorstHotel
{
    [System.Serializable]
    public sealed class GuestRoomMarkers
    {
        public int roomId;
        public DoorInteractable door;
        public Transform roomTarget, rest, shower, loud;
        public Transform bedAnchor, bedApproach, deskAnchor, unpackAnchor, phoneAnchor, doorInsideAnchor, doorOutsideAnchor;
        public Transform radiatorAnchor, radiatorTarget, roomPhoneAnchor, roomPhoneTarget;
        public GameObject showerWater, showerCurtain, loudIndicator;
        [System.NonSerialized] public bool showerActive, loudActive;
    }

    /// <summary>Small route description for the existing, authored single-floor hotel.</summary>
    public sealed class AuthoredGuestRoute
    {
        public readonly List<Vector3> Points = new List<Vector3>(16);
        public DoorInteractable Door;
        public int DoorCrossing = -1;
        const float Feet = .01f;

        public void Add(float x, float z) => Points.Add(new Vector3(x, Feet, z));
        public void Add(Vector3 point) => Add(point.x, point.z);

        public static bool IsOnRoomSide(Vector3 position, GuestRoomMarkers room)
        {
            var doorway = room.door.transform.position;
            // The half-plane alone would misclassify a reception queue position as a guest room.
            return Mathf.Abs(position.z - doorway.z) < 3.5f &&
                position.x * Mathf.Sign(doorway.x) > Mathf.Abs(doorway.x);
        }

        public static AuthoredGuestRoute Arrival(Vector3 reception)
        {
            var route = new AuthoredGuestRoute();
            route.Add(0, .35f); route.Add(reception.x, .35f); route.Add(reception);
            return route;
        }

        public static AuthoredGuestRoute ToRoom(Vector3 current, GuestRoomMarkers room, bool fromRoom)
        {
            var route = new AuthoredGuestRoute { Door = room.door };
            float side = Mathf.Sign(room.door.transform.position.x), z = room.door.transform.position.z;
            if (fromRoom) ReturnToInnerLane(route, current, side, z);
            else
            {
                // Stay in front of the reception counter; the diagonal shortcut intersects luggage.
                if (current.z < 6)
                {
                    route.Add(current.x, .35f); route.Add(side * .32f, .35f); route.Add(side * .32f, 6.6f);
                }
                else route.Add(side * .32f, current.z);
                route.Add(side * .32f, z); route.Add(Outside(room));
                route.DoorCrossing = route.Points.Count;
                route.Add(Inside(room));
            }
            route.Add(room.roomTarget.position.x, z); route.Add(room.roomTarget.position);
            return route;
        }

        public static AuthoredGuestRoute Activity(Vector3 current, GuestRoomMarkers room, GuestActivity activity, bool sleeping = false)
        {
            var route = new AuthoredGuestRoute();
            float side = Mathf.Sign(room.door.transform.position.x), z = room.door.transform.position.z;
            ReturnToInnerLane(route, current, side, z);
            var target = sleeping ? room.bedApproach : activity == GuestActivity.Shower ? room.shower :
                activity == GuestActivity.AdjustRadiator ? room.radiatorAnchor : activity == GuestActivity.CallReception ? room.roomPhoneAnchor :
                activity == GuestActivity.Work ? room.deskAnchor : activity == GuestActivity.Unpack || activity == GuestActivity.Pack ? room.unpackAnchor :
                activity == GuestActivity.PhoneCall ? room.phoneAnchor :
                activity == GuestActivity.LoudRoom || activity == GuestActivity.WatchTV ? room.loud : room.rest;
            if (target == null) { route.Add(room.roomTarget.position); return route; }
            float traverseZ = activity == GuestActivity.Shower || activity == GuestActivity.Work ? z + 2.60f :
                activity == GuestActivity.AdjustRadiator ? z - .9f : z - 1.6f;
            route.Add(side * 4.18f, traverseZ); route.Add(target.position.x, traverseZ); route.Add(target.position);
            return route;
        }

        public static AuthoredGuestRoute Exit(Vector3 current, GuestRoomMarkers room, Vector3 exit, bool inRoom)
        {
            var route = new AuthoredGuestRoute { Door = room.door };
            float side = Mathf.Sign(room.door.transform.position.x), z = room.door.transform.position.z;
            // A schedule can interrupt entry after the threshold but before RoomTarget is reached.
            inRoom |= IsOnRoomSide(current, room);
            if (inRoom)
            {
                ReturnToInnerLane(route, current, side, z);
                route.Add(Inside(room));
                route.DoorCrossing = route.Points.Count;
                route.Add(Outside(room)); route.Add(side * .32f, z);
            }
            else
            {
                if (current.z < 6) route.Add(current.x, .35f);
                route.Add(side * .32f, current.z < 6 ? .35f : current.z);
            }
            if (current.z >= 6) route.Add(side * .32f, 6.6f);
            route.Add(0, .35f); route.Add(exit);
            return route;
        }

        public static AuthoredGuestRoute LeaveRoom(Vector3 current, GuestRoomMarkers room)
        {
            var route = new AuthoredGuestRoute { Door = room.door };
            float side = Mathf.Sign(room.door.transform.position.x), z = room.door.transform.position.z;
            ReturnToInnerLane(route, current, side, z);
            route.Add(Inside(room));
            route.DoorCrossing = route.Points.Count;
            route.Add(Outside(room)); route.Add(side * .32f, z);
            return route;
        }

        public static AuthoredGuestRoute GuestAway(Vector3 current, GuestRoomMarkers room, Vector3 exteriorExit) =>
            Exit(current, room, exteriorExit, IsOnRoomSide(current, room));

        public static AuthoredGuestRoute ToServiceReception(Vector3 current, GuestRoomMarkers room, Vector3 reception, bool inRoom)
        {
            // Follow the same real doorway and clear corridor as a departure, then use the
            // reception waiting lane instead of the exterior. Room ownership is unchanged.
            var route = Exit(current, room, new Vector3(0, Feet, .35f), inRoom);
            route.Add(reception.x, .35f); route.Add(reception);
            return route;
        }

        static Vector3 Inside(GuestRoomMarkers room) => room.doorInsideAnchor ? room.doorInsideAnchor.position :
            new Vector3(Mathf.Sign(room.door.transform.position.x) * 3.25f, Feet, room.door.transform.position.z);
        static Vector3 Outside(GuestRoomMarkers room) => room.doorOutsideAnchor ? room.doorOutsideAnchor.position :
            new Vector3(Mathf.Sign(room.door.transform.position.x) * 1.15f, Feet, room.door.transform.position.z);

        public static AuthoredGuestRoute ToStandby(Vector3 current, Vector3 standby)
        {
            var route = new AuthoredGuestRoute();
            // The utility doorway is at the corridor's north end; cross its clear landing before
            // moving sideways to the staff alcove, away from the boiler and workbench.
            float lane = current.x < 0 ? -.32f : .32f;
            route.Add(lane, current.z); route.Add(lane, 30.1f);
            route.Add(standby.x, 30.1f); route.Add(standby);
            return route;
        }

        static void ReturnToInnerLane(AuthoredGuestRoute route, Vector3 current, float side, float roomZ)
        {
            // Go around the north or south end of the bed, never through its middle.
            // The radiator's outer aisle also returns around the foot of the bed. Crossing
            // straight from the right-hand valve at roomZ+1.2 would cut through the mattress.
            float z = current.z >= roomZ + 2.3f ? roomZ + 2.60f : current.x * side > 7.75f ? roomZ - .9f :
                current.z <= roomZ - .8f ? roomZ - 1.6f : current.z;
            route.Add(current.x, z); route.Add(side * 4.18f, z); route.Add(side * 4.18f, roomZ);
        }
    }
}
