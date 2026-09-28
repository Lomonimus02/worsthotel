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
            // Room numbers are signage; bookings and preparation live in the reservation book.
            label.text = roomId.ToString(CultureInfo.InvariantCulture);
            label.color = new Color(.95f, .84f, .58f);
        }
    }
}
