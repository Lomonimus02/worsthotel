using System;
using NUnit.Framework;

namespace WorstHotel.Tests
{
    public sealed class RepairSequenceTests
    {
        private static RepairSequence FailureInGreenBand(out BoilerSystem boiler)
        {
            var settings = new BoilerSettings(baseWearPerMinute: 0, overloadWearPerMinute: 0);
            boiler = new BoilerSystem(settings);
            var sequence = new RepairSequence(boiler, settings);
            boiler.ForceFailure();
            sequence.Refresh();
            Assert.That(boiler.SetRelief(0, true).Success, Is.True);
            boiler.Tick(11);
            Assert.That(boiler.InRepairBand, Is.True);
            return sequence;
        }

        private static void OpenAndIsolate(RepairSequence sequence, int actor = 1)
        {
            Assert.That(sequence.Press(RepairControlKind.Panel, actor).Success, Is.True);
            Assert.That(sequence.Press(RepairControlKind.Breaker, actor).Success, Is.True);
        }

        private static void SeatBothLatches(RepairSequence sequence, int actor = 1)
        {
            Assert.That(sequence.Press(RepairControlKind.LatchA, actor).Success, Is.True);
            Assert.That(sequence.HoldLatch(RepairControlKind.LatchA, actor, 2.5f).Success, Is.True);
            Assert.That(sequence.Press(RepairControlKind.LatchB, actor).Success, Is.True);
            Assert.That(sequence.HoldLatch(RepairControlKind.LatchB, actor, 2.5f).Success, Is.True);
        }

        [Test]
        public void OrderedTwoActorProcedureRestartsWithoutRestoringWear()
        {
            var sequence = FailureInGreenBand(out var boiler);
            float wornCondition = boiler.Condition;
            OpenAndIsolate(sequence);
            Assert.That(sequence.PanelOpen && sequence.BreakerIsolated, Is.True);
            SeatBothLatches(sequence);
            Assert.That(sequence.Step, Is.EqualTo(RepairStep.Restart));
            Assert.That(sequence.Press(RepairControlKind.Restart, 1).Success, Is.True);
            Assert.That(boiler.Failed, Is.False);
            Assert.That(boiler.ReliefActorId, Is.EqualTo(-1));
            Assert.That(boiler.Condition, Is.EqualTo(wornCondition));
            Assert.That(sequence.Step, Is.EqualTo(RepairStep.Complete));
            Assert.That(sequence.BreakerIsolated, Is.False, "Successful restart returns the breaker lever to RUN.");
            Assert.That(sequence.LatchAProgress, Is.EqualTo(1));
            Assert.That(sequence.LatchBProgress, Is.EqualTo(1));
        }

        [Test]
        public void OneActorCannotHoldReliefThenReplaceTheRequiredPartnerSequentially()
        {
            var sequence = FailureInGreenBand(out var boiler);
            Assert.That(sequence.Press(RepairControlKind.Panel, 0).Success, Is.False,
                "The relief operator cannot also perform the ordered procedure.");
            boiler.SetRelief(0, false);
            Assert.That(boiler.InRepairBand, Is.True, "The gauge briefly stays safe after release.");
            Assert.That(sequence.Press(RepairControlKind.Panel, 0).Success, Is.False,
                "A safe gauge alone is insufficient: a second live relief holder is required.");
            Assert.That(sequence.Step, Is.EqualTo(RepairStep.Panel));
            Assert.That(boiler.Failed, Is.True);
        }

        [Test]
        public void WrongOrderClosesAttemptAndCorrectReopeningRecovers()
        {
            var sequence = FailureInGreenBand(out var boiler);
            OpenAndIsolate(sequence);
            Assert.That(sequence.Press(RepairControlKind.LatchB, 1).Success, Is.False);
            Assert.That(sequence.Step, Is.EqualTo(RepairStep.Panel));
            Assert.That(sequence.PanelOpen, Is.False);
            Assert.That(sequence.BreakerIsolated, Is.False);
            Assert.That(sequence.Status, Does.Contain("Wrong order"));
            Assert.That(sequence.Press(RepairControlKind.Restart, 1).Success, Is.False);
            OpenAndIsolate(sequence);
            SeatBothLatches(sequence);
            Assert.That(sequence.Press(RepairControlKind.Restart, 1).Success, Is.True);
            Assert.That(boiler.Failed, Is.False);
        }

