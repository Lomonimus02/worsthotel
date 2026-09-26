using System.Globalization;
using UnityEngine;

namespace WorstHotel
{
    /// <summary>Read-only, event-driven room status. All displayed values come from the local authority.</summary>
    public sealed class RoomStatusPlaque : MonoBehaviour
    {
        public int roomId = 101;
        public TextMesh label;
        GameSession session;
        PlanningSystem plan;

        void OnEnable() => Bind();
        void Start() => Bind();
        void OnDisable() => Unbind();

        void Bind()
        {
            if (session != GameSession.Instance)
            {
                Unbind();
                session = GameSession.Instance;
                if (session != null) session.Changed += Refresh;
            }
            Refresh();
        }

        void Unbind()
        {
            if (session != null) session.Changed -= Refresh;
            if (plan != null) plan.Changed -= Refresh;
            session = null;
            plan = null;
        }

        void Refresh()
        {
            if (label == null) return;
            if (session == null || session.Rooms == null)
            {
                label.text = roomId.ToString(CultureInfo.InvariantCulture) + "\n—";
                return;
            }
            // NewGame and each new planning phase replace the plan instance.
            if (plan != session.Plan)
            {
                if (plan != null) plan.Changed -= Refresh;
                plan = session.Plan;
                if (plan != null) plan.Changed += Refresh;
            }
            var room = System.Array.Find(session.Rooms, item => item.Profile.Id == roomId);
            if (room == null)
            {
                label.text = roomId.ToString(CultureInfo.InvariantCulture) + "\n—";
                return;
            }
            bool reserved = session.Phase == DayPhase.Planning ? plan != null && plan.TryGetAssignment(roomId, out _) : room.Reserved;
            string status = room.Occupied ? "OCCUPIED" : room.DepartingGuestId != null ? "GUEST LEAVING" :
                room.TurnoverState == HousekeepingState.Cleaning ? "CLEANING" : room.Cleanliness == Cleanliness.Dirty ?
                (reserved ? "WAIT CLEAN" : "DIRTY") : reserved ? "RESERVED" : "READY";
            label.text = roomId.ToString(CultureInfo.InvariantCulture) + " / " + room.CircuitId + "\n" + status + "\n" +
                (room.HasPower ? room.Temperature.ToString("0.0", CultureInfo.InvariantCulture) + " °C" : "POWER OFF");
            label.color = !room.HasPower ? new Color(1, .52f, .26f) : room.Occupied ? new Color(.96f, .88f, .66f) :
                reserved ? new Color(1f, .77f, .38f) : new Color(.67f, .88f, .74f);
        }
    }
}
