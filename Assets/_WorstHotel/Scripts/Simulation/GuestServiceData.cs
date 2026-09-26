namespace WorstHotel
{
    public enum ServiceKind { ExtraBlanket, LuggageStorage, LateCheckout, WakeUpCall, AskNeighborsQuiet }
    public enum ServiceStatus { Requested, Acknowledged, InProgress, Fulfilled, Declined, Expired, Escalated }
    public enum PromiseStatus { Accepted, Completed, Missed, Cancelled }
    public enum ServiceItemKind { Blanket, ReplacementBulb, Luggage }
    public enum ServiceItemLocation { OnShelf, HeldByPlayer, Dropped, Delivered, Stored }

    public sealed class ServiceCase
    {
        public string Id { get; }
        public string GuestId { get; }
        public int RoomId { get; internal set; }
        public ServiceKind Kind { get; }
        public ServiceStatus Status { get; internal set; } = ServiceStatus.Requested;
        public float CreatedAt { get; }
        public float DueTime { get; internal set; }
        public string SourceEntityId { get; }
        public int SourceRoomId { get; internal set; }
        public string Description { get; }
        public GuestResponse Response { get; internal set; }
        public bool IsKnownToHotel => Response == null || Response.CommunicatedAt >= 0;
        public ServiceCommunicationState CommunicationState => Response?.CommunicationState ?? ServiceCommunicationState.Communicated;
        public bool BudgetCharged { get; internal set; }
        public float ResolutionAt { get; internal set; } = -1;
        public string ResolutionReason { get; internal set; }
        public bool Active => Status == ServiceStatus.Requested || Status == ServiceStatus.Acknowledged || Status == ServiceStatus.InProgress;
        internal float RecoverySeconds;
        internal ServiceCase(string id, string guest, int room, ServiceKind kind, float created, float due,
            string source, int sourceRoom, string description)
        { Id = id; GuestId = guest; RoomId = room; Kind = kind; CreatedAt = created; DueTime = due;
            SourceEntityId = source; SourceRoomId = sourceRoom; Description = description; }
    }

    /// <summary>The only supported promise type: a call at a hotel simulation time, within this stay.</summary>
    public sealed class PromiseWakeUp
    {
        public string Id { get; }
        public string GuestId { get; }
        public int RoomId { get; internal set; }
        public float DueTime { get; }
        public PromiseStatus Status { get; internal set; } = PromiseStatus.Accepted;
        public bool DueNotified { get; internal set; }
        public float CompletedAt { get; internal set; } = -1;
        internal PromiseWakeUp(string id, string guest, int room, float due)
        { Id = id; GuestId = guest; RoomId = room; DueTime = due; }
    }

    /// <summary>Stable physical identity. Taking changes ownership; it never creates a new item.</summary>
    public sealed class ServiceItemState
    {
        public string Id { get; }
        public ServiceItemKind Kind { get; }
        public ServiceItemLocation Location { get; internal set; } = ServiceItemLocation.OnShelf;
        public int? PlayerId { get; internal set; }
        public int? LastPlayerId { get; internal set; }
        public string GuestId { get; internal set; }
        public int? RoomId { get; internal set; }
        public int Generation { get; internal set; }
        internal ServiceItemState(string id, ServiceItemKind kind, int generation, string guest = null)
        { Id = id; Kind = kind; Generation = generation; GuestId = guest; }
    }
}
