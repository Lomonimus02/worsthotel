using System;
using System.Linq;
using NUnit.Framework;

namespace WorstHotel.Tests
{
    public sealed class BoilerTests
    {
        private static float RunService(BoilerSystem boiler, float load)
        {
            boiler.SetLoad(load); boiler.BeginService();
            float firstFailure = -1;
            for (int i = 0; i < 1500; i++)
            {
                boiler.Tick(0.2f);
                if (boiler.Failed && firstFailure < 0) firstFailure = (i + 1) * 0.2f;
            }
            return firstFailure;
        }

        [Test]
        public void FourUnitGuestsOnHealthyBoilerLoseTwentyConditionWithoutForcedFailure()
        {
            var boiler = new BoilerSystem(new BoilerSettings());
            Assert.That(RunService(boiler, 4), Is.EqualTo(-1));
            Assert.That(boiler.Condition, Is.EqualTo(65).Within(0.03f));
            Assert.That(boiler.Failed, Is.False);
        }

        [Test]
        public void SixGuestDemandAcceleratesWearAndProducesFailureFromPressureExposure()
        {
            var boiler = new BoilerSystem(new BoilerSettings(initialCondition: 80));
            float failedAt = RunService(boiler, 6);
            Assert.That(boiler.Condition, Is.EqualTo(25.7143f).Within(0.05f));
            Assert.That(failedAt, Is.InRange(180, 225));
            Assert.That(boiler.HeatingOutput, Is.EqualTo(0.1f));
        }

        [Test]
        public void PreviousDaysWearCausesEarlierFailureWithTheSameNewBookings()
        {
            var worn = new BoilerSystem(new BoilerSettings(initialCondition: 46));
            var fresh = new BoilerSystem(new BoilerSettings(initialCondition: 95));
            float wornFailure = RunService(worn, 6);
            float freshFailure = RunService(fresh, 6);
            Assert.That(wornFailure, Is.InRange(20, 65));
            Assert.That(freshFailure, Is.InRange(260, 300));
            Assert.That(wornFailure, Is.LessThan(freshFailure));
        }

        [Test]
        public void FailureRequiresContinuousThresholdExposureAndIsNotAOneTickDiceRoll()
        {
            var boiler = new BoilerSystem(new BoilerSettings(initialCondition: 0));
            boiler.SetLoad(8);
            while (boiler.Pressure < 85) boiler.Tick(0.2f);
            Assert.That(boiler.Failed, Is.False);
            Assert.That(boiler.FailureExposure, Is.LessThan(1));
            boiler.SetLoad(0);
            for (int i = 0; i < 300; i++) boiler.Tick(0.2f);
            // Very poor condition can remain unsafe even at zero guest load; a healthy lower-load test proves reset below threshold.
            var healthy = new BoilerSystem(new BoilerSettings());
            healthy.SetLoad(12);
            while (healthy.Pressure < 85) healthy.Tick(0.2f);
            healthy.SetLoad(0);
            for (int i = 0; i < 100; i++) healthy.Tick(0.2f);
            Assert.That(healthy.FailureExposure, Is.Zero);
            Assert.That(healthy.Failed, Is.False);
        }

        [Test]
        public void LoadOverrideIsExplicitReversibleAndRejectsNonFiniteValues()
        {
            var boiler = new BoilerSystem(new BoilerSettings());
            boiler.SetLoad(3.75f);
            boiler.OverrideLoad(6);
            boiler.SetLoad(4);
            Assert.That(boiler.Load, Is.EqualTo(6));
            boiler.OverrideLoad(null);
            Assert.That(boiler.Load, Is.EqualTo(4));
            Assert.Throws<ArgumentOutOfRangeException>(() => boiler.OverrideLoad(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => boiler.SetCondition(float.PositiveInfinity));
        }

        [Test]
        public void RestartRequiresDistinctActorsLiveReliefAndSafePressureWithoutRestoringCondition()
        {
            var boiler = new BoilerSystem(new BoilerSettings());
            boiler.ForceFailure();
            Assert.That(boiler.Restart(1).Success, Is.False);
            Assert.That(boiler.SetRelief(0, true).Success, Is.True);
            boiler.Tick(10);
            Assert.That(boiler.InRepairBand, Is.True);
            Assert.That(boiler.Restart(0).Success, Is.False);
            boiler.SetRelief(0, false);
            boiler.Tick(5);
            Assert.That(boiler.InRepairBand, Is.False);
            Assert.That(boiler.Restart(1).Success, Is.False);
            boiler.SetRelief(0, true); boiler.Tick(3);
            float conditionBefore = boiler.Condition;
            Assert.That(boiler.Restart(1).Success, Is.True);
            Assert.That(boiler.Failed, Is.False);
            Assert.That(boiler.Pressure, Is.EqualTo(40));
            Assert.That(boiler.Condition, Is.EqualTo(conditionBefore));
        }

        [Test]
        public void BeginningAnotherServiceDoesNotSilentlyRepairPreviousWearOrFailure()
        {
            var boiler = new BoilerSystem(new BoilerSettings());
            boiler.SetCondition(23); boiler.ForceFailure(); boiler.Tick(2);
            float condition = boiler.Condition, pressure = boiler.Pressure;
            boiler.BeginService();
            Assert.That(boiler.Condition, Is.EqualTo(condition));
            Assert.That(boiler.Failed, Is.True);
            Assert.That(boiler.Pressure, Is.EqualTo(pressure));
        }
    }
}
