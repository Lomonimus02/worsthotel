using System;
using System.Reflection;
using NUnit.Framework;

namespace WorstHotel.Tests
{
    public sealed class BoilerMaintenanceTests
    {
        // Internal equipment commits are intentionally inaccessible to gameplay clients. This
        // labelled subsystem adapter tests them without bypassing production wrappers in the game.
        private static T Invoke<T>(BoilerSystem boiler, string method, params object[] args) =>
            (T)typeof(BoilerSystem).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(boiler, args);
        private static CommandResult Patch(BoilerSystem boiler, int actor = 1) => Invoke<CommandResult>(boiler, "ApplyEmergencyPatch", actor);
        private static CommandResult Begin(BoilerSystem boiler, float now) => Invoke<CommandResult>(boiler, "BeginMaintenance", now);
        private static bool Complete(BoilerSystem boiler, float now) => Invoke<bool>(boiler, "CompleteMaintenance", now);
        private static BoilerSystem Create(BoilerCapacitySettings capacity = null, float daySeconds = 720) =>
            new BoilerSystem(new BoilerSettings(capacity: capacity ?? new BoilerCapacitySettings(
                runningWearPerHotelDay: 0, overloadWearPerHotelDay: 0)), daySeconds);
        private static void ReadyForRepair(BoilerSystem boiler)
        {
            boiler.ForceFailure();
            Assert.That(boiler.SetRelief(0, true).Success, Is.True);
            boiler.Tick(20);
            Assert.That(boiler.InRepairBand, Is.True);
        }

        [Test]
        public void EmergencyCommitPublishesPatchedConditionStressAndOutputBeforeEveryCallback()
        {
            var boiler = Create(); boiler.SetLoad(4.5f); ReadyForRepair(boiler);
            int callbacks = 0, resolved = 0;
            Action checkCommitted = () =>
            {
                callbacks++;
                Assert.That(boiler.Failed, Is.False);
                Assert.That(boiler.EmergencyPatchActive, Is.True);
                Assert.That(boiler.Condition, Is.EqualTo(40));
                Assert.That(boiler.Stress01, Is.EqualTo(.2f));
                Assert.That(boiler.MaintenanceInProgress, Is.False);
                Assert.That(boiler.ReliefActorId, Is.EqualTo(-1));
                Assert.That(boiler.Pressure, Is.EqualTo(40));
                Assert.That(boiler.HeatingOutput, Is.EqualTo(boiler.EffectiveCapacity / boiler.Load).Within(.00001f));
            };
            boiler.OnConditionChanged += _ => checkCommitted();
            boiler.OnPressureChanged += _ => checkCommitted();
            boiler.OnHeatingOutputChanged += _ => checkCommitted();
            boiler.OnFailureResolved += () => { resolved++; checkCommitted(); };
            Assert.That(Patch(boiler).Success, Is.True);
            Assert.That(callbacks, Is.EqualTo(4));
            Assert.That(resolved, Is.EqualTo(1));
            Assert.That(Patch(boiler).Success, Is.False);
            Assert.That(callbacks, Is.EqualTo(4), "A replay must not reapply the patch or emit a second recovery.");
        }

        [Test]
        public void RestartPermissionIsPureAndPatchRevalidatesLiveSupportAfterThePreflight()
        {
            var boiler = Create(); ReadyForRepair(boiler);
            float condition = boiler.Condition, pressure = boiler.Pressure;
            Assert.That(boiler.CanRestart(1).Success, Is.True);
            Assert.That(boiler.CanRestart(0).Success, Is.False);
            Assert.That(boiler.CanRestart(2).Success, Is.False);
            Assert.That(boiler.Condition, Is.EqualTo(condition));
            Assert.That(boiler.Pressure, Is.EqualTo(pressure));
            Assert.That(boiler.Failed, Is.True);
            boiler.SetRelief(0, false);
            Assert.That(Patch(boiler).Success, Is.False);
            Assert.That(boiler.EmergencyPatchActive, Is.False);
            Assert.That(boiler.Condition, Is.EqualTo(condition));
        }

