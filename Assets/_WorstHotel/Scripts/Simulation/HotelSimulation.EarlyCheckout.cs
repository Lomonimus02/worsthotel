using System;
using System.Linq;

namespace WorstHotel
{
    public sealed partial class HotelSimulation
    {
        public bool IsEarlyCheckoutWarningKnown(GuestStay guest)
        {
            if (guest?.EarlyCheckout.State != EarlyCheckoutState.Warning || guest.ReceiptPosted || guest.Agent == null ||
                guest.Agent.State == GuestAgentState.CheckingOut || guest.Agent.State == GuestAgentState.Leaving ||
                guest.Agent.State == GuestAgentState.Left) return false;
            var record = guest.EarlyCheckout;
            return record.RoomId == guest.RoomId && Incidents.Items.Any(incident => incident.Id == record.IncidentId &&
                incident.GuestId == guest.GuestId && incident.RoomId == guest.RoomId && incident.EpisodeCount == record.IncidentEpisode &&
                incident.Active && incident.HasContactedStaff);
        }

        public bool EarlyCheckoutDecisionPending(GuestStay guest)
        {
            if (guest?.EarlyCheckout.State != EarlyCheckoutState.Warning || guest.EarlyCheckout.GraceRemainingSeconds > 0 ||
                guest.ReceiptPosted || guest.Agent == null || !guest.Agent.CheckedIn || guest.EarlyCheckout.RoomId != guest.RoomId) return false;
            var record = guest.EarlyCheckout;
            return Incidents.Items.Any(incident => incident.Active && incident.Id == record.IncidentId &&
                incident.GuestId == guest.GuestId && incident.RoomId == guest.RoomId && incident.EpisodeCount == record.IncidentEpisode);
        }

        /// <summary>Pure current source measurement. In particular, a blanket received this step
        /// changes perceived cold before the next need integration without advancing history twice.</summary>
        public float CurrentEarlyCheckoutSeverity(GuestStay guest, HotelIncident incident)
        {
            if (guest?.Agent == null || !guest.Agent.InAssignedRoom || !guest.Perception.InAssignedRoom ||
                incident == null || !incident.Active || incident.GuestId != guest.GuestId || incident.RoomId != guest.RoomId ||
                !rooms.TryGetValue(guest.RoomId, out var room) || room.GuestId != guest.GuestId || incident.Cause == null) return 0;
            string source = incident.Cause.SourceEntityId;
            var profile = guest.Application.Archetype.Needs;
            if (incident.Reason == IncidentReason.Temperature && source == "room/" + guest.RoomId + "/temperature")
            {
                float perceived = room.Temperature < profile.PreferredTemperatureMin ?
                    Math.Min(profile.PreferredTemperatureMin, room.Temperature + guest.BlanketComfortBonus) : room.Temperature;
                return NeedEvaluator.TemperatureSeverity(perceived, profile);
            }
            if (incident.Reason == IncidentReason.Noise && Noise != null)
            {
                var contributions = Noise.GetContributions(guest.RoomId);
                float total = contributions.Sum(item => item.ReceivedNoise);
                float attributed = contributions.Where(item => item.Active && item.SourceGuestId != guest.GuestId &&
                    item.SourceEntityId == source).Sum(item => item.ReceivedNoise);
                return total > 0 ? NeedEvaluator.NoiseSeverity(Math.Min(1, total), profile) * attributed / total : 0;
            }
            if (incident.Reason == IncidentReason.RoomCondition)
            {
                if (source == "room/" + guest.RoomId + "/linen") return room.Cleanliness == Cleanliness.Dirty ? NeedsSettings.DirtySeverity : 0;
                if (source == "room/" + guest.RoomId + "/lamp") return room.LampBroken ? NeedsSettings.DegradedSeverity : 0;
                if (source == "circuit/" + (room.CircuitId ?? guest.RoomId.ToString()) + "/power")
                    return room.HasPower ? 0 : room.PowerLossConditionSeverity;
            }
            return 0;
        }

