using System;
using System.Linq;
using UnityEngine;

namespace WorstHotel
{
    /// <summary>Factual room information on a physical board, read from the same host/mirror model.</summary>
    public sealed class RoomStatusBoard : MonoBehaviour
    {
        public int[] roomIds = Array.Empty<int>();
        public TextMesh[] labels = Array.Empty<TextMesh>();
        float nextRefresh;
        public static string Status(HotelSimulation hotel, RoomState room)
        {
            if (!room.Operational) return "CLOSED";
            if (!string.IsNullOrEmpty(room.DepartingGuestId)) return "CHECKOUT";
            if (room.Occupied) return "OCCUPIED";
            if (room.Cleanliness != Cleanliness.Clean || room.Disorder != RoomDisorder.None) return "NOT READY";
            if (room.Reserved || hotel.Reservations.Any(r => r.RoomId == room.Profile.Id &&
                r.Status == ReservationStatus.Reserved && r.Offer.ArrivalDay == hotel.CalendarDay)) return "RESERVED";
            return "READY";
        }
        void LateUpdate()
        {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + .25f;
            var session = GameSession.Instance;
            if (!session || session.Simulation == null) return;
            for (int i = 0; i < roomIds.Length && i < labels.Length; i++)
            {
                var room = Array.Find(session.Rooms, r => r.Profile.Id == roomIds[i]);
                if (room == null || !labels[i]) continue;
                string status = Status(session.Simulation, room);
                labels[i].text = roomIds[i] + "   " + status;
                labels[i].color = status == "NOT READY" || status == "CHECKOUT" ? new Color(1, .58f, .36f) :
                    status == "READY" ? new Color(.56f, .93f, .79f) : new Color(.96f, .86f, .63f);
            }
        }
    }
}
