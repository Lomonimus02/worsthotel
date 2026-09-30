using System;
using System.Collections.Generic;
using System.Linq;

namespace WorstHotel
{
    internal static partial class SnapshotValidation
    {
        // Canonicalize only the incoming wire envelope, before validation/construction and
        // before any replica mutation. Required reports themselves are never discarded.
        static void NormalizeFinancialOptionals(HotelModelSnapshot model)
        {
            if (model.Reports != null)
                foreach (var report in model.Reports)
                    if (report != null) report.ContractPayment = SnapshotData.OptionalPayment(report.ContractPayment);
            var data = model.Operations;
            if (data == null) return;
            data.Contract = SnapshotData.OptionalContract(data.Contract);
            data.LastContractPayment = SnapshotData.OptionalPayment(data.LastContractPayment);
            data.PeriodContractPayment = SnapshotData.OptionalPayment(data.PeriodContractPayment);
            data.OwnershipLossReport = SnapshotData.OptionalFinancialReport(data.OwnershipLossReport);
            if (data.OwnershipLossReport != null)
                data.OwnershipLossReport.ContractPayment = SnapshotData.OptionalPayment(data.OwnershipLossReport.ContractPayment);
        }

        static float ReportPeriodStart(HotelCalendar calendar, int report) => report == 1 ? 0 :
            calendar.FirstReportAt + (report - 2) * calendar.Settings.SecondsPerDay;
        static float ReportPeriodEnd(HotelCalendar calendar, int report) =>
            calendar.FirstReportAt + (report - 1) * calendar.Settings.SecondsPerDay;

        // Use the same float boundary expression as the host. A double-only quotient can put
        // a rounded deadline on the wrong side of an exact snapshot timestamp.
        static int FinancialOccurrencesAt(float first, float interval, float time)
        {
            Require(Number.IsFinite(first) && first > 0 && Number.IsFinite(interval) && interval >= 24 &&
                Number.IsFinite(time) && time >= 0, "Invalid financial schedule.");
            if (time < first) return 0;
            double estimate = Math.Floor(((double)time - first) / interval) + 1;
            Require(estimate >= 0 && estimate <= 1000000, "Financial sequence exceeds the supported calendar.");
            int count = (int)estimate;
            while (count > 0 && first + (count - 1) * interval > time) count--;
            while (count < 1000000 && first + count * interval <= time) count++;
            Require(first + count * interval > time, "Financial sequence exceeds the supported calendar.");
            return count;
        }

        static void ValidateOperatingSpend(int spend, float start, float end, HotelCalendar calendar, EconomySettings economy)
        {
            float first = calendar.FirstOccurrenceAt(calendar.Settings.OperatingCostHour);
            int count = FinancialOccurrencesAt(first, calendar.Settings.SecondsPerDay, end) -
                FinancialOccurrencesAt(first, calendar.Settings.SecondsPerDay, start);
            Require(count >= 0 && count <= 1 && spend == (long)count * economy.DailyOperatingCost,
                "Operating spending differs from the posted charges in this interval.");
        }

        static void FinancialReport(ReportSnapshot report, IReadOnlyCollection<int> roomIds, int maximumDay, float time, bool continuous)
        {
            Require(report != null && report.Day >= 1 && report.Day <= maximumDay && report.OperatingCost >= 0 &&
                report.MaintenanceSpend >= 0 && report.CapitalSpend >= 0 &&
                (long)report.OperatingCost + report.MaintenanceSpend + report.CapitalSpend <= int.MaxValue &&
                (continuous || report.MaintenanceSpend == 0 && report.CapitalSpend == 0 && report.ContractPayment == null),
                "Invalid financial report.");
            Range(report.Reputation, 0, 100); Range(report.ServiceSeconds);
            var receipts = Array(report.Receipts, continuous ? 128 : 6);
            Unique(receipts.Select(receipt => receipt.GuestId));
            foreach (var receipt in receipts)
            {
                Text(receipt.GuestId); Text(receipt.Name); Text(receipt.Review, 4096, true);
                Require(roomIds.Contains(receipt.RoomId) && receipt.Price >= 0 && receipt.Compensation >= 0 &&
                    receipt.Compensation <= receipt.Price, "Invalid report receipt.");
                Range(receipt.Satisfaction, 0, 100); DepartureReceipt(receipt, time);
            }
            Require(receipts.Sum(receipt => (long)receipt.Price) <= int.MaxValue &&
                receipts.Sum(receipt => (long)receipt.Compensation) <= int.MaxValue, "Report totals overflow.");
            if (report.ContractPayment != null) FinancialPayment(report.ContractPayment, roomIds.Count);
            // DebugSetCash intentionally bypasses the transaction ledger. Also, normal spending
            // and checkout income may follow a payment before the report's closing cash is read.
        }

