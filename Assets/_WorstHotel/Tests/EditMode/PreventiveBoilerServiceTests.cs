using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace WorstHotel.Tests
{
    /// <summary>Paid model service tests; starting wear and headless repair support are explicit controlled fixtures.</summary>
    public sealed class PreventiveBoilerServiceTests
    {
        sealed class Fixture
        {
            public HotelSimulation Hotel;
            public RoomState[] Rooms;
            public SessionSettings Settings;
        }

        static void Require(CommandResult result) => Assert.That(result.Success, Is.True, result.Message);
        static Fixture Create(float condition = 48, int cash = 5000, bool continuous = true, bool open = true,
            BoilerCapacitySettings capacity = null)
        {
            var profiles = new[]
            {
                new GuestProfile(GuestKind.Budget, "Budget", "", 180, .85f, 18, .7f, 28, 90, .55f),
                new GuestProfile(GuestKind.ColdSensitive, "Cold", "", 300, 1.05f, 20, 1.35f, 16, 65, .4f),
                new GuestProfile(GuestKind.Business, "Business", "", 450, 1, 19.5f, 1, 10, 45, .25f)
            };
            var settings = new SessionSettings(profiles, Enumerable.Range(101, 6).Select(id =>
                new RoomProfile(id, "Room " + id, temperature: 22)), new BoilerSettings(initialCondition: condition, capacity: capacity),
                new EconomySettings(startingCash: cash));
            var rooms = settings.Rooms.Select(profile => new RoomState(profile)).ToArray();
            var hotel = new HotelSimulation(settings, rooms, new LivingHotelSettings(),
                operations: continuous ? new OperationsSettings() : null);
            if (continuous && open) Require(hotel.StartOperations());
            return new Fixture { Hotel = hotel, Rooms = rooms, Settings = settings };
        }

        static string State(HotelSimulation hotel) => JsonUtility.ToJson(hotel.CaptureSnapshot(411, 1)) +
            "|" + hotel.Boiler.ActiveServiceKind + ":" + hotel.Boiler.MaintenanceRevision;
        static void AdvanceTo(HotelSimulation hotel, float time)
        { while (hotel.Elapsed < time) hotel.Tick(Math.Min(1, time - hotel.Elapsed)); }
        static void ReadyForPatch(HotelSimulation hotel)
        {
            hotel.Boiler.ForceFailure(); // Controlled starting fault; this is not a failure-generation test.
            Require(hotel.Boiler.SetRelief(0, true));
            hotel.Tick(20); // Authenticated model support; actual physical controls have separate PlayMode tests.
            Assert.That(hotel.Boiler.CanRestart(1).Success, Is.True);
        }
        static void SetRevisionFixture(BoilerSystem boiler, int value) =>
            typeof(BoilerSystem).GetProperty(nameof(BoilerSystem.MaintenanceRevision)).GetSetMethod(true).Invoke(boiler, new object[] { value });

        [TestCase(48, 68)]
        [TestCase(79, 80)]
        public void BasicServiceHasRealThermalDowntimeThenPublishesOneCappedOutcome(float initial, float expected)
        {
            var fixture = Create(initial); var hotel = fixture.Hotel; var boiler = hotel.Boiler;
            int cash = hotel.Economy.Cash, cost = fixture.Settings.Economy.BasicMaintenanceCost;
            int callbacks = 0;
            boiler.OnHeatingOutputChanged += output =>
            {
                callbacks++;
                Assert.That(hotel.Economy.Cash, Is.EqualTo(cash - cost));
                Assert.That(hotel.PeriodMaintenanceSpend, Is.EqualTo(cost));
                Assert.That(boiler.ActiveServiceKind, Is.EqualTo(output == 0 ? BoilerServiceKind.Basic : BoilerServiceKind.None));
                Assert.That(boiler.MaintenanceRevision, Is.EqualTo(output == 0 ? 1 : 2));
                Assert.That(boiler.Condition, Is.EqualTo(output == 0 ? initial : expected));
            };
            string before = State(hotel);
            Require(hotel.CanBeginBoilerMaintenance(0, BoilerServiceKind.Basic, 0));
            Assert.That(State(hotel), Is.EqualTo(before), "Inspection/eligibility must not charge or change the boiler.");
            Require(hotel.BeginBoilerMaintenance(0, BoilerServiceKind.Basic, 0));
            float deadline = boiler.MaintenanceEndsAt;
            Assert.That(deadline, Is.EqualTo(.75f * hotel.Operations.SecondsPerDay / 24));
            Assert.That(boiler.HeatingOutput, Is.Zero);
            AdvanceTo(hotel, deadline - .25f);
            Assert.That(boiler.Condition, Is.EqualTo(initial));
            Assert.That(boiler.MaintenanceRevision, Is.EqualTo(1));
            Assert.That(fixture.Rooms[0].Temperature, Is.LessThan(22));
            AdvanceTo(hotel, deadline);
            float expectedTemperature = fixture.Settings.TemperatureBase + (22 - fixture.Settings.TemperatureBase) *
                (float)Math.Exp(-deadline / fixture.Settings.TemperatureTimeConstant);
            Assert.That(fixture.Rooms[0].Temperature, Is.EqualTo(expectedTemperature).Within(.001f),
                "The entire final downtime step remains heat-off; completion cannot heat the room retroactively.");
            Assert.That(boiler.Condition, Is.EqualTo(expected));
            Assert.That(boiler.MaintenanceEndsAt, Is.Zero);
            Assert.That(boiler.Stress01, Is.Zero);
            Assert.That(boiler.Failed, Is.False);
            Assert.That(callbacks, Is.EqualTo(2));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(cash - cost));
        }

        [Test]
        public void BasicCanRelieveRealStressWithoutReducingHealthierCondition()
        {
            var fixture = Create(95); var hotel = fixture.Hotel;
            hotel.Boiler.OverrideLoad(hotel.Boiler.EffectiveCapacity * 2);
            hotel.Tick(45); // Actual integrated stress, not a field assignment.
            hotel.Boiler.OverrideLoad(null);
            float condition = hotel.Boiler.Condition, stress = hotel.Boiler.Stress01;
            Assert.That(condition, Is.GreaterThan(80)); Assert.That(stress, Is.GreaterThan(.25f));
            Require(hotel.BeginBoilerMaintenance(0, BoilerServiceKind.Basic, 0));
            AdvanceTo(hotel, hotel.Boiler.MaintenanceEndsAt);
            Assert.That(hotel.Boiler.Condition, Is.EqualTo(condition));
            Assert.That(hotel.Boiler.Stress01, Is.EqualTo(stress - .25f).Within(.000001f));
        }

        [Test]
        public void BasicKeepsThePatchAndCapacityPurchaseWhileFullRestoresTheExistingCompleteOutcome()
        {
            var fixture = Create(); var hotel = fixture.Hotel;
            Require(hotel.PurchaseBoilerUpgrade(0));
            ReadyForPatch(hotel); Require(hotel.EmergencyPatchBoiler(1));
            Assert.That(hotel.Boiler.MaintenanceRevision, Is.EqualTo(1));
            Require(hotel.BeginBoilerMaintenance(0, BoilerServiceKind.Basic, 1));
            AdvanceTo(hotel, hotel.Boiler.MaintenanceEndsAt);
            Assert.That(hotel.Boiler.Condition, Is.EqualTo(60));
            Assert.That(hotel.Boiler.Stress01, Is.Zero);
            Assert.That(hotel.Boiler.EmergencyPatchActive, Is.True);
            Assert.That(hotel.Boiler.CapacityUpgradePurchased, Is.True);
            Assert.That(hotel.Boiler.MaintenanceRevision, Is.EqualTo(3));
            Require(hotel.BeginBoilerMaintenance(0)); // Backward-compatible single argument means Full.
            Assert.That(hotel.Boiler.ActiveServiceKind, Is.EqualTo(BoilerServiceKind.Full));
            float duration = hotel.Boiler.MaintenanceEndsAt - hotel.Elapsed;
            Assert.That(duration, Is.EqualTo(60).Within(.00001f));
            AdvanceTo(hotel, hotel.Boiler.MaintenanceEndsAt);
            Assert.That(hotel.Boiler.Condition, Is.EqualTo(95));
            Assert.That(hotel.Boiler.EmergencyPatchActive, Is.False);
            Assert.That(hotel.Boiler.CapacityUpgradePurchased, Is.True);
            Assert.That(hotel.Boiler.MaintenanceRevision, Is.EqualTo(5));
            Assert.That(hotel.PeriodMaintenanceSpend, Is.EqualTo(200 + 350 + 1500));
        }

        [Test]
        public void DuplicateAndOldRevisionCannotPurchaseAnotherServiceAfterTheFirstHasFinished()
        {
            var hotel = Create().Hotel;
            Require(hotel.BeginBoilerMaintenance(0, BoilerServiceKind.Basic, 0));
            string active = State(hotel);
            Assert.That(hotel.BeginBoilerMaintenance(1, BoilerServiceKind.Full, 0).Success, Is.False);
            Assert.That(hotel.BeginBoilerMaintenance(1, BoilerServiceKind.Basic, 1).Success, Is.False);
            Assert.That(State(hotel), Is.EqualTo(active));
            AdvanceTo(hotel, hotel.Boiler.MaintenanceEndsAt);
            Assert.That(hotel.Boiler.Condition, Is.EqualTo(68), "Another Basic would be useful, so only the stale revision should prevent replay.");
            string completed = State(hotel);
            Assert.That(hotel.BeginBoilerMaintenance(1, BoilerServiceKind.Basic, 0).Success, Is.False);
            Assert.That(State(hotel), Is.EqualTo(completed));
            Require(hotel.BeginBoilerMaintenance(1, BoilerServiceKind.Basic, 2));
            Assert.That(hotel.PeriodMaintenanceSpend, Is.EqualTo(700));
        }

        [Test]
        public void EmergencyPatchInvalidatesAnEarlierInspectedServiceChoice()
        {
            var hotel = Create().Hotel;
            int inspectedRevision = hotel.Boiler.MaintenanceRevision;
            ReadyForPatch(hotel); Require(hotel.EmergencyPatchBoiler(1));
            string afterPatch = State(hotel);
            Assert.That(hotel.BeginBoilerMaintenance(0, BoilerServiceKind.Basic, inspectedRevision).Success, Is.False);
            Assert.That(State(hotel), Is.EqualTo(afterPatch));
            Require(hotel.CanBeginBoilerMaintenance(0, BoilerServiceKind.Basic, hotel.Boiler.MaintenanceRevision));
        }

        [TestCase("healthy")]
        [TestCase("failed")]
        [TestCase("poor cash")]
        [TestCase("mirror")]
        [TestCase("closed")]
        [TestCase("legacy")]
        public void IneligibleBasicServiceRefusesBeforeAnyMoneyOrEquipmentMutation(string reason)
        {
            var fixture = Create(condition: reason == "healthy" ? 85 : 48, cash: reason == "poor cash" ? 349 : 5000,
                continuous: reason != "legacy", open: reason != "closed");
            var hotel = fixture.Hotel;
            if (reason == "failed") hotel.Boiler.ForceFailure();
            if (reason == "mirror") hotel.EnableReadOnlyMirror();
            string before = State(hotel);
            Assert.That(hotel.CanBeginBoilerMaintenance(0, BoilerServiceKind.Basic, 0).Success, Is.False);
            Assert.That(hotel.BeginBoilerMaintenance(0, BoilerServiceKind.Basic, 0).Success, Is.False);
            Assert.That(State(hotel), Is.EqualTo(before));
        }

        [Test]
        public void ConfiguredZeroBenefitDoesNotChargeEvenWhenConditionIsBelowTheNormalServiceCap()
        {
            var hotel = Create(capacity: new BoilerCapacitySettings(basicMaintenanceConditionGain: 0,
                basicMaintenanceStressReduction: 0)).Hotel;
            string before = State(hotel);
            Assert.That(hotel.BeginBoilerMaintenance(0, BoilerServiceKind.Basic, 0).Success, Is.False);
            Assert.That(State(hotel), Is.EqualTo(before));
        }

        [TestCase(-1, BoilerServiceKind.Basic, 0)]
        [TestCase(2, BoilerServiceKind.Basic, 0)]
        [TestCase(0, BoilerServiceKind.None, 0)]
        [TestCase(0, (BoilerServiceKind)99, 0)]
        [TestCase(0, BoilerServiceKind.Basic, -2)]
        [TestCase(0, BoilerServiceKind.Basic, 1)]
        public void InvalidStaffKindOrRevisionIsAtomic(int actor, BoilerServiceKind kind, int revision)
        {
            var hotel = Create().Hotel; string before = State(hotel);
            Assert.That(hotel.BeginBoilerMaintenance(actor, kind, revision).Success, Is.False);
            Assert.That(State(hotel), Is.EqualTo(before));
        }

        [Test]
        public void RevisionCapacityIsReservedBeforePaymentAndCompletionCannotOverflow()
        {
            var hotel = Create().Hotel;
            // Labelled boundary fixture: this revision cannot be reached in a short gameplay test.
            SetRevisionFixture(hotel.Boiler, int.MaxValue - 1);
            string before = State(hotel);
            Assert.That(hotel.BeginBoilerMaintenance(0, BoilerServiceKind.Basic, int.MaxValue - 1).Success, Is.False);
            Assert.That(State(hotel), Is.EqualTo(before));
            SetRevisionFixture(hotel.Boiler, int.MaxValue - 2);
            Require(hotel.BeginBoilerMaintenance(0, BoilerServiceKind.Basic, int.MaxValue - 2));
            AdvanceTo(hotel, hotel.Boiler.MaintenanceEndsAt);
            Assert.That(hotel.Boiler.MaintenanceRevision, Is.EqualTo(int.MaxValue));
            ReadyForPatch(hotel);
            before = State(hotel);
            Assert.That(hotel.EmergencyPatchBoiler(1).Success, Is.False);
            Assert.That(State(hotel), Is.EqualTo(before), "Patch exhaustion is checked before debit, not discovered during commit.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void BasicCrossesCalendarBoundaryAndExpenseIsNeverChargedAgain(bool report)
        {
            var fixture = Create(); var hotel = fixture.Hotel;
            float boundary = report ? hotel.NextReportAt : hotel.Calendar.At(2, 0);
            AdvanceTo(hotel, boundary - 10);
            int beforeCash = hotel.Economy.Cash;
            Require(hotel.BeginBoilerMaintenance(0, BoilerServiceKind.Basic, 0));
            float deadline = hotel.Boiler.MaintenanceEndsAt;
            AdvanceTo(hotel, boundary + .25f);
            Assert.That(hotel.Boiler.ActiveServiceKind, Is.EqualTo(BoilerServiceKind.Basic));
            Assert.That(hotel.Boiler.MaintenanceRevision, Is.EqualTo(1));
            Assert.That(hotel.Boiler.HeatingOutput, Is.Zero);
            Assert.That(hotel.Boiler.MaintenanceEndsAt, Is.EqualTo(deadline));
            int expectedCash = beforeCash - fixture.Settings.Economy.BasicMaintenanceCost -
                (report ? fixture.Settings.Economy.DailyOperatingCost : 0);
            Assert.That(hotel.Economy.Cash, Is.EqualTo(expectedCash));
            if (report) Assert.That(hotel.LastReport.MaintenanceSpend, Is.EqualTo(fixture.Settings.Economy.BasicMaintenanceCost));
            AdvanceTo(hotel, deadline);
            Assert.That(hotel.Boiler.MaintenanceRevision, Is.EqualTo(2));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(expectedCash));
        }

        [Test]
        public void ProductionBasicIsCheaperShorterAndLimitedWhileIndependentCustomFullSettingsRemainValid()
        {
            var boilerAsset = AssetDatabase.LoadAssetAtPath<BoilerConfig>("Assets/_WorstHotel/ScriptableObjects/Boiler.asset");
            var economyAsset = AssetDatabase.LoadAssetAtPath<EconomyConfig>("Assets/_WorstHotel/ScriptableObjects/Economy.asset");
            var boiler = boilerAsset.ToData().Capacity; var economy = economyAsset.ToData();
            Assert.That(boiler.BasicMaintenanceHours, Is.EqualTo(.75f).And.LessThan(boiler.MaintenanceHours));
            Assert.That(boiler.BasicMaintenanceConditionGain, Is.EqualTo(20));
            Assert.That(boiler.BasicMaintenanceConditionCap, Is.EqualTo(80).And.LessThan(boiler.ProperMaintenanceCondition));
            Assert.That(boiler.BasicMaintenanceStressReduction, Is.EqualTo(.25f));
            Assert.That(economy.BasicMaintenanceCost, Is.EqualTo(350).And.LessThan(economy.ProperRepairCost));
            Assert.DoesNotThrow(() => new BoilerCapacitySettings(maintenanceHours: .01f));
            Assert.DoesNotThrow(() => new EconomySettings(properRepairCost: 10));
            Assert.Throws<ArgumentException>(() => new BoilerCapacitySettings(basicMaintenanceHours: 0));
            Assert.Throws<ArgumentException>(() => new BoilerCapacitySettings(basicMaintenanceConditionGain: float.NaN));
            Assert.Throws<ArgumentException>(() => new BoilerCapacitySettings(basicMaintenanceConditionCap: 101));
            Assert.Throws<ArgumentException>(() => new BoilerCapacitySettings(basicMaintenanceStressReduction: 1.01f));
            Assert.Throws<ArgumentException>(() => new EconomySettings(basicMaintenanceCost: -1));
        }
    }
}
