using System;
using System.Collections.Generic;

namespace WorstHotel
{
    public sealed partial class HotelSimulation
    {
        public OperationsSettings Operations { get; }
        public bool ContinuousOperations => Operations != null;
        public HotelCalendar Calendar { get; }
        public int CalendarDay => Calendar?.Day ?? dayNumber;
        public int ReportSequence { get; private set; }
        public float NextReportAt => ContinuousOperations ? Calendar.FirstReportAt + ReportSequence * Operations.SecondsPerDay : settings.ServiceSeconds;
        readonly List<GuestReceipt> periodReceipts = new List<GuestReceipt>();
        int periodOpeningCash;
        float periodStartedAt;

        public CommandResult StartOperations()
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            if (!ContinuousOperations) return CommandResult.Fail("Continuous calendar settings are required.");
            if (Running || dayNumber != 0) return CommandResult.Fail("The hotel is already open. Its clock only resets for a new game.");
            if (Elapsed != 0) return CommandResult.Fail("Open continuous operations on a fresh hotel clock.");
            dayNumber = Calendar.Day;
            periodOpeningCash = Economy.Cash;
            periodStartedAt = Elapsed;
            Running = true;
            // Initial setup only. Calendar boundaries never call StartDay or BeginService.
            Services?.StartDay(dayNumber, Calendar.At(dayNumber + 1, Operations.CheckoutHour));
            RefreshGuestLoad();
            Boiler.BeginService();
            SignalEvent("Hotel open — continuous operations");
            return CommandResult.Ok("Hotel open. Review upcoming bookings while the building keeps running.");
        }

        void TickOperations(float delta)
        {
            // Production supplies fixed ticks. Bound diagnostic calls before work, without limiting
            // the lifetime of the hotel or turning a delayed frame into an unbounded catch-up loop.
            if (delta > Math.Min((double)Operations.SecondsPerDay * 32, 86400d))
                throw new ArgumentOutOfRangeException(nameof(delta), "Advance long intervals in bounded hotel steps.");
            float target = Elapsed + delta;
            if (!Number.IsFinite(target) || target <= Elapsed) throw new ArgumentOutOfRangeException(nameof(delta));
            while (Elapsed < target)
            {
                CloseDueOperatingReports();
                float step = Math.Min(1f, Math.Min(target - Elapsed, NextReportAt - Elapsed));
                if (step <= 0 || Elapsed + step == Elapsed)
                    throw new InvalidOperationException("The hotel clock cannot represent another simulation step.");
                TickStep(step);
                dayNumber = Calendar.Day;
                CloseDueOperatingReports();
            }
        }

        void CloseDueOperatingReports()
        {
            while (Elapsed >= NextReportAt)
            {
                float boundary = NextReportAt;
                LastReport = Economy.CloseOperatingDay(ReportSequence + 1, periodReceipts,
                    periodOpeningCash, boundary - periodStartedAt);
                reports.Add(LastReport);
                if (reports.Count > Operations.ReportHistoryLimit) reports.RemoveAt(0);
                ReportSequence++;
                periodReceipts.Clear();
                periodOpeningCash = Economy.Cash;
                periodStartedAt = boundary;
                SignalEvent("Operating report " + ReportSequence + " available — hotel remains open");
            }
        }
    }
}
