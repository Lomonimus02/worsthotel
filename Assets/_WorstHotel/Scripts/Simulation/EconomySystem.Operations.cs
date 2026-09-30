using System;
using System.Collections.Generic;
using System.Linq;

namespace WorstHotel
{
    public sealed partial class EconomySystem
    {
        int lastOperatingReport;
        internal void RestoreOperatingSequence(int number) => lastOperatingReport = number;

        internal void PayOwnershipContract(int amount)
        {
            if (ReadOnlyMirror || OwnershipRevoked || amount <= 0 || Cash < amount)
                throw new InvalidOperationException("Contract payment is not authorized or affordable.");
            Cash -= amount;
        }

        internal void PostCheckout(GuestReceipt receipt)
        {
            if (ReadOnlyMirror) throw new InvalidOperationException(HotelSimulation.MirrorMessage);
            if (OwnershipRevoked) throw new InvalidOperationException("Closed ownership cannot collect future checkout income.");
            if (receipt == null) throw new ArgumentNullException(nameof(receipt));
            long next = (long)Cash + receipt.Net;
            if (next > int.MaxValue || next < int.MinValue) throw new InvalidOperationException("Checkout exceeds the supported cash range.");
            Cash = (int)next;
        }

        // Receipts have already been paid at checkout. Closing a period never posts revenue again.
        internal DayReport CloseOperatingDay(int number, IEnumerable<GuestReceipt> receipts, int openingCash, float seconds, int maintenanceSpend = 0, int capitalSpend = 0)
        {
            if (ReadOnlyMirror) throw new InvalidOperationException(HotelSimulation.MirrorMessage);
            if (number != lastOperatingReport + 1 || receipts == null || !Number.IsFinite(seconds) || seconds <= 0 || maintenanceSpend < 0 || capitalSpend < 0)
                throw new ArgumentException("Operating reports must close consecutive positive intervals exactly once.");
            var copy = receipts.ToArray();
            if (copy.Any(receipt => receipt == null) || copy.Select(receipt => receipt.GuestId).Distinct().Count() != copy.Length)
                throw new ArgumentException("Operating report receipts must be unique.");
            long nextCash = (long)Cash - settings.DailyOperatingCost;
            if (nextCash < int.MinValue) throw new InvalidOperationException("Operating expense exceeds the supported cash range.");
            float nextReputation = Reputation;
            if (copy.Length > 0)
                nextReputation = Number.Clamp(Reputation + (copy.Average(receipt => receipt.Satisfaction) - settings.ReputationTarget) * settings.ReputationChangeFactor, 0, 100);
            var report = new DayReport(number, copy, openingCash, settings.DailyOperatingCost, (int)nextCash, nextReputation, seconds, maintenanceSpend, capitalSpend);
            Cash = (int)nextCash;
            Reputation = nextReputation;
            lastOperatingReport = number;
            return report;
        }
    }
}
