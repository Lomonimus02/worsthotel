using System.Linq;
using UnityEngine;
using static WorstHotel.HotelTheme;

namespace WorstHotel
{
    public sealed partial class ManagementUI
    {
        bool showingHousekeeping;

        void OpenHousekeeping() { showingHousekeeping = true; selectedServiceGuest = null; choosingMoveRoom = false; focus = 0; }

        void DrawHousekeeping()
        {
            var oldMatrix = GUI.matrix;
            if (Session.Phase == DayPhase.Planning)
                GUI.matrix = Matrix4x4.TRS(new Vector3(Screen.width * .25f, 0, 0), Quaternion.identity,
                    new Vector3(Screen.width / 1600f, Screen.height / 900f, 1));
            var system = Session.Simulation.Housekeeping;
            Fill(new Rect(15, 50, 770, 820), Paper);
            Border(new Rect(23, 58, 754, 804), Brass);
            Label(new Rect(42, 78, 700, 48), "ROOM PREPARATION", Title);
            int stock = system.Linens.Count(item => item.Kind == LinenKind.Clean && item.Location != LinenLocation.Consumed);
            Label(new Rect(42, 135, 700, 61), "YOU ARE THE STAFF  /  " + stock + " clean bundles available" +
                "\nDirty linen → utility hamper. Clean shelf → room bed.", Body, Muted);
            int index = 0;
            foreach (var room in Session.Rooms)
            {
                int roomId = room.Profile.Id;
                var task = system.Tasks.FirstOrDefault(t => t.RoomId == roomId);
                string status = PreparationStatus(room, task);
                var booking = Session.Plan.Assignments.FirstOrDefault(a => a.RoomId == roomId);
                var upcoming = booking != null ? Session.Plan.Applications.FirstOrDefault(a => a.Id == booking.BookingId) : null;
                float y = 212 + index++ * 68;
                Fill(new Rect(42, y, 705, 61), LightPaper);
                Label(new Rect(55, y + 4, 455, 29), roomId + "  /  " + status, Body, room.Cleanliness == Cleanliness.Clean && room.DepartingGuestId == null ? Teal : Wine);
                Label(new Rect(55, y + 32, 455, 24), upcoming != null ? upcoming.GuestName + " booked" : "No booking for this room", Small, Muted);
                Label(new Rect(530, y + 10, 204, 39), task?.WorkingPlayerId != null ?
                    "Staff " + (task.WorkingPlayerId.Value + 1) : room.Cleanliness == Cleanliness.Dirty ? "Player preparation" : "", Small, Muted);
            }
            Label(new Rect(42, 634, 700, 91), "You may reserve dirty rooms, but guests need a prepared bed before receiving their key. The linen shelf and hamper are in the utility room at the far end of the corridor. Stock refills each morning.\n" + Session.LastMessage, Small, Muted);
            ButtonAt(new Rect(42, 746, 705, 42), "Back to " + (Session.Phase == DayPhase.Planning ? "bookings" : "guest relations"), () => { showingHousekeeping = false; focus = 0; });
            ButtonAt(new Rect(42, 799, 705, 42), "Close ledger / keep working", Close);
            GUI.matrix = oldMatrix;
        }

        static string PreparationStatus(RoomState room, HousekeepingTask task)
        {
            if (room.Occupied) return "Occupied";
            if (room.DepartingGuestId != null) return "Guest leaving";
            if (room.Cleanliness == Cleanliness.Clean) return "Ready";
            if (task == null) return "Awaiting preparation";
            switch (task.Step)
            {
                case RoomPreparationStep.DirtyLinenOnBed: return "Remove dirty linen";
                case RoomPreparationStep.DeliverDirtyLinen: return "Deliver dirty linen to hamper";
                case RoomPreparationStep.NeedsCleanLinen: return "Bring clean linen from shelf";
                case RoomPreparationStep.MakingBed: return "Making bed " + Mathf.RoundToInt(task.Progress01 * 100) + "%";
                default: return "Ready";
            }
        }
    }
}
