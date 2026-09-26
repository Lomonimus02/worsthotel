using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace WorstHotel.Tests
{
    public sealed class ElectricalDebugTests
    {
        sealed class Fixture
        {
            public readonly RoomState[] Rooms = Enumerable.Range(101, 6)
                .Select(id => new RoomState(new RoomProfile(id, "Room " + id))).ToArray();
            public readonly HeaterSystem Heaters;
            public readonly ElectricalSystem Electrical;
            public ElectricalCircuit A => Electrical.Find("A");
            public Fixture()
            {
                Heaters = new HeaterSystem(new HeaterSettings(), Rooms.Select(room => room.Profile.Id));
                Electrical = new ElectricalSystem(new ElectricitySettings(), Rooms.Select(room => room.Profile.Id));
                AddHeater("real-heater", 101);
            }
            public void AddHeater(string id, int room)
            {
                Require(Heaters.Register(id)); Require(Heaters.AssignRoom(id, room));
                Require(Heaters.SetSwitchedOn(id, true)); Tick(0);
            }
            public void Tick(float seconds) => Electrical.Tick(Array.Empty<GuestStay>(), Rooms, Heaters, seconds);
        }

        [Test]
        public void DiagnosticTotalRetainsRealConsumerIdentityDemandAndDelivery()
        {
            var fixture = new Fixture();
            var original = fixture.Electrical.Consumers.Single();
            Require(fixture.Electrical.DebugOverrideLoad("A", 6));
            fixture.Tick(1);
            var current = fixture.Electrical.Consumers.Single();
            Assert.That(fixture.A.LoadOverride, Is.EqualTo(6));
            Assert.That(fixture.A.RequestedLoad, Is.EqualTo(6));
            Assert.That(fixture.A.DeliveredLoad, Is.EqualTo(6), "This is the effective diagnostic total, not physical delivery.");
            Assert.That(fixture.A.ActualRequestedLoad, Is.EqualTo(2));
            Assert.That(fixture.A.ActualDeliveredLoad, Is.EqualTo(2));
            Assert.That(current.Id, Is.EqualTo(original.Id));
            Assert.That(current.RoomId, Is.EqualTo(original.RoomId));
            Assert.That(current.RequestedLoad, Is.EqualTo(2));
            Assert.That(current.DeliveredLoad, Is.EqualTo(2));
            Assert.That(original.DeliveredLoad, Is.EqualTo(2), "Previously published consumer snapshots stay immutable.");
            Assert.That(fixture.Electrical.Consumers.Sum(consumer => consumer.RequestedLoad), Is.EqualTo(fixture.A.ActualRequestedLoad));
            Assert.That(fixture.Electrical.Find("B").RequestedLoad, Is.Zero);

            Require(fixture.Heaters.AssignRoom("real-heater", null)); fixture.Tick(0);
            Assert.That(fixture.A.LoadOverride, Is.EqualTo(6));
            Assert.That(fixture.A.ActualRequestedLoad, Is.Zero);
            Assert.That(fixture.Electrical.Consumers.Single().Id, Is.EqualTo(original.Id));
            Assert.That(fixture.Electrical.Consumers.Single().CircuitId, Is.Null);
            Assert.That(fixture.Heaters.Find("real-heater").EffectiveHeatOutput, Is.Zero,
                "A diagnostic circuit total never creates heat for a held or invalidly placed device.");
            Assert.That(fixture.Heaters.Find("real-heater").DemandedElectricalLoad, Is.Zero);
        }

        [Test]
        public void SustainedOverrideTripsNormallyAndClearingItDoesNotRepairBreaker()
        {
            var fixture = new Fixture();
            Require(fixture.Electrical.DebugOverrideLoad("A", 5));
            fixture.Tick(5.8f);
            Assert.That(fixture.A.Warning || fixture.A.Tripped, Is.False);
            fixture.Tick(.4f);
            Assert.That(fixture.A.Warning, Is.True);
            fixture.Tick(12);
            Assert.That(fixture.A.Tripped, Is.True);
            Assert.That(fixture.Rooms.Where(room => room.Profile.Id <= 103).All(room => !room.HasPower), Is.True);
            Assert.That(fixture.Rooms.Where(room => room.Profile.Id >= 104).All(room => room.HasPower), Is.True);
            Assert.That(fixture.A.ActualRequestedLoad, Is.EqualTo(2));
            Assert.That(fixture.A.ActualDeliveredLoad, Is.Zero);
            Assert.That(fixture.A.DeliveredLoad, Is.Zero);
            Assert.That(fixture.Electrical.Consumers.Single().DeliveredLoad, Is.Zero);
            Require(fixture.Electrical.DebugOverrideLoad("A", null));
            Assert.That(fixture.A.RequestedLoad, Is.EqualTo(2));
            Assert.That(fixture.A.OverloadSeconds, Is.Zero);
            Assert.That(fixture.A.Tripped, Is.True);
            Assert.That(fixture.Heaters.Find("real-heater").EffectiveHeatOutput, Is.Zero);
            Require(fixture.Electrical.ResetCircuit(0, "A"));
            Assert.That(fixture.A.ActualDeliveredLoad, Is.EqualTo(2));
            fixture.Tick(30);
            Assert.That(fixture.A.Tripped || fixture.A.Warning, Is.False);
            Assert.That(fixture.Heaters.Find("real-heater").EffectiveHeatOutput, Is.GreaterThan(0));
        }

        [Test]
        public void ResetWithRemainingDiagnosticDemandRetripsWithoutInventingConsumers()
        {
            var fixture = new Fixture();
            Require(fixture.Electrical.DebugOverrideLoad("A", 5)); fixture.Tick(18);
            Require(fixture.Electrical.ResetCircuit(1, "A"));
            Require(fixture.Heaters.SetSwitchedOn("real-heater", false)); fixture.Tick(0);
            Assert.That(fixture.A.ActualRequestedLoad, Is.Zero);
            Assert.That(fixture.A.RequestedLoad, Is.EqualTo(5));
            Assert.That(fixture.A.OverloadSeconds, Is.Zero);
            fixture.Tick(18);
            Assert.That(fixture.A.Tripped, Is.True);
            Assert.That(fixture.A.TripCount, Is.EqualTo(2));
            Assert.That(fixture.Electrical.Consumers.Count, Is.EqualTo(1));
            Assert.That(fixture.Electrical.Consumers.Single().RequestedLoad, Is.Zero);
        }

        [Test]
        public void RevertReadsCurrentRealDemandAndEndsOnlyTheCurrentContinuousWarning()
        {
            var fixture = new Fixture();
            fixture.AddHeater("second", 102); fixture.AddHeater("third", 103);
            Assert.That(fixture.A.ActualRequestedLoad, Is.EqualTo(6));
            Require(fixture.Electrical.DebugOverrideLoad("A", 0)); fixture.Tick(30);
            Assert.That(fixture.A.RequestedLoad, Is.Zero);
            Assert.That(fixture.A.ActualDeliveredLoad, Is.EqualTo(6), "Understating a diagnostic total does not erase real delivery.");
            Assert.That(fixture.Electrical.Consumers.Sum(consumer => consumer.DeliveredLoad), Is.EqualTo(6));
            Require(fixture.Electrical.DebugOverrideLoad("A", null)); fixture.Tick(7);
            Assert.That(fixture.A.Warning, Is.True);
            Require(fixture.Electrical.DebugOverrideLoad("A", 0));
            Assert.That(fixture.A.Warning, Is.False);
            Assert.That(fixture.A.OverloadSeconds, Is.Zero);
            Require(fixture.Heaters.SetSwitchedOn("third", false)); fixture.Tick(0);
            Require(fixture.Electrical.DebugOverrideLoad("A", null));
            Assert.That(fixture.A.RequestedLoad, Is.EqualTo(4), "Revert uses current consumers, not the total captured when the override began.");
            fixture.Tick(30);
            Assert.That(fixture.A.Tripped || fixture.A.Warning, Is.False);
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        [TestCase(-0.01f)]
        public void InvalidDiagnosticTotalsFailAtomically(float invalid)
        {
            var fixture = new Fixture();
            Require(fixture.Electrical.DebugOverrideLoad("A", 5)); fixture.Tick(7);
            int events = 0; fixture.Electrical.Changed += (_, __) => events++;
            var consumers = fixture.Electrical.Consumers;
            Assert.That(fixture.Electrical.DebugOverrideLoad("A", invalid).Success, Is.False);
            Assert.That(fixture.A.LoadOverride, Is.EqualTo(5));
            Assert.That(fixture.A.ActualRequestedLoad, Is.EqualTo(2));
            Assert.That(fixture.A.OverloadSeconds, Is.EqualTo(7));
            Assert.That(fixture.A.Warning, Is.True);
            Assert.That(fixture.A.Tripped, Is.False);
            Assert.That(fixture.Electrical.Consumers, Is.SameAs(consumers));
            Assert.That(events, Is.Zero);
        }

        [Test]
        public void UnknownCircuitAndRepeatedAssignmentLeaveStateAndEventRevisionAlone()
        {
            var fixture = new Fixture();
            int events = 0; fixture.Electrical.Changed += (_, __) => events++;
            foreach (var unknown in new[] { null, "", "C", "a" })
            {
                Assert.That(fixture.Electrical.DebugOverrideLoad(unknown, 6).Success, Is.False);
                Assert.That(fixture.Electrical.DebugOverrideLoad(unknown, null).Success, Is.False);
            }
            Require(fixture.Electrical.DebugOverrideLoad("A", 5));
            Require(fixture.Electrical.DebugOverrideLoad("A", 5));
            Assert.That(events, Is.EqualTo(1));
            Assert.That(fixture.A.OverloadSeconds, Is.Zero);
            Assert.That(fixture.Electrical.Find("B").LoadOverride, Is.Null);
        }

        [Test]
        public void SimulationWrapperRefreshesWithoutTimeOrAssetMutationAndFreshModelClearsOverrides()
        {
            var asset = AssetDatabase.LoadAssetAtPath<SessionConfig>("Assets/_WorstHotel/ScriptableObjects/PrototypeSession.asset");
            Assert.That(asset && asset.electricity && asset.living, Is.True);
            string configBefore = JsonUtility.ToJson(asset.electricity);
            var settings = asset.ToData();
            var electricalSettings = asset.electricity.ToData();
            Func<HotelSimulation> create = () => new HotelSimulation(settings, settings.Rooms.Select(room => new RoomState(room)).ToArray(),
                asset.living.ToData(), electricity: electricalSettings);
            var simulation = create();
            int revision = simulation.EventRevision;
            Require(simulation.DebugSetCircuitLoad("B", 9));
            Assert.That(simulation.EventRevision, Is.GreaterThan(revision));
            Assert.That(simulation.Elapsed, Is.Zero);
            Assert.That(simulation.Electrical.Find("B").RequestedLoad, Is.EqualTo(9));
            Assert.That(simulation.Electrical.Find("B").ActualRequestedLoad, Is.Zero);
            Assert.That(simulation.Electrical.Consumers, Is.Empty);
            Require(simulation.DebugTripCircuit("B")); Require(simulation.DebugSetCircuitLoad("B", null));
            Assert.That(simulation.Electrical.Find("B").Tripped, Is.True);
            Require(simulation.DebugSetCircuitLoad("A", 7));
            var fresh = create();
            Assert.That(fresh.Electrical.Circuits.All(circuit => !circuit.LoadOverride.HasValue && !circuit.Tripped), Is.True);
            Assert.That(JsonUtility.ToJson(asset.electricity), Is.EqualTo(configBefore));
            Assert.That(fresh.ElectricitySettings.CircuitCapacity, Is.EqualTo(electricalSettings.CircuitCapacity));
            var legacy = new HotelSimulation(settings, settings.Rooms.Select(room => new RoomState(room)).ToArray());
            Assert.That(legacy.DebugSetCircuitLoad("A", 1).Success, Is.False);
        }

        static void Require(CommandResult result) => Assert.That(result.Success, Is.True, result.Message);
    }
}
