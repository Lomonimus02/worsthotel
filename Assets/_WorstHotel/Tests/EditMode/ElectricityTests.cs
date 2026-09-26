using System;
using System.Linq;
using NUnit.Framework;

namespace WorstHotel.Tests
{
    public sealed class ElectricityTests
    {
        private sealed class Fixture
        {
            public SessionSettings Settings;
            public RoomState[] Rooms;
            public HotelSimulation Simulation;
            public RoomState Room(int id) => Rooms.Single(room => room.Profile.Id == id);
            public GuestStay Guest(int index = 0) => Simulation.Guests[index];
            public ElectricalCircuit Circuit(string id = "A") => Simulation.Electrical.Find(id);
        }

        private static Fixture Create(int[] assignedRooms = null, bool receive = true, bool coldRoom101 = false,
            ElectricitySettings electricity = null, HeaterSettings heater = null)
        {
            assignedRooms = assignedRooms ?? new[] { 101 };
            var profiles = new[]
            {
                new GuestProfile(GuestKind.Budget, "Budget", "", 180, 0.85f, 18, 0.7f, 28, 90, 0.55f),
                new GuestProfile(GuestKind.ColdSensitive, "Cold-sensitive", "", 300, 1.05f, 20, 1.35f, 16, 65, 0.4f),
                new GuestProfile(GuestKind.Business, "Business", "", 450, 1, 19.5f, 1, 10, 45, 0.25f)
            };
            // This diagnostic keeps central heating constant to isolate electrical delivery. The
            // ordinary 45-second room thermal response is retained, including in the cold-room trace.
            var settings = new SessionSettings(profiles, Enumerable.Range(101, 6).Select(id => new RoomProfile(id,
                "Room " + id, heatLoss: coldRoom101 && id == 101 ? 13.5f : 0, noise: 0,
                temperature: coldRoom101 && id == 101 ? 16 : 22.5f)),
                new BoilerSettings(), new EconomySettings(), temperatureBase: 22.5f, heatTemperatureGain: 0);
            var rooms = settings.Rooms.Select(profile => new RoomState(profile)).ToArray();
            var simulation = new HotelSimulation(settings, rooms,
                new LivingHotelSettings(firstArrivalSeconds: 0.2f, arrivalJitterSeconds: 0, firstActivityDelay: 1000,
                    activityDurationMin: 60, activityDurationMax: 60),
                new NeedSettings(), new NoiseSettings(), heater ?? new HeaterSettings(), electricity ?? new ElectricitySettings());
            var fixture = new Fixture { Settings = settings, Rooms = rooms, Simulation = simulation };
            var offers = GuestSystem.GenerateApplications(1, settings.GuestArchetypes).Take(assignedRooms.Length).ToArray();
            Assert.That(simulation.StartShift(offers.Select((offer, i) =>
                new BookingAssignment(assignedRooms[i], offer.Id, offer.ReferencePrice, i % 2)), offers).Success, Is.True);
            if (receive)
                for (int i = 0; i < offers.Length; i++)
                {
                    var guest = simulation.Guests[i];
                    AdvanceTo(fixture, guest.Agent.ArrivalTime + 0.2f);
                    Assert.That(simulation.SignalGuestReachedReception(guest.GuestId).Success, Is.True);
                    Assert.That(ModelKeyHandoff.CheckIn(simulation, i % 2, guest.GuestId).Success, Is.True);
                    Assert.That(simulation.SignalGuestReachedRoom(guest.GuestId).Success, Is.True);
                }
            simulation.RefreshElectrical();
            return fixture;
        }

        private static void AdvanceTo(Fixture fixture, float until)
        {
            for (int i = 0; i < 2000 && fixture.Simulation.Elapsed + 0.00001f < until; i++)
                fixture.Simulation.Tick(Math.Min(0.2f, until - fixture.Simulation.Elapsed));
            Assert.That(fixture.Simulation.Elapsed, Is.EqualTo(until).Within(0.002f));
        }

        private static void TickFor(Fixture fixture, float duration) => AdvanceTo(fixture, fixture.Simulation.Elapsed + duration);

