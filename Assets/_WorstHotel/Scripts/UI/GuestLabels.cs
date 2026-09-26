namespace WorstHotel
{
    public static partial class GuestLabels
    {
        public static string Activity(GuestActivity activity)
        {
            switch (activity)
            {
                case GuestActivity.Shower: return "Shower";
                case GuestActivity.LoudRoom: return "Loud TV / music";
                case GuestActivity.Unpack: return "Unpacking";
                case GuestActivity.Work: return "Working at desk";
                case GuestActivity.PhoneCall: return "Phone call";
                case GuestActivity.WatchTV: return "Watching TV";
                case GuestActivity.LeaveHotel: return "Going out";
                case GuestActivity.Pack: return "Packing";
                default: return "Quiet rest";
            }
        }

        public static string Tendencies(GuestProfile profile)
        {
            string routine = profile.Kind == GuestKind.Business ? "Work, calls, earlier sleep" :
                profile.Kind == GuestKind.Budget ? "Trips outside, TV, quiet breaks" : "Rest, longer hot showers";
            return routine + ((profile.Traits & GuestTraits.Noisy) != 0 ? " · louder TV / calls" : "");
        }
        public static string State(GuestAgent agent)
        {
            switch (agent.State)
            {
                case GuestAgentState.Scheduled: return "Arrival due";
                case GuestAgentState.WaitingForCheckIn: return "Waiting for key";
                case GuestAgentState.GoingToRoom: return agent.IsRelocating ? "Moving to new room" : "Going to room";
                case GuestAgentState.InRoom: return agent.RequiresActivityStaging && !agent.ActivityStaged ? "Settling into room" : "Resting";
                case GuestAgentState.PerformingActivity: return agent.RequiresActivityStaging && !agent.ActivityStaged ?
                    "Getting ready: " + Activity(agent.Activity) : Activity(agent.Activity);
                case GuestAgentState.Sleeping: return agent.RequiresActivityStaging && !agent.ActivityStaged ? "Going to bed" : "Sleeping";
                case GuestAgentState.LeavingRoom: return "Leaving the hotel";
                case GuestAgentState.GuestAway: return "Out of the hotel";
                case GuestAgentState.ReturningToRoom: return "Returning from outside";
                case GuestAgentState.CheckingOut: return "Checking out";
                default: return agent.State.ToString();
            }
        }
        public static string Traits(GuestTraits traits)
        {
            string value = "";
            Add(ref value, traits, GuestTraits.ColdSensitive, "Cold sensitive");
            Add(ref value, traits, GuestTraits.NoiseSensitive, "Noise sensitive");
            Add(ref value, traits, GuestTraits.Noisy, "Noisy activities");
            Add(ref value, traits, GuestTraits.Patient, "Patient");
            Add(ref value, traits, GuestTraits.Impatient, "Impatient");
            Add(ref value, traits, GuestTraits.PriceSensitive, "Price sensitive");
            return value.Length > 0 ? value : "Low maintenance";
        }
        static void Add(ref string text, GuestTraits flags, GuestTraits flag, string label)
        { if ((flags & flag) != 0) text += (text.Length > 0 ? " / " : "") + label; }
    }
}
