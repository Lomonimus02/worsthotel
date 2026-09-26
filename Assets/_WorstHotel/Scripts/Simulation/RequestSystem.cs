using System;
using System.Collections.Generic;
using System.Linq;

namespace WorstHotel
{
    public sealed class HotelRequest
    {
        public string Id { get; }
        public string GuestId { get; }
        public string GuestName { get; }
        public int RoomId => Source.RoomId;
        public IncidentReason Reason { get; }
        public float Age { get; internal set; }
        public float Patience => Source.Patience;
        public bool Resolved { get; internal set; }
        public bool Compensated { get; internal set; }
        public string MeasuredCause { get; internal set; }
        public SituationStage Stage => Source.Stage;
        public float Severity => Source.Severity;
        public bool PausedForTransfer => Source.PausedForTransfer;
        public bool AttentionAcknowledged => Source.AttentionAcknowledged;
        public bool ResponseAccepted => Source.ResponseAccepted;
        public float ResponseReliefRemainingSeconds => Source.ResponseReliefRemainingSeconds;
        public string ResolutionReason => Source.ResolutionReason;
        public float RemainingPatience => Math.Max(0, Patience - Age);
        public RequestUrgency Urgency => !Resolved && (RemainingPatience <= 0 ||
            Stage == SituationStage.Escalated || Stage == SituationStage.Critical) ? RequestUrgency.High : RequestUrgency.Normal;
        internal HotelIncident Source { get; }

        internal HotelRequest(HotelIncident incident)
        {
            Source = incident; Id = incident.Id; GuestId = incident.GuestId; GuestName = incident.GuestName;
            Reason = incident.Reason;
            MeasuredCause = incident.MeasuredCause;
        }
    }

    public sealed partial class RequestSystem
    {
        public IReadOnlyList<HotelRequest> Items => Array.AsReadOnly(requests.Values.Where(IsKnown).OrderBy(request => request.RoomId)
            .ThenBy(request => request.Reason).ToArray());
        public int ActiveCount => requests.Values.Count(request => IsKnown(request) && !request.Resolved);
        bool IsKnown(HotelRequest request) => !incidents.RequirePhysicalCommunication || request.Source.HasContactedStaff;
        public event Action<HotelRequest> OnRequestCreated;
        public event Action<HotelRequest> OnRequestResolved;
        private readonly Dictionary<string, HotelRequest> requests = new Dictionary<string, HotelRequest>();
        private readonly HashSet<string> compensatedGuests = new HashSet<string>();
        private readonly IncidentSystem incidents;

        public RequestSystem(IncidentSystem incidents)
        {
            if (incidents == null) throw new ArgumentNullException(nameof(incidents));
            this.incidents = incidents;
            incidents.OnIncidentStarted += OnIncidentStarted;
            incidents.OnIncidentResolved += OnIncidentResolved;
        }

        public void Clear() {
            if (ReadOnlyMirror) return; requests.Clear(); compensatedGuests.Clear(); }

        private void OnIncidentStarted(HotelIncident incident)
        {
            if (incident.Stage == SituationStage.Observed || incident.Stage == SituationStage.Resolved) return;
            if (incidents.RequirePhysicalCommunication && !incident.HasContactedStaff) return;
            if (!requests.TryGetValue(incident.Id, out var request))
            {
                request = new HotelRequest(incident);
                requests.Add(request.Id, request);
            }
            request.Resolved = false; request.Age = 0; request.MeasuredCause = incident.MeasuredCause;
            request.Compensated = compensatedGuests.Contains(request.GuestId);
            OnRequestCreated?.Invoke(request);
        }

        private void OnIncidentResolved(HotelIncident incident)
        {
            if (!requests.TryGetValue(incident.Id, out var request)) return;
            if (request.Resolved) return;
            request.Resolved = true; request.Age = incident.Age; request.MeasuredCause = incident.MeasuredCause;
            OnRequestResolved?.Invoke(request);
        }

        public void Tick()
        {
            if (ReadOnlyMirror) return;
            foreach (var request in requests.Values)
            {
                if (!IsKnown(request)) { request.Resolved = true; continue; }
                request.MeasuredCause = request.Source.MeasuredCause;
                if (incidents.LivingEnabled)
                {
                    bool resolved = !request.Source.Active || request.Source.Stage == SituationStage.Observed;
                    if (resolved && !request.Resolved)
                    {
                        request.Resolved = true;
                        OnRequestResolved?.Invoke(request);
                    }
                    request.Age = request.Source.Age;
                }
                else if (!request.Resolved) request.Age = request.Source.Age;
            }
        }

        public bool HasExpiredRequest(string guestId) => requests.Values.Any(request =>
            request.GuestId == guestId && IsKnown(request) && !request.Resolved && request.ResponseReliefRemainingSeconds <= 0 && request.RemainingPatience <= 0);

        public bool HasExpiredRoomRequest(string guestId) => requests.Values.Any(request =>
            request.GuestId == guestId && IsKnown(request) && request.Reason != IncidentReason.Service &&
            !request.Resolved && request.ResponseReliefRemainingSeconds <= 0 && request.RemainingPatience <= 0);

        public void SetCompensated(string guestId, bool compensated)
        {
            if (ReadOnlyMirror) return;
            if (compensated) compensatedGuests.Add(guestId); else compensatedGuests.Remove(guestId);
            foreach (var request in requests.Values.Where(request => request.GuestId == guestId)) request.Compensated = compensated;
        }

        /// <summary>Developer override. Normal resolution comes only from the source incident recovering.</summary>
        public CommandResult ResolveRequest(string id)
        {
            if (ReadOnlyMirror) return CommandResult.Fail(HotelSimulation.MirrorMessage);
            if (id == null || !requests.TryGetValue(id, out var request)) return CommandResult.Fail("Request not found.");
            if (incidents.LivingEnabled)
            {
                incidents.ResolveForDebug(request.Source);
                return CommandResult.Ok("Source situation resolved by developer override; persistent conditions can recur.");
            }
            request.Resolved = true;
            OnRequestResolved?.Invoke(request);
            return CommandResult.Ok("Request resolved by developer override.");
        }
    }
}


