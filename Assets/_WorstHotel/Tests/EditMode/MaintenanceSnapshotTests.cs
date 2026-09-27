using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace WorstHotel.Tests
{
    public sealed class MaintenanceSnapshotTests
    {
        static HotelSimulation Create()
        {
            var profiles = new[] {
                new GuestProfile(GuestKind.Budget, "Budget", "", 180, .85f, 18, .7f, 28, 90, .55f),
                new GuestProfile(GuestKind.ColdSensitive, "Cold", "", 300, 1.05f, 20, 1.35f, 16, 65, .4f),
                new GuestProfile(GuestKind.Business, "Business", "", 450, 1, 19.5f, 1, 10, 45, .25f) };
            var settings = new SessionSettings(profiles, Enumerable.Range(101, 6).Select(id => new RoomProfile(id, "Room " + id)),
                new BoilerSettings(), new EconomySettings(startingCash: 5000));
            var model = new HotelSimulation(settings, settings.Rooms.Select(room => new RoomState(room)).ToArray(),
                new LivingHotelSettings(), operations: new OperationsSettings());
            Require(model.StartOperations());
            return model;
        }
        static void Require(CommandResult result) => Assert.That(result.Success, Is.True, result.Message);
        static void Advance(HotelSimulation model, float target)
        { while (model.Elapsed < target) model.Tick(Math.Min(.5f, target - model.Elapsed)); }
        static HotelModelSnapshot Wire(HotelSimulation model, long sequence) =>
            JsonUtility.FromJson<HotelModelSnapshot>(JsonUtility.ToJson(model.CaptureSnapshot(606, sequence)));
        static string State(HotelSimulation model) => JsonUtility.ToJson(model.CaptureSnapshot(606, 1));
        static void Patch(HotelSimulation model)
        {
            // Explicit fault fixture, then the same supported green-band guard as the physical sequence.
            model.Boiler.ForceFailure(); Require(model.Boiler.SetRelief(1, true));
            while (!model.Boiler.InRepairBand) model.Tick(.5f);
            Require(model.EmergencyPatchBoiler(0));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PaidStatePersistsThroughReportAndMirrorCannotFinishItsOwnMaintenance(bool maintenance)
        {
            var host = Create(); var mirror = Create(); mirror.EnableReadOnlyMirror();
            Advance(host, host.Calendar.At(2, 5));
            Patch(host);
            if (maintenance) Require(host.BeginBoilerMaintenance(1));
            Advance(host, host.Calendar.At(2, 6) + .25f);
            Require(mirror.ApplySnapshot(Wire(host, 1)));
            Assert.That(State(mirror), Is.EqualTo(State(host)));
            Assert.That(host.LastReport.MaintenanceSpend, Is.EqualTo(maintenance ? 1700 : 200));
            Assert.That(host.Economy.Cash, Is.EqualTo(5000 - 450 - (maintenance ? 1700 : 200)));
            Assert.That(host.PeriodMaintenanceSpend, Is.Zero);
            Assert.That(mirror.Boiler.EmergencyPatchActive, Is.True);
            string before = State(mirror);
            Assert.That(mirror.BeginBoilerMaintenance(0).Success, Is.False);
            Assert.That(mirror.EmergencyPatchBoiler(0).Success, Is.False);
            mirror.Tick(720); mirror.Boiler.Tick(720);
            Assert.That(State(mirror), Is.EqualTo(before));
            if (maintenance)
            {
                Advance(host, host.Boiler.MaintenanceEndsAt);
                Require(mirror.ApplySnapshot(Wire(host, 2)));
                Assert.That(State(mirror), Is.EqualTo(State(host)));
                Assert.That(mirror.Boiler.MaintenanceInProgress, Is.False);
                Assert.That(mirror.Boiler.EmergencyPatchActive, Is.False);
                Assert.That(mirror.Boiler.Condition, Is.EqualTo(95));
                Assert.That(mirror.Boiler.HeatingOutput, Is.GreaterThan(0));
            }
        }

        [TestCase("past")]
        [TestCase("future")]
        [TestCase("nan")]
        [TestCase("negative")]
        [TestCase("heat")]
        [TestCase("relief")]
        [TestCase("spend")]
        [TestCase("overflow")]
        [TestCase("report")]
        [TestCase("failed-patch")]
        public void MalformedPaidStateRejectsAtomicallyWithoutConsumingSequence(string change)
        {
            var host = Create(); var mirror = Create(); mirror.EnableReadOnlyMirror();
            Advance(host, host.Calendar.At(2, 6) + 1);
            Require(host.BeginBoilerMaintenance(0));
            Require(mirror.ApplySnapshot(Wire(host, 1)));
            string before = State(mirror);
            var bad = Wire(host, 2);
            switch (change)
            {
                case "past": bad.Boiler.MaintenanceEndsAt = host.Elapsed; break;
                case "future": bad.Boiler.MaintenanceEndsAt += 720; break;
                case "nan": bad.Boiler.MaintenanceEndsAt = float.NaN; break;
                case "negative": bad.Boiler.MaintenanceEndsAt = -1; break;
                case "heat": bad.Boiler.HeatingOutput = .5f; break;
                case "relief": bad.Boiler.ReliefActorId = 1; break;
                case "spend": bad.Operations.PeriodMaintenanceSpend = -1; break;
                case "overflow": bad.Operations.PeriodMaintenanceSpend = int.MaxValue; break;
                case "report": bad.Reports[0].MaintenanceSpend = -1; break;
                case "failed-patch": bad.Boiler.Failed = true; bad.Boiler.EmergencyPatchActive = true; break;
            }
            Assert.That(mirror.ApplySnapshot(bad).Success, Is.False, change);
            Assert.That(State(mirror), Is.EqualTo(before));
            Assert.That(mirror.AppliedSnapshotSequence, Is.EqualTo(1));
            Require(mirror.ApplySnapshot(Wire(host, 2)));
            Assert.That(State(mirror), Is.EqualTo(State(host)));
        }
    }
}