        [Test]
        public void APartiallyTurnedLatchSpringsBackButASeatedLatchStaysSeated()
        {
            var sequence = FailureInGreenBand(out _);
            OpenAndIsolate(sequence);
            sequence.Press(RepairControlKind.LatchA, 1);
            sequence.HoldLatch(RepairControlKind.LatchA, 1, 1.25f);
            Assert.That(sequence.LatchAProgress, Is.EqualTo(0.5f).Within(0.001f));
            sequence.ReleaseLatch(RepairControlKind.LatchA, 1);
            Assert.That(sequence.LatchAProgress, Is.Zero);
            Assert.That(sequence.Step, Is.EqualTo(RepairStep.LatchA));
            sequence.Press(RepairControlKind.LatchA, 1);
            sequence.HoldLatch(RepairControlKind.LatchA, 1, 2.5f);
            sequence.ReleaseLatch(RepairControlKind.LatchA, 1);
            Assert.That(sequence.LatchAProgress, Is.EqualTo(1));
            Assert.That(sequence.Step, Is.EqualTo(RepairStep.LatchB));
        }

        [Test]
        public void ReleasingReliefPreventsRestartAndPressureEscapeInvalidatesAllProgress()
        {
            var sequence = FailureInGreenBand(out var boiler);
            OpenAndIsolate(sequence);
            SeatBothLatches(sequence);
            float supportedPressure = boiler.Pressure;
            boiler.SetRelief(0, false);
            Assert.That(sequence.Press(RepairControlKind.Restart, 1).Success, Is.False);
            Assert.That(boiler.Failed, Is.True);
            boiler.Tick(4);
            sequence.Refresh();
            Assert.That(boiler.Pressure, Is.GreaterThan(supportedPressure));
            Assert.That(boiler.InRepairBand, Is.False);
            Assert.That(sequence.Step, Is.EqualTo(RepairStep.Panel));
            Assert.That(sequence.LatchAProgress, Is.Zero);
            Assert.That(sequence.LatchBProgress, Is.Zero);
            Assert.That(sequence.Status, Does.Contain("green band"));
            boiler.SetRelief(0, true);
            boiler.Tick(5);
            Assert.That(boiler.InRepairBand, Is.True);
            Assert.That(sequence.Press(RepairControlKind.Restart, 1).Success, Is.False,
                "Restoring pressure does not restore the discarded mechanical sequence.");
            OpenAndIsolate(sequence);
            SeatBothLatches(sequence);
            Assert.That(sequence.Press(RepairControlKind.Restart, 1).Success, Is.True);
        }

        [Test]
        public void ChangingRepairRolesRequiresANewPanelSequence()
        {
            var sequence = FailureInGreenBand(out var boiler);
            sequence.Press(RepairControlKind.Panel, 1);
            boiler.SetRelief(0, false);
            boiler.SetRelief(1, true);
            Assert.That(sequence.Press(RepairControlKind.Breaker, 0).Success, Is.False);
            Assert.That(sequence.Step, Is.EqualTo(RepairStep.Panel));
            Assert.That(sequence.Press(RepairControlKind.Panel, 0).Success, Is.True);
            Assert.That(sequence.OperatorActorId, Is.EqualTo(0));
        }

        [Test]
        public void NewFailureAndExplicitInterruptionCannotReuseCompletedProgress()
        {
            var sequence = FailureInGreenBand(out var boiler);
            OpenAndIsolate(sequence);
            SeatBothLatches(sequence);
            sequence.CancelAttempt();
            Assert.That(sequence.Step, Is.EqualTo(RepairStep.Panel));
            Assert.That(sequence.LatchAProgress, Is.Zero);
            OpenAndIsolate(sequence);
            SeatBothLatches(sequence);
            sequence.Press(RepairControlKind.Restart, 1);
            boiler.ForceFailure();
            sequence.Refresh();
            Assert.That(sequence.Step, Is.EqualTo(RepairStep.Panel));
            Assert.That(sequence.LatchBProgress, Is.Zero);
            Assert.That(sequence.OperatorActorId, Is.EqualTo(-1));
            Assert.That(sequence.Press(RepairControlKind.Restart, 1).Success, Is.False);
        }

        [Test]
        public void InvalidActorOrTimeCannotAdvanceLatchTravel()
        {
            var sequence = FailureInGreenBand(out _);
            Assert.That(sequence.Press(RepairControlKind.Panel, 9).Success, Is.False);
            OpenAndIsolate(sequence);
            sequence.Press(RepairControlKind.LatchA, 1);
            Assert.That(sequence.HoldLatch(RepairControlKind.LatchA, 0, 2.5f).Success, Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() => sequence.HoldLatch(RepairControlKind.LatchA, 1, float.NaN));
            Assert.That(sequence.LatchAProgress, Is.Zero);
        }
    }
}
