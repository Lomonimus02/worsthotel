using System;
using System.Collections.Generic;
using System.Linq;

namespace WorstHotel
{
    public sealed partial class GuestServiceSystem
    {
        internal readonly List<GuestServiceIntent> intents = new List<GuestServiceIntent>();
        public IReadOnlyList<GuestServiceIntent> Intents => intents.AsReadOnly();
        public bool IntentBehaviorEnabled => simulation.ContinuousOperations;
        public GuestServiceIntent FindIntent(string id) => id == null ? null : intents.FirstOrDefault(item => item.Id == id);
        public GuestServiceIntent DirectIntent(string guestId) => intents.FirstOrDefault(item => item.GuestId == guestId &&
            item.Kind == ServiceIntentKind.Direct && item.Active);
        public GuestServiceIntent DropOffIntent(string guestId) => intents.FirstOrDefault(item => item.GuestId == guestId &&
            item.Kind == ServiceIntentKind.DropOff && item.Active);
        public GuestServiceIntent PendingDeliveryForItem(string itemId) => intents.FirstOrDefault(item => item.ItemId == itemId &&
            item.Status == ServiceIntentStatus.AwaitingReceipt);
        public static string BlanketDropPointId(int roomId) => "room/" + roomId + "/blanket-drop";

        GuestServiceIntent AddIntent(string id, GuestStay guest, ServiceIntentKind kind, ServiceIntentPurpose purpose)
        {
            var existing = FindIntent(id);
            if (existing != null) return existing;
            if (intents.Count >= 1024) return null;
            var intent = new GuestServiceIntent(id, guest.GuestId, guest.RoomId, kind, purpose, simulation.Elapsed);
            intents.Add(intent); return intent;
        }

        bool CanCommunicateIntent(GuestStay guest, ServiceCase request)
        {
            if (!IntentBehaviorEnabled) return true;
            var current = DirectIntent(guest.GuestId);
            if (current != null && (request == null || current.CaseId != request.Id)) return false;
            bool newDirect = request != null && request.Active && request.Kind != ServiceKind.ExtraBlanket &&
                request.Kind != ServiceKind.AskNeighborsQuiet && FindIntent(request.Id + "/intent") == null;
            if (newDirect && simulation.EarlyCheckoutDecisionPending(guest)) return false;
            return request == null || !request.Active || request.Kind == ServiceKind.AskNeighborsQuiet ||
                FindIntent(request.Id + "/intent") != null || intents.Count < 1024;
        }

        void BeginCaseIntent(GuestStay guest, ServiceCase request)
        {
            if (!IntentBehaviorEnabled || request == null || !request.Active) return;
            // Quiet-neighbour assistance remains the existing factual Remote incident.
            if (request.Kind == ServiceKind.AskNeighborsQuiet) return;
            bool blanket = request.Kind == ServiceKind.ExtraBlanket;
            var intent = AddIntent(request.Id + "/intent", guest, blanket ? ServiceIntentKind.DropOff : ServiceIntentKind.Direct,
                blanket ? ServiceIntentPurpose.BlanketDelivery : ServiceIntentPurpose.ServiceDecision);
            if (intent == null || !intent.Active) return;
            intent.CaseId = request.Id; intent.ResponseId = request.Response?.Id; intent.IncidentId = request.Response?.IncidentId;
            if (blanket) { intent.DeliveryPointId = BlanketDropPointId(guest.RoomId); return; }
            intent.Deadline = Math.Min(guest.Agent.CheckoutTime, simulation.Elapsed + Settings.DirectWaitSeconds);
            if (request.Kind == ServiceKind.WakeUpCall) intent.Deadline = Math.Min(intent.Deadline, request.DueTime);
            guest.Agent.DirectServiceIntentId = intent.Id;
            simulation.HoldGuestForServiceIntent(guest);
        }

