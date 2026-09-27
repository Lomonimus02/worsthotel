using System;
using System.Collections.Generic;
using System.Linq;

namespace WorstHotel
{
    [Serializable] public sealed class ServiceIntentSnapshot
    {
        public string Id, GuestId, CaseId, ResponseId, IncidentId, DeliveryPointId, ItemId, ResolutionReason;
        public int RoomId, Revision, ItemGeneration;
        public ServiceIntentKind Kind;
        public ServiceIntentPurpose Purpose;
        public ServiceIntentStatus Status;
        public float CreatedAt, Deadline, DeliveredAt, ReceivedAt, ResolutionAt;
    }

    public sealed partial class GuestServiceSystem
    {
        ServiceIntentSnapshot[] CaptureIntents() => Intents.Select(intent => new ServiceIntentSnapshot
        {
            Id = intent.Id, GuestId = intent.GuestId, RoomId = intent.RoomId, Revision = intent.Revision,
            Kind = intent.Kind, Purpose = intent.Purpose, Status = intent.Status, CreatedAt = intent.CreatedAt,
            Deadline = intent.Deadline, CaseId = SnapshotData.OptionalId(intent.CaseId),
            ResponseId = SnapshotData.OptionalId(intent.ResponseId), IncidentId = SnapshotData.OptionalId(intent.IncidentId),
            DeliveryPointId = SnapshotData.OptionalId(intent.DeliveryPointId), ItemId = SnapshotData.OptionalId(intent.ItemId),
            ItemGeneration = intent.ItemGeneration, DeliveredAt = intent.DeliveredAt, ReceivedAt = intent.ReceivedAt,
            ResolutionAt = intent.ResolutionAt, ResolutionReason = intent.ResolutionReason
        }).ToArray();

        void RestoreIntents(ServiceLayerSnapshot data)
        {
            intents.Clear();
            foreach (var intent in data.Intents)
                intents.Add(new GuestServiceIntent(intent.Id, intent.GuestId, intent.RoomId, intent.Kind, intent.Purpose, intent.CreatedAt)
                {
                    Status = intent.Status, Revision = intent.Revision, Deadline = intent.Deadline,
                    CaseId = SnapshotData.OptionalId(intent.CaseId), ResponseId = SnapshotData.OptionalId(intent.ResponseId),
                    IncidentId = SnapshotData.OptionalId(intent.IncidentId), DeliveryPointId = SnapshotData.OptionalId(intent.DeliveryPointId),
                    ItemId = SnapshotData.OptionalId(intent.ItemId), ItemGeneration = intent.ItemGeneration,
                    DeliveredAt = intent.DeliveredAt, ReceivedAt = intent.ReceivedAt, ResolutionAt = intent.ResolutionAt,
                    ResolutionReason = intent.ResolutionReason
                });
        }
    }