        private void TickEarlyCheckout(float now, float dt)
        {
            if (IsReadOnlyMirror || !Running || !ContinuousOperations || !LivingEnabled ||
                NeedsSettings.EarlyCheckout.Enabled != true || dt <= 0) return;
            var tuning = NeedsSettings.EarlyCheckout;
            foreach (var guest in guests)
            {
                var record = guest.EarlyCheckout; var agent = guest.Agent;
                if (record.State == EarlyCheckoutState.Committed) continue;
                if (guest.ReceiptPosted || agent == null || !agent.CheckedIn || !agent.HasReachedRoom ||
                    agent.State == GuestAgentState.CheckingOut || agent.State == GuestAgentState.Leaving || agent.State == GuestAgentState.Left ||
                    now >= agent.CheckoutTime)
                { record.Reset(); continue; }
                if (record.State != EarlyCheckoutState.None && record.RoomId != guest.RoomId) record.Reset();
                // Neither an outing nor travel to contact staff supplies room evidence or recovery.
                if (!agent.InAssignedRoom || !guest.Perception.InAssignedRoom || agent.IsRelocating ||
                    rooms[guest.RoomId].GuestId != guest.GuestId) continue;

                var incident = Incidents.Items.FirstOrDefault(item => item.Id == record.IncidentId && item.GuestId == guest.GuestId &&
                    item.RoomId == guest.RoomId && item.EpisodeCount == record.IncidentEpisode && item.Active);
                if (incident == null)
                {
                    record.Reset();
                    incident = EarlyCheckoutCandidate(guest);
                    if (incident == null) continue;
                    TrackEarlyCheckoutCause(record, incident);
                }
                float severity = CurrentEarlyCheckoutSeverity(guest, incident);
                if (severity < tuning.RecoveryThreshold)
                {
                    record.RecoverySeconds = AddEarlyCheckoutTime(record.RecoverySeconds, dt);
                    if (record.RecoverySeconds >= EarlyCheckoutSeconds(tuning.RecoveryHours))
                    {
                        record.State = EarlyCheckoutState.Monitoring;
                        record.SevereExposureSeconds = record.GraceRemainingSeconds = 0;
                        // Remember the first warning for this episode; an oscillating temperature
                        // cannot repeatedly announce the same warning or retain old severe time.
                        var other = EarlyCheckoutCandidate(guest);
                        if (other != null && other.Id != incident.Id)
                        { record.Reset(); TrackEarlyCheckoutCause(record, other); }
                    }
                    continue;
                }
                record.RecoverySeconds = 0;
                if (severity <= tuning.SevereThreshold) continue;
                record.SevereExposureSeconds = AddEarlyCheckoutTime(record.SevereExposureSeconds, dt);
                if (!EarlyCheckoutHistoryQualified(incident)) continue;
                float minimumRemaining = EarlyCheckoutSeconds(tuning.MinimumRemainingStayHours);
                if (record.State == EarlyCheckoutState.Monitoring)
                {
                    if (record.SevereExposureSeconds < EarlyCheckoutTrialSeconds(guest, incident) ||
                        agent.CheckoutTime - now < minimumRemaining + EarlyCheckoutSeconds(tuning.GraceHours)) continue;
                    bool firstWarning = record.WarningAt < 0;
                    record.State = EarlyCheckoutState.Warning;
                    if (firstWarning) record.WarningAt = now;
                    record.GraceRemainingSeconds = EarlyCheckoutSeconds(tuning.GraceHours);
                    if (firstWarning && incident.HasContactedStaff)
                        SignalEvent("Room " + guest.RoomId + ": the guest may leave early if the serious problem continues.");
                    continue; // The trial interval must not also consume the first grace interval.
                }
                var direct = Services?.DirectIntent(guest.GuestId);
                var response = agent.ResponseActionId == null ? null : Services?.FindResponse(agent.ResponseActionId);
                // Honor a bounded conversation already underway when the warning was issued.
                // Reopening new discussions afterward cannot renew the grace indefinitely.
                if (record.State != EarlyCheckoutState.Warning || incident.ResponseReliefRemainingSeconds > 0 ||
                    direct != null && direct.CreatedAt <= record.WarningAt ||
                    response != null && response.PhaseStartedAt <= record.WarningAt) continue;
                record.GraceRemainingSeconds = Math.Max(0, record.GraceRemainingSeconds - dt);
                if (record.GraceRemainingSeconds > 0 || agent.CheckoutTime - now < minimumRemaining || direct != null ||
                    agent.ResponseActionId != null || agent.PendingMoveRoomId.HasValue) continue;

                // No callback between the captured outcome and the shared terminal transition.
                // The helper releases actual keys/room/intents and keeps the physical exit route.
                record.CauseDescription = EarlyCheckoutDescription(guest, incident);
                record.CommittedAt = now; record.State = EarlyCheckoutState.Committed;
                BeginGuestCheckout(guest, now);
            }
        }