        internal CommandResult CanBeginRoomMoveIntent(GuestStay guest)
        {
            if (!IntentBehaviorEnabled) return CommandResult.Ok();
            var current = DirectIntent(guest.GuestId);
            if (current?.Purpose != ServiceIntentPurpose.RoomMove && simulation.EarlyCheckoutDecisionPending(guest))
                return CommandResult.Fail("The guest's departure decision is pending. A new room proposal cannot restart the wait.");
            if (current != null && current.Purpose != ServiceIntentPurpose.RoomMove && current.Purpose != ServiceIntentPurpose.CompensationDiscussion)
                return CommandResult.Fail("Finish the guest's current direct service decision before proposing another.");
            if (guest.Agent.State == GuestAgentState.Sleeping || guest.Agent.Activity == GuestActivity.Shower ||
                guest.Agent.IsRelocating || guest.Agent.ResponseActionId != null && current == null)
                return CommandResult.Fail("The guest is not currently available for a room-key exchange.");
            if (guest.Agent.CheckoutTime - simulation.Elapsed <= Settings.ContactLeadSeconds)
                return CommandResult.Fail("There is no longer enough time to arrange a room change before checkout.");
            return current?.Purpose == ServiceIntentPurpose.RoomMove || intents.Count < 1024 ? CommandResult.Ok() : CommandResult.Fail("Service history capacity reached.");
        }

        internal void BeginRoomMoveIntent(GuestStay guest, int destination)
        {
            if (!IntentBehaviorEnabled) return;
            var current = DirectIntent(guest.GuestId);
            if (current != null && current.Purpose != ServiceIntentPurpose.CompensationDiscussion) return;
            if (current != null) CloseIntent(current, ServiceIntentStatus.Cancelled, "Room change chosen instead", false);
            int ordinal = 1;
            while (FindIntent(guest.GuestId + "/move/" + ordinal) != null) ordinal++;
            string id = guest.GuestId + "/move/" + ordinal;
            var intent = AddIntent(id, guest, ServiceIntentKind.Direct, ServiceIntentPurpose.RoomMove);
            if (intent == null) return;
            intent.Deadline = Math.Min(guest.Agent.CheckoutTime, simulation.Elapsed + Settings.DirectWaitSeconds);
            guest.Agent.DirectServiceIntentId = intent.Id;
            if (current != null && guest.Agent.ResponseActionId != null) simulation.ClearGuestResponseAction(guest, false);
            simulation.HoldGuestForServiceIntent(guest);
        }

        internal void FinishRoomMoveIntent(GuestStay guest, bool completed)
        {
            var intent = DirectIntent(guest.GuestId);
            if (intent?.Purpose == ServiceIntentPurpose.RoomMove)
                CloseIntent(intent, completed ? ServiceIntentStatus.Completed : ServiceIntentStatus.Cancelled,
                    completed ? "Room keys exchanged" : "Room change cancelled", !completed);
        }

        internal void ReleaseDecisionIntent(ServiceCase request)
        {
            var intent = intents.FirstOrDefault(item => item.CaseId == request.Id && item.Kind == ServiceIntentKind.Direct && item.Active);
            if (intent != null) CloseIntent(intent, ServiceIntentStatus.Completed, "Service decision recorded", true);
        }

        void CloseIntent(GuestServiceIntent intent, ServiceIntentStatus status, string reason, bool resume = true)
        {
            if (intent == null || !intent.Active) return;
            bool pending = intent.Status == ServiceIntentStatus.AwaitingReceipt;
            intent.Status = status; intent.ResolutionAt = simulation.Elapsed; intent.ResolutionReason = reason; intent.Revision++;
            var guest = Guest(intent.GuestId);
            if (pending && status != ServiceIntentStatus.Completed)
            {
                var parcel = FindItem(intent.ItemId);
                if (parcel != null && parcel.Generation == intent.ItemGeneration && parcel.Location == ServiceItemLocation.AwaitingReceipt)
                {
                    // Reclaim the actual parcel where staff placed it. No shelf reset or room teleport.
                    parcel.Location = ServiceItemLocation.Dropped; parcel.GuestId = null; parcel.RoomId = null;
                    parcel.Generation++; ItemChanged?.Invoke(parcel);
                }
            }
            if (guest?.Agent?.DirectServiceIntentId == intent.Id)
            {
                guest.Agent.DirectServiceIntentId = null;
                if (resume && !Departed(guest)) simulation.ResumeGuestAfterServiceIntent(guest);
            }
        }

