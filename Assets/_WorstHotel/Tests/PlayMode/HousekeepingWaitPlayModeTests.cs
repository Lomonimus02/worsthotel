using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace WorstHotel.Tests
{
    public sealed partial class Phase1PlayModeTests
    {
        [UnityTest]
        public IEnumerator AllDirtyPlanningRequiresManualLinenAndNewGameResetsClaimsAndStock()
        {
            var session = GameSession.Instance;
            var simulation = session.Simulation;
            var housekeeping = simulation.Housekeeping;
            Assert.That(session.Phase, Is.EqualTo(DayPhase.Planning));
            // Explicit diagnostic fixture. No travel/worker callback is supplied, and no cleanliness is reset directly.
            foreach (var room in session.Rooms)
                Assert.That(simulation.DebugMarkRoomDirty(room.Profile.Id).Success, Is.True);
            Assert.That(housekeeping.Tasks.Count, Is.EqualTo(6));
            Assert.That(Object.FindObjectsByType<HousekeeperPresentation>(FindObjectsInactive.Include, FindObjectsSortMode.None), Is.Empty,
                "The initial hotel must not retain the automatic housekeeper NPC.");
            housekeeping.SetWorkerAvailable(true);
            Assert.That(housekeeping.WorkerAvailable, Is.False, "An old presentation callback cannot reactivate automatic cleaning.");
            Assert.That(simulation.SignalHousekeeperReachedRoom(106).Success, Is.False);
            float cash = simulation.Economy.Cash, condition = simulation.Boiler.Condition;
            float pressure = simulation.Boiler.Pressure, physicsStep = Time.fixedDeltaTime;
            var temperatures = session.Rooms.Select(room => room.Temperature).ToArray();

            yield return ReleaseWaitButtons();
            QueueWait(padA, true); QueueWait(padB, true);
            yield return new WaitForSecondsRealtime(WaitHoldSeconds + .15f);
            Assert.That(bootstrap.Players.All(player => player.Input.WaitHeld), Is.True);
            Assert.That(Waiter.IsWaiting, Is.False, "Two votes cannot replace carrying linen and making beds.");
            Assert.That(Waiter.HasVoted(0) || Waiter.HasVoted(1), Is.False);
            Assert.That(Waiter.Reason, Does.Contain("Prepare the rooms"));
            Assert.That(Waiter.Reason, Does.Contain("linen"));
            // The public preparation-time diagnostic uses the actual GameSession tick path.
            simulation.Clock.SetSpeed(8);
            Assert.That(Waiter.Mode, Is.EqualTo(HotelAdvanceMode.None));
            Assert.That(Waiter.IsWaiting || Waiter.HasSleepConsent(0) || Waiter.HasSleepConsent(1), Is.False,
                "A diagnostic speed cannot manufacture WAIT or bed consent.");
            session.AdvanceTime(120);
            Assert.That(simulation.Clock.Speed, Is.EqualTo(1), "WAIT guards must also stop diagnostic acceleration during manual-only preparation.");
            Assert.That(session.Rooms.All(room => room.Cleanliness == Cleanliness.Dirty), Is.True);
            Assert.That(housekeeping.Tasks.All(task => task.Step == RoomPreparationStep.DirtyLinenOnBed && task.ProgressSeconds == 0), Is.True);
            Assert.That(housekeeping.CurrentTask, Is.Null);
            Assert.That(housekeeping.Linens.Count(linen => linen.Kind == LinenKind.Clean && linen.Location == LinenLocation.OnShelf), Is.EqualTo(6));
            Assert.That(simulation.Economy.Cash, Is.EqualTo(cash));
            Assert.That(simulation.Boiler.Condition, Is.EqualTo(condition));
            Assert.That(simulation.Boiler.Pressure, Is.EqualTo(pressure));
            CollectionAssert.AreEqual(temperatures, session.Rooms.Select(room => room.Temperature).ToArray());
            Assert.That(simulation.Guests.Count, Is.Zero);
            Assert.That(session.Phase, Is.EqualTo(DayPhase.Planning));
            Assert.That(Time.timeScale, Is.EqualTo(1));
            Assert.That(Time.fixedDeltaTime, Is.EqualTo(physicsStep));

            // Arrange consumed stock and an unfinished claim through real model intentions.
            // Physical grab/deposit/hold authentication is covered by the separate linen interaction tests.
            var firstDirty = housekeeping.Find(101).DirtyLinenId;
            Assert.That(simulation.PickUpLinen(7, firstDirty).Success, Is.True);
            Assert.That(simulation.DepositDirtyLinen(7, firstDirty).Success, Is.True);
            Assert.That(simulation.PickUpLinen(7, "clean:0").Success, Is.True);
            Assert.That(simulation.BeginMakeBed(7, 101, "clean:0").Success, Is.True);
            Assert.That(simulation.AdvanceMakeBed(7, 101, housekeeping.Settings.MakeBedSeconds).Success, Is.True);
            Assert.That(housekeeping.FindLinen("clean:0").Location, Is.EqualTo(LinenLocation.Consumed));
            var secondDirty = housekeeping.Find(102).DirtyLinenId;
            Assert.That(simulation.PickUpLinen(7, secondDirty).Success, Is.True);
            Assert.That(simulation.DepositDirtyLinen(7, secondDirty).Success, Is.True);
            Assert.That(simulation.PickUpLinen(7, "clean:1").Success, Is.True);
            Assert.That(simulation.BeginMakeBed(7, 102, "clean:1").Success, Is.True);
            Assert.That(simulation.AdvanceMakeBed(7, 102, housekeeping.Settings.MakeBedSeconds * .4f).Success, Is.True);
            var previousClaim = housekeeping.Find(102);
            float manualProgress = previousClaim.ProgressSeconds;
            session.AdvanceTime(30);
            Assert.That(previousClaim.ProgressSeconds, Is.EqualTo(manualProgress), "Hotel ticks cannot finish even an already-started manual hold.");
            Assert.That(previousClaim.WorkingPlayerId, Is.EqualTo(7));
            yield return null; yield return null;

            session.NewGame();
            yield return null; yield return null; yield return null;
            var fresh = session.Simulation.Housekeeping;
            Assert.That(Waiter.IsWaiting, Is.False);
            Assert.That(Waiter.HasVoted(0) || Waiter.HasVoted(1), Is.False, "A new session must discard held preparation consent.");
            Assert.That(session.Simulation, Is.Not.SameAs(simulation));
            Assert.That(fresh.Tasks, Is.Empty);
            Assert.That(fresh.CurrentTask, Is.Null);
            Assert.That(fresh.LastRefillDay, Is.EqualTo(1));
            Assert.That(session.Rooms.All(room => room.Cleanliness == Cleanliness.Clean && room.DepartingGuestId == null && room.TurnoverState == HousekeepingState.None), Is.True);
            Assert.That(fresh.Linens.All(linen => linen.PlayerId == null), Is.True);
            Assert.That(fresh.Linens.Count(linen => linen.Kind == LinenKind.Clean && linen.Location == LinenLocation.OnShelf), Is.EqualTo(6));
            Assert.That(fresh.Linens.Where(linen => linen.Kind == LinenKind.Dirty).All(linen => linen.Location == LinenLocation.Consumed), Is.True);
            var physical = Object.FindObjectsByType<LinenBundleItem>(FindObjectsSortMode.None);
            Assert.That(physical.Length, Is.EqualTo(12));
            Assert.That(physical.Select(item => item.itemId).Distinct().Count(), Is.EqualTo(12));
            foreach (var item in physical)
            {
                Assert.That(item.BoundSimulation, Is.SameAs(session.Simulation));
                Assert.That(item.Body.GetComponent<ConfigurableJoint>(), Is.Null);
                if (item.State.Kind == LinenKind.Clean)
                    Assert.That(Vector3.Distance(item.Body.position, item.sourceAnchor.position), Is.LessThan(.04f), "The same physical shelf slots must return on NewGame.");
            }
            LogAssert.NoUnexpectedReceived();
        }
    }
}
