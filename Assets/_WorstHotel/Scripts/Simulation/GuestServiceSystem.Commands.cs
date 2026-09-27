using System;
using System.Linq;

namespace WorstHotel
{
    public sealed partial class GuestServiceSystem
    {
        CommandResult CanAct(int actor)
        {
            if (simulation.IsReadOnlyMirror) return CommandResult.Fail(HotelSimulation.MirrorMessage);
            if (!simulation.Running) return CommandResult.Fail("Guest services require an active hotel shift.");
            return actor < 0 || actor > 1 ? CommandResult.Fail("Unknown player identity.") : CommandResult.Ok();
        }

        public CommandResult Acknowledge(int actor, string caseId)
        {
            var allowed = CanAct(actor); if (!allowed.Success) return allowed;
            var item = FindCase(caseId);
            if (item == null || item.Status != ServiceStatus.Requested) return CommandResult.Fail("This service is no longer waiting for acknowledgement.");
            if (!item.IsKnownToHotel) return CommandResult.Fail("First speak with the guest about this concern.");
            if (item.Response != null) item.Response.AcknowledgedAt = simulation.Elapsed;
            item.Status = ServiceStatus.Acknowledged; Notify(item, "acknowledged");
            return CommandResult.Ok("Request noted. The guest still needs an actual response.");
        }

        public CommandResult Respond(int actor, string caseId, bool accept)
        {
            var allowed = CanAct(actor); if (!allowed.Success) return allowed;
            var item = FindCase(caseId); var guest = item == null ? null : Guest(item.GuestId);
            if (item == null || !item.Active || guest == null || Departed(guest)) return CommandResult.Fail("This service request is no longer active.");
            if (!item.IsKnownToHotel) return CommandResult.Fail("First speak with the guest about this concern.");
            if (!accept)
            {
                var promise = FindPromise(item.Id);
                if (promise != null && promise.Status == PromiseStatus.Accepted)
                {
                    if (simulation.Elapsed >= promise.DueTime) { MissPromise(promise, guest); return CommandResult.Ok("The overdue call was not made; the guest remembers the broken promise."); }
                    promise.Status = PromiseStatus.Cancelled;
                }
                Finish(item, guest, ServiceStatus.Declined, -Settings.DeclinedPenalty);
                return CommandResult.Ok("Service declined. The physical conditions remain unchanged.");
            }
            if (item.Status == ServiceStatus.InProgress) return CommandResult.Fail("This service has already been accepted.");
            if (item.Kind == ServiceKind.LateCheckout)
            {
                var result = simulation.ApplyServiceLateCheckout(guest, item.DueTime);
                if (!result.Success) return result;
                Finish(item, guest, ServiceStatus.Fulfilled, Settings.FulfilledBonus, true);
                return CommandResult.Ok("Late checkout accepted. The guest keeps the room longer and turnover starts later.");
            }
            if (item.Kind == ServiceKind.WakeUpCall)
            {
                if (simulation.Elapsed >= item.DueTime || FindPromise(item.Id) != null) return CommandResult.Fail("There is no longer time to accept that wake-up call.");
                promises.Add(new PromiseWakeUp(item.Id, guest.GuestId, guest.RoomId, item.DueTime));
            }
            ReleaseDecisionIntent(item);
            if (item.Kind == ServiceKind.LuggageStorage) AcceptLuggageResponsibility(guest);
            item.Status = ServiceStatus.InProgress; Notify(item, "accepted");
            if (IntentBehaviorEnabled && item.Kind == ServiceKind.ExtraBlanket)
                return CommandResult.Ok("Extra blanket promised. Carry one to this room's delivery point; the guest will receive it when available.");
            return CommandResult.Ok(item.Kind == ServiceKind.WakeUpCall ? "Wake-up promised. Use the reception telephone near the due time." :
                "Service accepted. Complete it using the actual hotel and its supplies.");
        }

        void TickPromises(float now)
        {
            foreach (var promise in promises.Where(item => item.Status == PromiseStatus.Accepted).ToArray())
            {
                var guest = Guest(promise.GuestId); if (guest == null) continue;
                promise.RoomId = guest.RoomId;
                if (now > promise.DueTime + Settings.WakeMissSeconds) { MissPromise(promise, guest); continue; }
                if (!promise.DueNotified && now >= promise.DueTime)
                { promise.DueNotified = true; simulation.SignalEvent("Wake-up call is due for room " + guest.RoomId); }
            }
        }