        private static PortableHeaterState PlaceHeater(Fixture fixture, string id = "heater", int roomId = 101)
        {
            Assert.That(fixture.Simulation.Heaters.Register(id).Success, Is.True);
            Assert.That(fixture.Simulation.Heaters.AssignRoom(id, roomId).Success, Is.True);
            Assert.That(fixture.Simulation.Heaters.SetSwitchedOn(id, true).Success, Is.True);
            fixture.Simulation.RefreshElectrical();
            return fixture.Simulation.Heaters.Find(id);
        }

        [Test]
        public void AuthoredCircuitsAndThreeOrdinaryRoomsHaveHeadroomWithoutAHeater()
        {
            var fixture = Create(new[] { 101, 102, 103 });
            Assert.That(fixture.Simulation.Electrical.Circuits.Count, Is.EqualTo(2));
            Assert.That(fixture.Circuit("A").RoomIds, Is.EqualTo(new[] { 101, 102, 103 }));
            Assert.That(fixture.Circuit("B").RoomIds, Is.EqualTo(new[] { 104, 105, 106 }));
            Assert.That(fixture.Circuit().Capacity, Is.EqualTo(4));
            Assert.That(fixture.Circuit().RequestedLoad, Is.EqualTo(3 * 0.85f).Within(0.0001f));
            foreach (var guest in fixture.Simulation.Guests)
                Assert.That(fixture.Simulation.ForceActivity(guest.GuestId, GuestActivity.LoudRoom).Success, Is.True);
            TickFor(fixture, 25);
            Assert.That(fixture.Circuit().RequestedLoad, Is.EqualTo(3 * (0.85f + 0.25f)).Within(0.0001f));
            Assert.That(fixture.Circuit().Tripped || fixture.Circuit().Warning, Is.False);
            Assert.That(fixture.Circuit().OverloadSeconds, Is.Zero);
            Assert.That(fixture.Rooms.All(room => room.HasPower), Is.True);
            Assert.That(fixture.Circuit("B").RequestedLoad, Is.Zero);
        }

        [Test]
        public void ReservationsAndRoomTravelDoNotCreateConsumersAndRelocationMigratesExactlyOnce()
        {
            var fixture = Create(receive: false);
            var guest = fixture.Guest();
            Assert.That(fixture.Simulation.Electrical.Consumers, Is.Empty);
            AdvanceTo(fixture, guest.Agent.ArrivalTime + 0.2f);
            Assert.That(fixture.Simulation.SignalGuestReachedReception(guest.GuestId).Success, Is.True);
            Assert.That(ModelKeyHandoff.CheckIn(fixture.Simulation, 0, guest.GuestId).Success, Is.True);
            fixture.Simulation.RefreshElectrical();
            Assert.That(fixture.Room(101).Occupied, Is.True);
            Assert.That(fixture.Circuit().RequestedLoad, Is.Zero, "The checked-in guest is still walking to their room.");
            Assert.That(fixture.Simulation.SignalGuestReachedRoom(guest.GuestId).Success, Is.True);
            fixture.Simulation.RefreshElectrical();
            Assert.That(fixture.Circuit().RequestedLoad, Is.EqualTo(0.85f).Within(0.0001f));
            var id = fixture.Simulation.Electrical.Consumers.Single().Id;
            Assert.That(ModelKeyHandoff.MoveGuest(fixture.Simulation, 0, guest.GuestId, 104).Success, Is.True);
            fixture.Simulation.RefreshElectrical();
            Assert.That(fixture.Simulation.Electrical.Consumers, Is.Empty);
            Assert.That(fixture.Circuit().RequestedLoad + fixture.Circuit("B").RequestedLoad, Is.Zero);
            Assert.That(fixture.Simulation.SignalGuestReachedRoom(guest.GuestId).Success, Is.True);
            fixture.Simulation.RefreshElectrical();
            Assert.That(fixture.Circuit().RequestedLoad, Is.Zero);
            Assert.That(fixture.Circuit("B").RequestedLoad, Is.EqualTo(0.85f).Within(0.0001f));
            Assert.That(fixture.Simulation.Electrical.Consumers.Single().Id, Is.EqualTo(id));
            Assert.That(fixture.Simulation.Electrical.Consumers.Single().RoomId, Is.EqualTo(104));
            AdvanceTo(fixture, guest.Agent.CheckoutTime + 0.2f);
            Assert.That(fixture.Simulation.Electrical.Consumers, Is.Empty);
            Assert.That(fixture.Circuit("B").RequestedLoad, Is.Zero);
        }

