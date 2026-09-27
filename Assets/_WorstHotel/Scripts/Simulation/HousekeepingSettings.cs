using System;

namespace WorstHotel
{
    public sealed class HousekeepingSettings
    {
        public float MakeBedSeconds { get; }
        public int CleanLinenPerDay { get; }
        // Compatibility for old readouts; this no longer drives a worker or hotel-time timer.
        public float CleaningSeconds => MakeBedSeconds;
        public HousekeepingSettings(float makeBedSeconds = 1.5f, int cleanLinenPerDay = 6)
        {
            if (!Number.IsFinite(makeBedSeconds) || makeBedSeconds <= 0 || cleanLinenPerDay <= 0 || cleanLinenPerDay > 10)
                throw new ArgumentException("Manual turnover requires positive finite bed-making time and one to ten physical shelf slots.");
            MakeBedSeconds = makeBedSeconds;
            CleanLinenPerDay = cleanLinenPerDay;
        }
    }
}
