using System;
using System.Linq;

namespace WorstHotel
{
    public sealed partial class GuestServiceSystem
    {
        GuestContactChannel ChooseChannel(GuestResponse response, GuestStay guest)
        {
            if (guest.Agent.State == GuestAgentState.WaitingForCheckIn) return GuestContactChannel.Reception;
            // Stable preference: business guests tend to call; some other guests visit the desk.
            uint hash = 2166136261;
            unchecked { foreach (char c in guest.GuestId) hash = (hash ^ c) * 16777619; }
            return guest.Application.Archetype.Kind == GuestKind.Business || hash % 3 != 0 ?
                GuestContactChannel.Phone : GuestContactChannel.Reception;
        }

        bool PrepareContact(GuestResponse response, GuestStay guest, float now)
        {
            if (response.ServiceCaseId == null && !IsSerious(response))
            {
                var incident = Incident(response);
                ServiceKind kind;
                if (incident?.Reason == IncidentReason.Temperature) kind = ServiceKind.ExtraBlanket;
                else if (incident?.Reason == IncidentReason.Noise) kind = ServiceKind.AskNeighborsQuiet;
                else return false;
                if (!Eligible(guest, kind) || !BudgetAvailable(guest) || cases.Count >= CaseCapacity ||
                    cases.Any(item => item.GuestId == guest.GuestId && item.Active) ||
                    !TryCause(guest, kind, now, out string source, out int room, out float due, out string text, true) || source != response.SourceEntityId)
                    return false;
                if (TryCreate(guest, kind, now, due, source, room, text) == null) return false;
            }
            var request = FindCase(response.ServiceCaseId);
            return IsSerious(response) || request?.Active == true && (request.BudgetCharged || BudgetAvailable(guest));
        }

        bool BeginContact(GuestResponse response, GuestStay guest, GuestContactChannel channel, float now)
        {
            if (simulation.EarlyCheckoutDecisionPending(guest)) return false;
            if (response.ContactAttempts >= Settings.MaxContactAttempts || !CausePresent(response, guest, now) ||
                !CanInterrupt(guest) || !ContactWindowAvailable(guest, now) || guest.Agent.ResponseActionId != null || !PrepareContact(response, guest, now)) return false;
            if (channel == GuestContactChannel.Phone && (IncomingCall != null || !guest.Agent.InAssignedRoom)) return false;
            response.Channel = channel; response.AttemptStartedAt = response.AttemptDeadline = -1;
            SetPhase(response, GuestResponsePhase.Contacting, now);
            simulation.StartGuestResponseAction(guest, response,
                channel == GuestContactChannel.Phone ? GuestResponseAnchor.RoomPhone : GuestResponseAnchor.Reception);
            // Initial check-in waiting already acknowledges a real reception arrival. There is no
            // second imaginary journey; disclosure still requires the player's conversation.
            if (guest.Agent.State == GuestAgentState.WaitingForCheckIn) StartAttempt(response, guest, now);
            return true;
        }

        bool StartAttempt(GuestResponse response, GuestStay guest, float now)
        {
            if (response.AttemptStartedAt >= 0 || response.ContactAttempts >= Settings.MaxContactAttempts ||
                !CausePresent(response, guest, now)) return false;
            if (response.Channel == GuestContactChannel.Phone && IncomingCall != null && IncomingCall != response) return false;
            if (!EnsureContactAllowance(response, guest, now, true)) return false;
            response.ContactAttempts++; response.AttemptStartedAt = now;
            response.AttemptDeadline = now + (response.Channel == GuestContactChannel.Phone ? Settings.PhoneRingSeconds : Settings.ReceptionWaitSeconds);
            response.RetryAt = -1;
            simulation.SignalEvent(response.Channel == GuestContactChannel.Phone ? "The reception telephone is ringing." : "A guest would like to speak at reception.");
            return true;
        }

        bool EnsureContactAllowance(GuestResponse response, GuestStay guest, float now, bool charge)
        {
            var request = FindCase(response.ServiceCaseId);
            if (request == null || request.BudgetCharged) return true;
            if (request.Active && BudgetAvailable(guest))
            { if (charge) ChargeBudget(request); return true; }
            // A serious factual complaint is independent of the optional service allowance.
            // Keep its response identity, but never grant an uncharged soft agreement.
            if (IsSerious(response))
            { Finish(request, guest, ServiceStatus.Escalated, 0); return true; }
            Finish(request, guest, ServiceStatus.Expired, 0);
            Cancel(response, guest, now);
            return false;
        }

