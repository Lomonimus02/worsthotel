using System.Linq;
using NUnit.Framework;

namespace WorstHotel.Tests
{
    public sealed class InfrastructureHistoryTests
    {
        static HotelSimulation Create()
        {
            var guests = new[]
            {
                new GuestProfile(GuestKind.Budget, "Budget", "", 180, .85f, 18, .7f, 28, 90, .55f),
                new GuestProfile(GuestKind.ColdSensitive, "Cold", "", 300, 1.05f, 20, 1.35f, 16, 65, .4f),
                new GuestProfile(GuestKind.Business, "Business", "", 450, 1, 19.5f, 1, 10, 45, .25f)
            };
            var settings = new SessionSettings(guests, Enumerable.Range(101, 6).Select(id => new RoomProfile(id, "Room " + id)),
                new BoilerSettings(), new EconomySettings(startingCash: 10000));
            var hotel = new HotelSimulation(settings, settings.Rooms.Select(room => new RoomState(room)).ToArray(),
                new LivingHotelSettings(), electricity: new ElectricitySettings(circuitCapacity: 3),
                operations: new OperationsSettings());
            Require(hotel.StartOperations()); return hotel;
        }

        static void Require(CommandResult result) => Assert.That(result.Success, Is.True, result.Message);

        [Test]
        public void ActualHeaterDemandWarningTripAndPowerLossHaveOrderedMeasuredEvidence()
        {
            var hotel = Create(); int beforeRevision = hotel.EventRevision;
            Require(hotel.SetRadiatorSetting(0, 101, 3));
            Assert.That(hotel.EventRevision, Is.EqualTo(beforeRevision), "Diagnostic evidence must not create gameplay events.");
            foreach (string id in new[] { "trace-heater-a", "trace-heater-b" })
            {
                Require(hotel.Heaters.Register(id)); hotel.RefreshElectrical();
                Require(hotel.Heaters.AssignRoom(id, 101)); hotel.RefreshElectrical();
                Require(hotel.Heaters.SetSwitchedOn(id, true)); hotel.RefreshElectrical();
            }
            var on = hotel.InfrastructureHistory.Last(item => item.Kind == InfrastructureChangeKind.HeaterSwitch);
            Assert.That(on.CircuitLoad, Is.EqualTo(4)); Assert.That(on.CircuitCapacity, Is.EqualTo(3));
            hotel.Tick(hotel.Electrical.Settings.WarningSeconds + .25f);
            var warning = hotel.InfrastructureHistory.Single(item => item.Kind == InfrastructureChangeKind.CircuitWarning && item.Value == 1);
            Assert.That(warning.At, Is.GreaterThan(on.At));
            hotel.Tick(hotel.Electrical.Settings.TripSeconds);
            var trip = hotel.InfrastructureHistory.Single(item => item.Kind == InfrastructureChangeKind.CircuitTrip && item.Value == 1);
            Assert.That(trip.Sequence, Is.GreaterThan(warning.Sequence)); Assert.That(trip.CircuitStress, Is.EqualTo(1));
            Assert.That(hotel.InfrastructureHistory.Where(item => item.Kind == InfrastructureChangeKind.HeaterPower && item.Value == 0)
                .All(item => item.Sequence > trip.Sequence), Is.True);
            Assert.That(hotel.InfrastructureHistory.All(item => !item.Diagnostic), Is.True);
            Assert.That(hotel.Boiler.Failed, Is.False);
        }

