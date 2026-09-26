using System;
using System.Collections.Generic;
using System.Linq;

namespace WorstHotel
{
    public sealed partial class GuestServiceSystem
    {
        private readonly List<GuestResponse> responses = new List<GuestResponse>();
        internal bool NaturalCommunicationEnabled => Settings.NaturalCommunicationEnabled;
        public IReadOnlyList<GuestResponse> Responses => responses.AsReadOnly();
        public GuestResponse FindResponse(string id) => responses.FirstOrDefault(item => item.Id == id);
        public GuestResponse IncomingCall => responses.FirstOrDefault(item => item.IsRinging);
        HotelIncident Incident(GuestResponse response) => response.IncidentId == null ? null :
            simulation.Incidents.Items.FirstOrDefault(item => item.Id == response.IncidentId && item.EpisodeCount == response.IncidentEpisode);

        GuestResponse Notice(HotelIncident incident, GuestStay guest, float now)
        {
            string id = incident.Id + "/contact/" + incident.EpisodeCount;
            var response = FindResponse(id);
            if (response == null && responses.Count < ResponseCapacity)
            {
                response = new GuestResponse(id, guest.GuestId, guest.RoomId, incident.Cause.SourceEntityId,
                    incident.Id, incident.EpisodeCount, null, now);
                responses.Add(response);
            }
            incident.Response = response;
            return response;
        }

        bool BindCaseResponse(ServiceCase item, GuestStay guest, float now)
        {
            var incident = simulation.Incidents.Items.FirstOrDefault(candidate => candidate.GuestId == guest.GuestId && candidate.Active &&
                candidate.Cause?.SourceEntityId == item.SourceEntityId &&
                (item.Kind == ServiceKind.ExtraBlanket && candidate.Reason == IncidentReason.Temperature ||
                 item.Kind == ServiceKind.AskNeighborsQuiet && candidate.Reason == IncidentReason.Noise));
            if (incident == null && responses.Count >= ResponseCapacity) return false;
            var response = incident == null ? new GuestResponse(item.Id + "/contact", guest.GuestId, guest.RoomId,
                item.SourceEntityId, null, 0, item.Id, now) : Notice(incident, guest, now);
            if (response == null) return false;
            if (!responses.Contains(response)) responses.Add(response);
            response.ServiceCaseId = item.Id; item.Response = response;
            return true;
        }

        bool ScheduleContext(GuestStay guest, ServiceKind kind, float now)
        {
            var agent = guest.Agent;
            if (!agent.InAssignedRoom || agent.State == GuestAgentState.Sleeping || agent.Activity == GuestActivity.Shower ||
                agent.Activity == GuestActivity.LoudRoom || agent.Activity == GuestActivity.PhoneCall || agent.Activity == GuestActivity.AdjustRadiator)
                return false;
            if (!ContactWindowAvailable(guest, now)) return false;
            if (agent.ResponseActionId != null)
                return FindResponse(agent.ResponseActionId)?.ServiceCaseId is string id && FindCase(id)?.Kind == kind;
            if (kind == ServiceKind.WakeUpCall)
                return !agent.SleepStarted && guest.Application.Archetype.Kind == GuestKind.Business &&
                    now >= agent.Schedule.SleepTime - Settings.ReplySeconds && now < agent.Schedule.SleepTime;
            return now >= agent.CheckoutTime - Settings.LateCheckoutRequestLead && now < agent.CheckoutTime - Settings.ObservationSeconds &&
                (agent.Activity == GuestActivity.Work || agent.Activity == GuestActivity.Pack || agent.Activity == GuestActivity.QuietRest);
        }

        bool CausePresent(GuestResponse response, GuestStay guest, float now)
        {
            if (guest == null || Departed(guest)) return false;
            var incident = Incident(response);
            if (response.IncidentId != null)
            {
                if (incident == null || !incident.Active) return false;
                if (guest.Agent.IsServiceReceptionTrip && guest.Agent.ResponseActionId == response.Id) return true;
                return guest.Agent.InAssignedRoom && incident.Severity >
                    (incident.Reason == IncidentReason.Noise ? 0 : simulation.NeedsSettings.RecoverySeverityThreshold) &&
                    (incident.Reason == IncidentReason.Temperature || incident.Cause?.ReceivedIntensity > 0);
            }
            var item = FindCase(response.ServiceCaseId);
            if (item == null || !item.Active) return false;
            // Once an agreement is communicated, its own outcome and promise govern validity.
            if (response.CommunicatedAt >= 0) return true;
            if (guest.Agent.IsServiceReceptionTrip && guest.Agent.ResponseActionId == response.Id)
                return now < (item.Kind == ServiceKind.WakeUpCall ? item.DueTime : guest.Agent.CheckoutTime - Settings.ObservationSeconds);
            return TryCause(guest, item.Kind, now, out string source, out _, out _, out _, true) && source == item.SourceEntityId;
        }

