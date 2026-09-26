using System;
using System.Linq;
using NUnit.Framework;

namespace WorstHotel.Tests
{
    public sealed class RoomInfrastructureTests
    {
        static SessionSettings Settings()
        {
            var budget = new GuestProfile(GuestKind.Budget, "Budget", "", 180, .85f, 18, .7f, 28, 90, .55f);
            var cold = new GuestProfile(GuestKind.ColdSensitive, "Cold", "", 300, 1.05f, 20, 1.35f, 16, 65, .4f);
            var business = new GuestProfile(GuestKind.Business, "Business", "", 450, 1, 19.5f, 1, 10, 45, .25f);
            return new SessionSettings(new[] { budget, cold, business }, Enumerable.Range(101, 6).Select(id => new RoomProfile(id, "Room " + id)),
                new BoilerSettings(), new EconomySettings());
        }

        [Test]
        public void ValveIncreasesRoomHeatAndBoilerDemandWhileZeroClosesItsHeat()
        {
            var settings = Settings(); var rooms = settings.Rooms.Select(profile => new RoomState(profile)).ToArray();
            var hotel = new HotelSimulation(settings, rooms, new LivingHotelSettings());
            var thermal = new RoomSystem(settings);
            Assert.That(hotel.SetRadiatorSetting(0, 101, 0).Success, Is.True);
            Assert.That(hotel.SetRadiatorSetting(0, 102, 1).Success, Is.True);
            Assert.That(hotel.SetRadiatorSetting(0, 103, 3).Success, Is.True);
            thermal.TickTemperature(rooms, 1, 60);
            Assert.That(rooms[2].Temperature, Is.GreaterThan(rooms[1].Temperature));
            Assert.That(rooms[1].Temperature, Is.GreaterThan(rooms[0].Temperature));
            float load = hotel.Boiler.Load;
            Assert.That(hotel.SetRadiatorSetting(0, 104, 3).Success, Is.True);
            Assert.That(hotel.Boiler.Load, Is.GreaterThan(load));
            Assert.That(hotel.Electrical.Consumers.Sum(item => item.RequestedLoad), Is.Zero);
            Assert.That(hotel.SetRadiatorSetting(0, 104, 4).Success, Is.False);
            Assert.That(rooms[3].RadiatorSetting, Is.EqualTo(3));
        }

        [Test]
        public void LampOnlyWearsWhenOccupiedAndPoweredAndBreakagePersistsWithoutPower()
        {
            var settings = Settings(); var room = new RoomState(new RoomProfile(101, "Old lamp", repairState: RepairState.Degraded));
            var thermal = new RoomSystem(settings, new RoomInfrastructureSettings(lampWearPerSecond: 1));
            float initial = room.LampCondition;
            thermal.TickInfrastructure(new[] { room }, 100);
            Assert.That(room.LampCondition, Is.EqualTo(initial), "An empty room does not consume bedside lamp life.");
            room.GuestId = "occupant";
            thermal.TickInfrastructure(new[] { room }, initial - 2);
            Assert.That(room.LampBroken, Is.False); Assert.That(room.LampCondition, Is.EqualTo(2));
            var power = new ElectricalSystem(new ElectricitySettings(), new[] { 101 });
            power.Tick(Array.Empty<GuestStay>(), new[] { room }, new HeaterSystem(new HeaterSettings(), new[] { 101 }), 0);
            Assert.That(power.ForceTrip("A").Success, Is.True);
            thermal.TickInfrastructure(new[] { room }, 100);
            Assert.That(room.LampCondition, Is.EqualTo(2), "An unpowered bulb cannot accrue operating wear.");
            Assert.That(power.ResetCircuit(0, "A").Success, Is.True);
            thermal.TickInfrastructure(new[] { room }, 2);
            Assert.That(room.LampBroken, Is.True); Assert.That(room.LampCondition, Is.Zero);
            thermal.TickInfrastructure(new[] { room }, 300);
            Assert.That(room.LampBroken, Is.True);
        }

        [Test]
        public void RealCircuitConsumerReadoutSurvivesTripsAndLabelsHeatersByActualRoom()
        {
            var rooms = Settings().Rooms.Select(profile => new RoomState(profile)).ToArray();
            var heaters = new HeaterSystem(new HeaterSettings(), rooms.Select(room => room.Profile.Id));
            var electrical = new ElectricalSystem(new ElectricitySettings(circuitCapacity: 3), rooms.Select(room => room.Profile.Id));
            foreach (var pair in new[] { ("portable-heater-1", 104), ("portable-heater-2", 106) })
            {
                Assert.That(heaters.Register(pair.Item1).Success, Is.True);
                Assert.That(heaters.AssignRoom(pair.Item1, pair.Item2).Success, Is.True);
                Assert.That(heaters.SetSwitchedOn(pair.Item1, true).Success, Is.True);
            }
            electrical.Tick(Array.Empty<GuestStay>(), rooms, heaters, electrical.Settings.TripSeconds + 1);
            var b = electrical.Find("B"); Assert.That(b.Tripped, Is.True);
            string text = ElectricalPanelPresentation.ConsumerBreakdown(electrical, "B");
            Assert.That(text, Does.Contain("HEATER 104  2.00").And.Contain("HEATER 106  2.00"));
            Assert.That(electrical.ResetCircuit(0, "B").Success, Is.True);
            electrical.Tick(Array.Empty<GuestStay>(), rooms, heaters, electrical.Settings.TripSeconds + 1);
            Assert.That(b.Tripped, Is.True); Assert.That(b.TripCount, Is.EqualTo(2));
            Assert.That(b.RequestedLoad, Is.EqualTo(4));
            Assert.That(heaters.SetSwitchedOn("portable-heater-2", false).Success, Is.True);
            electrical.Tick(Array.Empty<GuestStay>(), rooms, heaters, 0);
            Assert.That(electrical.ResetCircuit(0, "B").Success, Is.True);
            electrical.Tick(Array.Empty<GuestStay>(), rooms, heaters, electrical.Settings.TripSeconds + 1);
            Assert.That(b.HasPower, Is.True); Assert.That(b.TripCount, Is.EqualTo(2));
            Assert.That(ElectricalPanelPresentation.ConsumerBreakdown(electrical, "B"), Does.Not.Contain("HEATER 106"));
        }
    }
}