        static void FinancialPayment(ContractPaymentSnapshot payment, int roomCount)
        {
            Require(payment != null && payment.AssessedRooms >= 1 && payment.AssessedRooms <= roomCount,
                "Invalid payment room assessment.");
            SnapshotData.Payment(payment); // Validate outcome, amount, period and finite timestamp before mutation.
        }

        internal static bool SamePayment(ContractPaymentSnapshot a, ContractPaymentSnapshot b) =>
            a == null || b == null ? a == null && b == null :
            a.Period == b.Period && a.DueAt == b.DueAt && a.Due == b.Due && a.PaidAmount == b.PaidAmount &&
            a.FundsBeforePayment == b.FundsBeforePayment && a.AssessedRooms == b.AssessedRooms;

        internal static bool SameFinancialReceipts(ReceiptSnapshot[] a, ReceiptSnapshot[] b) =>
            a != null && b != null && a.Length == b.Length && a.Zip(b, (x, y) =>
                x.GuestId == y.GuestId && x.Name == y.Name && x.RoomId == y.RoomId && x.Price == y.Price &&
                x.Compensation == y.Compensation && x.Satisfaction == y.Satisfaction && x.Review == y.Review &&
                x.EarlyCheckout == y.EarlyCheckout && x.CheckoutAt == y.CheckoutAt && x.DepartureReason == y.DepartureReason).All(equal => equal);

        internal static bool SameFinancialReport(ReportSnapshot a, ReportSnapshot b, bool includeReputation = true) =>
            a == null || b == null ? a == null && b == null :
            a.Day == b.Day && a.OpeningCash == b.OpeningCash && a.Cash == b.Cash && a.OperatingCost == b.OperatingCost &&
            a.MaintenanceSpend == b.MaintenanceSpend && a.CapitalSpend == b.CapitalSpend && a.ServiceSeconds == b.ServiceSeconds &&
            (!includeReputation || a.Reputation == b.Reputation) && SamePayment(a.ContractPayment, b.ContractPayment) &&
            SameFinancialReceipts(a.Receipts, b.Receipts);

