using System;
using System.Linq;
using NUnit.Framework;

namespace WorstHotel.Tests
{
    public sealed class ContinuousCalendarTests
    {
        sealed class Fixture
        {
            public SessionSettings Settings;
            public RoomState[] Rooms;
            public HotelSimulation Hotel;
        }

        static Fixture Create(OperationsSettings operations = null, bool continuous = true,
            bool start = true, BoilerSettings boiler = null)
        {
            var profiles = new[]
            {
                new GuestProfile(GuestKind.Budget, "Budget", "", 180, .85f, 18, .7f, 28, 90, .55f),
                new GuestProfile(GuestKind.ColdSensitive, "Cold-sensitive", "", 300, 1.05f, 20, 1.35f, 16, 65, .4f),
                new GuestProfile(GuestKind.Business, "Business", "", 450, 1, 19.5f, 1, 10, 45, .25f)
            };
            var roomProfiles = Enumerable.Range(101, 6).Select(id => new RoomProfile(id, "Room " + id)).ToArray();
            // These calendar fixtures disable operating wear deliberately. They do not tune
            // production capacity or depend on an unrelated failure during several empty days.
            var settings = new SessionSettings(profiles, roomProfiles,
                boiler ?? new BoilerSettings(baseWearPerMinute: 0, overloadWearPerMinute: 0), new EconomySettings());
            var rooms = roomProfiles.Select(profile => new RoomState(profile)).ToArray();
            var hotel = new HotelSimulation(settings, rooms, new LivingHotelSettings(),
                operations: continuous ? operations ?? new OperationsSettings() : null);
            var fixture = new Fixture { Settings = settings, Rooms = rooms, Hotel = hotel };
            if (continuous && start) Require(hotel.StartOperations());
            return fixture;
        }

        static void Require(CommandResult result) => Assert.That(result.Success, Is.True, result.Message);

        [Test]
        public void CalendarReadsTheExistingClockAndSeparatesMidnightFromReporting()
        {
            var clock = new HotelGameClock();
            var calendar = new HotelCalendar(clock, new OperationsSettings());
            Assert.That(calendar.Day, Is.EqualTo(1));
            Assert.That(calendar.Hour, Is.EqualTo(8).Within(.0001f));
            Assert.That(calendar.DisplayTime, Does.Contain("08:00"));
            Assert.That(calendar.At(2, 0), Is.EqualTo(480).Within(.0001f));
            Assert.That(calendar.At(2, 6), Is.EqualTo(660).Within(.0001f));
            Assert.That(calendar.At(2, 10), Is.EqualTo(780).Within(.0001f));
            Assert.That(calendar.FirstReportAt, Is.EqualTo(660).Within(.0001f));

            clock.Advance(480);
            Assert.That(calendar.Day, Is.EqualTo(2));
            Assert.That(calendar.Hour, Is.EqualTo(0).Within(.0001f));
            Assert.That(calendar.DisplayTime, Does.Contain("00:00"));
            clock.Advance(180);
            Assert.That(calendar.Day, Is.EqualTo(2));
            Assert.That(calendar.Hour, Is.EqualTo(6).Within(.0001f));
            Assert.That(clock.SimulationTime, Is.EqualTo(660), "Calendar queries must not own or advance another clock.");
        }

        [Test]
        public void ConfiguredCalendarUsesItsOwnDurationAndOpeningHour()
        {
            var clock = new HotelGameClock();
            var calendar = new HotelCalendar(clock, new OperationsSettings(secondsPerDay: 1440, startHour: 22, reportHour: 6));
            Assert.That(calendar.At(2, 0), Is.EqualTo(120).Within(.0001f));
            Assert.That(calendar.FirstReportAt, Is.EqualTo(480).Within(.0001f));
            clock.Advance(120);
            Assert.That(calendar.Day, Is.EqualTo(2));
            Assert.That(calendar.Hour, Is.Zero.Within(.0001f));
        }

        [Test]
        public void OpeningAtReportHourDoesNotImmediatelyChargeAnEmptyDay()
        {
            var fixture = Create(new OperationsSettings(startHour: 6, reportHour: 6));
            Assert.That(fixture.Hotel.NextReportAt, Is.EqualTo(720).Within(.0001f));
            Assert.That(fixture.Hotel.Remaining, Is.EqualTo(720).Within(.0001f));
            fixture.Hotel.Tick(0);
            Assert.That(fixture.Hotel.ReportSequence, Is.Zero);
            Assert.That(fixture.Hotel.Economy.Cash, Is.EqualTo(fixture.Settings.Economy.StartingCash));
        }

