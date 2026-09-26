using System;

namespace WorstHotel
{
    public static partial class GuestLabels
    {
        public static string Service(ServiceKind kind)
        {
            switch (kind)
            {
                case ServiceKind.ExtraBlanket: return "Room feels cold";
                case ServiceKind.LuggageStorage: return "Luggage storage";
                case ServiceKind.LateCheckout: return "Late checkout";
                case ServiceKind.WakeUpCall: return "Wake-up call";
                default: return "A little more quiet";
            }
        }

        public static string ServiceState(ServiceStatus status)
        {
            switch (status)
            {
                case ServiceStatus.Requested: return "Reply needed";
                case ServiceStatus.Acknowledged: return "Acknowledged";
                case ServiceStatus.InProgress: return "Help agreed";
                case ServiceStatus.Fulfilled: return "Completed";
                case ServiceStatus.Declined: return "Declined";
                case ServiceStatus.Expired: return "No reply";
                default: return "Became a complaint";
            }
        }

        public static string HotelTime(float seconds)
        {
            int value = Math.Max(0, (int)Math.Ceiling(seconds));
            return value / 60 + ":" + (value % 60).ToString("D2");
        }

        public static string ServiceHelp(ServiceKind kind)
        {
            switch (kind)
            {
                case ServiceKind.ExtraBlanket: return "The guest wants to feel warmer. An agreement alone does not change their comfort.";
                case ServiceKind.LuggageStorage: return "The guest wants somewhere to leave their suitcase until they need it again.";
                case ServiceKind.LateCheckout: return "A later departure gives this guest more time, but leaves less time to strip and prepare the room.";
                case ServiceKind.WakeUpCall: return "Accept the time, then return to the reception phone to make the call when it is due.";
                default: return "The guest wants some quiet. A conversation or credit does not by itself remove the sound they hear.";
            }
        }

        public static string ServiceAcceptance(ServiceCase item, HotelSimulation simulation = null) => item.Kind == ServiceKind.WakeUpCall ?
            "Promise a call at " + HotelMoment(simulation, item.DueTime) : item.Kind == ServiceKind.LateCheckout ?
            "Allow checkout at " + HotelMoment(simulation, item.DueTime) : item.Kind == ServiceKind.ExtraBlanket ? "I'll look into the temperature" :
            item.Kind == ServiceKind.AskNeighborsQuiet ? "I'll look into the noise" : "Agree to store the suitcase";
    }
}
