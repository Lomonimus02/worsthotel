using System;
using System.Linq;
using NUnit.Framework;

namespace WorstHotel.Tests
{
    public sealed class ContinuousCapacityTests
    {
        private static BoilerSystem Boiler(float condition = 85, float daySeconds = 720, bool wear = false)
        {
            var capacity = wear ? new BoilerCapacitySettings() :
                new BoilerCapacitySettings(runningWearPerHotelDay: 0, overloadWearPerHotelDay: 0, strainedWearPerHotelDay: 0);
            // Current production rating; pure BoilerSettings' historical default remains4.2.
            // These are controlled equipment loads, not a natural guest-schedule fixture.
            return new BoilerSystem(new BoilerSettings(initialCondition: condition, safeLoad: 4.6f, capacity: capacity), daySeconds);
        }

        private static void Run(BoilerSystem boiler, float duration, float step = 1)
        {
            for (float elapsed = 0; elapsed < duration;)
            {
                float dt = Math.Min(step, duration - elapsed);
                boiler.Tick(dt); elapsed += dt;
            }
        }

        [Test]
        public void SmallActualOverloadReducesOutputAndAccumulatesStressWithoutInstantFailure()
        {
            var boiler = Boiler();
            boiler.SetLoad(boiler.EffectiveCapacity * 1.05f);
            boiler.Tick(30);
            Assert.That(boiler.LoadRatio, Is.EqualTo(1.05f).Within(.00001f));
            Assert.That(boiler.Reserve, Is.LessThan(0));
            Assert.That(boiler.HeatingOutput, Is.EqualTo(.9f / 1.05f).Within(.00001f));
            Assert.That(boiler.Stress01, Is.EqualTo((.015f + .05f * .25f) * 1.075f).Within(.00001f),
                "One hotel hour includes full strain plus the small real overload, with condition85.");
            Assert.That(boiler.CapacityBand, Is.EqualTo(CapacityBand.Overloaded));
            Assert.That(boiler.Failed, Is.False);
        }

        [Test]
        public void BriefSevereSpikeSurvivesButSameSustainedDemandFailsExactlyOnce()
        {
            var boiler = Boiler();
            int failures = 0; boiler.OnFailureStarted += () => failures++;
            boiler.SetLoad(boiler.EffectiveCapacity * 2);
            boiler.Tick(1);
            Assert.That(boiler.Failed, Is.False);
            Run(boiler, 89); // 90 model seconds = 3 hotel hours; .284875 stress/hour gives .854625.
            Assert.That(boiler.Failed, Is.False);
            Assert.That(boiler.CapacityBand, Is.EqualTo(CapacityBand.Critical));
            Run(boiler, 16);
            Assert.That(boiler.Failed, Is.True);
            Assert.That(boiler.Stress01, Is.EqualTo(1));
            Assert.That(boiler.HeatingOutput, Is.EqualTo(.1f));
            Run(boiler, 200);
            Assert.That(failures, Is.EqualTo(1));
        }

        [Test]
        public void ReducedConsumptionRecoversStoredStressGraduallyAndBeginServiceCannotEraseIt()
        {
            var boiler = Boiler();
            boiler.SetLoad(boiler.EffectiveCapacity * 1.5f);
            Run(boiler, 30);
            float stored = boiler.Stress01, pressure = boiler.Pressure;
            boiler.BeginService();
            Assert.That(boiler.Stress01, Is.EqualTo(stored));
            Assert.That(boiler.Pressure, Is.EqualTo(pressure));
            boiler.SetLoad(boiler.EffectiveCapacity * .5f);
            Assert.That(boiler.Stress01, Is.EqualTo(stored), "Changing demand is not elapsed recovery time.");
            boiler.Tick(1);
            Assert.That(boiler.Stress01, Is.GreaterThan(0).And.LessThan(stored));
            Assert.That(boiler.FailureExposure, Is.Zero);
            Run(boiler, 120);
            Assert.That(boiler.Stress01, Is.Zero);
            Assert.That(boiler.Failed, Is.False);
        }

        [Test]
        public void LowConditionModeratelyReducesCapacityAndIncreasesStressForIdenticalActualLoad()
        {
            var fresh = Boiler(100); var worn = Boiler(50);
            fresh.SetLoad(4.5f); worn.SetLoad(4.5f);
            Assert.That(worn.EffectiveCapacity / worn.RatedCapacity, Is.EqualTo(.925f).Within(.00001f));
            Run(fresh, 60); Run(worn, 60);
            Assert.That(worn.Stress01, Is.GreaterThan(fresh.Stress01));
            Assert.That(worn.HeatingOutput, Is.LessThan(fresh.HeatingOutput));
            Assert.That(worn.HeatingOutput, Is.GreaterThan(.8f), "Condition must not also apply the old large output penalty.");
        }

