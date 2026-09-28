using System.Linq;

namespace WorstHotel
{
    public sealed partial class GuestServiceSystem
    {
        public static int LuggageCount(BookingApplication application) => application.Special?.Baggage.Count ?? LuggageCount(application.Id);
        public static string LuggageId(string guestId, int index) => "luggage:" + guestId + (index == 0 ? "" : ":" + (index + 1));
        public ServiceItemState ActiveAmplifier(GuestStay guest)
        {
            var agent = guest?.Agent;
            if (guest?.Application.Special?.AmplifierLoad > 0 && agent != null && agent.InAssignedRoom &&
                agent.State != GuestAgentState.Sleeping && agent.ActivityStaged && agent.Activity == GuestActivity.Rehearsal)
                return items.FirstOrDefault(i => i.GuestId == guest.GuestId && i.Payload == LuggagePayload.Amplifier &&
                    !i.EquipmentSwitchedOff && i.Location == ServiceItemLocation.Delivered && i.RoomId == guest.RoomId);
            return null;
        }

        public static int LuggageCount(string guestId)
        {
            uint hash = 2166136261;
            foreach (char c in guestId) hash = unchecked((hash ^ c) * 16777619);
            return hash % 8 == 0 ? 0 : hash % 8 == 1 ? 2 : 1;
        }

        public bool CanOfferLuggage(string guestId, bool storage)
        {
            var guest = Guest(guestId);
            if (guest?.Agent == null || guest.Agent.State != GuestAgentState.WaitingForCheckIn ||
                !items.Any(item => item.Kind == ServiceItemKind.Luggage && item.GuestId == guestId &&
                    !item.StaffHandling && !item.LuggageOfferAnswered)) return false;
            return !storage || !LuggageRoomReady(guestId);
        }

        public bool LuggageRoomReady(string guestId)
        {
            var guest = Guest(guestId);
            return guest != null && rooms.TryGetValue(guest.RoomId, out var room) &&
                room.Cleanliness == Cleanliness.Clean && string.IsNullOrEmpty(room.DepartingGuestId) &&
                (!room.Occupied || room.GuestId == guestId) &&
                room.TurnoverState != HousekeepingState.Cleaning && room.TurnoverState != HousekeepingState.Moving;
        }

        public CommandResult OfferLuggage(int actor, string guestId, bool storage)
        {
            var allowed = CanAct(actor); if (!allowed.Success) return allowed;
            if (!CanOfferLuggage(guestId, storage)) return CommandResult.Fail("No luggage assistance to offer here.");
            var guest = Guest(guestId);
            // Most welcome help; an occasional independent traveller politely keeps their bag.
            bool accepts = guest.Application.Special != null || storage || guest.Name.Length % 7 != 0;
            foreach (var bag in items.Where(item => item.Kind == ServiceItemKind.Luggage && item.GuestId == guestId))
                bag.LuggageOfferAnswered = true;
            if (!accepts) return CommandResult.Ok("No thank you, I can carry it myself.");
            AcceptLuggageResponsibility(guest);
            var request = cases.FirstOrDefault(item => item.GuestId == guestId && item.Kind == ServiceKind.LuggageStorage && item.Active);
            if (request != null)
            {
                ReleaseDecisionIntent(request);
                Finish(request, guest, ServiceStatus.Expired, 0);
                request.ResolutionReason = "Staff offered physical luggage assistance";
            }
            return CommandResult.Ok(storage ? "Yes, thank you. Leave my bags in storage, then bring them to room " + guest.RoomId + "." :
                "Yes, please. Bring my bags INSIDE room " + guest.RoomId + ", beside the wardrobe. Carry my bag or guide its cart to use delivery access.");
        }

        void AcceptLuggageResponsibility(GuestStay guest)
        {
            foreach (var bag in items.Where(item => item.Kind == ServiceItemKind.Luggage && item.GuestId == guest.GuestId))
            {
                bag.StaffHandling = true; bag.LuggageOfferAnswered = true; bag.RoomId = guest.RoomId;
                if (bag.Location == ServiceItemLocation.OnShelf) bag.Location = ServiceItemLocation.Dropped;
                ItemChanged?.Invoke(bag);
            }
        }

        // Called only by the host's physical parcel after it has settled inside an authored zone.
        public CommandResult PlaceLuggage(string itemId, bool stored)
        {
            if (simulation.IsReadOnlyMirror) return CommandResult.Fail(HotelSimulation.MirrorMessage);
            var item = FindItem(itemId); var guest = item == null ? null : Guest(item.GuestId);
            if (guest == null || item.Kind != ServiceItemKind.Luggage ||
                item.Location != ServiceItemLocation.Dropped && item.Location != ServiceItemLocation.Stored ||
                !item.StaffHandling && !Departed(guest)) return CommandResult.Fail("No agreed suitcase to place.");
            if (!stored && (Departed(guest) || !LuggageRoomReady(guest.GuestId))) return CommandResult.Fail("The destination room is not ready.");
            item.Location = stored ? ServiceItemLocation.Stored : ServiceItemLocation.Delivered;
            item.RoomId = guest.RoomId; item.PlayerId = null; ItemChanged?.Invoke(item);
            var request = cases.FirstOrDefault(c => c.GuestId == guest.GuestId && c.Kind == ServiceKind.LuggageStorage && c.Active);
            if (request != null && items.Where(i => i.Kind == ServiceItemKind.Luggage && i.GuestId == guest.GuestId).All(i => i.Location == ServiceItemLocation.Stored || i.Location == ServiceItemLocation.Delivered))
                Finish(request, guest, ServiceStatus.Fulfilled, Settings.FulfilledBonus, true);
            if (stored && guest.Memory.LuggageStored == 0) guest.Memory.LuggageStored = 1;
            simulation.SignalEvent("Room " + guest.RoomId + ": suitcase " + (stored ? "stored beside reception" : "delivered"));
            return CommandResult.Ok();
        }

        public void SettleOwnLuggage(string itemId)
        {
            if (simulation.IsReadOnlyMirror) return;
            var item = FindItem(itemId);
            if (item?.Kind == ServiceItemKind.Luggage && !item.StaffHandling && item.Location == ServiceItemLocation.OnShelf)
            { item.Location = ServiceItemLocation.Delivered; ItemChanged?.Invoke(item); }
        }
    }
}
