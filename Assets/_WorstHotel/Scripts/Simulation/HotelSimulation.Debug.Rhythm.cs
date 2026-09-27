using System;
using System.Linq;

namespace WorstHotel
{
    public sealed partial class HotelSimulation
    {
        bool debugNextBookingDemand;
        public bool DebugNextBookingDemandPending => debugNextBookingDemand;

        CommandResult RhythmDebugGate()
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            return Running && ContinuousOperations ? CommandResult.Ok() :
                CommandResult.Fail("This diagnostic needs a running continuous hotel.");
        }

        public CommandResult DebugSetBoilerStress(float normalized)
        {
            var allowed = RhythmDebugGate(); if (!allowed.Success) return allowed;
            if (!Number.IsFinite(normalized) || normalized < 0 || normalized > 1)
                return CommandResult.Fail("Choose finite boiler stress between 0 and 1.");
            using (BeginDiagnosticInfrastructureChange()) return Boiler.DebugSetStress(normalized);
        }

        /// <summary>Edits the accumulated room-quality history behind the ordinary 0–100
        /// satisfaction calculation. Price, service penalties and current causes remain real.</summary>
        public CommandResult DebugSetGuestSatisfaction(string guestId, float target)
        {
            var allowed = RhythmDebugGate(); if (!allowed.Success) return allowed;
            if (!Number.IsFinite(target) || target < 0 || target > 100)
                return CommandResult.Fail("Choose finite satisfaction between 0 and 100.");
            var guest = guests.FirstOrDefault(item => item.GuestId == guestId);
            if (guest?.Agent == null || !guest.Agent.CheckedIn || !guest.Agent.HasReachedRoom ||
                guest.Needs == null || guest.Elapsed <= 0 || guest.ReceiptPosted ||
                guest.Agent.State == GuestAgentState.CheckingOut || guest.Agent.State == GuestAgentState.Leaving ||
                guest.Agent.State == GuestAgentState.Left)
                return CommandResult.Fail("Choose a current checked-in living guest with recorded room time.");
            var economy = settings.Economy;
            float serviceFraction = Number.Clamp(guest.Needs.ServiceIntegral /
                Math.Max(1, guest.Elapsed + guest.CheckInWaitingSeconds), 0, 1);
            float premium = Math.Max(0, (float)guest.Price / guest.Application.ReferencePrice - 1);
            float ceiling = 100 - guest.Application.Archetype.PriceSensitivity * premium -
                economy.PatiencePenalty * serviceFraction + (guest.Compensated ? economy.CompensationGoodwill : 0) +
                guest.ServiceSatisfactionAdjustment;
            float scale = economy.QualityPenaltyScale * Satisfaction.PriceExpectation(guest.Price, guest.Application.ReferencePrice);
            if (!Number.IsFinite(ceiling) || !Number.IsFinite(scale) || scale <= 0 || target > ceiling)
                return CommandResult.Fail("Existing price and service history limit attainable satisfaction; this command only changes room-quality history.");
            double quality = (double)(ceiling - target) * guest.Elapsed / scale;
            if (double.IsNaN(quality) || double.IsInfinity(quality) || quality < 0 || quality > float.MaxValue)
                return CommandResult.Fail("This history adjustment cannot be represented safely.");
            using (BeginDiagnosticInfrastructureChange()) guest.QualityIntegral = (float)quality;
            return CommandResult.Ok("Diagnostic: satisfaction history adjusted. Current needs, causes, memory and ordinary future scoring remain unchanged.");
        }

        /// <summary>Forces only the conversion roll of the next due enquiry, not its time,
        /// cursor, price, open-room policy or reservation availability.</summary>
        public CommandResult DebugForceNextBookingDemand()
        {
            var allowed = RhythmDebugGate(); if (!allowed.Success) return allowed;
            if (!AutomaticBookingsEnabled) return CommandResult.Fail("Ordinary automatic sales must be enabled.");
            if (!TryGetNextSalesDecision(out _, out _, out _)) return CommandResult.Fail("No future enquiry is currently scheduled.");
            using (BeginDiagnosticInfrastructureChange()) debugNextBookingDemand = true;
            return CommandResult.Ok("Diagnostic: only the next due enquiry ignores its demand roll. A real open, available room is still required.");
        }

