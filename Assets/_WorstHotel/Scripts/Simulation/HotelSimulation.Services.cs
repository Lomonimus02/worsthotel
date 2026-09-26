using System.Linq;

namespace WorstHotel
{
    public sealed partial class HotelSimulation
    {
        public GuestServiceSystem Services { get; private set; }
        internal void InitializeServices(GuestServiceSettings configuration)
        { if (configuration != null && LivingEnabled) Services = new GuestServiceSystem(configuration, this, rooms.Values); }
        static CommandResult ServicesDisabled() => CommandResult.Fail("The guest service layer is not enabled for this session.");
        public CommandResult RespondToService(int actorId, string caseId, bool accept) => Services?.Respond(actorId, caseId, accept) ?? ServicesDisabled();
        public CommandResult AcknowledgeService(int actorId, string caseId) => Services?.Acknowledge(actorId, caseId) ?? ServicesDisabled();
        public CommandResult CompleteWakeUpCall(int actorId, string promiseId) => Services?.CompleteWakeUp(actorId, promiseId) ?? ServicesDisabled();
        public CommandResult TakeServiceItem(int actorId, string itemId) => Services?.TakeItem(actorId, itemId) ?? ServicesDisabled();
        public CommandResult DropServiceItem(int actorId, string itemId) => Services?.DropItem(actorId, itemId) ?? ServicesDisabled();
        public CommandResult ReturnServiceItem(int actorId, string itemId) => Services?.ReturnItem(actorId, itemId) ?? ServicesDisabled();
        public CommandResult DeliverBlanket(int actorId, string guestId) => Services?.DeliverBlanket(actorId, guestId) ?? ServicesDisabled();
        public CommandResult StoreLuggage(int actorId, string guestId) => Services?.StoreLuggage(actorId, guestId) ?? ServicesDisabled();
        public CommandResult ReplaceRoomBulb(int actorId, int roomId) => Services?.ReplaceBulb(actorId, roomId) ?? ServicesDisabled();
        public CommandResult DebugForceService(string guestId, ServiceKind kind) => Services?.ForceRequest(guestId, kind) ?? ServicesDisabled();
        public CommandResult DebugSetBlanketStock(int count) => Services?.SetBlanketStock(count) ?? ServicesDisabled();
        public CommandResult DebugSetMildCold(string guestId)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            var guest = guests.FirstOrDefault(item => item.GuestId == guestId);
            if (Services == null || guest?.Agent == null || !guest.Agent.InAssignedRoom) return CommandResult.Fail("Choose a guest physically inside a room.");
            float target = guest.Application.Archetype.Needs.PreferredTemperatureMin - 1.5f;
            var result = SetRoomTemperature(guest.RoomId, target);
            if (result.Success) NeedEvaluator.Tick(guest, rooms[guest.RoomId], .001f);
            return result;
        }
    }
}
