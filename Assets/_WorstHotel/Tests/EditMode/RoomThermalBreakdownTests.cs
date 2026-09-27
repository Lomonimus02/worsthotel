using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace WorstHotel.Tests
{
    public sealed class RoomThermalBreakdownTests
    {
        sealed class Fixture
        {
            public SessionSettings Settings;
            public RoomState[] Rooms;
            public HotelSimulation Hotel;
        }

        static Fixture Create(bool start = true)
        {
            var profiles = new[]
            {
                new GuestProfile(GuestKind.Budget, "Budget", "", 180, .85f, 18, .7f, 28, 90, .55f),
                new GuestProfile(GuestKind.ColdSensitive, "Cold", "", 300, 1.05f, 20, 1.35f, 16, 65, .4f),
                new GuestProfile(GuestKind.Business, "Business", "", 450, 1, 19.5f, 1, 10, 45, .25f)
            };
            var settings = new SessionSettings(profiles, Enumerable.Range(101, 6).Select(id =>
                new RoomProfile(id, "Room " + id, heatLoss: (id - 100) * .37f, temperature: 21.23457f)),
                new BoilerSettings(), new EconomySettings(startingCash: 10000));
            var rooms = settings.Rooms.Select(room => new RoomState(room)).ToArray();
            var hotel = new HotelSimulation(settings, rooms,
                new LivingHotelSettings(firstActivityDelay: 1000, awayDurationMin: 600, awayDurationMax: 600),
                services: new GuestServiceSettings(eligibility: 0), operations: new OperationsSettings());
            if (start) Require(hotel.StartOperations());
            return new Fixture { Settings = settings, Rooms = rooms, Hotel = hotel };
        }

        static void Require(CommandResult result) => Assert.That(result.Success, Is.True, result.Message);
        static string Wire(HotelSimulation hotel) => JsonUtility.ToJson(hotel.CaptureSnapshot(73, 1));
        static int Bits(float value) => BitConverter.ToInt32(BitConverter.GetBytes(value), 0);

        // This reference is intentionally the pre-extraction production expression, not the new helper.
        static float OriginalStep(SessionSettings settings, RoomInfrastructureSettings infrastructure,
            RoomState room, float output, float supplement, float dt)
        {
            float blend = 1 - (float)Math.Exp(-dt / settings.TemperatureTimeConstant);
            float baselineTarget = settings.TemperatureBase + settings.HeatTemperatureGain * Math.Max(0, Math.Min(1, output)) *
                infrastructure.HeatMultiplier(room.RadiatorSetting) - room.Profile.HeatLoss;
            if (supplement == 0) return room.Temperature + (baselineTarget - room.Temperature) * blend;
            double target = baselineTarget + (double)supplement;
            double temperature = room.Temperature + (target - room.Temperature) * blend;
            return (float)Math.Max(-float.MaxValue, Math.Min(float.MaxValue, temperature));
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void ActualUnassistedTickPreservesOriginalFloatBitsAcrossValvesAndOutputs(int valve)
        {
            var fixture = Create(); var room = fixture.Rooms[0];
            Require(fixture.Hotel.SetRadiatorSetting(0, room.Profile.Id, valve));
            var system = new RoomSystem(fixture.Settings, fixture.Hotel.InfrastructureSettings);
            foreach (float output in new[] { -.2f, 0, .137f, .9012345f, 1, 2 })
                foreach (float dt in new[] { 0, .001f, .2f, 1, 17.73f, 720 })
                {
                    float expected = OriginalStep(fixture.Settings, system.Infrastructure, room, output, 0, dt);
                    var measured = system.ThermalBreakdownForRoom(room, output);
                    Assert.That(Bits(measured.TemperatureAfter(dt)), Is.EqualTo(Bits(expected)));
                    system.TickTemperature(new[] { room }, output, dt);
                    Assert.That(Bits(room.Temperature), Is.EqualTo(Bits(expected)), "Actual tick must use the shared calculation.");
                }
        }

        [TestCase(3.25f, 21.23457f)]
        [TestCase(float.MaxValue, -float.MaxValue)]
        [TestCase(float.MaxValue, float.MaxValue)]
        public void PoweredSupplementPreservesOriginalDoublePathAndFiniteClamp(float supplement, float current)
        {
            var fixture = Create(); var room = fixture.Rooms[0]; room.Temperature = current;
            var system = new RoomSystem(fixture.Settings);
            foreach (float dt in new[] { 0, .2f, 45, 720 })
            {
                float expected = OriginalStep(fixture.Settings, system.Infrastructure, room, .91f, supplement, dt);
                var measured = system.ThermalBreakdownForRoom(room, .91f, supplement);
                Assert.That(measured.TargetTemperature, Is.EqualTo(measured.BaselineTarget + (double)supplement));
                Assert.That(Bits(measured.TemperatureAfter(dt)), Is.EqualTo(Bits(expected)),
                    $"Original powered step: dt={dt:R}, current={room.Temperature:R}, supplement={supplement:R}.");
                system.TickTemperature(new[] { room }, .91f, dt, _ => supplement);
                Assert.That(Bits(room.Temperature), Is.EqualTo(Bits(expected)));
                Assert.That(float.IsNaN(room.Temperature) || float.IsInfinity(room.Temperature), Is.False);
            }
        }

        [Test]
        public void TripRemovesPoweredSupplementButRetainsCentralHeatAndActualRequestedConsumer()
        {
            var fixture = Create(); var hotel = fixture.Hotel;
            Require(hotel.Heaters.Register("thermal-heater"));
            Require(hotel.Heaters.AssignRoom("thermal-heater", 101));
            Require(hotel.Heaters.SetSwitchedOn("thermal-heater", true)); hotel.RefreshElectrical();
            Assert.That(hotel.TryGetRoomThermalBreakdown(101, out var before, out _), Is.True);
            Assert.That(before.SupplementalHeat, Is.GreaterThan(0));
            Require(hotel.DebugTripCircuit("A")); // Explicit isolated power-cut fixture, not a natural-trip claim.
            Assert.That(hotel.TryGetRoomThermalBreakdown(101, out var after, out _), Is.True);
            Assert.That(after.SupplementalHeat, Is.Zero);
            Assert.That(after.HeatingContribution, Is.EqualTo(before.HeatingContribution));
            Assert.That(after.BoilerFailed, Is.False);
            var consumer = hotel.Electrical.Consumers.Single(item => item.Id == "heater:thermal-heater");
            Assert.That(consumer.RequestedLoad, Is.GreaterThan(0)); Assert.That(consumer.DeliveredLoad, Is.Zero);
            Require(hotel.ResetCircuit(0, "A"));
            Assert.That(hotel.TryGetRoomThermalBreakdown(101, out var restored, out _), Is.True);
            Assert.That(restored.SupplementalHeat, Is.EqualTo(before.SupplementalHeat));
        }

        [Test]
        public void QuerySharesActualStagedAndAwayOwnershipDemandAndNeverMutatesHostOrMirror()
        {
            var fixture = Create(); var hotel = fixture.Hotel;
            Require(hotel.DebugSpawnGuest(GuestKind.ColdSensitive, 101)); hotel.Tick(1.25f);
            var guest = hotel.Guests.Single();
            // Labelled headless key/route/staging adapters; this test makes no physical-route claim.
            Require(hotel.SignalGuestReachedReception(guest.GuestId));
            Require(ModelKeyHandoff.CheckIn(hotel, 0, guest.GuestId));
            Require(hotel.SignalGuestReachedRoom(guest.GuestId));
            Require(hotel.RegisterGuestPhysicalStaging(guest.GuestId));
            Require(hotel.ForceActivity(guest.GuestId, GuestActivity.Shower));
            Assert.That(hotel.TryGetRoomThermalBreakdown(101, out _, out var moving), Is.True);
            Assert.That(moving.HotWater, Is.Zero);
            Require(hotel.SignalGuestActivityReady(guest.GuestId, GuestAgentState.PerformingActivity, GuestActivity.Shower));
            Assert.That(hotel.TryGetRoomThermalBreakdown(101, out _, out var shower), Is.True);
            Assert.That(shower.HotWater, Is.GreaterThan(0));
            Require(hotel.ForceLeaveRoom(guest.GuestId)); Require(hotel.SignalGuestLeftRoom(guest.GuestId));
            Assert.That(hotel.TryGetRoomThermalBreakdown(101, out _, out var away), Is.True);
            Assert.That(away.GuestId, Is.EqualTo(guest.GuestId));
            Assert.That(away.SpaceHeating, Is.EqualTo(shower.SpaceHeating)); Assert.That(away.HotWater, Is.Zero);
            var mirror = Create(false).Hotel; mirror.EnableReadOnlyMirror();
            Require(mirror.ApplySnapshot(JsonUtility.FromJson<HotelModelSnapshot>(Wire(hotel))));
            foreach (var model in new[] { hotel, mirror })
            {
                string state = Wire(model); int revision = model.EventRevision, count = model.InfrastructureHistory.Count;
                for (int iteration = 0; iteration < 8; iteration++)
                    foreach (var room in fixture.Rooms)
                    {
                        Assert.That(model.TryGetRoomThermalBreakdown(room.Profile.Id, out var thermal, out var row), Is.True);
                        Assert.That(row, Is.EqualTo(model.HeatingDemands.Single(item => item.RoomId == room.Profile.Id)));
                        Assert.That(thermal.CurrentTemperature, Is.EqualTo(model.CaptureSnapshot(73, 1).Rooms.Single(item => item.Id == room.Profile.Id).Temperature));
                    }
                Assert.That(model.TryGetRoomThermalBreakdown(999, out _, out _), Is.False);
                Assert.That(Wire(model), Is.EqualTo(state)); Assert.That(model.EventRevision, Is.EqualTo(revision));
                Assert.That(model.InfrastructureHistory.Count, Is.EqualTo(count));
            }
            Assert.That(mirror.InfrastructureHistory, Is.Empty, "Host-local diagnostic history is not replayed by the mirror.");
        }
    }
}
