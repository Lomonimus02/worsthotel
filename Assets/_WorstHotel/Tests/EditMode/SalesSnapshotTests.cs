using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace WorstHotel.Tests
{
    public sealed class SalesSnapshotTests
    {
        static HotelSimulation Create(bool start = true)
        {
            var config = AssetDatabase.LoadAssetAtPath<SessionConfig>("Assets/_WorstHotel/ScriptableObjects/PrototypeSession.asset");
            var settings = config.ToData(); var calendar = config.OperationsData();
            // Deterministic wire fixture: certainty at reference-or-cheaper rates, with
            // living guest models but no physical route adapters. Physical sales are tested separately.
            var model = new HotelSimulation(settings, settings.Rooms.Select(room => new RoomState(room)).ToArray(),
                config.living.ToData(), config.needs.ToData(), config.noise.ToData(), config.heater.ToData(),
                config.electricity.ToData(), config.housekeeping.ToData(), config.services.ToData(), config.infrastructure.ToData(),
                new OperationsSettings(calendar.SecondsPerDay, calendar.StartHour, calendar.ReportHour,
                    calendar.ArrivalStartHour, calendar.ArrivalEndHour, calendar.SleepHour, calendar.CheckoutHour,
                    calendar.ReportHistoryLimit, new SalesSettings(enabled: true, baseDemand: 1)));
            if (start) Require(model.StartOperations());
            return model;
        }
        static void Require(CommandResult result) => Assert.That(result.Success, Is.True, result.Message);
        static void Advance(HotelSimulation model, float target)
        { while (model.Elapsed < target) model.Tick(Math.Min(.25f, target - model.Elapsed)); }
        static HotelModelSnapshot Wire(HotelSimulation model, long sequence) =>
            JsonUtility.FromJson<HotelModelSnapshot>(JsonUtility.ToJson(model.CaptureSnapshot(5041, sequence)));
        static string State(HotelSimulation model) => JsonUtility.ToJson(model.CaptureSnapshot(5041, 1));

        [Test]
        public void PoliciesAndConsumedDemandRoundTripBeforeOpeningAndAcrossReportWhileReplicaCannotSell()
        {
            var host = Create(false); var mirror = Create(false); mirror.EnableReadOnlyMirror();
            Require(mirror.ApplySnapshot(Wire(host, 1)));
            Assert.That(State(mirror), Is.EqualTo(State(host)));
            Require(host.StartOperations()); Advance(host, 90);
            Assert.That(host.Reservations.Count, Is.EqualTo(4));
            Assert.That(host.Reservations.All(row => row.IsAutomatic && row.ActorId == -1), Is.True);
            var row = host.RoomSalesPolicies.Single(item => item.RoomId == 101);
            Require(host.SetRoomSalesPolicy(1, 101, false, 360, row.Revision));
            var reservation = host.Reservations.First();
            Require(host.ReassignBooking(1, reservation.Id, 106, reservation.Revision));
            Require(mirror.ApplySnapshot(Wire(host, 2)));
            Assert.That(State(mirror), Is.EqualTo(State(host)));
            Assert.That(mirror.FindReservation(reservation.Id).IsAutomatic, Is.True);
            Assert.That(mirror.FindReservation(reservation.Id).Price, Is.EqualTo(180));
            string before = State(mirror);
            mirror.Tick(720);
            Assert.That(mirror.SetRoomSalesPolicy(0, 101, true, 180, row.Revision).Success, Is.False);
            Assert.That(mirror.ReassignBooking(0, reservation.Id, 101, reservation.Revision).Success, Is.False);
            Assert.That(State(mirror), Is.EqualTo(before));
            Advance(host, host.Calendar.At(2, 6) + .25f);
            Require(mirror.ApplySnapshot(Wire(host, 3)));
            Assert.That(State(mirror), Is.EqualTo(State(host)));
            Assert.That(mirror.RoomSalesPolicies.Single(item => item.RoomId == 101).OpenForSale, Is.False);
            Assert.That(mirror.SalesDecisionCursors.Select(item => item.ArrivalDay), Is.EquivalentTo(new[] { 2, 3 }));
            before = State(mirror); mirror.Tick(720);
            Assert.That(State(mirror), Is.EqualTo(before));
        }

        [TestCase("missing-sales")]
        [TestCase("missing-settings")]
        [TestCase("mode")]
        [TestCase("seed")]
        [TestCase("nan")]
        [TestCase("timing")]
        [TestCase("missing-room")]
        [TestCase("duplicate-room")]
        [TestCase("unknown-room")]
        [TestCase("rate")]
        [TestCase("revision")]
        [TestCase("missing-cursor")]
        [TestCase("duplicate-day")]
        [TestCase("unknown-day")]
        [TestCase("negative-index")]
        [TestCase("oversized-index")]
        [TestCase("future-consumed")]
        [TestCase("past-pending")]
        [TestCase("manual-origin")]
        [TestCase("automatic-id")]
        [TestCase("exhausted-booking-revision")]
        public void InvalidPolicyCursorOrOriginRejectsBeforeReplicaCashClockOrSequenceChanges(string mutation)
        {
            var host = Create(); var mirror = Create(false); mirror.EnableReadOnlyMirror();
            Advance(host, 90); Require(mirror.ApplySnapshot(Wire(host, 1)));
            string before = State(mirror); var bad = Wire(host, 2); var sales = bad.Operations.Sales;
            switch (mutation)
            {
                case "missing-sales": bad.Operations.Sales = null; break;
                case "missing-settings": sales.Settings = null; break;
                case "mode": sales.Settings.Enabled = false; break;
                case "seed": sales.Settings.Seed++; break;
                case "nan": sales.Settings.BaseDemand = float.NaN; break;
                case "timing": sales.Settings.AdvanceDecisionStartHour += .5f; break;
                case "missing-room": sales.Rooms = sales.Rooms.Take(5).ToArray(); break;
                case "duplicate-room": sales.Rooms[1].RoomId = sales.Rooms[0].RoomId; break;
                case "unknown-room": sales.Rooms[0].RoomId = 999; break;
                case "rate": sales.Rooms[0].Price = 181; break;
                case "revision": sales.Rooms[0].Revision = 0; break;
                case "missing-cursor": sales.Days = sales.Days.Take(1).ToArray(); break;
                case "duplicate-day": sales.Days[1].ArrivalDay = sales.Days[0].ArrivalDay; break;
                case "unknown-day": sales.Days[1].ArrivalDay = 3; break;
                case "negative-index": sales.Days[0].NextOfferIndex = -1; break;
                case "oversized-index": sales.Days[0].NextOfferIndex = 9; break;
                case "future-consumed": sales.Days.Single(item => item.ArrivalDay == 1).NextOfferIndex = 8; break;
                case "past-pending": sales.Days.Single(item => item.ArrivalDay == 1).NextOfferIndex = 0; break;
                case "manual-origin": bad.Operations.Reservations[0].IsAutomatic = false; bad.Operations.Reservations[0].ActorId = 0; break;
                case "automatic-id": bad.Operations.Reservations[0].Offer.Application.Id = "stay-1-debug999"; break;
                case "exhausted-booking-revision": bad.Operations.Reservations[0].Revision = int.MaxValue - 1; break;
            }
            Assert.That(mirror.ApplySnapshot(bad).Success, Is.False, mutation);
            Assert.That(State(mirror), Is.EqualTo(before));
            Assert.That(mirror.AppliedSnapshotSequence, Is.EqualTo(1));
            Require(mirror.ApplySnapshot(Wire(host, 2)));
            Assert.That(State(mirror), Is.EqualTo(State(host)));
        }
    }
}
