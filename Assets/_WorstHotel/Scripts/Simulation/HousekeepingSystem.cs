using System;
using System.Collections.Generic;
using System.Linq;

namespace WorstHotel
{
    /// <summary>Manual turnover and finite linen ownership. Physical adapters authenticate reach, carrying and real hold time.</summary>
    public sealed partial class HousekeepingSystem
    {
        public HousekeepingSettings Settings { get; }
        public IReadOnlyList<HousekeepingTask> Tasks { get; }
        public IReadOnlyList<LinenBundleState> Linens { get; }
        public HousekeepingTask CurrentTask => ordered.FirstOrDefault(task => task.Step == RoomPreparationStep.MakingBed);
        public bool HasPendingWork => ordered.Count > 0;
        public int LastRefillDay { get; private set; }
        // Kept only so an old scene's worker cannot regain automatic cleaning authority.
        public bool WorkerAvailable => false;
        public event Action<HousekeepingTask, string> Changed;
        public event Action<LinenBundleState, string> LinenChanged;
        private readonly Dictionary<int, RoomState> rooms;
        private readonly Dictionary<int, HousekeepingTask> tasks = new Dictionary<int, HousekeepingTask>();
        private readonly Dictionary<string, LinenBundleState> linens;
        private readonly List<HousekeepingTask> ordered = new List<HousekeepingTask>();
        private long queuedSequence;

        public HousekeepingSystem(HousekeepingSettings settings, IEnumerable<RoomState> roomStates)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            if (roomStates == null) throw new ArgumentNullException(nameof(roomStates));
            var states = roomStates.ToArray();
            if (states.Length == 0 || states.Any(room => room == null) || states.Select(room => room.Profile.Id).Distinct().Count() != states.Length)
                throw new ArgumentException("Turnover needs unique real room states.");
            rooms = states.OrderBy(room => room.Profile.Id).ToDictionary(room => room.Profile.Id);
            var items = new List<LinenBundleState>();
            for (int slot = 0; slot < settings.CleanLinenPerDay; slot++)
                items.Add(new LinenBundleState("clean:" + slot, LinenKind.Clean, null, slot));
            foreach (var room in rooms.Values)
                items.Add(new LinenBundleState("dirty:" + room.Profile.Id, LinenKind.Dirty, room.Profile.Id, -1));
            Linens = items.AsReadOnly(); linens = items.ToDictionary(item => item.Id);
            Tasks = ordered.AsReadOnly();
            RefillForDay(1);
            SynchronizeTasks();
        }

        public HousekeepingTask Find(int roomId) => tasks.TryGetValue(roomId, out var task) ? task : null;
        public LinenBundleState FindLinen(string id) => id != null && linens.TryGetValue(id, out var linen) ? linen : null;
        public void SetWorkerAvailable(bool available) {
            if (ReadOnlyMirror) return; }
        public CommandResult PrioritizeRoom(int actorId, int roomId) => CommandResult.Fail("Players choose which room to prepare by carrying its linen; there is no automatic worker queue.");
        public CommandResult SignalReachedRoom(int roomId) => CommandResult.Fail("Reaching a room does not clean it. Remove dirty linen, deliver it to the hamper, then bring a clean set to the bed.");

        internal void MarkDirty(int roomId)
        {
            var room = rooms[roomId];
            room.Cleanliness = Cleanliness.Dirty;
            EnsureTask(room);
        }