        public CommandResult DebugClearNextBookingDemand()
        {
            var allowed = RhythmDebugGate(); if (!allowed.Success) return allowed;
            using (BeginDiagnosticInfrastructureChange()) debugNextBookingDemand = false;
            return CommandResult.Ok("Diagnostic enquiry override cleared.");
        }

        public CommandResult DebugPrimeEarlyCheckoutEligibility(string guestId)
        {
            var allowed = RhythmDebugGate(); if (!allowed.Success) return allowed;
            if (!LivingEnabled || NeedsSettings?.EarlyCheckout.Enabled != true)
                return CommandResult.Fail("The severe-problem early-checkout policy must be enabled.");
            var guest = guests.FirstOrDefault(item => item.GuestId == guestId);
            var agent = guest?.Agent;
            if (agent == null || !agent.CheckedIn || !agent.HasReachedRoom || !agent.InAssignedRoom || agent.IsRelocating ||
                guest.ReceiptPosted || guest.EarlyCheckout.State == EarlyCheckoutState.Committed ||
                !rooms.TryGetValue(guest.RoomId, out var room) || room.GuestId != guestId)
                return CommandResult.Fail("Choose a real checked-in guest currently inside the owned room.");
            var incident = Incidents.Items.Where(item => item.GuestId == guestId && item.RoomId == guest.RoomId &&
                    item.HasContactedStaff && EarlyCheckoutHistoryQualified(item))
                .Select(item => new { Incident = item, Severity = CurrentEarlyCheckoutSeverity(guest, item) })
                .Where(item => item.Severity > NeedsSettings.EarlyCheckout.SevereThreshold)
                .OrderByDescending(item => item.Severity).ThenBy(item => item.Incident.Id, StringComparer.Ordinal)
                .Select(item => item.Incident).FirstOrDefault();
            if (incident == null)
                return CommandResult.Fail("A current severe physical cause and an actual disclosed, escalated complaint are required.");
            var record = guest.EarlyCheckout;
            bool warning = record.State == EarlyCheckoutState.Warning && record.IncidentId == incident.Id &&
                record.IncidentEpisode == incident.EpisodeCount && record.RoomId == guest.RoomId;
            float normalStep = 1f / settings.TickRate;
            float remaining = EarlyCheckoutSeconds(NeedsSettings.EarlyCheckout.MinimumRemainingStayHours) + normalStep +
                (warning ? 0 : EarlyCheckoutSeconds(NeedsSettings.EarlyCheckout.GraceHours));
            if (agent.CheckoutTime - Elapsed < remaining)
                return CommandResult.Fail("Too little contracted room time remains for the normal warning and departure policy.");
            using (BeginDiagnosticInfrastructureChange())
            {
                if (!warning)
                {
                    if (record.IncidentId != incident.Id || record.IncidentEpisode != incident.EpisodeCount || record.RoomId != guest.RoomId)
                        record.Reset();
                    TrackEarlyCheckoutCause(record, incident);
                    record.SevereExposureSeconds = Math.Max(record.SevereExposureSeconds, EarlyCheckoutTrialSeconds(guest, incident));
                }
                else record.GraceRemainingSeconds = Math.Min(record.GraceRemainingSeconds, normalStep);
                record.RecoverySeconds = 0;
            }
            return CommandResult.Ok(warning ?
                "Diagnostic: existing warning grace is primed. The next ordinary tick still checks the current cause, dialogue and physical departure policy." :
                "Diagnostic: severe trial is primed. The next ordinary tick must issue the warning; its normal grace and physical checkout remain.");
        }
    }
}