    internal static partial class SnapshotValidation
    {
        static void ServiceIntents(HotelModelSnapshot snapshot, IReadOnlyCollection<int> roomIds)
        {
            var layer = snapshot.ServiceLayer;
            var intents = Array(layer.Intents, snapshot.HasOperations ? 1024 : 0);
            Unique(intents.Select(intent => intent.Id));
            var guests = snapshot.Guests.ToDictionary(guest => guest.Application.Id);
            var cases = layer.Cases.ToDictionary(item => item.Id);
            var responses = layer.Responses.ToDictionary(item => item.Id);
            var incidents = snapshot.Incidents.ToDictionary(item => item.Id);
            var items = layer.Items.ToDictionary(item => item.Id);
            bool Active(ServiceIntentSnapshot intent) => intent.Status == ServiceIntentStatus.Active ||
                intent.Status == ServiceIntentStatus.AwaitingReceipt;
            Unique(intents.Where(intent => Active(intent) && intent.Kind == ServiceIntentKind.Direct).Select(intent => intent.GuestId));
            Unique(intents.Where(intent => intent.Status == ServiceIntentStatus.AwaitingReceipt).Select(intent => intent.ItemId));
            foreach (var intent in intents)
            {
                Text(intent.Id, 1024); Text(intent.GuestId); EnumValue(intent.Kind); EnumValue(intent.Purpose); EnumValue(intent.Status);
                Require(guests.TryGetValue(intent.GuestId, out var guest) && roomIds.Contains(intent.RoomId), "Intent has an unknown guest or room.");
                if (Active(intent)) Require(!guest.ReceiptPosted && guest.RoomId == intent.RoomId,
                    "Active intent no longer belongs to the current stay and room.");
                Require(intent.Revision > 0, "Invalid intent revision.");
                Require(intent.Status != ServiceIntentStatus.AwaitingReceipt || intent.Kind == ServiceIntentKind.DropOff,
                    "Only a physical drop-off can await receipt.");
                Range(intent.CreatedAt, 0, snapshot.Time);
                foreach (float time in new[] { intent.Deadline, intent.DeliveredAt, intent.ReceivedAt, intent.ResolutionAt }) OptionalHotelTime(time);
                Require(intent.Deadline < 0 || intent.Deadline >= intent.CreatedAt, "Intent deadline precedes its creation.");
                Require(intent.DeliveredAt < 0 || intent.DeliveredAt >= intent.CreatedAt && intent.DeliveredAt <= snapshot.Time,
                    "Invalid physical delivery time.");
                Require(intent.ReceivedAt < 0 || intent.DeliveredAt >= 0 && intent.ReceivedAt >= intent.DeliveredAt && intent.ReceivedAt <= snapshot.Time,
                    "Receipt precedes physical delivery.");
                Require(Active(intent) ? intent.ResolutionAt == -1 : intent.ResolutionAt >= intent.CreatedAt && intent.ResolutionAt <= snapshot.Time,
                    "Intent completion differs from its lifecycle.");
                Text(intent.ResolutionReason, 2048, true);
                OptionalId(intent.CaseId, 512); OptionalId(intent.ResponseId, 512); OptionalId(intent.IncidentId, 512);
                OptionalId(intent.DeliveryPointId, 256); OptionalId(intent.ItemId, 256);
                if (!string.IsNullOrEmpty(intent.CaseId)) Require(cases.TryGetValue(intent.CaseId, out var linkedCase) &&
                    linkedCase.GuestId == intent.GuestId, "Intent lost its agreement.");
                if (!string.IsNullOrEmpty(intent.ResponseId)) Require(responses.TryGetValue(intent.ResponseId, out var response) &&
                    response.GuestId == intent.GuestId, "Intent lost its response.");
                if (!string.IsNullOrEmpty(intent.IncidentId)) Require(incidents.TryGetValue(intent.IncidentId, out var incident) &&
                    incident.GuestId == intent.GuestId, "Intent lost its causal incident.");
                if (intent.Kind == ServiceIntentKind.Remote && !string.IsNullOrEmpty(intent.ResponseId))
                    Require(responses[intent.ResponseId].IncidentId == intent.IncidentId,
                        "Remote response belongs to another incident.");
                if (!string.IsNullOrEmpty(intent.CaseId))
                {
                    string responseId = cases[intent.CaseId].ResponseId;
                    string caseIncident = !string.IsNullOrEmpty(responseId) ? responses[responseId].IncidentId : null;
                    Require(SnapshotData.OptionalId(intent.IncidentId) == SnapshotData.OptionalId(caseIncident),
                        "Intent and its agreement refer to different causes.");
                }
                Require(intent.Purpose == ServiceIntentPurpose.Incident ? intent.Kind == ServiceIntentKind.Remote && !string.IsNullOrEmpty(intent.IncidentId) :
                    intent.Purpose == ServiceIntentPurpose.BlanketDelivery ? intent.Kind == ServiceIntentKind.DropOff &&
                        !string.IsNullOrEmpty(intent.CaseId) && cases[intent.CaseId].Kind == ServiceKind.ExtraBlanket :
                    intent.Kind == ServiceIntentKind.Direct, "Intent purpose and interaction type disagree.");
                if (intent.Purpose == ServiceIntentPurpose.ServiceDecision)
                {
                    Require(!string.IsNullOrEmpty(intent.CaseId) && cases.TryGetValue(intent.CaseId, out var decisionCase) &&
                        (decisionCase.Kind == ServiceKind.LateCheckout || decisionCase.Kind == ServiceKind.WakeUpCall ||
                        decisionCase.Kind == ServiceKind.LuggageStorage), "Direct decision has no corresponding agreement.");
                    if (Active(intent))
                    {
                        var decision = cases[intent.CaseId];
                        Require(decision.Status == ServiceStatus.Requested || decision.Status == ServiceStatus.Acknowledged,
                            "Direct decision is already recorded.");
                        if (layer.NaturalCommunicationEnabled)
                            Require(responses.TryGetValue(decision.ResponseId, out var contact) && contact.CommunicatedAt >= 0 &&
                                intent.ResponseId == contact.Id, "Direct agreement was not communicated.");
                    }
                }
                if (intent.Purpose == ServiceIntentPurpose.RoomMove)
                {
                    Require(string.IsNullOrEmpty(intent.CaseId) && string.IsNullOrEmpty(intent.IncidentId) &&
                        string.IsNullOrEmpty(intent.ResponseId), "Room-key exchange cannot impersonate a service agreement.");
                    if (Active(intent)) Require(guest.Agent != null && guest.Agent.PendingMoveRoomId > 0,
                        "Direct key exchange has no pending destination.");
                }
                if (intent.Purpose == ServiceIntentPurpose.CompensationDiscussion)
                {
                    Require(string.IsNullOrEmpty(intent.CaseId) && !string.IsNullOrEmpty(intent.IncidentId) &&
                        incidents.ContainsKey(intent.IncidentId),
                        "Compensation discussion has no reported causal incident.");
                    if (!string.IsNullOrEmpty(intent.ResponseId))
                        Require(responses[intent.ResponseId].IncidentId == intent.IncidentId &&
                            responses[intent.ResponseId].CommunicatedAt >= 0,
                            "Compensation discussion has a private or unrelated response.");
                    if (intent.Status == ServiceIntentStatus.Completed)
                        Require(guest.Compensated, "Completed compensation discussion has no reserved credit.");
                    if (Active(intent))
                    {
                        // A stable incident ID can open a new, still-private episode after a
                        // finished discussion. Only the current wait requires current disclosure;
                        // terminal history retains its causal ID and any retained heard response.
                        Require(incidents[intent.IncidentId].HasContactedStaff,
                            "Active compensation discussion was not communicated.");
                        var agent = guest.Agent;
                        bool roomState = agent != null && (agent.State == GuestAgentState.InRoom || agent.State == GuestAgentState.PerformingActivity);
                        Require(agent != null && agent.CheckedIn && agent.HasReachedRoom && !agent.IsRelocating &&
                            agent.Activity != GuestActivity.Shower && (roomState || agent.State == GuestAgentState.WaitingAtServiceReception) &&
                            snapshot.Rooms.Any(room => room.Id == guest.RoomId && room.GuestId == intent.GuestId) &&
                            intent.Deadline <= agent.CheckoutTime,
                            "Compensation discussion has no available physical guest.");
                        if (!string.IsNullOrEmpty(agent.ResponseActionId))
                            Require(agent.ResponseActionId == intent.ResponseId,
                                "Compensation discussion lost its current physical contact.");
                        // Recovery is processed before service intents in a simulation step.
                        // An inactive cause can therefore be valid briefly before the discussion closes.
                    }
                }
                if (intent.Kind == ServiceIntentKind.Direct)
                {
                    Require(intent.Deadline >= intent.CreatedAt && intent.Status != ServiceIntentStatus.AwaitingReceipt, "Direct interaction needs a finite wait.");
                    if (Active(intent)) Require(guest.Agent != null && guest.Agent.DirectServiceIntentId == intent.Id &&
                        !guest.ReceiptPosted, "Active direct intent lost its guest scheduling owner.");
                }
                if (intent.Kind != ServiceIntentKind.DropOff)
                    Require(string.IsNullOrEmpty(intent.ItemId) && string.IsNullOrEmpty(intent.DeliveryPointId) && intent.ItemGeneration == -1 &&
                        intent.DeliveredAt == -1 && intent.ReceivedAt == -1, "Only a drop-off intent can own a parcel.");
                else
                {
                    Require(intent.DeliveryPointId == "room/" + intent.RoomId + "/blanket-drop", "Unknown blanket delivery point.");
                    if (intent.Status == ServiceIntentStatus.Active) Require(string.IsNullOrEmpty(intent.ItemId) &&
                        intent.ItemGeneration == -1 && intent.DeliveredAt == -1 && intent.ReceivedAt == -1,
                        "An unplaced delivery already contains a physical receipt.");
                    if (intent.Status == ServiceIntentStatus.Completed) Require(intent.ReceivedAt >= 0 &&
                        !string.IsNullOrEmpty(intent.ItemId), "Completed blanket delivery has no physical receipt.");
                    if (Active(intent))
                    {
                        var agreement = cases[intent.CaseId];
                        Require(agreement.Status == ServiceStatus.Requested || agreement.Status == ServiceStatus.Acknowledged ||
                            agreement.Status == ServiceStatus.InProgress, "Active drop-off refers to an ended agreement.");
                        if (layer.NaturalCommunicationEnabled) Require(responses.TryGetValue(agreement.ResponseId, out var contact) &&
                            contact.CommunicatedAt >= 0 && intent.ResponseId == contact.Id, "Drop-off was not communicated.");
                    }
                    if (string.IsNullOrEmpty(intent.ItemId)) Require(intent.ItemGeneration == -1 && intent.DeliveredAt == -1 && intent.ReceivedAt == -1 &&
                        intent.Status != ServiceIntentStatus.AwaitingReceipt, "Delivery has no physical item.");
                    else Require(items.TryGetValue(intent.ItemId, out var parcel) && parcel.Kind == ServiceItemKind.Blanket &&
                        intent.ItemGeneration >= 0 && intent.DeliveredAt >= 0, "Delivery does not refer to a real blanket slot.");
                    if (intent.Status == ServiceIntentStatus.AwaitingReceipt)
                    {
                        var pendingParcel = items[intent.ItemId];
                        Require(pendingParcel.Location == ServiceItemLocation.AwaitingReceipt && pendingParcel.PlayerId == -1 && pendingParcel.GuestId == intent.GuestId &&
                            pendingParcel.RoomId == intent.RoomId && pendingParcel.Generation == intent.ItemGeneration && intent.ReceivedAt == -1 &&
                            !guest.ReceiptPosted && guest.RoomId == intent.RoomId && guest.Memory.BlanketsDelivered == 0 &&
                            cases[intent.CaseId].Status == ServiceStatus.InProgress,
                            "Pending parcel lost its actual item, recipient, agreement or room, or gave comfort before receipt.");
                    }
                }
            }
            foreach (var item in layer.Items.Where(item => item.Location == ServiceItemLocation.AwaitingReceipt))
                Require(intents.Count(intent => intent.Status == ServiceIntentStatus.AwaitingReceipt && intent.ItemId == item.Id) == 1,
                    "An unreceived parcel needs exactly one live delivery intent.");
            foreach (var guest in snapshot.Guests)
            {
                string id = guest.Agent?.DirectServiceIntentId;
                OptionalId(id, 1024);
                if (!string.IsNullOrEmpty(id)) Require(intents.Any(intent => intent.Id == id && intent.GuestId == guest.Application.Id &&
                    intent.Kind == ServiceIntentKind.Direct && Active(intent)), "Guest is waiting for a missing or completed direct intent.");
            }
        }
    }
}