        void TickIntents(float now)
        {
            if (!IntentBehaviorEnabled) return;
            foreach (var incident in simulation.Incidents.Items.Where(item => item.Active && item.Cause != null))
            {
                var guest = Guest(incident.GuestId); if (guest == null || Departed(guest)) continue;
                var remote = AddIntent(incident.Id + "/remote", guest,
                    ServiceIntentKind.Remote, ServiceIntentPurpose.Incident);
                if (remote != null)
                {
                    remote.IncidentId = incident.Id; remote.ResponseId = incident.Response?.Id;
                    if (!remote.Active)
                    { remote.Status = ServiceIntentStatus.Active; remote.ResolutionAt = -1; remote.ResolutionReason = null; remote.Revision++; }
                }
            }
            foreach (var intent in intents.Where(item => item.Active).ToArray())
            {
                var guest = Guest(intent.GuestId);
                if (guest == null || Departed(guest)) { CloseIntent(intent, ServiceIntentStatus.Cancelled, "Stay ended", false); continue; }
                if (intent.RoomId != guest.RoomId)
                {
                    if (intent.Kind == ServiceIntentKind.DropOff) { CloseIntent(intent, ServiceIntentStatus.Cancelled, "Guest changed rooms"); continue; }
                    intent.RoomId = guest.RoomId; intent.Revision++;
                }
                if (intent.Kind == ServiceIntentKind.Remote)
                {
                    if (intent.ResponseId != null && FindResponse(intent.ResponseId) == null) intent.ResponseId = null;
                    if (!simulation.Incidents.Items.Any(item => item.Id == intent.IncidentId && item.Active))
                        CloseIntent(intent, ServiceIntentStatus.Completed, "Measured incident recovery");
                    continue;
                }
                if (intent.Kind == ServiceIntentKind.Direct && now >= intent.Deadline)
                {
                    CloseIntent(intent, ServiceIntentStatus.TimedOut, "Direct wait elapsed");
                    if (intent.Purpose == ServiceIntentPurpose.RoomMove && guest.Agent.PendingMoveRoomId.HasValue)
                        simulation.CancelGuestMove(0, guest.GuestId);
                    else if (FindCase(intent.CaseId) is ServiceCase request && request.Active)
                        Finish(request, guest, ServiceStatus.Expired, 0);
                    simulation.SignalEvent("Room " + guest.RoomId + ": the guest's direct service wait ended.");
                    continue;
                }
                if (intent.Purpose == ServiceIntentPurpose.CompensationDiscussion &&
                    (!CompensationPresence(guest) || CompensableIncident(guest, intent.IncidentId) == null))
                {
                    CloseIntent(intent, ServiceIntentStatus.Cancelled, "Compensation discussion no longer needed");
                    continue;
                }
                if (intent.Status == ServiceIntentStatus.AwaitingReceipt && CanReceiveBlanket(guest)) ReceiveBlanket(intent, guest);
            }
        }

        bool CanReceiveBlanket(GuestStay guest) => guest?.Agent?.InAssignedRoom == true && !Departed(guest) &&
            guest.Agent.State != GuestAgentState.Sleeping && guest.Agent.Activity != GuestActivity.Shower &&
            !guest.Agent.IsRelocating && guest.Agent.ActivityStaged && rooms[guest.RoomId].GuestId == guest.GuestId;

        public CommandResult DropOffBlanket(int actor, string guestId, int roomId, int expectedIntentRevision, int expectedItemGeneration)
        {
            var allowed = CanAct(actor); if (!allowed.Success) return allowed;
            var guest = Guest(guestId); var intent = DropOffIntent(guestId); var item = HeldBy(actor);
            if (!IntentBehaviorEnabled || guest == null || Departed(guest) || guest.RoomId != roomId || guest.Agent.IsRelocating ||
                !rooms.TryGetValue(roomId, out var room) || room.GuestId != guestId)
                return CommandResult.Fail("This delivery point no longer belongs to the intended guest.");
            if (intent == null || intent.Status != ServiceIntentStatus.Active || intent.RoomId != roomId ||
                intent.Revision != expectedIntentRevision || FindCase(intent.CaseId)?.Status != ServiceStatus.InProgress)
                return CommandResult.Fail("First agree to this guest's current blanket delivery request.");
            if (item == null || item.Kind != ServiceItemKind.Blanket || item.Generation != expectedItemGeneration ||
                guest.Memory.BlanketsDelivered > 0)
                return CommandResult.Fail("Carry the current physical blanket; duplicate or stale deliveries are refused.");
            intent.ItemId = item.Id; intent.ItemGeneration = item.Generation; intent.DeliveredAt = simulation.Elapsed;
            intent.Status = ServiceIntentStatus.AwaitingReceipt; intent.Revision++;
            item.Location = ServiceItemLocation.AwaitingReceipt; item.PlayerId = null; item.GuestId = guestId; item.RoomId = roomId;
            ItemChanged?.Invoke(item);
            simulation.SignalEvent("Blanket left at room " + roomId + "; awaiting the guest's receipt.");
            return CommandResult.Ok("Blanket placed at the agreed room point. The guest will receive it when available.");
        }