        [Test]
        public void UniqueRequestedConsumersPersistThroughPowerCutWhileDeliveryAndHeaterHeatStop()
        {
            var fixture = Create(new[] { 101, 102, 103 });
            var heater = PlaceHeater(fixture);
            Assert.That(fixture.Simulation.Heaters.Register(heater.Id).Success, Is.False);
            fixture.Simulation.RefreshElectrical();
            var consumers = fixture.Simulation.Electrical.Consumers;
            Assert.That(consumers.Count, Is.EqualTo(4));
            Assert.That(consumers.Select(consumer => consumer.Id).Distinct().Count(), Is.EqualTo(4));
            Assert.That(fixture.Circuit().RequestedLoad, Is.EqualTo(4.55f).Within(0.0001f));
            Assert.That(consumers.Sum(consumer => consumer.RequestedLoad), Is.EqualTo(fixture.Circuit().RequestedLoad).Within(0.0001f));
            Assert.That(fixture.Simulation.DebugTripCircuit("A").Success, Is.True);
            Assert.That(fixture.Circuit().RequestedLoad, Is.EqualTo(4.55f).Within(0.0001f));
            Assert.That(fixture.Circuit().DeliveredLoad, Is.Zero);
            Assert.That(fixture.Simulation.Electrical.Consumers.All(consumer => consumer.DeliveredLoad == 0), Is.True);
            Assert.That(heater.Powered, Is.False);
            Assert.That(heater.EffectiveHeatOutput, Is.Zero);
            Assert.That(heater.DemandedElectricalLoad, Is.EqualTo(2));
            Assert.That(fixture.Rooms.Where(room => room.Profile.Id <= 103).All(room => !room.HasPower), Is.True);
            Assert.That(fixture.Rooms.Where(room => room.Profile.Id >= 104).All(room => room.HasPower), Is.True);
            TickFor(fixture, 2);
            Assert.That(fixture.Circuit().RequestedLoad, Is.EqualTo(4.55f).Within(0.0001f),
                "A blackout must not erase the consumers that will overload the breaker again.");
        }

        [Test]
        public void ExactCapacityAndLightBookingWithOneLoudGuestRemainSafe()
        {
            var exact = Create(new[] { 101, 102 }, electricity: new ElectricitySettings(occupiedRoomLoad: 1));
            PlaceHeater(exact);
            TickFor(exact, 25);
            Assert.That(exact.Circuit().RequestedLoad, Is.EqualTo(exact.Circuit().Capacity).Within(0.0001f));
            Assert.That(exact.Circuit().Tripped || exact.Circuit().Warning, Is.False);
            Assert.That(exact.Circuit().OverloadSeconds, Is.Zero);
            var light = Create(new[] { 101, 102 });
            PlaceHeater(light);
            light.Simulation.ForceActivity(light.Guest().GuestId, GuestActivity.LoudRoom);
            TickFor(light, 25);
            Assert.That(light.Circuit().RequestedLoad, Is.EqualTo(3.95f).Within(0.0001f));
            Assert.That(light.Circuit().Tripped || light.Circuit().Warning, Is.False);
        }

