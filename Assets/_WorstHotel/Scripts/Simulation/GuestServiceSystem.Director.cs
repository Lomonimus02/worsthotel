using System.Linq;

namespace WorstHotel
{
    public sealed partial class GuestServiceSystem
    {
        internal bool CanEncourageService(GuestStay guest, ServiceKind kind) => !simulation.IsReadOnlyMirror && !Departed(guest) &&
            NaturalCommunicationEnabled && BudgetAvailable(guest) && CanInterrupt(guest) && guest.Agent.ResponseActionId == null &&
            !cases.Any(c => c.GuestId == guest.GuestId && (c.Active || c.Kind == kind)) &&
            (kind != ServiceKind.ExtraBlanket || simulation.Incidents.Items.Any(i => i.GuestId == guest.GuestId && i.Active && i.Reason == IncidentReason.Temperature)) &&
            TryCause(guest, kind, simulation.Elapsed, out _, out _, out _, out _);

        // Bypass only the stable willingness roll. Cause, privacy, schedule, one-case and
        // communication constraints are the exact same ones used by natural service.
        internal string EncourageService(GuestStay guest, ServiceKind kind)
        {
            if (!CanEncourageService(guest, kind) || !TryCause(guest, kind, simulation.Elapsed, out var source, out int room, out float due, out var reason)) return null;
            var request = TryCreate(guest, kind, simulation.Elapsed, due, source, room, reason);
            if (request == null) return null;
            BeginContact(request.Response, guest, ChooseChannel(request.Response, guest), simulation.Elapsed);
            return request.Id;
        }
    }
}