        [Test]
        public void EmptyOperationsStartOnceWithoutCreatingBookingsOrReceipts()
        {
            var fixture = Create(start: false);
            var hotel = fixture.Hotel;
            Assert.That(hotel.ContinuousOperations, Is.True);
            Assert.That(hotel.Running, Is.False);
            Require(hotel.StartOperations());
            Assert.That(hotel.Running, Is.True);
            Assert.That(hotel.CalendarDay, Is.EqualTo(1));
            Assert.That(hotel.Elapsed, Is.Zero);
            Assert.That(hotel.NextReportAt, Is.EqualTo(660).Within(.0001f));
            Assert.That(hotel.Guests, Is.Empty);
            Assert.That(fixture.Rooms.All(room => !room.Occupied && !room.Reserved), Is.True);
            Assert.That(hotel.DayReports, Is.Empty);
            Assert.That(hotel.IsServiceComplete, Is.False);
        }

        [Test]
        public void MidnightChangesCalendarDayWithoutPostingDailyExpense()
        {
            var fixture = Create();
            fixture.Hotel.Tick(479.75f);
            Assert.That(fixture.Hotel.CalendarDay, Is.EqualTo(1));
            fixture.Hotel.Tick(.25f);
            Assert.That(fixture.Hotel.CalendarDay, Is.EqualTo(2));
            Assert.That(fixture.Hotel.Calendar.Hour, Is.Zero.Within(.0001f));
            Assert.That(fixture.Hotel.ReportSequence, Is.Zero);
            Assert.That(fixture.Hotel.DayReports, Is.Empty);
            Assert.That(fixture.Hotel.Economy.Cash, Is.EqualTo(fixture.Settings.Economy.StartingCash));
            Assert.That(fixture.Hotel.Remaining, Is.EqualTo(180).Within(.0001f));
            Assert.That(fixture.Hotel.Running && !fixture.Hotel.IsServiceComplete, Is.True);
        }