        [Test]
        public void ConfiguredPatchOutcomeAndPenaltyApplyOnceWithoutAnExtraCapacityPenalty()
        {
            var tuning = new BoilerCapacitySettings(runningWearPerHotelDay: 0, overloadWearPerHotelDay: 0,
                emergencyPatchCondition: 35, emergencyPatchStress: .3f, emergencyPatchStressMultiplier: 1.4f);
            var patched = Create(tuning); var comparison = Create(tuning);
            ReadyForRepair(patched); Assert.That(Patch(patched).Success, Is.True);
            comparison.SetCondition(35);
            patched.SetLoad(4.5f); comparison.SetLoad(4.5f);
            Assert.That(patched.Condition, Is.EqualTo(35));
            Assert.That(patched.Stress01, Is.EqualTo(.3f));
            Assert.That(patched.EffectiveCapacity, Is.EqualTo(comparison.EffectiveCapacity));
            Assert.That(patched.HeatingOutput, Is.EqualTo(comparison.HeatingOutput));
            patched.Tick(30); comparison.Tick(30);
            Assert.That(patched.Stress01 - .3f, Is.EqualTo(comparison.Stress01 * 1.4f).Within(.00001f));
        }

        [Test]
        public void BelowCapacityStillRecoversAPatchAndLaterFailureRetainsItsUnservicedMarker()
        {
            var boiler = Create(); ReadyForRepair(boiler); Assert.That(Patch(boiler).Success, Is.True);
            boiler.SetLoad(0); boiler.Tick(1);
            Assert.That(boiler.Stress01, Is.GreaterThan(0).And.LessThan(.2f));
            boiler.ForceFailure();
            Assert.That(boiler.EmergencyPatchActive, Is.True);
        }

        [Test]
        public void MaintenanceStopsHeatAndWearUntilAbsoluteDeadlineAndCompletesExactlyOnce()
        {
            var boiler = Create(new BoilerCapacitySettings());
            boiler.SetLoad(8); ReadyForRepair(boiler); Assert.That(Patch(boiler).Success, Is.True);
            boiler.ForceFailure();
            float condition = boiler.Condition, stress = boiler.Stress01;
            int recovered = 0; boiler.OnFailureResolved += () => recovered++;
            Assert.That(Begin(boiler, 500).Success, Is.True);
            Assert.That(boiler.MaintenanceEndsAt, Is.EqualTo(560));
            Assert.That(boiler.MaintenanceRemaining(510), Is.EqualTo(50));
            Assert.That(boiler.HeatingOutput, Is.Zero);
            Assert.That(boiler.Failed, Is.True);
            Assert.That(recovered, Is.Zero);
            boiler.Tick(5000);
            Assert.That(boiler.MaintenanceInProgress, Is.True, "Elapsed Tick input cannot complete an absolute-time job by itself.");
            Assert.That(boiler.Condition, Is.EqualTo(condition));
            Assert.That(boiler.Stress01, Is.EqualTo(stress));
            Assert.That(Complete(boiler, 559.9f), Is.False);
            Assert.That(Complete(boiler, 560), Is.True);
            Assert.That(boiler.MaintenanceEndsAt, Is.Zero);
            Assert.That(boiler.EmergencyPatchActive, Is.False);
            Assert.That(boiler.Condition, Is.EqualTo(95));
            Assert.That(boiler.Stress01, Is.Zero);
            Assert.That(boiler.Failed, Is.False);
            Assert.That(boiler.HeatingOutput, Is.GreaterThan(0));
            Assert.That(recovered, Is.EqualTo(1));
            Assert.That(Complete(boiler, 1000), Is.False);
            Assert.That(recovered, Is.EqualTo(1));
        }

        [Test]
        public void MaintenanceCancelsSoloSupportAndRejectsAllRestartPathsWithoutResettingTheJob()
        {
            var boiler = Create(); boiler.ConfigureSoloAssist(new SoloAssistSettings()); ReadyForRepair(boiler);
            Assert.That(boiler.HoldSoloValve(0, 2).Success, Is.True);
            Assert.That(boiler.SoloValveLatched, Is.True);
            Assert.That(Begin(boiler, 10).Success, Is.True);
            Assert.That(boiler.SoloValveLatched, Is.False);
            Assert.That(boiler.SoloLatchSecondsRemaining, Is.Zero);
            Assert.That(boiler.ReliefActorId, Is.EqualTo(-1));
            Assert.That(boiler.SetRelief(0, true).Success, Is.False);
            Assert.That(boiler.HoldSoloValve(0, 10).Success, Is.False);
            Assert.That(boiler.CanRestart(1).Success, Is.False);
            Assert.That(boiler.Restart(1).Success, Is.False);
            Assert.That(Patch(boiler).Success, Is.False);
            Assert.That(Begin(boiler, 20).Success, Is.False);
            boiler.ApplyPaidMaintenance(100); boiler.BeginService();
            Assert.That(boiler.MaintenanceEndsAt, Is.EqualTo(70));
            Assert.That(boiler.HeatingOutput, Is.Zero);
        }