        [Test]
        public void WearAndStressFollowConfiguredHotelTimeRatherThanUnscaledRuntimeSeconds()
        {
            var normal = Boiler(daySeconds: 720, wear: true);
            var slow = Boiler(daySeconds: 1440, wear: true);
            normal.SetLoad(4.6f); slow.SetLoad(4.6f);
            Run(normal, 120, 1); Run(slow, 240, 2);
            Assert.That(slow.Condition, Is.EqualTo(normal.Condition).Within(.0001f));
            Assert.That(slow.Stress01, Is.EqualTo(normal.Stress01).Within(.0001f));
            Assert.That(slow.HeatingOutput, Is.EqualTo(normal.HeatingOutput).Within(.0001f));
            Assert.That(slow.Failed, Is.EqualTo(normal.Failed));
        }

        [TestCase(2.766f)]
        [TestCase(3.419f)]
        public void CautiousMeasuredDemandRemainsSafeForThreeHotelDaysWithGradualWear(float demand)
        {
            // Documented typical 3/4-guest demand, held constant to isolate the equipment kernel.
            // Room and guest-schedule integration has separate actual-consumer tests.
            var boiler = Boiler(wear: true);
            boiler.SetLoad(demand); Run(boiler, 3 * 720);
            Assert.That(boiler.Failed, Is.False);
            Assert.That(boiler.Stress01, Is.Zero);
            Assert.That(boiler.Condition, Is.InRange(76, 85));
            Assert.That(boiler.HeatingOutput, Is.EqualTo(1));
            Assert.That(boiler.Reserve, Is.GreaterThan(0));
        }

        [Test]
        public void SustainedFiveVersusSixGuestDemandHasDifferentConsequencesWithoutCalendarTriggers()
        {
            var five = Boiler(wear: true); var six = Boiler(wear: true);
            five.SetLoad(4.241f); six.SetLoad(5.033f);
            Run(five, 720); Run(six, 720);
            Assert.That(five.Failed, Is.False);
            Assert.That(five.Stress01, Is.GreaterThan(0).And.LessThan(1));
            Assert.That(six.Failed, Is.True);
            Assert.That(six.Load, Is.EqualTo(5.033f));
        }

        [Test]
        public void PoorIdleBoilerDoesNotInventOperatingWearOrFailure()
        {
            var boiler = Boiler(0, wear: true);
            Run(boiler, 3 * 720);
            Assert.That(boiler.Condition, Is.Zero);
            Assert.That(boiler.Stress01, Is.Zero);
            Assert.That(boiler.Failed, Is.False);
            Assert.That(boiler.HeatingOutput, Is.EqualTo(1));
        }

        [Test]
        public void FailedBoilerStopsOperatingWearAndStillRequiresActualReliefAndDistinctStaffForRestart()
        {
            var boiler = Boiler(wear: true);
            boiler.SetLoad(8); Run(boiler, 180);
            Assert.That(boiler.Failed, Is.True);
            float condition = boiler.Condition;
            Run(boiler, 100);
            Assert.That(boiler.Condition, Is.EqualTo(condition));
            Assert.That(boiler.Restart(1).Success, Is.False);
            Assert.That(boiler.SetRelief(0, true).Success, Is.True);
            boiler.Tick(20);
            Assert.That(boiler.InRepairBand, Is.True);
            Assert.That(boiler.Restart(0).Success, Is.False);
            Assert.That(boiler.Restart(1).Success, Is.True);
            Assert.That(boiler.Condition, Is.EqualTo(condition));
            Assert.That(boiler.Stress01, Is.Zero);
            boiler.Tick(1);
            Assert.That(boiler.Failed, Is.False, "The successful physical restart must allow a real operating interval.");
            Assert.That(boiler.Stress01, Is.GreaterThan(0));
        }

        [Test]
        public void ExistingPaidMaintenanceClearsStressAlongWithMechanicalCondition()
        {
            var boiler = Boiler(); boiler.SetLoad(6); Run(boiler, 30);
            Assert.That(boiler.Stress01, Is.GreaterThan(0));
            boiler.ApplyPaidMaintenance(95);
            Assert.That(boiler.Stress01, Is.Zero);
            Assert.That(boiler.Condition, Is.EqualTo(95));
        }

