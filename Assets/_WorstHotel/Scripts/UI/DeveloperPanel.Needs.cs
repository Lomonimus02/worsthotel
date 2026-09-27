#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Linq;
using UnityEngine;

namespace WorstHotel
{
    public sealed partial class DeveloperPanel
    {
        void DrawGuestNeedsDebug(GuestStay guest)
        {
            var needs = guest.Needs;
            if (needs == null) { GUILayout.Label("Needs begin with the first living-hotel tick.", body); return; }
            GUILayout.Label("NEEDS: severity / exposed seconds / accumulated dissatisfaction", body);
            NeedRow("Temperature", needs.Temperature);
            NeedRow("Noise", needs.Noise);
            NeedRow("Room condition", needs.RoomCondition);
            NeedRow("Service / patience", needs.Service);
            var profile = guest.Application.Archetype.Needs;
            GUILayout.Label("Temperature preference " + profile.PreferredTemperatureMin + "–" + profile.PreferredTemperatureMax +
                "°C; tolerance " + profile.ToleranceTemperatureMin + "–" + profile.ToleranceTemperatureMax +
                "°C\nNoise preference ≤" + profile.PreferredNoise.ToString("F2") + "; tolerance ≤" + profile.NoiseTolerance.ToString("F2") +
                "; patience " + profile.PatienceSeconds.ToString("F0") + "s", body);
            var perception = guest.Perception;
            GUILayout.Label(perception.InAssignedRoom ? "EXPERIENCED: Room " + perception.RoomId + " / " +
                perception.Temperature.ToString("F1") + "°C / noise " + perception.Noise.ToString("F3") :
                "EXPERIENCED: outside assigned room — room exposure suspended", body);
            foreach (var source in perception.NoiseSources)
                GUILayout.Label("Heard " + source.SourceEntityId + " / room " + source.SourceRoomId + " / " + source.Label +
                    " / raw " + source.NoiseOutput.ToString("F3") + " → received " + source.ReceivedNoise.ToString("F3"), body);
            var memory = guest.Memory;
            GUILayout.Label("MEMORY: complaints " + memory.NumberOfComplaints + " / compensation $" + memory.CompensationReceived +
                "\nResolved " + memory.ProblemsResolvedSuccessfully + " / ignored " + memory.ProblemsIgnored +
                " / previous noise warnings " + memory.PreviousNoiseWarnings + "\nRepeated problems: " +
                string.Join(" / ", memory.RepeatedProblemCount.Select(pair => pair.Key + " " + pair.Value)), body);
            var departure = guest.EarlyCheckout;
            if (departure != null)
                GUILayout.Label("EARLY CHECKOUT (debug): " + departure.State + " / known warning " + Session.Simulation.IsEarlyCheckoutWarningKnown(guest) +
                    "\nCause " + departure.IncidentId + " / episode " + departure.IncidentEpisode + " / room " + departure.RoomId + " / " + departure.Reason +
                    "\nSevere room exposure " + departure.SevereExposureSeconds.ToString("F1") + "s / recovery " + departure.RecoverySeconds.ToString("F1") +
                    "s / remaining severe-exposure grace " + departure.GraceRemainingSeconds.ToString("F1") + "s" +
                    "\nWarning at " + departure.WarningAt.ToString("F1") + " / committed at " + departure.CommittedAt.ToString("F1") +
                    "\nCaptured terminal cause: " + (departure.CauseDescription ?? "none"), body);
            foreach (var situation in Session.Simulation.Incidents.Items.Where(s => s.GuestId == guest.GuestId))
            {
                GUILayout.Label("CASE " + situation.Id + "\nAffected " + situation.GuestName + " (" + situation.GuestId + ") / room " +
                    situation.RoomId + " / " + situation.Reason + " / " + situation.Stage + " / episode " + situation.EpisodeCount +
                    "\nDuration " + situation.ExposureSeconds.ToString("F1") + "s / severity " + situation.Severity.ToString("F3") +
                    " / patience multiplier " + situation.EffectivePatienceMultiplier.ToString("F2") + "\n" + situation.MeasuredCause, body);
                var cause = situation.Cause;
                if (cause != null) GUILayout.Label("CAUSE " + cause.SourceType + " / entity " + cause.SourceEntityId +
                    " / source room " + cause.SourceRoomId + "\nRaw " + cause.RawIntensity.ToString("F3") + " / received " +
                    cause.ReceivedIntensity.ToString("F3") + " / guest threshold " + cause.GuestThreshold.ToString("F3"), body);
                foreach (var entry in situation.History)
                    GUILayout.Label("  " + entry.Time.ToString("F1") + "s — " + entry.Reason, body);
            }
        }

        void NeedRow(string label, GuestNeedSnapshot value) => GUILayout.Label(label + ": " +
            value.Severity.ToString("F3") + " / " + value.ExposureSeconds.ToString("F1") + "s / " + value.Dissatisfaction.ToString("F3"), body);
    }
}
#endif
