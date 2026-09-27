using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace WorstHotel.Tests
{
    public sealed class UpgradeSnapshotTests
    {
        static HotelSimulation Create(bool continuous = true, bool extremeBoiler = false, bool extremeCircuit = false)
        {
            var profiles = new[] {
                new GuestProfile(GuestKind.Budget, "Budget", "", 180, .85f, 18, .7f, 28, 90, .55f),
                new GuestProfile(GuestKind.ColdSensitive, "Cold", "", 300, 1.05f, 20, 1.35f, 16, 65, .4f),
                new GuestProfile(GuestKind.Business, "Business", "", 450, 1, 19.5f, 1, 10, 45, .25f) };
            var settings = new SessionSettings(profiles, Enumerable.Range(101, 6).Select(id => new RoomProfile(id, "Room " + id)),
                new BoilerSettings(safeLoad: extremeBoiler ? float.MaxValue : 4.2f), new EconomySettings(startingCash: 10000));
            var model = new HotelSimulation(settings, settings.Rooms.Select(room => new RoomState(room)).ToArray(),
                new LivingHotelSettings(), electricity: new ElectricitySettings(circuitCapacity: extremeCircuit ? float.MaxValue : 4),
                operations: continuous ? new OperationsSettings() : null);
            if (continuous) Require(model.StartOperations());
            return model;
        }
        static void Require(CommandResult result) => Assert.That(result.Success, Is.True, result.Message);
        static void Advance(HotelSimulation model, float target)
        { while (model.Elapsed < target) model.Tick(Math.Min(1, target - model.Elapsed)); }
        static HotelModelSnapshot Wire(HotelSimulation model, long sequence) =>
            JsonUtility.FromJson<HotelModelSnapshot>(JsonUtility.ToJson(model.CaptureSnapshot(707, sequence)));
        static string State(HotelSimulation model) => JsonUtility.ToJson(model.CaptureSnapshot(707, 1));

        [TestCase("A")]
        [TestCase("B")]
        public void PurchasedCapacityMirrorsThroughMidnightAndReportWithoutRepeatingCapitalDebit(string branch)
        {
            var host = Create(); var mirror = Create(); mirror.EnableReadOnlyMirror();
            Require(host.PurchaseBoilerUpgrade(0)); Require(host.PurchaseElectricalUpgrade(1, branch));
            int cash = host.Economy.Cash;
            Require(mirror.ApplySnapshot(Wire(host, 1)));
            Assert.That(State(mirror), Is.EqualTo(State(host)));
            Assert.That(mirror.Boiler.RatedCapacity, Is.EqualTo(5.25f).Within(.00001f));
            Assert.That(mirror.Electrical.Find(branch).Capacity, Is.EqualTo(5));
            Assert.That(mirror.Electrical.Find(branch == "A" ? "B" : "A").Capacity, Is.EqualTo(4));
            Advance(host, host.Calendar.At(2, 0) + .25f);
            Require(mirror.ApplySnapshot(Wire(host, 2)));
            Assert.That(State(mirror), Is.EqualTo(State(host)));
            Assert.That(host.Economy.Cash, Is.EqualTo(cash));
            Advance(host, host.NextReportAt + .25f);
            Require(mirror.ApplySnapshot(Wire(host, 3)));
            Assert.That(State(mirror), Is.EqualTo(State(host)));
            Assert.That(mirror.LastReport.CapitalSpend, Is.EqualTo(3000));
            Assert.That(mirror.LastReport.Net, Is.EqualTo(-3450));
            Assert.That(host.Economy.Cash, Is.EqualTo(cash - 450));
            Assert.That(mirror.PeriodCapitalSpend, Is.Zero);
            string before = State(mirror);
            Assert.That(mirror.PurchaseBoilerUpgrade(0).Success, Is.False);
            Assert.That(mirror.PurchaseElectricalUpgrade(1, branch).Success, Is.False);
            Assert.That(State(mirror), Is.EqualTo(before));
        }

        [TestCase("branch")]
        [TestCase("spend")]
        [TestCase("overflow")]
        [TestCase("report")]
        [TestCase("report-overflow")]
        public void MalformedCapitalFieldsRejectBeforeAnyReplicaMutation(string change)
        {
            var host = Create(); var mirror = Create(); mirror.EnableReadOnlyMirror();
            Require(host.PurchaseBoilerUpgrade(0)); Require(host.PurchaseElectricalUpgrade(1, "B"));
            Advance(host, host.NextReportAt + .25f);
            Require(mirror.ApplySnapshot(Wire(host, 1)));
            var bad = Wire(host, 2);
            switch (change)
            {
                case "branch": bad.UpgradedCircuitId = "C"; break;
                case "spend": bad.Operations.PeriodCapitalSpend = -1; break;
                case "overflow": bad.Operations.PeriodCapitalSpend = int.MaxValue; break;
                case "report": bad.Reports[0].CapitalSpend = -1; break;
                case "report-overflow": bad.Reports[0].CapitalSpend = int.MaxValue; break;
            }
            RejectWithoutConsuming(host, mirror, bad);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void LegacyPacketCannotAcquireCapacityPurchases(bool boiler)
        {
            var host = Create(false); var mirror = Create(false); mirror.EnableReadOnlyMirror();
            Require(mirror.ApplySnapshot(Wire(host, 1)));
            var bad = Wire(host, 2);
            if (boiler) bad.Boiler.CapacityUpgradePurchased = true;
            else bad.UpgradedCircuitId = "A";
            RejectWithoutConsuming(host, mirror, bad);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void HostPacketCannotInstallUnrepresentableCapacityForExtremeValidConfiguration(bool boiler)
        {
            var host = Create(extremeBoiler: boiler, extremeCircuit: !boiler);
            var mirror = Create(extremeBoiler: boiler, extremeCircuit: !boiler); mirror.EnableReadOnlyMirror();
            Require(mirror.ApplySnapshot(Wire(host, 1)));
            var bad = Wire(host, 2);
            if (boiler) bad.Boiler.CapacityUpgradePurchased = true;
            else bad.UpgradedCircuitId = "A";
            RejectWithoutConsuming(host, mirror, bad);
        }

        static void RejectWithoutConsuming(HotelSimulation host, HotelSimulation mirror, HotelModelSnapshot bad)
        {
            string before = State(mirror);
            Assert.That(mirror.ApplySnapshot(bad).Success, Is.False);
            Assert.That(State(mirror), Is.EqualTo(before));
            Assert.That(mirror.AppliedSnapshotSequence, Is.EqualTo(1));
            Require(mirror.ApplySnapshot(Wire(host, 2)));
            Assert.That(State(mirror), Is.EqualTo(State(host)));
        }
    }
}
