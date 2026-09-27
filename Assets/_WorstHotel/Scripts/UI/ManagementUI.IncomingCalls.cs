using System.Linq;
using UnityEngine;
using static WorstHotel.HotelTheme;

namespace WorstHotel
{
    public sealed partial class ManagementUI
    {
        string phoneResponseId;
        float phoneAnswerRequestedAt;
        public string PhoneResponseId => IsWakePhoneOpen ? phoneResponseId : null;

        void BuildPhoneChoices()
        {
            var services = Session.Simulation.Services;
            var response = services.FindResponse(phoneResponseId);
            if (phoneResponseId != null && (response == null || response.CommunicatedAt < 0 &&
                (response.Phase == GuestResponsePhase.Cancelled || Time.unscaledTime - phoneAnswerRequestedAt > 10)))
            { phoneResponseId = null; response = null; }
            if (phoneResponseId != null)
            {
                if (response.CommunicatedAt >= 0) BuildAnsweredPhoneChoices(response);
                return;
            }
            var incoming = services.IncomingCall;
            if (incoming != null)
            {
                string responseId = incoming.Id;
                AddServiceChoice(new Rect(42, 223, 705, 53), "Answer reception call", () =>
                {
                    if (Session.AnswerIncomingServiceCall(owner, responseId).Success)
                    { phoneResponseId = responseId; phoneAnswerRequestedAt = Time.unscaledTime; serviceHasResponse = false; focus = 0; }
                    else serviceHasResponse = true;
                }, callingPromise == null);
            }
            float y = incoming != null ? 323 : 236;
            foreach (var promise in services.Promises.Where(p => p.Status == PromiseStatus.Accepted).OrderBy(p => p.DueTime).Take(6))
            {
                var selected = promise;
                bool due = Session.Simulation.Elapsed >= promise.DueTime - services.Settings.WakeToleranceSeconds;
                AddServiceChoice(new Rect(42, y, 705, 53), "Room " + promise.RoomId + " · " +
                    (due ? "Call now" : "Call at " + GuestLabels.HotelMoment(Session.Simulation, promise.DueTime)) + "\n" + GuestName(promise.GuestId),
                    () => { callingPromise = selected.Id; callCompletesAt = Time.unscaledTime + 1.2f; serviceHasResponse = false; }, due && callingPromise == null);
                y += 60;
            }
        }

        void BuildAnsweredPhoneChoices(GuestResponse response)
        {
            var simulation = Session.Simulation;
            var item = simulation.Services.Cases.FirstOrDefault(c => c.Id == response.ServiceCaseId && GuestLabels.IsKnownOpenService(c));
            var discussion = simulation.ContinuousOperations ? simulation.Services.CompensationDiscussion(response.GuestId) : null;
            bool canDecide = !simulation.ContinuousOperations || discussion != null && discussion.ResponseId == response.Id;
            string discussionId = discussion?.Id;
            int discussionRevision = discussion?.Revision ?? -1;
            float y = 414;
            if (item != null && item.Status != ServiceStatus.InProgress)
            {
                PhoneChoice(ref y, GuestLabels.ServiceAcceptance(item, simulation), () => ServiceResponse(item.Id, true));
                PhoneChoice(ref y, "Decline politely", () => ServiceResponse(item.Id, false));
                if (item.Status == ServiceStatus.Requested)
                    PhoneChoice(ref y, "Acknowledge · decide later", () => { serviceHasResponse = true; Session.AcknowledgeService(owner, item.Id); });
                var guest = simulation.Guests.FirstOrDefault(value => value.GuestId == response.GuestId);
                if (item.Kind == ServiceKind.AskNeighborsQuiet && guest != null && !guest.Compensated && canDecide)
                    PhoneChoice(ref y, "Offer compensation · noise remains", () =>
                    { serviceHasResponse = true; Session.OfferCompensation(owner, guest.GuestId, discussionId, discussionRevision); });
            }
            else
            {
                var incident = simulation.Incidents.Items.FirstOrDefault(i => i.Id == response.IncidentId &&
                    i.EpisodeCount == response.IncidentEpisode && (GuestLabels.IsActionable(i) ||
                        discussion?.IncidentId == i.Id && i.Active && GuestLabels.IsKnownToHotel(i)));
                var guest = simulation.Guests.FirstOrDefault(g => g.GuestId == response.GuestId);
                if (incident != null && guest != null && !guest.Compensated && canDecide)
                    PhoneChoice(ref y, "Offer compensation", () => { serviceHasResponse = true; Session.OfferCompensation(owner, guest.GuestId, discussionId, discussionRevision); });
                if (incident != null || item != null)
                    PhoneChoice(ref y, "Review guest / room choices", () =>
                    {
                        Session.CloseWakePhone(owner); wakePhone = false; phoneResponseId = null;
                        selectedServiceGuest = response.GuestId; selectedServiceCase = null; focus = 0;
                    });
                if (incident != null && !incident.AttentionAcknowledged && canDecide)
                    PhoneChoice(ref y, "Leave the problem unresolved", () => { serviceHasResponse = true; Session.AcceptConsequences(owner, response.GuestId, discussionId, discussionRevision); });
            }
            PhoneChoice(ref y, "End conversation / other calls", () =>
            { Session.EndServicePhoneConversation(owner, response.GuestId); phoneResponseId = null; serviceHasResponse = false; focus = 0; });
        }

