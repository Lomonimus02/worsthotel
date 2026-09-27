using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace WorstHotel.Tests
{
    public sealed class RhythmSnapshotTests
    {
        static HotelSimulation Create()
        {
            var config = AssetDatabase.LoadAssetAtPath<SessionConfig>("Assets/_WorstHotel/ScriptableObjects/PrototypeSession.asset");
            var settings = config.ToData();
            // Only supply is prescribed. Production dated rhythm and all living systems stay enabled.
            var hotel = new HotelSimulation(settings, settings.Rooms.Select(room => new RoomState(room)).ToArray(),
                config.living.ToData(), config.needs.ToData(), config.noise.ToData(), config.heater.ToData(),
                config.electricity.ToData(), config.housekeeping.ToData(), config.services.ToData(),
                config.infrastructure.ToData(), ManualBookingFixture.Operations(config));
            Require(hotel.StartOperations());
            return hotel;
        }
        static void Require(CommandResult value) => Assert.That(value.Success, Is.True, value.Message);
        static void Advance(HotelSimulation hotel, float target)
        { while (hotel.Elapsed < target) hotel.Tick(Math.Min(.25f, target - hotel.Elapsed)); }
        static HotelModelSnapshot Wire(HotelSimulation hotel, long sequence) =>
            JsonUtility.FromJson<HotelModelSnapshot>(JsonUtility.ToJson(hotel.CaptureSnapshot(6041, sequence)));
        static string State(HotelSimulation hotel) => JsonUtility.ToJson(hotel.CaptureSnapshot(6041, 1));
        static GuestStay Materialize(HotelSimulation hotel)
        {
            var offer = hotel.BookingOffers.First();
            Require(hotel.AcceptBooking(0, offer.Id, 101, 180));
            Advance(hotel, offer.ArrivalAt + .25f);
            // No route callbacks: this is an immutable wire-state test, not a physical stay.
            return hotel.Guests.Single();
        }

        [Test]
        public void DatedRhythmRoundTripsWithStableGuestIdentityAcrossMidnightAndPureReplicaPreview()
        {
            var host = Create(); var guest = Materialize(host);
            Assert.That(guest.Agent.Schedule.MorningActivityIndex, Is.GreaterThanOrEqualTo(1));
            var mirror = Create(); mirror.EnableReadOnlyMirror();
            Require(mirror.ApplySnapshot(Wire(host, 1)));
            Assert.That(State(mirror), Is.EqualTo(State(host)));
            var replicaGuest = mirror.Guests.Single();
            string before = State(mirror);
            var reservation = mirror.FindReservation(guest.GuestId);
            var timing = mirror.Schedules.DatedTimingFor(reservation.Offer.Application, reservation.Offer.ArrivalDay,
                reservation.Offer.ArrivalAt, mirror.Calendar);
            Assert.That(timing.SleepAt, Is.EqualTo(replicaGuest.Agent.Schedule.SleepTime));
            Assert.That(timing.WakeAt, Is.EqualTo(replicaGuest.Agent.Schedule.WakeTime));
            Assert.That(timing.OutingReturnAt, Is.EqualTo(replicaGuest.Agent.Schedule.OutingReturnAt));
            mirror.Tick(60);
            Assert.That(State(mirror), Is.EqualTo(before));
            Advance(host, host.Calendar.At(2, .1f));
            Require(mirror.ApplySnapshot(Wire(host, 2)));
            Assert.That(mirror.Guests.Single(), Is.SameAs(replicaGuest));
            Assert.That(State(mirror), Is.EqualTo(State(host)));
        }

        [TestCase("missing-morning")]
        [TestCase("negative-morning")]
        [TestCase("outside-morning")]
        [TestCase("missing-shower")]
        [TestCase("nan-return")]
        [TestCase("early-return")]
        [TestCase("late-return")]
        [TestCase("different-return")]
        [TestCase("cursor-wrap")]
        [TestCase("morning-unpack")]
        [TestCase("morning-second-shower")]
        [TestCase("changed-rest-time")]
        [TestCase("repeated-outing")]
        public void MalformedRhythmRejectsBeforeMutatingClockGuestCashOrConsumingSequence(string mutation)
        {
            var host = Create(); Materialize(host);
            var mirror = Create(); mirror.EnableReadOnlyMirror();
            Require(mirror.ApplySnapshot(Wire(host, 1)));
            string before = State(mirror); var stableGuest = mirror.Guests.Single();
            var bad = Wire(host, 2); var agent = bad.Guests.Single().Agent;
            switch (mutation)
            {
                case "missing-morning": agent.MorningActivityIndex = -1; break;
                case "negative-morning": agent.MorningActivityIndex = -2; break;
                case "outside-morning": agent.MorningActivityIndex = agent.Schedule.Length; break;
                case "missing-shower": agent.Schedule[agent.MorningActivityIndex].Activity = GuestActivity.Work; break;
                case "nan-return": agent.OutingReturnAt = float.NaN; break;
                case "early-return": agent.OutingReturnAt = agent.ArrivalTime; break;
                case "late-return": agent.OutingReturnAt = agent.SleepTime; break;
                case "different-return": agent.OutingReturnAt += .5f; break;
                case "cursor-wrap": agent.ActivityIndex = agent.Schedule.Length + 1; break;
                case "morning-unpack": agent.Schedule[agent.Schedule.Length - 1].Activity = GuestActivity.Unpack; break;
                case "morning-second-shower": agent.Schedule[agent.Schedule.Length - 1].Activity = GuestActivity.Shower; break;
                case "repeated-outing": agent.Schedule[2].Activity = agent.Schedule[3].Activity = GuestActivity.LeaveHotel; break;
                case "changed-rest-time":
                    agent.SleepTime += .5f;
                    bad.Operations.Reservations.Single().Offer.SleepAt = agent.SleepTime;
                    var corresponding = bad.Operations.Offers.FirstOrDefault(row => row.Application.Id == bad.Guests.Single().Application.Id);
                    if (corresponding != null) corresponding.SleepAt = agent.SleepTime;
                    break;
            }
            Assert.That(mirror.ApplySnapshot(bad).Success, Is.False, mutation);
            Assert.That(State(mirror), Is.EqualTo(before));
            Assert.That(mirror.Guests.Single(), Is.SameAs(stableGuest));
            Assert.That(mirror.AppliedSnapshotSequence, Is.EqualTo(1));
            Require(mirror.ApplySnapshot(Wire(host, 2)));
            Assert.That(State(mirror), Is.EqualTo(State(host)));
        }
    }
}
