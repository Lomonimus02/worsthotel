using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace WorstHotel.Tests
{
    public sealed class EarlyCheckoutSnapshotTests
    {
        static void Require(CommandResult result) => Assert.That(result.Success, Is.True, result.Message);
        static HotelSimulation Create(bool populate = true)
        {
            var needs = new NeedProfile(21, 25, 18, 28, .125f, .25f, 65);
            var profiles = new[] {
                new GuestProfile(GuestKind.Budget, "Budget", "", 180, .85f, 18, .7f, 28, 90, .55f, needs: needs),
                new GuestProfile(GuestKind.ColdSensitive, "Cold", "", 300, 1.05f, 20, 1.35f, 16, 65, .4f, needs: needs),
                new GuestProfile(GuestKind.Business, "Business", "", 450, 1, 19.5f, 1, 10, 45, .25f, needs: needs) };
            var settings = new SessionSettings(profiles, Enumerable.Range(101, 6).Select(id =>
                new RoomProfile(id, "Room " + id, noise: 0, temperature: 22)), new BoilerSettings(), new EconomySettings());
            // A short, controlled policy fixture. Real policy/communication/billing runs;
            // room temperature and physical arrival callbacks are explicit model adapters.
            var hotel = new HotelSimulation(settings, settings.Rooms.Select(room => new RoomState(room)).ToArray(),
                new LivingHotelSettings(firstActivityDelay: 1000, quietDurationMin: 1000, quietDurationMax: 1000),
                new NeedSettings(complaintDissatisfaction: .01f, escalatedDissatisfaction: .02f, criticalDissatisfaction: .03f,
                    complaintExposureSeconds: .25f, escalatedExposureSeconds: .5f, criticalExposureSeconds: 1,
                    earlyCheckout: new EarlyCheckoutSettings(enabled: true, severeHours: .05f, graceHours: .05f,
                        recoveryHours: .01f, minimumRemainingStayHours: .1f)),
                services: new GuestServiceSettings(maxCasesPerShift: 8, maxCasesPerGuest: 4, eligibility: 0,
                    observationSeconds: .1f, naturalCommunicationEnabled: true, selfResponseObserveSeconds: 1000,
                    toleranceSeconds: 1000), operations: new OperationsSettings());
            if (!populate) return hotel;
            Require(hotel.StartOperations());
            var offer = hotel.BookingOffers.First(item => item.ArrivalDay == 1 && item.Application.Archetype.Kind == GuestKind.Business);
            Require(hotel.AcceptBooking(0, offer.Id, 101, 300));
            while (hotel.Elapsed < offer.ArrivalAt + .25f) hotel.Tick(Math.Min(.25f, offer.ArrivalAt + .25f - hotel.Elapsed));
            Require(hotel.SignalGuestReachedReception(offer.Id));
            Require(ModelKeyHandoff.CheckIn(hotel, 0, offer.Id));
            Require(hotel.SignalGuestReachedRoom(offer.Id));
            return hotel;
        }
        static void ColdTick(HotelSimulation hotel)
        { Require(hotel.SetRoomTemperature(101, 5)); hotel.Tick(.25f); }
        static void Reach(HotelSimulation hotel, EarlyCheckoutState state)
        {
            var guest = hotel.Guests.Single();
            if (guest.EarlyCheckout.WarningAt < 0)
            {
                for (int n = 0; n < 32 && !hotel.Incidents.Items.Any(item => item.Active && item.Stage >= SituationStage.Escalated); n++) ColdTick(hotel);
                var incident = hotel.Incidents.Items.Single(item => item.Active && item.Reason == IncidentReason.Temperature);
                Require(hotel.DiscussRoomConcern(0, guest.GuestId, incident.Response.Id));
                Require(hotel.AcceptConsequences(0, guest.GuestId));
            }
            for (int n = 0; n < 120 && guest.EarlyCheckout.State != state; n++) ColdTick(hotel);
            Assert.That(guest.EarlyCheckout.State, Is.EqualTo(state));
        }
        static HotelModelSnapshot Wire(HotelSimulation hotel, long sequence) =>
            JsonUtility.FromJson<HotelModelSnapshot>(JsonUtility.ToJson(hotel.CaptureSnapshot(9041, sequence)));
        static string State(HotelSimulation hotel) => JsonUtility.ToJson(hotel.CaptureSnapshot(9041, 1));

        [Test]
        public void ActualWarningThenCommittedBillRoundTripsAndReplicaCannotProgressThem()
        {
            var host = Create(); var mirror = Create(false); mirror.EnableReadOnlyMirror();
            Reach(host, EarlyCheckoutState.Warning);
            Require(mirror.ApplySnapshot(Wire(host, 1)));
            Assert.That(State(mirror), Is.EqualTo(State(host)));
            Assert.That(mirror.IsEarlyCheckoutWarningKnown(mirror.Guests.Single()), Is.True);
            string before = State(mirror); mirror.Tick(720);
            Assert.That(State(mirror), Is.EqualTo(before));
            Reach(host, EarlyCheckoutState.Committed);
            Require(mirror.ApplySnapshot(Wire(host, 2)));
            Assert.That(State(mirror), Is.EqualTo(State(host)));
            var receipt = Wire(mirror, 1).Operations.PeriodReceipts.Single();
            Assert.That(receipt.EarlyCheckout, Is.True);
            Assert.That(receipt.CheckoutAt, Is.LessThan(mirror.Guests.Single().Agent.CheckoutTime));
            before = State(mirror); mirror.Tick(720);
            Assert.That(State(mirror), Is.EqualTo(before));
        }

        [TestCase("missing")]
        [TestCase("enum")]
        [TestCase("reason")]
        [TestCase("negative")]
        [TestCase("nan")]
        [TestCase("oversized")]
        [TestCase("room")]
        [TestCase("incident")]
        [TestCase("episode")]
        [TestCase("zero-episode")]
        [TestCase("future-warning")]
        [TestCase("missing-warning")]
        [TestCase("grace")]
        [TestCase("pending-commit")]
        [TestCase("empty-dirty")]
        public void InvalidPendingEvidenceRejectsAtomicallyWithoutConsumingSequence(string mutation)
        {
            var host = Create(); var mirror = Create(false); mirror.EnableReadOnlyMirror();
            Reach(host, EarlyCheckoutState.Warning); Require(mirror.ApplySnapshot(Wire(host, 1)));
            string before = State(mirror); var bad = Wire(host, 2); var value = bad.Guests.Single().EarlyCheckout;
            switch (mutation)
            {
                case "missing": bad.Guests[0].EarlyCheckout = null; break;
                case "enum": value.State = (EarlyCheckoutState)99; break;
                case "reason": value.Reason = IncidentReason.Service; break;
                case "negative": value.SevereExposureSeconds = -1; break;
                case "nan": value.RecoverySeconds = float.NaN; break;
                case "oversized": value.IncidentId = new string('x', 513); break;
                case "room": value.RoomId = 999; break;
                case "incident": value.IncidentId = "unknown-episode"; break;
                case "episode": value.IncidentEpisode += 100; break;
                case "zero-episode": value.IncidentEpisode = 0; break;
                case "future-warning": value.WarningAt = bad.Time + 1; break;
                case "missing-warning": value.WarningAt = -1; break;
                case "grace": value.GraceRemainingSeconds += 720; break;
                case "pending-commit": value.CommittedAt = bad.Time; break;
                case "empty-dirty": value.State = EarlyCheckoutState.None; break;
            }
            Assert.That(mirror.ApplySnapshot(bad).Success, Is.False, mutation);
            Assert.That(State(mirror), Is.EqualTo(before));
            Assert.That(mirror.AppliedSnapshotSequence, Is.EqualTo(1));
            Require(mirror.ApplySnapshot(Wire(host, 2)));
        }

        [TestCase("receipt-flag")]
        [TestCase("missing-receipt")]
        [TestCase("receipt-time")]
        [TestCase("receipt-reason")]
        [TestCase("refund")]
        [TestCase("commit-time")]
        [TestCase("grace")]
        [TestCase("reason-size")]
        public void InvalidCommittedOutcomeRejectsBeforeCashOrReceiptChanges(string mutation)
        {
            var host = Create(); var mirror = Create(false); mirror.EnableReadOnlyMirror();
            Reach(host, EarlyCheckoutState.Warning); Require(mirror.ApplySnapshot(Wire(host, 1)));
            Reach(host, EarlyCheckoutState.Committed);
            string before = State(mirror); var bad = Wire(host, 2);
            var guest = bad.Guests.Single(); var receipt = bad.Operations.PeriodReceipts.Single();
            switch (mutation)
            {
                case "receipt-flag": receipt.EarlyCheckout = false; break;
                case "missing-receipt": bad.Operations.PeriodReceipts = Array.Empty<ReceiptSnapshot>(); break;
                case "receipt-time": receipt.CheckoutAt -= 1; break;
                case "receipt-reason": receipt.DepartureReason = "Different cause"; break;
                case "refund": receipt.Compensation = 0; break;
                case "commit-time": guest.EarlyCheckout.CommittedAt = guest.Agent.CheckoutTime; break;
                case "grace": guest.EarlyCheckout.GraceRemainingSeconds = 1; break;
                case "reason-size": guest.EarlyCheckout.CauseDescription = new string('x', 2049); break;
            }
            Assert.That(mirror.ApplySnapshot(bad).Success, Is.False, mutation);
            Assert.That(State(mirror), Is.EqualTo(before));
            Assert.That(mirror.AppliedSnapshotSequence, Is.EqualTo(1));
            Require(mirror.ApplySnapshot(Wire(host, 2)));
            Assert.That(State(mirror), Is.EqualTo(State(host)));
        }
    }
}