        void MissContact(GuestResponse response, GuestStay guest, float now)
        {
            response.AttemptStartedAt = response.AttemptDeadline = -1;
            response.RetryAt = now + Settings.ContactRetryDelaySeconds;
            SetPhase(response, response.ContactAttempts < Settings.MaxContactAttempts ? GuestResponsePhase.WaitingToContact : GuestResponsePhase.Cancelled, now);
            simulation.ClearGuestResponseAction(guest, true);
        }

        void Cancel(GuestResponse response, GuestStay guest, float now)
        {
            SetPhase(response, GuestResponsePhase.Cancelled, now);
            response.AttemptStartedAt = response.AttemptDeadline = response.RetryAt = -1;
            if (guest.Agent.ResponseActionId == response.Id) simulation.ClearGuestResponseAction(guest, true);
            var request = FindCase(response.ServiceCaseId);
            if (request?.Active == true && response.IncidentId == null) Finish(request, guest, ServiceStatus.Expired, 0);
        }

        internal CommandResult AnchorReached(GuestStay guest, GuestResponse response, GuestResponseAnchor anchor)
        {
            float now = simulation.Elapsed;
            if (anchor == GuestResponseAnchor.AssignedRoom)
            { simulation.CompleteGuestResponseReturn(guest); return CommandResult.Ok("Guest returned to the assigned room."); }
            if (!CausePresent(response, guest, now))
            { Cancel(response, guest, now); return CommandResult.Fail("The original context has recovered or ended."); }
            if (anchor == GuestResponseAnchor.Radiator)
            {
                if (response.Phase != GuestResponsePhase.SelfResponding || response.SelfResponseAttempted)
                    return CommandResult.Fail("This radiator response has already ended.");
                bool apply = CanSelfRespond(response, guest);
                response.SelfResponseAttempted = true; response.SelfResponseAt = now;
                if (apply)
                { simulation.ApplyRadiatorSetting(rooms[guest.RoomId], rooms[guest.RoomId].RadiatorSetting + 1); response.SelfResponseApplied = true; }
                SetPhase(response, GuestResponsePhase.Tolerating, now);
                simulation.ClearGuestResponseAction(guest, true);
                return CommandResult.Ok(apply ? "Guest adjusted the room's actual radiator valve." : "Guest found no useful radiator adjustment.");
            }
            if (response.Phase != GuestResponsePhase.Contacting || response.AttemptStartedAt >= 0)
                return CommandResult.Fail("This contact is no longer awaiting physical arrival.");
            if (!StartAttempt(response, guest, now)) return CommandResult.Fail("The reception telephone is busy or the contact is no longer eligible.");
            if (anchor == GuestResponseAnchor.Reception) simulation.WaitGuestAtServiceReception(guest);
            else guest.Agent.ActivityStaged = true;
            return CommandResult.Ok("Guest has physically started contacting the hotel.");
        }

