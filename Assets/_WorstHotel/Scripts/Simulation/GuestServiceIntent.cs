namespace WorstHotel
{
    public enum ServiceIntentKind { Remote, DropOff, Direct }
    public enum ServiceIntentPurpose { Incident, BlanketDelivery, ServiceDecision, RoomMove, CompensationDiscussion = 4 }
    public enum ServiceIntentStatus { Active, AwaitingReceipt, Completed, Cancelled, TimedOut }

    /// <summary>One authoritative continuation of an existing concern or agreement, not a second request generator.</summary>
    public sealed class GuestServiceIntent
    {
        public string Id { get; }
        public string GuestId { get; }
        public int RoomId { get; internal set; }
        public ServiceIntentKind Kind { get; }
        public ServiceIntentPurpose Purpose { get; }
        public ServiceIntentStatus Status { get; internal set; } = ServiceIntentStatus.Active;
        public int Revision { get; internal set; } = 1;
        public float CreatedAt { get; }
        public float Deadline { get; internal set; } = -1;
        public string CaseId { get; internal set; }
        public string ResponseId { get; internal set; }
        public string IncidentId { get; internal set; }
        public string DeliveryPointId { get; internal set; }
        public string ItemId { get; internal set; }
        public int ItemGeneration { get; internal set; } = -1;
        public float DeliveredAt { get; internal set; } = -1;
        public float ReceivedAt { get; internal set; } = -1;
        public float ResolutionAt { get; internal set; } = -1;
        public string ResolutionReason { get; internal set; }
        public bool Active => Status == ServiceIntentStatus.Active || Status == ServiceIntentStatus.AwaitingReceipt;

        internal GuestServiceIntent(string id, string guestId, int roomId, ServiceIntentKind kind,
            ServiceIntentPurpose purpose, float createdAt)
        { Id = id; GuestId = guestId; RoomId = roomId; Kind = kind; Purpose = purpose; CreatedAt = createdAt; }
    }
}
