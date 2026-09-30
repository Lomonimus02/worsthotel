using System.Linq;

namespace WorstHotel
{
    public sealed partial class HotelSimulation
    {
        public int PeriodOpeningCash => periodOpeningCash;
        public float PeriodStartedAt => periodStartedAt;
        System.Collections.ObjectModel.ReadOnlyCollection<GuestReceipt> currentReceiptsView;
        /// <summary>Already-posted, immutable receipts; the live view rolls over when a report is published.</summary>
        public System.Collections.Generic.IReadOnlyList<GuestReceipt> CurrentReceipts => currentReceiptsView ??= periodReceipts.AsReadOnly();
        public long PeriodGross => periodReceipts.Sum(receipt => (long)receipt.Price);
        public long PeriodCompensation => periodReceipts.Sum(receipt => (long)receipt.Compensation);
        public long PeriodCheckoutIncome => periodReceipts.Sum(receipt => (long)receipt.Net);
        /// <summary>Nominal gross of active dated commitments. This is unpaid potential revenue, not cash or guaranteed income.</summary>
        public long UnpaidBookedRevenue => reservations.Where(reservation => reservation.Active)
            .Sum(reservation => (long)reservation.Price);
    }
}
