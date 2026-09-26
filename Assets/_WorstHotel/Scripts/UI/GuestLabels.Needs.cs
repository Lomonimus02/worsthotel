namespace WorstHotel
{
    public static partial class GuestLabels
    {
        // Observed exposure is background life, never a task for the player.
        public static bool IsActionable(HotelIncident situation) => situation != null && situation.Active &&
            situation.Stage >= SituationStage.Complaint && situation.Stage < SituationStage.Resolved &&
            (situation.Reason == IncidentReason.Noise || situation.Reason == IncidentReason.Temperature ||
             situation.Reason == IncidentReason.RoomCondition);

        public static string ComplaintClue(HotelIncident situation)
        {
            string repeat = situation.EpisodeCount > 1 ? "Again: " : "";
            if (situation.Reason == IncidentReason.Noise) return repeat + "“I can hear someone nearby.”";
            if (situation.Reason == IncidentReason.Temperature) return repeat +
                (situation.Cause != null && situation.Cause.ReceivedIntensity > situation.Cause.GuestThreshold ?
                    "“The room temperature has been uncomfortable.”" : "“It's cold in here.”");
            if (situation.Cause?.SourceType == "Power loss") return repeat + "“The lights in my room are out.”";
            if (situation.Cause?.SourceType == "Burnt-out lamp") return repeat + "“The bedside lamp has stopped working.”";
            return repeat + "“The bed hasn't been changed.”";
        }

        public static string Problem(IncidentReason reason)
        {
            switch (reason)
            {
                case IncidentReason.Cold: return "Too cold";
                case IncidentReason.Noise: return "Noise disturbance";
                case IncidentReason.Dirty: return "Dirty room";
                case IncidentReason.Broken: return "Broken fixture";
                case IncidentReason.Temperature: return "Temperature discomfort";
                case IncidentReason.RoomCondition: return "Room condition";
                case IncidentReason.Service: return "Service delay";
                default: return "Room discomfort";
            }
        }

        public static string Situation(SituationStage stage)
        {
            switch (stage)
            {
                case SituationStage.Observed: return "Discomfort noticed";
                case SituationStage.Complaint: return "Guest complaint";
                case SituationStage.Escalated: return "Patience declining";
                case SituationStage.Critical: return "Urgent complaint";
                default: return "Resolved";
            }
        }

        public static string Need(float severity) => severity <= .03f ? "Comfortable" : severity < .3f ? "Uneasy" : severity < .7f ? "Uncomfortable" : "Very uncomfortable";
        public static string NeedSummary(GuestStay guest)
        {
            if (guest.Needs == null || guest.Agent == null) return "Awaiting check-in";
            if (guest.Agent.State == GuestAgentState.WaitingForCheckIn)
                return "Reception: " + (guest.Needs.Service.Severity > .03f ? "patience declining" : "waiting patiently");
            if (!guest.Agent.InAssignedRoom) return State(guest.Agent);
            return "Temperature: " + Need(guest.Needs.Temperature.Severity) + "  /  Quiet: " + Need(guest.Needs.Noise.Severity);
        }
    }
}
