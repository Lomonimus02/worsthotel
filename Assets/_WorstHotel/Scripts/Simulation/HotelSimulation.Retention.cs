using System;
using System.Collections.Generic;
using System.Linq;

namespace WorstHotel
{
    public sealed partial class HotelSimulation
    {
        // Reports contain their own receipts. They do not require keeping every former body,
        // service case and incident alive for the lifetime of an open hotel.
        internal void PruneCompletedOperatingHistory()
        {
            if (!ContinuousOperations || IsReadOnlyMirror) return;
            int cutoff = Calendar.Day - 2;
            var historyOwners = new HashSet<string>(guests.Where(guest => !guest.ReceiptPosted ||
                LivingEnabled && guest.Agent.State != GuestAgentState.Left ||
                FindReservation(guest.GuestId)?.Offer.ArrivalDay >= cutoff).Select(guest => guest.GuestId));
            void Pin(string id) { if (!string.IsNullOrEmpty(id)) historyOwners.Add(id); }
            foreach (var room in rooms.Values)
            { Pin(room.GuestId); Pin(room.ReservedGuestId); Pin(room.DepartingGuestId); }
            foreach (var key in Keys.Items) Pin(key.GuestId);
            foreach (var receipt in periodReceipts) Pin(receipt.GuestId);
            foreach (var guest in guests)
                if (!string.IsNullOrEmpty(guest.Agent?.ResponseActionId)) Pin(guest.GuestId);
            foreach (var incident in Incidents.AllIncidents)
                if (incident.Active) Pin(incident.GuestId);
            if (Services != null)
            {
                foreach (var item in Services.Cases.Where(item => item.Active)) Pin(item.GuestId);
                foreach (var promise in Services.Promises.Where(item => item.Status == PromiseStatus.Accepted)) Pin(promise.GuestId);
                foreach (var item in Services.Items.Where(item => item.Location == ServiceItemLocation.HeldByPlayer ||
                    item.Location == ServiceItemLocation.Dropped)) Pin(item.GuestId);
            }
            var retainedIncidents = new HashSet<string>(Incidents.AllIncidents.Where(item =>
                historyOwners.Contains(item.GuestId)).Select(item => item.Id));
            if (Services != null)
            {
                foreach (var item in Services.Cases.Where(item => historyOwners.Contains(item.GuestId)))
                    if (item.Response?.IncidentId != null) retainedIncidents.Add(item.Response.IncidentId);
                foreach (var guest in guests)
                {
                    var response = Services.FindResponse(guest.Agent?.ResponseActionId);
                    if (response?.IncidentId != null) retainedIncidents.Add(response.IncidentId);
                }
            }
            var retainedGuests = new HashSet<string>(historyOwners);
            void KeepIdentity(string id) { if (!string.IsNullOrEmpty(id)) retainedGuests.Add(id); }
            foreach (var incident in Incidents.AllIncidents.Where(item => retainedIncidents.Contains(item.Id)))
            { KeepIdentity(incident.GuestId); KeepIdentity(incident.Cause?.SourceGuestId); }
            if (Noise != null) foreach (var source in Noise.Sources) KeepIdentity(source.SourceGuestId);
            // Perception is also serialized. Close only its identity references, not each
            // source guest's unrelated incident/service history (which could retain a chain).
            int previousCount;
            do
            {
                previousCount = retainedGuests.Count;
                foreach (var guest in guests.Where(item => retainedGuests.Contains(item.GuestId)))
                {
                    KeepIdentity(guest.Perception.TemperatureCause?.SourceGuestId);
                    foreach (var cause in guest.Perception.ConditionCauses) KeepIdentity(cause.SourceGuestId);
                    foreach (var source in guest.Perception.NoiseSources) KeepIdentity(source.SourceGuestId);
                }
            } while (retainedGuests.Count != previousCount);
            Services?.PruneCompletedStays(historyOwners, retainedGuests, retainedIncidents);
            Requests.PruneCompletedStays(historyOwners, retainedIncidents);
            Incidents.PruneCompletedStays(historyOwners, retainedIncidents);
            Schedules?.PruneCompletedStays(retainedGuests);
            guests.RemoveAll(guest => !retainedGuests.Contains(guest.GuestId));
            reservations.RemoveAll(item => !item.Active && item.Offer.ArrivalDay < cutoff && !retainedGuests.Contains(item.Id));
        }
    }
}
