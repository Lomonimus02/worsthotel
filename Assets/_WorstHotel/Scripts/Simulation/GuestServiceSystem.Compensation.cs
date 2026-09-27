using System;
using System.Linq;

namespace WorstHotel
{
    public sealed partial class GuestServiceSystem
    {
        public GuestServiceIntent CompensationDiscussion(string guestId)
        {
            var direct = DirectIntent(guestId);
            return direct?.Purpose == ServiceIntentPurpose.CompensationDiscussion ? direct : null;
        }

        bool CompensationPresence(GuestStay guest)
        {
            var agent = guest?.Agent;
            if (agent == null || !agent.CheckedIn || !agent.HasReachedRoom || agent.IsRelocating || Departed(guest) ||
                agent.State == GuestAgentState.Sleeping || agent.Activity == GuestActivity.Shower ||
                !rooms.TryGetValue(guest.RoomId, out var room) || room.GuestId != guest.GuestId) return false;
            return agent.InAssignedRoom || agent.State == GuestAgentState.WaitingAtServiceReception;
        }

        HotelIncident CompensableIncident(GuestStay guest, string incidentId = null) => guest == null ? null :
            simulation.Incidents.Items.Where(item => item.GuestId == guest.GuestId && item.Active && item.HasContactedStaff &&
                (incidentId == null || item.Id == incidentId) &&
                (!guest.Compensated || !item.AttentionAcknowledged))
                .OrderByDescending(item => item.Stage).ThenBy(item => item.Id, StringComparer.Ordinal).FirstOrDefault();

        public CommandResult BeginCompensationDiscussion(int actorId, string guestId, string incidentId = null)
        {
            var allowed = CanAct(actorId); if (!allowed.Success) return allowed;
            if (!IntentBehaviorEnabled) return CommandResult.Fail("Bounded compensation discussions require continuous operations.");
            var guest = Guest(guestId);
            if (!CompensationPresence(guest))
                return CommandResult.Fail("Speak with an awake current guest before discussing compensation.");
            var current = DirectIntent(guestId);
            if (current != null)
            {
                if (current.Purpose != ServiceIntentPurpose.CompensationDiscussion)
                    return CommandResult.Fail("Finish the guest's current direct service decision first.");
                if (simulation.Elapsed >= current.Deadline || incidentId != null && current.IncidentId != incidentId ||
                    CompensableIncident(guest, current.IncidentId) == null)
                    return CommandResult.Fail("That compensation discussion is no longer current.");
                // A repeated physical open or another staff member never extends this wait.
                return CommandResult.Ok("The guest is still waiting for the compensation decision.");
            }
            // This dialogue promises an item that can be left while the guest continues life.
            // Do not turn its underlying Remote temperature cause into a mandatory Direct wait.
            if (DropOffIntent(guestId) != null)
                return CommandResult.Fail("The current blanket agreement can be delivered without another direct discussion.");
            var incident = CompensableIncident(guest, incidentId);
            if (incident == null) return CommandResult.Fail("This guest has no reported current problem to compensate.");
            var response = incident.Response;
            if (guest.Agent.ResponseActionId != null && (response == null || guest.Agent.ResponseActionId != response.Id ||
                    response.CommunicatedAt < 0))
                return CommandResult.Fail("Let the guest finish their current physical response first.");
            if (guest.Agent.CheckoutTime - simulation.Elapsed <= Settings.ContactLeadSeconds || intents.Count >= 1024)
                return CommandResult.Fail("There is no available wait for another compensation discussion.");
            int ordinal = 1;
            while (FindIntent(guestId + "/compensation/" + ordinal) != null) ordinal++;
            var intent = AddIntent(guestId + "/compensation/" + ordinal, guest, ServiceIntentKind.Direct,
                ServiceIntentPurpose.CompensationDiscussion);
            if (intent == null) return CommandResult.Fail("Service history capacity reached.");
            intent.IncidentId = incident.Id; intent.ResponseId = response?.Id;
            intent.Deadline = Math.Min(guest.Agent.CheckoutTime, simulation.Elapsed + Settings.DirectWaitSeconds);
            guest.Agent.DirectServiceIntentId = intent.Id;
            simulation.HoldGuestForServiceIntent(guest);
            return CommandResult.Ok("The guest will wait briefly for a compensation decision. The room problem remains.");
        }

        internal bool CanResolveCompensationDiscussion(GuestStay guest)
        {
            var intent = guest == null ? null : CompensationDiscussion(guest.GuestId);
            return intent != null && simulation.Elapsed < intent.Deadline &&
                CompensationPresence(guest) && CompensableIncident(guest, intent.IncidentId) != null;
        }

        internal bool CanCompensate(GuestStay guest) => guest != null && !guest.Compensated && CanResolveCompensationDiscussion(guest);

        internal void FinishCompensationDiscussion(GuestStay guest, bool credited, string reason, bool resume = true)
        {
            var intent = guest == null ? null : CompensationDiscussion(guest.GuestId);
            if (intent != null) CloseIntent(intent, credited ? ServiceIntentStatus.Completed : ServiceIntentStatus.Cancelled, reason, resume);
        }

        public CommandResult EndCompensationDiscussion(int actorId, string guestId, string expectedIntentId, int expectedRevision)
        {
            var allowed = CanAct(actorId); if (!allowed.Success) return allowed;
            var intent = CompensationDiscussion(guestId);
            if (intent == null || intent.Id != expectedIntentId || intent.Revision != expectedRevision || simulation.Elapsed >= intent.Deadline)
                return CommandResult.Fail("This compensation discussion changed or already ended.");
            FinishCompensationDiscussion(Guest(guestId), false, "Discussion ended without compensation");
            return CommandResult.Ok("Discussion ended. The underlying room problem remains.");
        }
    }
}
