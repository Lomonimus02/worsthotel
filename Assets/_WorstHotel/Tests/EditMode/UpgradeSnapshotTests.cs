using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace WorstHotel.Tests
{
    public sealed class UpgradeSnapshotTests
    {
        static HotelSimulation Create(bool continuous = true, bool extremeBoiler = false, bool extremeCircuit = false, bool wing = false)
        {
            var profiles = new[] {
                new GuestProfile(GuestKind.Budget, "Budget", "", 180, .85f, 18, .7f, 28, 90, .55f),
                new GuestProfile(GuestKind.ColdSensitive, "Cold", "", 300, 1.05f, 20, 1.35f, 16, 65, .4f),
                new GuestProfile(GuestKind.Business, "Business", "", 450, 1, 19.5f, 1, 10, 45, .25f) };
            var settings = new SessionSettings(profiles, Enumerable.Range(101, wing ? 10 : 6).Select(id => new RoomProfile(id, "Room " + id)),
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
        [TestCase("duplicates")]
        [TestCase("missing")]
        [TestCase("empty-id")]
        [TestCase("null-id")]
        [TestCase("too-many")]
        [TestCase("old-schema")]
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
                case "branch": bad.UpgradedCircuitIds = new[] { "C" }; break;
                case "duplicates": bad.UpgradedCircuitIds = new[] { "A", "A" }; break;
                case "missing": bad.UpgradedCircuitIds = null; break;
                case "empty-id": bad.UpgradedCircuitIds = new[] { "" }; break;
                case "null-id": bad.UpgradedCircuitIds = new string[] { null }; break;
                case "too-many": bad.UpgradedCircuitIds = new[] { "A", "B", "A" }; break;
                case "old-schema": bad.Version = HotelModelSnapshot.ProtocolVersion - 1; break;
                case "spend": bad.Operations.PeriodCapitalSpend = -1; break;
                case "overflow":
                    // Just after a report all other expense buckets are zero; MaxValue alone is representable.
                    bad.Operations.PeriodCapitalSpend = int.MaxValue;
                    bad.Operations.PeriodMaintenanceSpend = 1;
                    break;
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
            // Exercise the model's legacy capacity boundary directly. JsonUtility materializes the
            // absent Operations object; that separate legacy wire compatibility issue is not this guard.
            Require(mirror.ApplySnapshot(host.CaptureSnapshot(707, 1)));
            var bad = host.CaptureSnapshot(707, 2);
            if (boiler) bad.Boiler.CapacityUpgradePurchased = true;
            else bad.UpgradedCircuitIds = new[] { "A" };
            RejectWithoutConsuming(host, mirror, bad, throughJson: false,
                expectedRejection: boiler ? "Legacy boiler cannot contain a capacity purchase." : "Invalid purchased branch.");
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
            else bad.UpgradedCircuitIds = new[] { "A" };
            RejectWithoutConsuming(host, mirror, bad);
        }

        [TestCase("A", "B")]
        [TestCase("B", "A")]
        public void BothBranchesMirrorAndPersistAcrossReportsWithoutAnotherDebit(string first, string second)
        {
            var host = Create(); var mirror = Create(); mirror.EnableReadOnlyMirror();
            Require(host.PurchaseElectricalUpgrade(0, first));
            Require(mirror.ApplySnapshot(Wire(host, 1)));
            Assert.That(mirror.Electrical.IsCapacityUpgraded(first), Is.True);
            Assert.That(mirror.Electrical.IsCapacityUpgraded(second), Is.False);
            Require(host.PurchaseElectricalUpgrade(1, second));
            int cash = host.Economy.Cash;
            Require(mirror.ApplySnapshot(Wire(host, 2)));
            Advance(host, host.Calendar.At(2, 0) + .25f);
            Require(mirror.ApplySnapshot(Wire(host, 3)));
            Assert.That(host.Economy.Cash, Is.EqualTo(cash));
            Advance(host, host.NextReportAt + .25f);
            Require(mirror.ApplySnapshot(Wire(host, 4)));
            Assert.That(State(mirror), Is.EqualTo(State(host)));
            Assert.That(mirror.Electrical.Circuits.All(c => mirror.Electrical.IsCapacityUpgraded(c.Id) && c.Capacity == 5), Is.True);
            Assert.That(mirror.LastReport.CapitalSpend, Is.EqualTo(2400));
            Assert.That(mirror.PeriodCapitalSpend, Is.Zero);
            Assert.That(host.Economy.Cash, Is.EqualTo(cash - 450));
            string before = State(mirror);
            Assert.That(mirror.PurchaseElectricalUpgrade(0, first).Success, Is.False);
            Assert.That(mirror.PurchaseElectricalUpgrade(1, second).Success, Is.False);
            Assert.That(State(mirror), Is.EqualTo(before));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void WingRestorationNeverPurchasesElectricalCapacity(bool restoreFirst)
        {
            var host = Create(wing: true);
            int cash = host.Economy.Cash;
            if (restoreFirst)
            {
                Require(host.RestoreNorthWing(0));
                Assert.That(host.Electrical.Circuits.Any(c => host.Electrical.IsCapacityUpgraded(c.Id)), Is.False);
            }
            Require(host.PurchaseElectricalUpgrade(0, "A"));
            Require(host.PurchaseElectricalUpgrade(1, "B"));
            Assert.That(host.NorthWingRestored, Is.EqualTo(restoreFirst));
            if (!restoreFirst) Require(host.RestoreNorthWing(0));
            Assert.That(host.Electrical.Circuits.All(c => host.Electrical.IsCapacityUpgraded(c.Id) && c.Capacity == 5), Is.True);
            Assert.That(host.Economy.Cash, Is.EqualTo(cash - host.PeriodCapitalSpend));
            Assert.That(host.PeriodCapitalSpend, Is.EqualTo(2400 + new EconomySettings().WingRestorationCost));
        }

        static void RejectWithoutConsuming(HotelSimulation host, HotelSimulation mirror, HotelModelSnapshot bad,
            bool throughJson = true, string expectedRejection = null)
        {
            string before = State(mirror);
            var result = mirror.ApplySnapshot(bad);
            Assert.That(result.Success, Is.False);
            if (expectedRejection != null) Assert.That(result.Message, Does.Contain(expectedRejection));
            Assert.That(State(mirror), Is.EqualTo(before));
            Assert.That(mirror.AppliedSnapshotSequence, Is.EqualTo(1));
            Require(mirror.ApplySnapshot(throughJson ? Wire(host, 2) : host.CaptureSnapshot(707, 2)));
            Assert.That(State(mirror), Is.EqualTo(State(host)));
        }
    }
}