        public CommandResult PickUpLinen(int playerId, string id)
        {
            if (ReadOnlyMirror) return CommandResult.Fail(HotelSimulation.MirrorMessage);
            if (playerId < 0) return CommandResult.Fail("Unknown player identity.");
            var linen = FindLinen(id);
            if (linen == null) return CommandResult.Fail("Unknown linen bundle.");
            if (linen.Location != LinenLocation.OnBed && linen.Location != LinenLocation.OnShelf && linen.Location != LinenLocation.Dropped)
                return CommandResult.Fail("This linen is not available to pick up.");
            if (Linens.Any(item => item.Location == LinenLocation.HeldByPlayer && item.PlayerId == playerId))
                return CommandResult.Fail("Put down your current bundle before taking another.");
            HousekeepingTask task = null;
            if (linen.Kind == LinenKind.Dirty)
            {
                task = Find(linen.SourceRoomId.Value);
                if (task == null || task.Generation != linen.Generation || task.DirtyLinenId != id || !Eligible(rooms[task.RoomId]) ||
                    (task.Step != RoomPreparationStep.DirtyLinenOnBed && task.Step != RoomPreparationStep.DeliverDirtyLinen))
                    return CommandResult.Fail("Dirty linen can be removed only from its current, physically vacant turnover room.");
            }
            linen.Location = LinenLocation.HeldByPlayer; linen.PlayerId = playerId;
            if (task != null) task.Step = RoomPreparationStep.DeliverDirtyLinen;
            if (task != null) Changed?.Invoke(task, "dirty linen removed; carry it to the hamper");
            LinenChanged?.Invoke(linen, "picked up by player " + playerId);
            return CommandResult.Ok(linen.Kind == LinenKind.Dirty ? "Carry this dirty bundle to the hamper." : "Carry this clean set to a stripped bed.");
        }

        public CommandResult DropLinen(int playerId, string id)
        {
            if (ReadOnlyMirror) return CommandResult.Fail(HotelSimulation.MirrorMessage);
            var linen = FindLinen(id);
            if (!HeldBy(linen, playerId)) return CommandResult.Fail("This player is not carrying that linen bundle.");
            var task = ordered.FirstOrDefault(candidate => candidate.WorkingPlayerId == playerId && candidate.CleanLinenId == id);
            linen.Location = LinenLocation.Dropped; linen.PlayerId = null;
            if (task != null) ResetWork(task);
            if (task != null) Changed?.Invoke(task, "bed-making cancelled because the clean bundle was released");
            LinenChanged?.Invoke(linen, "dropped in the hotel");
            return CommandResult.Ok("Linen dropped; it can be picked up again.");
        }

        public CommandResult DepositDirtyLinen(int playerId, string id)
        {
            if (ReadOnlyMirror) return CommandResult.Fail(HotelSimulation.MirrorMessage);
            var linen = FindLinen(id);
            if (!HeldBy(linen, playerId) || linen.Kind != LinenKind.Dirty)
                return CommandResult.Fail("Bring a dirty bundle you are carrying to the hamper.");
            var task = Find(linen.SourceRoomId.Value);
            if (task == null || task.Generation != linen.Generation || task.DirtyLinenId != id ||
                task.Step != RoomPreparationStep.DeliverDirtyLinen || !Eligible(rooms[task.RoomId]))
                return CommandResult.Fail("This bundle does not belong to an available room's current turnover.");
            linen.Location = LinenLocation.InHamper; linen.PlayerId = null;
            task.Step = RoomPreparationStep.NeedsCleanLinen;
            Changed?.Invoke(task, "dirty linen deposited; the bed needs a clean set");
            LinenChanged?.Invoke(linen, "deposited in the dirty-linen hamper");
            return CommandResult.Ok("Dirty linen deposited. Fetch a clean set from the shelf.");
        }

