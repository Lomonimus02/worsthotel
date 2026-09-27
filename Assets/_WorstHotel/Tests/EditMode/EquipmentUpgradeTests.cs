using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace WorstHotel.Tests
{
    public sealed class EquipmentUpgradeTests
    {
        // Labelled subsystem adapters call internal equipment commits only. Integration and
        // physical tests exercise the paid authoritative simulation/session commands instead.
        static T Invoke<T>(object target, string method, params object[] args) =>
            (T)target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
        static CommandResult Install(BoilerSystem boiler) => Invoke<CommandResult>(boiler, "InstallCapacityUpgrade");
        static CommandResult Install(ElectricalSystem electrical, string circuit) =>
            Invoke<CommandResult>(electrical, "InstallCapacityUpgrade", circuit);
        static BoilerSystem Boiler(float multiplier = 1.25f) => new BoilerSystem(new BoilerSettings(capacity:
            new BoilerCapacitySettings(runningWearPerHotelDay: 0, overloadWearPerHotelDay: 0, capacityUpgradeMultiplier: multiplier)), 720);
        static void PreparePatch(BoilerSystem boiler)
        {
            boiler.ForceFailure(); Assert.That(boiler.SetRelief(0, true).Success, Is.True);
            boiler.Tick(20);
            Assert.That(Invoke<CommandResult>(boiler, "ApplyEmergencyPatch", 1).Success, Is.True);
        }

        [Test]
        public void IdenticalDemandGetsMoreCapacityAndHeatWithoutResettingStoredStressOrWear()
        {
            var boiler = Boiler(); boiler.SetLoad(5); boiler.Tick(60);
            float condition = boiler.Condition, stress = boiler.Stress01, pressure = boiler.Pressure;
            float output = boiler.HeatingOutput, capacity = boiler.EffectiveCapacity;
            Assert.That(stress, Is.GreaterThan(0));
            int observations = 0;
            boiler.OnHeatingOutputChanged += _ =>
            {
                observations++;
                Assert.That(boiler.CapacityUpgradePurchased, Is.True);
                Assert.That(boiler.RatedCapacity, Is.EqualTo(5.25f).Within(.00001f));
                Assert.That(boiler.Condition, Is.EqualTo(condition));
                Assert.That(boiler.Stress01, Is.EqualTo(stress));
                Assert.That(boiler.Pressure, Is.EqualTo(pressure));
            };
            Assert.That(Invoke<CommandResult>(boiler, "CanInstallCapacityUpgrade").Success, Is.True);
            Assert.That(boiler.CapacityUpgradePurchased, Is.False, "The pre-payment guard is pure.");
            Assert.That(Install(boiler).Success, Is.True);
            Assert.That(observations, Is.EqualTo(1));
            Assert.That(boiler.EffectiveCapacity, Is.EqualTo(capacity * 1.25f).Within(.00001f));
            Assert.That(boiler.Load, Is.EqualTo(5));
            Assert.That(boiler.LoadRatio, Is.LessThan(1));
            Assert.That(boiler.HeatingOutput, Is.GreaterThan(output));
            boiler.Tick(1);
            Assert.That(boiler.Stress01, Is.LessThan(stress).And.GreaterThan(0), "Only elapsed safe operation recovers stress.");
        }

        [Test]
        public void SameLoadProducesLessFutureStressButGreaterLoadCanStillFailTheUpgradedBoiler()
        {
            var baseline = Boiler(); var upgraded = Boiler();
            Assert.That(Install(upgraded).Success, Is.True);
            baseline.SetLoad(6); upgraded.SetLoad(6);
            baseline.Tick(30); upgraded.Tick(30);
            Assert.That(upgraded.Stress01, Is.GreaterThan(0).And.LessThan(baseline.Stress01));
            Assert.That(upgraded.LoadRatio, Is.GreaterThan(1));
            for (int i = 0; i < 720 && !upgraded.Failed; i++) upgraded.Tick(1);
            Assert.That(upgraded.Failed, Is.True, "A finite capacity improvement does not make demand unlimited.");
            Assert.That(upgraded.CapacityUpgradePurchased, Is.True);
        }

        [Test]
        public void BoilerUpgradeRetainsCurrentFailureAndUnservicedPatchWithoutEmittingRecovery()
        {
            var boiler = Boiler(); PreparePatch(boiler); boiler.SetLoad(5); boiler.ForceFailure();
            float condition = boiler.Condition, stress = boiler.Stress01, pressure = boiler.Pressure, output = boiler.HeatingOutput;
            int recovered = 0; boiler.OnFailureResolved += () => recovered++;
            Assert.That(Install(boiler).Success, Is.True);
            Assert.That(boiler.Failed, Is.True);
            Assert.That(boiler.EmergencyPatchActive, Is.True);
            Assert.That(boiler.Condition, Is.EqualTo(condition));
            Assert.That(boiler.Stress01, Is.EqualTo(stress));
            Assert.That(boiler.Pressure, Is.EqualTo(pressure));
            Assert.That(boiler.HeatingOutput, Is.EqualTo(output));
            Assert.That(recovered, Is.Zero);
        }

        [Test]
        public void UpgradeDuringMaintenancePreservesDeadlineAndStaysInstalledAfterCompletion()
        {
            var boiler = Boiler(); PreparePatch(boiler);
            Assert.That(Invoke<CommandResult>(boiler, "BeginMaintenance", 100f).Success, Is.True);
            float deadline = boiler.MaintenanceEndsAt, condition = boiler.Condition, stress = boiler.Stress01;
            Assert.That(Install(boiler).Success, Is.True);
            Assert.That(boiler.MaintenanceEndsAt, Is.EqualTo(deadline));
            Assert.That(boiler.HeatingOutput, Is.Zero);
            Assert.That(boiler.Condition, Is.EqualTo(condition));
            Assert.That(boiler.Stress01, Is.EqualTo(stress));
            Assert.That(boiler.EmergencyPatchActive, Is.True);
            Assert.That(Invoke<bool>(boiler, "CompleteMaintenance", deadline), Is.True);
            Assert.That(boiler.CapacityUpgradePurchased, Is.True);
            Assert.That(boiler.RatedCapacity, Is.EqualTo(5.25f).Within(.00001f));
            Assert.That(boiler.Condition, Is.EqualTo(95));
        }

        [Test]
        public void RepeatedBoilerPurchaseDoesNotCompoundConfiguredCapacity()
        {
            var boiler = Boiler(1.5f);
            Assert.That(Install(boiler).Success, Is.True);
            Assert.That(boiler.RatedCapacity, Is.EqualTo(6.3f).Within(.00001f));
            Assert.That(Invoke<CommandResult>(boiler, "CanInstallCapacityUpgrade").Success, Is.False);
            Assert.That(Install(boiler).Success, Is.False);
            Assert.That(boiler.RatedCapacity, Is.EqualTo(6.3f).Within(.00001f));
            boiler.BeginService();
            Assert.That(boiler.CapacityUpgradePurchased, Is.True);
        }

        sealed class CircuitFixture
        {
            public readonly RoomState[] Rooms = Enumerable.Range(101, 6).Select(id => new RoomState(new RoomProfile(id, "Room " + id))).ToArray();
            public readonly ElectricalSystem Electrical;
            public readonly HeaterSystem Heaters;
            public ElectricalCircuit A => Electrical.Find("A");
            public ElectricalCircuit B => Electrical.Find("B");
            public CircuitFixture(float capacity = 3, float increase = 1, bool continuous = true)
            {
                int[] ids = Rooms.Select(room => room.Profile.Id).ToArray();
                Electrical = new ElectricalSystem(new ElectricitySettings(circuitCapacity: capacity, capacityUpgradeAmount: increase), ids, continuous);
                Heaters = new HeaterSystem(new HeaterSettings(), ids);
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
        public void BranchUpgradeChangesOneCapacityAndPreservesConsumersAndResidualStress()
        {
            var fixture = new CircuitFixture(); fixture.Tick(8);
            var consumers = fixture.Electrical.Consumers;
            float stress = fixture.A.Stress01;
            int changes = 0;
            fixture.Electrical.Changed += (circuit, _) =>
            {
                changes++;
                Assert.That(fixture.Electrical.UpgradedCircuitId, Is.EqualTo("A"));
                Assert.That(circuit.Capacity, Is.EqualTo(4));
                Assert.That(circuit.Stress01, Is.EqualTo(stress));
            };
            Assert.That(Install(fixture.Electrical, "A").Success, Is.True);
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(fixture.A.RequestedLoad, Is.EqualTo(4));
            Assert.That(fixture.A.Capacity, Is.EqualTo(4));
            Assert.That(fixture.A.Reserve, Is.Zero);
            Assert.That(fixture.B.Capacity, Is.EqualTo(3));
            Assert.That(fixture.Electrical.Consumers, Is.SameAs(consumers), "No appliance is invented, removed or rescaled by a purchase.");
            fixture.Tick(0);
            Assert.That(fixture.A.Stress01, Is.EqualTo(stress));
            fixture.Tick(1);
            Assert.That(fixture.A.Stress01, Is.LessThan(stress).And.GreaterThan(0));
        }

        [Test]
        public void IncreasingCapacityOfTrippedCircuitDoesNotSupplyPowerUntilExplicitReset()
        {
            var fixture = new CircuitFixture(); fixture.Tick(18);
            Assert.That(fixture.A.Tripped, Is.True);
            float stress = fixture.A.Stress01;
            Assert.That(Install(fixture.Electrical, "A").Success, Is.True);
            Assert.That(fixture.A.Tripped, Is.True);
            Assert.That(fixture.A.Stress01, Is.EqualTo(stress));
            Assert.That(fixture.A.TripCount, Is.EqualTo(1));
            Assert.That(fixture.A.ActualRequestedLoad, Is.EqualTo(4));
            Assert.That(fixture.A.ActualDeliveredLoad, Is.Zero);
            Assert.That(fixture.Heaters.Items.All(item => item.EffectiveHeatOutput == 0), Is.True);
            Assert.That(fixture.Electrical.ResetCircuit(0, "A").Success, Is.True);
            fixture.Tick(30);
            Assert.That(fixture.A.Tripped, Is.False);
            Assert.That(fixture.A.ActualDeliveredLoad, Is.EqualTo(4));
            Assert.That(fixture.Heaters.Items.All(item => item.EffectiveHeatOutput > 0), Is.True);
        }

        [Test]
        public void TwoRealHeatersCanStillOverloadAnInsufficientConfiguredUpgrade()
        {
            var fixture = new CircuitFixture(increase: .5f);
            Assert.That(Install(fixture.Electrical, "A").Success, Is.True);
            Assert.That(fixture.A.Capacity, Is.EqualTo(3.5f));
            Assert.That(fixture.A.ActualRequestedLoad, Is.EqualTo(4));
            fixture.Tick(18);
            Assert.That(fixture.A.Tripped, Is.True);
            Assert.That(fixture.B.Tripped, Is.False);
        }

        [Test]
        public void TheHotelHasOneElectricalPurchaseRatherThanOnePurchasePerBranch()
        {
            var fixture = new CircuitFixture();
            Assert.That(Install(fixture.Electrical, "C").Success, Is.False);
            Assert.That(fixture.Electrical.UpgradedCircuitId, Is.Empty);
            Assert.That(Invoke<CommandResult>(fixture.Electrical, "CanInstallCapacityUpgrade", "B").Success, Is.True);
            Assert.That(fixture.Electrical.UpgradedCircuitId, Is.Empty);
            Assert.That(Install(fixture.Electrical, "B").Success, Is.True);
            Assert.That(Install(fixture.Electrical, "A").Success, Is.False);
            Assert.That(Install(fixture.Electrical, "B").Success, Is.False);
            Assert.That(fixture.Electrical.UpgradedCircuitId, Is.EqualTo("B"));
            Assert.That(fixture.B.Capacity, Is.EqualTo(4));
            Assert.That(fixture.A.Capacity, Is.EqualTo(3));
            fixture.Tick(18);
            Assert.That(fixture.A.Tripped, Is.True, "Upgrading the other branch cannot secretly relieve these appliances.");
        }

        [Test]
        public void LegacyAndReadOnlyEquipmentCannotInstallUpgrades()
        {
            Assert.That(Install(new BoilerSystem(new BoilerSettings())).Success, Is.False);
            var legacy = new CircuitFixture(continuous: false);
            Assert.That(Install(legacy.Electrical, "A").Success, Is.False);
            var boiler = Boiler(); var electrical = new CircuitFixture().Electrical;
            // Explicitly mark these isolated subsystems as replicas, without changing their capacities.
            typeof(BoilerSystem).GetField("ReadOnlyMirror", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(boiler, true);
            typeof(ElectricalSystem).GetField("ReadOnlyMirror", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(electrical, true);
            Assert.That(Install(boiler).Success, Is.False);
            Assert.That(Install(electrical, "A").Success, Is.False);
            Assert.That(boiler.CapacityUpgradePurchased, Is.False);
            Assert.That(electrical.UpgradedCircuitId, Is.Empty);
        }

        [Test]
        public void UnrepresentableCapacityIncreaseIsRefusedBeforeAnyEquipmentMutation()
        {
            var boiler = new BoilerSystem(new BoilerSettings(safeLoad: float.MaxValue), 720);
            Assert.That(Install(boiler).Success, Is.False);
            Assert.That(boiler.CapacityUpgradePurchased, Is.False);
            var roundedAway = new CircuitFixture(capacity: float.MaxValue);
            Assert.That(Install(roundedAway.Electrical, "A").Success, Is.False);
            var overflow = new CircuitFixture(capacity: float.MaxValue, increase: float.MaxValue);
            Assert.That(Install(overflow.Electrical, "A").Success, Is.False);
            Assert.That(overflow.Electrical.UpgradedCircuitId, Is.Empty);
            Assert.That(overflow.A.Capacity, Is.EqualTo(float.MaxValue));
        }

        [Test]
        public void UpgradeConfigurationRequiresFinitePositiveCapacityGains()
        {
            Assert.Throws<ArgumentException>(() => new BoilerCapacitySettings(capacityUpgradeMultiplier: 1));
            Assert.Throws<ArgumentException>(() => new BoilerCapacitySettings(capacityUpgradeMultiplier: float.NaN));
            Assert.Throws<ArgumentException>(() => new ElectricitySettings(capacityUpgradeAmount: 0));
            Assert.Throws<ArgumentException>(() => new ElectricitySettings(capacityUpgradeAmount: float.PositiveInfinity));
        }
    }
}
