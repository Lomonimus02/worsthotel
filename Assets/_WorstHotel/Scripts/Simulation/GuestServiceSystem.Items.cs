using System;
using System.Linq;

namespace WorstHotel
{
    public sealed partial class GuestServiceSystem
    {
        CommandResult CanHandle(int actor)
        {
            if (simulation.IsReadOnlyMirror) return CommandResult.Fail(HotelSimulation.MirrorMessage);
            return actor < 0 || actor > 1 ? CommandResult.Fail("Unknown player identity.") : CommandResult.Ok();
        }

        public CommandResult TakeItem(int actor, string id)
        {
            var allowed = CanHandle(actor); if (!allowed.Success) return allowed;
            var item = FindItem(id);
            if (item == null || (item.Location != ServiceItemLocation.OnShelf && item.Location != ServiceItemLocation.Dropped))
                return CommandResult.Fail("This physical item is not available to take.");
            if (HeldBy(actor) != null) return CommandResult.Fail("Put down the service item already being carried.");
            if (item.Kind == ServiceItemKind.Luggage && item.Location == ServiceItemLocation.OnShelf &&
                (Guest(item.GuestId)?.Agent.CheckedIn != false ||
                !cases.Any(request => request.GuestId == item.GuestId && request.Kind == ServiceKind.LuggageStorage && request.Status == ServiceStatus.InProgress)))
                return CommandResult.Fail("Accept the guest's luggage storage request before taking their suitcase.");
            item.Location = ServiceItemLocation.HeldByPlayer; item.PlayerId = item.LastPlayerId = actor;
            ItemChanged?.Invoke(item); return CommandResult.Ok("Picked up " + item.Kind + ".");
        }

        public CommandResult DropItem(int actor, string id)
        {
            var allowed = CanHandle(actor); if (!allowed.Success) return allowed;
            var item = FindItem(id);
            if (item == null || item.Location != ServiceItemLocation.HeldByPlayer || item.PlayerId != actor)
                return CommandResult.Fail("You are not carrying this item.");
            item.Location = ServiceItemLocation.Dropped; item.PlayerId = null; item.LastPlayerId = actor;
            ItemChanged?.Invoke(item); return CommandResult.Ok("Item put down.");
        }

        public CommandResult ReturnItem(int actor, string id)
        {
            var allowed = CanHandle(actor); if (!allowed.Success) return allowed;
            var item = FindItem(id);
            if (item == null || item.Location != ServiceItemLocation.HeldByPlayer || item.PlayerId != actor || item.Kind == ServiceItemKind.Luggage)
                return CommandResult.Fail("Carry an unused hotel supply back to its shelf.");
            item.Location = ServiceItemLocation.OnShelf; item.PlayerId = null; item.LastPlayerId = actor;
            item.GuestId = null; item.RoomId = null;
            ItemChanged?.Invoke(item); return CommandResult.Ok("Supply returned to its physical shelf.");
        }

        public CommandResult DeliverBlanket(int actor, string guestId)
        {
            var allowed = CanAct(actor); if (!allowed.Success) return allowed;
            var guest = Guest(guestId); var item = HeldBy(actor);
            if (guest == null || !guest.Agent.InAssignedRoom || Departed(guest)) return CommandResult.Fail("Deliver the blanket to a guest in their room.");
            if (IntentBehaviorEnabled && !CanReceiveBlanket(guest))
                return CommandResult.Fail("The guest is unavailable. An agreed blanket can be left at the room's exterior delivery point.");
            if (DropOffIntent(guestId)?.Status == ServiceIntentStatus.AwaitingReceipt)
                return CommandResult.Fail("An actual blanket is already awaiting this guest at their delivery point.");
            if (guest.Memory.BlanketsDelivered > 0) return CommandResult.Fail("This guest already has an extra blanket.");
            if (item == null || item.Kind != ServiceItemKind.Blanket) return CommandResult.Fail("Carry an actual blanket from linen storage.");
            guest.BlanketComfortBonus = Settings.BlanketComfortBonus;
            guest.Memory.BlanketsDelivered = Count(guest.Memory.BlanketsDelivered);
            Deliver(item, guest);
            RecordInteriorBlanketReceipt(guest, item);
            RecordStaffAction(guestId, IncidentReason.Temperature);
            var request = cases.FirstOrDefault(request => request.GuestId == guestId && request.Kind == ServiceKind.ExtraBlanket && request.Active);
            if (request != null && (!NaturalCommunicationEnabled || IntentBehaviorEnabled)) Finish(request, guest, ServiceStatus.Fulfilled, Settings.FulfilledBonus, true);
            else simulation.SignalEvent("Extra blanket delivered to room " + guest.RoomId);
            return CommandResult.Ok("Blanket delivered. Personal cold comfort improves; room temperature and electrical load are unchanged.");
        }