        [Test]
        public void DiagnosticScopeIsBoundedNestedAndDoesNotRelabelLaterNaturalFailureOrServiceCompletion()
        {
            var hotel = Create();
            using (hotel.BeginDiagnosticInfrastructureChange())
            using (hotel.BeginDiagnosticInfrastructureChange()) hotel.Boiler.OverrideLoad(20);
            Assert.That(hotel.InfrastructureHistory.Single(item => item.Kind == InfrastructureChangeKind.BoilerBand).Diagnostic, Is.True);
            for (int second = 0; second < 120 && !hotel.Boiler.Failed; second++) hotel.Tick(1);
            Assert.That(hotel.Boiler.Failed, Is.True, "Explicit high-load fixture then real accumulated overload failure.");
            var critical = hotel.InfrastructureHistory.First(item => item.Kind == InfrastructureChangeKind.BoilerStressCritical && item.Value == 1);
            var failed = hotel.InfrastructureHistory.Single(item => item.Kind == InfrastructureChangeKind.BoilerFailure && item.Value == 1);
            Assert.That(failed.Sequence, Is.GreaterThan(critical.Sequence)); Assert.That(failed.Diagnostic, Is.False);
            Assert.That(failed.BoilerOutput, Is.EqualTo(hotel.Boiler.HeatingOutput));
            using (hotel.BeginDiagnosticInfrastructureChange())
            {
                hotel.Boiler.OverrideLoad(null);
                Require(hotel.BeginBoilerMaintenance(0, BoilerServiceKind.Full));
            }
            var begun = hotel.InfrastructureHistory.Last(item => item.Kind == InfrastructureChangeKind.BoilerService);
            Assert.That(begun.Value, Is.EqualTo((int)BoilerServiceKind.Full)); Assert.That(begun.Diagnostic, Is.True);
            float deadline = hotel.Boiler.MaintenanceEndsAt;
            hotel.Tick(hotel.Boiler.MaintenanceRemaining(hotel.Elapsed));
            var finished = hotel.InfrastructureHistory.Last(item => item.Kind == InfrastructureChangeKind.BoilerService);
            Assert.That(finished.Value, Is.Zero); Assert.That(finished.PreviousValue, Is.EqualTo((int)BoilerServiceKind.Full));
            Assert.That(finished.At, Is.EqualTo(deadline)); Assert.That(finished.Diagnostic, Is.False);
            Assert.That(hotel.InfrastructureHistory.Last(item => item.Kind == InfrastructureChangeKind.BoilerFailure).Value, Is.Zero);
        }

        [Test]
        public void HistoryRetainsOnly64ActualChangesAcrossReportsAndFreshModelStartsEmpty()
        {
            var hotel = Create();
            for (int index = 0; index < 100; index++) Require(hotel.SetRadiatorSetting(0, 101, index % 2 == 0 ? 0 : 3));
            Assert.That(hotel.InfrastructureHistory.Count, Is.EqualTo(HotelSimulation.InfrastructureHistoryLimit));
            var retained = hotel.InfrastructureHistory.Select(item => item.Sequence).ToArray();
            Assert.That(retained.Distinct().Count(), Is.EqualTo(64));
            Assert.That(retained.Last(), Is.EqualTo(100));
            hotel.Tick(hotel.NextReportAt - hotel.Elapsed);
            Assert.That(hotel.ReportSequence, Is.EqualTo(1));
            Assert.That(hotel.InfrastructureHistory.Select(item => item.Sequence), Is.EqualTo(retained));
            Assert.That(Create().InfrastructureHistory, Is.Empty);
        }

        [Test]
        public void NoOpsInvalidCommandsAndReadOnlyMutationAttemptsDoNotAppendOrEmitHistory()
        {
            var hotel = Create(); int revision = hotel.EventRevision;
            using (hotel.BeginDiagnosticInfrastructureChange())
            {
                Require(hotel.SetRadiatorSetting(0, 101, 1));
                Assert.That(hotel.SetRadiatorSetting(2, 101, 3).Success, Is.False);
                Assert.That(hotel.ResetCircuit(0, "A").Success, Is.False);
            }
            Assert.That(hotel.InfrastructureHistory, Is.Empty); Assert.That(hotel.EventRevision, Is.EqualTo(revision));
            hotel.EnableReadOnlyMirror();
            using (hotel.BeginDiagnosticInfrastructureChange())
            {
                Assert.That(hotel.SetRadiatorSetting(0, 101, 3).Success, Is.False);
                hotel.Boiler.OverrideLoad(20); hotel.Boiler.ForceFailure(); hotel.RefreshElectrical(); hotel.Tick(100);
            }
            Assert.That(hotel.InfrastructureHistory, Is.Empty); Assert.That(hotel.EventRevision, Is.EqualTo(revision));
        }
    }
}
