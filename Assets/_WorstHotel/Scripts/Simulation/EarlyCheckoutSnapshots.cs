using System;
using System.Collections.Generic;
using System.Linq;

namespace WorstHotel
{
    [Serializable] public sealed class EarlyCheckoutSnapshot
    {
        public EarlyCheckoutState State;
        public string IncidentId, CauseDescription;
        public int IncidentEpisode, RoomId;
        public IncidentReason Reason;
        public float SevereExposureSeconds, RecoverySeconds, GraceRemainingSeconds;
        public float WarningAt = -1, CommittedAt = -1;
    }

    internal static partial class SnapshotData
    {
        internal static EarlyCheckoutSnapshot Capture(GuestEarlyCheckout value) => new EarlyCheckoutSnapshot
        {
            State = value.State, IncidentId = value.IncidentId, IncidentEpisode = value.IncidentEpisode,
            RoomId = value.RoomId, Reason = value.Reason, SevereExposureSeconds = value.SevereExposureSeconds,
            RecoverySeconds = value.RecoverySeconds, GraceRemainingSeconds = value.GraceRemainingSeconds,
            WarningAt = value.WarningAt, CommittedAt = value.CommittedAt, CauseDescription = value.CauseDescription
        };

        internal static void Restore(GuestEarlyCheckout target, EarlyCheckoutSnapshot value)
        {
            target.State = value.State; target.IncidentId = OptionalId(value.IncidentId);
            target.IncidentEpisode = value.IncidentEpisode; target.RoomId = value.RoomId; target.Reason = value.Reason;
            target.SevereExposureSeconds = value.SevereExposureSeconds; target.RecoverySeconds = value.RecoverySeconds;
            target.GraceRemainingSeconds = value.GraceRemainingSeconds; target.WarningAt = value.WarningAt;
            target.CommittedAt = value.CommittedAt; target.CauseDescription = OptionalId(value.CauseDescription);
        }
    }

