using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace WorstHotel
{
    public sealed partial class IncidentSystem
    {
        readonly Dictionary<int, RoomState> livingRooms = new Dictionary<int, RoomState>();
        public float SituationTime { get; internal set; }

        /// <summary>Observe factual perception after needs advance. One guest/source key persists across escalation and grace.</summary>
        public void TickLiving(IEnumerable<GuestStay> guests, IEnumerable<RoomState> roomStates, float dt)
        {
            if (ReadOnlyMirror) return;
            if (!Number.IsFinite(dt) || dt < 0) throw new ArgumentOutOfRangeException(nameof(dt));
            if (guests == null || roomStates == null) throw new ArgumentNullException("Guests and rooms are required.");
            if (!LivingEnabled) throw new InvalidOperationException("Living situations require NeedSettings.");
            if (dt == 0) return;
            SituationTime = AddTime(SituationTime, dt);
            livingRooms.Clear();
            foreach (var room in roomStates)
            {
                if (room == null) throw new ArgumentException("Room collection contains a null room.");
                livingRooms.Add(room.Profile.Id, room);
            }
            var evaluator = new GuestNeedEvaluator(needSettings);
            foreach (var guest in guests)
            {
                if (guest == null) throw new ArgumentException("Guest collection contains a null guest.");
                var agent = guest.Agent;
                if (agent == null || guest.Needs == null) continue;
                if (agent.State == GuestAgentState.CheckingOut || agent.State == GuestAgentState.Leaving || agent.State == GuestAgentState.Left)
                { RecordIgnored(guest); EndGuestStay(guest.GuestId); continue; }
                if (agent.IsRelocating) continue;
                // A guest going to tell staff retains the last room observation. Transit is
                // neither fresh room exposure nor evidence that the problem was repaired.
                if (RequirePhysicalCommunication && agent.IsServiceReceptionTrip) continue;
                if (!livingRooms.ContainsKey(guest.RoomId)) throw new ArgumentException("Guest references an unknown room.");
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var perception = guest.Perception;
                if (agent.InAssignedRoom && perception.InAssignedRoom)
                {
                    if (perception.TemperatureCause != null)
                        Observe(guest, IncidentReason.Temperature, perception.TemperatureCause, guest.Needs.Temperature, dt, seen);
                    float aggregateNoiseSeverity = evaluator.NoiseSeverity(perception.Noise, guest.Application.Archetype.Needs);
                    float sourceTotal = perception.NoiseSources.Sum(source => source.ReceivedNoise);
                    foreach (var source in perception.NoiseSources)
                    {
                        if (!source.Active || source.SourceGuestId == guest.GuestId || source.ReceivedNoise <= 0) continue;
                        // Allocate the measured aggregate discomfort across its real contributors.
                        // Each source then accumulates only its own share of dissatisfaction.
                        float severity = aggregateNoiseSeverity > needSettings.RecoverySeverityThreshold && sourceTotal > 0 ?
                            aggregateNoiseSeverity * source.ReceivedNoise / sourceTotal : 0;
                        var cause = new SituationCause(source.Label, source.SourceEntityId, source.SourceGuestId, source.SourceRoomId,
                            source.NoiseOutput, source.ReceivedNoise, guest.Application.Archetype.Needs.PreferredNoise,
                            "Room " + source.SourceRoomId + " — " + source.Label + ": source " + F(source.NoiseOutput) +
                            ", received from this source " + F(source.ReceivedNoise) + "; total received " + F(perception.Noise) +
                            "; comfortable below " + F(guest.Application.Archetype.Needs.PreferredNoise) + ".");
                        Observe(guest, IncidentReason.Noise, cause,
                            new GuestNeedSnapshot(severity, guest.Needs.Noise.ExposureSeconds, guest.Needs.Noise.Dissatisfaction), dt, seen);
                    }
                    foreach (var cause in perception.ConditionCauses)
                        Observe(guest, IncidentReason.RoomCondition, cause,
                            new GuestNeedSnapshot(cause.ReceivedIntensity, guest.Needs.RoomCondition.ExposureSeconds,
                                guest.Needs.RoomCondition.Dissatisfaction), dt, seen);
                }
                foreach (var incident in incidents.Values.Where(i => i.GuestId == guest.GuestId && !seen.Contains(i.Id)).ToArray())
                {
                    if (!incident.Active)
                    {
                        incident.ReopenCooldown = Math.Max(0, incident.ReopenCooldown - dt);
                        if (incident.Reason == IncidentReason.Noise)
                            incident.Dissatisfaction = Math.Max(0, incident.Dissatisfaction - needSettings.RecoveryPerSecond * dt);
                        continue;
                    }
                    // Absence supplies no new evidence about a Remote room problem. Keep
                    // its episode, last factual cause and timers until actual room perception
                    // resumes. Inactive cooldowns above still age normally while guests are away.
                    if (SuspendRemoteIncident(guest, incident)) continue;
                    string reason = !agent.InAssignedRoom ? "Guest is outside the assigned room; exposure ended." :
                        "The source no longer reaches the guest in room " + guest.RoomId + ".";
                    EvaluateLiving(guest, incident, new GuestNeedSnapshot(0, incident.LastNeedExposure, incident.Dissatisfaction), null, reason, dt);
                }
            }
        }

        void Observe(GuestStay guest, IncidentReason reason, SituationCause cause, GuestNeedSnapshot need, float dt, HashSet<string> seen)
        {
            ValidateNeed(need);
            if (cause == null || string.IsNullOrWhiteSpace(cause.SourceEntityId)) return;
            string key = new SituationKey(reason, guest.GuestId, cause.SourceEntityId).ToString();
            seen.Add(key);
            if (!incidents.TryGetValue(key, out var incident))
            {
                if (!HasExposure(reason, need.Severity)) return;
                incident = new HotelIncident(guest, reason, true, cause.SourceEntityId) { Cause = cause };
                incidents.Add(key, incident);
            }
            EvaluateLiving(guest, incident, need, cause, cause.Description, dt);
        }

        void EvaluateLiving(GuestStay guest, HotelIncident incident, GuestNeedSnapshot need, SituationCause cause, string explanation, float dt)
        {
            ValidateNeed(need);
            bool bad = HasExposure(incident.Reason, need.Severity);
            if (cause != null && incident.Cause != null && need.Severity < incident.Severity - .05f)
                AddHistory(incident, "Exposure reduced: " + explanation);
            if (cause != null) incident.Cause = cause;
            else if (incident.Cause != null)
                incident.Cause = new SituationCause(incident.Cause.SourceType, incident.Cause.SourceEntityId, incident.Cause.SourceGuestId,
                    incident.Cause.SourceRoomId, incident.Cause.RawIntensity, 0, incident.Cause.GuestThreshold, explanation);
            incident.RoomId = guest.RoomId; incident.MeasuredCause = explanation;
            incident.Severity = need.Severity;
            incident.Dissatisfaction = incident.Reason == IncidentReason.Noise ? Number.Clamp(incident.Dissatisfaction +
                (bad ? need.Severity * needSettings.BuildupPerSecond * needSettings.NoiseBuildupMultiplier :
                    -needSettings.RecoveryPerSecond) * dt, 0, 1) : need.Dissatisfaction;
            incident.LastNeedExposure = need.ExposureSeconds;
            if (!incident.Active)
            {
                incident.ReopenCooldown = Math.Max(0, incident.ReopenCooldown - dt);
                if (!bad || incident.ReopenCooldown > 0) return;
                incident.Active = true; incident.Resolved = false; incident.HasOccurred = true;
                incident.AttentionAcknowledged = incident.ResponseAccepted = incident.PausedForTransfer = incident.IgnoreRecorded = false;
                incident.ResolutionReason = null;
                incident.Age = incident.StageAge = incident.RecoverySeconds = incident.ExposureSeconds = 0;
                incident.ResponseReliefRemainingSeconds = 0;
                incident.Stage = SituationStage.Observed;
                incident.HasContactedStaff = false;
                incident.ComplaintRecorded = false; incident.Response = null;
                incident.EpisodeCount = Math.Min(needSettings.MemoryCountLimit, incident.EpisodeCount + 1);
                incident.EffectivePatienceMultiplier = guest.Memory.PatienceMultiplier(incident.Reason, needSettings);
                AddHistory(incident, "Exposure started: " + explanation);
                OnSituationChanged?.Invoke(incident);
            }
            if (!bad)
            {
                if (incident.RecoverySeconds == 0) AddHistory(incident, "Exposure ended; verifying recovery. " + explanation);
                incident.RecoverySeconds = AddTime(incident.RecoverySeconds, dt);
                if (incident.RecoverySeconds >= needSettings.RecoverySeconds)
                {
                    bool complained = incident.Stage >= SituationStage.Complaint && incident.Stage != SituationStage.Resolved;
                    incident.Active = false; incident.Resolved = true; incident.ResponseReliefRemainingSeconds = 0;
                    incident.ReopenCooldown = needSettings.ReopenCooldownSeconds;
                    incident.ResolutionReason = explanation;
                    if (complained && guest.Agent.InAssignedRoom && (!RequirePhysicalCommunication ||
                        incident.HasContactedStaff && incident.Response?.StaffActionAt >= 0))
                        guest.Memory.ProblemsResolvedSuccessfully = Math.Min(needSettings.MemoryCountLimit, guest.Memory.ProblemsResolvedSuccessfully + 1);
                    AddHistory(incident, "Resolved through measured recovery.");
                    ChangeStage(incident, SituationStage.Resolved);
                    OnIncidentResolved?.Invoke(incident);
                }
                return;
            }
            incident.RecoverySeconds = 0;
            incident.ExposureSeconds = AddTime(incident.ExposureSeconds, dt);
            if (incident.ResponseReliefRemainingSeconds > 0)
            {
                incident.ResponseReliefRemainingSeconds = Math.Max(0, incident.ResponseReliefRemainingSeconds - dt);
                if (incident.ResponseReliefRemainingSeconds == 0) AddHistory(incident, "Compensation grace expired; the cause is still present.");
                return;
            }
            incident.StageAge = AddTime(incident.StageAge, dt);
            if (incident.Stage != SituationStage.Observed) incident.Age = AddTime(incident.Age, dt);
            if (incident.Stage == SituationStage.Observed && Reached(incident, needSettings.ComplaintExposureSeconds, needSettings.ComplaintDissatisfaction))
            {
                incident.Age = 0;
                if (!RequirePhysicalCommunication) incident.HasContactedStaff = true;
                ChangeStage(incident, SituationStage.Complaint);
                PublishComplaintIfKnown(guest, incident);
            }
            if (incident.Stage == SituationStage.Complaint && Reached(incident, needSettings.EscalatedExposureSeconds, needSettings.EscalatedDissatisfaction))
                ChangeStage(incident, SituationStage.Escalated);
            if (incident.Stage == SituationStage.Escalated && Reached(incident, needSettings.CriticalExposureSeconds, needSettings.CriticalDissatisfaction))
            {
                ChangeStage(incident, SituationStage.Critical);
                RecordIgnored(guest, incident);
            }
        }

        internal void RecordIgnored(GuestStay guest)
        {
            foreach (var incident in incidents.Values.Where(i => i.GuestId == guest.GuestId && i.Active && i.Stage >= SituationStage.Complaint))
                RecordIgnored(guest, incident);
        }
        void RecordIgnored(GuestStay guest, HotelIncident incident)
        {
            if (incident.IgnoreRecorded || RequirePhysicalCommunication && !incident.ComplaintRecorded) return;
            incident.IgnoreRecorded = true;
            guest.Memory.ProblemsIgnored = Math.Min(needSettings.MemoryCountLimit, guest.Memory.ProblemsIgnored + 1);
            AddHistory(incident, "Problem left unresolved; guest remembers this.");
        }
        internal void RecordNoiseWarning(string sourceGuestId)
        {
            foreach (var incident in incidents.Values.Where(i => i.Active && i.Cause?.SourceGuestId == sourceGuestId))
                AddHistory(incident, "Source guest was asked to keep it down.");
        }
        internal CommandResult MarkCommunicated(GuestStay guest, string incidentId, int episode, float now)
        {
            if (ReadOnlyMirror) return CommandResult.Fail(HotelSimulation.MirrorMessage);
            if (!incidents.TryGetValue(incidentId, out var incident) || incident.GuestId != guest.GuestId ||
                !incident.Active || incident.EpisodeCount != episode || incident.Response == null ||
                incident.Response.IncidentId != incidentId || incident.Response.IncidentEpisode != episode ||
                incident.Response.CommunicatedAt < 0 || !Number.IsFinite(now))
                return CommandResult.Fail("The communicated concern no longer matches this factual episode.");
            incident.HasContactedStaff = true;
            AddHistory(incident, "Guest explained the situation to staff.");
            PublishComplaintIfKnown(guest, incident);
            return CommandResult.Ok("Guest concern communicated.");
        }
        void PublishComplaintIfKnown(GuestStay guest, HotelIncident incident)
        {
            if (!incident.Active || !incident.HasContactedStaff || incident.Stage < SituationStage.Complaint ||
                incident.Stage == SituationStage.Resolved || incident.ComplaintRecorded) return;
            incident.ComplaintRecorded = true;
            guest.Memory.RecordComplaint(incident.Reason, needSettings);
            OnIncidentStarted?.Invoke(incident);
        }
        void AddHistory(HotelIncident incident, string reason)
        {
            incident.history.Add(new SituationHistoryEntry(SituationTime, reason));
            while (incident.history.Count > needSettings.HistoryCapacity) incident.history.RemoveAt(0);
        }
        void ChangeStage(HotelIncident incident, SituationStage stage)
        {
            if (incident.Stage == stage) return;
            incident.Stage = stage; incident.StageAge = 0;
            AddHistory(incident, "Situation " + stage + ".");
            OnSituationChanged?.Invoke(incident);
        }
        static bool Reached(HotelIncident incident, float exposure, float dissatisfaction) =>
            incident.ExposureSeconds >= exposure * incident.EffectivePatienceMultiplier &&
            incident.Dissatisfaction >= dissatisfaction * incident.EffectivePatienceMultiplier;
        // Noise severity was already gated against the aggregate recovery threshold before
        // attribution. Applying that threshold again to each share would lose combined causes.
        bool HasExposure(IncidentReason reason, float severity) =>
            severity > (reason == IncidentReason.Noise ? 0 : needSettings.RecoverySeverityThreshold);
        static void ValidateNeed(GuestNeedSnapshot need)
        {
            if (!Number.IsFinite(need.Severity) || need.Severity < 0 || need.Severity > 1 ||
                !Number.IsFinite(need.Dissatisfaction) || need.Dissatisfaction < 0 || need.Dissatisfaction > 1 ||
                !Number.IsFinite(need.ExposureSeconds) || need.ExposureSeconds < 0)
                throw new ArgumentException("Need snapshots must contain finite, valid severity, exposure and dissatisfaction.");
        }
        static float AddTime(float value, float dt) => (float)Math.Min(float.MaxValue, (double)value + dt);
        static string F(float value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    }
}
