using System.Linq;
using UnityEngine;
using static WorstHotel.HotelTheme;

namespace WorstHotel
{
    public sealed partial class ManagementUI
    {
        bool DrawServiceGuestDetail()
        {
            var guest = Session.Simulation.Guests.FirstOrDefault(g => g.GuestId == selectedServiceGuest);
            if (guest == null) { selectedServiceGuest = null; return false; }
            var room = Session.Rooms.First(r => r.Profile.Id == guest.RoomId);
            if (choosingMoveRoom) { DrawRelocationOptions(guest); return true; }
            Fill(new Rect(15, 50, 770, 820), Paper);
            Border(new Rect(23, 58, 754, 804), Brass);
            Label(new Rect(42, 78, 700, 48), "ROOM " + guest.RoomId + " / GUEST", Title);
            Label(new Rect(42, 135, 700, 28), guest.Name + "  ·  " + (guest.Agent != null ? GuestLabels.State(guest.Agent) : "In room"), Heading);
            Label(new Rect(42, 169, 700, 42), GuestLabels.Traits(guest.Application.Archetype.Traits) + "\n" + GuestLabels.Tendencies(guest.Application.Archetype), Small, Muted);
            var needs = guest.Needs;
            if (needs != null && guest.Agent != null && (guest.Agent.InAssignedRoom || guest.Agent.State == GuestAgentState.WaitingForCheckIn))
            {
                bool inRoom = guest.Agent.InAssignedRoom;
                DrawNeed(207, "TEMPERATURE", inRoom ? GuestLabels.Need(needs.Temperature.Severity) : "Not in the room yet",
                    "Room " + room.Temperature.ToString("F1") + "°C" + (guest.BlanketComfortBonus > 0 ? " / blanket comfort +" + guest.BlanketComfortBonus.ToString("F1") + "°C" : "") +
                    "; prefers " + guest.Application.Archetype.Needs.PreferredTemperatureMin.ToString("F0") + "–" + guest.Application.Archetype.Needs.PreferredTemperatureMax.ToString("F0") + "°C", inRoom && needs.Temperature.Severity > .03f);
                DrawNeed(271, "QUIET", inRoom ? GuestLabels.Need(needs.Noise.Severity) : "Not in the room yet",
                    inRoom ? RoomNoiseCause(room) : "Room exposure begins after the guest arrives there.", inRoom && needs.Noise.Severity > .03f);
                DrawNeed(335, "ROOM CONDITION", inRoom ? GuestLabels.Need(needs.RoomCondition.Severity) : "Awaiting room access",
                    (room.Cleanliness == Cleanliness.Dirty ? "Used bed linen remains" : "Bed prepared") + " / " +
                    (room.LampBroken ? "Lamp bulb burnt out" : RoomPowerDescription(room)), inRoom && needs.RoomCondition.Severity > .03f);
            }
            else Label(new Rect(42, 240, 700, 65), guest.Agent == null ? "Guest information is unavailable." : GuestLabels.State(guest.Agent) + ". Room experience is recorded during the stay.", Body, Muted);
            Label(new Rect(42, 399, 705, 68), "THIS STAY: " + guest.Memory.NumberOfComplaints + " complaints · " +
                    guest.Memory.ProblemsResolvedSuccessfully + " resolved · " + guest.Memory.ProblemsIgnored + " ignored\n" +
                    "Credit $" + guest.Memory.CompensationReceived + " · Noise warnings " + guest.Memory.PreviousNoiseWarnings +
                    "\nServices " + guest.Memory.ServicesFulfilled + "/" + guest.Memory.ServicesRequested + " helped · " + guest.Memory.ServicesDeclined +
                    " declined · Promises " + guest.Memory.PromisesKept + " kept / " + guest.Memory.PromisesBroken + " missed", Small, Muted);
            var situations = Session.Simulation.Incidents.Items.Where(s => s.GuestId == guest.GuestId && GuestLabels.IsActionable(s)).OrderByDescending(s => s.Stage).ToArray();
            float relief = Session.Simulation.Incidents.Items.Where(s => s.GuestId == guest.GuestId)
                .Select(s => s.ResponseReliefRemainingSeconds).DefaultIfEmpty(0).Max();
            Label(new Rect(42, 471, 700, 27), "CURRENT CONDITIONS", Small, Muted);
            if (situations.Length == 0) Label(new Rect(42, 499, 700, 36), relief > 0 ?
                "Credit relief · " + Mathf.CeilToInt(relief) + " hotel seconds left" : guest.Compensated ?
                "Credit accepted. Continuing problems can cause new complaints." : "No active situation.", Body, Teal);
            for (int i = 0; i < Mathf.Min(3, situations.Length); i++)
                Label(new Rect(42, 499 + i * 29, 700, 28), GuestLabels.Problem(situations[i].Reason) + "  /  " +
                    GuestLabels.ComplaintClue(situations[i]) + (situations[i].ResponseAccepted ?
                        (situations[i].ResponseReliefRemainingSeconds > 0 ? " / credit relief " + Mathf.CeilToInt(situations[i].ResponseReliefRemainingSeconds) + "s" :
                            " / credit; cause remains") : situations[i].AttentionAcknowledged ? " / ignored" :
                        " / " + GuestLabels.Situation(situations[i].Stage)), Small, situations[i].Stage >= SituationStage.Escalated ? Red : Ink);
            DrawGuestResponseActions(guest, situations);
            Label(new Rect(42, 680, 700, 53), "Room rate $" + guest.Price + (guest.Compensated ? " / credit reserved $" + guest.CompensationCredit : "") + "\n" +
                (guest.Agent?.PendingMoveRoomId.HasValue == true ? "Bring key " + guest.Agent.PendingMoveRoomId + " to this guest to exchange rooms." :
                 situations.Any(s => s.Reason == IncidentReason.Noise) ? "Sound carries through walls and doors. Speaking to a source guest also lets you offer them a move." :
                 situations.Any(s => s.Reason == IncidentReason.Temperature) ? "Check heat delivery or use a powered heater. Credit buys patience; it does not warm the room." :
                 situations.Any(s => s.Reason == IncidentReason.RoomCondition) ? "Inspect the bed and lights. A room change needs a prepared room and its physical key." : Session.LastMessage), Small, Muted);
            ButtonAt(new Rect(42, 746, 342, 42), "Back to all guests", () => { selectedServiceGuest = null; focus = 0; });
            var service = Session.Simulation.Services?.Cases.Where(c => c.GuestId == guest.GuestId).OrderByDescending(c => c.Active).ThenByDescending(c => c.CreatedAt).FirstOrDefault();
            ButtonAt(new Rect(405, 746, 342, 42), "Service requests / promises", () => ShowServices(service?.Id), Session.Simulation.Services != null);
            ButtonAt(new Rect(42, 799, 705, 42), "Close ledger / keep working", Close);
            return true;
        }

        void DrawNeed(float y, string title, string state, string cause, bool uncomfortable)
        {
            Fill(new Rect(42, y, 705, 61), LightPaper);
            Label(new Rect(55, y + 5, 682, 26), title + "  /  " + state, Body, uncomfortable ? Wine : Teal);
            Label(new Rect(55, y + 33, 682, 25), cause, Small, Muted);
        }
    }
}
