using System;

namespace WorstHotel
{
    public static partial class GuestLabels
    {
        public static string HotelMoment(HotelSimulation simulation, float time)
        {
            if (simulation?.ContinuousOperations != true) return HotelTime(time);
            double hours = simulation.Operations.StartHour + (double)time * 24 / simulation.Operations.SecondsPerDay;
            int day = (int)Math.Floor(hours / 24) + 1;
            int minutes = (int)Math.Floor((hours % 24) * 60 + .00001) % 1440;
            return "Day " + day + " · " + (minutes / 60).ToString("00") + ":" + (minutes % 60).ToString("00");
        }

        public static string HotelDuration(HotelSimulation simulation, float seconds)
        {
            if (simulation?.ContinuousOperations != true) return HotelTime(seconds);
            int minutes = Math.Max(0, (int)Math.Ceiling((double)seconds * 1440 / simulation.Operations.SecondsPerDay));
            return minutes >= 60 ? minutes / 60 + "h " + minutes % 60 + "m" : minutes + " min";
        }
    }
}
