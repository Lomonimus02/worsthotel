using System;
using NUnit.Framework;

namespace WorstHotel.Tests
{
    public sealed class SoloRepairTests
    {
        static BoilerSystem FailedSolo(out RepairSequence sequence, float seconds = 18)
        {
            var settings = new BoilerSettings(baseWearPerMinute: 0, overloadWearPerMinute: 0);
            var boiler = new BoilerSystem(settings);
            boiler.ConfigureSoloAssist(new SoloAssistSettings(2, seconds));
            boiler.ForceFailure(); sequence = new RepairSequence(boiler, settings);
            return boiler;
        }

        static void Secure(BoilerSystem boiler)
        {
            Assert.That(boiler.SetRelief(0, true).Success, Is.True);
            boiler.Tick(11);
            Assert.That(boiler.InRepairBand, Is.True);
            Assert.That(boiler.HoldSoloValve(0, 2).Success, Is.True);
            boiler.SetRelief(0, false);
            Assert.That(boiler.SoloValveLatched, Is.True);
            Assert.That(boiler.ReliefActorId, Is.EqualTo(-1), "A mechanism is not a synthetic second staff member.");
        }

        static void Seat(RepairSequence sequence)
        {
            Assert.That(sequence.Press(RepairControlKind.Panel, 0).Success, Is.True);
            Assert.That(sequence.Press(RepairControlKind.Breaker, 0).Success, Is.True);
            Assert.That(sequence.Press(RepairControlKind.LatchA, 0).Success, Is.True);
            Assert.That(sequence.HoldLatch(RepairControlKind.LatchA, 0, 2.5f).Success, Is.True);
            Assert.That(sequence.Press(RepairControlKind.LatchB, 0).Success, Is.True);
            Assert.That(sequence.HoldLatch(RepairControlKind.LatchB, 0, 2.5f).Success, Is.True);
        }

        [Test]
        public void SoloCatchStillRequiresSafePressureARealHoldAndCompleteMechanicalSequence()
        {
            var boiler = FailedSolo(out var sequence);
            Assert.That(boiler.HoldSoloValve(0, 2).Success, Is.False);
            boiler.SetRelief(0, true);
            Assert.That(boiler.HoldSoloValve(0, 2).Success, Is.False, "Unsafe pressure cannot be secured.");
            Assert.That(boiler.SoloValveLatched, Is.False);
            boiler.Tick(11); boiler.HoldSoloValve(0, 1);
            Assert.That(boiler.SoloValveHoldProgress, Is.EqualTo(.5f));
            boiler.SetRelief(0, false);
            Assert.That(boiler.SoloValveHoldProgress, Is.Zero, "Incomplete catch charging cancels on release.");
            Assert.That(sequence.Press(RepairControlKind.Panel, 0).Success, Is.False);
            Secure(boiler);
            float wear = boiler.Condition;
            Assert.That(sequence.Press(RepairControlKind.Restart, 0).Success, Is.False, "No one-button repair bypass.");
            Seat(sequence);
            Assert.That(sequence.Press(RepairControlKind.Restart, 0).Success, Is.True);
            Assert.That(boiler.Failed, Is.False);
            Assert.That(boiler.Condition, Is.EqualTo(wear));
            Assert.That(boiler.SoloLatchSecondsRemaining, Is.Zero);
        }

        [Test]
        public void ExpiredCatchDiscardsProgressEvenWhileGaugeIsStillGreen()
        {
            var boiler = FailedSolo(out var sequence);
            Secure(boiler); Seat(sequence);
            boiler.AdvanceSoloLatch(18);
            Assert.That(boiler.InRepairBand, Is.True);
            sequence.Refresh();
            Assert.That(sequence.Step, Is.EqualTo(RepairStep.Panel));
            Assert.That(sequence.LatchAProgress, Is.Zero);
            Assert.That(sequence.Press(RepairControlKind.Restart, 0).Success, Is.False);
            boiler.Tick(4);
            Assert.That(boiler.InRepairBand, Is.False, "Released mechanism allows actual failed pressure to rise.");
        }

        [Test]
        public void HotelClockAccelerationCannotConsumeOrExtendThePhysicalCatchWindow()
        {
            var boiler = FailedSolo(out _);
            Secure(boiler);
            boiler.Tick(40);
            Assert.That(boiler.SoloLatchSecondsRemaining, Is.EqualTo(18));
            Assert.That(boiler.InRepairBand, Is.True);
            boiler.AdvanceSoloLatch(3);
            Assert.That(boiler.SoloLatchSecondsRemaining, Is.EqualTo(15));
        }

        [Test]
        public void ContinuingToHoldCannotRefreshAnArmedOrExpiredCatchUntilRelease()
        {
            var boiler = FailedSolo(out _);
            boiler.SetRelief(0, true); boiler.Tick(11); boiler.HoldSoloValve(0, 2);
            boiler.AdvanceSoloLatch(17); boiler.HoldSoloValve(0, 10);
            Assert.That(boiler.SoloLatchSecondsRemaining, Is.EqualTo(1));
            boiler.AdvanceSoloLatch(1); boiler.HoldSoloValve(0, 10);
            Assert.That(boiler.SoloValveLatched, Is.False);
            boiler.SetRelief(0, false); boiler.SetRelief(0, true); boiler.HoldSoloValve(0, 2);
            Assert.That(boiler.SoloValveLatched, Is.True);
        }

        [Test]
        public void MultiplayerCannotUseSoloCatchAndChangingModeClearsIt()
        {
            var boiler = FailedSolo(out var sequence);
            Secure(boiler);
            boiler.ConfigureSoloAssist(null);
            Assert.That(boiler.SoloValveLatched, Is.False);
            boiler.SetRelief(0, true);
            Assert.That(boiler.HoldSoloValve(0, 100).Success, Is.False);
            Assert.That(sequence.Press(RepairControlKind.Panel, 0).Success, Is.False);
            Assert.That(sequence.Press(RepairControlKind.Panel, 1).Success, Is.True);
        }

        [Test]
        public void CancelMaintenanceAndNewServiceCannotReuseCatch()
        {
            var boiler = FailedSolo(out _); Secure(boiler);
            boiler.CancelSoloLatch(); Assert.That(boiler.SoloValveLatched, Is.False);
            Secure(boiler); boiler.ApplyPaidMaintenance(80);
            Assert.That(boiler.SoloLatchSecondsRemaining, Is.Zero);
            boiler.ForceFailure(); Secure(boiler); boiler.BeginService();
            Assert.That(boiler.SoloLatchSecondsRemaining, Is.Zero);
        }

        [Test]
        public void SoloConfigurationAndMechanismRejectNonFiniteTimes()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SoloAssistSettings(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SoloAssistSettings(2, 0));
            var boiler = FailedSolo(out _);
            Assert.Throws<ArgumentOutOfRangeException>(() => boiler.HoldSoloValve(0, float.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => boiler.AdvanceSoloLatch(-1));
        }
    }
}
