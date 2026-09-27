using System;
using System.Linq;
using NUnit.Framework;

namespace WorstHotel.Tests
{
    /// <summary>Explicit headless player intentions for scenarios that test hotel state, not physical travel.</summary>
    internal static class ManualTurnoverModelAdapter
    {
        internal static void Require(CommandResult result) => Assert.That(result.Success, Is.True, result.Message);

        internal static string StripAndDeposit(HotelSimulation simulation, int roomId, int playerId)
        {
            var task = simulation.Housekeeping.Find(roomId);
            Assert.That(task, Is.Not.Null);
            Require(simulation.PickUpLinen(playerId, task.DirtyLinenId));
            Require(simulation.DepositDirtyLinen(playerId, task.DirtyLinenId));
            return task.DirtyLinenId;
        }

        internal static string TakeClean(HotelSimulation simulation, int playerId)
        {
            var clean = simulation.Housekeeping.Linens.First(item => item.Kind == LinenKind.Clean && item.Location == LinenLocation.OnShelf);
            Require(simulation.PickUpLinen(playerId, clean.Id));
            return clean.Id;
        }

        internal static void FinishBed(HotelSimulation simulation, int roomId, int playerId, string cleanId)
        {
            Require(simulation.BeginMakeBed(playerId, roomId, cleanId));
            var task = simulation.Housekeeping.Find(roomId);
            // Authenticated explicit action samples. AdvancePreparation/Tick cannot supply these.
            for (int sample = 0; sample < 100 && simulation.Housekeeping.Find(roomId) != null; sample++)
                Require(simulation.AdvanceMakeBed(playerId, roomId, .1f));
            Assert.That(task.Step, Is.EqualTo(RoomPreparationStep.Ready));
            Assert.That(simulation.Housekeeping.Find(roomId), Is.Null);
        }

        internal static void PrepareRoom(HotelSimulation simulation, int roomId, int playerId = 0)
        {
            StripAndDeposit(simulation, roomId, playerId);
            FinishBed(simulation, roomId, playerId, TakeClean(simulation, playerId));
        }
    }

    /// <summary>Declared 1x player-route adapter for economic traces. It occupies one of the same
    /// staff slots used by check-in, executes every item command, and never creates a virtual NPC cleaner.</summary>
    internal sealed class TimedManualTurnoverAdapter
    {
        readonly HotelSimulation simulation;
        readonly RoomState[] rooms;
        readonly string[] staff;
        readonly Action<int, int> prepareRoomFixtures;
        readonly float fixturePreparationSeconds;
        HousekeepingTask task;
        int actor = -1, phase;
        float due, lastAction, previousDoor = 1.3f;
        string cleanId;
        const float StaffSpeed = 3.5f, LinenStorageZ = 30.65f;

        internal TimedManualTurnoverAdapter(HotelSimulation simulation, RoomState[] rooms, string[] staff,
            Action<int, int> prepareRoomFixtures = null, float fixturePreparationSeconds = 0)
        {
            this.simulation = simulation; this.rooms = rooms; this.staff = staff;
            this.prepareRoomFixtures = prepareRoomFixtures; this.fixturePreparationSeconds = fixturePreparationSeconds;
        }

        internal void Tick()
        {
            float now = simulation.Elapsed;
            if (task == null)
            {
                actor = Array.FindIndex(staff, slot => slot == null);
                if (actor < 0) return;
                task = simulation.Housekeeping.Tasks.FirstOrDefault(item => item.Step == RoomPreparationStep.DirtyLinenOnBed &&
                    rooms.Any(room => room.Profile.Id == item.RoomId && !room.Occupied && room.DepartingGuestId == null));
                if (task == null) return;
                staff[actor] = "manual-linen:" + task.RoomId;
                phase = 0;
                due = now + (Math.Abs(DoorZ(task.RoomId) - previousDoor) + 5.8f) / StaffSpeed + .8f;
            }
            if (now < due) return;
            float storageTrip = (Math.Abs(LinenStorageZ - DoorZ(task.RoomId)) + 9.9f) / StaffSpeed + .8f;
            switch (phase)
            {
                case 0:
                    ManualTurnoverModelAdapter.Require(simulation.PickUpLinen(actor, task.DirtyLinenId));
                    phase = 1; due = now + storageTrip;
                    break;
                case 1:
                    ManualTurnoverModelAdapter.Require(simulation.DepositDirtyLinen(actor, task.DirtyLinenId));
                    phase = 2; due = now + 1.8f / StaffSpeed + .5f;
                    break;
                case 2:
                    // Finite stock may be exhausted until the authorized next-day refill. The
                    // player remains occupied; this adapter cannot invent another clean bundle.
                    if (!simulation.Housekeeping.Linens.Any(item => item.Kind == LinenKind.Clean && item.Location == LinenLocation.OnShelf)) return;
                    cleanId = ManualTurnoverModelAdapter.TakeClean(simulation, actor);
                    phase = 3; due = now + storageTrip;
                    break;
                case 3:
                    if (prepareRoomFixtures != null)
                    {
                        // Staff have physically returned to this still-dirty vacant room.
                        // The same slot remains occupied while the optional inspection runs;
                        // bed completion cannot release the room for another key handoff yet.
                        phase = 5; due = now + fixturePreparationSeconds;
                        break;
                    }
                    ManualTurnoverModelAdapter.Require(simulation.BeginMakeBed(actor, task.RoomId, cleanId));
                    phase = 4; lastAction = now; due = now + .1f;
                    break;
                case 5:
                    prepareRoomFixtures(actor, task.RoomId);
                    ManualTurnoverModelAdapter.Require(simulation.BeginMakeBed(actor, task.RoomId, cleanId));
                    phase = 4; lastAction = now; due = now + .1f;
                    break;
                case 4:
                    float remaining = now - lastAction;
                    while (remaining >= .09999f && simulation.Housekeeping.Find(task.RoomId) != null)
                    {
                        ManualTurnoverModelAdapter.Require(simulation.AdvanceMakeBed(actor, task.RoomId, .1f));
                        remaining -= .1f; lastAction += .1f;
                    }
                    if (simulation.Housekeeping.Find(task.RoomId) == null)
                    {
                        previousDoor = DoorZ(task.RoomId);
                        staff[actor] = null; actor = -1; task = null;
                    }
                    break;
            }
        }

        static float DoorZ(int roomId) => 10 + (roomId - 101) / 2 * 7;
    }
}