        [Test]
        public void OverloadNeedsContinuousExposureAndRecoversWhenTheExtraConsumerIsRemoved()
        {
            var fixture = Create(new[] { 101, 102, 103 }, heater: new HeaterSettings(0.01f, 2));
            var heater = PlaceHeater(fixture);
            TickFor(fixture, 5.6f);
            Assert.That(fixture.Circuit().Warning || fixture.Circuit().Tripped, Is.False);
            int beforeWarning = fixture.Simulation.EventRevision;
            TickFor(fixture, 0.8f);
            Assert.That(fixture.Circuit().Warning, Is.True);
            Assert.That(fixture.Circuit().Tripped, Is.False);
            Assert.That(fixture.Simulation.EventRevision, Is.GreaterThan(beforeWarning));
            int stableWarning = fixture.Simulation.EventRevision;
            TickFor(fixture, 1);
            Assert.That(fixture.Simulation.EventRevision, Is.EqualTo(stableWarning), "A continuing warning must not emit the same event each tick.");
            fixture.Simulation.Heaters.SetSwitchedOn(heater.Id, false);
            TickFor(fixture, 0.2f);
            Assert.That(fixture.Circuit().Warning || fixture.Circuit().Tripped, Is.False);
            Assert.That(fixture.Circuit().OverloadSeconds, Is.Zero);
            fixture.Simulation.Heaters.SetSwitchedOn(heater.Id, true);
            TickFor(fixture, 16);
            Assert.That(fixture.Circuit().Tripped, Is.False, "Two separated overload episodes must not share a continuous trip timer.");
            TickFor(fixture, 2.4f);
            Assert.That(fixture.Circuit().Tripped, Is.True);
            Assert.That(fixture.Circuit().TripCount, Is.EqualTo(1));
        }

        [Test]
        public void ResetWithoutReducingDemandRetripsWhileReducedLoadStaysPowered()
        {
            var fixture = Create(new[] { 101, 102, 103 });
            var heater = PlaceHeater(fixture);
            TickFor(fixture, 18.4f);
            Assert.That(fixture.Circuit().Tripped, Is.True);
            Assert.That(fixture.Simulation.ResetCircuit(-1, "A").Success, Is.False);
            Assert.That(fixture.Simulation.ResetCircuit(2, "A").Success, Is.False);
            Assert.That(fixture.Simulation.ResetCircuit(0, "missing").Success, Is.False);
            Assert.That(fixture.Circuit().Tripped, Is.True);
            int revision = fixture.Simulation.EventRevision;
            Assert.That(fixture.Simulation.ResetCircuit(1, "A").Success, Is.True);
            Assert.That(fixture.Circuit().HasPower, Is.True);
            Assert.That(fixture.Circuit().OverloadSeconds, Is.Zero);
            Assert.That(fixture.Simulation.EventRevision, Is.GreaterThan(revision));
            Assert.That(heater.Powered, Is.True);
            Assert.That(fixture.Circuit().RequestedLoad, Is.EqualTo(4.55f).Within(0.0001f));
            TickFor(fixture, 18.4f);
            Assert.That(fixture.Circuit().Tripped, Is.True);
            Assert.That(fixture.Circuit().TripCount, Is.EqualTo(2));
            fixture.Simulation.Heaters.SetSwitchedOn(heater.Id, false);
            fixture.Simulation.RefreshElectrical();
            Assert.That(fixture.Circuit().Tripped, Is.True, "Removing load does not physically reset an open breaker.");
            Assert.That(fixture.Simulation.ResetCircuit(0, "A").Success, Is.True);
            Assert.That(fixture.Simulation.ResetCircuit(0, "A").Success, Is.False, "A healthy breaker must not create another reset event.");
            TickFor(fixture, 30);
            Assert.That(fixture.Circuit().Tripped || fixture.Circuit().Warning, Is.False);
            Assert.That(fixture.Circuit().TripCount, Is.EqualTo(2));
        }

