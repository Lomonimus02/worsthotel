using System.Linq;
using UnityEngine;

namespace WorstHotel
{
    public sealed partial class GameSession
    {
        sealed class ConversationGrant
        {
            public HotelSimulation model;
            public string guestId;
            public int roomId;
            public bool throughDoor;
            public Transform source;
            public float expires;
        }
        readonly ConversationGrant[] conversations = new ConversationGrant[2];
        float ConversationLifetime => Simulation.ContinuousOperations ?
            Mathf.Max(30, (Simulation.Services?.Settings.DirectWaitSeconds ?? 30) + 5) : 30;

        internal void ClearGuestConversation(int actorId)
        {
            if (actorId >= 0 && actorId < conversations.Length) conversations[actorId] = null;
        }

        // Only a physical host-side interaction creates a grant. Opening a UI never grants authority.
        public CommandResult OpenGuestConversation(int actorId, string guestId, bool throughDoor = false)
        {
            if (IsLanReplica || Phase != DayPhase.Service || actorId < 0 || actorId >= conversations.Length ||
                !PlayerInteractor.TryGetPlayer(actorId, out var player) || !player.CanAct)
                return CommandResult.Fail("Approach the guest or knock at their room first.");
            var guest = Simulation.Guests.FirstOrDefault(g => g.GuestId == guestId);
            if (guest?.Agent == null || !(guest.Agent.InAssignedRoom || !throughDoor &&
                (guest.Agent.State == GuestAgentState.WaitingAtServiceReception ||
                 guest.Agent.State == GuestAgentState.WaitingForCheckIn)))
                return CommandResult.Fail("The guest is not available in the room.");
            RoomNoiseInteraction door = player.Focused as RoomNoiseInteraction;
            if (!door && player.Focused is DoorInteractable roomDoor) door = roomDoor.Conversation;
            var body = player.Focused as GuestReceptionInteraction;
            Transform source = null;
            if (throughDoor && door && door.roomId == guest.RoomId && door.HasAnswered(actorId)) source = door.transform;
            if (!throughDoor && body && body.GuestId == guestId) source = body.transform;
            if (!source || Vector3.Distance(player.transform.position, source.position) > 4)
                return CommandResult.Fail("Stay near the guest to speak with them.");
            var lan = LanSession.Instance;
            if (!(lan && lan.Role == LanRole.Host && actorId == 1) && ManagementUI.Instance?.IsOpen == true)
                ManagementUI.Instance.Close();
            conversations[actorId] = new ConversationGrant { model = Simulation, guestId = guestId,
                roomId = guest.RoomId, throughDoor = throughDoor, source = source, expires = Time.unscaledTime + ConversationLifetime };
            DiscloseGuestConcern(actorId, guest);
            if (Simulation.ContinuousOperations) Simulation.BeginCompensationDiscussion(actorId, guestId);
            if (lan && lan.Role == LanRole.Host && actorId == 1) lan.RequestRemoteGuestConversation(guestId, throughDoor);
            else ManagementUI.Instance?.OpenGuestContext(actorId, guestId, throughDoor);
            return CommandResult.Ok("Speaking with " + guest.Name + ".");
        }

        public void CloseGuestConversation(int actorId, string guestId)
        {
            if (string.IsNullOrEmpty(guestId)) return;
            if (ForwardLan(LanCommandKind.CloseGuestConversation, guestId)) return;
            if (actorId >= 0 && actorId < conversations.Length && conversations[actorId]?.guestId == guestId)
                conversations[actorId] = null;
        }

        public bool HasGuestConversation(int actorId, string guestId)
        {
            if (IsLanReplica || actorId < 0 || actorId >= conversations.Length) return false;
            var grant = conversations[actorId];
            if (grant == null || grant.model != Simulation || grant.guestId != guestId || !grant.source ||
                Time.unscaledTime > grant.expires || Phase != DayPhase.Service ||
                !PlayerInteractor.TryGetPlayer(actorId, out var player) ||
                Vector3.Distance(player.transform.position, grant.source.position) > 4) return false;
            var guest = Simulation.Guests.FirstOrDefault(g => g.GuestId == guestId);
            return guest?.Agent != null && (guest.Agent.InAssignedRoom || !grant.throughDoor &&
                (guest.Agent.State == GuestAgentState.WaitingAtServiceReception ||
                 guest.Agent.State == GuestAgentState.ReturningFromServiceReception ||
                 guest.Agent.State == GuestAgentState.WaitingForCheckIn)) && guest.RoomId == grant.roomId;
        }

        public CommandResult RequestGuestRoomEntry(int actorId, string guestId)
        {
            if (ForwardLan(LanCommandKind.RequestGuestRoomEntry, guestId)) return CommandResult.Ok(LastMessage);
            if (!HasGuestConversation(actorId, guestId) || !conversations[actorId].throughDoor)
                return GuestCommand(CommandResult.Fail("Knock and ask the guest for permission first."));
            var grant = conversations[actorId];
            var result = Simulation.RequestStaffRoomAccess(actorId, grant.roomId);
            if (!result.Success) return GuestCommand(result);
            var door = grant.source.GetComponentInParent<DoorInteractable>();
            if (!door || !PlayerInteractor.TryGetPlayer(actorId, out var player))
                return GuestCommand(CommandResult.Fail("The room entrance is no longer available."));
            conversations[actorId] = null;
            // Remote actor input remains menu-blocked until its next packet; permission is already host validated.
            door.OpenForAuthorizedConversation(player);
            return GuestCommand(result);
        }
    }
}