        public bool IsDepartedLuggage(string itemId)
        {
            var item = FindItem(itemId);
            var guest = item?.Kind == ServiceItemKind.Luggage ? Guest(item.GuestId) : null;
            return guest != null && Departed(guest);
        }

        public CommandResult CanStoreLuggage(int actor, string guestId)
        {
            var allowed = CanAct(actor); if (!allowed.Success) return allowed;
            var guest = Guest(guestId); var item = HeldBy(actor);
            if (guest == null || item == null || item.Kind != ServiceItemKind.Luggage || item.GuestId != guestId)
                return CommandResult.Fail("Carry this guest's actual suitcase to luggage storage.");
            if (Departed(guest)) return CommandResult.Ok("Place the departed guest's suitcase in lost-property storage.");
            var request = cases.FirstOrDefault(request => request.GuestId == guestId && request.Kind == ServiceKind.LuggageStorage);
            if (request == null) return CommandResult.Fail("There is no luggage storage agreement for this suitcase.");
            if (NaturalCommunicationEnabled && (!request.IsKnownToHotel || request.Status != ServiceStatus.InProgress))
                return CommandResult.Fail("This suitcase needs a current accepted storage agreement.");
            return CommandResult.Ok();
        }

        public CommandResult StoreLuggage(int actor, string guestId)
        {
            var allowed = CanStoreLuggage(actor, guestId); if (!allowed.Success) return allowed;
            var guest = Guest(guestId); var item = HeldBy(actor);
            item.Location = ServiceItemLocation.Stored; item.PlayerId = null; item.RoomId = null; ItemChanged?.Invoke(item);
            if (Departed(guest)) return CommandResult.Ok("The departed guest's suitcase is in lost-property storage.");
            var request = cases.First(request => request.GuestId == guestId && request.Kind == ServiceKind.LuggageStorage);
            if (request.Active)
            { guest.Memory.LuggageStored = Count(guest.Memory.LuggageStored); Finish(request, guest, ServiceStatus.Fulfilled, Settings.FulfilledBonus, true); }
            return CommandResult.Ok("The suitcase is stored beside reception.");
        }

        public CommandResult ReplaceBulb(int actor, int roomId)
        {
            var allowed = CanHandle(actor); if (!allowed.Success) return allowed;
            var item = HeldBy(actor);
            if (!rooms.TryGetValue(roomId, out var room) || !room.LampBroken) return CommandResult.Fail("This room's lamp does not need a bulb.");
            if (item == null || item.Kind != ServiceItemKind.ReplacementBulb) return CommandResult.Fail("Carry a replacement bulb from maintenance storage.");
            room.LampCondition = 100; room.LampBroken = false;
            if (room.GuestId != null) RecordStaffAction(room.GuestId, IncidentReason.RoomCondition, "room/" + roomId + "/lamp");
            item.Location = ServiceItemLocation.Delivered; item.RoomId = roomId; item.PlayerId = null; ItemChanged?.Invoke(item);
            simulation.SignalEvent("Room " + roomId + ": bedside lamp repaired");
            return CommandResult.Ok("Bulb replaced. The lamp works when the room has power.");
        }

        void Deliver(ServiceItemState item, GuestStay guest)
        { item.Location = ServiceItemLocation.Delivered; item.PlayerId = null; item.GuestId = guest.GuestId;
            item.RoomId = guest.RoomId; ItemChanged?.Invoke(item); }
    }
}
