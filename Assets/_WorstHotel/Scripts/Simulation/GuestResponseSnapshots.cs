using System;
using System.Linq;

namespace WorstHotel
{
    [Serializable] public sealed class GuestResponseSnapshot
    {
        public string Id, GuestId, SourceEntityId, IncidentId, ServiceCaseId;
        public int RoomId, IncidentEpisode, ContactAttempts, ActionVersion;
        public GuestResponsePhase Phase;
        public GuestContactChannel Channel;
        public float CreatedAt, PhaseStartedAt, DwellSeconds, SelfResponseAt;
        public float AttemptStartedAt, AttemptDeadline, RetryAt, CommunicatedAt, AcknowledgedAt, StaffActionAt;
        public bool SelfResponseAttempted, SelfResponseApplied;
    }

    public sealed partial class GuestServiceSystem
    {
        GuestResponseSnapshot[] CaptureResponses() => responses.Select(r => new GuestResponseSnapshot
        {
            Id = r.Id, GuestId = r.GuestId, RoomId = r.RoomId, SourceEntityId = r.SourceEntityId,
            IncidentId = SnapshotData.OptionalId(r.IncidentId), IncidentEpisode = r.IncidentEpisode,
            ServiceCaseId = SnapshotData.OptionalId(r.ServiceCaseId), Phase = r.Phase, Channel = r.Channel,
            CreatedAt = r.CreatedAt, PhaseStartedAt = r.PhaseStartedAt, DwellSeconds = r.DwellSeconds,
            SelfResponseAttempted = r.SelfResponseAttempted, SelfResponseApplied = r.SelfResponseApplied,
            SelfResponseAt = r.SelfResponseAt, ContactAttempts = r.ContactAttempts,
            AttemptStartedAt = r.AttemptStartedAt, AttemptDeadline = r.AttemptDeadline, RetryAt = r.RetryAt,
            CommunicatedAt = r.CommunicatedAt, AcknowledgedAt = r.AcknowledgedAt,
            StaffActionAt = r.StaffActionAt, ActionVersion = r.ActionVersion
        }).ToArray();

        void RestoreResponses(ServiceLayerSnapshot data)
        {
            responses.Clear();
            foreach (var r in data.Responses)
                responses.Add(new GuestResponse(r.Id, r.GuestId, r.RoomId, r.SourceEntityId,
                    SnapshotData.OptionalId(r.IncidentId), r.IncidentEpisode, SnapshotData.OptionalId(r.ServiceCaseId), r.CreatedAt)
                {
                    Phase = r.Phase, Channel = r.Channel, PhaseStartedAt = r.PhaseStartedAt, DwellSeconds = r.DwellSeconds,
                    SelfResponseAttempted = r.SelfResponseAttempted, SelfResponseApplied = r.SelfResponseApplied,
                    SelfResponseAt = r.SelfResponseAt, ContactAttempts = r.ContactAttempts,
                    AttemptStartedAt = r.AttemptStartedAt, AttemptDeadline = r.AttemptDeadline, RetryAt = r.RetryAt,
                    CommunicatedAt = r.CommunicatedAt, AcknowledgedAt = r.AcknowledgedAt,
                    StaffActionAt = r.StaffActionAt, ActionVersion = r.ActionVersion
                });
            foreach (var c in data.Cases) FindCase(c.Id).Response = FindResponse(SnapshotData.OptionalId(c.ResponseId));
            foreach (var incident in simulation.Incidents.Items)
                incident.Response = responses.FirstOrDefault(r => r.IncidentId == incident.Id && r.IncidentEpisode == incident.EpisodeCount);
        }
    }

    internal static partial class SnapshotValidation
    {
        static void OptionalHotelTime(float value)
        { Require(value == -1 || Number.IsFinite(value) && value >= 0, "Invalid optional hotel time."); }

