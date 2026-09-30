using System;
using System.Collections.Generic;
using System.Linq;

namespace WorstHotel
{
    public sealed partial class GuestServiceSystem
    {
        // Root selects owner history separately from identity-only causal sources. A source
        // retained by another guest's history must not drag its own entire history back in.
        internal void PruneCompletedStays(ISet<string> historyOwnerIds, ISet<string> retainedGuestIds,
            ISet<string> retainedIncidentIds)
        {
            if (simulation.IsReadOnlyMirror) return;
            if (historyOwnerIds == null || retainedGuestIds == null || retainedIncidentIds == null)
                throw new ArgumentNullException("Retention needs owner, identity and incident sets.");
            // Current owners keep their finite intent decisions, including move ordinals.
            // Identity-only causal sources do not retain a second tree of finished services.
            intents.RemoveAll(item => !item.Active && !historyOwnerIds.Contains(item.GuestId));
            var protectedResponses = new HashSet<string>(simulation.Incidents.Items
                .Where(incident => retainedIncidentIds.Contains(incident.Id) && incident.Response != null)
                .Select(incident => incident.Response.Id));
            foreach (var guest in simulation.Guests)
                if (!string.IsNullOrEmpty(guest.Agent?.ResponseActionId)) protectedResponses.Add(guest.Agent.ResponseActionId);
            var promisedCases = new HashSet<string>(promises.Where(promise => promise.Status == PromiseStatus.Accepted)
                .Select(promise => promise.Id));
            cases.RemoveAll(item => !historyOwnerIds.Contains(item.GuestId) && !item.Active &&
                !promisedCases.Contains(item.Id) && (item.Response == null || !protectedResponses.Contains(item.Response.Id)));
            var keptCases = new HashSet<string>(cases.Select(item => item.Id));
            promises.RemoveAll(item => item.Status != PromiseStatus.Accepted && !keptCases.Contains(item.Id));
            foreach (var item in cases)
                if (item.Response != null) protectedResponses.Add(item.Response.Id);
            responses.RemoveAll(item => !protectedResponses.Contains(item.Id));
            foreach (var intent in intents)
                if (intent.ResponseId != null && !protectedResponses.Contains(intent.ResponseId)) intent.ResponseId = null;

            items.RemoveAll(item => item.Kind == ServiceItemKind.Luggage && !historyOwnerIds.Contains(item.GuestId) &&
                item.Location != ServiceItemLocation.HeldByPlayer && item.Location != ServiceItemLocation.Dropped &&
                (Guest(item.GuestId) == null || Departed(Guest(item.GuestId))));
            foreach (var item in items)
            {
                // Fixed stock slots survive pruning. Their consumed state is not a free refill.
                if (item.Kind == ServiceItemKind.Luggage || item.GuestId == null ||
                    item.Location == ServiceItemLocation.HeldByPlayer || item.Location == ServiceItemLocation.Dropped ||
                    item.Location == ServiceItemLocation.AwaitingReceipt) continue;
                if (retainedGuestIds.Contains(item.GuestId) && historyOwnerIds.Contains(item.GuestId)) continue;
                var guest = Guest(item.GuestId);
                if (guest != null && !Departed(guest)) continue;
                item.GuestId = null;
                // A used blanket still belongs to this room until its bed turnover completes.
                if (!simulation.ContinuousOperations || item.Kind != ServiceItemKind.Blanket ||
                    item.Location != ServiceItemLocation.Delivered) item.RoomId = null;
                if (item.Generation < int.MaxValue) item.Generation++;
            }
        }
    }
}