        [Test]
        public void PowerCutSilencesElectricLoudActivityButPreservesShowerAndRequestedActivityLoad()
        {
            var fixture = Create(new[] { 101, 103 });
            var loud = fixture.Guest(0); var shower = fixture.Guest(1);
            fixture.Simulation.ForceActivity(loud.GuestId, GuestActivity.LoudRoom);
            fixture.Simulation.ForceActivity(shower.GuestId, GuestActivity.Shower);
            fixture.Simulation.RefreshElectrical();
            float requested = fixture.Circuit().RequestedLoad;
            Assert.That(fixture.Room(101).SourceNoise, Is.GreaterThan(0));
            Assert.That(fixture.Simulation.DebugTripCircuit("A").Success, Is.True);
            Assert.That(loud.Agent.Activity, Is.EqualTo(GuestActivity.LoudRoom));
            Assert.That(loud.Agent.NoiseOutput, Is.EqualTo(fixture.Simulation.LivingSettings.LoudNoiseOutput));
            Assert.That(fixture.Room(101).SourceNoise, Is.Zero);
            Assert.That(fixture.Room(103).SourceNoise, Is.EqualTo(fixture.Simulation.LivingSettings.ShowerNoiseOutput).Within(0.0001f));
            Assert.That(fixture.Circuit().RequestedLoad, Is.EqualTo(requested).Within(0.0001f));
            Assert.That(fixture.Circuit().DeliveredLoad, Is.Zero);
            Assert.That(fixture.Simulation.ResetCircuit(0, "A").Success, Is.True);
            Assert.That(fixture.Room(101).SourceNoise, Is.EqualTo(fixture.Simulation.LivingSettings.LoudNoiseOutput).Within(0.0001f));
        }

        [Test]
        public void PowerLossContributesOnceToRoomConditionAndDoesNotInventASeparateQualityPenalty()
        {
            var fixture = Create(new[] { 101, 104 });
            var guest = fixture.Guest();
            Assert.That(fixture.Simulation.DebugTripCircuit("A").Success, Is.True);
            float quality = guest.QualityIntegral, elapsed = guest.Elapsed;
            TickFor(fixture, 1);
            float severity = fixture.Simulation.ElectricitySettings.PowerLossConditionSeverity;
            Assert.That(guest.Needs.RoomCondition.Severity, Is.EqualTo(severity).Within(0.0001f));
            Assert.That(guest.Needs.Temperature.Severity + guest.Needs.Noise.Severity + guest.Needs.Service.Severity, Is.Zero);
            Assert.That(guest.Needs.CombinedRoomDeficit, Is.EqualTo(severity).Within(0.0001f));
            Assert.That(guest.QualityIntegral - quality, Is.EqualTo(severity).Within(0.002f));
            Assert.That(guest.Elapsed - elapsed, Is.EqualTo(1).Within(0.002f));
            Assert.That(fixture.Guest(1).Needs.RoomCondition.Severity, Is.Zero);
            Assert.That(fixture.Simulation.Requests.ActiveCount, Is.Zero, "The loss is observed before the complaint exposure threshold.");
            fixture.Room(101).Cleanliness = Cleanliness.Dirty;
            TickFor(fixture, 0.2f);
            Assert.That(guest.Needs.RoomCondition.Severity, Is.EqualTo(1), "Combined condition causes stay normalized in one need.");
            var causes = fixture.Simulation.Incidents.Items.Where(item => item.GuestId == guest.GuestId && item.Reason == IncidentReason.RoomCondition).ToArray();
            Assert.That(causes.Length, Is.EqualTo(2), "Power loss and dirty linen are distinct supported causes, each deduplicated independently.");
            Assert.That(causes.Select(item => item.Cause.SourceType), Is.EquivalentTo(new[] { "Power loss", "Dirty linen" }));
        }

