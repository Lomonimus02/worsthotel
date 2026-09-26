using System.Linq;

namespace WorstHotel
{
    public static partial class GuestLabels
    {
        // Every normal surface uses the same knowledge boundary. F2 deliberately shows raw state.
        public static bool IsKnownToHotel(ServiceCase item) => item != null && item.IsKnownToHotel;
        public static bool IsKnownToHotel(HotelIncident item) => item != null && item.HasContactedStaff;
        public static bool IsKnownOpenService(ServiceCase item) => IsKnownToHotel(item) && item.Active;

        public static string ServiceClue(ServiceCase item, HotelSimulation simulation = null)
        {
            if (!IsKnownToHotel(item)) return "The guest has not spoken to staff about this.";
            switch (item.Kind)
            {
                case ServiceKind.ExtraBlanket: return simulation?.ContinuousOperations == true ? "“Could you leave an extra blanket outside my room? I will collect it when I am back.”" : "“It's still quite cold in my room.”";
                case ServiceKind.AskNeighborsQuiet: return "“I can hear someone nearby. It's difficult to rest.”";
                case ServiceKind.LuggageStorage: return "“Could I leave my suitcase here for a while?”";
                case ServiceKind.LateCheckout: return "“Could I stay until " + HotelMoment(simulation, item.DueTime) + "?”";
                default: return "“Could you give me a wake-up call at " + HotelMoment(simulation, item.DueTime) + "?”";
            }
        }

        public static string ContactCue(HotelSimulation simulation)
        {
            var services = simulation?.Services;
            if (services == null) return null;
            if (services.IncomingCall != null) return "PHONE RINGING · reception";
            if (simulation.Guests.Any(g => g.Agent?.State == GuestAgentState.WaitingAtServiceReception))
                return "A guest is waiting to speak at reception.";
            if (services.Responses.Any(r => r.CommunicatedAt < 0 && r.ContactAttempts > 0 &&
                r.Channel == GuestContactChannel.Phone && r.Phase == GuestResponsePhase.WaitingToContact))
                return "MISSED CALL · a guest may try again.";
            return null;
        }

        public static string ResponseClue(HotelSimulation simulation, GuestResponse response)
        {
            if (response == null || response.CommunicatedAt < 0) return "Reception telephone";
            var request = simulation.Services.Cases.FirstOrDefault(c => c.Id == response.ServiceCaseId);
            if (simulation.ContinuousOperations && request?.Kind == ServiceKind.ExtraBlanket && IsKnownToHotel(request))
                return ServiceClue(request, simulation);
            var incident = simulation.Incidents.Items.FirstOrDefault(i => i.Id == response.IncidentId &&
                i.EpisodeCount == response.IncidentEpisode && IsKnownToHotel(i));
            if (incident != null) return ComplaintClue(incident);
            return ServiceClue(simulation.Services.Cases.FirstOrDefault(c => c.Id == response.ServiceCaseId), simulation);
        }
    }
}