        void PhoneChoice(ref float y, string title, System.Action action)
        { AddServiceChoice(new Rect(42, y, 705, 47), title, action); y += 57; }

        void DrawPhoneContents()
        {
            var services = Session.Simulation.Services;
            var response = services.FindResponse(phoneResponseId);
            if (phoneResponseId != null)
            {
                if (response == null || response.CommunicatedAt < 0)
                    Label(new Rect(42, 214, 705, 100), "Answering reception…", Body, Muted);
                else
                {
                    Label(new Rect(42, 206, 705, 35), "ROOM " + response.RoomId + " · " + GuestName(response.GuestId), Heading);
                    var guest = Session.Simulation.Guests.FirstOrDefault(value => value.GuestId == response.GuestId);
                    string departureWarning = GuestLabels.KnownEarlyDepartureWarning(guest, Session.Simulation);
                    Label(new Rect(42, 264, 705, departureWarning != null ? 65 : 98), GuestLabels.ResponseClue(Session.Simulation, response), Body);
                    if (departureWarning != null) Label(new Rect(42, 335, 705, 26), departureWarning, Small, Wine);
                    var item = services.Cases.FirstOrDefault(c => c.Id == response.ServiceCaseId && GuestLabels.IsKnownToHotel(c));
                    Label(new Rect(42, 365, 705, 33), GuestLabels.IntentState(services.CompensationDiscussion(response.GuestId), Session.Simulation) ??
                        (item != null ? GuestLabels.ServiceProgress(item, Session.Simulation) : "Guest concern · heard by reception"), Small, Teal);
                }
                return;
            }
            bool incoming = services.IncomingCall != null;
            Label(new Rect(42, 188, 705, 31), incoming ? "INCOMING CALL · a guest is calling reception" :
                Session.Simulation.ContinuousOperations ? "WAKE-UP CALLS · agreed hotel dates and times" :
                "WAKE-UP CALLS · agreed times are measured from shift start", Small, Teal);
            if (incoming) Label(new Rect(42, 286, 705, 29), "PROMISED WAKE-UP CALLS", Small, Teal);
            if (!services.Promises.Any(p => p.Status == PromiseStatus.Accepted))
                Label(new Rect(42, incoming ? 336 : 246, 705, 98), "No wake-up calls are waiting.\nAgreed calls will appear here.", Body, Muted);
            Label(new Rect(42, 687, 705, 43), callingPromise != null ? "Calling the room… Stay on the line." :
                "Answering tells you what the guest needs. It does not promise or complete the help.", Small, Muted);
        }
    }
}
