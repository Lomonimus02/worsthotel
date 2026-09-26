namespace WorstHotel
{
    public enum HousekeepingState { None, Queued, Moving, Cleaning }
    public enum RoomPreparationStep { DirtyLinenOnBed, DeliverDirtyLinen, NeedsCleanLinen, MakingBed, Ready }

    public sealed class HousekeepingTask
    {
        public int RoomId { get; }
        public int Generation { get; }
        public RoomPreparationStep Step { get; internal set; } = RoomPreparationStep.DirtyLinenOnBed;
        public string DirtyLinenId { get; }
        public string CleanLinenId { get; internal set; }
        public int? WorkingPlayerId { get; internal set; }
        public HousekeepingState State { get; internal set; } = HousekeepingState.Queued;
        public float ProgressSeconds { get; internal set; }
        public float Progress01 => Number.Clamp(ProgressSeconds / RequiredSeconds, 0, 1);
        public float RequiredSeconds { get; }
        public long QueuedOrder { get; }
        public int PrioritizedByActorId => -1;
        internal int CleanLinenGeneration { get; set; }
        internal HousekeepingTask(int roomId, int generation, string dirtyLinenId, float requiredSeconds, long queuedOrder)
        {
            RoomId = roomId; Generation = generation; DirtyLinenId = dirtyLinenId;
            RequiredSeconds = requiredSeconds; QueuedOrder = queuedOrder;
        }
    }
}
