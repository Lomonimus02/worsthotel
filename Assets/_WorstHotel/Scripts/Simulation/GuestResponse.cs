namespace WorstHotel
{
    public enum GuestResponsePhase { Noticed, SelfResponding, Tolerating, WaitingToContact, Contacting, Communicated, Cancelled }
    public enum GuestContactChannel { None, Phone, Reception, RoomConversation }
    public enum GuestResponseAnchor { Radiator, RoomPhone, Reception, AssignedRoom }
    public enum ServiceCommunicationState { Uncommunicated, ContactingHotel, Communicated }

    /// <summary>One bounded response journey shared by a factual incident episode and its optional service agreement.</summary>
    public sealed class GuestResponse
    {
        public string Id { get; }
        public string GuestId { get; }
        public int RoomId { get; internal set; }
        public string SourceEntityId { get; }
        public string IncidentId { get; }
        public int IncidentEpisode { get; }
        public string ServiceCaseId { get; internal set; }
        public GuestResponsePhase Phase { get; internal set; }
        public GuestContactChannel Channel { get; internal set; }
        public float CreatedAt { get; }
        public float PhaseStartedAt { get; internal set; }
        public float DwellSeconds { get; internal set; }
        public bool SelfResponseAttempted { get; internal set; }
        public bool SelfResponseApplied { get; internal set; }
        public float SelfResponseAt { get; internal set; } = -1;
        public int ContactAttempts { get; internal set; }
        public float AttemptStartedAt { get; internal set; } = -1;
        public float AttemptDeadline { get; internal set; } = -1;
        public float RetryAt { get; internal set; } = -1;
        public float CommunicatedAt { get; internal set; } = -1;
        public float AcknowledgedAt { get; internal set; } = -1;
        public float StaffActionAt { get; internal set; } = -1;
        public int ActionVersion { get; internal set; }
        public ServiceCommunicationState CommunicationState => CommunicatedAt >= 0 ? ServiceCommunicationState.Communicated :
            Phase == GuestResponsePhase.Contacting ? ServiceCommunicationState.ContactingHotel : ServiceCommunicationState.Uncommunicated;
        public bool IsRinging => Channel == GuestContactChannel.Phone && Phase == GuestResponsePhase.Contacting && AttemptStartedAt >= 0;
        internal GuestResponse(string id, string guestId, int roomId, string sourceEntityId, string incidentId,
            int incidentEpisode, string serviceCaseId, float createdAt)
        {
            Id = id; GuestId = guestId; RoomId = roomId; SourceEntityId = sourceEntityId;
            IncidentId = incidentId; IncidentEpisode = incidentEpisode; ServiceCaseId = serviceCaseId;
            CreatedAt = PhaseStartedAt = createdAt;
        }
    }
}
