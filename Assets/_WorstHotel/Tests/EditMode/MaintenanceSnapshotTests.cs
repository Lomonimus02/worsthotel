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
        [TestCase("missing-kind")]
        [TestCase("invalid-kind")]
        [TestCase("idle-deadline")]
        [TestCase("negative-revision")]
        [TestCase("missing-start-revision")]
        [TestCase("exhausted-active-revision")]
        [TestCase("old-service-schema")]
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
                case "missing-kind": bad.Boiler.ActiveServiceKind = BoilerServiceKind.None; break;
                case "invalid-kind": bad.Boiler.ActiveServiceKind = (BoilerServiceKind)99; break;
                case "idle-deadline": bad.Boiler.MaintenanceEndsAt = 0; break;
                case "negative-revision": bad.Boiler.MaintenanceRevision = -1; break;
                case "missing-start-revision": bad.Boiler.MaintenanceRevision = 0; break;
                case "exhausted-active-revision": bad.Boiler.MaintenanceRevision = int.MaxValue; break;
                case "old-service-schema": bad.Version = HotelModelSnapshot.ProtocolVersion - 1; break;
            }
            Assert.That(mirror.ApplySnapshot(bad).Success, Is.False, change);
            Assert.That(State(mirror), Is.EqualTo(before));
            Assert.That(mirror.AppliedSnapshotSequence, Is.EqualTo(1));
            Require(mirror.ApplySnapshot(Wire(host, 2)));
            Assert.That(State(mirror), Is.EqualTo(State(host)));
        }

        [TestCase(BoilerServiceKind.Basic)]
        [TestCase(BoilerServiceKind.Full)]
        public void WorkingServiceKindDeadlineAndRevisionCrossReportAndOnlyHostCompletes(BoilerServiceKind kind)
        {
            var host = Create(); var mirror = Create(); mirror.EnableReadOnlyMirror();
            Advance(host, host.Calendar.At(2, 5.75f));
            host.Boiler.SetCondition(48); // Explicit equipment-wear fixture, no guest or fault claim.
            int revision = host.Boiler.MaintenanceRevision, cash = host.Economy.Cash;
            var economy = new EconomySettings(); var tuning = new BoilerCapacitySettings();
            int cost = kind == BoilerServiceKind.Basic ? economy.BasicMaintenanceCost : economy.ProperRepairCost;
            float hours = kind == BoilerServiceKind.Basic ? tuning.BasicMaintenanceHours : tuning.MaintenanceHours;
            Require(host.BeginBoilerMaintenance(0, kind, revision));
            float ends = host.Boiler.MaintenanceEndsAt;
            Assert.That(ends - host.Elapsed, Is.EqualTo(hours * host.Operations.SecondsPerDay / 24).Within(.0001f));
            Advance(host, host.Calendar.At(2, 6) + .25f);
            Require(mirror.ApplySnapshot(Wire(host, 1)));
            Assert.That(State(mirror), Is.EqualTo(State(host)));
            Assert.That(mirror.Boiler.ActiveServiceKind, Is.EqualTo(kind));
            Assert.That(mirror.Boiler.MaintenanceRevision, Is.EqualTo(revision + 1));
            Assert.That(mirror.Boiler.HeatingOutput, Is.Zero);
            Assert.That(mirror.Boiler.Failed, Is.False);
            Assert.That(host.LastReport.MaintenanceSpend, Is.EqualTo(cost));
            Assert.That(host.Economy.Cash, Is.EqualTo(cash - cost - economy.DailyOperatingCost));
            string before = State(mirror);
            mirror.Tick(720); mirror.Boiler.Tick(720);
            Assert.That(mirror.BeginBoilerMaintenance(1, kind, revision + 1).Success, Is.False);
            Assert.That(State(mirror), Is.EqualTo(before));
            Advance(host, ends);
            Require(mirror.ApplySnapshot(Wire(host, 2)));
            Assert.That(State(mirror), Is.EqualTo(State(host)));
            Assert.That(mirror.Boiler.ActiveServiceKind, Is.EqualTo(BoilerServiceKind.None));
            Assert.That(mirror.Boiler.MaintenanceRevision, Is.EqualTo(revision + 2));
            Assert.That(mirror.Boiler.Condition, Is.EqualTo(kind == BoilerServiceKind.Basic ?
                Math.Min(tuning.BasicMaintenanceConditionCap, 48 + tuning.BasicMaintenanceConditionGain) : tuning.ProperMaintenanceCondition));
            Assert.That(mirror.Boiler.HeatingOutput, Is.EqualTo(1));
            before = State(host);
            Assert.That(host.BeginBoilerMaintenance(1, kind, revision).Success, Is.False,
                "A delayed second command cannot buy another job after completion.");
            Assert.That(State(host), Is.EqualTo(before));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void BasicSnapshotCannotBorrowFullDurationOrRestoreAFailedBoiler(bool failed)
        {
            var host = Create(); var mirror = Create(); mirror.EnableReadOnlyMirror();
            host.Boiler.SetCondition(48);
            Require(host.BeginBoilerMaintenance(0, BoilerServiceKind.Basic, 0));
            Require(mirror.ApplySnapshot(Wire(host, 1)));
            string before = State(mirror);
            var bad = Wire(host, 2);
            if (failed) bad.Boiler.Failed = true;
            else bad.Boiler.MaintenanceEndsAt = host.Elapsed + host.Operations.SecondsPerDay * new BoilerCapacitySettings().MaintenanceHours / 24;
            Assert.That(mirror.ApplySnapshot(bad).Success, Is.False);
            Assert.That(State(mirror), Is.EqualTo(before));
            Require(mirror.ApplySnapshot(Wire(host, 2)));
        }
    }
}