        [Test]
        public void WearIntegrationHasBoundedSubdivisionErrorAndStressOnlyIntegrationIsExact()
        {
            foreach (bool wear in new[] { false, true })
            {
                var whole = Boiler(wear: wear); var split = Boiler(wear: wear);
                whole.SetLoad(5); split.SetLoad(5);
                whole.Tick(60); Run(split, 60);
                Assert.That(split.Condition, Is.EqualTo(whole.Condition).Within(wear ? .02f : .0001f));
                Assert.That(split.Stress01, Is.EqualTo(whole.Stress01).Within(wear ? .002f : .0001f));
                Assert.That(split.Failed, Is.EqualTo(whole.Failed));
            }
        }

        [Test]
        public void CapacityBandsTrackActualUtilizationAndExtremeFiniteDemandDoesNotProduceNan()
        {
            var boiler = Boiler();
            Assert.That(boiler.CapacityBand, Is.EqualTo(CapacityBand.Comfortable));
            boiler.SetLoad(boiler.EffectiveCapacity * .9f);
            Assert.That(boiler.CapacityBand, Is.EqualTo(CapacityBand.Strained));
            boiler.SetLoad(float.MaxValue); boiler.Tick(1);
            Assert.That(float.IsNaN(boiler.LoadRatio) || float.IsInfinity(boiler.LoadRatio), Is.False);
            Assert.That(float.IsNaN(boiler.Pressure) || float.IsInfinity(boiler.Pressure), Is.False);
            Assert.That(boiler.Stress01, Is.InRange(0, 1));
            Assert.That(boiler.HeatingOutput, Is.InRange(0, 1));
            Assert.That(boiler.CapacityBand, Is.EqualTo(CapacityBand.Critical));
        }