        internal CommandResult Communicate(int actor, string responseId, GuestContactChannel channel)
        {
            var allowed = CanAct(actor); if (!allowed.Success) return allowed;
            if (!NaturalCommunicationEnabled) return CommandResult.Fail("Natural guest communication is not enabled.");
            var response = FindResponse(responseId); var guest = response == null ? null : Guest(response.GuestId);
            if (response == null || guest == null || response.CommunicatedAt >= 0 || !CausePresent(response, guest, simulation.Elapsed))
                return CommandResult.Fail("There is no undisclosed current concern to discuss.");
            if (guest.Agent.ResponseActionId != null && guest.Agent.ResponseActionId != response.Id)
                return CommandResult.Fail("Let the guest finish the current response before discussing another concern.");
            if (channel == GuestContactChannel.Phone && (IncomingCall != response || simulation.Elapsed >= response.AttemptDeadline))
                return CommandResult.Fail("That guest is not currently ringing reception.");
            if (channel == GuestContactChannel.Reception && (response.Channel != channel || response.AttemptStartedAt < 0 ||
                simulation.Elapsed >= response.AttemptDeadline || guest.Agent.State != GuestAgentState.WaitingAtServiceReception &&
                guest.Agent.State != GuestAgentState.WaitingForCheckIn)) return CommandResult.Fail("Speak to the guest physically waiting at reception.");
            if (channel == GuestContactChannel.RoomConversation)
            {
                if (!guest.Agent.InAssignedRoom || guest.Agent.State == GuestAgentState.Sleeping || guest.Agent.Activity == GuestActivity.Shower ||
                    guest.Agent.IsRelocating || response.DwellSeconds < Settings.ObservationSeconds || !PrepareContact(response, guest, simulation.Elapsed))
                    return CommandResult.Fail("The guest is not available to discuss this concern in their room.");
                if (!EnsureContactAllowance(response, guest, simulation.Elapsed, true))
                    return CommandResult.Fail("This guest has no additional low-service contact.");
            }
            else if (channel != GuestContactChannel.Phone && channel != GuestContactChannel.Reception)
                return CommandResult.Fail("Choose an actual conversation channel.");
            if (!CanCommunicateIntent(guest, FindCase(response.ServiceCaseId)))
                return CommandResult.Fail("Finish the current direct service decision before starting another.");
            response.Channel = channel; response.CommunicatedAt = simulation.Elapsed;
            response.AttemptStartedAt = response.AttemptDeadline = response.RetryAt = -1;
            SetPhase(response, GuestResponsePhase.Communicated, simulation.Elapsed);
            var item = FindCase(response.ServiceCaseId);
            if (item != null)
            { if (item.BudgetCharged) guest.Memory.ServicesRequested = Count(guest.Memory.ServicesRequested); Changed?.Invoke(item); }
            if (response.IncidentId != null) simulation.Incidents.MarkCommunicated(guest, response.IncidentId, response.IncidentEpisode, simulation.Elapsed);
            BeginCaseIntent(guest, item);
            // Only this actual conversation may hold a guest for a compensation choice.
            // Merely observing a Remote cause never pauses their ordinary schedule.
            if (IntentBehaviorEnabled && DirectIntent(guest.GuestId) == null && response.IncidentId != null)
                BeginCompensationDiscussion(actor, guest.GuestId, response.IncidentId);
            if (DirectIntent(guest.GuestId) == null) simulation.ClearGuestResponseAction(guest, true);
            var reason = Incident(response)?.Reason;
            string concern = reason == IncidentReason.Temperature ? "The room temperature is uncomfortable." :
                reason == IncidentReason.Noise ? "Noise from nearby is disturbing the guest." : "The guest reported a problem with the room.";
            string explanation = item?.BudgetCharged == true ? item.Description : concern;
            simulation.SignalEvent("Room " + guest.RoomId + ": " + explanation);
            return CommandResult.Ok(explanation);
        }

        public CommandResult DebugBeginSelfResponse(string guestId)
        {
            var allowed = CanAct(0); if (!allowed.Success) return allowed;
            var guest = Guest(guestId);
            var response = responses.FirstOrDefault(item => item.GuestId == guestId && item.CommunicatedAt < 0 && CanSelfRespond(item, guest));
            if (!NaturalCommunicationEnabled || guest == null || response == null || guest.Agent.ResponseActionId != null || !CanInterrupt(guest))
                return CommandResult.Fail("Choose a present guest with actual mild cold and an unused radiator response.");
            BeginSelfResponse(response, guest, simulation.Elapsed); return CommandResult.Ok("Guest will walk to the real radiator.");
        }

        public CommandResult DebugBeginContact(string guestId, GuestContactChannel channel)
        {
            var allowed = CanAct(0); if (!allowed.Success) return allowed;
            if (!NaturalCommunicationEnabled || channel != GuestContactChannel.Phone && channel != GuestContactChannel.Reception)
                return CommandResult.Fail("Choose the room phone or reception route.");
            var guest = Guest(guestId);
            if (guest == null) return CommandResult.Fail("Choose a current guest.");
            var response = responses.Where(item => item.GuestId == guestId && item.CommunicatedAt < 0 && CausePresent(item, guest, simulation.Elapsed))
                .OrderByDescending(IsSerious).ThenBy(item => item.CreatedAt).FirstOrDefault();
            return response != null && BeginContact(response, guest, channel, simulation.Elapsed) ?
                CommandResult.Ok("Guest will physically contact the hotel.") : CommandResult.Fail("No valid current concern can begin that contact.");
        }

        public CommandResult DebugCancelContact(string responseId)
        {
            var allowed = CanAct(0); if (!allowed.Success) return allowed;
            var response = FindResponse(responseId);
            if (response == null || response.CommunicatedAt >= 0) return CommandResult.Fail("Choose an uncommunicated response.");
            Cancel(response, Guest(response.GuestId), simulation.Elapsed); return CommandResult.Ok("Contact cancelled; a guest at reception will return normally.");
        }
    }
}