        void ChargeBudget(ServiceCase item)
        { if (!item.BudgetCharged) { item.BudgetCharged = true; item.BudgetDay = day; } }

        bool BudgetAvailable(GuestStay guest) => cases.Count(item => item.BudgetCharged &&
                (!simulation.ContinuousOperations || item.BudgetDay == day)) < Settings.MaxCasesPerShift &&
            cases.Count(item => item.GuestId == guest.GuestId && item.BudgetCharged) < Settings.MaxCasesPerGuest;
        bool IsSerious(GuestResponse response) => Incident(response)?.Stage >= SituationStage.Complaint && Incident(response)?.Active == true;

        void TickNaturalResponses(float now, float dt)
        {
            TickPromises(now);
            foreach (var guest in simulation.Guests.OrderBy(item => item.GuestId, StringComparer.Ordinal))
            {
                EnsureLuggage(guest); SyncGuestRoom(guest);
                if (Departed(guest)) { EndGuestStay(guest); continue; }
                foreach (var incident in simulation.Incidents.Items.Where(item => item.GuestId == guest.GuestId && item.Active && item.Cause != null))
                    Notice(incident, guest, now);
                foreach (var item in cases.Where(item => item.GuestId == guest.GuestId && item.Active).ToArray()) UpdateCase(item, guest, now, dt);
                if (cases.Count < CaseCapacity && BudgetAvailable(guest) && !cases.Any(item => item.GuestId == guest.GuestId && item.Active))
                    foreach (var kind in new[] { ServiceKind.LuggageStorage, ServiceKind.WakeUpCall, ServiceKind.LateCheckout })
                        if (Eligible(guest, kind) && TryCause(guest, kind, now, out string source, out int room, out float due, out string text) &&
                            TryCreate(guest, kind, now, due, source, room, text) != null) break;
                // Most pressing factual episode wins; stable ID ordering breaks ties without random timers.
                foreach (var response in responses.Where(item => item.GuestId == guest.GuestId).OrderByDescending(IsSerious)
                    .ThenBy(item => item.CreatedAt).ThenBy(item => item.Id, StringComparer.Ordinal).ToArray())
                    TickResponse(response, guest, now, dt);
            }
            responses.RemoveAll(item => !cases.Any(request => request.Response == item) &&
                !simulation.Incidents.Items.Any(incident => incident.Response == item) &&
                !simulation.Guests.Any(guest => guest.Agent.ResponseActionId == item.Id));
            foreach (var intent in intents)
                if (intent.ResponseId != null && FindResponse(intent.ResponseId) == null) intent.ResponseId = null;
        }

        void TickResponse(GuestResponse response, GuestStay guest, float now, float dt)
        {
            bool present = CausePresent(response, guest, now);
            if (!present)
            {
                response.DwellSeconds = 0;
                if (response.CommunicatedAt >= 0) return;
                if (response.IncidentId != null && Incident(response)?.Active == true && response.ContactAttempts == 0)
                {
                    if (guest.Agent.ResponseActionId == response.Id) simulation.ClearGuestResponseAction(guest, true);
                    if (response.Phase != GuestResponsePhase.Cancelled) SetPhase(response, GuestResponsePhase.Noticed, now);
                    return;
                }
                if (response.Phase != GuestResponsePhase.Cancelled) Cancel(response, guest, now);
                return;
            }
            if (response.CommunicatedAt >= 0) return;
            if (response.Phase == GuestResponsePhase.Cancelled)
            {
                var expired = FindCase(response.ServiceCaseId);
                // Withdrawing an optional, unattempted service must not silence a later
                // serious complaint from the same continuing physical cause.
                if (response.ContactAttempts == 0 && expired?.BudgetCharged == false &&
                    expired.Status == ServiceStatus.Expired && IsSerious(response))
                    SetPhase(response, GuestResponsePhase.WaitingToContact, now);
                else return;
            }
            response.DwellSeconds += dt;
            if (response.Phase == GuestResponsePhase.Contacting)
            {
                // Another real arrival can spend the last slot while this guest is walking.
                // Cancel the uncharged soft action instead of reserving the body indefinitely.
                if (response.AttemptStartedAt < 0 && !EnsureContactAllowance(response, guest, now, false)) return;
                if (response.AttemptStartedAt >= 0 && now >= response.AttemptDeadline) MissContact(response, guest, now);
                return;
            }
            if (response.Phase == GuestResponsePhase.SelfResponding) return;
            if (guest.Agent.ResponseActionId != null || !CanInterrupt(guest)) return;
            if (response.Phase == GuestResponsePhase.Noticed && response.DwellSeconds >= Settings.SelfResponseObserveSeconds)
            {
                if (CanSelfRespond(response, guest)) BeginSelfResponse(response, guest, now);
                else SetPhase(response, GuestResponsePhase.Tolerating, now);
                return;
            }
            if (response.Phase == GuestResponsePhase.Tolerating && now - response.PhaseStartedAt >= Settings.ToleranceSeconds &&
                response.DwellSeconds >= Settings.ObservationSeconds)
                SetPhase(response, GuestResponsePhase.WaitingToContact, now);
            if (response.Phase == GuestResponsePhase.WaitingToContact && now >= response.RetryAt &&
                response.DwellSeconds >= Settings.ObservationSeconds)
                BeginContact(response, guest, response.ContactAttempts > 0 ? response.Channel : ChooseChannel(response, guest), now);
        }

