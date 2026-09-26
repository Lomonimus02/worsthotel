using System;
using System.Collections.Generic;
using System.Linq;

namespace WorstHotel
{
    public enum IncidentReason { Cold, Noise, Dirty, Broken, Temperature, RoomCondition, Service }
    public enum RequestUrgency { Normal, High }
    public enum SituationStage { Observed, Complaint, Escalated, Critical, Resolved }

    public sealed class HotelIncident
    {
        public string Id { get; }
        public string GuestId { get; }
        public string GuestName { get; }
        public int RoomId { get; internal set; }
        public IncidentReason Reason { get; }
        public bool Active { get; internal set; }
        public bool Resolved { get; internal set; }
        public float Age { get; internal set; }
        public string MeasuredCause { get; internal set; }
        public float Patience => BasePatience * EffectivePatienceMultiplier;
        internal float BasePatience { get; }
        public float EffectivePatienceMultiplier { get; internal set; } = 1;
        public SituationCause Cause { get; internal set; }
        public SituationKey Key => new SituationKey(Reason, GuestId, Cause?.SourceEntityId ?? "legacy");
        public int EpisodeCount { get; internal set; }
        public bool HasContactedStaff { get; internal set; }
        public bool ComplaintRecorded { get; internal set; }
        public GuestResponse Response { get; internal set; }
        internal bool IgnoreRecorded;
        internal readonly List<SituationHistoryEntry> history = new List<SituationHistoryEntry>();
        public IReadOnlyList<SituationHistoryEntry> History => history.AsReadOnly();
        public SituationStage Stage { get; internal set; } = SituationStage.Observed;
        public float Severity { get; internal set; }
        /// <summary>Bad-condition seconds in the current episode; guest needs retain lifetime exposure.</summary>
        public float ExposureSeconds { get; internal set; }
        public float Dissatisfaction { get; internal set; }
        public float StageAge { get; internal set; }
        public bool PausedForTransfer { get; internal set; }
        public bool AttentionAcknowledged { get; internal set; }
        public bool ResponseAccepted { get; internal set; }
        public float ResponseReliefRemainingSeconds { get; internal set; }
        public string ResolutionReason { get; internal set; }
        internal bool HasOccurred;
        internal float ConditionSeconds;
        internal float RecoverySeconds;
        internal float ExposureBaseline, LastNeedExposure, ReopenCooldown;

        internal HotelIncident(GuestStay guest, IncidentReason reason, bool living = false, string sourceEntityId = null)
        {
            Id = sourceEntityId == null ? guest.GuestId + ":" + reason : new SituationKey(reason, guest.GuestId, sourceEntityId).ToString();
            GuestId = guest.GuestId; GuestName = guest.Name; RoomId = guest.RoomId;
            Reason = reason; BasePatience = living ? guest.Application.Archetype.Needs.PatienceSeconds : guest.Application.Archetype.Patience;
        }
    }

    public sealed partial class IncidentSystem
    {
        public IReadOnlyList<HotelIncident> Items => Array.AsReadOnly(incidents.Values.Where(incident => incident.HasOccurred)
            .OrderBy(incident => incident.RoomId).ThenBy(incident => incident.Reason).ToArray());
        // Retention must also see private observations and legacy records which never occurred.
        internal IEnumerable<HotelIncident> AllIncidents => incidents.Values;
        public int ActiveCount => incidents.Values.Count(incident => incident.Active);
        public event Action<HotelIncident> OnIncidentStarted;
        public event Action<HotelIncident> OnIncidentResolved;
        public event Action<HotelIncident> OnSituationChanged;
        public bool LivingEnabled => needSettings != null;
        internal bool RequirePhysicalCommunication { get; set; }
        private readonly Dictionary<string, HotelIncident> incidents = new Dictionary<string, HotelIncident>();
        private readonly SessionSettings settings;
        private readonly NeedSettings needSettings;

        public IncidentSystem(SessionSettings settings, NeedSettings needs = null)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            needSettings = needs;
        }
        public void Clear() {
            if (ReadOnlyMirror) return; incidents.Clear(); livingRooms.Clear(); SituationTime = 0; }

        internal void PruneCompletedStays(ISet<string> historyOwnerIds, ISet<string> retainedIncidentIds)
        {
            if (ReadOnlyMirror) return;
            if (historyOwnerIds == null) throw new ArgumentNullException(nameof(historyOwnerIds));
            if (retainedIncidentIds == null) throw new ArgumentNullException(nameof(retainedIncidentIds));
            // Retirement is storage maintenance, never a new resolution or a guest outcome.
            var obsolete = incidents.Values.Where(incident => !incident.Active &&
                !historyOwnerIds.Contains(incident.GuestId) && !retainedIncidentIds.Contains(incident.Id))
                .Select(incident => incident.Id).ToArray();
            foreach (var id in obsolete) incidents.Remove(id);
        }

