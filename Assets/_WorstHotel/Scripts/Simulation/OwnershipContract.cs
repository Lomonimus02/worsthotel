using System;

namespace WorstHotel
{
    /// <summary>Predictable ownership costs, independent of hotel revenue and operating profit.</summary>
    public sealed class OwnershipContractSettings
    {
        public int BaseDue { get; }
        public int DailyIncrease { get; }
        public int ExtraRoomCharge { get; }

        public OwnershipContractSettings(int baseDue = 250, int dailyIncrease = 25, int extraRoomCharge = 60)
        {
            if (baseDue <= 0 || dailyIncrease <= 0 || extraRoomCharge < 0)
                throw new ArgumentException("A contract needs a positive initial payment and daily increase, and a nonnegative room charge.");
            BaseDue = baseDue; DailyIncrease = dailyIncrease; ExtraRoomCharge = extraRoomCharge;
        }

        public int AmountFor(int period, int assessedRooms, int baseRooms)
        {
            if (period < 1 || baseRooms < 1 || assessedRooms < baseRooms)
                throw new ArgumentException("Invalid contract period or physical room assessment.");
            long amount = BaseDue + (long)(period - 1) * DailyIncrease + (long)(assessedRooms - baseRooms) * ExtraRoomCharge;
            return (int)Math.Min(int.MaxValue, amount);
        }
    }

    /// <summary>A frozen settlement receipt. Failed payments take no partial funds and create no rolling debt.</summary>
    public sealed class ContractPayment
    {
        public int Due { get; }
        public int PaidAmount { get; }
        public int FundsBeforePayment { get; }
        public int AssessedRooms { get; }
        public bool OwnershipLost => PaidAmount != Due;
        public long Shortfall => Math.Max(0L, (long)Due - FundsBeforePayment);
        public int CashAfterPayment => FundsBeforePayment - PaidAmount;

        public ContractPayment(int due, int paidAmount, int fundsBeforePayment, int assessedRooms)
        {
            if (due <= 0 || assessedRooms < 1 || (paidAmount != 0 && paidAmount != due) ||
                (fundsBeforePayment >= due) != (paidAmount == due))
                throw new ArgumentException("Contract payment does not match its available funds.");
            Due = due; PaidAmount = paidAmount; FundsBeforePayment = fundsBeforePayment; AssessedRooms = assessedRooms;
        }
    }
}
