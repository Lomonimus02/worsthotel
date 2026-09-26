using System;

namespace WorstHotel
{
    public sealed class OperationsSettings
    {
        public float SecondsPerDay { get; }
        public float StartHour { get; }
        public float ReportHour { get; }
        public float ArrivalStartHour { get; }
        public float ArrivalEndHour { get; }
        public float SleepHour { get; }
        public float CheckoutHour { get; }
        public int ReportHistoryLimit { get; }

        public OperationsSettings(float secondsPerDay = 720, float startHour = 8, float reportHour = 6,
            float arrivalStartHour = 14, float arrivalEndHour = 18, float sleepHour = 23,
            float checkoutHour = 10, int reportHistoryLimit = 32)
        {
            if (!Number.IsFinite(secondsPerDay) || secondsPerDay < 24 ||
                !ValidHour(startHour) || !ValidHour(reportHour) || !ValidHour(arrivalStartHour) ||
                !ValidHour(arrivalEndHour) || !ValidHour(sleepHour) || !ValidHour(checkoutHour) ||
                arrivalStartHour >= arrivalEndHour || sleepHour <= arrivalEndHour || checkoutHour >= arrivalStartHour ||
                reportHistoryLimit < 1 || reportHistoryLimit > 128)
                throw new ArgumentException("Invalid continuous hotel calendar settings.");
            SecondsPerDay = secondsPerDay; StartHour = startHour; ReportHour = reportHour;
            ArrivalStartHour = arrivalStartHour; ArrivalEndHour = arrivalEndHour;
            SleepHour = sleepHour; CheckoutHour = checkoutHour; ReportHistoryLimit = reportHistoryLimit;
        }

        static bool ValidHour(float value) => Number.IsFinite(value) && value >= 0 && value < 24;
    }

    /// <summary>A calendar view of the existing hotel clock, never a second advancing clock.</summary>
    public sealed class HotelCalendar
    {
        readonly IGameClock clock;
        public OperationsSettings Settings { get; }
        double TotalHours => Settings.StartHour + (double)clock.SimulationTime * 24 / Settings.SecondsPerDay;
        public int Day => checked((int)Math.Floor(TotalHours / 24) + 1);
        public float Hour => (float)(TotalHours % 24);
        public string DisplayTime
        {
            get
            {
                int minute = (int)Math.Floor((TotalHours % 24) * 60 + .00001) % 1440;
                return (minute / 60).ToString("00") + ":" + (minute % 60).ToString("00");
            }
        }
        public float FirstReportAt => (float)((Settings.ReportHour > Settings.StartHour ?
            (double)Settings.ReportHour - Settings.StartHour : 24d + Settings.ReportHour - Settings.StartHour) * Settings.SecondsPerDay / 24);

        public HotelCalendar(IGameClock clock, OperationsSettings settings)
        { this.clock = clock ?? throw new ArgumentNullException(nameof(clock)); Settings = settings ?? throw new ArgumentNullException(nameof(settings)); }

        public float At(int day, float hour)
        {
            if (day < 1 || !Number.IsFinite(hour) || hour < 0 || hour >= 24)
                throw new ArgumentOutOfRangeException(nameof(day), "A calendar date needs a positive day and an hour in [0,24).");
            double result = ((day - 1d) * 24 + hour - Settings.StartHour) * Settings.SecondsPerDay / 24;
            if (result > float.MaxValue) throw new ArgumentOutOfRangeException(nameof(day));
            return (float)result;
        }
    }
}