        [Test]
        public void HeaterMovingOrBeingCarriedMigratesItsDemandWithoutGhostRoomConsumers()
        {
            var fixture = Create(new[] { 101, 104 });
            var heater = PlaceHeater(fixture);
            Assert.That(fixture.Circuit().RequestedLoad, Is.EqualTo(2.85f).Within(0.0001f));
            Assert.That(fixture.Simulation.Heaters.AssignRoom(heater.Id, null).Success, Is.True);
            fixture.Simulation.RefreshElectrical();
            Assert.That(fixture.Circuit().RequestedLoad, Is.EqualTo(0.85f).Within(0.0001f));
            var carried = fixture.Simulation.Electrical.Consumers.Single(consumer => consumer.Id == "heater:" + heater.Id);
            Assert.That(carried.RoomId, Is.Null);
            Assert.That(carried.CircuitId, Is.Null);
            Assert.That(carried.RequestedLoad + carried.DeliveredLoad, Is.Zero,
                "A carried registered tool remains inspectable but must not leave a phantom room load.");
            Assert.That(fixture.Simulation.Heaters.AssignRoom(heater.Id, 104).Success, Is.True);
            fixture.Simulation.RefreshElectrical();
            Assert.That(fixture.Circuit("B").RequestedLoad, Is.EqualTo(2.85f).Within(0.0001f));
            Assert.That(fixture.Circuit().RequestedLoad, Is.EqualTo(0.85f).Within(0.0001f));
            var consumer = fixture.Simulation.Electrical.Consumers.Single(item => item.Id == "heater:" + heater.Id);
            Assert.That(consumer.RoomId, Is.EqualTo(104));
            Assert.That(consumer.CircuitId, Is.EqualTo("B"));
            float overload = fixture.Circuit("B").OverloadSeconds;
            for (int i = 0; i < 5; i++) fixture.Simulation.RefreshElectrical();
            Assert.That(fixture.Circuit("B").OverloadSeconds, Is.EqualTo(overload));
            Assert.That(fixture.Simulation.Electrical.Consumers.Select(item => item.Id).Distinct().Count(),
                Is.EqualTo(fixture.Simulation.Electrical.Consumers.Count));
        }

        [Test]
        public void TrippedCircuitPersistsAcrossSettlementAndNewDayButFreshSessionStartsPowered()
        {
            var fixture = Create();
            var heater = PlaceHeater(fixture);
            Assert.That(fixture.Simulation.DebugTripCircuit("A").Success, Is.True);
            fixture.Simulation.EndShift();
            Assert.That(heater.SwitchedOn, Is.False);
            Assert.That(fixture.Circuit().Tripped, Is.True);
            Assert.That(fixture.Circuit().RequestedLoad, Is.Zero);
            Assert.That(fixture.Simulation.ApplyMaintenance(0, MaintenanceChoice.Defer).Success, Is.True);
            var offer = GuestSystem.GenerateApplications(2, fixture.Settings.GuestArchetypes).First();
            Assert.That(fixture.Simulation.StartShift(new[] { new BookingAssignment(101, offer.Id, offer.ReferencePrice, 0) }, new[] { offer }).Success, Is.True);
            Assert.That(fixture.Circuit().Tripped, Is.True);
            Assert.That(fixture.Room(101).HasPower, Is.False);
            Assert.That(heater.SwitchedOn, Is.False);
            var fresh = Create();
            Assert.That(fresh.Circuit().HasPower, Is.True);
            Assert.That(fresh.Circuit().TripCount, Is.Zero);
            Assert.That(fresh.Rooms.All(room => room.HasPower), Is.True);
        }

