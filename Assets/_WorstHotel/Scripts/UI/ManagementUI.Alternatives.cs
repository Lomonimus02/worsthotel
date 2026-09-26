using System;
using System.Linq;
using UnityEngine;
using static WorstHotel.HotelTheme;

namespace WorstHotel
{
    public sealed partial class ManagementUI
    {
        bool choosingMoveRoom;

        void DrawGuestResponseActions(GuestStay guest, HotelIncident[] situations)
        {
            bool inRoom = guest.Agent != null && guest.Agent.InAssignedRoom;
            bool softNoise = Session.Simulation.Services?.Cases.Any(c => c.GuestId == guest.GuestId && GuestLabels.IsKnownOpenService(c) && c.Kind == ServiceKind.AskNeighborsQuiet) == true;
            int credit = (int)Math.Round(guest.Price * Session.Economy.CompensationRate, MidpointRounding.AwayFromZero);
            ButtonAt(new Rect(42, 598, 345, 35), guest.Compensated ? "Credit reserved $" + guest.CompensationCredit : "Offer $" + credit + " for patience",
                () => Session.OfferCompensation(owner, guest.GuestId), inRoom && !guest.Compensated && (situations.Length > 0 || softNoise));
            ButtonAt(new Rect(401, 598, 346, 35), "Accept loss / leave unresolved",
                () => Session.AcceptConsequences(owner, guest.GuestId), inRoom && situations.Any(s => !s.AttentionAcknowledged));
            bool pendingMove = guest.Agent != null && guest.Agent.PendingMoveRoomId.HasValue;
            var moveIntent = Session.Simulation.ContinuousOperations ? Session.Simulation.Services?.DirectIntent(guest.GuestId) : null;
            string expectedIntentId = moveIntent?.Id;
            int expectedIntentRevision = moveIntent?.Revision ?? -1;
            ButtonAt(new Rect(42, 641, 705, 35), pendingMove ? "Cancel move to " + guest.Agent.PendingMoveRoomId : "Compare rooms / relocate guest",
                () => { if (pendingMove) Session.CancelGuestMove(owner, guest.GuestId, expectedIntentId, expectedIntentRevision); else { choosingMoveRoom = true; focus = 0; } }, inRoom);
        }

        void DrawRelocationOptions(GuestStay guest)
        {
            Fill(new Rect(15, 50, 770, 820), Paper);
            Border(new Rect(23, 58, 754, 804), Brass);
            Label(new Rect(42, 78, 700, 48), "A DIFFERENT ROOM", Title);
            Label(new Rect(42, 135, 700, 61), guest.Name + " / currently " + guest.RoomId +
                "\nAgreed price and experience so far stay with the guest.", Body, Muted);
            int index = 0;
            foreach (var room in Session.Rooms)
            {
                int id = room.Profile.Id;
                bool preparing = room.Cleanliness != Cleanliness.Clean || room.TurnoverState == HousekeepingState.Moving ||
                    room.TurnoverState == HousekeepingState.Cleaning;
                bool departing = !string.IsNullOrEmpty(room.DepartingGuestId);
                bool available = !room.Occupied && !room.Reserved && !preparing && !departing;
                string status = id == guest.RoomId ? "CURRENT ROOM" : room.Occupied ? "Occupied" : room.Reserved ? "Reserved" :
                    departing ? "Guest leaving" : preparing ? "Needs preparation" : "Available";
                ButtonAt(new Rect(42, 212 + index * 68, 705, 59),
                    id + "  /  " + status + "  /  " + room.Temperature.ToString("F1") + "°C\n" +
                    room.Profile.Label + " · noise " + (room.Noise > guest.Application.Archetype.Needs.NoiseTolerance ? "above tolerance" : "within tolerance") +
                    " · " + room.RepairState + (room.HasPower ? "" : " · POWER OFF"),
                    () => { if (Session.MoveGuest(owner, guest.GuestId, id).Success) { choosingMoveRoom = false; focus = 0; } },
                    available && guest.Agent != null && guest.Agent.InAssignedRoom);
                index++;
            }
            Label(new Rect(42, 639, 700, 87), "Reserve a room, then bring its key from reception to the guest. Their current room stays in use until the key exchange.\n" + Session.LastMessage, Small, Muted);
            ButtonAt(new Rect(42, 746, 705, 42), "Back to guest", () => { choosingMoveRoom = false; focus = 0; });
            ButtonAt(new Rect(42, 799, 705, 42), "Close ledger / keep working", Close);
        }
    }
}