        public CommandResult CompleteWakeUp(int actor, string promiseId)
        {
            var allowed = CanAct(actor); if (!allowed.Success) return allowed;
            var promise = FindPromise(promiseId); var guest = promise == null ? null : Guest(promise.GuestId);
            if (promise == null || promise.Status != PromiseStatus.Accepted || guest == null || Departed(guest))
                return CommandResult.Fail("There is no accepted wake-up promise to call.");
            float now = simulation.Elapsed;
            if (now < promise.DueTime - Settings.WakeToleranceSeconds) return CommandResult.Fail("It is too early for this wake-up call.");
            if (now > promise.DueTime + Settings.WakeMissSeconds)
            { MissPromise(promise, guest); return CommandResult.Fail("That wake-up promise was missed."); }
            if (!guest.Agent.InAssignedRoom) return CommandResult.Fail("The guest is not in the room to answer the telephone.");
            promise.Status = PromiseStatus.Completed; promise.CompletedAt = now;
            guest.Memory.PromisesKept = Count(guest.Memory.PromisesKept);
            bool onTime = now <= promise.DueTime + Settings.WakeToleranceSeconds;
            Finish(FindCase(promise.Id), guest, ServiceStatus.Fulfilled, onTime ? Settings.FulfilledBonus : -Settings.DeclinedPenalty, true);
            if (guest.Agent.State == GuestAgentState.Sleeping)
            {
                if (guest.Agent.Schedule.HasDailyRhythm) simulation.BeginGuestMorningRoutine(guest, now);
                else simulation.ForceActivity(guest.GuestId, GuestActivity.QuietRest);
            }
            return CommandResult.Ok(onTime ? "Wake-up call completed on time. Thank you." : "The guest answered the late wake-up call.");
        }

        void MissPromise(PromiseWakeUp promise, GuestStay guest)
        {
            if (promise.Status != PromiseStatus.Accepted) return;
            promise.Status = PromiseStatus.Missed; guest.Memory.PromisesBroken = Count(guest.Memory.PromisesBroken);
            Finish(FindCase(promise.Id), guest, ServiceStatus.Expired, -Settings.BrokenPromisePenalty);
        }

        public CommandResult ForceRequest(string guestId, ServiceKind kind)
        {
            var allowed = CanAct(0); if (!allowed.Success) return allowed;
            if (!Enum.IsDefined(typeof(ServiceKind), kind)) return CommandResult.Fail("Unknown service kind.");
            var guest = Guest(guestId);
            if (guest == null || Departed(guest)) return CommandResult.Fail("Choose a current guest.");
            if (cases.Count >= CaseCapacity || NaturalCommunicationEnabled && !BudgetAvailable(guest))
                return CommandResult.Fail("The finite service contact allowance is already used.");
            if (cases.Any(item => item.GuestId == guestId && item.Active)) return CommandResult.Fail("This guest already has an active service case.");
            if (!TryCause(guest, kind, simulation.Elapsed, out string source, out int sourceRoom, out float due, out string reason, true))
                return CommandResult.Fail("The factual prerequisite is missing: mild cold, real noise, an unready room, or a suitable remaining schedule.");
            return TryCreate(guest, kind, simulation.Elapsed, due, source, sourceRoom, reason) != null ?
                CommandResult.Ok("Developer service request created from a real cause.") : CommandResult.Fail("This guest already requested that service during this stay.");
        }

        public CommandResult SetBlanketStock(int count)
        {
            var allowed = CanHandle(0); if (!allowed.Success) return allowed;
            if (count < 0 || count > 6) return CommandResult.Fail("Blanket stock must be between zero and six.");
            int assigned = items.Count(item => item.Kind == ServiceItemKind.Blanket &&
                (item.GuestId != null || item.Location == ServiceItemLocation.HeldByPlayer || item.Location == ServiceItemLocation.Dropped));
            if (count + assigned > 6) return CommandResult.Fail("The six physical blanket slots include blankets already in use.");
            foreach (var item in items.Where(item => item.Kind == ServiceItemKind.Blanket && item.Location == ServiceItemLocation.OnShelf).ToArray())
            { item.Location = ServiceItemLocation.Delivered; ItemChanged?.Invoke(item); }
            for (int index = 0; index < count; index++)
            {
                var slot = items.FirstOrDefault(item => item.Kind == ServiceItemKind.Blanket && item.Location == ServiceItemLocation.Delivered && item.GuestId == null);
                if (slot == null)
                { int next = items.Count(item => item.Kind == ServiceItemKind.Blanket); slot = new ServiceItemState("blanket:" + next, ServiceItemKind.Blanket, day); items.Add(slot); }
                slot.Location = ServiceItemLocation.OnShelf; slot.Generation++; ItemChanged?.Invoke(slot);
            }
            return CommandResult.Ok("Developer shelf stock updated; carried and delivered blankets were preserved.");
        }
    }
}
