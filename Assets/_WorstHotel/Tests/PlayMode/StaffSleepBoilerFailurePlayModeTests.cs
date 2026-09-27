using System.Collections;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace WorstHotel.Tests
{
    public sealed partial class Phase1PlayModeTests
    {
        [UnityTest, Category("ContinuousOperations"), Category("AutomaticSales"), Category("StaffSleep")]
        public IEnumerator SustainedBoilerStressNaturallyFailsDuringActualBedSleepAndWakesStaff()
        {
            yield return PrepareStaffSleep(solo: true);
            var session = GameSession.Instance;
            var model = session.Simulation;
            AdvanceStaffSleepSetupTo(model.Calendar.At(1, 23));
            var rooms = session.Rooms; var roomIdentities = rooms.ToArray();
            var boiler = model.Boiler; var clock = model.Clock;
            var actor = bootstrap.Players[0];
            float physicsStep = Time.fixedDeltaTime;
            long epoch = LanSession.Instance ? LanSession.Instance.Epoch : 0;
            Assert.That(model.Reservations, Is.Empty);
            Assert.That(model.Guests, Is.Empty);
            Assert.That(boiler.Failed || boiler.MaintenanceInProgress, Is.False);
            Assert.That(model.Heaters.Items.All(item => !item.SwitchedOn), Is.True);
            Assert.That(model.Electrical.Circuits.All(item => item.HasPower && item.TripCount == 0), Is.True);

            // Explicit empty-employee approach placement, followed by genuine pad aim at
            // the authored first-hit bed. This is not a walking-route or physical F2 test.
            yield return FaceStaffBed(0, 0);
            Assert.That(actor.Interactor.Focused, Is.SameAs(StaffBed(0)));
            Assert.That(actor.Interactor.HeldBody, Is.Null);

            // Explicit permitted high-risk fixture BEFORE consent. Production capacity,
            // stress/wear rates, calendar and failure threshold remain untouched. This
            // isolated synthetic thermal demand is NOT attributed occupancy evidence.
            // Only subsequent ordinary GameSession ticks may cause the actual failure.
            float fixtureLoad = boiler.EffectiveCapacity * 2;
            const float fixtureStress = .5f;
            using (model.BeginDiagnosticInfrastructureChange())
            {
                boiler.OverrideLoad(fixtureLoad);
                SleepRequire(model.DebugSetBoilerStress(fixtureStress));
            }
            float setupAt = model.Elapsed;
            Assert.That(boiler.LoadRatio, Is.GreaterThan(1));
            Assert.That(boiler.Stress01, Is.EqualTo(fixtureStress));
            Assert.That(boiler.Stress01, Is.LessThan(session.BoilerSettings.Capacity.CriticalStress));
            Assert.That(boiler.Failed, Is.False, "Setup must not cross the failure threshold or call ForceFailure.");
            Assert.That(boiler.HeatingOutput, Is.GreaterThan(0));
            Assert.That(model.InfrastructureHistory.Any(item => item.Kind == InfrastructureChangeKind.BoilerFailure && item.Value == 1), Is.False);

            int failures = 0;
            bool failedWhileAsleep = false;
            float failureAt = -1;
            boiler.OnFailureStarted += ObserveActualFailure;
            try
            {
                // Fresh production Use input creates the actual bed consent. No model
                // TrySleep/DebugStartSleep call and no AdvanceTime occur during sleep.
                QueueUse(padA, true);
                yield return WaitForCondition(() => Waiter.IsSleeping, 2, "Fresh physical bed input should permit an overloaded but still working boiler.");
                float consentAt = model.Elapsed;
                float initialStress = boiler.Stress01, initialOutput = boiler.HeatingOutput;
                Assert.That(boiler.Failed, Is.False, "Actual sleep starts before failure, not in an already-failed fixture.");
                Assert.That(initialStress, Is.LessThan(1));
                Assert.That(Waiter.SleepBedId(0), Is.EqualTo(0));
                Assert.That(Waiter.HasSleepConsent(0) && !Waiter.HasSleepConsent(1), Is.True);
                Assert.That(clock.Speed, Is.EqualTo(WaitController.SleepSpeed));
                QueueUse(padA, false); yield return null; yield return null;

                yield return WaitForCondition(() => boiler.Stress01 >= session.BoilerSettings.Capacity.CriticalStress, 8,
                    "Ordinary accelerated ticks must measurably accumulate stress from the declared persistent load.");
                Assert.That(boiler.Stress01, Is.GreaterThan(initialStress));
                Assert.That(boiler.Stress01, Is.LessThan(1));
                Assert.That(boiler.Failed, Is.False, "Critical stress is observed before the actual failure threshold.");
                Assert.That(Waiter.IsSleeping, Is.True, "A risk indicator alone is not the requested boiler-failure wake event.");
                Assert.That(failures, Is.Zero);
                float criticalAt = model.Elapsed;

                yield return WaitForCondition(() => boiler.Failed, 8,
                    "Sustained real kernel stress, not a scripted sleep event, must cause the boiler failure.");
                Assert.That(failures, Is.EqualTo(1));
                Assert.That(failedWhileAsleep, Is.True, "The failure must start while actual bed sleep owns acceleration.");
                Assert.That(failureAt, Is.GreaterThan(criticalAt));
                Assert.That(model.Elapsed - consentAt, Is.GreaterThan(5), "The scenario observes sustained accumulation rather than immediate failure on consent.");
                Assert.That(boiler.Stress01, Is.EqualTo(1));
                Assert.That(boiler.HeatingOutput, Is.LessThan(initialOutput));
                Assert.That(Waiter.IsSleeping || Waiter.HasSleepConsent(0) || Waiter.HasSleepConsent(1), Is.False);
                Assert.That(Waiter.WakeReason, Is.EqualTo(StaffWakeReason.BoilerFailure));
                Assert.That(Waiter.SleepUntil, Is.Zero);
                Assert.That(clock.Speed, Is.EqualTo(1));
                Assert.That(Time.timeScale, Is.EqualTo(1));
                Assert.That(Time.fixedDeltaTime, Is.EqualTo(physicsStep));
                Assert.That(model.Electrical.Circuits.All(item => item.HasPower && item.TripCount == 0), Is.True,
                    "Thermal overload does not inject a separate electrical outage.");
                Assert.That(model.Heaters.Items.All(item => !item.SwitchedOn), Is.True);
                Assert.That(session.Simulation, Is.SameAs(model));
                Assert.That(session.Rooms, Is.SameAs(rooms));
                Assert.That(model.Clock, Is.SameAs(clock));
                Assert.That(model.Boiler, Is.SameAs(boiler));
                for (int index = 0; index < rooms.Length; index++) Assert.That(rooms[index], Is.SameAs(roomIdentities[index]));
                Assert.That(LanSession.Instance ? LanSession.Instance.Epoch : 0, Is.EqualTo(epoch));
                var failure = model.InfrastructureHistory.Single(item => item.Kind == InfrastructureChangeKind.BoilerFailure && item.Value == 1);
                Assert.That(failure.Diagnostic, Is.False, "Only pre-sleep setup is diagnostic; the later recorded failure arose in ordinary ticks.");
                Assert.That(failure.At, Is.GreaterThan(setupAt));
                string trace = "STAFF SLEEP NATURAL BOILER FAILURE: setup=" + setupAt.ToString("F3", CultureInfo.InvariantCulture) +
                    " consent=" + consentAt.ToString("F3", CultureInfo.InvariantCulture) +
                    " critical=" + criticalAt.ToString("F3", CultureInfo.InvariantCulture) +
                    " failure=" + failureAt.ToString("F3", CultureInfo.InvariantCulture) +
                    " fixtureLoad=" + fixtureLoad.ToString("F3", CultureInfo.InvariantCulture) +
                    " initialStress=" + initialStress.ToString("F3", CultureInfo.InvariantCulture) +
                    " endStress=" + boiler.Stress01.ToString("F3", CultureInfo.InvariantCulture) +
                    " wake=" + Waiter.WakeReason + " failures=" + failures + " electricalTrips=0 physicsScale=1";
                LogAssert.Expect(LogType.Log, trace); Debug.Log(trace);
            }
            finally { boiler.OnFailureStarted -= ObserveActualFailure; }
            LogAssert.NoUnexpectedReceived();

            void ObserveActualFailure()
            {
                failures++;
                failedWhileAsleep = Waiter.IsSleeping;
                failureAt = model.Elapsed;
            }
        }
    }
}
