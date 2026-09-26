#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

namespace WorstHotel
{
    public sealed partial class DeveloperPanel
    {
        void DrawHousekeepingDebug()
        {
            var system = Session.Simulation.Housekeeping;
            if (system == null) return;
            GUILayout.Space(8);
            GUILayout.Label("PLAYER LINEN TURNOVER / FINITE STOCK", heading);
            foreach (var task in system.Tasks)
                GUILayout.Label(task.RoomId + " / " + task.Step + " / " + task.ProgressSeconds.ToString("F1") + "/" + task.RequiredSeconds.ToString("F1") +
                    "s / player " + task.WorkingPlayerId + " / generation " + task.Generation, body);
            foreach (var linen in system.Linens)
                GUILayout.Label(linen.Id + " / " + linen.Location + " / player " + linen.PlayerId, body);
            foreach (var room in Session.Rooms)
                GUILayout.Label(room.Profile.Id + " / " + room.Cleanliness + " / " + room.TurnoverState +
                    " / departing " + (room.DepartingGuestId ?? "none"), body);
            var selected = Session.Rooms[roomIndex];
            if (Button("Mark selected room " + selected.Profile.Id + " dirty", !selected.Occupied &&
                string.IsNullOrEmpty(selected.DepartingGuestId) && selected.Cleanliness == Cleanliness.Clean))
                Apply(() => Session.Simulation.DebugMarkRoomDirty(selected.Profile.Id));
        }
    }
}
#endif
