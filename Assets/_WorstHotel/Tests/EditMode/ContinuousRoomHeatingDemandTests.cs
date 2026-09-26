using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace WorstHotel.Tests
{
    /// <summary>Room-source tests. Travel, keys and anchor acknowledgements below are explicit headless adapters.</summary>
    public sealed class ContinuousRoomHeatingDemandTests
    {
        sealed class Fixture
        {
            public SessionSettings Settings;
            public LivingHotelSettings Living;
            public RoomState[] Rooms;
            public HotelSimulation Hotel;
            public GuestStay Guest => Hotel.Guests.Single(guest => guest.RoomId == 101);
        }

        static void Require(CommandResult result) => Assert.That(result.Success, Is.True, result.Message);

        static Fixture Create(bool populate = true, LivingHotelSettings living = null, bool continuous = true)
        {
            var profiles = new[]
            {
                new GuestProfile(GuestKind.Budget, "Budget", "", 180, .85f, 18, .7f, 28, 90, .55f),
                new GuestProfile(GuestKind.ColdSensitive, "Cold", "", 300, 1.05f, 20, 1.35f, 16, 65, .4f),
                new GuestProfile(GuestKind.Business, "Business", "", 450, 1, 19.5f, 1, 10, 45, .25f)
            };
            var settings = new SessionSettings(profiles, Enumerable.Range(101, 6).Select(id =>
                new RoomProfile(id, "Room " + id, heatLoss: id == 101 ? 2 : 0, noise: 0, temperature: 22)),
                new BoilerSettings(), new EconomySettings());
            var rooms = settings.Rooms.Select(profile => new RoomState(profile)).ToArray();
            living = living ?? new LivingHotelSettings(firstActivityDelay: 1000, awayDurationMin: 600, awayDurationMax: 600);
            var hotel = new HotelSimulation(settings, rooms, living,
                services: new GuestServiceSettings(eligibility: 0, naturalCommunicationEnabled: true),
                infrastructure: new RoomInfrastructureSettings(lampWearPerSecond: 0),
                operations: continuous ? new OperationsSettings() : null);
            var fixture = new Fixture { Settings = settings, Living = living, Rooms = rooms, Hotel = hotel };
            if (continuous)
            {
                Require(hotel.StartOperations());
                if (!populate) return fixture;
                Require(hotel.DebugSpawnGuest(GuestKind.ColdSensitive, 101));
                hotel.Tick(1.25f); // Materialize the diagnostic reservation only when its real arrival is due.
            }
            else
            {
                if (!populate) return fixture;
                var offer = new BookingApplication("legacy-cold", "Legacy cold", profiles[1], 300);
                Require(hotel.StartShift(new[] { new BookingAssignment(101, offer.Id, 300, 0) }, new[] { offer }));
                hotel.Tick(hotel.Guests.Single().Agent.ArrivalTime + .25f);
            }
            Require(hotel.SignalGuestReachedReception(fixture.Guest.GuestId));
            Require(ModelKeyHandoff.CheckIn(hotel, 0, fixture.Guest.GuestId));
            Require(hotel.SignalGuestReachedRoom(fixture.Guest.GuestId));
            Require(hotel.RegisterGuestPhysicalStaging(fixture.Guest.GuestId));
            return fixture;
        }

        static RoomHeatingDemand Row(Fixture fixture, int roomId = 101) =>
            fixture.Hotel.HeatingDemands.Single(row => row.RoomId == roomId);

        static void StartShower(Fixture fixture)
        {
            Require(fixture.Hotel.ForceActivity(fixture.Guest.GuestId, GuestActivity.Shower));
            Require(fixture.Hotel.SignalGuestActivityReady(fixture.Guest.GuestId,
                GuestAgentState.PerformingActivity, GuestActivity.Shower));
        }

        static void SumMatchesBoiler(Fixture fixture)
        {
            var rows = fixture.Hotel.HeatingDemands;
            Assert.That(rows.Select(row => row.RoomId), Is.EqualTo(Enumerable.Range(101, 6)));
            Assert.That(rows.All(row => row.SpaceHeating >= 0 && row.HotWater >= 0), Is.True);
            Assert.That(fixture.Hotel.Boiler.Load, Is.EqualTo(rows.Sum(row => row.Total)).Within(.00001f));
        }

        [Test]
        public void VacantRadiatorsHaveExplicitLossAdjustedDemandAndClosingOneRemovesOnlyThatRoom()
        {
            var fixture = Create(false);
            Assert.That(fixture.Hotel.Boiler.CapacityModelEnabled, Is.True);
            Assert.That(Row(fixture).GuestId, Is.Null);
            Assert.That(Row(fixture).SpaceHeating, Is.EqualTo(.08f * 1.08f).Within(.00001f));
            Assert.That(Row(fixture).HotWater, Is.Zero);
            float other = Row(fixture, 102).Total;
            float initial = fixture.Hotel.Boiler.Load;
            Require(fixture.Hotel.SetRadiatorSetting(0, 101, 0));
            Assert.That(Row(fixture).Total, Is.Zero);
            Assert.That(Row(fixture, 102).Total, Is.EqualTo(other));
            Assert.That(fixture.Hotel.Boiler.Load, Is.EqualTo(initial - .0864f).Within(.00001f));
            SumMatchesBoiler(fixture);
        }

        [Test]
        public void ReservationAndLobbyWaitingDoNotClaimProfileDemandButKeyHandoffDoesImmediately()
        {
            var fixture = Create(false); var hotel = fixture.Hotel;
            Require(hotel.DebugSpawnGuest(GuestKind.Business, 101));
            Assert.That(Row(fixture).GuestId, Is.Null);
            float vacant = Row(fixture).Total;
            hotel.Tick(1.25f);
            var guest = fixture.Guest;
            Require(hotel.SignalGuestReachedReception(guest.GuestId));
            Assert.That(Row(fixture).Total, Is.EqualTo(vacant));
            Require(ModelKeyHandoff.CheckIn(hotel, 0, guest.GuestId));
            Assert.That(guest.Agent.State, Is.EqualTo(GuestAgentState.GoingToRoom));
            Assert.That(Row(fixture).GuestId, Is.EqualTo(guest.GuestId));
            Assert.That(Row(fixture).SpaceHeating, Is.EqualTo(1f * .85f * 1.08f).Within(.00001f));
            Assert.That(Row(fixture).HotWater, Is.Zero);
            SumMatchesBoiler(fixture);
        }

        [Test]
        public void ShowerTapStartsOnlyAfterItsCurrentAnchorAcknowledgementAndStopsOnActivityChange()
        {
            var fixture = Create(); var hotel = fixture.Hotel; string id = fixture.Guest.GuestId;
            float quietSpace = Row(fixture).SpaceHeating;
            Require(hotel.ForceActivity(id, GuestActivity.Shower));
            Assert.That(fixture.Guest.Agent.ActivityStaged, Is.False);
            Assert.That(Row(fixture).HotWater, Is.Zero);
            Assert.That(Row(fixture).SpaceHeating, Is.EqualTo(quietSpace));
            Assert.That(hotel.SignalGuestActivityReady(id, GuestAgentState.InRoom, GuestActivity.QuietRest).Success, Is.False);
            Assert.That(Row(fixture).HotWater, Is.Zero);
            Require(hotel.SignalGuestActivityReady(id, GuestAgentState.PerformingActivity, GuestActivity.Shower));
            Assert.That(Row(fixture).HotWater, Is.EqualTo(1.05f * (1.6f - .85f)).Within(.00001f));
            SumMatchesBoiler(fixture);
            Require(hotel.ForceActivity(id, GuestActivity.Work));
            Assert.That(Row(fixture).HotWater, Is.Zero, "Walking away from a shower closes that tap immediately.");
            Assert.That(hotel.SignalGuestActivityReady(id, GuestAgentState.PerformingActivity, GuestActivity.Shower).Success, Is.False);
            SumMatchesBoiler(fixture);
        }

        [Test]
        public void ClosedOwnAndOtherValvesCannotCancelAnActualShower()
        {
            var fixture = Create(); StartShower(fixture);
            float hotWater = Row(fixture).HotWater;
            Require(fixture.Hotel.SetRadiatorSetting(0, 101, 0));
            Assert.That(Row(fixture).SpaceHeating, Is.Zero);
            Assert.That(Row(fixture).HotWater, Is.EqualTo(hotWater));
            foreach (var room in fixture.Rooms.Where(room => room.Profile.Id != 101))
                Require(fixture.Hotel.SetRadiatorSetting(0, room.Profile.Id, 0));
            Assert.That(fixture.Hotel.Boiler.Load, Is.EqualTo(hotWater).Within(.00001f),
                "Five closed vacant valves cannot subtract from the occupied room's open shower.");
            Require(fixture.Hotel.SetRadiatorSetting(0, 102, 3));
            Assert.That(Row(fixture, 102).SpaceHeating, Is.EqualTo(.08f * 1.5f).Within(.00001f));
            Assert.That(Row(fixture).HotWater, Is.EqualTo(hotWater));
            SumMatchesBoiler(fixture);
        }

        [Test]
        public void AwayOwnerKeepsRadiatorDemandAndHeatButCannotUseHotWaterInTransitOrOutside()
        {
            var fixture = Create(); StartShower(fixture);
            Require(fixture.Hotel.SetRadiatorSetting(0, 101, 3));
            float space = Row(fixture).SpaceHeating;
            Require(fixture.Hotel.ForceLeaveRoom(fixture.Guest.GuestId));
            Assert.That(Row(fixture).HotWater, Is.Zero);
            Assert.That(Row(fixture).SpaceHeating, Is.EqualTo(space));
            Require(fixture.Hotel.SignalGuestLeftRoom(fixture.Guest.GuestId));
            Assert.That(fixture.Guest.Agent.CurrentLocation, Is.EqualTo(GuestLocation.Away));
            Assert.That(Row(fixture).GuestId, Is.EqualTo(fixture.Guest.GuestId));
            Assert.That(Row(fixture).SpaceHeating, Is.EqualTo(space));
            var room = fixture.Rooms.Single(item => item.Profile.Id == 101);
            room.Temperature = 10;
            new RoomSystem(fixture.Settings, fixture.Hotel.InfrastructureSettings).TickTemperature(new[] { room }, 1, 60);
            Assert.That(room.Temperature, Is.GreaterThan(18), "The still-open radiator really heats the absent guest's room.");
            Require(fixture.Hotel.ForceReturnRoom(fixture.Guest.GuestId));
            Assert.That(Row(fixture).HotWater, Is.Zero);
            Assert.That(Row(fixture).SpaceHeating, Is.EqualTo(space));
            Require(fixture.Hotel.SignalGuestReturnedRoom(fixture.Guest.GuestId));
            Assert.That(Row(fixture).HotWater, Is.Zero);
            SumMatchesBoiler(fixture);
        }

        [TestCase(GuestActivity.LoudRoom)]
        [TestCase(GuestActivity.PhoneCall)]
        [TestCase(GuestActivity.WatchTV)]
        [TestCase(GuestActivity.Work)]
        public void OtherStagedActivitiesDoNotInventHotWaterOrChangeRadiatorDemand(GuestActivity activity)
        {
            var fixture = Create(); float space = Row(fixture).SpaceHeating;
            Require(fixture.Hotel.ForceActivity(fixture.Guest.GuestId, activity));
            Require(fixture.Hotel.SignalGuestActivityReady(fixture.Guest.GuestId, GuestAgentState.PerformingActivity, activity));
            Assert.That(Row(fixture).SpaceHeating, Is.EqualTo(space));
            Assert.That(Row(fixture).HotWater, Is.Zero);
            SumMatchesBoiler(fixture);
        }

        [Test]
        public void DuplicateOrStaleRoomOwnershipCannotDuplicateAGuestConsumer()
        {
            var fixture = Create(); StartShower(fixture);
            var wrongRoom = fixture.Rooms.Single(room => room.Profile.Id == 102);
            wrongRoom.GuestId = fixture.Guest.GuestId; // Deliberately malformed world reference; do not send it as a valid snapshot.
            Assert.That(Row(fixture, 102).GuestId, Is.Null);
            Assert.That(Row(fixture, 102).SpaceHeating, Is.EqualTo(.08f));
            Assert.That(Row(fixture, 102).HotWater, Is.Zero);
            Assert.That(fixture.Hotel.HeatingDemands.Count(row => row.GuestId == fixture.Guest.GuestId), Is.EqualTo(1));
            var thermal = new RoomSystem(fixture.Settings);
            Assert.That(thermal.HeatingDemandForRoom(wrongRoom, fixture.Guest, fixture.Living).HotWater, Is.Zero);
            var original = fixture.Rooms.Single(room => room.Profile.Id == 101);
            original.GuestId = "stale-owner";
            Assert.That(thermal.HeatingDemandForRoom(original, fixture.Guest, fixture.Living).GuestId, Is.Null);
            Assert.That(thermal.HeatingDemandForRoom(original, fixture.Guest, fixture.Living).HotWater, Is.Zero);
        }

        [Test]
        public void CommittedRelocationDropsOldRoomOwnershipAndStartsNewProfileDemandOnlyAtArrival()
        {
            var fixture = Create(); string id = fixture.Guest.GuestId;
            Require(ModelKeyHandoff.MoveGuest(fixture.Hotel, 0, id, 103));
            Assert.That(Row(fixture, 101).GuestId, Is.Null);
            Assert.That(Row(fixture, 103).GuestId, Is.Null, "A destination key reserves the room until the guest physically arrives.");
            Assert.That(fixture.Hotel.HeatingDemands.All(row => row.HotWater == 0), Is.True);
            SumMatchesBoiler(fixture);
            Require(fixture.Hotel.SignalGuestReachedRoom(id));
            Assert.That(Row(fixture, 103).GuestId, Is.EqualTo(id));
            Assert.That(Row(fixture, 103).SpaceHeating, Is.EqualTo(1.05f * .85f).Within(.00001f));
            SumMatchesBoiler(fixture);
        }

        [Test]
        public void CheckoutRemovesProfileDemandBeforeDepartureBodyIsGone()
        {
            var fixture = Create(); var guest = fixture.Guest;
            // The registered model guest stays unstaged. Checkout must outrank anchor waiting.
            fixture.Hotel.Tick(guest.Agent.CheckoutTime - fixture.Hotel.Elapsed + .25f);
            Assert.That(guest.ReceiptPosted, Is.True);
            Assert.That(guest.Agent.State, Is.EqualTo(GuestAgentState.CheckingOut));
            Assert.That(Row(fixture).GuestId, Is.Null);
            Assert.That(Row(fixture).HotWater, Is.Zero);
            Assert.That(Row(fixture).SpaceHeating, Is.EqualTo(.0864f).Within(.00001f));
            SumMatchesBoiler(fixture);
        }

        [Test]
        public void ShowerIncrementCannotBecomeNegativeUnderCustomActivityTuning()
        {
            var fixture = Create(living: new LivingHotelSettings(firstActivityDelay: 1000,
                quietDemandMultiplier: 1.7f, showerDemandMultiplier: .8f));
            StartShower(fixture);
            Assert.That(Row(fixture).HotWater, Is.Zero);
            Assert.That(Row(fixture).SpaceHeating, Is.GreaterThan(0));
            SumMatchesBoiler(fixture);
        }

        [Test]
        public void JsonMirrorDerivesSameRoomRowsAndCannotChangeThemByTickOrValveCommand()
        {
            var fixture = Create(); StartShower(fixture);
            Require(fixture.Hotel.SetRadiatorSetting(0, 101, 0));
            Require(fixture.Hotel.SetRadiatorSetting(0, 106, 3));
            var mirror = Create(false); mirror.Hotel.EnableReadOnlyMirror();
            var packet = JsonUtility.FromJson<HotelModelSnapshot>(JsonUtility.ToJson(fixture.Hotel.CaptureSnapshot(65, 1)));
            Require(mirror.Hotel.ApplySnapshot(packet));
            Assert.That(mirror.Hotel.HeatingDemands, Is.EqualTo(fixture.Hotel.HeatingDemands));
            SumMatchesBoiler(mirror);
            Assert.That(mirror.Hotel.SetRadiatorSetting(0, 101, 3).Success, Is.False);
            mirror.Hotel.Tick(20);
            Assert.That(mirror.Hotel.HeatingDemands, Is.EqualTo(fixture.Hotel.HeatingDemands));
            SumMatchesBoiler(mirror);
        }

        [Test]
        public void LegacyShiftRetainsOriginalActivityMultiplierAndSignedValveCorrection()
        {
            var fixture = Create(continuous: false); StartShower(fixture);
            Assert.That(fixture.Hotel.Boiler.CapacityModelEnabled, Is.False);
            Assert.That(fixture.Hotel.Boiler.Load, Is.EqualTo(1.05f * 1.6f).Within(.00001f));
            Require(fixture.Hotel.SetRadiatorSetting(0, 102, 0));
            Assert.That(fixture.Hotel.Boiler.Load, Is.EqualTo(1.05f * 1.6f - .25f).Within(.00001f));
            Require(fixture.Hotel.ForceLeaveRoom(fixture.Guest.GuestId));
            Assert.That(fixture.Hotel.Boiler.Load, Is.Zero);
        }

        [Test]
        public void InvalidNewDemandCoefficientsAreRejectedAndCustomValuesReachTheRoomCalculation()
        {
            foreach (float value in new[] { -.01f, float.NaN, float.PositiveInfinity })
            {
                Assert.Throws<ArgumentException>(() => new RoomInfrastructureSettings(vacantRadiatorDemand: value));
                Assert.Throws<ArgumentException>(() => new RoomInfrastructureSettings(heatLossDemandFactor: value));
            }
            var fixture = Create(false);
            var custom = new RoomSystem(fixture.Settings, new RoomInfrastructureSettings(vacantRadiatorDemand: .2f, heatLossDemandFactor: .1f));
            Assert.That(custom.HeatingDemandForRoom(fixture.Rooms[0], null, fixture.Living).SpaceHeating,
                Is.EqualTo(.24f).Within(.00001f));
        }
    }
}
