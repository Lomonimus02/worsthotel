using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace WorstHotel.Tests
{
    public sealed class ContinuousCausalRetentionTests
    {
        sealed class Fixture
        {
            public readonly HotelSimulation Hotel;
            public readonly RoomState[] Rooms;

            public Fixture(bool sensitive = true)
            {
                var needs = sensitive ? new NeedProfile(21, 25, 18, 28, .08f, .2f, 45) :
                    new NeedProfile(5, 40, 0, 45, .8f, 1, 90);
                var profiles = new[]
                {
                    new GuestProfile(GuestKind.Budget, "Budget", "", 180, .85f, 18, .7f, 28, 90, .55f, needs: needs),
                    new GuestProfile(GuestKind.ColdSensitive, "Cold", "", 300, 1.05f, 20, 1.35f, 16, 65, .4f, needs: needs),
                    new GuestProfile(GuestKind.Business, "Business", "", 450, 1, 19.5f, 1, 10, 45, .25f, needs: needs)
                };
                // Retention tests isolate unrelated wear and spontaneous excursions. Neither
                // these schedules nor the headless route acknowledgements claim natural pacing.
                var settings = new SessionSettings(profiles, Enumerable.Range(101, 6).Select(id =>
                    new RoomProfile(id, "Room " + id, noise: 0, temperature: 22.5f)),
                    new BoilerSettings(safeLoad: 100, baseWearPerMinute: 0, overloadWearPerMinute: 0), new EconomySettings());
                Rooms = settings.Rooms.Select(profile => new RoomState(profile)).ToArray();
                Hotel = new HotelSimulation(settings, Rooms,
                    new LivingHotelSettings(firstActivityDelay: 10000, quietDurationMin: 10000, quietDurationMax: 10000),
                    new NeedSettings(buildupPerSecond: .5f, complaintExposureSeconds: 2,
                        escalatedExposureSeconds: 6, criticalExposureSeconds: 12, recoverySeconds: 2),
                    infrastructure: new RoomInfrastructureSettings(lampWearPerSecond: 0), operations: new OperationsSettings());
                Require(Hotel.StartOperations());
            }
        }

        static void Require(CommandResult result) => Assert.That(result.Success, Is.True, result.Message);
        static void Prune(HotelSimulation hotel) => typeof(HotelSimulation).GetMethod("PruneCompletedOperatingHistory",
            BindingFlags.Instance | BindingFlags.NonPublic).Invoke(hotel, null);

        static void Advance(Fixture fixture, float target, string keepExit = null, bool keepAllExits = false, int coldRoom = 0)
        {
            var hotel = fixture.Hotel;
            while (hotel.Elapsed < target)
            {
                foreach (var room in fixture.Rooms) room.Temperature = room.Profile.Id == coldRoom ? 5 : 22.5f;
                hotel.Tick(Math.Min(1, target - hotel.Elapsed));
                // Explicit model adapters: room departure and hotel exit are separate physical
                // acknowledgements. Omitted hotel exits intentionally pin pending bodies.
                foreach (var guest in hotel.Guests.ToArray())
                {
                    if (guest.Agent.State == GuestAgentState.Arriving)
                    {
                        Require(hotel.SignalGuestReachedReception(guest.GuestId));
                        Require(ModelKeyHandoff.CheckIn(hotel, 0, guest.GuestId));
                        Require(hotel.SignalGuestReachedRoom(guest.GuestId));
                        Require(hotel.ForceActivity(guest.GuestId, GuestActivity.QuietRest));
                    }
                    if (guest.Agent.State == GuestAgentState.LeavingRoom) Require(hotel.SignalGuestLeftRoom(guest.GuestId));
                    if (guest.Agent.State == GuestAgentState.ReturningToRoom) Require(hotel.SignalGuestReturnedRoom(guest.GuestId));
                    if (guest.Agent.State != GuestAgentState.Leaving) continue;
                    var room = fixture.Rooms.Single(item => item.Profile.Id == guest.RoomId);
                    if (room.DepartingGuestId == guest.GuestId) Require(hotel.SignalGuestVacatedRoom(guest.GuestId, guest.RoomId));
                    if (!keepAllExits && guest.GuestId != keepExit) Require(hotel.SignalGuestLeft(guest.GuestId));
                }
            }
        }

        static void PrepareRooms(Fixture fixture)
        {
            var hotel = fixture.Hotel;
            foreach (var room in fixture.Rooms.Where(item => item.Cleanliness == Cleanliness.Dirty))
            {
                string dirty = "dirty:" + room.Profile.Id;
                Require(hotel.PickUpLinen(1, dirty));
                Require(hotel.DepositDirtyLinen(1, dirty));
                string clean = hotel.Housekeeping.Linens.First(item => item.Kind == LinenKind.Clean && item.Location == LinenLocation.OnShelf).Id;
                Require(hotel.PickUpLinen(1, clean));
                Require(hotel.BeginMakeBed(1, room.Profile.Id, clean));
                Require(hotel.AdvanceMakeBed(1, room.Profile.Id, hotel.Housekeeping.Settings.MakeBedSeconds));
            }
        }

        static HotelModelSnapshot Mirror(HotelSimulation source, HotelSimulation mirror, long sequence)
        {
            var snapshot = JsonUtility.FromJson<HotelModelSnapshot>(JsonUtility.ToJson(source.CaptureSnapshot(305, sequence)));
            Require(mirror.ApplySnapshot(snapshot));
            Assert.That(mirror.Guests.Select(guest => guest.GuestId), Is.EqualTo(source.Guests.Select(guest => guest.GuestId)));
            return snapshot;
        }

        [Test]
        public void RetainedNoiseCausePinsOldSourceIdentityWithoutKeepingItsUnrelatedHistoryForever()
        {
            var fixture = new Fixture(); var hotel = fixture.Hotel;
            var sourceOffer = hotel.BookingOffers.First(offer => offer.ArrivalDay == 1 && offer.Application.Archetype.Kind == GuestKind.Budget);
            var affectedOffer = hotel.BookingOffers.First(offer => offer.ArrivalDay == 1 && offer.Application.Archetype.Kind == GuestKind.Business);
            Require(hotel.AcceptBooking(0, sourceOffer.Id, 103, sourceOffer.Application.ReferencePrice));
            Require(hotel.AcceptBooking(0, affectedOffer.Id, 102, affectedOffer.Application.ReferencePrice));
            Advance(fixture, Math.Max(sourceOffer.ArrivalAt, affectedOffer.ArrivalAt) + 1);
            var source = hotel.Guests.Single(guest => guest.GuestId == sourceOffer.Id);
            var affected = hotel.Guests.Single(guest => guest.GuestId == affectedOffer.Id);

            Advance(fixture, hotel.Elapsed + 3, coldRoom: source.RoomId);
            var oldHistory = hotel.Incidents.Items.Single(incident => incident.GuestId == source.GuestId && incident.Reason == IncidentReason.Temperature);
            Assert.That(oldHistory.Stage, Is.GreaterThanOrEqualTo(SituationStage.Complaint));
            Advance(fixture, hotel.Elapsed + 4);
            Assert.That(oldHistory.Resolved, Is.True);
            Require(hotel.ForceActivity(source.GuestId, GuestActivity.LoudRoom));
            Advance(fixture, hotel.Elapsed + 3);
            var retainedCause = hotel.Incidents.Items.Single(incident => incident.GuestId == affected.GuestId && incident.Reason == IncidentReason.Noise);
            Assert.That(retainedCause.Stage, Is.GreaterThanOrEqualTo(SituationStage.Complaint));
            Assert.That(retainedCause.Cause.SourceGuestId, Is.EqualTo(source.GuestId));
            Assert.That(retainedCause.History.First().Time, Is.GreaterThan(oldHistory.History.First().Time));
            Require(hotel.ForceActivity(source.GuestId, GuestActivity.QuietRest));

            Advance(fixture, hotel.Calendar.At(2, 11), keepExit: affected.GuestId);
            Assert.That(hotel.CaptureSnapshot(305, 1).Incidents.Any(incident => incident.Id == oldHistory.Id), Is.True);
            int sourceComplaints = source.Memory.NumberOfComplaints, sourceIgnored = source.Memory.ProblemsIgnored;
            int affectedComplaints = affected.Memory.NumberOfComplaints, affectedIgnored = affected.Memory.ProblemsIgnored;
            Advance(fixture, hotel.Calendar.At(8, 8), keepExit: affected.GuestId);
            Prune(hotel);

            Assert.That(source.ReceiptPosted, Is.True);
            Assert.That(source.Agent.State, Is.EqualTo(GuestAgentState.Left));
            Assert.That(affected.Agent.State, Is.EqualTo(GuestAgentState.Leaving));
            Assert.That(hotel.Guests.Single(guest => guest.GuestId == source.GuestId), Is.SameAs(source));
            Assert.That(hotel.Noise.Sources, Is.Empty, "The historical cause, not a still-playing television, pins this identity.");
            Assert.That(source.Perception.NoiseSources, Is.Empty);
            Assert.That(hotel.Incidents.Items.Any(incident => incident.GuestId == source.GuestId), Is.False);
            Assert.That(hotel.Requests.Items.Any(request => request.GuestId == source.GuestId), Is.False);
            Assert.That(hotel.Incidents.Items.Single(incident => incident.Id == retainedCause.Id), Is.SameAs(retainedCause));
            Assert.That(source.Memory.NumberOfComplaints, Is.EqualTo(sourceComplaints));
            Assert.That(source.Memory.ProblemsIgnored, Is.EqualTo(sourceIgnored));
            Assert.That(affected.Memory.NumberOfComplaints, Is.EqualTo(affectedComplaints));
            Assert.That(affected.Memory.ProblemsIgnored, Is.EqualTo(affectedIgnored));
            var replica = new Fixture().Hotel; replica.EnableReadOnlyMirror();
            var snapshot = Mirror(hotel, replica, 2);
            Assert.That(snapshot.Incidents.All(incident => incident.GuestId != source.GuestId), Is.True,
                "Hidden records must retire too; keeping a causal identity must not retain the source's whole history.");
            Assert.That(replica.Incidents.Items.Single(incident => incident.Id == retainedCause.Id).Cause.SourceGuestId, Is.EqualTo(source.GuestId));

            Require(hotel.SignalGuestLeft(affected.GuestId));
            int revision = hotel.EventRevision, cash = hotel.Economy.Cash;
            Prune(hotel); Prune(hotel);
            Assert.That(hotel.Guests, Is.Empty);
            Assert.That(hotel.Reservations, Is.Empty);
            Assert.That(hotel.Incidents.Items, Is.Empty);
            Assert.That(hotel.Requests.Items, Is.Empty);
            Assert.That(hotel.EventRevision, Is.EqualTo(revision));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(cash));
            Assert.That(hotel.DayReports.Any(report => report.Receipts.Any(receipt => receipt.GuestId == source.GuestId)), Is.True);
            Mirror(hotel, replica, 3);
        }

        [Test]
        public void FullProtectedDepartureSetRefusesNewAdmissionUntilOneOldBodyActuallyExits()
        {
            var fixture = new Fixture(false); var hotel = fixture.Hotel;
            string firstId = null;
            for (int day = 1; day <= 22; day++)
            {
                PrepareRooms(fixture);
                int count = day == 22 ? 2 : 6;
                var offers = hotel.BookingOffers.Where(offer => offer.ArrivalDay == day).Take(count).ToArray();
                Assert.That(offers.Length, Is.EqualTo(count));
                firstId = firstId ?? offers[0].Id;
                for (int index = 0; index < offers.Length; index++)
                    Require(hotel.AcceptBooking(0, offers[index].Id, 101 + index, offers[index].Application.ReferencePrice));
                // Artificially delayed hotel exits stress the admission bound; every stay
                // otherwise uses its real dated booking, checkout receipt and linen turnover.
                Advance(fixture, hotel.Calendar.At(day + 1, 11), keepAllExits: true);
            }
            PrepareRooms(fixture);
            Assert.That(hotel.Guests.Count, Is.EqualTo(128));
            Assert.That(hotel.Reservations.Count, Is.EqualTo(128));
            Assert.That(hotel.Guests.All(guest => guest.ReceiptPosted && guest.Agent.State == GuestAgentState.Leaving), Is.True);
            var offerToAccept = hotel.BookingOffers.First(offer => offer.ArrivalDay == 23);
            Require(hotel.CanReserveRoom(101, offerToAccept));
            var protectedIds = hotel.Guests.Select(guest => guest.GuestId).ToArray();
            int cash = hotel.Economy.Cash, revision = hotel.EventRevision;

            var result = hotel.AcceptBooking(0, offerToAccept.Id, 101, offerToAccept.Application.ReferencePrice);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain("ledger is full"));
            Assert.That(hotel.Guests.Select(guest => guest.GuestId), Is.EqualTo(protectedIds));
            Assert.That(hotel.FindReservation(offerToAccept.Id), Is.Null);
            Assert.That(hotel.Economy.Cash, Is.EqualTo(cash));
            Assert.That(hotel.EventRevision, Is.EqualTo(revision));
            var replica = new Fixture(false).Hotel; replica.EnableReadOnlyMirror();
            Mirror(hotel, replica, 1);

            Require(hotel.SignalGuestLeft(firstId));
            Require(hotel.AcceptBooking(0, offerToAccept.Id, 101, offerToAccept.Application.ReferencePrice));
            Assert.That(hotel.Guests.Count, Is.EqualTo(127));
            Assert.That(hotel.Guests.Any(guest => guest.GuestId == firstId), Is.False);
            Assert.That(hotel.Reservations.Count, Is.EqualTo(128));
            Assert.That(hotel.FindReservation(offerToAccept.Id).Status, Is.EqualTo(ReservationStatus.Reserved));
            Mirror(hotel, replica, 2);
        }
    }
}
