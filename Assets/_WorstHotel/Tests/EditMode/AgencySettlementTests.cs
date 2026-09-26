using System.Linq;
using NUnit.Framework;

namespace WorstHotel.Tests
{
    public sealed class AgencySettlementTests
    {
        sealed class Fixture
        {
            public HotelSimulation Hotel;
            public RoomState[] Rooms;
            public GuestStay Guest => Hotel.Guests.Single();
        }

        static Fixture Create()
        {
            var profiles = new[]
            {
                new GuestProfile(GuestKind.Budget, "Budget", "", 180, .85f, 18, .7f, 28, 90, .55f),
                new GuestProfile(GuestKind.ColdSensitive, "Cold", "", 300, 1.05f, 20, 1.35f, 16, 65, .4f),
                new GuestProfile(GuestKind.Business, "Business", "", 450, 1, 19.5f, 1, 10, 45, .25f)
            };
            var settings = new SessionSettings(profiles, Enumerable.Range(101, 6).Select(id =>
                new RoomProfile(id, "Room " + id, noise: 0, temperature: 22.5f)), new BoilerSettings(), new EconomySettings());
            var rooms = settings.Rooms.Select(room => new RoomState(room)).ToArray();
            var hotel = new HotelSimulation(settings, rooms,
                new LivingHotelSettings(firstArrivalSeconds: .2f, arrivalJitterSeconds: 0, firstActivityDelay: 1000),
                new NeedSettings(buildupPerSecond: .5f, complaintExposureSeconds: 2, escalatedExposureSeconds: 6,
                    criticalExposureSeconds: 12, recoverySeconds: 2));
            var offer = new BookingApplication("settlement-guest", "Settlement guest", profiles[0], 180);
            Assert.That(hotel.StartShift(new[] { new BookingAssignment(101, offer.Id, 180, 0) }, new[] { offer }).Success, Is.True);
            hotel.Tick(.4f);
            Assert.That(hotel.SignalGuestReachedReception(offer.Id).Success, Is.True);
            Assert.That(ModelKeyHandoff.CheckIn(hotel, 0, offer.Id).Success, Is.True);
            Assert.That(hotel.SignalGuestReachedRoom(offer.Id).Success, Is.True);
            return new Fixture { Hotel = hotel, Rooms = rooms };
        }

        [Test]
        public void EndShiftWhileGuestIsAlreadyOutsideDoesNotLeaveDepartureOwnershipBlockingTurnover()
        {
            var f = Create(); f.Hotel.Tick(2);
            Assert.That(f.Hotel.ForceLeaveRoom(f.Guest.GuestId).Success, Is.True);
            Assert.That(f.Hotel.SignalGuestLeftRoom(f.Guest.GuestId).Success, Is.True);
            Assert.That(f.Guest.Agent.State, Is.EqualTo(GuestAgentState.GuestAway));
            var report = f.Hotel.EndShift();
            Assert.That(f.Guest.Agent.State, Is.EqualTo(GuestAgentState.Left));
            Assert.That(f.Rooms.All(room => !room.Occupied && !room.Reserved && room.DepartingGuestId == null), Is.True);
            Assert.That(f.Rooms[0].Cleanliness, Is.EqualTo(Cleanliness.Dirty), "Room turnover remains real work after the room becomes physically clear.");
            Assert.That(f.Hotel.Housekeeping.HasPendingWork, Is.True);
            Assert.That(report.Receipts.Single().Price, Is.EqualTo(f.Guest.Price));
            Assert.That(f.Hotel.EndShift(), Is.SameAs(report));
        }

        [Test]
        public void SettlementRecordsAnUnresolvedComplaintBeforeBuildingItsReviewExactlyOnce()
        {
            var f = Create();
            for (int step = 0; step < 16; step++)
            { f.Rooms[0].Temperature = 5; f.Hotel.Tick(.2f); }
            var incident = f.Hotel.Incidents.Items.Single(i => i.Reason == IncidentReason.Temperature);
            Assert.That(incident.Stage, Is.EqualTo(SituationStage.Complaint));
            Assert.That(f.Guest.Memory.NumberOfComplaints, Is.EqualTo(1));
            Assert.That(f.Guest.Memory.ProblemsIgnored, Is.Zero, "This case has not yet reached automatic critical escalation.");
            var report = f.Hotel.EndShift();
            Assert.That(f.Guest.Memory.ProblemsIgnored, Is.EqualTo(1));
            Assert.That(report.Receipts.Single().Review, Does.Contain("leave a reported problem unresolved"));
            Assert.That(incident.Resolved, Is.True, "Departure ends exposure without pretending staff repaired the room.");
            Assert.That(f.Guest.Memory.ProblemsResolvedSuccessfully, Is.Zero);
            Assert.That(f.Hotel.EndShift(), Is.SameAs(report));
            Assert.That(f.Guest.Memory.ProblemsIgnored, Is.EqualTo(1));
        }
    }
}
