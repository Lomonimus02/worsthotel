using System;
using System.Collections.Generic;
using System.Linq;

namespace WorstHotel
{
    public sealed partial class EconomySystem
    {
        public int Cash { get; private set; }
        public float Reputation { get; private set; }
        private readonly EconomySettings settings;
        private readonly Dictionary<int, DayReport> settledDays = new Dictionary<int, DayReport>();

        public EconomySystem(EconomySettings settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Cash = settings.StartingCash; Reputation = settings.InitialReputation;
        }

        public CommandResult ReserveCompensation(GuestStay stay)
        {
            if (ReadOnlyMirror) return CommandResult.Fail(HotelSimulation.MirrorMessage);
            if (stay == null) return CommandResult.Fail("Guest not found.");
            if (stay.Compensated) return CommandResult.Fail("A compensation credit has already been promised to this guest.");
            stay.Compensated = true;
            stay.CompensationCredit = Math.Min(stay.Price, RoundMoney(stay.Price * settings.CompensationRate));
            return CommandResult.Ok("$" + stay.CompensationCredit + " credit reserved for checkout. The room's underlying problem remains.");
        }

        public GuestReceipt CalculateReceipt(GuestStay stay, float satisfaction)
        {
            if (stay == null || !Number.IsFinite(satisfaction)) throw new ArgumentException("A valid stay and finite score are required.");
            satisfaction = Number.Clamp(satisfaction, 0, 100);
            bool earlyCheckout = stay.EarlyCheckout.State == EarlyCheckoutState.Committed;
            // A sustained, warned departure is a negative stay outcome even if earlier
            // comfortable hours diluted the integral. Reuse the existing severe-score boundary.
            if (earlyCheckout) satisfaction = Math.Min(satisfaction, settings.SevereRefundThreshold);
            float refundRate = satisfaction < settings.SevereRefundThreshold ? settings.SevereRefundRate :
                satisfaction < settings.PartialRefundThreshold ? settings.PartialRefundRate : 0;
            if (earlyCheckout) refundRate = Math.Max(refundRate, settings.SevereRefundRate);
            int requiredRefund = RoundMoney(stay.Price * refundRate);
            int compensation = Math.Min(stay.Price, Math.Max(stay.CompensationCredit, requiredRefund));
            string review = ReviewSystem.Build(stay, satisfaction, compensation);
            if (earlyCheckout) review = stay.EarlyCheckout.CauseDescription + " " + review;
            return new GuestReceipt(stay.GuestId, stay.Name, stay.RoomId, stay.Price, satisfaction, compensation, review,
                earlyCheckout, earlyCheckout ? stay.EarlyCheckout.CommittedAt : -1,
                earlyCheckout ? stay.EarlyCheckout.CauseDescription : null);
        }

        public DayReport Settle(int dayNumber, IEnumerable<GuestReceipt> receipts, float serviceSeconds)
        {
            if (ReadOnlyMirror) throw new InvalidOperationException(HotelSimulation.MirrorMessage);
            if (settledDays.TryGetValue(dayNumber, out var existing)) return existing;
            if (dayNumber <= 0 || receipts == null || !Number.IsFinite(serviceSeconds) || serviceSeconds < 0)
                throw new ArgumentException("Invalid settlement.");
            var copy = receipts.ToArray();
            if (copy.Select(receipt => receipt.GuestId).Distinct().Count() != copy.Length)
                throw new ArgumentException("A guest cannot be paid twice in one settlement.");
            int openingCash = Cash;
            long nextCash = (long)Cash + copy.Sum(receipt => receipt.Net) - settings.DailyOperatingCost;
            if (nextCash < int.MinValue || nextCash > int.MaxValue) throw new InvalidOperationException("Settlement exceeds the supported cash range; reduce the developer cash override.");
            Cash = (int)nextCash;
            float average = copy.Length == 0 ? 0 : copy.Average(receipt => receipt.Satisfaction);
            Reputation = Number.Clamp(Reputation + (average - settings.ReputationTarget) * settings.ReputationChangeFactor, 0, 100);
            var report = new DayReport(dayNumber, copy, openingCash, settings.DailyOperatingCost, Cash, Reputation, serviceSeconds);
            settledDays.Add(dayNumber, report);
            return report;
        }

        private static int RoundMoney(float value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

        public CommandResult TrySpend(int cost)
        {
            if (ReadOnlyMirror) return CommandResult.Fail(HotelSimulation.MirrorMessage);
            if (cost < 0) return CommandResult.Fail("A purchase cost cannot be negative.");
            if (Cash < cost) return CommandResult.Fail("Not enough cash for this maintenance choice.");
            Cash -= cost;
            return CommandResult.Ok();
        }

        public CommandResult DebugSetCash(float amount)
        {
            if (ReadOnlyMirror) return CommandResult.Fail(HotelSimulation.MirrorMessage);
            if (!Number.IsFinite(amount)) return CommandResult.Fail("Cash must be finite.");
            double rounded = Math.Round((double)amount, MidpointRounding.AwayFromZero);
            if (rounded < int.MinValue || rounded > int.MaxValue) return CommandResult.Fail("Cash is outside the supported integer range.");
            Cash = (int)rounded;
            return CommandResult.Ok("Cash changed by developer override.");
        }
    }
}


