using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace WorstHotel.Tests
{
    public sealed partial class Phase1PlayModeTests
    {
        [UnityTest, Category("ContinuousOperations"), Category("StaffSleep")]
        public IEnumerator StaffSleepFramesValidateAtomicallyAcrossMorningAndRequireFreshRevision()
        {
            var session = GameSession.Instance;
            var hotel = session.Simulation;
            // Explicit wire-validation fixture: no claim of physical consent. The separate
            // bed-input tests establish that authority; here normal ticks construct both dates.
            session.AdvanceTime(hotel.Calendar.At(2, 5.5f) - hotel.Elapsed);
            var first = session.CaptureLanFrame(918, 1);
            first.sleep = new LanStaffSleepFrame { revision = 10 };
            session.AdvanceTime(hotel.Calendar.At(2, 6.5f) - hotel.Elapsed);
            var next = session.CaptureLanFrame(918, 2);
            next.model.Speed = WaitController.SleepSpeed;
            next.sleep = new LanStaffSleepFrame
            {
                mode = HotelAdvanceMode.Sleep, revision = 11,
                until = WaitController.NextMorningAt(hotel.Calendar, next.model.Time),
                bedIds = new[] { 0, 1 }, ready = new[] { true, true }
            };
            Assert.That(next.sleep.until, Is.GreaterThan(WaitController.NextMorningAt(hotel.Calendar, first.model.Time)),
                "The incoming date crosses06:00; validating with the old mirror clock would select the wrong morning.");
            string nextJson = JsonUtility.ToJson(next);
            session.PrepareLanReplica();
            var applied = session.ApplyLanFrame(first);
            Assert.That(applied.Success, Is.True, applied.Message);
            var mirror = session.Simulation;
            var rooms = session.Rooms;
            var plan = session.Plan;
            string before = JsonUtility.ToJson(mirror.CaptureSnapshot(918, 1));
            string beforeView = JsonUtility.ToJson(LanStaffSleepFrame.FromView(session.Wait.CaptureSleepView()));
            ManagementUI.Instance.Open(0);
            var corruptions = new Action<LanHotelFrame>[]
            {
                value => value.sleep = null,
                value => value.sleep.mode = (HotelAdvanceMode)99,
                value => value.sleep.wakeReason = (StaffWakeReason)99,
                value => value.sleep.revision = -1,
                value => value.sleep.revision = 9,
                value => value.sleep.revision = 10,
                value => value.sleep.until = float.NaN,
                value => value.sleep.until = float.PositiveInfinity,
                value => value.sleep.until = value.model.Time,
                value => value.sleep.until += 1,
                value => value.sleep.ready = new[] { true },
                value => value.sleep.bedIds = null,
                value => value.sleep.bedIds[1] = 0,
                value => value.sleep.bedIds[0] = 2,
                value => value.sleep.ready[1] = false,
                value => { value.sleep.ready[1] = false; value.sleep.bedIds[1] = -1; },
                value => value.sleep.mode = HotelAdvanceMode.Wait,
                value => value.sleep.wakeReason = StaffWakeReason.Morning,
                value => value.model.Speed = 1,
                value => value.waitVotes[0] = true,
                value => value.waitProgress[1] = .5f,
                value => value.hostPaused = true
            };
            foreach (var corrupt in corruptions)
            {
                var invalid = JsonUtility.FromJson<LanHotelFrame>(nextJson);
                corrupt(invalid);
                Assert.That(session.ApplyLanFrame(invalid).Success, Is.False, "Malformed or stale bed state must fail before hotel mutation.");
                Assert.That(session.Simulation, Is.SameAs(mirror));
                Assert.That(session.Rooms, Is.SameAs(rooms));
                Assert.That(session.Plan, Is.SameAs(plan));
                Assert.That(JsonUtility.ToJson(mirror.CaptureSnapshot(918, 1)), Is.EqualTo(before));
                Assert.That(JsonUtility.ToJson(LanStaffSleepFrame.FromView(session.Wait.CaptureSleepView())), Is.EqualTo(beforeView));
                Assert.That(ManagementUI.Instance.IsOperationsOpen, Is.True);
            }
            foreach (int invalidKind in new[] { 0, 1, 2 })
            {
                var invalidWait = JsonUtility.FromJson<LanHotelFrame>(nextJson);
                invalidWait.sleep = new LanStaffSleepFrame { mode = HotelAdvanceMode.Wait, revision = 11 };
                invalidWait.waitVotes = new[] { true, true }; invalidWait.waitProgress = new[] { 1f, 1f };
                if (invalidKind == 0) invalidWait.sleep.revision = 0;
                else if (invalidKind == 1) invalidWait.sleep.wakeReason = StaffWakeReason.Morning;
                else invalidWait.hostPaused = true;
                Assert.That(session.ApplyLanFrame(invalidWait).Success, Is.False, "Impossible active WAIT presentation is also rejected atomically.");
                Assert.That(JsonUtility.ToJson(mirror.CaptureSnapshot(918, 1)), Is.EqualTo(before));
                Assert.That(JsonUtility.ToJson(LanStaffSleepFrame.FromView(session.Wait.CaptureSleepView())), Is.EqualTo(beforeView));
            }
            applied = session.ApplyLanFrame(JsonUtility.FromJson<LanHotelFrame>(nextJson));
            Assert.That(applied.Success, Is.True, "A corrected packet uses the SAME sequence: " + applied.Message);
            Assert.That(session.Wait.IsSleeping && session.Wait.HasSleepConsent(0) && session.Wait.HasSleepConsent(1), Is.True);
            Assert.That(session.Wait.SleepUntil, Is.EqualTo(next.sleep.until));
            Assert.That(session.Wait.SleepRevision, Is.EqualTo(11));
            float time = mirror.Elapsed;
            mirror.Tick(1);
            Assert.That(session.Wait.TrySleep(0, 0).Success, Is.False, "Replica bed APIs cannot authorize time advance.");
            yield return null;
            Assert.That(mirror.Elapsed, Is.EqualTo(time), "A mirrored sleep view cannot advance hotel time without a newer host packet.");
            Assert.That(session.ApplyLanFrame(JsonUtility.FromJson<LanHotelFrame>(nextJson)).Success, Is.False);

            var pending = JsonUtility.FromJson<LanHotelFrame>(nextJson);
            pending.sequence = pending.model.Sequence = 3;
            pending.model.Speed = 1; pending.sleep.mode = HotelAdvanceMode.None; pending.sleep.revision = 12;
            pending.sleep.ready[1] = false; pending.sleep.bedIds[1] = -1;
            applied = session.ApplyLanFrame(pending);
            Assert.That(applied.Success, Is.True, applied.Message);
            Assert.That(session.Wait.HasSleepConsent(0) && !session.Wait.HasSleepConsent(1), Is.True);
            Assert.That(session.Wait.IsSleeping, Is.False);
            Assert.That(mirror.Clock.Speed, Is.EqualTo(1));

            var cleared = JsonUtility.FromJson<LanHotelFrame>(JsonUtility.ToJson(pending));
            cleared.sequence = cleared.model.Sequence = 4;
            cleared.sleep = new LanStaffSleepFrame { revision = 13, wakeReason = StaffWakeReason.StaffCancelled };
            applied = session.ApplyLanFrame(cleared);
            Assert.That(applied.Success, Is.True, applied.Message);
            Assert.That(session.Wait.HasSleepConsent(0) || session.Wait.HasSleepConsent(1), Is.False);
            Assert.That(session.Wait.SleepUntil, Is.Zero);
            Assert.That(session.Wait.WakeReason, Is.EqualTo(StaffWakeReason.StaffCancelled));
            cleared.sequence = cleared.model.Sequence = 5;
            Assert.That(session.ApplyLanFrame(cleared).Success, Is.True, "An unchanged controller revision may accompany newer hotel state.");

            first.epoch = first.model.Epoch = 919;
            first.sleep = new LanStaffSleepFrame();
            applied = session.ApplyLanFrame(first);
            Assert.That(applied.Success, Is.True, "A genuinely new epoch can start a fresh lower consent revision: " + applied.Message);
            Assert.That(session.Wait.SleepRevision, Is.Zero);
            Assert.That(session.Wait.HasSleepConsent(0) || session.Wait.HasSleepConsent(1), Is.False);
            var sleepingAgain = JsonUtility.FromJson<LanHotelFrame>(nextJson);
            sleepingAgain.epoch = sleepingAgain.model.Epoch = 920;
            sleepingAgain.sequence = sleepingAgain.model.Sequence = 1;
            applied = session.ApplyLanFrame(sleepingAgain);
            Assert.That(applied.Success, Is.True, applied.Message);
            Assert.That(session.Wait.IsSleeping, Is.True);
            session.NewGame();
            Assert.That(session.Wait.Mode, Is.EqualTo(HotelAdvanceMode.None), "NewGame must immediately clear a previous replica sleep view, before Update.");
            Assert.That(session.Wait.HasSleepConsent(0) || session.Wait.HasSleepConsent(1), Is.False);
            Assert.That(session.Wait.SleepUntil, Is.Zero);
            Assert.That(session.Simulation.IsReadOnlyMirror, Is.False);
            Assert.That(session.Simulation.Clock.Speed, Is.EqualTo(1));
            ManagementUI.Instance.Close();
            LogAssert.NoUnexpectedReceived();
        }
    }
}
