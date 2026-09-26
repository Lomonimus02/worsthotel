using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace WorstHotel.Tests
{
    public sealed class CapacitySnapshotTests
    {
        static HotelSimulation Create(bool continuous = true)
        {
            var profiles = new[]
            {
                new GuestProfile(GuestKind.Budget, "Budget", "", 180, .85f, 18, .7f, 28, 90, .55f),
                new GuestProfile(GuestKind.ColdSensitive, "Cold", "", 300, 1.05f, 20, 1.35f, 16, 65, .4f),
                new GuestProfile(GuestKind.Business, "Business", "", 450, 1, 19.5f, 1, 10, 45, .25f)
            };
            var settings = new SessionSettings(profiles, Enumerable.Range(101, 6).Select(id => new RoomProfile(id, "Room " + id)),
                new BoilerSettings(), new EconomySettings());
            var model = new HotelSimulation(settings, settings.Rooms.Select(room => new RoomState(room)).ToArray(),
                new LivingHotelSettings(), operations: continuous ? new OperationsSettings() : null);
            if (continuous) Require(model.StartOperations());
            return model;
        }

        static void Require(CommandResult result) => Assert.That(result.Success, Is.True, result.Message);
        static void Advance(HotelSimulation model, float target)
        { while (model.Elapsed < target) model.Tick(Math.Min(.5f, target - model.Elapsed)); }
        static HotelModelSnapshot Wire(HotelSimulation model, long sequence) =>
            JsonUtility.FromJson<HotelModelSnapshot>(JsonUtility.ToJson(model.CaptureSnapshot(505, sequence)));
        static string State(HotelSimulation model) => JsonUtility.ToJson(model.CaptureSnapshot(505, 1));

        [TestCase(false)]
        [TestCase(true)]
        public void RealAccumulatedStressAndFailureSurviveMidnightReportAndJsonWithoutAReset(bool failBeforeMidnight)
        {
            var host = Create(); var mirror = Create(); mirror.EnableReadOnlyMirror();
            // Explicit diagnostic demand isolates equipment persistence from schedules. Other
            // capacity tests use actual room consumers, valves and staged shower sources.
            host.Boiler.OverrideLoad(host.Boiler.EffectiveCapacity * 1.05f);
            Advance(host, host.Calendar.At(2, 0) - .25f);
            Assert.That(host.Boiler.Stress01, Is.GreaterThan(.05f));
            Assert.That(host.Boiler.Failed, Is.False, "A mild overload must leave time for a decision.");
            if (failBeforeMidnight) host.Boiler.ForceFailure();
            float before = host.Boiler.Stress01;
            Require(mirror.ApplySnapshot(Wire(host, 1)));
            Assert.That(State(mirror), Is.EqualTo(State(host)));
            host.Tick(1);
            Require(mirror.ApplySnapshot(Wire(host, 2)));
            Assert.That(host.CalendarDay, Is.EqualTo(2));
            Assert.That(host.Boiler.Stress01, Is.GreaterThanOrEqualTo(before));
            Assert.That(mirror.Boiler.Stress01, Is.EqualTo(host.Boiler.Stress01));
            Assert.That(mirror.Boiler.CapacityBand, Is.EqualTo(host.Boiler.CapacityBand));
            Assert.That(mirror.Boiler.EffectiveCapacity, Is.EqualTo(host.Boiler.EffectiveCapacity));
            Advance(host, host.Calendar.At(2, 6) + .25f);
            Require(mirror.ApplySnapshot(Wire(host, 3)));
            Assert.That(host.DayReports.Count, Is.EqualTo(1));
            Assert.That(host.Boiler.Stress01, Is.GreaterThanOrEqualTo(before));
            Assert.That(host.Boiler.Failed, Is.EqualTo(failBeforeMidnight));
            Assert.That(State(mirror), Is.EqualTo(State(host)));
            string unchanged = State(mirror);
            mirror.Boiler.OverrideLoad(0); mirror.Boiler.Tick(100); mirror.Tick(100);
            Assert.That(State(mirror), Is.EqualTo(unchanged), "Only host time can change replica equipment.");
        }

        [TestCase(-.01f)]
        [TestCase(1.01f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void MalformedStressRejectsBeforeMutationAndDoesNotConsumeSequence(float stress)
        {
            var host = Create(); var mirror = Create(); mirror.EnableReadOnlyMirror();
            host.Boiler.OverrideLoad(host.Boiler.EffectiveCapacity * 1.1f); host.Tick(10);
            Require(mirror.ApplySnapshot(Wire(host, 1)));
            string before = State(mirror);
            var packet = Wire(host, 2); packet.Boiler.Stress01 = stress;
            Assert.That(mirror.ApplySnapshot(packet).Success, Is.False);
            Assert.That(State(mirror), Is.EqualTo(before));
            Assert.That(mirror.AppliedSnapshotSequence, Is.EqualTo(1));
            Require(mirror.ApplySnapshot(Wire(host, 2)));
            Assert.That(State(mirror), Is.EqualTo(State(host)));
        }

        [Test]
        public void LegacyBoilerCannotAcquireContinuousStressThroughAHostPacket()
        {
            var host = Create(false); var mirror = Create(false); mirror.EnableReadOnlyMirror();
            Require(mirror.ApplySnapshot(Wire(host, 1)));
            var packet = Wire(host, 2); packet.Boiler.Stress01 = .3f;
            string before = State(mirror);
            Assert.That(mirror.ApplySnapshot(packet).Success, Is.False);
            Assert.That(State(mirror), Is.EqualTo(before));
            Require(mirror.ApplySnapshot(Wire(host, 2)));
            Assert.That(mirror.Boiler.Stress01, Is.Zero);
        }
    }
}
