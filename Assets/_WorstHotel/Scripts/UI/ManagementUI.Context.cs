using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using static WorstHotel.HotelTheme;

namespace WorstHotel
{
    public sealed partial class ManagementUI
    {
        bool guestContext, contextThroughDoor, contextAwaitingEntry, contextHasResponse;
        readonly List<(string title, System.Action action)> contextChoices = new List<(string, System.Action)>();
        public bool IsGuestContextOpen => IsOpen && guestContext;
        public string ContextGuestId => IsGuestContextOpen ? selectedServiceGuest : null;
        public IEnumerable<string> ContextOptionTitles => contextChoices.Select(choice => choice.title);

        /// <summary>Called after the authority has accepted a physical knock or conversation.</summary>
        public void OpenGuestContext(int actorId, string guestId, bool throughDoor = false)
        {
            if (!Session || Session.Phase != DayPhase.Service ||
                !Session.Simulation.Guests.Any(g => g.GuestId == guestId)) return;
            if (IsOpen) Close();
            Open(actorId);
            if (!IsOpen) return;
            selectedServiceGuest = guestId;
            contextThroughDoor = throughDoor;
            contextAwaitingEntry = false;
            contextHasResponse = false;
            guestContext = true;
            UpdateGuestContext();
        }

        void UpdateGuestContext()
        {
            if (!guestContext) return;
            var guest = Session.Simulation.Guests.FirstOrDefault(g => g.GuestId == selectedServiceGuest);
            if (guest == null) { Close(); return; }
            var room = Session.Rooms.FirstOrDefault(r => r.Profile.Id == guest.RoomId);
            if (contextAwaitingEntry && room != null && room.DoorState == RoomDoorState.Open) { Close(); return; }
            BuildGuestContextChoices(guest);
            // Input is independent of IMGUI repaint timing (hidden windows and split views may
            // skip a repaint). The visible panel renders this same contextual action collection.
            actions.Clear(); enabledActions.Clear();
            foreach (var choice in contextChoices) { actions.Add(choice.action); enabledActions.Add(true); }
            focus = Mathf.Clamp(focus, 0, contextChoices.Count - 1);
        }

        public static bool CanAskForQuiet(HotelSimulation simulation, GuestStay guest)
        {
            var agent = guest?.Agent;
            if (simulation == null || agent == null || !agent.InAssignedRoom || !agent.ActivityStaged ||
                agent.State == GuestAgentState.Sleeping || agent.QuietUntil > simulation.Clock.SimulationTime) return false;
            bool television = agent.Activity == GuestActivity.LoudRoom || agent.Activity == GuestActivity.WatchTV;
            return agent.NoiseOutput > .1f && (agent.Activity == GuestActivity.PhoneCall || television &&
                (simulation.Electrical == null || simulation.Electrical.CircuitForRoom(guest.RoomId)?.HasPower == true));
        }

        void DrawGuestContext()
        {
            var guest = Session.Simulation.Guests.FirstOrDefault(g => g.GuestId == selectedServiceGuest);
            if (guest?.Agent == null) { pending = Close; return; }
            var cases = Session.Simulation.Incidents.Items.Where(s => s.GuestId == guest.GuestId && GuestLabels.IsActionable(s))
                .OrderByDescending(s => s.Stage).ToArray();
            bool present = ContextGuestAvailable(guest);
            bool privateActivity = guest.Agent.State == GuestAgentState.Sleeping || guest.Agent.Activity == GuestActivity.Shower;
            bool noisy = CanAskForQuiet(Session.Simulation, guest);
            Fill(new Rect(70, 220, 660, 510), Paper);
            Border(new Rect(78, 228, 644, 494), Brass, 2);
            Label(new Rect(99, 271, 598, 34), "ROOM " + guest.RoomId + "  /  " + guest.Name, Heading);
            var service = CurrentGuestService(guest);
            string line = !present ? "No answer; the guest is out." : privateActivity ? "I need some privacy. Please come back later." :
                cases.Length > 0 ? GuestLabels.ComplaintClue(cases[0]) : service != null ? GuestLabels.ServiceClue(service) : noisy ?
                    (guest.Memory.PreviousNoiseWarnings > 0 ? "Yes? We have already spoken about the noise." : "Yes? You wanted to speak to me?") : "Yes? What is it?";
            Label(new Rect(99, 310, 598, 66), line, Body, Muted);
            if (contextHasResponse) Label(new Rect(99, 370, 598, 20), Session.LastMessage, Small, Wine);
            float y = 390;
            foreach (var choice in contextChoices) ContextButton(ref y, choice.title, choice.action);
            Label(new Rect(99, 690, 598, 24), "D-pad / arrows select · A / Enter confirms · B / Backspace closes", Small, Muted);
        }