        [Test]
        public void InvalidCapacityTuningAndOperatingClockScaleAreRejected()
        {
            Assert.Throws<ArgumentException>(() => new BoilerCapacitySettings(conditionCapacityFloor: 0));
            Assert.Throws<ArgumentException>(() => new BoilerCapacitySettings(strainedLoadRatio: 1));
            Assert.Throws<ArgumentException>(() => new BoilerCapacitySettings(criticalStress: float.NaN));
            Assert.Throws<ArgumentException>(() => new BoilerCapacitySettings(stressGainPerHotelHour: -1));
            Assert.Throws<ArgumentException>(() => new BoilerCapacitySettings(runningWearPerHotelDay: float.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => Boiler(daySeconds: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Boiler(daySeconds: float.NaN));
            Assert.That(new BoilerSystem(new BoilerSettings()).CapacityModelEnabled, Is.False);
        }

        private sealed class CircuitFixture
        {
            public readonly RoomState[] Rooms = Enumerable.Range(101, 6).Select(id => new RoomState(new RoomProfile(id, "Room " + id))).ToArray();
            public readonly HeaterSystem Heaters;
            public readonly ElectricalSystem Electrical;
            public ElectricalCircuit Circuit => Electrical.Find("A");
            public CircuitFixture(bool continuous = true, float heaterLoad = 2)
            {
                int[] ids = Rooms.Select(room => room.Profile.Id).ToArray();
                Heaters = new HeaterSystem(new HeaterSettings(electricalLoad: heaterLoad), ids);
                Electrical = new ElectricalSystem(new ElectricitySettings(circuitCapacity: 3), ids, continuous);
                foreach (string id in new[] { "one", "two" })
                {
                    Assert.That(Heaters.Register(id).Success, Is.True);
                    Assert.That(Heaters.AssignRoom(id, 101).Success, Is.True);
                    Assert.That(Heaters.SetSwitchedOn(id, true).Success, Is.True);
                }
                Tick(0);
            }
            public void Tick(float dt) => Electrical.Tick(Array.Empty<GuestStay>(), Rooms, Heaters, dt);
        }

        [Test]
        public void RealHeaterReductionRecoversCircuitStressGraduallyWithoutErasingConsumerIdentity()
        {
            var fixture = new CircuitFixture(); fixture.Tick(6);
            Assert.That(fixture.Circuit.Warning, Is.True);
            Assert.That(fixture.Circuit.OverloadSeconds, Is.EqualTo(6));
            fixture.Heaters.SetSwitchedOn("two", false); fixture.Tick(0);
            Assert.That(fixture.Circuit.OverloadSeconds, Is.EqualTo(6));
            fixture.Tick(1);
            Assert.That(fixture.Circuit.OverloadSeconds, Is.EqualTo(5));
            Assert.That(fixture.Circuit.Stress01, Is.EqualTo(5f / 18).Within(.00001f));
            Assert.That(fixture.Circuit.RequestedLoad, Is.EqualTo(2));
            Assert.That(fixture.Circuit.Reserve, Is.EqualTo(1));
            Assert.That(fixture.Electrical.Consumers.Select(item => item.Id), Is.EqualTo(new[] { "heater:one", "heater:two" }));
            fixture.Tick(10);
            Assert.That(fixture.Circuit.Stress01, Is.Zero);
            Assert.That(fixture.Circuit.Tripped, Is.False);
        }

        [Test]
        public void RepeatedOverloadRetainsResidualStressAndResetRetripsUntilActualHeaterIsSwitchedOff()
        {
            var fixture = new CircuitFixture(); fixture.Tick(10);
            fixture.Heaters.SetSwitchedOn("two", false); fixture.Tick(2);
            fixture.Heaters.SetSwitchedOn("two", true); fixture.Tick(9);
            Assert.That(fixture.Circuit.Tripped, Is.False);
            Assert.That(fixture.Circuit.CapacityBand, Is.EqualTo(CapacityBand.Critical));
            fixture.Tick(1);
            Assert.That(fixture.Circuit.Tripped, Is.True);
            Assert.That(fixture.Circuit.ActualRequestedLoad, Is.EqualTo(4));
            Assert.That(fixture.Circuit.ActualDeliveredLoad, Is.Zero);
            Assert.That(fixture.Heaters.Find("one").EffectiveHeatOutput, Is.Zero);
            Assert.That(fixture.Electrical.ResetCircuit(0, "A").Success, Is.True);
            fixture.Tick(18);
            Assert.That(fixture.Circuit.TripCount, Is.EqualTo(2));
            fixture.Heaters.SetSwitchedOn("two", false);
            fixture.Tick(0);
            Assert.That(fixture.Electrical.ResetCircuit(1, "A").Success, Is.True);
            fixture.Tick(60);
            Assert.That(fixture.Circuit.Tripped, Is.False);
            Assert.That(fixture.Heaters.Find("one").EffectiveHeatOutput, Is.GreaterThan(0));
        }

        [Test]
        public void CircuitBandUsesCapacityWhileOtherBranchRemainsUnaffected()
        {
            var fixture = new CircuitFixture(heaterLoad: 2.7f);
            fixture.Heaters.SetSwitchedOn("two", false); fixture.Tick(0);
            Assert.That(fixture.Circuit.RatedCapacity, Is.EqualTo(3));
            Assert.That(fixture.Circuit.LoadRatio, Is.EqualTo(.9f).Within(.00001f));
            Assert.That(fixture.Circuit.CapacityBand, Is.EqualTo(CapacityBand.Strained));
            Assert.That(fixture.Electrical.Find("B").LoadRatio, Is.Zero);
            Assert.That(fixture.Electrical.Find("B").CapacityBand, Is.EqualTo(CapacityBand.Comfortable));
        }

        [Test]
        public void DiagnosticLoadChangeDoesNotEraseStoredStressOrFabricateConsumers()
        {
            var fixture = new CircuitFixture(); fixture.Tick(8);
            Assert.That(fixture.Electrical.DebugOverrideLoad("A", 0).Success, Is.True);
            fixture.Tick(0);
            Assert.That(fixture.Circuit.OverloadSeconds, Is.EqualTo(8));
            Assert.That(fixture.Circuit.ActualRequestedLoad, Is.EqualTo(4));
            Assert.That(fixture.Electrical.Consumers.Sum(item => item.RequestedLoad), Is.EqualTo(4));
            fixture.Tick(3);
            Assert.That(fixture.Circuit.OverloadSeconds, Is.EqualTo(5));
            fixture.Electrical.DebugOverrideLoad("A", null); fixture.Tick(0);
            Assert.That(fixture.Circuit.OverloadSeconds, Is.EqualTo(5));
            Assert.That(fixture.Circuit.RequestedLoad, Is.EqualTo(4));
        }

        [Test]
        public void LegacyCircuitStillClearsOverloadImmediatelyAndNewSettingsAreValidated()
        {
            var fixture = new CircuitFixture(continuous: false); fixture.Tick(8);
            fixture.Heaters.SetSwitchedOn("two", false); fixture.Tick(0);
            Assert.That(fixture.Circuit.OverloadSeconds, Is.Zero);
            Assert.That(fixture.Electrical.ContinuousStress, Is.False);
            Assert.Throws<ArgumentException>(() => new ElectricitySettings(stressRecoveryPerSecond: -1));
            Assert.Throws<ArgumentException>(() => new ElectricitySettings(strainedLoadRatio: 1));
            Assert.Throws<ArgumentException>(() => new ElectricitySettings(criticalStress: 0));
        }
    }
}
