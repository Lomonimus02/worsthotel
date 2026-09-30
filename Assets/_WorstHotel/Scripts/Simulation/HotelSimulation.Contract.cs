namespace WorstHotel
{
    public sealed partial class HotelSimulation
    {
        public bool ContractEnabled => Operations?.Contract != null;
        public bool OwnershipLost { get; private set; }
        public int ContractBaseRooms { get; private set; }
        public int ContractAssessedRooms { get; private set; }
        public int ContractDailyIncrease => Operations?.Contract?.DailyIncrease ?? 0;
        public int ContractRoomSurcharge => Operations?.Contract?.ExtraRoomCharge ?? 0;
        public int ContractDue => !ContractEnabled ? 0 : OwnershipLost ? LastReport.ContractPayment.Due :
            Operations.Contract.AmountFor(ReportSequence + 1, ContractAssessedRooms, ContractBaseRooms);
        public int NextContractDue => NextContractDueWithRooms(OperationalRoomCount);
        public int NextContractDueWithRooms(int restoredRooms) => !ContractEnabled || OwnershipLost ? 0 :
            Operations.Contract.AmountFor(ReportSequence + 2, restoredRooms, ContractBaseRooms);

        void InitializeContract()
        {
            if (!ContractEnabled) return;
            ContractBaseRooms = ContractAssessedRooms = OperationalRoomCount;
            // Validate the opening assessment before any hotel transactions can occur.
            Operations.Contract.AmountFor(1, ContractAssessedRooms, ContractBaseRooms);
        }

        DayReport SettleOwnershipContract(DayReport operatingReport)
        {
            if (!ContractEnabled) return operatingReport;
            int due = ContractDue;
            int available = Economy.Cash;
            int paid = available >= due ? due : 0;
            var payment = new ContractPayment(due, paid, available, ContractAssessedRooms);
            // The ordinary economy has already posted all receipts and purchase costs.
            // This is a separate debit, not a revenue target or an extra operating expense.
            if (paid > 0) Economy.PayOwnershipContract(paid);
            if (payment.OwnershipLost)
            {
                OwnershipLost = true;
                Running = false;
                Economy.OwnershipRevoked = true;
                Clock.SetSpeed(1);
            }
            return new DayReport(operatingReport.DayNumber, operatingReport.Receipts, operatingReport.OpeningCash,
                operatingReport.OperatingCost, Economy.Cash, operatingReport.Reputation, operatingReport.ServiceSeconds,
                operatingReport.MaintenanceSpend, operatingReport.CapitalSpend, payment);
        }
    }
}