        internal static void OwnershipContract(HotelModelSnapshot model, OwnershipContractSettings expected, int[] roomIds,
            HotelCalendar calendar, EconomySettings economy)
        {
            var data = model.Operations;
            Require((data.Contract != null) == (expected != null), "Ownership contract mode differs from this hotel.");
            if (expected == null)
            {
                Require(!data.OwnershipLost && data.ContractBaseRooms == 0 && data.ContractAssessedRooms == 0 && data.ContractSequence == 0 &&
                    data.LastContractPayment == null && data.PeriodContractPayment == null && data.OwnershipLossReport == null &&
                    model.Reports.All(report => report.ContractPayment == null), "Disabled contract contains ownership state.");
                return;
            }
            var config = data.Contract.ToSettings();
            Require(config.BaseDue == expected.BaseDue && config.DailyIncrease == expected.DailyIncrease &&
                config.ExtraRoomCharge == expected.ExtraRoomCharge && config.PaymentHour == expected.PaymentHour &&
                config.FirstPaymentDay == expected.FirstPaymentDay, "Ownership contract settings differ from this hotel.");
            float first = calendar.At(config.FirstPaymentDay, config.PaymentHour), interval = calendar.Settings.SecondsPerDay;
            Require(data.ContractSequence == FinancialOccurrencesAt(first, interval, model.Time),
                "Contract attempts differ from the clock.");
            int baseRooms = roomIds.Count(id => id <= 106);
            int operationalRooms = data.NorthWingRestored ? roomIds.Length : baseRooms;
            bool ValidAssessment(int count) => count == baseRooms || data.NorthWingRestored && count == operationalRooms;
            Require(baseRooms > 0 && data.ContractBaseRooms == baseRooms && ValidAssessment(data.ContractAssessedRooms) &&
                (data.ContractSequence != 0 || data.ContractAssessedRooms == baseRooms), "Invalid locked contract room assessment.");

            void ValidatePayment(ContractPaymentSnapshot payment)
            {
                FinancialPayment(payment, roomIds.Length);
                Require(payment.Period <= data.ContractSequence && payment.DueAt == first + (payment.Period - 1) * interval &&
                    payment.DueAt <= model.Time && ValidAssessment(payment.AssessedRooms) &&
                    (payment.Period != 1 || payment.AssessedRooms == baseRooms) &&
                    payment.Due == config.AmountFor(payment.Period, payment.AssessedRooms, baseRooms), "Invalid dated contract assessment.");
                Require((payment.PaidAmount == 0) == (data.OwnershipLost && payment.Period == data.ContractSequence),
                    "Payment outcome differs from ownership state.");
            }
            void ValidateInterval(ContractPaymentSnapshot payment, float start, float end)
            {
                int endSequence = FinancialOccurrencesAt(first, interval, end);
                int count = endSequence - FinancialOccurrencesAt(first, interval, start);
                Require(count >= 0 && count <= 1 && (payment != null) == (count == 1),
                    "Accounting interval is missing its payment or contains an unexpected payment.");
                if (payment == null) return;
                ValidatePayment(payment);
                Require(payment.Period == endSequence && payment.DueAt > start && payment.DueAt <= end,
                    "Payment belongs to a different accounting interval.");
            }

            Require((data.LastContractPayment != null) == (data.ContractSequence > 0), "Missing or premature last contract payment.");
            if (data.LastContractPayment != null)
            {
                ValidatePayment(data.LastContractPayment);
                Require(data.LastContractPayment.Period == data.ContractSequence, "Last payment is not the latest attempt.");
            }
            int previousRooms = baseRooms;
            foreach (var report in model.Reports)
            {
                ValidateInterval(report.ContractPayment, ReportPeriodStart(calendar, report.Day), ReportPeriodEnd(calendar, report.Day));
                if (report.ContractPayment == null) continue;
                Require(report.ContractPayment.AssessedRooms >= previousRooms, "Historical room assessment cannot shrink.");
                previousRooms = report.ContractPayment.AssessedRooms;
                if (report.ContractPayment.Period == data.ContractSequence)
                    Require(SamePayment(report.ContractPayment, data.LastContractPayment), "Historical and last payment disagree.");
            }
            ValidateInterval(data.PeriodContractPayment, data.PeriodStartedAt, model.Time);
            if (data.PeriodContractPayment != null)
            {
                Require(SamePayment(data.PeriodContractPayment, data.LastContractPayment) &&
                    data.PeriodContractPayment.AssessedRooms >= previousRooms, "Current and last payment disagree.");
                previousRooms = data.PeriodContractPayment.AssessedRooms;
            }
            Require(data.ContractAssessedRooms >= previousRooms && (data.ContractSequence == 0 ||
                data.PeriodContractPayment != null || model.Reports.Any(report => SamePayment(report.ContractPayment, data.LastContractPayment))),
                "Latest payment is missing from the retained accounting interval.");
            Require((data.OwnershipLossReport != null) == data.OwnershipLost, "Missing or unexpected ownership loss report.");
            if (!data.OwnershipLost) return;

            var last = data.LastContractPayment;
            Require(last != null && !model.Running && model.Speed == 1 && model.Time == last.DueAt &&
                data.ContractAssessedRooms == last.AssessedRooms && (long)last.FundsBeforePayment - last.PaidAmount == model.Cash,
                "Lost ownership must stop at its failed payment with unchanged final cash.");
            var loss = data.OwnershipLossReport;
            FinancialReport(loss, roomIds, model.Day + 1, model.Time, true);
            bool closedAtFailure = data.ReportSequence > 0 && model.Time == data.PeriodStartedAt;
            int lossPeriod = closedAtFailure ? data.ReportSequence : data.ReportSequence + 1;
            float lossStart = ReportPeriodStart(calendar, lossPeriod);
            Require(loss.Day == lossPeriod && loss.Cash == model.Cash && SamePayment(loss.ContractPayment, last) &&
                Math.Abs(loss.ServiceSeconds - (model.Time - lossStart)) < .01f, "Invalid frozen ownership loss interval.");
            ValidateOperatingSpend(loss.OperatingCost, lossStart, model.Time, calendar, economy);
            if (closedAtFailure)
            {
                // Closing reputation is computed after the failure summary was frozen.
                Require(SameFinancialReport(loss, model.Reports.Last(), false) && data.PeriodReceipts.Length == 0 &&
                    data.PeriodOpeningCash == model.Cash && data.PeriodMaintenanceSpend == 0 && data.PeriodCapitalSpend == 0 &&
                    data.PeriodOperatingSpend == 0 && data.PeriodContractPayment == null,
                    "Coincident failure and report close must preserve the same posted transactions.");
            }
            else
                Require(loss.OpeningCash == data.PeriodOpeningCash && loss.OperatingCost == data.PeriodOperatingSpend &&
                    loss.MaintenanceSpend == data.PeriodMaintenanceSpend && loss.CapitalSpend == data.PeriodCapitalSpend &&
                    loss.Reputation == model.Reputation && SameFinancialReceipts(loss.Receipts, data.PeriodReceipts),
                    "Ownership loss report differs from the frozen open period.");
        }
    }
}