        public CommandResult BeginMakeBed(int playerId, int roomId, string cleanId)
        {
            if (ReadOnlyMirror) return CommandResult.Fail(HotelSimulation.MirrorMessage);
            var linen = FindLinen(cleanId);
            if (!HeldBy(linen, playerId) || linen.Kind != LinenKind.Clean)
                return CommandResult.Fail("Carry an unused clean set to this bed.");
            var task = Find(roomId);
            if (task == null || !Eligible(rooms[roomId])) return CommandResult.Fail("The room must be dirty and physically vacant.");
            if (task.Step == RoomPreparationStep.MakingBed && task.WorkingPlayerId == playerId && task.CleanLinenId == cleanId)
                return CommandResult.Ok("Continue making this bed.");
            if (task.Step != RoomPreparationStep.NeedsCleanLinen)
                return CommandResult.Fail(task.Step == RoomPreparationStep.MakingBed ? "Another player is making this bed." : "Deposit this room's dirty linen in the hamper first.");
            if (ordered.Any(candidate => candidate.WorkingPlayerId == playerId))
                return CommandResult.Fail("Finish or cancel your current bed before starting another.");
            var dirty = FindLinen(task.DirtyLinenId);
            if (dirty.Generation != task.Generation || dirty.Location != LinenLocation.InHamper)
                return CommandResult.Fail("This room's current dirty bundle has not been deposited.");
            task.WorkingPlayerId = playerId; task.CleanLinenId = cleanId; task.CleanLinenGeneration = linen.Generation;
            task.ProgressSeconds = 0; task.Step = RoomPreparationStep.MakingBed;
            task.State = rooms[roomId].TurnoverState = HousekeepingState.Cleaning;
            Changed?.Invoke(task, "player " + playerId + " started making the bed");
            return CommandResult.Ok("Keep holding the bed interaction to fit the clean linen.");
        }

        public CommandResult AdvanceMakeBed(int playerId, int roomId, float realDt)
        {
            if (ReadOnlyMirror) return CommandResult.Fail(HotelSimulation.MirrorMessage);
            if (!Number.IsFinite(realDt) || realDt < 0) return CommandResult.Fail("Bed-making time must be finite and nonnegative.");
            var task = Find(roomId);
            if (playerId < 0 || task == null || task.Step != RoomPreparationStep.MakingBed || task.WorkingPlayerId != playerId)
                return CommandResult.Fail("This player has no active bed-making interaction here.");
            if (!ValidWork(task))
            {
                ResetWork(task);
                Changed?.Invoke(task, "bed-making cancelled because the room or carried clean set is no longer available");
                return CommandResult.Fail("Bed-making interrupted; the clean set was not consumed.");
            }
            task.ProgressSeconds = (float)Math.Min(task.RequiredSeconds, (double)task.ProgressSeconds + realDt);
            if (task.ProgressSeconds < task.RequiredSeconds) return CommandResult.Ok("Making the bed.");
            var linen = FindLinen(task.CleanLinenId);
            linen.Location = LinenLocation.Consumed; linen.PlayerId = null;
            task.WorkingPlayerId = null; task.Step = RoomPreparationStep.Ready; task.State = HousekeepingState.None;
            var room = rooms[roomId]; room.Cleanliness = Cleanliness.Clean; room.TurnoverState = HousekeepingState.None;
            tasks.Remove(roomId); ordered.Remove(task);
            Changed?.Invoke(task, "clean linen fitted; room ready for key handoff");
            LinenChanged?.Invoke(linen, "fitted to room " + roomId + " bed");
            return CommandResult.Ok("Room " + roomId + " is ready for its next guest.");
        }

        public CommandResult CancelMakeBed(int playerId, int roomId)
        {
            if (ReadOnlyMirror) return CommandResult.Fail(HotelSimulation.MirrorMessage);
            var task = Find(roomId);
            if (playerId < 0 || task == null || task.Step != RoomPreparationStep.MakingBed || task.WorkingPlayerId != playerId)
                return CommandResult.Fail("This player has no active bed-making interaction here.");
            ResetWork(task);
            Changed?.Invoke(task, "bed-making cancelled; the clean set remains with its player");
            return CommandResult.Ok("Bed-making cancelled without consuming linen.");
        }