        bool CanInterrupt(GuestStay guest) => ContactWindowAvailable(guest, simulation.Elapsed) &&
            (guest.Agent.State == GuestAgentState.WaitingForCheckIn ||
            guest.Agent.InAssignedRoom && guest.Agent.State != GuestAgentState.Sleeping && !guest.Agent.IsRelocating &&
            guest.Agent.Activity != GuestActivity.Shower && guest.Agent.Activity != GuestActivity.Pack &&
            guest.Agent.Activity != GuestActivity.PhoneCall && guest.Agent.Activity != GuestActivity.LoudRoom);

        bool CanSelfRespond(GuestResponse response, GuestStay guest) => !response.SelfResponseAttempted &&
            Incident(response)?.Reason == IncidentReason.Temperature && guest.Agent.InAssignedRoom &&
            rooms[guest.RoomId].RadiatorSetting < 3 && guest.Needs.Temperature.Severity >= Settings.MildColdMinimum &&
            guest.Needs.Temperature.Severity <= Settings.MildColdMaximum &&
            rooms[guest.RoomId].Temperature < guest.Application.Archetype.Needs.PreferredTemperatureMin;

        void BeginSelfResponse(GuestResponse response, GuestStay guest, float now)
        {
            SetPhase(response, GuestResponsePhase.SelfResponding, now);
            simulation.StartGuestResponseAction(guest, response, GuestResponseAnchor.Radiator);
        }
        static void SetPhase(GuestResponse response, GuestResponsePhase phase, float now)
        { response.Phase = phase; response.PhaseStartedAt = now; }

        void SyncResponseRoom(GuestStay guest)
        {
            if (!NaturalCommunicationEnabled) return;
            foreach (var response in responses.Where(item => item.GuestId == guest.GuestId && item.RoomId != guest.RoomId))
            {
                response.RoomId = guest.RoomId; response.ActionVersion++;
                if (response.CommunicatedAt >= 0) response.StaffActionAt = simulation.Elapsed;
                if (guest.Agent.ResponseActionId == response.Id) simulation.ClearGuestResponseAction(guest, false);
                if (response.CommunicatedAt < 0) Cancel(response, guest, simulation.Elapsed);
            }
        }

        void EndGuestResponses(GuestStay guest)
        {
            if (!NaturalCommunicationEnabled || guest.Agent == null) return;
            foreach (var response in responses.Where(item => item.GuestId == guest.GuestId && item.CommunicatedAt < 0))
            { SetPhase(response, GuestResponsePhase.Cancelled, simulation.Elapsed); response.AttemptStartedAt = response.AttemptDeadline = -1; }
            simulation.ClearGuestResponseAction(guest, false);
        }

        public void RecordStaffAction(string guestId, IncidentReason? reason = null, string sourceEntityId = null)
        {
            if (simulation.IsReadOnlyMirror || !NaturalCommunicationEnabled) return;
            foreach (var response in responses.Where(item => item.GuestId == guestId &&
                (sourceEntityId == null || item.SourceEntityId == sourceEntityId)))
            {
                var incident = Incident(response);
                if (reason.HasValue && incident?.Reason != reason.Value) continue;
                if (incident?.Active == true || FindCase(response.ServiceCaseId)?.Active == true) response.StaffActionAt = simulation.Elapsed;
            }
        }
    }
}