        static void GuestResponses(HotelModelSnapshot snapshot, bool enabled)
        {
            var layer = snapshot.ServiceLayer;
            var responses = Array(layer.Responses, 288);
            Unique(responses.Select(r => r.Id));
            if (!enabled)
            {
                Require(responses.Length == 0 && layer.Cases.All(c => string.IsNullOrEmpty(c.ResponseId)) &&
                    snapshot.Incidents.All(i => string.IsNullOrEmpty(i.ResponseId)) &&
                    snapshot.Guests.All(g => string.IsNullOrEmpty(g.Agent?.ResponseActionId)), "Unexpected natural guest response.");
                return;
            }
            var responseIds = responses.ToDictionary(r => r.Id);
            var cases = layer.Cases.ToDictionary(c => c.Id);
            var incidents = snapshot.Incidents.ToDictionary(i => i.Id);
            var guests = snapshot.Guests.ToDictionary(g => g.Application.Id);
            foreach (var r in responses)
            {
                Text(r.Id, 512); Text(r.GuestId); Text(r.SourceEntityId, 256);
                OptionalId(r.IncidentId, 512); OptionalId(r.ServiceCaseId, 512);
                EnumValue(r.Phase); EnumValue(r.Channel);
                Nonnegative(r.CreatedAt, r.PhaseStartedAt, r.DwellSeconds);
                Require(r.PhaseStartedAt >= r.CreatedAt && r.ActionVersion >= 0 && r.IncidentEpisode >= 0 &&
                    r.ContactAttempts >= 0 && r.ContactAttempts <= 2, "Invalid response phase or attempts.");
                foreach (float time in new[] { r.SelfResponseAt, r.AttemptStartedAt, r.AttemptDeadline, r.RetryAt,
                    r.CommunicatedAt, r.AcknowledgedAt, r.StaffActionAt }) OptionalHotelTime(time);
                Require(guests.TryGetValue(r.GuestId, out var guest) && guest.RoomId == r.RoomId,
                    "Response guest or destination changed.");
                bool hasIncident = !string.IsNullOrEmpty(r.IncidentId), hasCase = !string.IsNullOrEmpty(r.ServiceCaseId);
                Require(hasIncident || hasCase, "Response has no causal target.");
                if (hasIncident)
                {
                    Require(incidents.TryGetValue(r.IncidentId, out var incident) && incident.GuestId == r.GuestId &&
                        incident.Cause.SourceEntityId == r.SourceEntityId && r.IncidentEpisode > 0 &&
                        r.IncidentEpisode <= incident.EpisodeCount, "Response has no matching incident episode.");
                    Require(r.Id == r.IncidentId + "/contact/" + r.IncidentEpisode, "Invalid incident response identity.");
                    if (r.IncidentEpisode == incident.EpisodeCount)
                    {
                        Require(incident.ResponseId == r.Id, "Incident response link differs.");
                        Require(incident.HasContactedStaff == (r.CommunicatedAt >= 0), "Incident knowledge differs from communication.");
                    }
                    else Require(hasCase && (r.Phase == GuestResponsePhase.Cancelled || r.Phase == GuestResponsePhase.Communicated),
                        "Historical episode cannot initiate a new response action.");
                }
                else Require(r.IncidentEpisode == 0 && r.Id == r.ServiceCaseId + "/contact", "Invalid service response identity.");
                if (hasCase)
                    Require(cases.TryGetValue(r.ServiceCaseId, out var service) && service.ResponseId == r.Id &&
                        service.GuestId == r.GuestId && service.SourceEntityId == r.SourceEntityId,
                        "Service response link differs.");
                Require(!r.SelfResponseApplied || r.SelfResponseAttempted && r.SelfResponseAt >= 0,
                    "Self-help has no completed physical action.");
                Require(r.AcknowledgedAt < 0 || r.CommunicatedAt >= 0 && r.AcknowledgedAt >= r.CommunicatedAt,
                    "Acknowledgement precedes communication.");
                Require((r.Phase == GuestResponsePhase.Communicated) == (r.CommunicatedAt >= 0),
                    "Response phase differs from communication.");
                Require(r.AttemptStartedAt < 0 || r.ContactAttempts > 0 && r.AttemptDeadline >= r.AttemptStartedAt,
                    "Invalid contact attempt timing.");
                Require(r.ContactAttempts != 0 || r.AttemptStartedAt < 0, "Contact started without an attempt.");
                Require(r.Channel != GuestContactChannel.None || r.ContactAttempts == 0 && r.CommunicatedAt < 0,
                    "Contact has no channel.");
            }
            foreach (var c in layer.Cases)
                Require(!string.IsNullOrEmpty(c.ResponseId) && responseIds.TryGetValue(c.ResponseId, out var r) &&
                    r.ServiceCaseId == c.Id, "Service case lost its response.");
            foreach (var i in snapshot.Incidents)
            {
                OptionalId(i.ResponseId, 512);
                if (!string.IsNullOrEmpty(i.ResponseId))
                    Require(responseIds.TryGetValue(i.ResponseId, out var r) && r.IncidentId == i.Id &&
                        r.IncidentEpisode == i.EpisodeCount, "Incident points to an old response.");
                Require(!i.ComplaintRecorded || i.HasContactedStaff, "Private incident contains a spoken complaint.");
            }
            Require(responses.Count(r => r.Phase == GuestResponsePhase.Contacting && r.Channel == GuestContactChannel.Phone &&
                r.AttemptStartedAt >= 0 && r.AttemptDeadline > snapshot.Time) <= 1, "Multiple ringing calls.");
            foreach (var g in snapshot.Guests)
            {
                var a = g.Agent;
                OptionalId(a.ResponseActionId, 512);
                Require(a.ResponseActionVersion >= 0, "Invalid response action version.");
                bool serviceTrip = a.State == GuestAgentState.GoingToServiceReception ||
                    a.State == GuestAgentState.WaitingAtServiceReception || a.State == GuestAgentState.ReturningFromServiceReception;
                bool responseActivity = a.Activity == GuestActivity.AdjustRadiator || a.Activity == GuestActivity.CallReception;
                if (string.IsNullOrEmpty(a.ResponseActionId))
                { Require(!serviceTrip && !responseActivity, "Physical response has no owner."); continue; }
                Require(responseIds.TryGetValue(a.ResponseActionId, out var r) && r.GuestId == g.Application.Id &&
                    r.ActionVersion == a.ResponseActionVersion, "Stale or foreign physical response action.");
                Require(serviceTrip || responseActivity || a.State == GuestAgentState.WaitingForCheckIn,
                    "Response action is attached to an unrelated activity.");
                Require(a.State == GuestAgentState.WaitingForCheckIn ||
                    snapshot.Rooms.Any(room => room.Id == g.RoomId && room.GuestId == g.Application.Id),
                    "Response actor does not own their room.");
            }
        }
    }
}
