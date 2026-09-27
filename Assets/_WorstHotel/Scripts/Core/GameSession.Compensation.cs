using UnityEngine;

namespace WorstHotel
{
    public sealed partial class GameSession
    {
        public CommandResult OfferCompensation(int actorId, string guestId)
        {
            var intent = Simulation?.ContinuousOperations == true ? Simulation.Services?.CompensationDiscussion(guestId) : null;
            return OfferCompensation(actorId, guestId, intent?.Id, intent?.Revision ?? -1);
        }

        public CommandResult OfferCompensation(int actorId, string guestId, string expectedIntentId, int expectedIntentRevision)
        {
            if (ForwardLan(LanCommandKind.OfferCredit, guestId, directIntentId: expectedIntentId, directIntentRevision: expectedIntentRevision))
                return CommandResult.Ok(LastMessage);
            var valid = ValidateCompensationDecision(actorId, guestId, expectedIntentId, expectedIntentRevision);
            if (!valid.Success) return GuestCommand(valid);
            return GuestCommand(Simulation.OfferCompensation(guestId));
        }

        public CommandResult AcceptConsequences(int actorId, string guestId)
        {
            var intent = Simulation?.ContinuousOperations == true ? Simulation.Services?.CompensationDiscussion(guestId) : null;
            return AcceptConsequences(actorId, guestId, intent?.Id, intent?.Revision ?? -1);
        }

        public CommandResult AcceptConsequences(int actorId, string guestId, string expectedIntentId, int expectedIntentRevision)
        {
            if (ForwardLan(LanCommandKind.AcceptConsequences, guestId, directIntentId: expectedIntentId, directIntentRevision: expectedIntentRevision))
                return CommandResult.Ok(LastMessage);
            var valid = ValidateCompensationDecision(actorId, guestId, expectedIntentId, expectedIntentRevision);
            if (!valid.Success) return GuestCommand(valid);
            return GuestCommand(Simulation.AcceptConsequences(actorId, guestId));
        }

        CommandResult ValidateCompensationDecision(int actorId, string guestId, string expectedIntentId, int expectedIntentRevision)
        {
            if (actorId < 0 || actorId > 1 || Phase != DayPhase.Service)
                return CommandResult.Fail("A guest discussion requires active staff during service.");
            if (!Simulation.ContinuousOperations) return CommandResult.Ok("Legacy guest response.");
            var staff = LocalCoopBootstrap.Instance;
            if (!staff || actorId >= staff.Players.Length || !staff.Players[actorId] ||
                !staff.Players[actorId].DeviceReady || staff.IsPaused)
                return CommandResult.Fail("An active staff member must finish the guest discussion.");
            var intent = Simulation.Services?.CompensationDiscussion(guestId);
            if (intent == null || intent.Id != expectedIntentId || intent.Revision != expectedIntentRevision)
                return CommandResult.Fail("That discussion has changed or ended. Speak with the guest again.");
            if (!HasGuestConversation(actorId, guestId) && !HasAnsweredPhoneConversation(actorId, guestId, intent.ResponseId))
                return CommandResult.Fail("Discuss this with the guest in person or on their answered reception call.");
            return CommandResult.Ok("Current guest discussion.");
        }

        bool HasAnsweredPhoneConversation(int actorId, string guestId, string responseId)
        {
            var grant = actorId >= 0 && actorId < phoneGrants.Length ? phoneGrants[actorId] : null;
            return grant != null && grant.model == Simulation && grant.source && grant.answeredGuestId == guestId &&
                !string.IsNullOrEmpty(responseId) && grant.answeredResponseId == responseId &&
                Time.unscaledTime <= grant.expires && PlayerInteractor.TryGetPlayer(actorId, out var player) &&
                Vector3.Distance(player.transform.position, grant.source.position) <= 4;
        }

        // Ending one call revokes its guest authority, but keeps the physical handset available for outgoing wake calls.
        public void EndServicePhoneConversation(int actorId, string guestId)
        {
            if (ForwardLan(LanCommandKind.EndServicePhoneConversation, guestId)) return;
            var grant = actorId >= 0 && actorId < phoneGrants.Length ? phoneGrants[actorId] : null;
            if (grant == null || grant.answeredGuestId != guestId) return;
            grant.answeredGuestId = null; grant.answeredResponseId = null;
        }
    }
}
