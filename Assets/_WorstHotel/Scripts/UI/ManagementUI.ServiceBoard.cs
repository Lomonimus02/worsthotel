using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static WorstHotel.HotelTheme;

namespace WorstHotel
{
    public sealed partial class ManagementUI
    {
        bool showingServiceBoard, wakePhone, serviceHasResponse;
        string selectedServiceCase;
        int servicePage;
        string callingPromise;
        float callCompletesAt;
        HotelSimulation phoneSimulation;
        readonly List<(Rect rect, string title, Action action, bool enabled)> serviceChoices = new();
        public bool IsServiceBoardOpen => IsOpen && showingServiceBoard;
        public bool IsWakePhoneOpen => IsOpen && wakePhone;
        public bool IsWakeCallInProgress => IsWakePhoneOpen && callingPromise != null;
        public IEnumerable<string> ServiceOptionTitles => serviceChoices.Select(choice => choice.title);

        public void OpenReceptionServiceBoard(int actorId)
        {
            if (!Session || Session.Simulation?.Services == null) return;
            if (IsOpen) Close();
            Open(actorId);
            if (!IsOpen) return;
            showingOperations = false; showingServiceBoard = true; wakePhone = false; servicePage = 0;
            selectedServiceCase = null; serviceHasResponse = false;
            UpdateServicePanel();
        }

        public void OpenWakePhone(int actorId)
        {
            if (!Session || Session.Phase != DayPhase.Service || Session.Simulation?.Services == null) return;
            if (IsOpen) Close();
            Open(actorId);
            if (!IsOpen) return;
            showingOperations = false; wakePhone = true; showingServiceBoard = false; servicePage = 0;
            callingPromise = null;
            phoneResponseId = null;
            phoneSimulation = Session.Simulation;
            selectedServiceCase = null; serviceHasResponse = false;
            UpdateServicePanel();
        }

        void ShowServices(string caseId = null)
        {
            showingServiceBoard = true; wakePhone = false; selectedServiceCase = caseId;
            selectedServiceGuest = null; servicePage = 0; focus = 0; serviceHasResponse = false;
            UpdateServicePanel();
        }

        void AddServiceChoice(Rect rect, string title, Action action, bool enabled = true) =>
            serviceChoices.Add((rect, title, action, enabled));

