namespace WorstHotel
{
    public enum HotelAdvanceMode { None = 0, Wait = 1, Sleep = 2 }
    public enum StaffWakeReason
    {
        None = 0, Morning = 1, StaffCancelled = 2, BoilerFailure = 3, CircuitTrip = 4,
        CriticalGuest = 5, Paused = 6, DeviceUnavailable = 7, MenuOpened = 8,
        InputExpired = 9, SessionChanged = 10, Stopped = 11
    }

    public readonly struct StaffSleepConsent
    {
        public int ActorId { get; }
        public int BedId { get; }
        public bool Ready { get; }
        public StaffSleepConsent(int actorId, int bedId, bool ready)
        { ActorId = actorId; BedId = bedId; Ready = ready; }
    }

    /// <summary>A bounded, immutable presentation of host-owned time advancement.</summary>
    public readonly struct StaffSleepView
    {
        public HotelAdvanceMode Mode { get; }
        public long Revision { get; }
        public float Until { get; }
        public StaffWakeReason WakeReason { get; }
        public StaffSleepConsent Staff0 { get; }
        public StaffSleepConsent Staff1 { get; }
        public StaffSleepView(HotelAdvanceMode mode, long revision, float until, StaffWakeReason wakeReason,
            StaffSleepConsent staff0, StaffSleepConsent staff1)
        { Mode = mode; Revision = revision; Until = until; WakeReason = wakeReason; Staff0 = staff0; Staff1 = staff1; }
    }
}
