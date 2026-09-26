namespace WorstHotel
{
    public enum LinenKind { Dirty, Clean }
    public enum LinenLocation { OnBed, OnShelf, HeldByPlayer, Dropped, InHamper, Consumed }

    /// <summary>A stable physical slot. Generation changes when that slot is reused, never when it changes hands.</summary>
    public sealed class LinenBundleState
    {
        public string Id { get; }
        public LinenKind Kind { get; }
        public LinenLocation Location { get; internal set; } = LinenLocation.Consumed;
        public int? PlayerId { get; internal set; }
        public int? SourceRoomId { get; }
        public int Generation { get; internal set; }
        public int ShelfSlotIndex { get; }
        internal LinenBundleState(string id, LinenKind kind, int? sourceRoomId, int shelfSlotIndex)
        { Id = id; Kind = kind; SourceRoomId = sourceRoomId; ShelfSlotIndex = shelfSlotIndex; }
    }
}
