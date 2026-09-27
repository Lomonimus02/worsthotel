using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace WorstHotel.Tests
{
    /// <summary>Controlled equipment loads verify the pre-failure curve; natural room consumers have separate tests.</summary>
    public sealed class PreFailureCapacityTests
    {
        static BoilerSystem FixedCondition(float condition = 100) => new BoilerSystem(
            new BoilerSettings(initialCondition: condition, safeLoad: 4.6f, capacity: new BoilerCapacitySettings(
                runningWearPerHotelDay: 0, overloadWearPerHotelDay: 0, strainedWearPerHotelDay: 0)), 720);

        [Test]
        public void AppendedBusyValueUsesSemanticSeverityAndCannotBecomeAnAlarmByItsIntegerValue()
        {
            Assert.That((int)CapacityBand.Strained, Is.EqualTo(1));
            Assert.That((int)CapacityBand.Overloaded, Is.EqualTo(2));
            Assert.That((int)CapacityBand.Critical, Is.EqualTo(3));
            Assert.That((int)CapacityBand.Busy, Is.EqualTo(4));
            var order = new[] { CapacityBand.Comfortable, CapacityBand.Busy, CapacityBand.Strained,
                CapacityBand.Overloaded, CapacityBand.Critical };
            for (int band = 0; band < order.Length; band++)
                for (int threshold = 0; threshold < order.Length; threshold++)
                    Assert.That(CapacityBands.AtLeast(order[band], order[threshold]), Is.EqualTo(band >= threshold),
                        order[band] + " compared with " + order[threshold]);
            Assert.Throws<ArgumentOutOfRangeException>(() => CapacityBands.AtLeast((CapacityBand)99, CapacityBand.Busy));
        }

        [TestCase(.6999f, CapacityBand.Comfortable)]
        [TestCase(.70f, CapacityBand.Busy)]
        [TestCase(.8499f, CapacityBand.Busy)]
        [TestCase(.85f, CapacityBand.Strained)]
        [TestCase(1f, CapacityBand.Strained)]
        [TestCase(1.0001f, CapacityBand.Overloaded)]
        public void LoadOnlyClassificationHasOrderedBoundariesWithoutInventingStoredCriticalStress(float ratio, CapacityBand band)
        {
            Assert.That(CapacityBands.ForLoad(ratio, .70f, .85f), Is.EqualTo(band));
            Assert.That(CapacityBands.ForLoad(float.MaxValue, .70f, .85f), Is.EqualTo(CapacityBand.Overloaded));
        }

        [Test]
        public void SustainedSubcapacityStrainLosesHeatButCannotFailFromStoredStressAlone()
        {
            var boiler = FixedCondition();
            boiler.SetLoad(boiler.EffectiveCapacity * .925f);
            Assert.That(boiler.CapacityBand, Is.EqualTo(CapacityBand.Strained));
            Assert.That(boiler.HeatingOutput, Is.EqualTo(.95f).Within(.00001f));
            boiler.Tick(30); // One hotel hour at half strain, condition held constant by this fixture.
            Assert.That(boiler.Stress01, Is.EqualTo(.0075f).Within(.00001f));
            Assert.That(boiler.FailureExposure, Is.Zero);
            // Deliberately long constant subcapacity setup tests failure eligibility, not a natural stay.
            boiler.Tick(4200);
            Assert.That(boiler.Stress01, Is.EqualTo(1));
            Assert.That(boiler.CapacityBand, Is.EqualTo(CapacityBand.Critical));
            Assert.That(boiler.Failed, Is.False, "A stored warning alone cannot bypass the actual overload gate.");
            Assert.That(boiler.HeatingOutput, Is.EqualTo(.95f).Within(.00001f));
            boiler.SetLoad(boiler.EffectiveCapacity * .75f);
            Assert.That(boiler.HeatingOutput, Is.EqualTo(1));
            Assert.That(boiler.Stress01, Is.EqualTo(1), "Changing a valve/load is not an instant stress reset.");
            boiler.Tick(30);
            Assert.That(boiler.Stress01, Is.EqualTo(1 - .25f * (.85f - .75f) / .85f).Within(.00001f));
            Assert.That(boiler.CapacityBand, Is.EqualTo(CapacityBand.Critical), "Stored stress remains visible while recovering.");
            boiler.Tick(1200);
            Assert.That(boiler.Stress01, Is.Zero);
            Assert.That(boiler.CapacityBand, Is.EqualTo(CapacityBand.Busy));
            Assert.That(boiler.Failed, Is.False);
        }

        [Test]
        public void RecoveryAndStrainJoinContinuouslyAtTheThresholdWithoutAHiddenSafeLoadReset()
        {
            var below = FixedCondition(); var at = FixedCondition(); var above = FixedCondition();
            foreach (var boiler in new[] { below, at, above })
            {
                boiler.SetLoad(boiler.EffectiveCapacity * 2);
                boiler.Tick(30); // Real overload integration establishes the same .265 stored stress.
            }
            float stored = at.Stress01;
            below.SetLoad(below.EffectiveCapacity * .8499f);
            at.SetLoad(at.EffectiveCapacity * .85f);
            above.SetLoad(above.EffectiveCapacity * .8501f);
            foreach (var boiler in new[] { below, at, above })
            {
                Assert.That(boiler.Stress01, Is.EqualTo(stored));
                boiler.Tick(30);
                Assert.That(boiler.Failed, Is.False);
            }
            Assert.That(below.Stress01, Is.LessThan(stored).And.GreaterThan(stored - .0001f));
            Assert.That(at.Stress01, Is.EqualTo(stored).Within(.000001f));
            Assert.That(above.Stress01, Is.GreaterThan(stored).And.LessThan(stored + .0001f));
            Assert.That(below.HeatingOutput, Is.EqualTo(1));
            Assert.That(at.HeatingOutput, Is.EqualTo(1).Within(.000001f));
            Assert.That(above.HeatingOutput, Is.LessThan(1).And.GreaterThan(.9998f));
        }

        [Test]
        public void ConfiguredStrainWearAddsGraduallyBeforeOverloadWithoutAnotherConditionMultiplier()
        {
            var tuning = new BoilerCapacitySettings(runningWearPerHotelDay: 0, overloadWearPerHotelDay: 0,
                strainedWearPerHotelDay: 3);
            var busy = new BoilerSystem(new BoilerSettings(initialCondition: 100, safeLoad: 4.6f, capacity: tuning), 720);
            var strained = new BoilerSystem(new BoilerSettings(initialCondition: 100, safeLoad: 4.6f, capacity: tuning), 720);
            busy.SetLoad(busy.EffectiveCapacity * .75f);
            strained.SetLoad(strained.EffectiveCapacity * .925f);
            busy.Tick(30); strained.Tick(30);
            Assert.That(busy.Condition, Is.EqualTo(100));
            Assert.That(strained.Condition, Is.EqualTo(100 - 1.5f / 24).Within(.00001f),
                "Half strain costs half the configured3 points/day, integrated over one hotel hour.");
            Assert.That(strained.LoadRatio, Is.LessThan(1));
            Assert.That(strained.Failed, Is.False);
        }

        [Test]
        public void LegacyKernelIgnoresContinuousStrainSettingsAndKeepsItsHistoricalHeatAndWear()
        {
            var baseline = new BoilerSystem(new BoilerSettings());
            var alternative = new BoilerSystem(new BoilerSettings(capacity: new BoilerCapacitySettings(
                busyLoadRatio: .20f, maximumStrainedHeatLoss: 1, strainedStressGainPerHotelHour: 50,
                strainedWearPerHotelDay: 100)));
            baseline.SetLoad(5); alternative.SetLoad(5);
            baseline.Tick(5); alternative.Tick(5);
            Assert.That(alternative.CapacityModelEnabled, Is.False);
            Assert.That(alternative.Condition, Is.EqualTo(baseline.Condition));
            Assert.That(alternative.Pressure, Is.EqualTo(baseline.Pressure));
            Assert.That(alternative.HeatingOutput, Is.EqualTo(baseline.HeatingOutput));
            Assert.That(alternative.Failed, Is.EqualTo(baseline.Failed));
            Assert.That(alternative.Stress01, Is.Zero);
        }

        [Test]
        public void ProductionConfigurationCarriesTheExplicitTrialAndCopiesNewSettingsImmutably()
        {
            var asset = AssetDatabase.LoadAssetAtPath<BoilerConfig>("Assets/_WorstHotel/ScriptableObjects/Boiler.asset");
            Assert.That(asset, Is.Not.Null);
            var config = UnityEngine.Object.Instantiate(asset);
            try
            {
                var data = config.ToData();
                Assert.That(data.SafeLoad, Is.EqualTo(4.7f));
                Assert.That(data.Capacity.BusyLoadRatio, Is.EqualTo(.70f));
                Assert.That(data.Capacity.MaximumStrainedHeatLoss, Is.EqualTo(.10f));
                Assert.That(data.Capacity.StrainedStressGainPerHotelHour, Is.EqualTo(.015f));
                Assert.That(data.Capacity.StrainedWearPerHotelDay, Is.EqualTo(3));
                config.busyLoadRatio = .65f; config.maximumStrainedHeatLoss = .08f;
                config.strainedStressGainPerHotelHour = .02f; config.strainedWearPerHotelDay = 2;
                var changed = config.ToData();
                Assert.That(changed.Capacity.BusyLoadRatio, Is.EqualTo(.65f));
                Assert.That(changed.Capacity.MaximumStrainedHeatLoss, Is.EqualTo(.08f));
                Assert.That(changed.Capacity.StrainedStressGainPerHotelHour, Is.EqualTo(.02f));
                Assert.That(changed.Capacity.StrainedWearPerHotelDay, Is.EqualTo(2));
                Assert.That(data.Capacity.MaximumStrainedHeatLoss, Is.EqualTo(.10f));
                Assert.That(asset.maximumStrainedHeatLoss, Is.EqualTo(.10f));
            }
            finally { UnityEngine.Object.DestroyImmediate(config); }
        }

        [Test]
        public void InvalidNewSettingsAndClassificationInputsAreRejected()
        {
            Assert.Throws<ArgumentException>(() => new BoilerCapacitySettings(busyLoadRatio: 0));
            Assert.Throws<ArgumentException>(() => new BoilerCapacitySettings(busyLoadRatio: .85f));
            Assert.Throws<ArgumentException>(() => new BoilerCapacitySettings(maximumStrainedHeatLoss: 1.01f));
            Assert.Throws<ArgumentException>(() => new BoilerCapacitySettings(strainedStressGainPerHotelHour: float.NaN));
            Assert.Throws<ArgumentException>(() => new BoilerCapacitySettings(strainedWearPerHotelDay: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => CapacityBands.ForLoad(float.PositiveInfinity, .7f, .85f));
            Assert.Throws<ArgumentException>(() => CapacityBands.ForLoad(.5f, .85f, .85f));
        }
    }
}
