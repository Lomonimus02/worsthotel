#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Linq;
using UnityEngine;

namespace WorstHotel
{
    public sealed partial class DeveloperPanel
    {
        void DrawNaturalResponseDebug(GuestStay guest)
        {
            var services = Session.Simulation.Services;
            var settings = services.Settings;
            var responses = services.Responses.Where(r => r.GuestId == guest.GuestId).OrderBy(r => r.CreatedAt).ToArray();
            GUILayout.Label("NATURAL COMMUNICATION · " + (settings.NaturalCommunicationEnabled ? "enabled" : "legacy mode"), heading);
            GUILayout.Label("CurrentComfort: temperature " + GuestLabels.Need(guest.Needs?.Temperature.Severity ?? 0) +
                " / quiet " + GuestLabels.Need(guest.Needs?.Noise.Severity ?? 0) +
                "\nCurrentNoticeState: " + (responses.Length == 0 ? "Normal" : string.Join(", ", responses.Select(r => r.Phase.ToString()))) +
                "\nSelf-help action: " + (guest.Agent.Activity == GuestActivity.AdjustRadiator ? "walking to / adjusting real valve" : "none") +
                "\nContact threshold: observe " + settings.ObservationSeconds.ToString("F1") + "s + tolerate " + settings.ToleranceSeconds.ToString("F1") +
                "s; self-help after " + settings.SelfResponseObserveSeconds.ToString("F1") + "s", body);
            foreach (var response in responses)
            {
                var item = services.Cases.FirstOrDefault(c => c.Id == response.ServiceCaseId);
                var incident = Session.Simulation.Incidents.Items.FirstOrDefault(i => i.Id == response.IncidentId && i.EpisodeCount == response.IncidentEpisode);
                string state = item != null && !item.Active ? GuestLabels.ServiceState(item.Status) :
                    incident?.Stage >= SituationStage.Escalated ? "Escalated" : response.AcknowledgedAt >= 0 ? "Acknowledged" :
                    response.CommunicatedAt >= 0 ? "Communicated" : response.Phase == GuestResponsePhase.Contacting ? "ContactingHotel" :
                    response.Phase == GuestResponsePhase.Cancelled ? "Cancelled / recovered" : "Uncommunicated";
                GUILayout.Label("RESPONSE " + response.Id + "\nPhase " + response.Phase + " / case " + state + " / channel " + response.Channel +
                    "\nTime uncomfortable " + response.DwellSeconds.ToString("F1") + "s / phase since " + response.PhaseStartedAt.ToString("F1") +
                    "s\nSelf-help attempted " + response.SelfResponseAttempted + " / applied " + response.SelfResponseApplied +
                    " / at " + response.SelfResponseAt.ToString("F1") + "s\nAttempts " + response.ContactAttempts +
                    " / deadline " + response.AttemptDeadline.ToString("F1") + "s / retry " + response.RetryAt.ToString("F1") +
                    "s\nCommunicated " + response.CommunicatedAt.ToString("F1") + "s / acknowledged " + response.AcknowledgedAt.ToString("F1") +
                    "s / real staff action " + response.StaffActionAt.ToString("F1") + "s\nSource " + response.SourceEntityId +
                    " / episode " + response.IncidentEpisode + " / physical action version " + response.ActionVersion, body);
            }
            foreach (var intent in services.Intents.Where(item => item.GuestId == guest.GuestId))
                GUILayout.Label("INTENT " + intent.Id + " / revision " + intent.Revision + "\n" + intent.Kind + " / " + intent.Purpose + " / " + intent.Status +
                    " / known " + GuestLabels.IsKnownToHotel(intent, Session.Simulation) + "\nDeadline " + (intent.Deadline < 0 ? "none" :
                    GuestLabels.HotelMoment(Session.Simulation, intent.Deadline)) + " / point " + (intent.DeliveryPointId ?? "none") +
                    "\nItem " + (intent.ItemId ?? "none") + " generation " + intent.ItemGeneration + " / left " + intent.DeliveredAt.ToString("F1") +
                    " / received " + intent.ReceivedAt.ToString("F1") + "\nResolution " + (intent.ResolutionReason ?? "pending"), body);
            bool active = Session.Phase == DayPhase.Service && settings.NaturalCommunicationEnabled;
            GUILayout.BeginHorizontal();
            if (Button("Force severe cold", active && guest.Agent.InAssignedRoom)) Apply(() => Session.DebugForceSevereCold(guest.GuestId));
            if (Button("Force real noise exposure", active && guest.Agent.InAssignedRoom)) Apply(() => Session.DebugForceNoiseExposure(guest.GuestId));
            GUILayout.EndHorizontal();
            if (Button("Force self-help · walk to own radiator", active)) Apply(() => Session.DebugBeginGuestSelfResponse(guest.GuestId));
            GUILayout.BeginHorizontal();
            if (Button("Force phone contact", active)) Apply(() => Session.DebugBeginGuestContact(guest.GuestId, GuestContactChannel.Phone));
            if (Button("Force front-desk request", active)) Apply(() => Session.DebugBeginGuestContact(guest.GuestId, GuestContactChannel.Reception));
            GUILayout.EndHorizontal();
            var pending = responses.FirstOrDefault(r => r.CommunicatedAt < 0 && r.Phase != GuestResponsePhase.Cancelled);
            if (Button("Cancel selected guest contact", active && pending != null)) Apply(() => Session.DebugCancelGuestContact(pending.Id));
            if (Button("Force escalation through a real condition", active)) Apply(() => Session.DebugForceGuestEscalation(guest.GuestId));
        }
    }
}
#endif
