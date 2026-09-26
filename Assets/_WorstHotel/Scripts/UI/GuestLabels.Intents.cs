using System.Linq;

namespace WorstHotel
{
    public static partial class GuestLabels
    {
        public static bool IsKnownToHotel(GuestServiceIntent intent, HotelSimulation simulation)
        {
            if (intent == null || simulation == null) return false;
            if (intent.Purpose == ServiceIntentPurpose.RoomMove) return true; // A staff-proposed key exchange.
            if (intent.CaseId != null) return IsKnownToHotel(simulation.Services?.FindCase(intent.CaseId));
            return IsKnownToHotel(simulation.Incidents.Items.FirstOrDefault(item => item.Id == intent.IncidentId));
        }

        public static string IntentState(GuestServiceIntent intent, HotelSimulation simulation)
        {
            if (!IsKnownToHotel(intent, simulation)) return null;
            if (intent.Status == ServiceIntentStatus.AwaitingReceipt) return "Left outside room " + intent.RoomId + " · not yet received";
            if (intent.Status == ServiceIntentStatus.Completed)
                return intent.ReceivedAt >= 0 ? "Blanket received by the guest" : intent.Purpose == ServiceIntentPurpose.ServiceDecision ?
                    "Decision recorded · " + ServiceState(simulation.Services.FindCase(intent.CaseId)?.Status ?? ServiceStatus.Fulfilled) :
                    intent.Purpose == ServiceIntentPurpose.RoomMove ? "Room keys exchanged" : "Guest condition recovered";
            if (intent.Status == ServiceIntentStatus.TimedOut) return "The guest's wait ended";
            if (intent.Status == ServiceIntentStatus.Cancelled) return intent.DeliveredAt >= 0 ? "Delivery cancelled · collect the blanket" : "Agreement ended";
            if (intent.Kind == ServiceIntentKind.DropOff) return "Delivery to room " + intent.RoomId + " · guest can continue their day";
            if (intent.Kind == ServiceIntentKind.Direct) return (intent.Purpose == ServiceIntentPurpose.RoomMove ?
                "Waiting for key " + simulation.Guests.FirstOrDefault(guest => guest.GuestId == intent.GuestId)?.Agent?.PendingMoveRoomId : "Waiting for your decision") +
                " until " + HotelMoment(simulation, intent.Deadline);
            return "Reported problem · reassessed when the guest is in the room";
        }

        public static string ServiceProgress(ServiceCase item, HotelSimulation simulation)
        {
            if (!IsKnownToHotel(item)) return "Not yet reported";
            var intent = simulation?.Services?.Intents.FirstOrDefault(value => value.CaseId == item.Id);
            return IntentState(intent, simulation) ?? ServiceState(item.Status);
        }

        public static string ServiceBrief(ServiceCase item, HotelSimulation simulation)
        {
            if (!IsKnownToHotel(item)) return "Not yet reported";
            var intent = simulation?.Services?.Intents.FirstOrDefault(value => value.CaseId == item.Id && value.Active);
            if (intent?.Status == ServiceIntentStatus.AwaitingReceipt) return "Left at door · awaiting guest";
            if (intent?.Kind == ServiceIntentKind.Direct) return "Decision by " + HotelMoment(simulation, intent.Deadline);
            return ServiceState(item.Status);
        }

        public static string GuestIntentStatus(GuestStay guest, HotelSimulation simulation)
        {
            var intent = simulation?.Services?.Intents.Where(value => value.GuestId == guest.GuestId && value.Active &&
                IsKnownToHotel(value, simulation)).OrderByDescending(value => value.Kind == ServiceIntentKind.Direct)
                .ThenByDescending(value => value.Kind == ServiceIntentKind.DropOff).FirstOrDefault();
            return IntentState(intent, simulation);
        }
    }
}
