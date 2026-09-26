using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace WorstHotel.Tests
{
    public sealed partial class Phase1PlayModeTests
    {
        EconomyConfig maintenanceFixtureEconomy;

        [UnityTearDown]
        public IEnumerator DestroyMaintenanceFixtureEconomy()
        {
            if (maintenanceFixtureEconomy) Object.Destroy(maintenanceFixtureEconomy);
            maintenanceFixtureEconomy = null;
            yield return null;
        }

        [UnityTest, Category("ContinuousOperations")]
        public IEnumerator ContinuousSoloPhysicalRepairPaysOnceAndLeavesEmergencyPatchState()
        {
            ManagementUI.Instance.Close();
            bootstrap.ConfigureSolo();
            InputSystem.RemoveDevice(padB); padB = null;
            var session = GameSession.Instance;
            session.NewGame(); ManagementUI.Instance.Close();
            yield return null; yield return null;
            Assert.That(session.Simulation.ContinuousOperations && bootstrap.IsSolo, Is.True);
            Assert.That(bootstrap.Players[1], Is.Null);
            var model = session.Simulation; var boiler = model.Boiler;
            int cash = model.Economy.Cash;
            Assert.That(cash, Is.GreaterThanOrEqualTo(session.Economy.CheapPatchCost));
            // Explicit initial failure isolates paid physical repair; it is not overload evidence.
            boiler.ForceFailure();
            yield return HoldPhysicalRelief();
            yield return WaitForCondition(() => boiler.SoloValveLatched, 22, "Real relief hold must secure the SOLO catch.");
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That(boiler.ReliefActorId, Is.EqualTo(-1));
            float fixedStep = Time.fixedDeltaTime;
            foreach (var pair in new[]
            {
                (RepairControlKind.Panel, RepairStep.Breaker), (RepairControlKind.Breaker, RepairStep.LatchA),
                (RepairControlKind.LatchA, RepairStep.LatchB), (RepairControlKind.LatchB, RepairStep.Restart)
            })
            {
                yield return SoloWalkToControl(pair.Item1);
                yield return SoloUseControl(pair.Item1, pair.Item2);
                Assert.That(model.Economy.Cash, Is.EqualTo(cash), "Opening and seating physical controls cannot debit a patch early.");
            }
            yield return SoloWalkToControl(RepairControlKind.Restart);
            Assert.That(Control(RepairControlKind.Restart).GetPrompt(bootstrap.Players[0].Interactor),
                Does.Contain("$" + session.Economy.CheapPatchCost));
            yield return SoloUseControl(RepairControlKind.Restart, RepairStep.Complete);
            Assert.That(boiler.Failed, Is.False);
            Assert.That(boiler.EmergencyPatchActive, Is.True);
            Assert.That(boiler.Condition, Is.EqualTo(session.BoilerSettings.Capacity.EmergencyPatchCondition).Within(.05f));
            Assert.That(boiler.Stress01, Is.EqualTo(session.BoilerSettings.Capacity.EmergencyPatchStress).Within(.025f));
            Assert.That(boiler.SoloLatchSecondsRemaining, Is.Zero);
            Assert.That(boiler.HeatingOutput, Is.GreaterThan(0));
            Assert.That(model.Economy.Cash, Is.EqualTo(cash - session.Economy.CheapPatchCost));
            Assert.That(model.PeriodMaintenanceSpend, Is.EqualTo(session.Economy.CheapPatchCost));
            yield return WaitForCondition(() => session.Cash == model.Economy.Cash, 2, "The management cash display must observe the paid physical patch.");
            var gauge = Object.FindAnyObjectByType<BoilerReadout>();
            yield return WaitForCondition(() => gauge.capacityReadout.text.Contains("PATCHED"), 2, "The real boiler must retain its patched status.");
            QueueUse(padA, true); yield return null; yield return null;
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That(model.Economy.Cash, Is.EqualTo(cash - session.Economy.CheapPatchCost), "Repeated restart input cannot pay twice.");
            Assert.That(model.PeriodMaintenanceSpend, Is.EqualTo(session.Economy.CheapPatchCost));
            Assert.That(Time.timeScale, Is.EqualTo(1));
            Assert.That(Time.fixedDeltaTime, Is.EqualTo(fixedStep), "SOLO support and control holds remain real-time physics.");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest, Category("ContinuousOperations")]
        public IEnumerator ContinuousReceptionMaintenanceDebitsOnceKeepsHeatOffAndAllowsRealWaitUntilCompletion()
        {
            var session = GameSession.Instance;
            // Labelled starting-funds fixture: price, downtime and repair tuning remain production values.
            waitScenarioSessionConfig = Object.Instantiate(session.config);
            maintenanceFixtureEconomy = Object.Instantiate(session.config.economy);
            maintenanceFixtureEconomy.startingCash = maintenanceFixtureEconomy.properRepairCost + 500;
            waitScenarioSessionConfig.economy = maintenanceFixtureEconomy;
            session.config = waitScenarioSessionConfig; session.NewGame(); ManagementUI.Instance.Close();
            var model = session.Simulation; var boiler = model.Boiler;
            Assert.That(model.ContinuousOperations, Is.True);
            int cash = model.Economy.Cash;
            // Maintenance on an existing failure must be planned downtime, including for WAIT.
            boiler.ForceFailure();
            var terminal = Object.FindAnyObjectByType<ReceptionTerminal>();
            yield return FaceStation(bootstrap.Players[0], padA, terminal, terminal.transform.position + Vector3.up * .35f);
            QueueUse(padA, true);
            yield return WaitForCondition(() => ManagementUI.Instance.IsOperationsOpen, 2, "Use the real reception ledger to authorize maintenance.");
            QueueUse(padA, false); yield return null; yield return null;
            yield return ChooseOperationsOption("Boiler maintenance");
            yield return ChooseOperationsOption("Start proper maintenance");
            Assert.That(boiler.MaintenanceInProgress, Is.True);
            Assert.That(boiler.Failed, Is.True, "Buying the work must not claim completion of the existing failure.");
            Assert.That(boiler.HeatingOutput, Is.Zero);
            Assert.That(model.Economy.Cash, Is.EqualTo(cash - session.Economy.ProperRepairCost));
            Assert.That(session.Cash, Is.EqualTo(model.Economy.Cash));
            Assert.That(model.PeriodMaintenanceSpend, Is.EqualTo(session.Economy.ProperRepairCost));
            float end = boiler.MaintenanceEndsAt;
            Assert.That(boiler.MaintenanceRemaining(model.Elapsed), Is.InRange(50f, 60f));
            Assert.That(session.BeginBoilerMaintenance(1).Success, Is.False, "A partner's simultaneous repeat cannot restart or pay for the same job.");
            Assert.That(session.BeginBoilerMaintenance(-1).Success, Is.False);
            Assert.That(boiler.MaintenanceEndsAt, Is.EqualTo(end));
            Assert.That(model.PeriodMaintenanceSpend, Is.EqualTo(session.Economy.ProperRepairCost));
            Assert.That(model.Economy.Cash, Is.EqualTo(cash - session.Economy.ProperRepairCost));
            yield return null; yield return null;
            Assert.That(RepairController.CanUseControls, Is.False);
            Assert.That(RepairController.Status, Does.Contain("MAINTENANCE"));
            Assert.That(ManagementUI.Instance.OperationsOptionTitles.Any(title => title.StartsWith("Maintenance in progress")), Is.True);
            var gauge = Object.FindAnyObjectByType<BoilerReadout>();
            Assert.That(gauge.capacityReadout.text, Does.Contain("HEATING OFF").And.Contain("READY"));
            yield return ChooseOperationsOption("Close / keep working");
            float fixedStep = Time.fixedDeltaTime;
            yield return ConsentToWait();
            Assert.That(Waiter.IsWaiting, Is.True, "A planned paid shutdown must not be mistaken for an unanswered fault.");
            Assert.That(boiler.HeatingOutput, Is.Zero);
            Assert.That(Time.timeScale, Is.EqualTo(1));
            Assert.That(Time.fixedDeltaTime, Is.EqualTo(fixedStep));
            yield return WaitForCondition(() => !boiler.MaintenanceInProgress, 20, "Ordinary WAIT model ticks must complete the scheduled work.");
            yield return null; yield return null;
            QueueWait(padA, false); QueueWait(padB, false);
            Assert.That(model.Elapsed, Is.GreaterThanOrEqualTo(end));
            Assert.That(boiler.Failed || boiler.EmergencyPatchActive, Is.False);
            Assert.That(boiler.Condition, Is.EqualTo(session.BoilerSettings.Capacity.ProperMaintenanceCondition).Within(.05f));
            Assert.That(boiler.Stress01, Is.Zero);
            Assert.That(boiler.HeatingOutput, Is.GreaterThan(0));
            Assert.That(RepairController.Status, Does.Not.Contain("in progress"));
            Assert.That(Waiter.IsWaiting, Is.False, "Maintenance completion is one meaningful hotel event.");
            Assert.That(model.Economy.Cash, Is.EqualTo(cash - session.Economy.ProperRepairCost));
            Assert.That(model.PeriodMaintenanceSpend, Is.EqualTo(session.Economy.ProperRepairCost));
            Assert.That(session.Phase, Is.EqualTo(DayPhase.Service));
            Assert.That(ManagementUI.Instance.IsOpen, Is.False, "Completion never interrupts the player with an automatic menu.");
            LogAssert.NoUnexpectedReceived();
        }
    }
}