        void UpdateServicePanel()
        {
            if (!showingServiceBoard && !wakePhone) return;
            var services = Session.Simulation.Services;
            if (services == null) { Close(); return; }
            if (wakePhone && (Session.Phase != DayPhase.Service || !ReferenceEquals(phoneSimulation, Session.Simulation)))
            { Close(); return; }
            serviceChoices.Clear();
            if (wakePhone)
            {
                if (callingPromise != null && Time.unscaledTime >= callCompletesAt)
                {
                    string completed = callingPromise; callingPromise = null; serviceHasResponse = true;
                    Session.CompleteWakeUpCall(owner, completed);
                }
                BuildPhoneChoices();
                AddServiceChoice(new Rect(42, 799, 705, 42), "Put down the phone", Close);
            }
            else if (selectedServiceCase != null)
            {
                var item = services.Cases.FirstOrDefault(c => c.Id == selectedServiceCase && GuestLabels.IsKnownToHotel(c));
                if (item == null) { selectedServiceCase = null; UpdateServicePanel(); return; }
                if (item.Active)
                {
                    if (item.Status != ServiceStatus.InProgress)
                    {
                        AddServiceChoice(new Rect(42, 483, 342, 43), GuestLabels.ServiceAcceptance(item, Session.Simulation), () => ServiceResponse(item.Id, true));
                        AddServiceChoice(new Rect(405, 483, 342, 43), "Decline politely", () => ServiceResponse(item.Id, false));
                        AddServiceChoice(new Rect(42, 538, 705, 43), "Acknowledge · decide later", () =>
                        { serviceHasResponse = true; Session.AcknowledgeService(owner, item.Id); }, item.Status == ServiceStatus.Requested);
                    }
                    else if (item.Kind == ServiceKind.WakeUpCall && services.Promises.Any(p => p.Id == item.Id && p.Status == PromiseStatus.Accepted))
                        AddServiceChoice(new Rect(42, 483, 705, 43), Session.Simulation.Elapsed < item.DueTime ?
                            "Cancel promised call" : "Cancel overdue call · counts as missed", () => ServiceResponse(item.Id, false));
                    if (item.Kind == ServiceKind.AskNeighborsQuiet)
                        AddServiceChoice(new Rect(42, 594, 705, 43), "Compare rooms / guest options", () =>
                        { showingServiceBoard = false; selectedServiceCase = null; selectedServiceGuest = item.GuestId; focus = 0; });
                }
                AddServiceChoice(new Rect(42, 746, 705, 42), "Back to service board", () =>
                { selectedServiceCase = null; focus = 0; serviceHasResponse = false; });
                AddServiceChoice(new Rect(42, 799, 705, 42), "Close / keep working", Close);
            }
            else
            {
                var cases = services.Cases.Where(GuestLabels.IsKnownOpenService).OrderBy(c => c.CreatedAt).ToArray();
                servicePage = Mathf.Clamp(servicePage, 0, Math.Max(0, (cases.Length - 1) / 6));
                foreach (var pair in cases.Skip(servicePage * 6).Take(6).Select((item, i) => (item, i)))
                {
                    var item = pair.item;
                    AddServiceChoice(new Rect(42, 242 + pair.i * 65, 348, 57), item.RoomId + " · " + GuestLabels.Service(item.Kind) +
                        "\n" + GuestLabels.ServiceState(item.Status), () => { selectedServiceCase = item.Id; focus = 0; serviceHasResponse = false; });
                }
                if (cases.Length > 6)
                    AddServiceChoice(new Rect(42, 640, 348, 34), "More requests ›", () => { servicePage = (servicePage + 1) % ((cases.Length + 5) / 6); focus = 0; });
                AddServiceChoice(new Rect(42, 746, 342, 42), "Guest ledger", () =>
                { showingServiceBoard = false; selectedServiceGuest = null; focus = 0; });
                AddServiceChoice(new Rect(405, 746, 342, 42), "Room preparation", () =>
                { showingServiceBoard = false; OpenHousekeeping(); });
                AddServiceChoice(new Rect(42, 799, 705, 42), "Close / keep working", Close);
            }
            actions.Clear(); enabledActions.Clear();
            foreach (var choice in serviceChoices) { actions.Add(choice.action); enabledActions.Add(choice.enabled); }
            focus = Mathf.Clamp(focus, 0, Math.Max(0, actions.Count - 1));
            if (actions.Count > 0 && !enabledActions[focus])
            { int first = enabledActions.FindIndex(enabled => enabled); if (first >= 0) focus = first; }
        }

        void ServiceResponse(string id, bool accept)
        { serviceHasResponse = true; Session.RespondToService(owner, id, accept); }

        string GuestName(string guestId) => Session.Simulation.Guests.FirstOrDefault(g => g.GuestId == guestId)?.Name ?? "Guest";

