namespace WorstHotel
{
    public enum EarlyCheckoutState { None, Monitoring, Warning, Committed }

    /// <summary>One factual episode, not a replacement for the contracted checkout schedule.</summary>
    public sealed class GuestEarlyCheckout
    {
        public EarlyCheckoutState State { get; internal set; }
        public string IncidentId { get; internal set; }
        public int IncidentEpisode { get; internal set; }
        public int RoomId { get; internal set; }
        public IncidentReason Reason { get; internal set; }
        public float SevereExposureSeconds { get; internal set; }
        public float RecoverySeconds { get; internal set; }
        public float WarningAt { get; internal set; } = -1;
        public float GraceRemainingSeconds { get; internal set; }
        public float CommittedAt { get; internal set; } = -1;
        public string CauseDescription { get; internal set; }

        internal void Reset()
        {
            State = EarlyCheckoutState.None; IncidentId = CauseDescription = null;
            IncidentEpisode = RoomId = 0; Reason = default;
            SevereExposureSeconds = RecoverySeconds = GraceRemainingSeconds = 0;
            WarningAt = CommittedAt = -1;
        }
    }
}