        /// <summary>Host lifecycle hook. Reuses consumed shelf slots; carried or dropped clean sets never duplicate.</summary>
        public CommandResult RefillForDay(int day)
        {
            if (ReadOnlyMirror) return CommandResult.Fail(HotelSimulation.MirrorMessage);
            if (day < 1 || day < LastRefillDay) return CommandResult.Fail("A linen refill needs the current or a later positive planning day.");
            if (day == LastRefillDay) return CommandResult.Ok("This day's linen stock has already been replenished.");
            var replenished = Linens.Where(item => item.Kind == LinenKind.Clean && item.Location == LinenLocation.Consumed).ToArray();
            foreach (var linen in replenished)
            {
                linen.Location = LinenLocation.OnShelf; linen.PlayerId = null; linen.Generation++;
            }
            LastRefillDay = day;
            foreach (var linen in replenished) LinenChanged?.Invoke(linen, "clean shelf slot replenished for day " + day);
            return CommandResult.Ok("Clean linen replenished for day " + day + "; held and dropped sets were preserved.");
        }

        /// <summary>Hotel time can update vacancy guards, but only authenticated real-time AdvanceMakeBed completes work.</summary>
        public void Tick(float dt)
        {
            if (ReadOnlyMirror) return;
            if (!Number.IsFinite(dt) || dt < 0) throw new ArgumentOutOfRangeException(nameof(dt));
            SynchronizeTasks();
            foreach (var task in ordered.ToArray())
                if (task.Step == RoomPreparationStep.MakingBed && !ValidWork(task))
                {
                    ResetWork(task);
                    Changed?.Invoke(task, "bed-making cancelled because the room or carried clean set is no longer available");
                }
        }

        private static bool Eligible(RoomState room) => room.Cleanliness == Cleanliness.Dirty && !room.Occupied &&
            string.IsNullOrEmpty(room.DepartingGuestId);
        private static bool HeldBy(LinenBundleState linen, int playerId) => playerId >= 0 && linen != null &&
            linen.Location == LinenLocation.HeldByPlayer && linen.PlayerId == playerId;
        private bool ValidWork(HousekeepingTask task)
        {
            var clean = FindLinen(task.CleanLinenId);
            return task.WorkingPlayerId.HasValue && HeldBy(clean, task.WorkingPlayerId.Value) &&
                clean.Kind == LinenKind.Clean && clean.Generation == task.CleanLinenGeneration && Eligible(rooms[task.RoomId]);
        }
        private void ResetWork(HousekeepingTask task)
        {
            task.WorkingPlayerId = null; task.CleanLinenId = null; task.CleanLinenGeneration = 0;
            task.ProgressSeconds = 0; task.Step = RoomPreparationStep.NeedsCleanLinen;
            task.State = rooms[task.RoomId].TurnoverState = HousekeepingState.Queued;
        }
        private void EnsureTask(RoomState room)
        {
            if (tasks.ContainsKey(room.Profile.Id)) return;
            var dirty = FindLinen("dirty:" + room.Profile.Id);
            dirty.Generation++; dirty.Location = LinenLocation.OnBed; dirty.PlayerId = null;
            var task = new HousekeepingTask(room.Profile.Id, dirty.Generation, dirty.Id, Settings.MakeBedSeconds, ++queuedSequence);
            tasks.Add(task.RoomId, task); ordered.Add(task); room.TurnoverState = HousekeepingState.Queued;
            Changed?.Invoke(task, "used bed needs its dirty linen removed");
            LinenChanged?.Invoke(dirty, "dirty linen left on the used bed");
        }
        private void SynchronizeTasks()
        {
            foreach (var task in ordered.ToArray())
                if (rooms[task.RoomId].Cleanliness != Cleanliness.Dirty)
                {
                    // Legacy public room snapshots can be edited by diagnostics. Never complete or consume linen from such an edit.
                    ResetWork(task); task.State = rooms[task.RoomId].TurnoverState = HousekeepingState.None;
                    tasks.Remove(task.RoomId); ordered.Remove(task);
                    Changed?.Invoke(task, "turnover cancelled because the room was externally marked clean");
                }
            foreach (var room in rooms.Values)
                if (room.Cleanliness == Cleanliness.Dirty) EnsureTask(room);
                else if (!tasks.ContainsKey(room.Profile.Id)) room.TurnoverState = HousekeepingState.None;
        }
    }
}


