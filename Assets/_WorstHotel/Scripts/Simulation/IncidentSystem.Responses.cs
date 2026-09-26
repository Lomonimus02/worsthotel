using System.Linq;

namespace WorstHotel
{
    public sealed partial class IncidentSystem
    {
        internal void BeginTransfer(string guestId, int targetRoomId)
        {
            foreach (var incident in incidents.Values)
            {
                if (incident.GuestId != guestId) continue;
                incident.RoomId = targetRoomId;
                incident.PausedForTransfer = incident.Active || incident.ResponseReliefRemainingSeconds > 0;
            }
        }

        internal void EndTransfer(string guestId, int roomId)
        {
            foreach (var incident in incidents.Values)
            {
                if (incident.GuestId != guestId) continue;
                incident.RoomId = roomId;
                incident.PausedForTransfer = false;
            }
        }

        internal int AcknowledgeAttention(string guestId)
        {
            int changed = 0;
            foreach (var incident in incidents.Values)
            {
                if (incident.GuestId != guestId || !incident.Active || incident.AttentionAcknowledged) continue;
                incident.AttentionAcknowledged = true;
                changed++;
            }
            return changed;
        }

        internal void AcceptCompensationResponse(GuestStay guest)
        {
            if (!LivingEnabled) return;
            var accepted = incidents.Values.Where(incident => incident.GuestId == guest.GuestId && incident.Active).ToArray();
            guest.Memory.CompensationReceived = guest.CompensationCredit;
            foreach (var incident in accepted)
            {
                incident.ResponseAccepted = true;
                incident.AttentionAcknowledged = true;
                incident.PausedForTransfer = false;
                incident.ResponseReliefRemainingSeconds = needSettings.CompensationReliefSeconds * incident.EffectivePatienceMultiplier;
                switch (incident.Reason)
                {
                    case IncidentReason.Temperature: incident.Dissatisfaction = guest.Needs.Temperature.Dissatisfaction; break;
                    case IncidentReason.Noise:
                        float multiplier = System.Math.Max(needSettings.MinimumRepeatPatienceMultiplier,
                            1 - System.Math.Max(0, guest.Memory.ComplaintsInCategory(IncidentReason.Noise) - 1) * needSettings.RepeatPatienceReduction);
                        incident.Dissatisfaction = System.Math.Max(0, incident.Dissatisfaction - needSettings.CompensationDissatisfactionReduction * multiplier);
                        break;
                    case IncidentReason.RoomCondition: incident.Dissatisfaction = guest.Needs.RoomCondition.Dissatisfaction; break;
                    case IncidentReason.Service: incident.Dissatisfaction = guest.Needs.Service.Dissatisfaction; break;
                }
                incident.ResolutionReason = null;
                AddHistory(incident, "Compensation accepted: $" + guest.CompensationCredit + "; renewed patience, cause unchanged.");
            }
            // This updates the existing card. OfferCompensation emits the response event;
            // re-emitting the unchanged complaint stage here would create a duplicate notification.
        }
    }
}