        [Test]
        public void FirstExactReportChargesOncePreservesReputationAndDoesNotEndOperations()
        {
            var fixture = Create();
            var hotel = fixture.Hotel;
            float reputation = hotel.Economy.Reputation;
            hotel.Tick(659.75f);
            Assert.That(hotel.ReportSequence, Is.Zero);
            hotel.Tick(.25f);
            Assert.That(hotel.Elapsed, Is.EqualTo(660).Within(.0001f));
            Assert.That(hotel.ReportSequence, Is.EqualTo(1));
            Assert.That(hotel.DayReports.Count, Is.EqualTo(1));
            var report = hotel.DayReports.Single();
            Assert.That(report, Is.SameAs(hotel.LastReport));
            Assert.That(report.Receipts, Is.Empty);
            Assert.That(report.OpeningCash, Is.EqualTo(350));
            Assert.That(report.OperatingCost, Is.EqualTo(450));
            Assert.That(report.Cash, Is.EqualTo(-100));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(-100), "The first report precedes next-morning checkout income.");
            Assert.That(hotel.Economy.Reputation, Is.EqualTo(reputation));
            Assert.That(report.Reputation, Is.EqualTo(reputation));
            Assert.That(hotel.NextReportAt, Is.EqualTo(1380).Within(.0001f));
            Assert.That(hotel.Remaining, Is.EqualTo(720).Within(.0001f));
            Assert.That(hotel.Running && !hotel.IsServiceComplete, Is.True);
            int revision = hotel.EventRevision;
            hotel.Tick(0);
            Assert.That(hotel.EventRevision, Is.EqualTo(revision));
            hotel.Tick(.25f);
            Assert.That(hotel.ReportSequence, Is.EqualTo(1));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(-100));
            Assert.That(report.Cash, Is.EqualTo(-100), "A published report is an immutable historical entry.");
        }

        [Test]
        public void OneLargeTickPostsEveryCrossedReportAndMatchesSmallerTicks()
        {
            var large = Create().Hotel;
            var small = Create().Hotel;
            large.Tick(2100);
            for (int step = 0; step < 8400; step++) small.Tick(.25f);
            Assert.That(large.Elapsed, Is.EqualTo(2100).Within(.0001f));
            Assert.That(large.Elapsed, Is.EqualTo(small.Elapsed).Within(.0001f));
            Assert.That(large.CalendarDay, Is.EqualTo(small.CalendarDay));
            Assert.That(large.ReportSequence, Is.EqualTo(3));
            Assert.That(large.ReportSequence, Is.EqualTo(small.ReportSequence));
            Assert.That(large.Economy.Cash, Is.EqualTo(350 - 3 * 450));
            Assert.That(large.Economy.Cash, Is.EqualTo(small.Economy.Cash));
            Assert.That(large.Economy.Reputation, Is.EqualTo(small.Economy.Reputation));
            Assert.That(large.DayReports.Select(report => report.Cash),
                Is.EqualTo(small.DayReports.Select(report => report.Cash)));
            Assert.That(large.NextReportAt, Is.EqualTo(2820).Within(.0001f));
            Assert.That(large.Remaining, Is.EqualTo(720).Within(.0001f));
            Assert.That(large.Running && !large.IsServiceComplete, Is.True);
        }

        [Test]
        public void CappedReportHistoryDoesNotResetAccountingOrImposeTheLegacyThreeDayLimit()
        {
            var fixture = Create(new OperationsSettings(reportHistoryLimit: 2));
            var hotel = fixture.Hotel;
            hotel.Tick(3540);
            Assert.That(hotel.ReportSequence, Is.EqualTo(5));
            Assert.That(hotel.DayReports.Count, Is.EqualTo(2));
            Assert.That(hotel.DayReports.Select(report => report.DayNumber), Is.EqualTo(new[] { 4, 5 }));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(350 - 5 * 450));
            hotel.Tick(720);
            Assert.That(hotel.ReportSequence, Is.EqualTo(6));
            Assert.That(hotel.DayReports.Select(report => report.DayNumber), Is.EqualTo(new[] { 5, 6 }));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(350 - 6 * 450));
            Assert.That(hotel.Economy.Reputation, Is.EqualTo(fixture.Settings.Economy.InitialReputation));
            Assert.That(hotel.Running && !hotel.IsServiceComplete, Is.True);
            Assert.That(hotel.MaintenanceRequired, Is.False);
        }

        static void SetPersistentPhysicalState(Fixture fixture)
        {
            var hotel = fixture.Hotel;
            // Explicit model setup, not a claim of a naturally generated failure or physical pickup.
            hotel.Boiler.SetCondition(37);
            hotel.Boiler.ForceFailure();
            Require(hotel.DebugTripCircuit("A"));
            Require(hotel.Heaters.Register("calendar-heater"));
            Require(hotel.Heaters.AssignRoom("calendar-heater", 104));
            Require(hotel.Heaters.SetSwitchedOn("calendar-heater", true));
            hotel.RefreshElectrical();
            Require(hotel.DebugMarkRoomDirty(102));
            Require(hotel.PickUpLinen(1, "clean:0"));
            Require(hotel.Keys.PickUp(0, 103));
            fixture.Rooms.Single(room => room.Profile.Id == 101).Temperature = 13;
        }

        [Test]
        public void ReportBoundaryPreservesFailurePowerHeaterLinenKeysAndOrdinaryTemperatureEvolution()
        {
            var reported = Create();
            var control = Create(new OperationsSettings(reportHour: 7));
            reported.Hotel.Tick(659.5f);
            control.Hotel.Tick(659.5f);
            SetPersistentPhysicalState(reported);
            SetPersistentPhysicalState(control);
            var key = reported.Hotel.Keys.Find(103);
            var heater = reported.Hotel.Heaters.Find("calendar-heater");
            var linen = reported.Hotel.Housekeeping.FindLinen("clean:0");
            var linenLocation = linen.Location;
            int refillDay = reported.Hotel.Housekeeping.LastRefillDay;
            reported.Hotel.Tick(1);
            control.Hotel.Tick(1);

            Assert.That(reported.Hotel.ReportSequence, Is.EqualTo(1));
            Assert.That(control.Hotel.ReportSequence, Is.Zero, "The counterfactual runs identical physics without a report at this instant.");
            Assert.That(reported.Hotel.Boiler.Failed, Is.True);
            Assert.That(reported.Hotel.Boiler.Condition, Is.EqualTo(control.Hotel.Boiler.Condition).Within(.001f));
            Assert.That(reported.Hotel.Boiler.Pressure, Is.EqualTo(control.Hotel.Boiler.Pressure).Within(.001f));
            Assert.That(reported.Hotel.Electrical.Find("A").Tripped, Is.True);
            Assert.That(reported.Rooms.Where(room => room.Profile.Id <= 103).All(room => !room.HasPower), Is.True);
            Assert.That(reported.Hotel.Heaters.Find("calendar-heater"), Is.SameAs(heater));
            Assert.That(heater.SwitchedOn && heater.Powered, Is.True);
            Assert.That(heater.RoomId, Is.EqualTo(104));
            Assert.That(reported.Rooms.Single(room => room.Profile.Id == 102).Cleanliness, Is.EqualTo(Cleanliness.Dirty));
            Assert.That(reported.Hotel.Keys.Find(103), Is.SameAs(key));
            Assert.That(key.Location, Is.EqualTo(RoomKeyLocation.HeldByPlayer));
            Assert.That(key.PlayerId, Is.EqualTo(0));
            Assert.That(reported.Hotel.Housekeeping.FindLinen("clean:0"), Is.SameAs(linen));
            Assert.That(linen.Location, Is.EqualTo(linenLocation));
            Assert.That(linen.PlayerId, Is.EqualTo(1));
            Assert.That(reported.Hotel.Housekeeping.LastRefillDay, Is.EqualTo(refillDay));
            foreach (var room in reported.Rooms)
                Assert.That(room.Temperature, Is.EqualTo(control.Rooms.Single(other => other.Profile.Id == room.Profile.Id).Temperature).Within(.002f));
            Assert.That(reported.Hotel.Running && !reported.Hotel.IsServiceComplete, Is.True);
        }

        [Test]
        public void RepeatedStartAndLegacyLifecycleCommandsCannotResetRunningOperations()
        {
            var hotel = Create().Hotel;
            hotel.Tick(660);
            int cash = hotel.Economy.Cash, reports = hotel.ReportSequence;
            float time = hotel.Elapsed, next = hotel.NextReportAt;
            Assert.That(hotel.StartOperations().Success, Is.False);
            Assert.That(hotel.StartShift(Array.Empty<BookingAssignment>(), Array.Empty<BookingApplication>()).Success, Is.False);
            Assert.Throws<InvalidOperationException>(() => hotel.EndShift());
            Assert.That(hotel.ApplyMaintenance(0, MaintenanceChoice.CheapPatch).Success, Is.False);
            Assert.That(hotel.ApplyMaintenance(0, MaintenanceChoice.Defer).Success, Is.False);
            Assert.That(hotel.Elapsed, Is.EqualTo(time));
            Assert.That(hotel.NextReportAt, Is.EqualTo(next));
            Assert.That(hotel.ReportSequence, Is.EqualTo(reports));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(cash));
            Assert.That(hotel.Running, Is.True);
        }

        [TestCase(1f)]
        [TestCase(4f)]
        [TestCase(8f)]
        public void ClockSpeedDoesNotMultiplyExplicitTicksOrAdvanceWithoutElapsedInput(float speed)
        {
            var hotel = Create().Hotel;
            hotel.Tick(659.5f);
            hotel.Clock.SetSpeed(speed);
            hotel.Tick(0);
            Assert.That(hotel.Elapsed, Is.EqualTo(659.5f));
            Assert.That(hotel.ReportSequence, Is.Zero);
            hotel.Tick(.25f);
            Assert.That(hotel.Elapsed, Is.EqualTo(659.75f));
            Assert.That(hotel.ReportSequence, Is.Zero, "WAIT's caller budgets accelerated ticks; the calendar cannot multiply them again.");
            hotel.Tick(.25f);
            Assert.That(hotel.ReportSequence, Is.EqualTo(1));
            Assert.That(hotel.Elapsed, Is.EqualTo(660));
        }

        [Test]
        public void ReadOnlyContinuousMirrorCannotAdvanceTimePostReportsOrRestart()
        {
            var hotel = Create().Hotel;
            hotel.Tick(659.75f);
            hotel.EnableReadOnlyMirror();
            float condition = hotel.Boiler.Condition, pressure = hotel.Boiler.Pressure;
            int cash = hotel.Economy.Cash, revision = hotel.EventRevision;
            hotel.Tick(1440);
            hotel.Clock.Advance(1440);
            hotel.Clock.Reset();
            Assert.That(hotel.StartOperations().Success, Is.False);
            Assert.That(hotel.Elapsed, Is.EqualTo(659.75f));
            Assert.That(hotel.ReportSequence, Is.Zero);
            Assert.That(hotel.DayReports, Is.Empty);
            Assert.That(hotel.NextReportAt, Is.EqualTo(660));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(cash));
            Assert.That(hotel.EventRevision, Is.EqualTo(revision));
            Assert.That(hotel.Boiler.Condition, Is.EqualTo(condition));
            Assert.That(hotel.Boiler.Pressure, Is.EqualTo(pressure));
        }

        [Test]
        public void OptOutKeepsLegacyFiniteShiftAndSettlementBehavior()
        {
            var fixture = Create(continuous: false, start: false, boiler: new BoilerSettings());
            var hotel = fixture.Hotel;
            Assert.That(hotel.ContinuousOperations, Is.False);
            Assert.That(hotel.StartOperations().Success, Is.False);
            var offer = new BookingApplication("legacy-calendar-control", "Legacy control", fixture.Settings.GuestArchetypes[0], 180);
            Require(hotel.StartShift(new[] { new BookingAssignment(101, offer.Id, 180, 0) }, new[] { offer }));
            hotel.Tick(1000);
            Assert.That(hotel.Elapsed, Is.EqualTo(300));
            Assert.That(hotel.Remaining, Is.Zero);
            Assert.That(hotel.IsServiceComplete, Is.True);
            Assert.That(hotel.DayReports, Is.Empty);
            var report = hotel.EndShift();
            Assert.That(hotel.Running, Is.False);
            Assert.That(report.DayNumber, Is.EqualTo(1));
            Assert.That(hotel.DayReports.Count, Is.EqualTo(1));
            Assert.That(hotel.EndShift(), Is.SameAs(report));
            Assert.That(hotel.MaintenanceRequired, Is.True);
        }

        [Test]
        public void InvalidOperationsSettingsAndCalendarInputsAreRejected()
        {
            foreach (float value in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                Assert.Catch<ArgumentException>(() => new OperationsSettings(secondsPerDay: value));
            foreach (float hour in new[] { -1f, 24f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                Assert.Catch<ArgumentException>(() => new OperationsSettings(startHour: hour));
                Assert.Catch<ArgumentException>(() => new OperationsSettings(reportHour: hour));
                Assert.Catch<ArgumentException>(() => new OperationsSettings(arrivalStartHour: hour));
                Assert.Catch<ArgumentException>(() => new OperationsSettings(arrivalEndHour: hour));
                Assert.Catch<ArgumentException>(() => new OperationsSettings(sleepHour: hour));
                Assert.Catch<ArgumentException>(() => new OperationsSettings(checkoutHour: hour));
            }
            Assert.Catch<ArgumentException>(() => new OperationsSettings(arrivalStartHour: 18, arrivalEndHour: 14));
            Assert.Catch<ArgumentException>(() => new OperationsSettings(secondsPerDay: 23));
            Assert.Catch<ArgumentException>(() => new OperationsSettings(sleepHour: 18));
            Assert.Catch<ArgumentException>(() => new OperationsSettings(checkoutHour: 14));
            Assert.Catch<ArgumentException>(() => new OperationsSettings(reportHistoryLimit: 0));
            Assert.Catch<ArgumentException>(() => new OperationsSettings(reportHistoryLimit: -1));
            Assert.Catch<ArgumentException>(() => new OperationsSettings(reportHistoryLimit: 129));
            Assert.Catch<ArgumentException>(() => new HotelCalendar(null, new OperationsSettings()));
            Assert.Catch<ArgumentException>(() => new HotelCalendar(new HotelGameClock(), null));
            var calendar = new HotelCalendar(new HotelGameClock(), new OperationsSettings());
            Assert.Catch<ArgumentException>(() => calendar.At(0, 8));
            foreach (float hour in new[] { -1f, 24f, float.NaN, float.PositiveInfinity })
                Assert.Catch<ArgumentException>(() => calendar.At(1, hour));
        }

        [Test]
        public void FractionalStepAcrossAReportConsumesItsRemainderWithoutClosingTwice()
        {
            var hotel = Create().Hotel;
            hotel.Tick(659.9f);
            float before = hotel.Elapsed;
            hotel.Tick(.2f);
            Assert.That(hotel.Elapsed, Is.EqualTo(before + .2f).Within(.0001f));
            Assert.That(hotel.Elapsed, Is.GreaterThan(660));
            Assert.That(hotel.ReportSequence, Is.EqualTo(1));
            Assert.That(hotel.NextReportAt, Is.EqualTo(1380));
            hotel.Tick(.2f);
            Assert.That(hotel.ReportSequence, Is.EqualTo(1));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(-100));
        }

        [Test]
        public void OversizedFiniteTickIsRejectedBeforeAnySimulationOrAccountingWork()
        {
            var hotel = Create().Hotel;
            hotel.Tick(659.75f);
            int revision = hotel.EventRevision;
            float condition = hotel.Boiler.Condition, pressure = hotel.Boiler.Pressure;
            Assert.Catch<ArgumentException>(() => hotel.Tick(float.MaxValue));
            Assert.That(hotel.Elapsed, Is.EqualTo(659.75f));
            Assert.That(hotel.EventRevision, Is.EqualTo(revision));
            Assert.That(hotel.ReportSequence, Is.Zero);
            Assert.That(hotel.DayReports, Is.Empty);
            Assert.That(hotel.Economy.Cash, Is.EqualTo(350));
            Assert.That(hotel.Boiler.Condition, Is.EqualTo(condition));
            Assert.That(hotel.Boiler.Pressure, Is.EqualTo(pressure));
        }

        [Test]
        public void PositiveDeltaBelowClockPrecisionIsRejectedBeforePostingOverdueReports()
        {
            var hotel = Create().Hotel;
            // Explicit precision probe through the clock API, not simulated player progress.
            hotel.Clock.Advance(16777216f);
            int revision = hotel.EventRevision;
            Assert.That(hotel.Elapsed + .1f, Is.EqualTo(hotel.Elapsed));
            Assert.Catch<ArgumentException>(() => hotel.Tick(.1f));
            Assert.That(hotel.Elapsed, Is.EqualTo(16777216f));
            Assert.That(hotel.EventRevision, Is.EqualTo(revision));
            Assert.That(hotel.ReportSequence, Is.Zero);
            Assert.That(hotel.DayReports, Is.Empty);
            Assert.That(hotel.Economy.Cash, Is.EqualTo(350));
        }

        [Test]
        public void OperationsCannotOpenAnAlreadyAdvancedClockOrPartiallyInitializeTheDay()
        {
            var hotel = Create(start: false).Hotel;
            hotel.Clock.Advance(700);
            // Existing capture exposes the internal lifecycle day without adding test-only API.
            int lifecycleDay = hotel.CaptureSnapshot(0, 0).Day;
            int calendarDay = hotel.CalendarDay, revision = hotel.EventRevision;
            Assert.That(hotel.StartOperations().Success, Is.False);
            Assert.That(hotel.Running, Is.False);
            Assert.That(hotel.Elapsed, Is.EqualTo(700));
            Assert.That(hotel.CaptureSnapshot(0, 0).Day, Is.EqualTo(lifecycleDay));
            Assert.That(hotel.CalendarDay, Is.EqualTo(calendarDay));
            Assert.That(hotel.EventRevision, Is.EqualTo(revision));
            Assert.That(hotel.ReportSequence, Is.Zero);
            Assert.That(hotel.DayReports, Is.Empty);
            Assert.That(hotel.Economy.Cash, Is.EqualTo(350));
        }

        [Test]
        public void MaximumFiniteDayDurationHasAFiniteFirstReportBoundary()
        {
            var calendar = new HotelCalendar(new HotelGameClock(), new OperationsSettings(secondsPerDay: float.MaxValue));
            float boundary = calendar.FirstReportAt;
            Assert.That(float.IsNaN(boundary) || float.IsInfinity(boundary), Is.False);
            Assert.That(boundary, Is.GreaterThan(0));
            Assert.That(boundary, Is.EqualTo((float)((double)float.MaxValue * 22 / 24)));
        }

        [Test]
        public void InvalidTickCannotChangeCalendarOrAccounting()
        {
            var hotel = Create().Hotel;
            hotel.Tick(659.75f);
            foreach (float delta in new[] { -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                Assert.Catch<ArgumentException>(() => hotel.Tick(delta));
                Assert.That(hotel.Elapsed, Is.EqualTo(659.75f));
                Assert.That(hotel.ReportSequence, Is.Zero);
                Assert.That(hotel.Economy.Cash, Is.EqualTo(350));
                Assert.That(hotel.NextReportAt, Is.EqualTo(660));
            }
        }
    }
}
