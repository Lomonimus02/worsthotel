using System;

namespace WorstHotel
{
    public sealed partial class IncidentSystem
    {
        // Continuous operations declare factual room problems Remote even when no optional
        // service case exists. A null policy preserves the historical shift behavior.
        internal Func<string, bool> ShouldDeferRemoteEvaluation { get; set; }

        bool SuspendRemoteIncident(GuestStay guest, HotelIncident incident) =>
            (!guest.Agent.InAssignedRoom || !guest.Perception.InAssignedRoom) &&
            (incident.Reason == IncidentReason.Temperature || incident.Reason == IncidentReason.Noise ||
                incident.Reason == IncidentReason.RoomCondition) &&
            ShouldDeferRemoteEvaluation != null && ShouldDeferRemoteEvaluation(guest.GuestId);
    }
}
