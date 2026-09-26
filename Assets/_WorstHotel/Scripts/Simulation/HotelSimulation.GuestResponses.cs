using System;
using System.Linq;

namespace WorstHotel
{
    public sealed partial class HotelSimulation
    {
        public CommandResult AnswerIncomingServiceCall(int actorId, string responseId) =>
            Services?.Communicate(actorId, responseId, GuestContactChannel.Phone) ?? ServicesDisabled();
        public CommandResult TalkToServiceGuest(int actorId, string guestId, string responseId) =>
            Services == null || guestId == null || Services.FindResponse(responseId)?.GuestId != guestId ? CommandResult.Fail("This concern belongs to another guest.") :
            Services.Communicate(actorId, responseId, GuestContactChannel.Reception);
        public CommandResult DiscussRoomConcern(int actorId, string guestId, string responseId) =>
            Services == null || guestId == null || Services.FindResponse(responseId)?.GuestId != guestId ? CommandResult.Fail("This concern belongs to another guest.") :
            Services.Communicate(actorId, responseId, GuestContactChannel.RoomConversation);
        public CommandResult DebugBeginGuestSelfResponse(string guestId) => Services?.DebugBeginSelfResponse(guestId) ?? ServicesDisabled();
        public CommandResult DebugBeginGuestContact(string guestId, GuestContactChannel channel) => Services?.DebugBeginContact(guestId, channel) ?? ServicesDisabled();
        public CommandResult DebugCancelGuestContact(string responseId) => Services?.DebugCancelContact(responseId) ?? ServicesDisabled();
        public CommandResult DebugForceSevereCold(string guestId)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            var guest = guests.FirstOrDefault(item => item.GuestId == guestId);
            if (!Running || Services == null || guest?.Agent?.InAssignedRoom != true)
                return CommandResult.Fail("Choose a guest physically inside their room.");
            var result = SetRoomTemperature(guest.RoomId, guest.Application.Archetype.Needs.PreferredTemperatureMin - 8);
            if (result.Success) NeedEvaluator.Tick(guest, rooms[guest.RoomId], .001f);
            return result.Success ? CommandResult.Ok("Actual severe cold applied. The guest still observes, reacts and contacts staff through the normal gates.") : result;
        }
        public CommandResult DebugForceGuestEscalation(string guestId) => DebugForceSevereCold(guestId);
        public CommandResult DebugForceNoiseExposure(string guestId)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            var guest = guests.FirstOrDefault(item => item.GuestId == guestId);
            if (!Running || Noise == null || guest?.Agent?.InAssignedRoom != true)
                return CommandResult.Fail("Choose a guest physically inside their room.");
            var source = guests.Where(item => item.GuestId != guestId && item.Agent?.InAssignedRoom == true &&
                item.Agent.ResponseActionId == null && rooms[item.RoomId].HasPower &&
                Noise.Graph.Links.Any(link => link.RoomA == guest.RoomId && link.RoomB == item.RoomId ||
                    link.RoomB == guest.RoomId && link.RoomA == item.RoomId))
                .OrderBy(item => item.GuestId, StringComparer.Ordinal).FirstOrDefault();
            if (source == null) return CommandResult.Fail("There is no real nearby guest available to produce noise.");
            var result = ForceActivity(source.GuestId, GuestActivity.LoudRoom);
            return result.Success ? CommandResult.Ok("A real nearby guest started television. Exposure begins after their physical activity staging.") : result;
        }

        internal void StartGuestResponseAction(GuestStay guest, GuestResponse response, GuestResponseAnchor anchor)
        {
            var agent = guest.Agent; response.ActionVersion++;
            agent.ResponseActionId = response.Id; agent.ResponseActionVersion = response.ActionVersion;
            if (anchor == GuestResponseAnchor.Reception && agent.State == GuestAgentState.WaitingForCheckIn) return;
            if (anchor == GuestResponseAnchor.Reception)
            {
                agent.NoiseOutput = agent.HeatingDemandMultiplier = 0;
                agent.ActivityEndsAt = agent.NextActivityTime = float.PositiveInfinity;
                Transition(guest, GuestAgentState.GoingToServiceReception, Elapsed, null);
            }
            else
            {
                SetActivity(guest, anchor == GuestResponseAnchor.Radiator ? GuestActivity.AdjustRadiator : GuestActivity.CallReception,
                    Elapsed, float.PositiveInfinity);
                // Even headless callers must explicitly acknowledge this causal physical action.
                agent.ActivityStaged = false;
            }
            RefreshGuestLoad(); RefreshElectrical();
        }

        internal void WaitGuestAtServiceReception(GuestStay guest) => Transition(guest, GuestAgentState.WaitingAtServiceReception, Elapsed, null);

        internal void ClearGuestResponseAction(GuestStay guest, bool resume)
        {
            var agent = guest.Agent;
            if (agent.ResponseActionId == null) return;
            if (resume && agent.IsServiceReceptionTrip && agent.CheckedIn)
            {
                if (agent.State == GuestAgentState.ReturningFromServiceReception) return;
                var response = Services.FindResponse(agent.ResponseActionId);
                response.ActionVersion++; agent.ResponseActionVersion = response.ActionVersion;
                Transition(guest, GuestAgentState.ReturningFromServiceReception, Elapsed, null);
                return;
            }
            agent.ResponseActionId = null; agent.ResponseActionVersion = 0;
            if (!resume && (agent.Activity == GuestActivity.AdjustRadiator || agent.Activity == GuestActivity.CallReception))
            { agent.Activity = GuestActivity.QuietRest; agent.ActivityStaged = true; }
            if (resume && agent.InAssignedRoom) SetActivity(guest, GuestActivity.QuietRest, Elapsed, LivingSettings.QuietDurationMin);
            RefreshGuestLoad(); RefreshElectrical(); RefreshRoomPresence();
        }

        internal void CompleteGuestResponseReturn(GuestStay guest)
        {
            guest.Agent.ResponseActionId = null; guest.Agent.ResponseActionVersion = 0;
            SetActivity(guest, GuestActivity.QuietRest, Elapsed, LivingSettings.QuietDurationMin);
            RefreshGuestLoad(); RefreshElectrical(); RefreshRoomPresence();
        }

        public CommandResult SignalGuestResponseAnchorReached(string guestId, string responseId, int actionVersion, GuestResponseAnchor anchor)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            var guest = guests.FirstOrDefault(item => item.GuestId == guestId);
            var response = Services?.FindResponse(responseId); var agent = guest?.Agent;
            if (!Running || !Enum.IsDefined(typeof(GuestResponseAnchor), anchor) || response == null || agent == null ||
                response.GuestId != guestId || agent.ResponseActionId != responseId || agent.ResponseActionVersion != actionVersion ||
                response.ActionVersion != actionVersion || response.RoomId != guest.RoomId)
                return CommandResult.Fail("This physical response action is stale or belongs to another guest.");
            if (anchor == GuestResponseAnchor.AssignedRoom)
            {
                if (agent.State != GuestAgentState.ReturningFromServiceReception || rooms[guest.RoomId].GuestId != guestId)
                    return CommandResult.Fail("This guest is not returning to the owned room.");
            }
            else if (anchor == GuestResponseAnchor.Reception)
            {
                if (agent.State != GuestAgentState.GoingToServiceReception || response.Channel != GuestContactChannel.Reception)
                    return CommandResult.Fail("This guest is not travelling to reception.");
            }
            else if (!agent.InAssignedRoom || rooms[guest.RoomId].GuestId != guestId ||
                anchor == GuestResponseAnchor.Radiator && agent.Activity != GuestActivity.AdjustRadiator ||
                anchor == GuestResponseAnchor.RoomPhone && (agent.Activity != GuestActivity.CallReception || response.Channel != GuestContactChannel.Phone))
                return CommandResult.Fail("The response did not reach its own room anchor.");
            return Services.AnchorReached(guest, response, anchor);
        }
    }
}
