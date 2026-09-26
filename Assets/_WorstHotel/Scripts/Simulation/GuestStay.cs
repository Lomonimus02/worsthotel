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
        public float BlanketComfortBonus { get; internal set; }
        public float ServiceSatisfactionAdjustment { get; internal set; }
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
        public int Price { get; }
        public float Satisfaction { get; }
        public int Compensation { get; }
        public int Net => Price - Compensation;
        public string Review { get; }

        public GuestReceipt(string guestId, string name, int roomId, int price, float satisfaction, int compensation, string review)
        {
            GuestId = guestId; Name = name; RoomId = roomId; Price = price;
            Satisfaction = satisfaction; Compensation = compensation; Review = review;
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
        public int Net => Gross - Compensation - OperatingCost - MaintenanceSpend;
        public int Cash { get; }
        public float Reputation { get; }
        public float AverageSatisfaction { get; }
        public float ServiceSeconds { get; }

        public DayReport(int dayNumber, IEnumerable<GuestReceipt> receipts, int openingCash, int operatingCost,
            int cash, float reputation, float serviceSeconds, int maintenanceSpend = 0)
        {
            var copy = receipts.ToArray();
            if (maintenanceSpend < 0 || (long)operatingCost + maintenanceSpend > int.MaxValue)
                throw new ArgumentException("Invalid report expense totals.");
            MaintenanceSpend = maintenanceSpend;
            DayNumber = dayNumber; Receipts = Array.AsReadOnly(copy); OpeningCash = openingCash;
            Gross = copy.Sum(receipt => receipt.Price); Compensation = copy.Sum(receipt => receipt.Compensation);
            OperatingCost = operatingCost; Cash = cash; Reputation = reputation; ServiceSeconds = serviceSeconds;
            AverageSatisfaction = copy.Length == 0 ? 0 : copy.Average(receipt => receipt.Satisfaction);
        }
    }
}
