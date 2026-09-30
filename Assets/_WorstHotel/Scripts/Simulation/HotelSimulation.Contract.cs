namespace WorstHotel
{
    public sealed partial class HotelSimulation
    {
        public bool ContractEnabled => Operations?.Contract != null;
        public bool OwnershipLost { get; private set; }
        public int ContractBaseRooms { get; private set; }
        public int ContractAssessedRooms { get; private set; }
        public int ContractSequence { get; private set; }
        public ContractPayment LastContractPayment { get; private set; }
        public ContractPayment PeriodContractPayment { get; private set; }
        public DayReport OwnershipLossReport { get; private set; }
        public float FirstContractAt => ContractEnabled ? Calendar.At(Operations.Contract.FirstPaymentDay, Operations.Contract.PaymentHour) : float.PositiveInfinity;
        public float NextContractAt => ContractEnabled ? FirstContractAt + ContractSequence * Operations.SecondsPerDay : float.PositiveInfinity;
        public int ContractDailyIncrease => Operations?.Contract?.DailyIncrease ?? 0;
        public int ContractRoomSurcharge => Operations?.Contract?.ExtraRoomCharge ?? 0;
        public int ContractDue => !ContractEnabled ? 0 : OwnershipLost ? LastContractPayment.Due :
            Operations.Contract.AmountFor(ContractSequence + 1, ContractAssessedRooms, ContractBaseRooms);
        public int NextContractDue => NextContractDueWithRooms(OperationalRoomCount);
        public int NextContractDueWithRooms(int restoredRooms) => !ContractEnabled || OwnershipLost ? 0 :
            Operations.Contract.AmountFor(ContractSequence + 2, restoredRooms, ContractBaseRooms);

        void InitializeContract()
        {
            if (!ContractEnabled) return;
            ContractBaseRooms = ContractAssessedRooms = OperationalRoomCount;
            // Validate the opening assessment before any hotel transactions can occur.
            Operations.Contract.AmountFor(1, ContractAssessedRooms, ContractBaseRooms);
            if (FirstContractAt <= 0) throw new System.ArgumentException("The first ownership payment must be after opening.");
        }

        void SettleDueOwnershipContract()
        {
            if (!ContractEnabled || !Running || Elapsed < NextContractAt) return;
            int due = ContractDue;
            int available = Economy.Cash;
            int paid = available >= due ? due : 0;
            var payment = new ContractPayment(due, paid, available, ContractAssessedRooms, ContractSequence + 1, NextContractAt);
            // The deadline reads today's actual cash. No future operating bill or report participates.
            if (PeriodContractPayment != null) throw new System.InvalidOperationException("A daily report cannot contain two ownership payments.");
            if (paid > 0) Economy.PayOwnershipContract(paid);
            LastContractPayment = PeriodContractPayment = payment;
            ContractSequence++;
            if (payment.OwnershipLost)
            {
                OwnershipLossReport = new DayReport(ReportSequence + 1, periodReceipts, periodOpeningCash,
                    PeriodOperatingSpend, Economy.Cash, Economy.Reputation, Elapsed - periodStartedAt,
                    PeriodMaintenanceSpend, PeriodCapitalSpend, payment, laundrySpend: PeriodLaundrySpend, bulbSpend: PeriodBulbSpend);
                OwnershipLost = true;
                Running = false;
                Economy.OwnershipRevoked = true;
                Clock.SetSpeed(1);
            }
            else ContractAssessedRooms = OperationalRoomCount;
            SignalEvent(OwnershipLost ? "OWNERSHIP REVOKED — payment short by $" + payment.Shortfall + ". See the notice." :
                "Contract payment $" + paid + " complete. Receipt in ACCOUNTS; next obligation $" + ContractDue + ".");
        }
    }
}
