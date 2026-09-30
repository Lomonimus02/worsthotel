using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace WorstHotel.Tests
{
    /// <summary>Paid upgrades against actual room/heater registries. Headless travel, keys and device placement are explicit model adapters.</summary>
    public sealed class ContinuousUpgradeTests
    {
        static readonly GuestKind[] Mixed = { GuestKind.Budget, GuestKind.ColdSensitive, GuestKind.Business,
            GuestKind.Budget, GuestKind.ColdSensitive, GuestKind.Business };
        static readonly float[] RoomLoss = { 0, 1.4f, .2f, .4f, .3f, .7f };

        sealed class Fixture
        {
            public SessionSettings Settings;
            public RoomState[] Rooms;
            public HotelSimulation Hotel;
        }

        static void Require(CommandResult result) => Assert.That(result.Success, Is.True, result.Message);
        static string State(HotelSimulation hotel) => JsonUtility.ToJson(hotel.CaptureSnapshot(77, 1));

        static Fixture Create(int cash = 5000, int occupancy = 0, bool open = true, bool continuous = true)
        {
            var profiles = new[]
            {
                new GuestProfile(GuestKind.Budget, "Budget", "", 180, .85f, 18, .7f, 28, 90, .55f),
                new GuestProfile(GuestKind.ColdSensitive, "Cold", "", 300, 1.05f, 20, 1.35f, 16, 65, .4f),
                new GuestProfile(GuestKind.Business, "Business", "", 450, 1, 19.5f, 1, 10, 45, .25f)
            };
            var settings = new SessionSettings(profiles, Enumerable.Range(0, 6).Select(index =>
                new RoomProfile(101 + index, "Room " + (101 + index), heatLoss: RoomLoss[index], noise: 0, temperature: 22)),
                new BoilerSettings(), new EconomySettings(startingCash: cash));
            var rooms = settings.Rooms.Select(profile => new RoomState(profile)).ToArray();
            var hotel = new HotelSimulation(settings, rooms, new LivingHotelSettings(firstActivityDelay: 1000),
                services: new GuestServiceSettings(eligibility: 0, naturalCommunicationEnabled: true,
                    selfResponseObserveSeconds: 1000, toleranceSeconds: 1000),
                operations: continuous ? new OperationsSettings() : null);
            var fixture = new Fixture { Settings = settings, Rooms = rooms, Hotel = hotel };
            if (open && continuous) Require(hotel.StartOperations());
            if (occupancy > 0) AddGuests(fixture, 101, Mixed.Take(occupancy).ToArray());
            return fixture;
        }

        static void AddGuests(Fixture fixture, int firstRoom, GuestKind[] kinds)
        {
            var hotel = fixture.Hotel;
            var existing = hotel.Guests.Select(guest => guest.GuestId).ToArray();
            for (int index = 0; index < kinds.Length; index++) Require(hotel.DebugSpawnGuest(kinds[index], firstRoom + index));
            hotel.Tick(1.25f); // Explicit diagnostic reservations still wait for their real scheduled arrivals.
            foreach (var guest in hotel.Guests.Where(guest => !existing.Contains(guest.GuestId)))
            {
                // Headless boundary adapters execute normal key ownership and room commands.
                Require(hotel.SignalGuestReachedReception(guest.GuestId));
                Require(ModelKeyHandoff.CheckIn(hotel, 0, guest.GuestId));
                Require(hotel.SignalGuestReachedRoom(guest.GuestId));
                Require(hotel.RegisterGuestPhysicalStaging(guest.GuestId));
                Require(hotel.SignalGuestActivityReady(guest.GuestId, GuestAgentState.InRoom, GuestActivity.QuietRest));
            }
        }

        static void AddHeater(Fixture fixture, string id, int roomId)
        {
            // Explicit placement adapter: register a real consumer, assign its room, switch it on.
            // This is not a claim that this EditMode test carried a rendered heater.
            Require(fixture.Hotel.Heaters.Register(id));
            Require(fixture.Hotel.Heaters.AssignRoom(id, roomId));
            Require(fixture.Hotel.Heaters.SetSwitchedOn(id, true));
            fixture.Hotel.RefreshElectrical();
        }

        static void AdvanceTo(HotelSimulation hotel, float target)
        {
            while (hotel.Elapsed < target) hotel.Tick(Math.Min(1, target - hotel.Elapsed));
        }

        static void PatchFixture(Fixture fixture)
        {
            // Labelled initial failure plus authenticated model relief isolate an already-paid patch.
            var hotel = fixture.Hotel; hotel.Boiler.ForceFailure();
            Require(hotel.Boiler.SetRelief(0, true));
            float time = (hotel.Boiler.Pressure - fixture.Settings.Boiler.ReliefTarget) / fixture.Settings.Boiler.ReliefRate;
            AdvanceTo(hotel, hotel.Elapsed + time);
            Require(hotel.EmergencyPatchBoiler(1));
        }

        [TestCase(-1)]
        [TestCase(2)]
        public void InvalidActorCannotDebitOrInstallEitherUpgrade(int actor)
        {
            var fixture = Create(); string before = State(fixture.Hotel);
            Assert.That(fixture.Hotel.PurchaseBoilerUpgrade(actor).Success, Is.False);
            Assert.That(fixture.Hotel.PurchaseElectricalUpgrade(actor, "A").Success, Is.False);
            Assert.That(State(fixture.Hotel), Is.EqualTo(before));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("C")]
        public void UnknownBranchCannotConsumeEitherElectricalPurchase(string circuit)
        {
            var fixture = Create(); string before = State(fixture.Hotel);
            Assert.That(fixture.Hotel.PurchaseElectricalUpgrade(0, circuit).Success, Is.False);
            Assert.That(State(fixture.Hotel), Is.EqualTo(before));
            Assert.That(fixture.Hotel.Electrical.Circuits.Any(c => fixture.Hotel.Electrical.IsCapacityUpgraded(c.Id)), Is.False);
        }

        [TestCase(true, 1799)]
        [TestCase(false, 1199)]
        public void UnaffordableUpgradeLeavesCashCapacityAndPurchaseFlagsUnchanged(bool boiler, int cash)
        {
            var fixture = Create(cash); string before = State(fixture.Hotel);
            Assert.That(cash, Is.LessThan(boiler ? fixture.Settings.Economy.BoilerUpgradeCost : fixture.Settings.Economy.ElectricalUpgradeCost));
            var result = boiler ? fixture.Hotel.PurchaseBoilerUpgrade(0) : fixture.Hotel.PurchaseElectricalUpgrade(0, "B");
            Assert.That(result.Success, Is.False);
            Assert.That(State(fixture.Hotel), Is.EqualTo(before));
        }

        [TestCase("A", "B")]
        [TestCase("B", "A")]
        public void EachBranchUpgradeDebitsOnceAndLeavesTheOtherAvailable(string first, string second)
        {
            var fixture = Create(); var hotel = fixture.Hotel;
            int cash = hotel.Economy.Cash;
            Require(hotel.PurchaseBoilerUpgrade(0));
            Require(hotel.PurchaseElectricalUpgrade(1, first));
            int spent = fixture.Settings.Economy.BoilerUpgradeCost + fixture.Settings.Economy.ElectricalUpgradeCost;
            Assert.That(hotel.Economy.Cash, Is.EqualTo(cash - spent));
            Assert.That(hotel.PeriodCapitalSpend, Is.EqualTo(spent));
            Assert.That(hotel.Boiler.CapacityUpgradePurchased, Is.True);
            Assert.That(hotel.Electrical.IsCapacityUpgraded(first), Is.True);
            Assert.That(hotel.Electrical.Find(second).Capacity, Is.EqualTo(hotel.ElectricitySettings.CircuitCapacity));
            Require(hotel.PurchaseElectricalUpgrade(0, second));
            spent += fixture.Settings.Economy.ElectricalUpgradeCost;
            Assert.That(hotel.Economy.Cash, Is.EqualTo(cash - spent));
            Assert.That(hotel.PeriodCapitalSpend, Is.EqualTo(spent));
            Assert.That(hotel.Electrical.Circuits.All(c => hotel.Electrical.IsCapacityUpgraded(c.Id) &&
                c.Capacity == hotel.ElectricitySettings.CircuitCapacity + hotel.ElectricitySettings.CapacityUpgradeAmount), Is.True);
            string after = State(hotel);
            Assert.That(hotel.PurchaseBoilerUpgrade(1).Success, Is.False);
            Assert.That(hotel.PurchaseElectricalUpgrade(0, "A").Success, Is.False);
            Assert.That(hotel.PurchaseElectricalUpgrade(0, "B").Success, Is.False);
            Assert.That(hotel.PurchaseElectricalUpgrade(0, "unknown").Success, Is.False);
            Assert.That(State(hotel), Is.EqualTo(after));
        }

        [TestCase("A", "B")]
        [TestCase("B", "A")]
        public void UnaffordableSecondBranchPreservesFirstPurchaseAndCash(string first, string second)
        {
            var fixture = Create(cash: 1200); var hotel = fixture.Hotel;
            Require(hotel.PurchaseElectricalUpgrade(0, first));
            string before = State(hotel);
            Assert.That(hotel.PurchaseElectricalUpgrade(1, second).Success, Is.False);
            Assert.That(State(hotel), Is.EqualTo(before));
            Assert.That(hotel.Electrical.IsCapacityUpgraded(first), Is.True);
            Assert.That(hotel.Electrical.IsCapacityUpgraded(second), Is.False);
        }

        [Test]
        public void BoilerUpgradeReducesUtilizationOfTheSameSixOwnedRoomsWithoutErasingWearOrStress()
        {
            var fixture = Create(occupancy: 6); var hotel = fixture.Hotel;
            float initialStress = hotel.Boiler.Stress01;
            hotel.Tick(30); // Actual six-room demand accumulates real overload stress before buying capacity.
            var rows = hotel.HeatingDemands.ToArray();
            Assert.That(rows.All(row => row.GuestId != null && row.HotWater == 0), Is.True);
            Assert.That(hotel.Boiler.LoadOverride, Is.Null);
            float condition = hotel.Boiler.Condition, stress = hotel.Boiler.Stress01, pressure = hotel.Boiler.Pressure;
            float load = hotel.Boiler.Load, ratio = hotel.Boiler.LoadRatio, effective = hotel.Boiler.EffectiveCapacity;
            float rated = hotel.Boiler.RatedCapacity, clock = hotel.Elapsed;
            Assert.That(stress, Is.GreaterThan(0));
            Assert.That(ratio, Is.GreaterThan(1));
            int cash = hotel.Economy.Cash;
            Require(hotel.PurchaseBoilerUpgrade(0));
            Assert.That(hotel.HeatingDemands, Is.EqualTo(rows));
            Assert.That(hotel.Boiler.Load, Is.EqualTo(load));
            Assert.That(hotel.Boiler.Condition, Is.EqualTo(condition));
            Assert.That(hotel.Boiler.Stress01, Is.EqualTo(stress));
            Assert.That(hotel.Boiler.Pressure, Is.EqualTo(pressure));
            Assert.That(hotel.Elapsed, Is.EqualTo(clock));
            Assert.That(hotel.Boiler.RatedCapacity, Is.EqualTo(rated * fixture.Settings.Boiler.Capacity.CapacityUpgradeMultiplier).Within(.0001f));
            Assert.That(hotel.Boiler.EffectiveCapacity, Is.EqualTo(effective * fixture.Settings.Boiler.Capacity.CapacityUpgradeMultiplier).Within(.0001f));
            Assert.That(hotel.Boiler.LoadRatio, Is.LessThan(ratio));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(cash - fixture.Settings.Economy.BoilerUpgradeCost));
            Assert.That(hotel.Boiler.CapacityBand, Is.EqualTo(CapacityBand.Strained));
            hotel.Tick(20);
            Assert.That(hotel.Boiler.Load, Is.EqualTo(load), "The upgrade cannot silently replace or turn down the room consumers.");
            float beforeGainPerSecond = (stress - initialStress) / 30;
            float afterGainPerSecond = (hotel.Boiler.Stress01 - stress) / 20;
            Assert.That(afterGainPerSecond, Is.GreaterThan(0).And.LessThan(beforeGainPerSecond * .5f),
                "These same six rooms still strain the upgraded historical4.2-rated fixture; accumulation is substantially slower, not reversed.");

            // Explicit headless staff access/valve adapters. The player really removes these
            // two room heat consumers; the upgrade itself did not do that work or preserve their comfort.
            float beforeReduction = hotel.Boiler.Stress01;
            foreach (int roomId in new[] { 101, 104 })
            {
                Require(hotel.RequestStaffRoomAccess(0, roomId));
                Require(hotel.SetRadiatorSetting(0, roomId, 0));
                Assert.That(hotel.HeatingDemands.Single(row => row.RoomId == roomId).Total, Is.Zero);
            }
            Assert.That(hotel.Boiler.Load, Is.LessThan(load));
            Assert.That(hotel.Boiler.LoadRatio, Is.LessThan(fixture.Settings.Boiler.Capacity.StrainedLoadRatio));
            Assert.That(hotel.Boiler.Stress01, Is.EqualTo(beforeReduction), "A valve command does not erase stored stress.");
            Assert.That(hotel.Boiler.HeatingOutput, Is.EqualTo(1));
            hotel.Tick(20);
            Assert.That(hotel.Boiler.Stress01, Is.GreaterThan(0).And.LessThan(beforeReduction),
                "Only the actual additional load reduction and elapsed safe operation recover stress.");
            Assert.That(hotel.Boiler.Failed, Is.False);
        }

        [Test]
        public void OneActualHeaterCanFitAfterChosenBranchUpgradeButTheTripNeedsResetAndTwoStillOverload()
        {
            // A supplies odd rooms: populate 101, 103 and 105 to produce 3 * .85 + 2 heater units.
            var fixture = Create(occupancy: 5); var hotel = fixture.Hotel;
            AddHeater(fixture, "upgrade-heater-1", 101);
            var circuit = hotel.Electrical.Find("A");
            Assert.That(circuit.LoadOverride, Is.Null);
            Assert.That(circuit.ActualRequestedLoad, Is.EqualTo(4.55f).Within(.0001f));
            hotel.Tick(hotel.ElectricitySettings.TripSeconds + 1);
            Assert.That(circuit.Tripped, Is.True);
            float storedStress = circuit.OverloadSeconds;
            var consumerIds = hotel.Electrical.Consumers.Select(consumer => consumer.Id).ToArray();
            Require(hotel.PurchaseElectricalUpgrade(0, "A"));
            Assert.That(circuit.Capacity, Is.EqualTo(hotel.ElectricitySettings.CircuitCapacity + hotel.ElectricitySettings.CapacityUpgradeAmount));
            Assert.That(circuit.Tripped, Is.True, "Purchasing capacity is not a breaker reset.");
            Assert.That(circuit.ActualDeliveredLoad, Is.Zero);
            Assert.That(circuit.OverloadSeconds, Is.EqualTo(storedStress));
            Assert.That(circuit.ActualRequestedLoad, Is.EqualTo(4.55f).Within(.0001f));
            Assert.That(hotel.Electrical.Consumers.Select(consumer => consumer.Id), Is.EqualTo(consumerIds));
            Assert.That(hotel.Electrical.Find("B").Capacity, Is.EqualTo(hotel.ElectricitySettings.CircuitCapacity));
            Require(hotel.ResetCircuit(0, "A")); // Explicit model equivalent of the player's manual reset; physical lever has separate tests.
            hotel.Tick(hotel.ElectricitySettings.TripSeconds + 1);
            Assert.That(circuit.Tripped, Is.False);
            Assert.That(circuit.ActualDeliveredLoad, Is.EqualTo(circuit.ActualRequestedLoad));
            Assert.That(hotel.Heaters.Find("upgrade-heater-1").EffectiveHeatOutput, Is.GreaterThan(0));
            int trips = circuit.TripCount;
            AddHeater(fixture, "upgrade-heater-2", 103);
            Assert.That(circuit.ActualRequestedLoad, Is.EqualTo(6.55f).Within(.0001f));
            hotel.Tick(hotel.ElectricitySettings.TripSeconds + 1);
            Assert.That(circuit.Tripped, Is.True);
            Assert.That(circuit.TripCount, Is.EqualTo(trips + 1));
            Assert.That(circuit.ActualRequestedLoad, Is.EqualTo(6.55f).Within(.0001f));
            Assert.That(circuit.ActualDeliveredLoad, Is.Zero);
        }

        [TestCase("running")]
        [TestCase("failed")]
        [TestCase("patched")]
        [TestCase("maintenance")]
        public void PurchasingCapacityDoesNotRepairOrCancelTheCurrentBoilerWork(string initialState)
        {
            var fixture = Create(cash: 10000); var hotel = fixture.Hotel;
            if (initialState == "failed") hotel.Boiler.ForceFailure(); // Explicit starting condition, not overload evidence.
            if (initialState == "patched") PatchFixture(fixture);
            if (initialState == "maintenance") Require(hotel.BeginBoilerMaintenance(0));
            float condition = hotel.Boiler.Condition, stress = hotel.Boiler.Stress01, pressure = hotel.Boiler.Pressure;
            float deadline = hotel.Boiler.MaintenanceEndsAt, remaining = hotel.Boiler.MaintenanceRemaining(hotel.Elapsed);
            float load = hotel.Boiler.Load, time = hotel.Elapsed;
            bool failed = hotel.Boiler.Failed, patched = hotel.Boiler.EmergencyPatchActive;
            int recovered = 0; hotel.Boiler.OnFailureResolved += () => recovered++;
            Require(hotel.PurchaseBoilerUpgrade(0));
            Require(hotel.PurchaseElectricalUpgrade(1, "B"));
            Assert.That(hotel.Boiler.Condition, Is.EqualTo(condition));
            Assert.That(hotel.Boiler.Stress01, Is.EqualTo(stress));
            Assert.That(hotel.Boiler.Pressure, Is.EqualTo(pressure));
            Assert.That(hotel.Boiler.Failed, Is.EqualTo(failed));
            Assert.That(hotel.Boiler.EmergencyPatchActive, Is.EqualTo(patched));
            Assert.That(hotel.Boiler.MaintenanceEndsAt, Is.EqualTo(deadline));
            Assert.That(hotel.Boiler.MaintenanceRemaining(hotel.Elapsed), Is.EqualTo(remaining));
            Assert.That(hotel.Boiler.Load, Is.EqualTo(load));
            Assert.That(hotel.Elapsed, Is.EqualTo(time));
            Assert.That(recovered, Is.Zero);
            if (initialState == "maintenance") Assert.That(hotel.Boiler.HeatingOutput, Is.Zero);
            if (initialState == "failed") Assert.That(hotel.Boiler.HeatingOutput, Is.EqualTo(fixture.Settings.Boiler.FailedHeatOutput));
        }

        [Test]
        public void PurchasesPersistAcrossMidnightAndReportsWithCapitalAndMaintenanceRecordedButNeverDebitedAgain()
        {
            var fixture = Create(cash: 7000); var hotel = fixture.Hotel;
            AdvanceTo(hotel, hotel.Calendar.At(2, 0) - 5);
            int initialCash = hotel.Economy.Cash;
            Require(hotel.PurchaseBoilerUpgrade(0)); Require(hotel.PurchaseElectricalUpgrade(0, "B"));
            Require(hotel.PurchaseElectricalUpgrade(1, "A"));
            Require(hotel.BeginBoilerMaintenance(0));
            int capital = fixture.Settings.Economy.BoilerUpgradeCost + 2 * fixture.Settings.Economy.ElectricalUpgradeCost;
            int maintenance = fixture.Settings.Economy.ProperRepairCost;
            Assert.That(hotel.PeriodCapitalSpend, Is.EqualTo(capital));
            AdvanceTo(hotel, hotel.Calendar.At(2, 0) + 1);
            Assert.That(hotel.Boiler.CapacityUpgradePurchased, Is.True);
            Assert.That(hotel.Electrical.IsCapacityUpgraded("B"), Is.True);
            Assert.That(hotel.Boiler.MaintenanceInProgress, Is.True);
            Assert.That(hotel.PeriodCapitalSpend, Is.EqualTo(capital));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(initialCash - capital - maintenance));
            AdvanceTo(hotel, hotel.NextReportAt + .25f);
            var report = hotel.DayReports.Single();
            Assert.That(report.CapitalSpend, Is.EqualTo(capital));
            Assert.That(report.MaintenanceSpend, Is.EqualTo(maintenance));
            Assert.That(report.Net, Is.EqualTo(-capital - maintenance - fixture.Settings.Economy.DailyOperatingCost));
            Assert.That(report.Cash, Is.EqualTo(report.OpeningCash + report.Net));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(initialCash + report.Net));
            Assert.That(hotel.PeriodCapitalSpend, Is.Zero);
            Assert.That(hotel.PeriodMaintenanceSpend, Is.Zero);
            Assert.That(hotel.Boiler.CapacityUpgradePurchased, Is.True);
            Assert.That(hotel.Electrical.IsCapacityUpgraded("B"), Is.True);
            float rated = hotel.Boiler.RatedCapacity, branchCapacity = hotel.Electrical.Find("B").Capacity;
            int cashAfter = hotel.Economy.Cash;
            AdvanceTo(hotel, hotel.NextReportAt + .25f);
            var second = hotel.DayReports.Last();
            Assert.That(second.CapitalSpend, Is.Zero);
            Assert.That(second.MaintenanceSpend, Is.Zero);
            Assert.That(hotel.Economy.Cash, Is.EqualTo(cashAfter - fixture.Settings.Economy.DailyOperatingCost));
            Assert.That(hotel.Boiler.RatedCapacity, Is.EqualTo(rated));
            Assert.That(hotel.Electrical.Find("B").Capacity, Is.EqualTo(branchCapacity));
            Assert.That(hotel.Electrical.IsCapacityUpgraded("A"), Is.True);
            Assert.That(hotel.Electrical.Find("A").Capacity, Is.EqualTo(branchCapacity));
        }

        [Test]
        public void AdditionalRealBookingsCanOverloadAnUpgradedBoilerWithoutRemovingThePurchasedCapacity()
        {
            var fixture = Create(); var hotel = fixture.Hotel;
            AddGuests(fixture, 101, Enumerable.Repeat(GuestKind.ColdSensitive, 3).ToArray());
            Require(hotel.PurchaseBoilerUpgrade(0));
            Assert.That(hotel.Boiler.LoadRatio, Is.LessThan(1));
            float smallLoad = hotel.Boiler.Load, purchasedCapacity = hotel.Boiler.RatedCapacity;
            AddGuests(fixture, 104, Enumerable.Repeat(GuestKind.ColdSensitive, 3).ToArray());
            Assert.That(hotel.Guests.Count, Is.EqualTo(6));
            Assert.That(hotel.HeatingDemands.Count(row => row.GuestId != null), Is.EqualTo(6));
            Assert.That(hotel.Boiler.Load, Is.EqualTo(hotel.HeatingDemands.Sum(row => row.Total)).Within(.00001f));
            Assert.That(hotel.Boiler.Load, Is.GreaterThan(smallLoad));
            Assert.That(hotel.Boiler.LoadRatio, Is.GreaterThan(1));
            Assert.That(hotel.Boiler.RatedCapacity, Is.EqualTo(purchasedCapacity));
            Assert.That(hotel.Boiler.CapacityUpgradePurchased, Is.True);
            Assert.That(hotel.Boiler.LoadOverride, Is.Null);
            float stress = hotel.Boiler.Stress01;
            hotel.Tick(20);
            Assert.That(hotel.Boiler.Stress01, Is.GreaterThan(stress));
            Assert.That(hotel.Boiler.RatedCapacity, Is.EqualTo(purchasedCapacity));
        }

        [Test]
        public void ReadOnlyMirrorUnopenedAndLegacyHotelsCannotPurchaseOrLoseInstalledUpgrades()
        {
            var host = Create(); Require(host.Hotel.PurchaseBoilerUpgrade(0)); Require(host.Hotel.PurchaseElectricalUpgrade(0, "B"));
            var mirror = Create(); mirror.Hotel.EnableReadOnlyMirror();
            Require(mirror.Hotel.ApplySnapshot(JsonUtility.FromJson<HotelModelSnapshot>(JsonUtility.ToJson(host.Hotel.CaptureSnapshot(77, 1)))));
            Assert.That(mirror.Hotel.Boiler.RatedCapacity, Is.EqualTo(host.Hotel.Boiler.RatedCapacity));
            Assert.That(mirror.Hotel.Electrical.IsCapacityUpgraded("B"), Is.True);
            Assert.That(mirror.Hotel.Electrical.Find("B").Capacity, Is.EqualTo(host.Hotel.Electrical.Find("B").Capacity));
            foreach (var hotel in new[] { mirror.Hotel, Create(open: false).Hotel, Create(open: false, continuous: false).Hotel })
            {
                string before = State(hotel);
                Assert.That(hotel.PurchaseBoilerUpgrade(0).Success, Is.False);
                Assert.That(hotel.PurchaseElectricalUpgrade(0, "A").Success, Is.False);
                Assert.That(State(hotel), Is.EqualTo(before));
            }
            string mirrored = State(mirror.Hotel);
            mirror.Hotel.Tick(1000);
            Assert.That(State(mirror.Hotel), Is.EqualTo(mirrored));
        }
    }
}