        HotelIncident EarlyCheckoutCandidate(GuestStay guest) => Incidents.Items.Where(item => item.GuestId == guest.GuestId &&
                item.RoomId == guest.RoomId && item.Active && (item.Reason == IncidentReason.Temperature ||
                item.Reason == IncidentReason.Noise || item.Reason == IncidentReason.RoomCondition))
            .Select(item => new { Incident = item, Severity = CurrentEarlyCheckoutSeverity(guest, item) })
            .Where(item => item.Severity > NeedsSettings.EarlyCheckout.SevereThreshold)
            .OrderByDescending(item => item.Severity).ThenBy(item => item.Incident.Id, StringComparer.Ordinal)
            .Select(item => item.Incident).FirstOrDefault();

        static void TrackEarlyCheckoutCause(GuestEarlyCheckout record, HotelIncident incident)
        {
            record.State = EarlyCheckoutState.Monitoring; record.IncidentId = incident.Id;
            record.IncidentEpisode = incident.EpisodeCount; record.RoomId = incident.RoomId; record.Reason = incident.Reason;
        }

        bool EarlyCheckoutHistoryQualified(HotelIncident incident) => incident.Active &&
            (incident.Stage == SituationStage.Escalated || incident.Stage == SituationStage.Critical) &&
            (incident.HasContactedStaff || Services?.ExhaustedContactAttempts(incident) == true);

        float EarlyCheckoutTrialSeconds(GuestStay guest, HotelIncident incident)
        {
            var tuning = NeedsSettings.EarlyCheckout;
            var traits = guest.Application.Archetype.Traits;
            float personality = (traits & GuestTraits.Patient) != 0 ? tuning.PatientMultiplier :
                (traits & GuestTraits.Impatient) != 0 ? tuning.ImpatientMultiplier : 1;
            double multiplier = (double)guest.Application.Archetype.Needs.PatienceSeconds / tuning.ReferencePatienceSeconds *
                personality * incident.EffectivePatienceMultiplier;
            multiplier = Math.Max(tuning.MinimumPatienceMultiplier, Math.Min(tuning.MaximumPatienceMultiplier, multiplier));
            return (float)Math.Min(float.MaxValue, (double)EarlyCheckoutSeconds(tuning.SevereHours) * multiplier);
        }

        float EarlyCheckoutSeconds(float hours) => (float)Math.Min(float.MaxValue, (double)Operations.SecondsPerDay * hours / 24);
        static float AddEarlyCheckoutTime(float previous, float dt) => (float)Math.Min(float.MaxValue, (double)previous + dt);

        string EarlyCheckoutDescription(GuestStay guest, HotelIncident incident)
        {
            string problem = incident.Reason == IncidentReason.Noise ? "severe noise" :
                incident.Reason == IncidentReason.Temperature ? (rooms[guest.RoomId].Temperature < guest.Application.Archetype.Needs.PreferredTemperatureMin ?
                    "severe cold" : "severe heat") : incident.Cause.SourceType == "Power loss" ? "a prolonged power outage" : "severe room conditions";
            return "Left early after " + problem + " in room " + guest.RoomId + " remained unresolved.";
        }
    }
}