        [Test]
        public void HealthyMaintenanceDoesNotInventFailureAndCompletionNeverLowersBetterCondition()
        {
            var boiler = Create(); boiler.SetCondition(99);
            int failures = 0, recoveries = 0;
            boiler.OnFailureStarted += () => failures++;
            boiler.OnFailureResolved += () => recoveries++;
            Assert.That(Begin(boiler, 0).Success, Is.True);
            boiler.ForceFailure();
            Assert.That(boiler.Failed, Is.False, "An offline appliance cannot suffer a running-overload failure.");
            Assert.That(Complete(boiler, 60), Is.True);
            Assert.That(boiler.Condition, Is.EqualTo(99));
            Assert.That(failures, Is.Zero);
            Assert.That(recoveries, Is.Zero);
            Assert.That(boiler.HeatingOutput, Is.EqualTo(1));
        }

        [Test]
        public void MaintenanceCompletionCallbacksSeeOneFullyRestoredState()
        {
            var boiler = Create(); ReadyForRepair(boiler); Assert.That(Begin(boiler, 100).Success, Is.True);
            int observations = 0;
            Action check = () =>
            {
                observations++;
                Assert.That(boiler.MaintenanceInProgress, Is.False);
                Assert.That(boiler.Condition, Is.EqualTo(95));
                Assert.That(boiler.Failed, Is.False);
                Assert.That(boiler.HeatingOutput, Is.EqualTo(1));
                Assert.That(boiler.Stress01, Is.Zero);
                Assert.That(boiler.Pressure, Is.EqualTo(20));
            };
            boiler.OnConditionChanged += _ => check();
            boiler.OnPressureChanged += _ => check();
            boiler.OnHeatingOutputChanged += _ => check();
            boiler.OnFailureResolved += check;
            Assert.That(Complete(boiler, 160), Is.True);
            Assert.That(observations, Is.EqualTo(4));
        }

        [Test]
        public void MaintenanceHoursUseConfiguredCalendarAndInvalidDeadlinesDoNotStartOrMuteHeat()
        {
            var boiler = Create(new BoilerCapacitySettings(maintenanceHours: 3), daySeconds: 1440);
            Assert.That(Begin(boiler, float.NaN).Success, Is.False);
            Assert.That(Begin(boiler, -1).Success, Is.False);
            Assert.That(Begin(boiler, float.MaxValue).Success, Is.False);
            Assert.That(boiler.MaintenanceInProgress, Is.False);
            Assert.That(boiler.HeatingOutput, Is.EqualTo(1));
            Assert.That(Begin(boiler, 100).Success, Is.True);
            Assert.That(boiler.MaintenanceEndsAt, Is.EqualTo(280));
            Assert.That(boiler.MaintenanceRemaining(300), Is.Zero);
        }

        [Test]
        public void LegacyRepairRemainsImmediateWithoutCreatingContinuousPatchOrTimedWork()
        {
            var boiler = new BoilerSystem(new BoilerSettings()); ReadyForRepair(boiler);
            float condition = boiler.Condition;
            Assert.That(Begin(boiler, 0).Success, Is.False);
            Assert.That(Patch(boiler).Success, Is.False);
            Assert.That(boiler.CanRestart(1).Success, Is.True);
            Assert.That(boiler.Restart(1).Success, Is.True);
            Assert.That(boiler.Condition, Is.EqualTo(condition));
            Assert.That(boiler.EmergencyPatchActive, Is.False);
            Assert.That(boiler.MaintenanceInProgress, Is.False);
        }

        [Test]
        public void MaintenanceTuningRequiresARealDowntimeAndABetterProperOutcome()
        {
            Assert.Throws<ArgumentException>(() => new BoilerCapacitySettings(emergencyPatchCondition: 101));
            Assert.Throws<ArgumentException>(() => new BoilerCapacitySettings(emergencyPatchStress: 1));
            Assert.Throws<ArgumentException>(() => new BoilerCapacitySettings(emergencyPatchStressMultiplier: .5f));
            Assert.Throws<ArgumentException>(() => new BoilerCapacitySettings(properMaintenanceCondition: 30));
            Assert.Throws<ArgumentException>(() => new BoilerCapacitySettings(maintenanceHours: 0));
            Assert.Throws<ArgumentException>(() => new BoilerCapacitySettings(maintenanceHours: float.PositiveInfinity));
        }
    }
}
