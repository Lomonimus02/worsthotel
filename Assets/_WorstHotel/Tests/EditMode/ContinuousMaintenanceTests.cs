using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace WorstHotel.Tests
{
    /// <summary>Continuous integration tests. Failure setups and headless staff/guest boundary adapters are labelled explicitly.</summary>
    public sealed class ContinuousMaintenanceTests
    {
        sealed class Fixture
        {
            public SessionSettings Settings;
            public RoomState[] Rooms;
            public HotelSimulation Hotel;
        }

        static void Require(CommandResult result) => Assert.That(result.Success, Is.True, result.Message);
        static string State(HotelSimulation hotel) => JsonUtility.ToJson(hotel.CaptureSnapshot(76, 1));

        static Fixture Create(int cash = 5000, bool open = true, bool continuous = true, bool occupied = false)
        {
            var profiles = new[]
            {
                new GuestProfile(GuestKind.Budget, "Budget", "", 180, .85f, 18, .7f, 28, 90, .55f),
                new GuestProfile(GuestKind.ColdSensitive, "Cold", "", 300, 1.05f, 20, 1.35f, 16, 65, .4f),
                new GuestProfile(GuestKind.Business, "Business", "", 450, 1, 19.5f, 1, 10, 45, .25f)
            };
            var settings = new SessionSettings(profiles, Enumerable.Range(101, 6).Select(id =>
                new RoomProfile(id, "Room " + id, noise: 0, temperature: 22)), new BoilerSettings(),
                new EconomySettings(startingCash: cash));
            var rooms = settings.Rooms.Select(profile => new RoomState(profile)).ToArray();
            var hotel = new HotelSimulation(settings, rooms, new LivingHotelSettings(firstActivityDelay: 1000),
                services: new GuestServiceSettings(eligibility: 0, naturalCommunicationEnabled: true,
                    selfResponseObserveSeconds: 1000, toleranceSeconds: 1000),
                operations: continuous ? new OperationsSettings() : null);
            var fixture = new Fixture { Settings = settings, Rooms = rooms, Hotel = hotel };
            if (open && continuous) Require(hotel.StartOperations());
            if (!occupied) return fixture;
            foreach (var room in rooms) Require(hotel.DebugSpawnGuest(GuestKind.ColdSensitive, room.Profile.Id));
            hotel.Tick(1.25f); // Explicit diagnostic reservations materialize at their real scheduled arrival.
            foreach (var guest in hotel.Guests)
            {
                // These are headless travel/key/anchor adapters, not claims about controller or body travel.
                Require(hotel.SignalGuestReachedReception(guest.GuestId));
                Require(ModelKeyHandoff.CheckIn(hotel, 0, guest.GuestId));
                Require(hotel.SignalGuestReachedRoom(guest.GuestId));
                Require(hotel.RegisterGuestPhysicalStaging(guest.GuestId));
                Require(hotel.SignalGuestActivityReady(guest.GuestId, GuestAgentState.InRoom, GuestActivity.QuietRest));
            }
            Assert.That(hotel.HeatingDemands.Count(row => row.GuestId != null), Is.EqualTo(6));
            return fixture;
        }

        static void AdvanceTo(HotelSimulation hotel, float target, float chunk = 1)
        {
            Assert.That(target, Is.GreaterThanOrEqualTo(hotel.Elapsed));
            while (hotel.Elapsed < target) hotel.Tick(Math.Min(chunk, target - hotel.Elapsed));
        }

        static void FailedWithSafeRelief(Fixture fixture)
        {
            // Explicit failure fixture; failure generation itself is covered by capacity stress tests.
            fixture.Hotel.Boiler.ForceFailure();
            Require(fixture.Hotel.Boiler.SetRelief(0, true));
            float seconds = (fixture.Hotel.Boiler.Pressure - fixture.Settings.Boiler.ReliefTarget) / fixture.Settings.Boiler.ReliefRate;
            AdvanceTo(fixture.Hotel, fixture.Hotel.Elapsed + Math.Max(0, seconds));
            Assert.That(fixture.Hotel.Boiler.InRepairBand, Is.True);
            Assert.That(fixture.Hotel.Boiler.ReliefActorId, Is.EqualTo(0));
        }

        [Test]
        public void RestartValidationIsPureAndWrongPressureOrReliefOwnershipCannotDebit()
        {
            var fixture = Create(); var hotel = fixture.Hotel;
            hotel.Boiler.ForceFailure(); // Labelled initial failed-machine fixture.
            string before = State(hotel);
            Assert.That(hotel.Boiler.CanRestart(1).Success, Is.False);
            Assert.That(hotel.EmergencyPatchBoiler(1).Success, Is.False);
            Assert.That(State(hotel), Is.EqualTo(before));
            Require(hotel.Boiler.SetRelief(0, true));
            before = State(hotel);
            Assert.That(hotel.Boiler.InRepairBand, Is.False);
            Assert.That(hotel.EmergencyPatchBoiler(1).Success, Is.False, "Correct staff ownership alone cannot bypass unsafe pressure.");
            Assert.That(State(hotel), Is.EqualTo(before));
            FailedWithSafeRelief(fixture);
            before = State(hotel);
            Assert.That(hotel.Boiler.CanRestart(0).Success, Is.False, "A co-op operator cannot also hold relief.");
            Assert.That(hotel.EmergencyPatchBoiler(0).Success, Is.False);
            Assert.That(hotel.Boiler.CanRestart(1).Success, Is.True);
            Assert.That(State(hotel), Is.EqualTo(before), "Both failed and successful permission probes are nonmutating.");
        }

        [TestCase(-1)]
        [TestCase(2)]
        public void UnknownActorCannotPatchOrStartMaintenanceEvenWithCashAndSafePressure(int actor)
        {
            var fixture = Create(); FailedWithSafeRelief(fixture);
            string before = State(fixture.Hotel);
            Assert.That(fixture.Hotel.EmergencyPatchBoiler(actor).Success, Is.False);
            Assert.That(fixture.Hotel.BeginBoilerMaintenance(actor).Success, Is.False);
            Assert.That(State(fixture.Hotel), Is.EqualTo(before));
        }

        [Test]
        public void InsufficientPatchCashDoesNotConsumeTheReadyRepairOrChangeMachineState()
        {
            var fixture = Create(cash: 199); FailedWithSafeRelief(fixture);
            Assert.That(fixture.Hotel.Economy.Cash, Is.LessThan(fixture.Settings.Economy.CheapPatchCost));
            Assert.That(fixture.Hotel.Boiler.CanRestart(1).Success, Is.True);
            string before = State(fixture.Hotel);
            Assert.That(fixture.Hotel.EmergencyPatchBoiler(1).Success, Is.False);
            Assert.That(State(fixture.Hotel), Is.EqualTo(before));
        }

        [Test]
        public void InsufficientMaintenanceCashDoesNotBeginDowntimeOrChangeTheDeadline()
        {
            var fixture = Create(cash: 1499);
            Assert.That(fixture.Hotel.Economy.Cash, Is.LessThan(fixture.Settings.Economy.ProperRepairCost));
            string before = State(fixture.Hotel);
            Assert.That(fixture.Hotel.BeginBoilerMaintenance(0).Success, Is.False);
            Assert.That(State(fixture.Hotel), Is.EqualTo(before));
            Assert.That(fixture.Hotel.Boiler.MaintenanceEndsAt, Is.Zero);
            Assert.That(fixture.Hotel.Boiler.HeatingOutput, Is.GreaterThan(0));
        }

        [Test]
        public void SuccessfulPatchChargesOnceAndReportsItsLimitedConditionStressAndWorkingHeat()
        {
            var fixture = Create(); FailedWithSafeRelief(fixture); var hotel = fixture.Hotel;
            int cash = hotel.Economy.Cash;
            int recovered = 0;
            hotel.Boiler.OnFailureResolved += () =>
            {
                recovered++;
                Assert.That(hotel.Economy.Cash, Is.EqualTo(cash - fixture.Settings.Economy.CheapPatchCost));
                Assert.That(hotel.PeriodMaintenanceSpend, Is.EqualTo(fixture.Settings.Economy.CheapPatchCost));
                Assert.That(hotel.Boiler.EmergencyPatchActive, Is.True);
                Assert.That(hotel.Boiler.Condition, Is.EqualTo(fixture.Settings.Boiler.Capacity.EmergencyPatchCondition));
                Assert.That(hotel.Boiler.Stress01, Is.EqualTo(fixture.Settings.Boiler.Capacity.EmergencyPatchStress));
                Assert.That(hotel.Boiler.Failed, Is.False, "Callbacks must observe the fully committed paid outcome.");
            };
            Require(hotel.EmergencyPatchBoiler(1));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(cash - fixture.Settings.Economy.CheapPatchCost));
            Assert.That(hotel.PeriodMaintenanceSpend, Is.EqualTo(fixture.Settings.Economy.CheapPatchCost));
            Assert.That(hotel.Boiler.EmergencyPatchActive, Is.True);
            Assert.That(hotel.Boiler.Condition, Is.EqualTo(fixture.Settings.Boiler.Capacity.EmergencyPatchCondition));
            Assert.That(hotel.Boiler.Stress01, Is.EqualTo(fixture.Settings.Boiler.Capacity.EmergencyPatchStress));
            Assert.That(hotel.Boiler.Failed, Is.False);
            Assert.That(hotel.Boiler.HeatingOutput, Is.GreaterThan(0));
            Assert.That(recovered, Is.EqualTo(1));
            string after = State(hotel);
            Assert.That(hotel.EmergencyPatchBoiler(1).Success, Is.False);
            Assert.That(State(hotel), Is.EqualTo(after));
            Assert.That(recovered, Is.EqualTo(1));
        }

        [Test]
        public void ExpiredSoloCatchRejectsThePaidPatchWithoutConsumingCash()
        {
            var fixture = Create(); var boiler = fixture.Hotel.Boiler;
            boiler.ConfigureSoloAssist(new SoloAssistSettings());
            FailedWithSafeRelief(fixture);
            // Explicit authenticated real-time model samples. Physical holding is tested separately.
            Require(boiler.HoldSoloValve(0, boiler.SoloValveRequiredHoldSeconds));
            Require(boiler.SetRelief(0, false));
            Assert.That(boiler.SoloValveLatched, Is.True);
            boiler.AdvanceSoloLatch(boiler.SoloLatchSecondsRemaining + .1f);
            Assert.That(boiler.SoloValveLatched, Is.False);
            string before = State(fixture.Hotel);
            Assert.That(fixture.Hotel.EmergencyPatchBoiler(0).Success, Is.False);
            Assert.That(State(fixture.Hotel), Is.EqualTo(before));
        }

        [Test]
        public void ActiveSoloCatchAllowsOnePaidPatchAndIsReleasedByTheSuccessfulRestart()
        {
            var fixture = Create(); var boiler = fixture.Hotel.Boiler;
            boiler.ConfigureSoloAssist(new SoloAssistSettings()); FailedWithSafeRelief(fixture);
            // Explicit model authentication samples, independent of hotel-time acceleration.
            Require(boiler.HoldSoloValve(0, boiler.SoloValveRequiredHoldSeconds));
            Require(boiler.SetRelief(0, false));
            int cash = fixture.Hotel.Economy.Cash;
            Require(fixture.Hotel.EmergencyPatchBoiler(0));
            Assert.That(boiler.SoloValveLatched, Is.False);
            Assert.That(boiler.SoloLatchSecondsRemaining, Is.Zero);
            Assert.That(fixture.Hotel.Economy.Cash, Is.EqualTo(cash - fixture.Settings.Economy.CheapPatchCost));
            Assert.That(boiler.EmergencyPatchActive, Is.True);
        }

        [Test]
        public void MaintenanceStopsRealHeatImmediatelyRejectsDuplicatesAndDoesNotFinishBeforeTheHotelDeadline()
        {
            var fixture = Create(); var hotel = fixture.Hotel;
            int cash = hotel.Economy.Cash;
            Require(hotel.BeginBoilerMaintenance(0));
            float deadline = hotel.Boiler.MaintenanceEndsAt;
            float expectedDuration = fixture.Settings.Boiler.Capacity.MaintenanceHours * hotel.Operations.SecondsPerDay / 24;
            Assert.That(deadline, Is.EqualTo(hotel.Elapsed + expectedDuration).Within(.00001f));
            Assert.That(hotel.Boiler.MaintenanceInProgress, Is.True);
            Assert.That(hotel.Boiler.MaintenanceRemaining(hotel.Elapsed), Is.EqualTo(expectedDuration).Within(.00001f));
            Assert.That(hotel.Boiler.HeatingOutput, Is.Zero);
            Assert.That(hotel.Economy.Cash, Is.EqualTo(cash - fixture.Settings.Economy.ProperRepairCost));
            string beforeDuplicate = State(hotel);
            Assert.That(hotel.BeginBoilerMaintenance(1).Success, Is.False);
            Assert.That(hotel.EmergencyPatchBoiler(1).Success, Is.False);
            Assert.That(State(hotel), Is.EqualTo(beforeDuplicate));
            AdvanceTo(hotel, deadline - .25f);
            Assert.That(hotel.Boiler.MaintenanceInProgress, Is.True);
            Assert.That(hotel.Boiler.HeatingOutput, Is.Zero);
            Assert.That(fixture.Rooms[0].Temperature, Is.LessThan(16), "Central downtime cools the actual room state.");
            AdvanceTo(hotel, deadline);
            Assert.That(hotel.Boiler.MaintenanceInProgress, Is.False);
            Assert.That(hotel.Boiler.MaintenanceEndsAt, Is.Zero);
            Assert.That(hotel.Boiler.MaintenanceRemaining(hotel.Elapsed), Is.Zero);
            Assert.That(hotel.Boiler.Condition, Is.GreaterThanOrEqualTo(fixture.Settings.Boiler.Capacity.ProperMaintenanceCondition));
            Assert.That(hotel.Boiler.EmergencyPatchActive, Is.False);
            Assert.That(hotel.Boiler.Stress01, Is.Zero);
            Assert.That(hotel.Boiler.HeatingOutput, Is.GreaterThan(0));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(cash - fixture.Settings.Economy.ProperRepairCost));
        }

        [Test]
        public void ExactCompletionSplitsThermalIntegrationAndIsIndependentOfCallerTickChunking()
        {
            var coarse = Create(); var fine = Create();
            Require(coarse.Hotel.BeginBoilerMaintenance(0));
            Require(fine.Hotel.BeginBoilerMaintenance(0));
            float duration = coarse.Hotel.Boiler.MaintenanceEndsAt;
            coarse.Hotel.Tick(duration);
            AdvanceTo(fine.Hotel, duration, .37f);
            float coldTarget = coarse.Settings.TemperatureBase;
            float expectedAtDeadline = coldTarget + (22 - coldTarget) * (float)Math.Exp(-duration / coarse.Settings.TemperatureTimeConstant);
            Assert.That(coarse.Rooms[0].Temperature, Is.EqualTo(expectedAtDeadline).Within(.0005f),
                "The final downtime interval still uses zero heat; restored output belongs to the next interval.");
            Assert.That(fine.Rooms[0].Temperature, Is.EqualTo(coarse.Rooms[0].Temperature).Within(.001f));
            Assert.That(fine.Hotel.Boiler.Condition, Is.EqualTo(coarse.Hotel.Boiler.Condition).Within(.0001f));
            Assert.That(coarse.Hotel.Boiler.MaintenanceInProgress || fine.Hotel.Boiler.MaintenanceInProgress, Is.False);
            // A single public call spanning the deadline must have the same cold/hot portions.
            var crossing = Create(); Require(crossing.Hotel.BeginBoilerMaintenance(0));
            crossing.Hotel.Tick(duration + 12);
            coarse.Hotel.Tick(12);
            AdvanceTo(fine.Hotel, duration + 12, .37f);
            Assert.That(crossing.Rooms[0].Temperature, Is.EqualTo(coarse.Rooms[0].Temperature).Within(.001f));
            Assert.That(fine.Rooms[0].Temperature, Is.EqualTo(coarse.Rooms[0].Temperature).Within(.001f));
            Assert.That(coarse.Rooms[0].Temperature, Is.GreaterThan(expectedAtDeadline));
            Assert.That(crossing.Hotel.Economy.Cash, Is.EqualTo(coarse.Hotel.Economy.Cash));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MaintenanceSurvivesMidnightOrReportAndChargesOnlyAtStart(bool crossReport)
        {
            var fixture = Create(); var hotel = fixture.Hotel;
            float reportAt = hotel.NextReportAt;
            float boundary = crossReport ? reportAt : hotel.Calendar.At(2, 0);
            AdvanceTo(hotel, boundary - 10);
            hotel.Boiler.ForceFailure(); // Explicit failed-machine fixture isolates maintenance completion/recovery events.
            int recovered = 0; hotel.Boiler.OnFailureResolved += () => recovered++;
            int initialCash = hotel.Economy.Cash;
            Require(hotel.BeginBoilerMaintenance(0));
            float deadline = hotel.Boiler.MaintenanceEndsAt;
            Assert.That(deadline, Is.GreaterThan(boundary));
            AdvanceTo(hotel, boundary + 1);
            Assert.That(hotel.Boiler.MaintenanceInProgress, Is.True);
            Assert.That(hotel.Boiler.MaintenanceEndsAt, Is.EqualTo(deadline));
            Assert.That(hotel.Boiler.HeatingOutput, Is.Zero);
            Assert.That(recovered, Is.Zero);
            Assert.That(hotel.Economy.Cash, Is.EqualTo(initialCash - fixture.Settings.Economy.ProperRepairCost -
                (crossReport ? fixture.Settings.Economy.DailyOperatingCost : 0)));
            Assert.That(hotel.PeriodMaintenanceSpend, Is.EqualTo(crossReport ? 0 : fixture.Settings.Economy.ProperRepairCost));
            AdvanceTo(hotel, deadline);
            Assert.That(recovered, Is.EqualTo(1));
            Assert.That(hotel.Boiler.MaintenanceInProgress, Is.False);
            AdvanceTo(hotel, Math.Max(hotel.Elapsed, reportAt + 1));
            var report = hotel.DayReports.Single();
            Assert.That(report.MaintenanceSpend, Is.EqualTo(fixture.Settings.Economy.ProperRepairCost));
            Assert.That(report.Net, Is.EqualTo(-fixture.Settings.Economy.DailyOperatingCost - report.MaintenanceSpend));
            Assert.That(report.OpeningCash + report.Net, Is.EqualTo(report.Cash));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(initialCash - fixture.Settings.Economy.ProperRepairCost - fixture.Settings.Economy.DailyOperatingCost));
            Assert.That(hotel.PeriodMaintenanceSpend, Is.Zero);
            int cashAfter = hotel.Economy.Cash;
            hotel.Tick(10);
            Assert.That(hotel.DayReports.Count, Is.EqualTo(1));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(cashAfter));
            Assert.That(recovered, Is.EqualTo(1), "Crossing a later tick cannot complete the same maintenance again.");
        }

        [Test]
        public void BothPatchAndProperMaintenanceAccumulateInOneReportWithoutSecondDebits()
        {
            var fixture = Create(); FailedWithSafeRelief(fixture); var hotel = fixture.Hotel;
            int initialCash = hotel.Economy.Cash;
            Require(hotel.EmergencyPatchBoiler(1));
            Require(hotel.BeginBoilerMaintenance(0));
            int spent = fixture.Settings.Economy.CheapPatchCost + fixture.Settings.Economy.ProperRepairCost;
            Assert.That(hotel.PeriodMaintenanceSpend, Is.EqualTo(spent));
            AdvanceTo(hotel, hotel.NextReportAt + .25f);
            var report = hotel.DayReports.Single();
            Assert.That(report.MaintenanceSpend, Is.EqualTo(spent));
            Assert.That(report.Net, Is.EqualTo(-spent - fixture.Settings.Economy.DailyOperatingCost));
            Assert.That(report.Cash, Is.EqualTo(initialCash + report.Net));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(report.Cash));
            Assert.That(hotel.PeriodMaintenanceSpend, Is.Zero);
        }

        [Test]
        public void SameSixOwnedRoomsCauseFasterStressGrowthAfterPatchThanAfterProperMaintenance()
        {
            var patched = Create(occupied: true); var proper = Create(occupied: true);
            FailedWithSafeRelief(patched); FailedWithSafeRelief(proper);
            Require(patched.Hotel.EmergencyPatchBoiler(1));
            Require(proper.Hotel.BeginBoilerMaintenance(0));
            float maintenanceEnd = proper.Hotel.Boiler.MaintenanceEndsAt;
            AdvanceTo(patched.Hotel, maintenanceEnd);
            AdvanceTo(proper.Hotel, maintenanceEnd);
            Assert.That(patched.Hotel.Boiler.Load, Is.EqualTo(proper.Hotel.Boiler.Load).Within(.00001f));
            Assert.That(patched.Hotel.HeatingDemands.All(row => row.GuestId != null && row.HotWater == 0), Is.True);
            Assert.That(proper.Hotel.HeatingDemands.All(row => row.GuestId != null && row.HotWater == 0), Is.True);
            float patchStress = patched.Hotel.Boiler.Stress01;
            float properStress = proper.Hotel.Boiler.Stress01;
            float periodEnd = maintenanceEnd + proper.Hotel.Operations.SecondsPerDay / 24;
            AdvanceTo(patched.Hotel, periodEnd); AdvanceTo(proper.Hotel, periodEnd);
            Assert.That(patched.Hotel.Boiler.Load, Is.EqualTo(proper.Hotel.Boiler.Load).Within(.00001f));
            Assert.That(patched.Hotel.Boiler.EffectiveCapacity, Is.LessThan(proper.Hotel.Boiler.EffectiveCapacity));
            Assert.That(patched.Hotel.Boiler.Stress01 - patchStress, Is.GreaterThan(proper.Hotel.Boiler.Stress01 - properStress),
                "An entire hotel hour under the same actual room demand exposes the patch's worse capacity and stress penalty.");
            Assert.That(patched.Hotel.Boiler.Stress01 - patchStress, Is.GreaterThan(.01f));
            Assert.That(proper.Hotel.Boiler.Failed, Is.False);
            Assert.That(patched.Hotel.Boiler.EmergencyPatchActive, Is.True);
            Assert.That(proper.Hotel.Boiler.EmergencyPatchActive, Is.False);
            TestContext.WriteLine(FormattableString.Invariant($"Same actual room load {proper.Hotel.Boiler.Load:F6}: patch stress gain {patched.Hotel.Boiler.Stress01 - patchStress:F6}, proper stress gain {proper.Hotel.Boiler.Stress01 - properStress:F6}; patch capacity {patched.Hotel.Boiler.EffectiveCapacity:F6}, proper capacity {proper.Hotel.Boiler.EffectiveCapacity:F6}."));
        }

        [Test]
        public void ReadOnlyMirrorAndUnopenedOrLegacyHotelsCannotStartPaidWork()
        {
            var host = Create(); Require(host.Hotel.BeginBoilerMaintenance(0));
            var mirror = Create(); mirror.Hotel.EnableReadOnlyMirror();
            Require(mirror.Hotel.ApplySnapshot(JsonUtility.FromJson<HotelModelSnapshot>(JsonUtility.ToJson(host.Hotel.CaptureSnapshot(76, 1)))));
            foreach (var hotel in new[] { mirror.Hotel, Create(open: false).Hotel, Create(open: false, continuous: false).Hotel })
            {
                string before = State(hotel);
                Assert.That(hotel.EmergencyPatchBoiler(0).Success, Is.False);
                Assert.That(hotel.BeginBoilerMaintenance(0).Success, Is.False);
                Assert.That(State(hotel), Is.EqualTo(before));
            }
            string mirrorBefore = State(mirror.Hotel);
            mirror.Hotel.Tick(500);
            Assert.That(State(mirror.Hotel), Is.EqualTo(mirrorBefore));
        }
    }
}
