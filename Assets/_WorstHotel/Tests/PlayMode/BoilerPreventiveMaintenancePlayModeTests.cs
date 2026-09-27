using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace WorstHotel.Tests
{
    public sealed partial class Phase1PlayModeTests
    {
        // These fixtures explicitly arrange a worn, working boiler and enough initial money.
        // Staff approach positioning is the existing empty-actor fixture; inspection, menu
        // selection, focus, held setup and WAIT all use the real controller/input pipeline.
        // Empty rooms isolate actual thermal downtime from unrelated guest interruptions.
        [UnityTest, Category("ContinuousOperations")]
        public IEnumerator WorkingBoilerBasicServiceRequiresFreshFocusedHoldAndReallyCoolsRooms()
        {
            yield return PrepareWorkingBoilerServiceFixture(true);
            var session = GameSession.Instance; var model = session.Simulation; var boiler = model.Boiler;
            var tuning = session.BoilerSettings.Capacity;
            int cash = model.Economy.Cash;
            float fixedStep = Time.fixedDeltaTime;
            yield return OpenPhysicalBoilerInspection();
            yield return SelectBoilerServiceThroughMenu(BoilerServiceKind.Basic);
            yield return ChooseOperationsOption("Close / keep working");
            yield return FaceBoilerService();
            var plaque = BoilerServiceInteraction.Instance;

            // A short release cannot bank setup progress or purchase any work.
            QueueUse(padA, true);
            yield return WaitForCondition(() => plaque.SetupProgress(0) >= .12f, 2, "Real hold should begin Basic setup.");
            Assert.That(plaque.SetupProgress(0), Is.LessThan(.8f));
            QueueUse(padA, false); yield return null; yield return null;
            AssertUnstartedBoilerService(cash);
            Assert.That(plaque.SetupProgress(0), Is.Zero);

            // Keep use held, but look away through actual pad input: losing the target cancels.
            QueueUse(padA, true);
            yield return WaitForCondition(() => plaque.SetupProgress(0) >= .12f, 2, "A second attempt starts from zero.");
            InputSystem.QueueStateEvent(padA, new GamepadState { rightStick = new Vector2(.9f, 0) }.WithButton(GamepadButton.South));
            yield return WaitForCondition(() => bootstrap.Players[0].Interactor.Focused != plaque, 1,
                "Actual look input must leave the boiler surface before setup finishes.");
            QueueUse(padA, false); yield return null; yield return null;
            AssertUnstartedBoilerService(cash);
            Assert.That(plaque.SetupProgress(0), Is.Zero);
            Assert.That(plaque.TryGetSelection(0, out var selected, out _), Is.True);
            Assert.That(selected, Is.EqualTo(BoilerServiceKind.Basic));

            float temperature = session.Rooms[0].Temperature;
            yield return HoldSelectedBoilerService(BoilerServiceKind.Basic);
            float conditionAtStart = boiler.Condition;
            float expectedCondition = Mathf.Max(conditionAtStart,
                Mathf.Min(tuning.BasicMaintenanceConditionCap, conditionAtStart + tuning.BasicMaintenanceConditionGain));
            Assert.That(boiler.Failed || boiler.EmergencyPatchActive, Is.False);
            Assert.That(boiler.HeatingOutput, Is.Zero);
            Assert.That(model.PeriodMaintenanceSpend, Is.EqualTo(session.Economy.BasicMaintenanceCost));
            Assert.That(model.Economy.Cash, Is.EqualTo(cash - session.Economy.BasicMaintenanceCost));
            Assert.That(boiler.MaintenanceRemaining(model.Elapsed), Is.EqualTo(
                model.Calendar.Settings.SecondsPerDay * tuning.BasicMaintenanceHours / 24).Within(.6f));
            Assert.That(RepairController.CanUseControls, Is.False);
            yield return FinishBoilerServiceWithRealWait();
            Assert.That(boiler.Condition, Is.EqualTo(expectedCondition).Within(.08f));
            Assert.That(boiler.Condition, Is.LessThan(tuning.ProperMaintenanceCondition), "Basic remains partial preventive work.");
            Assert.That(session.Rooms[0].Temperature, Is.LessThan(temperature - .5f), "Unforced room temperature must respond to real lost heat.");
            Assert.That(boiler.HeatingOutput, Is.GreaterThan(0));
            Assert.That(model.Economy.Cash, Is.EqualTo(cash - session.Economy.BasicMaintenanceCost));
            Assert.That(model.PeriodMaintenanceSpend, Is.EqualTo(session.Economy.BasicMaintenanceCost));
            Assert.That(Time.timeScale, Is.EqualTo(1));
            Assert.That(Time.fixedDeltaTime, Is.EqualTo(fixedStep));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest, Category("ContinuousOperations")]
        public IEnumerator WorkingBoilerFullServiceCancelsPausedSetupAndCompletesOneStrongerPaidJob()
        {
            yield return PrepareWorkingBoilerServiceFixture(false);
            var session = GameSession.Instance; var model = session.Simulation; var boiler = model.Boiler;
            int cash = model.Economy.Cash;
            float fixedStep = Time.fixedDeltaTime;
            yield return OpenPhysicalBoilerInspection();
            yield return SelectBoilerServiceThroughMenu(BoilerServiceKind.Full);
            yield return ChooseOperationsOption("Close / keep working");
            yield return FaceBoilerService();
            var plaque = BoilerServiceInteraction.Instance;
            Assert.That(plaque.TryGetSelection(0, out _, out var revision), Is.True);
            Assert.That(session.BeginBoilerMaintenance(0, BoilerServiceKind.Full, revision).Success, Is.False,
                "Selection and proximity alone cannot bypass the completed physical hold.");
            AssertUnstartedBoilerService(cash);

            QueueUse(padA, true);
            yield return WaitForCondition(() => plaque.SetupProgress(0) >= .12f, 2, "Real use should begin Full setup.");
            bootstrap.SetPaused(true); yield return null; yield return null;
            Assert.That(plaque.SetupProgress(0), Is.Zero, "Pause cancels setup instead of preserving partial work.");
            AssertUnstartedBoilerService(cash);
            Assert.That(plaque.TryGetSelection(0, out var selected, out var selectedRevision), Is.True);
            Assert.That(selected, Is.EqualTo(BoilerServiceKind.Full));
            Assert.That(selectedRevision, Is.EqualTo(revision));
            QueueUse(padA, false); bootstrap.SetPaused(false); yield return null; yield return null;

            float temperature = session.Rooms[0].Temperature;
            yield return HoldSelectedBoilerService(BoilerServiceKind.Full);
            Assert.That(boiler.Failed, Is.False, "Planned shutdown of a working boiler is not a failure.");
            Assert.That(boiler.HeatingOutput, Is.Zero);
            Assert.That(model.Economy.Cash, Is.EqualTo(cash - session.Economy.ProperRepairCost));
            Assert.That(model.PeriodMaintenanceSpend, Is.EqualTo(session.Economy.ProperRepairCost));
            Assert.That(boiler.MaintenanceRemaining(model.Elapsed), Is.EqualTo(
                model.Calendar.Settings.SecondsPerDay * session.BoilerSettings.Capacity.MaintenanceHours / 24).Within(.6f));
            Assert.That(session.BoilerSettings.Capacity.MaintenanceHours,
                Is.GreaterThan(session.BoilerSettings.Capacity.BasicMaintenanceHours));
            yield return FinishBoilerServiceWithRealWait();
            Assert.That(boiler.Condition, Is.EqualTo(session.BoilerSettings.Capacity.ProperMaintenanceCondition).Within(.08f));
            Assert.That(boiler.EmergencyPatchActive || boiler.Failed, Is.False);
            Assert.That(boiler.Stress01, Is.Zero);
            Assert.That(boiler.HeatingOutput, Is.GreaterThan(0));
            Assert.That(session.Rooms[0].Temperature, Is.LessThan(temperature - 1), "Full downtime must have a real thermal cost.");
            Assert.That(model.PeriodMaintenanceSpend, Is.EqualTo(session.Economy.ProperRepairCost));
            Assert.That(model.Economy.Cash, Is.EqualTo(cash - session.Economy.ProperRepairCost));
            Assert.That(session.BeginBoilerMaintenance(0, BoilerServiceKind.Full, revision).Success, Is.False);
            Assert.That(model.Economy.Cash, Is.EqualTo(cash - session.Economy.ProperRepairCost), "The old selection cannot purchase a second job.");
            Assert.That(Time.timeScale, Is.EqualTo(1));
            Assert.That(Time.fixedDeltaTime, Is.EqualTo(fixedStep));
            LogAssert.NoUnexpectedReceived();
        }

        IEnumerator PrepareWorkingBoilerServiceFixture(bool solo)
        {
            ManagementUI.Instance.Close();
            if (solo) { bootstrap.ConfigureSolo(); InputSystem.RemoveDevice(padB); padB = null; }
            var session = GameSession.Instance;
            waitScenarioSessionConfig = Object.Instantiate(session.config);
            maintenanceFixtureEconomy = Object.Instantiate(session.config.economy);
            maintenanceFixtureEconomy.startingCash = maintenanceFixtureEconomy.properRepairCost + 500;
            waitScenarioSessionConfig.economy = maintenanceFixtureEconomy;
            session.config = waitScenarioSessionConfig; session.NewGame(); ManagementUI.Instance.Close();
            session.Simulation.Boiler.SetCondition(48); // Explicit wear fixture, not a natural wear-rate claim.
            yield return null; yield return null;
            Assert.That(session.Simulation.ContinuousOperations, Is.True);
            Assert.That(session.Simulation.Boiler.Failed, Is.False);
            Assert.That(session.Simulation.Boiler.HeatingOutput, Is.GreaterThan(0));
        }

        IEnumerator FaceBoilerService()
        {
            var plaque = BoilerServiceInteraction.Instance;
            Assert.That(plaque, Is.Not.Null, "The authored boiler inspection must have a physical interaction surface.");
            yield return FaceStation(bootstrap.Players[0], padA, plaque, plaque.InteractionPoint);
        }

        IEnumerator OpenPhysicalBoilerInspection()
        {
            yield return FaceBoilerService();
            QueueUse(padA, true);
            yield return WaitForCondition(() => ManagementUI.Instance.IsOperationsOpen, 2, "Actual use on the working boiler opens inspection.");
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That(ManagementUI.Instance.OperationsOptionTitles, Has.Some.StartsWith("Select Basic Service"));
            Assert.That(ManagementUI.Instance.OperationsOptionTitles, Has.Some.StartsWith("Select Full Service"));
        }

        IEnumerator SelectBoilerServiceThroughMenu(BoilerServiceKind kind)
        {
            var model = GameSession.Instance.Simulation; var boiler = model.Boiler;
            int cash = model.Economy.Cash, spend = model.PeriodMaintenanceSpend, revision = boiler.MaintenanceRevision;
            float deadline = boiler.MaintenanceEndsAt, output = boiler.HeatingOutput;
            yield return ChooseOperationsOption(kind == BoilerServiceKind.Basic ? "Select Basic Service" : "Select Full Service");
            Assert.That(ManagementUI.Instance.IsOperationsOpen, Is.True, "Selecting work leaves the inspection open.");
            Assert.That(BoilerServiceInteraction.Instance.TryGetSelection(0, out var selected, out var selectedRevision), Is.True);
            Assert.That(selected, Is.EqualTo(kind));
            Assert.That(selectedRevision, Is.EqualTo(revision));
            Assert.That(boiler.MaintenanceRevision, Is.EqualTo(revision));
            Assert.That(boiler.MaintenanceEndsAt, Is.EqualTo(deadline));
            Assert.That(boiler.MaintenanceInProgress, Is.False);
            Assert.That(boiler.HeatingOutput, Is.EqualTo(output).Within(.0001f));
            Assert.That(model.Economy.Cash, Is.EqualTo(cash));
            Assert.That(model.PeriodMaintenanceSpend, Is.EqualTo(spend));
        }

        void AssertUnstartedBoilerService(int expectedCash)
        {
            var model = GameSession.Instance.Simulation;
            Assert.That(model.Boiler.MaintenanceInProgress, Is.False);
            Assert.That(model.Boiler.MaintenanceEndsAt, Is.Zero);
            Assert.That(model.Boiler.HeatingOutput, Is.GreaterThan(0));
            Assert.That(model.Economy.Cash, Is.EqualTo(expectedCash));
            Assert.That(model.PeriodMaintenanceSpend, Is.Zero);
        }

        IEnumerator HoldSelectedBoilerService(BoilerServiceKind kind)
        {
            if (ManagementUI.Instance.IsOpen) yield return ChooseOperationsOption("Close / keep working");
            yield return FaceBoilerService();
            var model = GameSession.Instance.Simulation;
            var plaque = BoilerServiceInteraction.Instance;
            Assert.That(plaque.TryGetSelection(0, out var selected, out var revision), Is.True);
            Assert.That(selected, Is.EqualTo(kind));
            Assert.That(plaque.SetupProgress(0), Is.Zero);
            float started = Time.realtimeSinceStartup;
            QueueUse(padA, true);
            yield return WaitForCondition(() => model.Boiler.MaintenanceInProgress,
                BoilerServiceInteraction.SetupHoldSeconds + 3, "Only a complete real focused hold may start paid maintenance.");
            Assert.That(Time.realtimeSinceStartup - started, Is.GreaterThanOrEqualTo(BoilerServiceInteraction.SetupHoldSeconds - .1f));
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That(model.Boiler.ActiveServiceKind, Is.EqualTo(kind));
            Assert.That(model.Boiler.MaintenanceRevision, Is.EqualTo(revision + 1));
            Assert.That(plaque.SetupProgress(0), Is.Zero);
            Assert.That(ManagementUI.Instance.IsOpen, Is.False);
        }

        IEnumerator FinishBoilerServiceWithRealWait()
        {
            var model = GameSession.Instance.Simulation;
            float end = model.Boiler.MaintenanceEndsAt;
            if (bootstrap.IsSolo)
            {
                QueueWait(padA, false); yield return null; yield return null;
                QueueWait(padA, true);
                yield return WaitForCondition(() => Waiter.IsWaiting, WaitHoldSeconds + 1, "SOLO may wait through planned service downtime.");
            }
            else yield return ConsentToWait();
            Assert.That(model.Boiler.HeatingOutput, Is.Zero);
            yield return WaitForCondition(() => !model.Boiler.MaintenanceInProgress, 25, "Normal WAIT model ticks complete the configured service.");
            yield return null; yield return null;
            QueueWait(padA, false); if (padB != null) QueueWait(padB, false);
            Assert.That(model.Elapsed, Is.GreaterThanOrEqualTo(end));
            Assert.That(model.Boiler.ActiveServiceKind, Is.EqualTo(BoilerServiceKind.None));
            Assert.That(Waiter.IsWaiting, Is.False, "Completed work is a meaningful event that ends WAIT.");
            Assert.That(ManagementUI.Instance.IsOpen, Is.False);
        }

        IEnumerator ReopenReceptionOperations()
        {
            var terminal = Object.FindAnyObjectByType<ReceptionTerminal>();
            yield return FaceStation(bootstrap.Players[0], padA, terminal, terminal.transform.position + Vector3.up * .35f);
            QueueUse(padA, true);
            yield return WaitForCondition(() => ManagementUI.Instance.IsOperationsOpen, 2, "Actual reception use reopens operations.");
            QueueUse(padA, false); yield return null; yield return null;
        }
    }
}
