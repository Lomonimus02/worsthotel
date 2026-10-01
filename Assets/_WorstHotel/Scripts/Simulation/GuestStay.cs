using System;
using System.Collections.Generic;
using System.Linq;

namespace WorstHotel
{
    public sealed class GuestStay
    {
        internal bool IsReplica;
        public BookingApplication Application { get; }
        public string GuestId => Application.Id;
        public string Name => Application.GuestName;
        public int RoomId { get; internal set; }
        public int Price { get; }
        public GuestAgent Agent { get; internal set; }
        public GuestNeeds Needs { get; internal set; }
        public GuestMemory Memory { get; } = new GuestMemory();
        public GuestPerception Perception { get; } = new GuestPerception();
        public GuestEarlyCheckout EarlyCheckout { get; } = new GuestEarlyCheckout();
        public float BlanketComfortBonus { get; internal set; }
        public float ServiceSatisfactionAdjustment { get; internal set; }
        public bool LockedOut { get; internal set; }
        public bool KeyLossConsidered { get; internal set; }
        public bool AbandonedCheckIn { get; internal set; }
        public float LockoutSeconds { get; internal set; }
        public float LuggageDelaySeconds { get; internal set; }
        public float CheckInWaitingSeconds { get; internal set; }
        public float CheckInDelayPenaltySeconds { get; internal set; }
        public float Elapsed { get; internal set; }
        public float QualityIntegral { get; internal set; }
        public float ExpiredComplaintSeconds { get; internal set; }
        public int CompensationCredit { get; internal set; }
        public bool Compensated { get; internal set; }
        public bool ReceiptPosted { get; internal set; }
        public float ColdExposureSeconds { get; internal set; }
        public float HotExposureSeconds { get; internal set; }
        public float NoiseExposureSeconds { get; internal set; }
        public float DirtyExposureSeconds { get; internal set; }
        public float FixtureExposureSeconds { get; internal set; }
        public float PowerLossExposureSeconds { get; internal set; }

        public GuestStay(BookingApplication application, int roomId, int price)
        {
            if (roomId <= 0 || price <= 0) throw new ArgumentException("Guest stay requires a valid room and positive price.");
            Application = application ?? throw new ArgumentNullException(nameof(application));
            RoomId = roomId; Price = price;
        }
    }

    public sealed class GuestReceipt
    {
        public string GuestId { get; }
        public string Name { get; }
        public int RoomId { get; }
        /// <summary>The booked amount, retained even when no room time was charged. Never used in settlement totals.</summary>
        public int AgreedPrice { get; }
        public int Price { get; }
        public float Satisfaction { get; }
        public int Compensation { get; }
        public int Net => Price - Compensation;
        public string Review { get; }
        public bool EarlyCheckout { get; }
        public float CheckoutAt { get; }
        public string DepartureReason { get; }

        public GuestReceipt(string guestId, string name, int roomId, int price, float satisfaction, int compensation, string review,
            bool earlyCheckout = false, float checkoutAt = -1, string departureReason = null, int agreedPrice = 0)
        {
            GuestId = guestId; Name = name; RoomId = roomId; Price = price;
            AgreedPrice = agreedPrice == 0 ? price : agreedPrice;
            Satisfaction = satisfaction; Compensation = compensation; Review = review;
            EarlyCheckout = earlyCheckout; CheckoutAt = checkoutAt; DepartureReason = departureReason;
        }
    }

    public sealed class DayReport
    {
        public int DayNumber { get; }
        public IReadOnlyList<GuestReceipt> Receipts { get; }
        public int OpeningCash { get; }
        public int Gross { get; }
        public int Compensation { get; }
        public int OperatingCost { get; }
        public int MaintenanceSpend { get; }
        public int CapitalSpend { get; }
        public int LaundrySpend { get; }
        public int BulbSpend { get; }
        public ContractPayment ContractPayment { get; }
        public int Net => Gross - Compensation - OperatingCost - MaintenanceSpend - CapitalSpend - LaundrySpend - BulbSpend;
        public int Cash { get; }
        public float Reputation { get; }
        public float AverageSatisfaction { get; }
        public float ServiceSeconds { get; }

        public DayReport(int dayNumber, IEnumerable<GuestReceipt> receipts, int openingCash, int operatingCost,
            int cash, float reputation, float serviceSeconds, int maintenanceSpend = 0, int capitalSpend = 0,
            ContractPayment contractPayment = null, int laundrySpend = 0, int bulbSpend = 0)
        {
            var copy = receipts.ToArray();
            if (operatingCost < 0 || maintenanceSpend < 0 || capitalSpend < 0 || laundrySpend < 0 || bulbSpend < 0 ||
                (long)operatingCost + maintenanceSpend + capitalSpend + laundrySpend + bulbSpend > int.MaxValue)
                throw new ArgumentException("Invalid report expense totals.");
            MaintenanceSpend = maintenanceSpend;
            CapitalSpend = capitalSpend;
            LaundrySpend = laundrySpend;
            BulbSpend = bulbSpend;
            // A payment is an earlier transaction; more income/costs can occur before this report.
            ContractPayment = contractPayment;
            DayNumber = dayNumber; Receipts = Array.AsReadOnly(copy); OpeningCash = openingCash;
            Gross = copy.Sum(receipt => receipt.Price); Compensation = copy.Sum(receipt => receipt.Compensation);
            OperatingCost = operatingCost; Cash = cash; Reputation = reputation; ServiceSeconds = serviceSeconds;
            AverageSatisfaction = copy.Length == 0 ? 0 : copy.Average(receipt => receipt.Satisfaction);
        }
    }
}
