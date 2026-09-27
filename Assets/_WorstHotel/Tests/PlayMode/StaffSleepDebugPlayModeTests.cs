using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace WorstHotel.Tests
{
    public sealed partial class Phase1PlayModeTests
    {
        [UnityTest, Category("ContinuousOperations"), Category("AutomaticSales"), Category("StaffSleepDebug")]
        public IEnumerator DiagnosticSoloSleepRequiresActualBedAndNormalBoilerFailureObserverWakesIt()
        {
            // This proves the labelled F2 fixture boundary, not normal physical button consent.
            // Ordinary SOLO/co-op consent is covered by StaffSleepPlayModeTests.
            yield return PrepareStaffSleep();
            var session = GameSession.Instance;
            var before = Waiter.CaptureSleepView();
            var rejected = Waiter.DebugStartSleep();
            Assert.That(rejected.Success, Is.False, "Local two-staff mode cannot borrow a SOLO diagnostic vote.");
            Assert.That(Waiter.SleepRevision, Is.EqualTo(before.Revision));
            Assert.That(Waiter.HasSleepConsent(0) || Waiter.HasSleepConsent(1), Is.False);
            Assert.That(session.Simulation.Clock.Speed, Is.EqualTo(1));

            yield return PrepareStaffSleep(solo: true);
            var model = session.Simulation;
            var player = bootstrap.Players[0];
            foreach (int bedId in new[] { 0, 1 })
                Assert.That(Vector3.Distance(player.PlayerCamera.transform.position, StaffBed(bedId).InteractionPoint),
                    Is.GreaterThan(player.Interactor.reach), "New-game reception spawn is explicitly away from staff beds.");
            before = Waiter.CaptureSleepView();
            rejected = Waiter.DebugStartSleep();
            Assert.That(rejected.Success, Is.False, "F2 must not authorize sleep from anywhere in the building.");
            Assert.That(Waiter.SleepRevision, Is.EqualTo(before.Revision));
            Assert.That(model.Clock.Speed, Is.EqualTo(1));

            // Labelled empty-actor approach placement plus real pad aim at the authored first surface.
            // We deliberately do not press Use; this path is diagnostic authorization only.
            yield return FaceStaffBed(0, 0);
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That(player.Input.PrimaryPressed || player.Input.PrimaryHeld, Is.False);
            Vector3 position = player.transform.position;
            float expectedMorning = WaitController.NextMorningAt(model.Calendar, model.Elapsed);
            SleepRequire(Waiter.DebugStartSleep());
            Assert.That(Waiter.IsSleeping && Waiter.HasSleepConsent(0), Is.True);
            Assert.That(Waiter.HasSleepConsent(1), Is.False);
            Assert.That(Waiter.SleepBedId(0), Is.EqualTo(0));
            Assert.That(Waiter.SleepUntil, Is.EqualTo(expectedMorning));
            Assert.That(model.Clock.Speed, Is.EqualTo(WaitController.SleepSpeed));
            Assert.That(player.transform.position, Is.EqualTo(position), "The diagnostic command does not teleport staff.");
            yield return null; yield return null;
            Assert.That(Waiter.IsSleeping, Is.True, Waiter.Reason);

            // Explicit actual infrastructure fault fixture. No manual wake-mode or speed writes.
            using (model.BeginDiagnosticInfrastructureChange()) model.Boiler.ForceFailure();
            Assert.That(model.Boiler.Failed, Is.True);
            Waiter.ObserveSimulationEvents();
            Assert.That(Waiter.IsSleeping, Is.False);
            Assert.That(Waiter.WakeReason, Is.EqualTo(StaffWakeReason.BoilerFailure));
            Assert.That(Waiter.HasSleepConsent(0) || Waiter.HasSleepConsent(1), Is.False);
            Assert.That(Waiter.SleepUntil, Is.Zero);
            Assert.That(model.Clock.Speed, Is.EqualTo(1));
            Assert.That(model.InfrastructureHistory.Last(item => item.Kind == InfrastructureChangeKind.BoilerFailure).Diagnostic, Is.True);
            Assert.That(session.Simulation, Is.SameAs(model), "Waking preserves the same running hotel.");

            // A replica cannot execute even this explicit local diagnostic API.
            session.PrepareLanReplica();
            Assert.That(session.IsLanReplica, Is.True);
            var mirror = session.Simulation;
            string snapshot = JsonUtility.ToJson(mirror.CaptureSnapshot(907, 1));
            before = Waiter.CaptureSleepView();
            rejected = Waiter.DebugStartSleep();
            Assert.That(rejected.Success, Is.False);
            Assert.That(Waiter.SleepRevision, Is.EqualTo(before.Revision));
            Assert.That(JsonUtility.ToJson(mirror.CaptureSnapshot(907, 1)), Is.EqualTo(snapshot));
            Assert.That(Waiter.HasSleepConsent(0) || Waiter.HasSleepConsent(1), Is.False);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
