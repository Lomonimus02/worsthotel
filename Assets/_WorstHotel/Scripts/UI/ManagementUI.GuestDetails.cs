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
            DrawReportedNeed(207, guest, IncidentReason.Temperature, ServiceKind.ExtraBlanket, "TEMPERATURE",
                "Prefers " + guest.Application.Archetype.Needs.PreferredTemperatureMin.ToString("F0") + "–" +
                guest.Application.Archetype.Needs.PreferredTemperatureMax.ToString("F0") + "°C");
            DrawReportedNeed(271, guest, IncidentReason.Noise, ServiceKind.AskNeighborsQuiet, "QUIET", "A guest may tolerate some sound before speaking to staff.");
            DrawReportedNeed(335, guest, IncidentReason.RoomCondition, null, "ROOM CONDITION", "Speak to the guest or inspect the room with their permission.");
            Label(new Rect(42, 399, 705, 68), "THIS STAY: " + guest.Memory.NumberOfComplaints + " complaints · " +
                    guest.Memory.ProblemsResolvedSuccessfully + " resolved · " + guest.Memory.ProblemsIgnored + " ignored\n" +
                    "Credit $" + guest.Memory.CompensationReceived + " · Noise warnings " + guest.Memory.PreviousNoiseWarnings +
                    "\nServices " + guest.Memory.ServicesFulfilled + "/" + guest.Memory.ServicesRequested + " helped · " + guest.Memory.ServicesDeclined +
                    " declined · Promises " + guest.Memory.PromisesKept + " kept / " + guest.Memory.PromisesBroken + " missed", Small, Muted);
            var situations = Session.Simulation.Incidents.Items.Where(s => s.GuestId == guest.GuestId && GuestLabels.IsActionable(s)).OrderByDescending(s => s.Stage).ToArray();
            float relief = Session.Simulation.Incidents.Items.Where(s => s.GuestId == guest.GuestId && GuestLabels.IsKnownToHotel(s))
                .Select(s => s.ResponseReliefRemainingSeconds).DefaultIfEmpty(0).Max();
            Label(new Rect(42, 471, 700, 27), "REPORTED CONCERNS", Small, Muted);
            if (situations.Length == 0) Label(new Rect(42, 499, 700, 36), relief > 0 ?
                "Credit relief · " + Mathf.CeilToInt(relief) + " hotel seconds left" : guest.Compensated ?
                "Credit accepted. Continuing problems can cause new complaints." : "No active complaint reported.", Body, Teal);
            for (int i = 0; i < Mathf.Min(3, situations.Length); i++)
                Label(new Rect(42, 499 + i * 29, 700, 28), GuestLabels.Problem(situations[i].Reason) + "  /  " +
                    GuestLabels.ComplaintClue(situations[i]) + (situations[i].ResponseAccepted ?
                        (situations[i].ResponseReliefRemainingSeconds > 0 ? " / credit relief " + Mathf.CeilToInt(situations[i].ResponseReliefRemainingSeconds) + "s" :
                            " / credit; cause remains") : situations[i].AttentionAcknowledged ? " / ignored" :
                        " / " + GuestLabels.Situation(situations[i].Stage)), Small, situations[i].Stage >= SituationStage.Escalated ? Red : Ink);
            DrawGuestResponseActions(guest, situations);
            Label(new Rect(42, 680, 700, 53), "Room rate $" + guest.Price + (guest.Compensated ? " / credit reserved $" + guest.CompensationCredit : "") + "\n" +
                (guest.Agent?.PendingMoveRoomId.HasValue == true ? "Bring key " + guest.Agent.PendingMoveRoomId + " to this guest to exchange rooms." :
                 situations.Length > 0 ? "The guest has described a problem. How you help is your decision." : "Talk to guests at reception or by their room."), Small, Muted);
            ButtonAt(new Rect(42, 746, 342, 42), "Back to all guests", () => { selectedServiceGuest = null; focus = 0; });
            var service = Session.Simulation.Services?.Cases.Where(c => c.GuestId == guest.GuestId && GuestLabels.IsKnownToHotel(c)).OrderByDescending(c => c.Active).ThenByDescending(c => c.CreatedAt).FirstOrDefault();
            ButtonAt(new Rect(405, 746, 342, 42), "Service requests / promises", () => ShowServices(service?.Id), Session.Simulation.Services != null);
            ButtonAt(new Rect(42, 799, 705, 42), "Close ledger / keep working", Close);
            return true;
        }

        void DrawReportedNeed(float y, GuestStay guest, IncidentReason reason, ServiceKind? kind, string title, string fallback)
        {
            var concern = Session.Simulation.Incidents.Items.FirstOrDefault(s => s.GuestId == guest.GuestId &&
                s.Reason == reason && s.Active && GuestLabels.IsKnownToHotel(s));
            var service = kind.HasValue ? Session.Simulation.Services?.Cases.FirstOrDefault(c =>
                c.GuestId == guest.GuestId && c.Kind == kind.Value && GuestLabels.IsKnownOpenService(c)) : null;
            bool known = concern != null || service != null;
            DrawNeed(y, title, known ? "Guest spoke to staff" : "No concern reported",
                concern != null ? GuestLabels.ComplaintClue(concern) : service != null ? GuestLabels.ServiceClue(service) : fallback, known);
        }

        void DrawNeed(float y, string title, string state, string cause, bool uncomfortable)
        {
            Fill(new Rect(42, y, 705, 61), LightPaper);
            Label(new Rect(55, y + 5, 682, 26), title + "  /  " + state, Body, uncomfortable ? Wine : Teal);
            Label(new Rect(55, y + 33, 682, 25), cause, Small, Muted);
        }
    }
}