        void BuildGuestContextChoices(GuestStay guest)
        {
            contextChoices.Clear();
            var cases = Session.Simulation.Incidents.Items.Where(s => s.GuestId == guest.GuestId && GuestLabels.IsActionable(s)).ToArray();
            bool canTalk = ContextGuestAvailable(guest) && guest.Agent.State != GuestAgentState.Sleeping &&
                guest.Agent.Activity != GuestActivity.Shower;
            bool noisy = CanAskForQuiet(Session.Simulation, guest);
            var service = CurrentGuestService(guest);
            if (canTalk && service != null && cases.Length == 0 && !noisy)
            {
                if (service.Status != ServiceStatus.InProgress)
                {
                    contextChoices.Add((GuestLabels.ServiceAcceptance(service), () =>
                    { contextHasResponse = true; Session.RespondToService(owner, service.Id, true); }));
                    contextChoices.Add(("Decline politely", () =>
                    { contextHasResponse = true; Session.RespondToService(owner, service.Id, false); }));
                    if (service.Status == ServiceStatus.Requested && service.Kind != ServiceKind.AskNeighborsQuiet)
                        contextChoices.Add(("Acknowledge · decide later", () =>
                        { contextHasResponse = true; Session.AcknowledgeService(owner, service.Id); }));
                }
                else contextChoices.Add(("Review our agreement", () =>
                { Session.CloseGuestConversation(owner, guest.GuestId); guestContext = false; ShowServices(service.Id); }));
                if (service.Kind == ServiceKind.WakeUpCall && service.Status == ServiceStatus.InProgress)
                    contextChoices.Add((Session.Simulation.Elapsed < service.DueTime ? "Cancel promised call" : "Cancel overdue call · counts as missed", () =>
                    { contextHasResponse = true; Session.RespondToService(owner, service.Id, false); }));
                if (service.Kind == ServiceKind.AskNeighborsQuiet && !guest.Compensated)
                {
                    int credit = (int)System.Math.Round(guest.Price * Session.Economy.CompensationRate, System.MidpointRounding.AwayFromZero);
                    contextChoices.Add(("Offer $" + credit + " compensation · noise remains", () =>
                    { contextHasResponse = true; Session.OfferCompensation(owner, guest.GuestId); }));
                }
                if (contextThroughDoor)
                    contextChoices.Add(("Ask permission to enter", () =>
                    { contextHasResponse = true; contextAwaitingEntry = Session.RequestGuestRoomEntry(owner, guest.GuestId).Success; }));
                contextChoices.Add(("Cancel / end conversation", Close));
                return;
            }
            if (canTalk)
            {
                if (noisy) contextChoices.Add(("Ask to keep it down", () =>
                { contextHasResponse = true; Session.RequestQuiet(owner, guest.GuestId); }));
                if (cases.Length > 0 && !guest.Compensated)
                {
                    int credit = (int)System.Math.Round(guest.Price * Session.Economy.CompensationRate, System.MidpointRounding.AwayFromZero);
                    contextChoices.Add(("Offer $" + credit + " compensation", () =>
                    { contextHasResponse = true; Session.OfferCompensation(owner, guest.GuestId); }));
                }
                if (noisy || cases.Length > 0 || guest.Agent.PendingMoveRoomId.HasValue)
                    contextChoices.Add((guest.Agent.PendingMoveRoomId.HasValue ? "Review the room change" : "Offer another room", () =>
                    { Session.CloseGuestConversation(owner, guest.GuestId); guestContext = false; choosingMoveRoom = true; focus = 0; }));
                if (!noisy && cases.Length > 0 && cases.All(s => !s.AttentionAcknowledged))
                    contextChoices.Add(("Leave the problem unresolved", () =>
                    { contextHasResponse = true; Session.AcceptConsequences(owner, guest.GuestId); }));
                if (contextThroughDoor)
                    contextChoices.Add(("Ask permission to enter", () =>
                    { contextHasResponse = true; contextAwaitingEntry = Session.RequestGuestRoomEntry(owner, guest.GuestId).Success; }));
            }
            contextChoices.Add(("Cancel / end conversation", Close));
        }

        ServiceCase CurrentGuestService(GuestStay guest) => Session.Simulation.Services?.Cases
            .Where(c => c.GuestId == guest.GuestId && GuestLabels.IsKnownOpenService(c)).OrderBy(c => c.CreatedAt).FirstOrDefault();

        bool ContextGuestAvailable(GuestStay guest) => guest?.Agent != null && (guest.Agent.InAssignedRoom ||
            guest.Agent.State == GuestAgentState.WaitingForCheckIn || guest.Agent.State == GuestAgentState.WaitingAtServiceReception ||
            guestContext && guest.Agent.State == GuestAgentState.ReturningFromServiceReception);

        void ContextButton(ref float y, string title, System.Action command)
        {
            ButtonAt(new Rect(99, y, 598, 47), title, () => { command(); });
            y += 55;
        }
    }
}
