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
            if ((48d + Operations.CheckoutHour - Operations.StartHour) * Operations.SecondsPerDay / 24 > float.MaxValue)
                return CommandResult.Fail("This calendar scale cannot represent the upcoming booking dates.");
            dayNumber = Calendar.Day;
            periodOpeningCash = Economy.Cash;
            periodStartedAt = Elapsed;
            Running = true;
            // Initial setup only. Calendar boundaries never call StartDay or BeginService.
            Services?.StartDay(dayNumber, Calendar.At(dayNumber + 1, Operations.CheckoutHour));
            RefreshBookingSchedule(Elapsed);
            RefreshSalesDays(Elapsed);
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
            if (!Number.IsFinite(target) || target <= Elapsed)
                throw new ArgumentOutOfRangeException(nameof(delta), "Hotel step must advance a finite clock: elapsed=" +
                    Elapsed.ToString("R") + ", delta=" + delta.ToString("R") + ", target=" + target.ToString("R"));
            while (Running && Elapsed < target)
            {
                CloseDueOperatingReports();
                if (!Running) break;
                RefreshSalesDays(Elapsed);
                ProcessDueSalesDecisions(Elapsed);
                float step = Math.Min(1f, Math.Min(target - Elapsed, Math.Min(NextReportAt, Calendar.At(Calendar.Day + 1, 0)) - Elapsed));
                if (Boiler.MaintenanceInProgress) step = Math.Min(step, Boiler.MaintenanceEndsAt - Elapsed);
                if (TryGetNextSalesDecision(out float salesAt, out _, out _)) step = Math.Min(step, salesAt - Elapsed);
                if (step <= 0 || Elapsed + step == Elapsed)
                    throw new InvalidOperationException("The hotel clock cannot represent another simulation step.");
                RefreshBookingSchedule(Elapsed + step);
                TickStep(step);
                RefreshSalesDays(Elapsed);
                ProcessDueSalesDecisions(Elapsed);
                TickEarlyCheckout(Elapsed, step);
                PostCompletedStays(Elapsed);
                dayNumber = Calendar.Day;
                CloseDueOperatingReports();
            }
        }

        void CloseDueOperatingReports()
        {
            while (Running && Elapsed >= NextReportAt)
            {
                float boundary = NextReportAt;
                var operatingReport = Economy.CloseOperatingDay(ReportSequence + 1, periodReceipts,
                    periodOpeningCash, boundary - periodStartedAt, PeriodMaintenanceSpend, PeriodCapitalSpend);
                LastReport = SettleOwnershipContract(operatingReport);
                reports.Add(LastReport);
                if (reports.Count > Operations.ReportHistoryLimit) reports.RemoveAt(0);
                ReportSequence++;
                if (ContractEnabled && !OwnershipLost) ContractAssessedRooms = OperationalRoomCount;
                periodReceipts.Clear();
                PeriodMaintenanceSpend = 0;
                PeriodCapitalSpend = 0;
                periodOpeningCash = Economy.Cash;
                periodStartedAt = boundary;
                PruneCompletedOperatingHistory();
                SignalEvent(OwnershipLost ? "OWNERSHIP REVOKED — contract payment short by $" + LastReport.ContractPayment.Shortfall + ". See the official notice." :
                    ContractEnabled ? "Contract payment $" + LastReport.ContractPayment.PaidAmount + " complete. Accounts filed; next obligation $" + ContractDue + "." :
                    "Operating report " + ReportSequence + " available — hotel remains open");
            }
        }
    }
}
