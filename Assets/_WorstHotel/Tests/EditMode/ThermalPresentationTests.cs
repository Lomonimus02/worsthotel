using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace WorstHotel.Tests
{
    public sealed class ThermalPresentationTests
    {
        static HotelSimulation Create(out RoomState[] rooms, out SessionSettings settings)
        {
            var profiles = new[]
            {
                new GuestProfile(GuestKind.Budget, "Budget", "", 180, .85f, 18, .7f, 28, 90, .55f),
                new GuestProfile(GuestKind.ColdSensitive, "Cold", "", 300, 1.05f, 20, 1.35f, 16, 65, .4f),
                new GuestProfile(GuestKind.Business, "Business", "", 450, 1, 19.5f, 1, 10, 45, .25f)
            };
            settings = new SessionSettings(profiles, Enumerable.Range(101, 6).Select(id =>
                new RoomProfile(id, "Room " + id, heatLoss: (id - 101) * .3f, temperature: 21)),
                new BoilerSettings(), new EconomySettings(startingCash: 10000));
            rooms = settings.Rooms.Select(profile => new RoomState(profile)).ToArray();
            var hotel = new HotelSimulation(settings, rooms, new LivingHotelSettings(), services: new GuestServiceSettings(eligibility: 0),
                operations: new OperationsSettings());
            Require(hotel.StartOperations()); return hotel;
        }

        static void Require(CommandResult result) => Assert.That(result.Success, Is.True, result.Message);
        static RoomThermalBreakdown Measure(HotelSimulation hotel, int roomId = 101)
        { Assert.That(hotel.TryGetRoomThermalBreakdown(roomId, out var thermal, out _), Is.True); return thermal; }

        [Test]
        public void ActualLocalHeaterReversesAirTrendWithoutMakingClosedRadiatorLookWarm()
        {
            var hotel = Create(out var rooms, out var settings); var room = rooms[0];
            Require(hotel.SetRadiatorSetting(0, 101, 0));
            room.Temperature = settings.TemperatureBase + 1; // Labelled initial thermometer setup only.
            Assert.That(ThermalLabels.Movement(Measure(hotel)), Is.EqualTo(ThermalMovement.Cooling));
            Require(hotel.Heaters.Register("presentation-heater"));
            Require(hotel.Heaters.AssignRoom("presentation-heater", 101));
            Require(hotel.Heaters.SetSwitchedOn("presentation-heater", true)); hotel.RefreshElectrical();
            var heated = Measure(hotel);
            Assert.That(heated.SupplementalHeat, Is.GreaterThan(0));
            Assert.That(ThermalLabels.Movement(heated), Is.EqualTo(ThermalMovement.Warming));
            Assert.That(ThermalLabels.RadiatorWarmth01(heated, settings.HeatTemperatureGain), Is.Zero);
            float before = room.Temperature; hotel.Tick(1);
            Assert.That(room.Temperature, Is.GreaterThan(before), "The actual next thermal step agrees with the displayed direction.");

            Require(hotel.DebugTripCircuit("A")); // Isolated power-cut adapter, not a natural overload claim.
            var cut = Measure(hotel);
            Assert.That(cut.SupplementalHeat, Is.Zero);
            Assert.That(ThermalLabels.Movement(cut), Is.EqualTo(ThermalMovement.Cooling));
            Assert.That(ThermalLabels.RadiatorWarmth01(cut, settings.HeatTemperatureGain), Is.Zero);
            before = room.Temperature; hotel.Tick(1);
            Assert.That(room.Temperature, Is.LessThan(before));
        }

        [Test]
        public void PowerCutDoesNotHideCentralHeatWhileMaintenanceReallyRemovesItAndProjectionIsPure()
        {
            var hotel = Create(out var rooms, out var settings);
            var original = Measure(hotel);
            float warmth = ThermalLabels.RadiatorWarmth01(original, settings.HeatTemperatureGain);
            Assert.That(warmth, Is.GreaterThan(0));
            Require(hotel.DebugTripCircuit("A"));
            var cut = Measure(hotel);
            Assert.That(rooms[0].HasPower, Is.False);
            Assert.That(cut.CurrentTemperature, Is.EqualTo(rooms[0].Temperature));
            Assert.That(cut.HeatingContribution, Is.EqualTo(original.HeatingContribution));
            Assert.That(ThermalLabels.RadiatorWarmth01(cut, settings.HeatTemperatureGain), Is.EqualTo(warmth));
            Require(hotel.BeginBoilerMaintenance(0, BoilerServiceKind.Full, hotel.Boiler.MaintenanceRevision));
            var offline = Measure(hotel);
            Assert.That(offline.BoilerMaintenance, Is.True);
            Assert.That(offline.CurrentTemperature, Is.EqualTo(cut.CurrentTemperature), "Starting service cannot instantly cool the room.");
            Assert.That(ThermalLabels.RadiatorWarmth01(offline, settings.HeatTemperatureGain), Is.Zero);
            Assert.That(ThermalLabels.Movement(offline), Is.EqualTo(ThermalMovement.Cooling));

            string before = JsonUtility.ToJson(hotel.CaptureSnapshot(821, 1));
            int revision = hotel.EventRevision;
            for (int repeat = 0; repeat < 4; repeat++)
                foreach (var room in rooms)
                {
                    var thermal = Measure(hotel, room.Profile.Id);
                    ThermalLabels.Reading(thermal);
                    ThermalLabels.Sources(thermal, ThermalLabels.ColdProne(thermal, rooms));
                    ThermalLabels.RadiatorWarmth01(thermal, settings.HeatTemperatureGain);
                }
            Assert.That(JsonUtility.ToJson(hotel.CaptureSnapshot(821, 1)), Is.EqualTo(before));
            Assert.That(hotel.EventRevision, Is.EqualTo(revision));
        }
    }
}
