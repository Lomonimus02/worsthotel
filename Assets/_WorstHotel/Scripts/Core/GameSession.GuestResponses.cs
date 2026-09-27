using System.Linq;
using UnityEngine;

namespace WorstHotel
{
    public sealed partial class GameSession
    {
        public CommandResult AnswerIncomingServiceCall(int actor, string responseId)
        {
            if (ForwardLan(LanCommandKind.AnswerServiceCall, responseId)) return CommandResult.Ok(LastMessage);
            var grant = actor >= 0 && actor < phoneGrants.Length ? phoneGrants[actor] : null;
            if (grant == null || grant.model != Simulation || !grant.source ||
                Time.unscaledTime > grant.expires || Phase != DayPhase.Service ||
                !PlayerInteractor.TryGetPlayer(actor, out var player) ||
                Vector3.Distance(player.transform.position, grant.source.position) > 4)
                return GuestCommand(CommandResult.Fail("Answer using the physical reception telephone."));
            var result = Simulation.AnswerIncomingServiceCall(actor, responseId);
            if (result.Success)
            {
                var response = Simulation.Services.FindResponse(responseId);
                grant.answeredGuestId = response?.GuestId;
                grant.answeredResponseId = response?.Id;
            }
            return GuestCommand(result);
        }

        public CommandResult TalkToServiceGuest(int actor, string guestId, string responseId)
        {
            if (ForwardLan(LanCommandKind.TalkServiceGuest, responseId)) return CommandResult.Ok(LastMessage);
            if (!HasGuestConversation(actor, guestId) || conversations[actor].throughDoor ||
                Simulation.Services?.FindResponse(responseId)?.GuestId != guestId)
                return GuestCommand(CommandResult.Fail("Speak with the waiting guest at reception."));
            return GuestCommand(Simulation.TalkToServiceGuest(actor, guestId, responseId));
        }

        public CommandResult DiscussRoomConcern(int actor, string guestId, string responseId)
        {
            if (ForwardLan(LanCommandKind.DiscussRoomConcern, responseId)) return CommandResult.Ok(LastMessage);
            if (!HasGuestConversation(actor, guestId) || Simulation.Services?.FindResponse(responseId)?.GuestId != guestId)
                return GuestCommand(CommandResult.Fail("Speak with the guest in their room or at the answered door."));
            return GuestCommand(Simulation.DiscussRoomConcern(actor, guestId, responseId));
        }

        // Invoked only after OpenGuestConversation has validated the real body/answered door.
        void DiscloseGuestConcern(int actor, GuestStay guest)
        {
            var response = Simulation.Services?.Responses.Where(r => r.GuestId == guest.GuestId &&
                r.CommunicatedAt < 0 && r.Phase != GuestResponsePhase.Cancelled)
                .OrderByDescending(r => r.Phase == GuestResponsePhase.Contacting).ThenBy(r => r.CreatedAt).FirstOrDefault();
            if (response == null) return;
            if (guest.Agent.State == GuestAgentState.WaitingAtServiceReception || guest.Agent.State == GuestAgentState.WaitingForCheckIn)
                Simulation.TalkToServiceGuest(actor, guest.GuestId, response.Id);
            else if (guest.Agent.InAssignedRoom)
                Simulation.DiscussRoomConcern(actor, guest.GuestId, response.Id);
        }

        public CommandResult DebugBeginGuestSelfResponse(string guestId) => GuestCommand(Simulation.DebugBeginGuestSelfResponse(guestId));
        public CommandResult DebugBeginGuestContact(string guestId, GuestContactChannel channel) => GuestCommand(Simulation.DebugBeginGuestContact(guestId, channel));
        public CommandResult DebugCancelGuestContact(string responseId) => GuestCommand(Simulation.DebugCancelGuestContact(responseId));
        public CommandResult DebugForceSevereCold(string guestId) => GuestCommand(Simulation.DebugForceSevereCold(guestId));
        public CommandResult DebugForceNoiseExposure(string guestId) => GuestCommand(Simulation.DebugForceNoiseExposure(guestId));
        public CommandResult DebugForceGuestEscalation(string guestId) => GuestCommand(Simulation.DebugForceGuestEscalation(guestId));
    }
}