        void DrawServicePanel()
        {
            var services = Session.Simulation.Services;
            Fill(new Rect(15, 50, 770, 820), Paper);
            Border(new Rect(23, 58, 754, 804), Brass);
            Label(new Rect(42, 78, 705, 48), wakePhone ? "RECEPTION PHONE" : "GUEST SERVICE BOARD", Title);
            Label(new Rect(42, 136, 705, 43), (Session.Simulation.ContinuousOperations ? GuestLabels.HotelMoment(Session.Simulation, Session.Simulation.Elapsed) :
                "DAY " + Session.Day + " · Hotel time " + GuestLabels.HotelTime(Session.Simulation.Elapsed)) +
                (wakePhone ? "\nReception conversations and promised wake-up calls." : "\nGuest conversations, agreed promises and the next arrivals."), Small, Muted);
            if (wakePhone)
            {
                DrawPhoneContents();
            }
            else if (selectedServiceCase != null) DrawServiceCase();
            else
            {
                Label(new Rect(42, 202, 348, 30), "NOW", Heading, Teal);
                Label(new Rect(413, 202, 334, 30), "UPCOMING", Heading, Teal);
                Fill(new Rect(399, 204, 1, 445), Brass);
                if (!services.Cases.Any(GuestLabels.IsKnownOpenService)) Label(new Rect(42, 245, 345, 100), "No outstanding conversations.\nA quiet moment to prepare.", Body, Muted);
                var upcoming = new List<(float time, string text)>();
                foreach (var reservation in Session.Simulation.Reservations.Where(r => r.Status == ReservationStatus.Reserved))
                    upcoming.Add((reservation.Offer.ArrivalAt, "ARRIVAL · room " + reservation.RoomId + "\n" + reservation.Offer.Application.GuestName));
                foreach (var promise in services.Promises.Where(p => p.Status == PromiseStatus.Accepted))
                    upcoming.Add((promise.DueTime, "WAKE-UP · room " + promise.RoomId + "\n" + GuestName(promise.GuestId)));
                foreach (var guest in Session.Simulation.Guests.Where(g => g.Agent != null))
                {
                    var agent = guest.Agent;
                    if (agent.State == GuestAgentState.Scheduled || agent.State == GuestAgentState.Arriving)
                        upcoming.Add((agent.ArrivalTime, "ARRIVAL · room " + guest.RoomId + "\n" + guest.Name));
                    else if (agent.CheckedIn && agent.State != GuestAgentState.Leaving && agent.State != GuestAgentState.Left)
                    {
                        bool late = services.Cases.Any(c => c.GuestId == guest.GuestId && c.Kind == ServiceKind.LateCheckout && c.Status == ServiceStatus.Fulfilled);
                        upcoming.Add((agent.CheckoutTime, (late ? "LATE CHECKOUT" : "CHECKOUT") + " · room " + guest.RoomId + "\n" + guest.Name));
                    }
                }
                int i = 0;
                foreach (var entry in upcoming.OrderBy(e => e.time).Take(6))
                {
                    float delta = entry.time - Session.Simulation.Elapsed;
                    Label(new Rect(413, 242 + i * 65, 334, 61), GuestLabels.HotelMoment(Session.Simulation, entry.time) + " · " + entry.text + "\n" +
                        (delta <= 0 ? "due now" : "in " + GuestLabels.HotelDuration(Session.Simulation, delta)), Small, delta <= 0 ? Wine : Ink);
                    i++;
                }
                if (i == 0) Label(new Rect(413, 245, 334, 60), "Nothing scheduled yet.", Body, Muted);
                Label(new Rect(42, 684, 705, 49), "ON THE SHELVES · blankets " + services.BlanketsAvailable + " · spare bulbs " + services.BulbsAvailable +
                    "\n" + (GuestLabels.ContactCue(Session.Simulation) ?? "No one is waiting to speak right now."), Small, Muted);
            }
            if (serviceHasResponse) Label(new Rect(42, 716, 705, 25), Session.LastMessage, Small, Wine);
            foreach (var choice in serviceChoices) ButtonAt(choice.rect, choice.title, choice.action, choice.enabled);
        }

        void DrawServiceCase()
        {
            var item = Session.Simulation.Services.Cases.FirstOrDefault(c => c.Id == selectedServiceCase && GuestLabels.IsKnownToHotel(c));
            if (item == null) return;
            Label(new Rect(42, 201, 705, 38), "ROOM " + item.RoomId + " · " + GuestLabels.Service(item.Kind), Heading);
            Label(new Rect(42, 245, 705, 27), GuestName(item.GuestId) + " · " + GuestLabels.ServiceState(item.Status), Small, Teal);
            Label(new Rect(42, 283, 705, 91), GuestLabels.ServiceClue(item, Session.Simulation), Body, Ink);
            Label(new Rect(42, 385, 705, 72), GuestLabels.ServiceHelp(item.Kind), Small, Muted);
            string timing = item.Kind == ServiceKind.WakeUpCall ? "Requested call at " : item.Kind == ServiceKind.LateCheckout ? "Requested checkout at " : "Reply / help due by ";
            Label(new Rect(42, 653, 705, 37), timing + GuestLabels.HotelMoment(Session.Simulation, item.DueTime), Small, Muted);
        }
    }
}