        public void EndGuestStay(string guestId)
        {
            if (ReadOnlyMirror) return;
            foreach (var incident in incidents.Values)
            {
                if (incident.GuestId != guestId) continue;
                bool wasActive = incident.Active;
                incident.Active = false;
                incident.Resolved = true;
                incident.ConditionSeconds = incident.RecoverySeconds = 0;
                incident.Stage = SituationStage.Resolved;
                incident.StageAge = incident.Severity = 0;
                incident.PausedForTransfer = false;
                incident.ResponseReliefRemainingSeconds = 0;
                incident.MeasuredCause = "Guest checked out; room exposure ended.";
                incident.ResolutionReason = "Guest checked out; room exposure ended.";
                if (wasActive)
                {
                    if (LivingEnabled) OnSituationChanged?.Invoke(incident);
                    OnIncidentResolved?.Invoke(incident);
                }
            }
        }

        public void Tick(IEnumerable<GuestStay> guests, IEnumerable<RoomState> roomStates, float dt)
        {
            if (ReadOnlyMirror) return;
            if (LivingEnabled) { TickLiving(guests, roomStates, dt); return; }
            if (!Number.IsFinite(dt) || dt < 0) throw new ArgumentOutOfRangeException(nameof(dt));
            if (dt == 0) return;
            var rooms = roomStates.ToDictionary(room => room.Profile.Id);
            foreach (var guest in guests)
            {
                var room = rooms[guest.RoomId];
                var profile = guest.Application.Archetype;
                Evaluate(guest, IncidentReason.Cold, room.Temperature < profile.ColdThreshold,
                    room.Temperature >= profile.ColdThreshold + settings.ColdResolutionHysteresis,
                    room.Temperature.ToString("F1") + "°C; guest needs " + profile.ColdThreshold.ToString("F1") + "°C", dt);
                Evaluate(guest, IncidentReason.Noise, room.Noise > profile.NoiseTolerance, room.Noise <= profile.NoiseTolerance,
                    "Noise " + room.Noise.ToString("F2") + "; tolerance " + profile.NoiseTolerance.ToString("F2"), dt);
                Evaluate(guest, IncidentReason.Dirty, room.Cleanliness == Cleanliness.Dirty, room.Cleanliness == Cleanliness.Clean,
                    "Room cleanliness: " + room.Cleanliness, dt);
                Evaluate(guest, IncidentReason.Broken, room.RepairState == RepairState.Broken, room.RepairState != RepairState.Broken,
                    "Fixture condition: " + room.RepairState, dt);
            }
        }

        private void Evaluate(GuestStay guest, IncidentReason reason, bool bad, bool recovered, string measuredCause, float dt)
        {
            string key = guest.GuestId + ":" + reason;
            if (!incidents.TryGetValue(key, out var incident))
            {
                incident = new HotelIncident(guest, reason);
                incidents.Add(key, incident);
            }
            incident.MeasuredCause = measuredCause;
            if (incident.Active)
            {
                incident.Age += dt;
                incident.RecoverySeconds = recovered ? incident.RecoverySeconds + dt : 0;
                if (recovered && incident.RecoverySeconds >= settings.ResolutionDelaySeconds)
                {
                    incident.Active = false; incident.Resolved = true; incident.ConditionSeconds = 0;
                    incident.Stage = SituationStage.Resolved;
                    OnIncidentResolved?.Invoke(incident);
                }
                return;
            }
            incident.ConditionSeconds = bad ? incident.ConditionSeconds + dt : 0;
            if (bad && incident.ConditionSeconds >= settings.IncidentDelaySeconds)
            {
                incident.Active = true; incident.Resolved = false; incident.HasOccurred = true;
                incident.Stage = SituationStage.Complaint;
                incident.Age = 0; incident.RecoverySeconds = 0;
                OnIncidentStarted?.Invoke(incident);
            }
        }

        internal void ResolveForDebug(HotelIncident incident)
        {
            if (!incident.Active) return;
            incident.Active = false; incident.Resolved = true;
            incident.Stage = SituationStage.Resolved; incident.StageAge = 0;
            incident.RecoverySeconds = 0;
            incident.ResponseReliefRemainingSeconds = 0;
            incident.ExposureBaseline = incident.LastNeedExposure;
            incident.ReopenCooldown = needSettings != null ? needSettings.ReopenCooldownSeconds : 0;
            if (LivingEnabled) OnSituationChanged?.Invoke(incident);
            OnIncidentResolved?.Invoke(incident);
        }
    }
}