        void ReceiveBlanket(GuestServiceIntent intent, GuestStay guest)
        {
            var item = FindItem(intent.ItemId);
            if (item == null || item.Generation != intent.ItemGeneration || item.Location != ServiceItemLocation.AwaitingReceipt ||
                item.GuestId != guest.GuestId || item.RoomId != guest.RoomId || guest.Memory.BlanketsDelivered > 0)
            { CloseIntent(intent, ServiceIntentStatus.Cancelled, "Delivery identity changed"); return; }
            guest.BlanketComfortBonus = Settings.BlanketComfortBonus;
            guest.Memory.BlanketsDelivered = Count(guest.Memory.BlanketsDelivered);
            intent.ReceivedAt = simulation.Elapsed; Deliver(item, guest);
            CloseIntent(intent, ServiceIntentStatus.Completed, "Guest received the actual blanket");
            RecordStaffAction(guest.GuestId, IncidentReason.Temperature);
            Finish(FindCase(intent.CaseId), guest, ServiceStatus.Fulfilled, Settings.FulfilledBonus, true);
        }

        void FinishCaseIntent(ServiceCase request, ServiceStatus status)
        {
            foreach (var intent in intents.Where(item => item.CaseId == request.Id && item.Active).ToArray())
                CloseIntent(intent, status == ServiceStatus.Fulfilled ? ServiceIntentStatus.Completed : ServiceIntentStatus.Cancelled,
                    "Service " + status);
        }

        void RecordInteriorBlanketReceipt(GuestStay guest, ServiceItemState item)
        {
            var intent = DropOffIntent(guest.GuestId);
            if (intent == null) return;
            intent.ItemId = item.Id; intent.ItemGeneration = item.Generation;
            intent.DeliveredAt = intent.ReceivedAt = simulation.Elapsed;
            CloseIntent(intent, ServiceIntentStatus.Completed, "Guest received the actual blanket inside the room");
        }

        void EndGuestIntents(GuestStay guest)
        {
            foreach (var intent in intents.Where(item => item.GuestId == guest.GuestId && item.Active).ToArray())
                CloseIntent(intent, ServiceIntentStatus.Cancelled, "Stay ended", false);
        }

        internal void CompleteCheckInContext(GuestStay guest)
        {
            if (!IntentBehaviorEnabled) return;
            // A real correct-key handoff makes waiting-room luggage assistance unnecessary.
            // End that decision atomically, before GoingToRoom can expose an unrelated action
            // in a snapshot. Already held/dropped suitcases remain the same physical items.
            foreach (var request in cases.Where(item => item.GuestId == guest.GuestId &&
                item.Kind == ServiceKind.LuggageStorage && item.Active && item.Status != ServiceStatus.InProgress).ToArray())
                Finish(request, guest, ServiceStatus.Expired, 0);
            var response = FindResponse(guest.Agent.ResponseActionId);
            if (response == null || FindCase(response.ServiceCaseId)?.Kind != ServiceKind.LuggageStorage) return;
            if (response.CommunicatedAt < 0) Cancel(response, guest, simulation.Elapsed);
            else simulation.ClearGuestResponseAction(guest, false);
        }

        bool ContactWindowAvailable(GuestStay guest, float now)
        {
            if (!IntentBehaviorEnabled) return true;
            var agent = guest.Agent;
            if (DirectIntent(guest.GuestId) != null || agent.IsRelocating || agent.CheckoutTime - now <= Settings.ContactLeadSeconds) return false;
            if (agent.State == GuestAgentState.WaitingForCheckIn) return true;
            if (!agent.InAssignedRoom || agent.State == GuestAgentState.Sleeping || agent.Activity == GuestActivity.Shower) return false;
            if (agent.ResponseActionId != null) return true;
            if (!agent.SleepStarted && agent.Schedule.SleepTime - now <= Settings.ContactLeadSeconds) return false;
            return agent.NextPlannedActivity != GuestActivity.LeaveHotel || agent.NextActivityTime - now > Settings.ContactLeadSeconds;
        }
    }
}