    internal static partial class SnapshotValidation
    {
        internal static void EarlyDepartures(HotelModelSnapshot model, EarlyCheckoutSettings tuning,
            OperationsSettings operations, EconomySettings economy, IReadOnlyCollection<int> roomIds)
        {
            var receipts = model.Reports.SelectMany(report => report.Receipts)
                .Concat(model.HasOperations ? model.Operations.PeriodReceipts : System.Array.Empty<ReceiptSnapshot>()).ToArray();
            Unique(receipts.Where(item => item.EarlyCheckout).Select(item => item.GuestId));
            foreach (var receipt in receipts.Where(item => item.EarlyCheckout))
            {
                Require(model.HasOperations, "Early departure requires continuous operations.");
                int minimumRefund = Math.Min(receipt.Price, (int)Math.Round(receipt.Price * economy.SevereRefundRate,
                    MidpointRounding.AwayFromZero));
                Require(receipt.Compensation >= minimumRefund && receipt.Satisfaction <= economy.SevereRefundThreshold,
                    "Early departure bill omits its severe outcome.");
            }
            foreach (var guest in model.Guests)
            {
                var value = guest.EarlyCheckout;
                Require(value != null, "Missing early-departure state.");
                EnumValue(value.State); EnumValue(value.Reason);
                OptionalId(value.IncidentId, 512); OptionalId(value.CauseDescription, 2048);
                Range(value.SevereExposureSeconds, 0, model.Time);
                Range(value.RecoverySeconds, 0, model.Time);
                Range(value.GraceRemainingSeconds);
                Range(value.WarningAt, -1, model.Time); Range(value.CommittedAt, -1, model.Time);
                Require((value.WarningAt == -1 || value.WarningAt >= 0) &&
                    (value.CommittedAt == -1 || value.CommittedAt >= 0), "Invalid early-departure timestamp.");
                Require(value.IncidentEpisode >= 0 && value.IncidentEpisode <= 10000, "Invalid early-departure episode.");
                if (value.State == EarlyCheckoutState.None)
                {
                    Require(string.IsNullOrEmpty(value.IncidentId) && string.IsNullOrEmpty(value.CauseDescription) &&
                        value.RoomId == 0 && value.IncidentEpisode == 0 && value.Reason == default &&
                        value.SevereExposureSeconds == 0 && value.RecoverySeconds == 0 && value.GraceRemainingSeconds == 0 &&
                        value.WarningAt == -1 && value.CommittedAt == -1, "Empty early-departure state contains an outcome.");
                }
                else
                {
                    Require(model.HasOperations && tuning?.Enabled == true && guest.Agent != null && guest.Agent.CheckedIn &&
                        guest.Agent.HasReachedRoom && roomIds.Contains(value.RoomId), "Early-departure policy or stay is unavailable.");
                    bool physicalService = value.Reason == IncidentReason.Service && value.State == EarlyCheckoutState.Committed &&
                        (guest.LockoutSeconds >= Math.Max(35, guest.Agent.WaitingPatience) * 4 ||
                         guest.LuggageDelaySeconds >= Math.Max(35, guest.Agent.WaitingPatience) * 5);
                    Text(value.IncidentId, 512);
                    Require(value.IncidentEpisode >= 1, "Early-departure cause needs a real episode.");
                    Require(value.Reason == IncidentReason.Temperature || value.Reason == IncidentReason.Noise ||
                        value.Reason == IncidentReason.RoomCondition || physicalService, "Unsupported early-departure cause.");
                    Range(value.GraceRemainingSeconds, 0, (float)Math.Min(float.MaxValue, (double)operations.SecondsPerDay * tuning.GraceHours / 24));
                    Require(value.WarningAt == -1 || value.WarningAt >= guest.Agent.ArrivalTime, "Warning precedes arrival.");
                    if (value.State == EarlyCheckoutState.Committed)
                    {
                        Require(value.WarningAt >= 0 && value.CommittedAt >= value.WarningAt &&
                            value.CommittedAt < guest.Agent.CheckoutTime && value.GraceRemainingSeconds == 0 &&
                            (guest.Agent.State == GuestAgentState.CheckingOut || guest.Agent.State == GuestAgentState.Leaving ||
                            guest.Agent.State == GuestAgentState.Left), "Committed early departure has no completed stay.");
                        Text(value.CauseDescription, 2048);
                        // Old physical identities may outlive pruned reports, but an outcome
                        // posted in the current accounting period must still have its bill.
                        if (value.CommittedAt >= model.Operations.PeriodStartedAt)
                            Require(receipts.Count(item => item.GuestId == guest.Application.Id) == 1,
                                "Current early departure is missing its single checkout receipt.");
                    }
                    else
                    {
                        Require(value.CommittedAt == -1 && string.IsNullOrEmpty(value.CauseDescription) && !guest.ReceiptPosted,
                            "Pending early departure contains a terminal outcome.");
                        Require(value.State != EarlyCheckoutState.Warning || value.WarningAt >= 0, "Missing warning time.");
                        Require(value.State != EarlyCheckoutState.Monitoring || value.GraceRemainingSeconds == 0,
                            "Monitoring cannot consume warning grace.");
                        // A move or resolution command may precede the next policy tick. Retain
                        // the factual old episode, while the normal UI hides this stale warning.
                        Require(model.Incidents.Any(item => item.Id == value.IncidentId && item.GuestId == guest.Application.Id &&
                            item.Reason == value.Reason && item.EpisodeCount >= value.IncidentEpisode), "Unknown early-departure cause.");
                    }
                }
                foreach (var receipt in receipts.Where(item => item.GuestId == guest.Application.Id))
                    Require(receipt.EarlyCheckout == (value.State == EarlyCheckoutState.Committed) &&
                        (!receipt.EarlyCheckout || receipt.CheckoutAt == value.CommittedAt && receipt.RoomId == value.RoomId &&
                        receipt.DepartureReason == value.CauseDescription && receipt.Price == guest.Price &&
                        receipt.Compensation >= guest.CompensationCredit), "Early-departure record and receipt differ.");
            }
        }

        internal static void DepartureReceipt(ReceiptSnapshot receipt, float now)
        {
            Range(receipt.CheckoutAt, -1, now);
            Require(receipt.CheckoutAt == -1 || receipt.CheckoutAt >= 0, "Invalid actual checkout time.");
            if (receipt.EarlyCheckout)
            {
                Require(receipt.CheckoutAt >= 0, "Early checkout needs an actual departure time.");
                Text(receipt.DepartureReason, 2048);
            }
            else Require(string.IsNullOrEmpty(receipt.DepartureReason), "Ordinary checkout cannot carry an early-departure reason.");
        }
    }
}