        [Test]
        public void HeaterOverloadCreatesASecondaryGuestComplaintAndWorseReceiptThanDistributingBookings()
        {
            var overloaded = Create(new[] { 101, 102, 103 }, coldRoom101: true);
            var distributed = Create(new[] { 101, 102, 104 }, coldRoom101: true);
            PlaceHeater(overloaded); PlaceHeater(distributed);
            float initialTemperature = overloaded.Room(101).Temperature;
            float start = overloaded.Simulation.Elapsed, tripTime = -1, complaintTime = -1, warmestBeforeTrip = initialTemperature;
            for (int i = 0; i < 450; i++)
            {
                overloaded.Simulation.Tick(0.2f); distributed.Simulation.Tick(0.2f);
                if (!overloaded.Circuit().Tripped) warmestBeforeTrip = Math.Max(warmestBeforeTrip, overloaded.Room(101).Temperature);
                if (overloaded.Circuit().Tripped && tripTime < 0) tripTime = overloaded.Simulation.Elapsed - start;
                if (complaintTime < 0 && overloaded.Simulation.Requests.Items.Any(request => request.GuestId == overloaded.Guest(1).GuestId &&
                    request.Reason == IncidentReason.RoomCondition && !request.Resolved)) complaintTime = overloaded.Simulation.Elapsed - start;
            }
            Assert.That(tripTime, Is.InRange(17.9f, 18.5f));
            Assert.That(warmestBeforeTrip, Is.GreaterThan(initialTemperature + 1), "The heater must measurably warm its real room before electricity fails.");
            Assert.That(overloaded.Circuit().RequestedLoad, Is.EqualTo(4.55f).Within(0.0001f));
            Assert.That(distributed.Circuit().RequestedLoad, Is.EqualTo(3.7f).Within(0.0001f));
            Assert.That(distributed.Circuit().Tripped, Is.False);
            Assert.That(complaintTime, Is.GreaterThan(tripTime), "The secondary guest's complaint must follow actual power-loss exposure.");
            Assert.That(overloaded.Guest(1).Needs.Temperature.Severity, Is.Zero,
                "This secondary complaint must be power loss, not another artificially cold guest.");
            Assert.That(overloaded.Guest(1).PowerLossExposureSeconds, Is.GreaterThan(0));
            Assert.That(distributed.Guest(1).PowerLossExposureSeconds, Is.Zero);
            Assert.That(distributed.Simulation.Requests.Items.Any(request => request.GuestId == distributed.Guest(1).GuestId && request.Reason == IncidentReason.RoomCondition), Is.False);
            Assert.That(distributed.Room(101).Temperature, Is.GreaterThan(overloaded.Room(101).Temperature));
            float failedTemperature = overloaded.Room(101).Temperature, poweredTemperature = distributed.Room(101).Temperature;
            var failedReport = overloaded.Simulation.EndShift(); var poweredReport = distributed.Simulation.EndShift();
            var failedSecondary = failedReport.Receipts.Single(receipt => receipt.GuestId == overloaded.Guest(1).GuestId);
            var poweredSecondary = poweredReport.Receipts.Single(receipt => receipt.GuestId == distributed.Guest(1).GuestId);
            Assert.That(failedSecondary.Satisfaction, Is.LessThan(poweredSecondary.Satisfaction));
            Assert.That(failedSecondary.Review, Does.Contain("electrical power"), "The review must name the actual power-loss experience.");
            Assert.That(failedReport.Compensation, Is.GreaterThan(poweredReport.Compensation));
            Assert.That(failedReport.Net, Is.LessThan(poweredReport.Net));
            TestContext.WriteLine("Controlled electrical diagnostic (real 45s thermal response): heater 101; three rooms on A demand4.55 vs distributed demand3.70; " +
                "trip after " + tripTime + "s, secondary room102 complaint after " + complaintTime + "s. " +
                "Room101 temperature start " + initialTemperature + ", pretrip peak " + warmestBeforeTrip + ", failed end " + failedTemperature +
                ", powered end " + poweredTemperature + ". Refunds " + failedReport.Compensation + " vs " + poweredReport.Compensation +
                "; room102 satisfaction " + failedSecondary.Satisfaction + " vs " + poweredSecondary.Satisfaction +
                ". No forced trip or mid-run temperature override; natural schedule/balance story belongs to Phase9.");
        }

        [Test]
        public void InvalidElectricalConfigurationAndResetCommandsCannotAlterHealthyPower()
        {
            Assert.Throws<ArgumentException>(() => new ElectricitySettings(circuitCapacity: 0));
            Assert.Throws<ArgumentException>(() => new ElectricitySettings(circuitCapacity: float.NaN));
            Assert.Throws<ArgumentException>(() => new ElectricitySettings(occupiedRoomLoad: -1));
            Assert.Throws<ArgumentException>(() => new ElectricitySettings(loudActivityLoad: float.PositiveInfinity));
            Assert.Throws<ArgumentException>(() => new ElectricitySettings(warningSeconds: 10, tripSeconds: 5));
            Assert.Throws<ArgumentException>(() => new ElectricitySettings(powerLossConditionSeverity: 1.1f));
            var fixture = Create();
            int revision = fixture.Simulation.EventRevision;
            Assert.That(fixture.Simulation.ResetCircuit(0, "A").Success, Is.False);
            Assert.That(fixture.Simulation.DebugTripCircuit("missing").Success, Is.False);
            Assert.That(fixture.Simulation.ResetCircuit(0, null).Success, Is.False);
            Assert.That(fixture.Circuit().HasPower, Is.True);
            Assert.That(fixture.Simulation.EventRevision, Is.EqualTo(revision));
        }
    }
}
