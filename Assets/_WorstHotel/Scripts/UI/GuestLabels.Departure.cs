using System.Linq;

namespace WorstHotel
{
    public static partial class GuestLabels
    {
        /// <summary>Risk assessment about an actually reported current episode, never private telemetry.</summary>
        public static string KnownEarlyDepartureWarning(GuestStay guest, HotelSimulation simulation, HotelIncident concern = null)
        {
            if (guest == null || simulation == null || !simulation.IsEarlyCheckoutWarningKnown(guest)) return null;
            var departure = guest.EarlyCheckout;
            if (concern != null && (concern.Id != departure.IncidentId || concern.EpisodeCount != departure.IncidentEpisode ||
                concern.GuestId != guest.GuestId || concern.RoomId != departure.RoomId)) return null;
            var incident = concern ?? simulation.Incidents.Items.FirstOrDefault(item => item.Id == departure.IncidentId &&
                item.EpisodeCount == departure.IncidentEpisode && item.GuestId == guest.GuestId && item.RoomId == departure.RoomId);
            if (incident == null || !incident.Active || !IsKnownToHotel(incident)) return null;
            string problem = incident.Reason == IncidentReason.Temperature ?
                (incident.Cause != null && incident.Cause.ReceivedIntensity < incident.Cause.GuestThreshold ? "cold" : "temperature discomfort") :
                incident.Reason == IncidentReason.Noise ? "noise" : "room discomfort";
            return "Reported " + problem + " persists · guest may leave early";
        }

        /// <summary>The paid receipt is sufficient after its guest/incident has been pruned.</summary>
        public static string EarlyCheckoutReceiptSummary(GuestReceipt receipt, HotelSimulation simulation) =>
            receipt?.EarlyCheckout == true ? "EARLY CHECKOUT · " + HotelMoment(simulation, receipt.CheckoutAt) +
                "\n" + receipt.DepartureReason : null;
    }
}
