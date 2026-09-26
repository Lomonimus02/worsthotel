namespace WorstHotel
{
    public readonly struct GuestNeedSnapshot
    {
        public float Severity { get; }
        /// <summary>Lifetime real seconds exposed, never reset on recovery. An episode records its own baseline.</summary>
        public float ExposureSeconds { get; }
        public float Dissatisfaction { get; }
        internal GuestNeedSnapshot(float severity, float exposureSeconds, float dissatisfaction)
        { Severity = severity; ExposureSeconds = exposureSeconds; Dissatisfaction = dissatisfaction; }
    }

    public sealed class GuestNeeds
    {
        public GuestNeedSnapshot Temperature { get; internal set; }
        public GuestNeedSnapshot Noise { get; internal set; }
        public GuestNeedSnapshot RoomCondition { get; internal set; }
        public GuestNeedSnapshot Service { get; internal set; }
        public float CombinedRoomDeficit { get; internal set; }
        public float ServiceIntegral { get; internal set; }
        public bool ExpiredRoomComplaint { get; internal set; }
    }
}
