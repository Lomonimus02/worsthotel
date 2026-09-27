namespace WorstHotel
{
    public sealed partial class GuestServiceSystem
    {
        internal bool ExhaustedContactAttempts(HotelIncident incident)
        {
            var response = incident?.Response;
            // MissContact retains RetryAt even after the last real unanswered attempt.
            // Generic cancellation (recovery/debug/context loss) explicitly clears it.
            return response != null && response.IncidentId == incident.Id && response.IncidentEpisode == incident.EpisodeCount &&
                response.GuestId == incident.GuestId && response.RoomId == incident.RoomId && response.CommunicatedAt < 0 &&
                response.Phase == GuestResponsePhase.Cancelled && response.ContactAttempts >= Settings.MaxContactAttempts &&
                response.RetryAt >= 0;
        }
    }
}
