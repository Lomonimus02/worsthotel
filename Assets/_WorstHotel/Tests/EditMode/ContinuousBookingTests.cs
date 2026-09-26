using System;
using System.Linq;
using NUnit.Framework;

namespace WorstHotel.Tests
{
    public sealed class ContinuousBookingTests
    {
        sealed class Fixture
        {
            public SessionSettings Settings;
            public RoomState[] Rooms;
            public HotelSimulation Hotel;
        }

        static Fixture Create()
        {
            var profiles = new[]
            {
                new GuestProfile(GuestKind.Budget, "Budget", "", 180, .85f, 18, .7f, 28, 90, .55f),
                new GuestProfile(GuestKind.ColdSensitive, "Cold-sensitive", "", 300, 1.05f, 20, 1.35f, 16, 65, .4f),
                new GuestProfile(GuestKind.Business, "Business", "", 450, 1, 19.5f, 1, 10, 45, .25f)
            };
            var profilesForRooms = Enumerable.Range(101, 6).Select(id => new RoomProfile(id, "Room " + id)).ToArray();
            // Booking/accounting tests isolate unrelated operating wear; production remains untouched.
            var settings = new SessionSettings(profiles, profilesForRooms,
                new BoilerSettings(baseWearPerMinute: 0, overloadWearPerMinute: 0), new EconomySettings());
            var rooms = profilesForRooms.Select(profile => new RoomState(profile)).ToArray();
            var hotel = new HotelSimulation(settings, rooms, new LivingHotelSettings(), operations: new OperationsSettings());
            Require(hotel.StartOperations());
            return new Fixture { Settings = settings, Rooms = rooms, Hotel = hotel };
        }

        static void Require(CommandResult result) => Assert.That(result.Success, Is.True, result.Message);
        static RoomState Room(Fixture fixture, int id) => fixture.Rooms.Single(room => room.Profile.Id == id);
        static ScheduledBookingOffer Offer(Fixture fixture, int day = 1) => fixture.Hotel.BookingOffers
            .Where(offer => offer.ArrivalDay == day && offer.Application.Archetype.Kind == GuestKind.Budget)
            .OrderBy(offer => offer.ArrivalAt).First();
        static HotelReservation Reservation(Fixture fixture, string id) => fixture.Hotel.Reservations.Single(item => item.Id == id);

        static void Book(Fixture fixture, ScheduledBookingOffer offer, int roomId = 101) =>
            Require(fixture.Hotel.AcceptBooking(0, offer.Id, roomId, offer.Application.ReferencePrice));

        static void AdvanceTo(Fixture fixture, float target)
        {
            Assert.That(target, Is.GreaterThanOrEqualTo(fixture.Hotel.Elapsed));
            while (fixture.Hotel.Elapsed < target)
            {
                fixture.Hotel.Tick(Math.Min(.25f, target - fixture.Hotel.Elapsed));
                // Explicit headless route boundaries for already checked-in guests. These do
                // not claim to test scene navigation, hand out keys, or fabricate room arrival.
                foreach (var guest in fixture.Hotel.Guests.ToArray())
                {
                    if (guest.Agent.State == GuestAgentState.LeavingRoom)
                        Require(fixture.Hotel.SignalGuestLeftRoom(guest.GuestId));
                    else if (guest.Agent.State == GuestAgentState.ReturningToRoom)
                        Require(fixture.Hotel.SignalGuestReturnedRoom(guest.GuestId));
                }
            }
        }

        static GuestStay ReachReception(Fixture fixture, ScheduledBookingOffer offer)
        {
            AdvanceTo(fixture, offer.ArrivalAt + .25f);
            var guest = fixture.Hotel.Guests.Single(item => item.GuestId == offer.Id);
            Assert.That(guest.Agent.State, Is.EqualTo(GuestAgentState.Arriving));
            Require(fixture.Hotel.SignalGuestReachedReception(guest.GuestId));
            return guest;
        }

        static GuestStay CheckIn(Fixture fixture, ScheduledBookingOffer offer)
        {
            var guest = ReachReception(fixture, offer);
            Require(ModelKeyHandoff.CheckIn(fixture.Hotel, 0, guest.GuestId));
            Require(fixture.Hotel.SignalGuestReachedRoom(guest.GuestId));
            return guest;
        }

        static void PrepareDirtyRoom(Fixture fixture, int roomId)
        {
            // The ordinary linen commands are retained; only the physical hold is a model adapter.
            Require(fixture.Hotel.PickUpLinen(1, "dirty:" + roomId));
            Require(fixture.Hotel.DepositDirtyLinen(1, "dirty:" + roomId));
            string clean = fixture.Hotel.Housekeeping.Linens.First(item =>
                item.Kind == LinenKind.Clean && item.Location == LinenLocation.OnShelf).Id;
            Require(fixture.Hotel.PickUpLinen(1, clean));
            Require(fixture.Hotel.BeginMakeBed(1, roomId, clean));
            Require(fixture.Hotel.AdvanceMakeBed(1, roomId, fixture.Hotel.Housekeeping.Settings.MakeBedSeconds));
            Assert.That(Room(fixture, roomId).Cleanliness, Is.EqualTo(Cleanliness.Clean));
        }

        static string State(Fixture fixture)
        {
            var hotel = fixture.Hotel;
            return hotel.Elapsed + ";" + hotel.EventRevision + ";" + hotel.Economy.Cash + ";" + hotel.ReportSequence + ";" +
                string.Join("|", hotel.Reservations.Select(item => item.Id + ":" + item.Status + ":" + item.RoomId + ":" + item.Price + ":" + item.Revision)) + ";" +
                string.Join("|", hotel.Guests.Select(item => item.GuestId + ":" + item.RoomId + ":" + item.Agent.State + ":" + item.ReceiptPosted)) + ";" +
                string.Join("|", fixture.Rooms.Select(item => item.Profile.Id + ":" + item.GuestId + ":" + item.ReservedGuestId + ":" + item.Cleanliness)) + ";" +
                string.Join("|", hotel.Keys.Items.Select(item => item.RoomId + ":" + item.Location + ":" + item.PlayerId + ":" + item.GuestId));
        }

        [Test]
        public void RollingOffersCoverTodayAndTomorrowWithAbsoluteSingleNightDates()
        {
            var fixture = Create();
            var hotel = fixture.Hotel;
            Assert.That(hotel.BookingOffers.Count, Is.EqualTo(16));
            Assert.That(hotel.BookingOffers.Select(offer => offer.Id).Distinct().Count(), Is.EqualTo(16));
            Assert.That(hotel.BookingOffers.Count(offer => offer.ArrivalDay == 1), Is.EqualTo(8));
            Assert.That(hotel.BookingOffers.Count(offer => offer.ArrivalDay == 2), Is.EqualTo(8));
            foreach (var offer in hotel.BookingOffers)
            {
                Assert.That(offer.Id, Is.EqualTo(offer.Application.Id));
                Assert.That(offer.ArrivalAt, Is.InRange(hotel.Calendar.At(offer.ArrivalDay, 14), hotel.Calendar.At(offer.ArrivalDay, 18)));
                Assert.That(offer.SleepAt, Is.GreaterThan(offer.ArrivalAt));
                Assert.That(offer.CheckoutAt, Is.EqualTo(hotel.Calendar.At(offer.ArrivalDay + 1, 10)).Within(.001f));
                Assert.That(offer.WakeAt, Is.EqualTo(hotel.Calendar.At(offer.ArrivalDay + 1, 8)).Within(.001f));
                Assert.That(offer.SleepAt, Is.LessThan(offer.WakeAt));
                Assert.That(offer.Application.ReferencePrice, Is.EqualTo(offer.Application.Archetype.ReferencePrice));
            }
            Assert.That(hotel.Guests, Is.Empty, "Applications are not materialized guest bodies or infrastructure consumers.");
            Assert.That(hotel.Boiler.Load, Is.Zero);
            Assert.That(hotel.Electrical.Consumers, Is.Empty);
        }

        [Test]
        public void ReservationDoesNotMaterializeBeforeItsDueTimeOrTurnFutureRoomIntoAnOccupant()
        {
            var fixture = Create();
            var offer = Offer(fixture, 2);
            Book(fixture, offer);
            var reservation = Reservation(fixture, offer.Id);
            Assert.That(reservation.Status, Is.EqualTo(ReservationStatus.Reserved));
            Assert.That(fixture.Hotel.Guests, Is.Empty);
            Assert.That(Room(fixture, 101).GuestId, Is.Null.Or.Empty);
            Assert.That(Room(fixture, 101).ReservedGuestId, Is.Null.Or.Empty);
            Assert.That(fixture.Hotel.Keys.Find(101).Location, Is.EqualTo(RoomKeyLocation.OnRack));
            AdvanceTo(fixture, offer.ArrivalAt - .25f);
            Assert.That(fixture.Hotel.Guests, Is.Empty);
            Assert.That(reservation.Status, Is.EqualTo(ReservationStatus.Reserved));
            AdvanceTo(fixture, offer.ArrivalAt);
            Assert.That(fixture.Hotel.Guests.Count(item => item.GuestId == offer.Id), Is.EqualTo(1));
            Assert.That(reservation.Status, Is.EqualTo(ReservationStatus.Arrived));
            Assert.That(Room(fixture, 101).ReservedGuestId, Is.EqualTo(offer.Id));
            Assert.That(Room(fixture, 101).GuestId, Is.Null.Or.Empty);
            fixture.Hotel.Tick(1);
            Assert.That(fixture.Hotel.Guests.Count(item => item.GuestId == offer.Id), Is.EqualTo(1));
        }

        [Test]
        public void NextNightReservationPreservesTheCurrentOccupantAndTheirPhysicalKey()
        {
            var fixture = Create();
            var today = Offer(fixture);
            Book(fixture, today);
            var guest = CheckIn(fixture, today);
            var tomorrow = Offer(fixture, 2);
            Require(fixture.Hotel.CanReserveRoom(101, tomorrow));
            Book(fixture, tomorrow);
            Assert.That(fixture.Hotel.Reservations.Count(item => item.RoomId == 101), Is.EqualTo(2));
            Assert.That(fixture.Hotel.Guests.Single(), Is.SameAs(guest));
            Assert.That(Room(fixture, 101).GuestId, Is.EqualTo(guest.GuestId));
            Assert.That(Room(fixture, 101).ReservedGuestId, Is.Null.Or.Empty);
            Assert.That(fixture.Hotel.Keys.Find(101).Location, Is.EqualTo(RoomKeyLocation.HeldByGuest));
            Assert.That(fixture.Hotel.Keys.Find(101).GuestId, Is.EqualTo(guest.GuestId));
            Assert.That(Reservation(fixture, tomorrow.Id).Status, Is.EqualTo(ReservationStatus.Reserved));
            Require(fixture.Hotel.CancelBooking(1, tomorrow.Id));
            Assert.That(Room(fixture, 101).GuestId, Is.EqualTo(guest.GuestId));
            Assert.That(fixture.Hotel.Keys.Find(101).GuestId, Is.EqualTo(guest.GuestId),
                "Cancelling tomorrow's reservation cannot reclaim tonight's guest key.");
        }

        [Test]
        public void OverlappingStayAndDuplicateOfferAreRejectedWithoutMutatingEitherBooking()
        {
            var fixture = Create();
            var first = Offer(fixture);
            var conflict = fixture.Hotel.BookingOffers.First(item => item.ArrivalDay == 1 && item.Id != first.Id);
            Book(fixture, first);
            string before = State(fixture);
            Assert.That(fixture.Hotel.CanReserveRoom(101, conflict).Success, Is.False);
            Assert.That(fixture.Hotel.AcceptBooking(1, conflict.Id, 101, conflict.Application.ReferencePrice).Success, Is.False);
            Assert.That(fixture.Hotel.AcceptBooking(1, first.Id, 102, first.Application.ReferencePrice).Success, Is.False);
            Assert.That(State(fixture), Is.EqualTo(before));
            Require(fixture.Hotel.CanReserveRoom(102, conflict));
            Require(fixture.Hotel.AcceptBooking(1, conflict.Id, 102, conflict.Application.ReferencePrice));
            Assert.That(fixture.Hotel.Reservations.Count, Is.EqualTo(2));
        }

        [Test]
        public void DirtyReservationIsAllowedButCheckInWaitsForRealLinenPreparationAndKey()
        {
            var fixture = Create();
            Require(fixture.Hotel.DebugMarkRoomDirty(101));
            var offer = Offer(fixture);
            Book(fixture, offer);
            var guest = ReachReception(fixture, offer);
            Require(fixture.Hotel.Keys.PickUp(0, 101));
            Assert.That(fixture.Hotel.CheckIn(0, guest.GuestId).Success, Is.False);
            Assert.That(guest.Agent.CheckedIn, Is.False);
            Assert.That(Room(fixture, 101).GuestId, Is.Null.Or.Empty);
            Assert.That(fixture.Hotel.Keys.Find(101).PlayerId, Is.EqualTo(0));
            PrepareDirtyRoom(fixture, 101);
            Require(fixture.Hotel.CheckIn(0, guest.GuestId));
            Require(fixture.Hotel.SignalGuestReachedRoom(guest.GuestId));
            Assert.That(guest.Agent.HasReachedRoom, Is.True);
            Assert.That(Room(fixture, 101).GuestId, Is.EqualTo(guest.GuestId));
            Assert.That(fixture.Hotel.Keys.Find(101).GuestId, Is.EqualTo(guest.GuestId));
        }

        [Test]
        public void ReservationAndReceptionArrivalDoNotBypassPhysicalKeyHandoff()
        {
            var fixture = Create();
            var offer = Offer(fixture);
            Book(fixture, offer);
            var guest = ReachReception(fixture, offer);
            Assert.That(fixture.Hotel.CheckIn(0, guest.GuestId).Success, Is.False);
            Assert.That(guest.Agent.State, Is.EqualTo(GuestAgentState.WaitingForCheckIn));
            Require(fixture.Hotel.Keys.PickUp(1, 101));
            Assert.That(fixture.Hotel.CheckIn(0, guest.GuestId).Success, Is.False, "Another staff member's held key cannot be borrowed by a command.");
            Require(fixture.Hotel.CheckIn(1, guest.GuestId));
            Assert.That(guest.Agent.State, Is.EqualTo(GuestAgentState.GoingToRoom));
            Assert.That(guest.Agent.HasReachedRoom, Is.False);
            Require(fixture.Hotel.SignalGuestReachedRoom(guest.GuestId));
            Assert.That(guest.Agent.HasReachedRoom, Is.True);
        }

        [Test]
        public void ConsecutiveNightsStillWaitForPreviousBodyExitAndLinenBeforeReusingTheRoom()
        {
            var fixture = Create();
            var today = Offer(fixture);
            var tomorrow = Offer(fixture, 2);
            Book(fixture, today);
            Book(fixture, tomorrow);
            var previous = CheckIn(fixture, today);
            AdvanceTo(fixture, today.CheckoutAt + 3);
            Assert.That(previous.ReceiptPosted, Is.True);
            Assert.That(Room(fixture, 101).DepartingGuestId, Is.EqualTo(previous.GuestId));
            var next = ReachReception(fixture, tomorrow);
            Require(fixture.Hotel.Keys.PickUp(0, 101));
            Assert.That(fixture.Hotel.CheckIn(0, next.GuestId).Success, Is.False);
            Assert.That(fixture.Hotel.PickUpLinen(1, "dirty:101").Success, Is.False,
                "Payment and a calendar boundary are not physical departure acknowledgements.");
            Require(fixture.Hotel.SignalGuestVacatedRoom(previous.GuestId, 101));
            Require(fixture.Hotel.SignalGuestLeft(previous.GuestId));
            PrepareDirtyRoom(fixture, 101);
            Require(fixture.Hotel.CheckIn(0, next.GuestId));
            Require(fixture.Hotel.SignalGuestReachedRoom(next.GuestId));
            Assert.That(Room(fixture, 101).GuestId, Is.EqualTo(next.GuestId));
            Assert.That(fixture.Hotel.Keys.Find(101).GuestId, Is.EqualTo(next.GuestId));
            Assert.That(next.ReceiptPosted, Is.False);
            Assert.That(fixture.Hotel.CalendarDay, Is.EqualTo(2));
        }

        [Test]
        public void MaterializedScheduleSleepsOverMidnightWakesNextMorningAndChecksOutOnce()
        {
            var fixture = Create();
            var offer = Offer(fixture);
            Book(fixture, offer);
            var guest = CheckIn(fixture, offer);
            Assert.That(guest.Agent.ArrivalTime, Is.EqualTo(offer.ArrivalAt));
            Assert.That(guest.Agent.Schedule.SleepTime, Is.EqualTo(offer.SleepAt));
            Assert.That(guest.Agent.Schedule.WakeTime, Is.EqualTo(offer.WakeAt));
            Assert.That(guest.Agent.CheckoutTime, Is.EqualTo(offer.CheckoutAt));
            AdvanceTo(fixture, offer.SleepAt + .25f);
            Assert.That(guest.Agent.State, Is.EqualTo(GuestAgentState.Sleeping));
            AdvanceTo(fixture, fixture.Hotel.Calendar.At(2, 0));
            Assert.That(guest.Agent.State, Is.EqualTo(GuestAgentState.Sleeping));
            Assert.That(Room(fixture, 101).GuestId, Is.EqualTo(guest.GuestId));
            AdvanceTo(fixture, offer.WakeAt + .25f);
            Assert.That(guest.Agent.State, Is.Not.EqualTo(GuestAgentState.Sleeping));
            fixture.Hotel.Tick(.25f);
            Assert.That(guest.Agent.State, Is.Not.EqualTo(GuestAgentState.Sleeping), "A past bedtime must not immediately put the guest back to sleep.");
            Assert.That(guest.ReceiptPosted, Is.False);
            AdvanceTo(fixture, offer.CheckoutAt + .25f);
            Assert.That(guest.ReceiptPosted, Is.True);
            Assert.That(Reservation(fixture, offer.Id).Status, Is.EqualTo(ReservationStatus.Completed));
            Assert.That(Room(fixture, 101).GuestId, Is.Null.Or.Empty);
            Assert.That(fixture.Hotel.Keys.Find(101).GuestId, Is.Null.Or.Empty);
            Assert.That(Room(fixture, 101).Cleanliness, Is.EqualTo(Cleanliness.Dirty));
        }

        [Test]
        public void PriceAndCancellationAreEditableOnlyBeforeArrival()
        {
            var fixture = Create();
            var offer = Offer(fixture);
            Book(fixture, offer);
            var reservation = Reservation(fixture, offer.Id);
            int revision = reservation.Revision;
            Require(fixture.Hotel.SetBookingPrice(1, reservation.Id, 200));
            Assert.That(reservation.Price, Is.EqualTo(200));
            Assert.That(reservation.Revision, Is.GreaterThan(revision));
            AdvanceTo(fixture, offer.ArrivalAt + .25f);
            var guest = fixture.Hotel.Guests.Single(item => item.GuestId == offer.Id);
            Assert.That(guest.Price, Is.EqualTo(200));
            string before = State(fixture);
            Assert.That(fixture.Hotel.SetBookingPrice(0, reservation.Id, 220).Success, Is.False);
            Assert.That(fixture.Hotel.CancelBooking(0, reservation.Id).Success, Is.False);
            Assert.That(State(fixture), Is.EqualTo(before));
        }

        [Test]
        public void CancelledFutureStayNeverMaterializesAndReleasesItsRoomInterval()
        {
            var fixture = Create();
            var offer = Offer(fixture);
            var replacement = fixture.Hotel.BookingOffers.First(item => item.ArrivalDay == 1 && item.Id != offer.Id);
            Book(fixture, offer);
            Require(fixture.Hotel.CancelBooking(1, offer.Id));
            Assert.That(Reservation(fixture, offer.Id).Status, Is.EqualTo(ReservationStatus.Cancelled));
            Require(fixture.Hotel.CanReserveRoom(101, replacement));
            Book(fixture, replacement);
            AdvanceTo(fixture, Math.Max(offer.ArrivalAt, replacement.ArrivalAt) + .25f);
            Assert.That(fixture.Hotel.Guests.Any(guest => guest.GuestId == offer.Id), Is.False);
            Assert.That(fixture.Hotel.Guests.Count(guest => guest.GuestId == replacement.Id), Is.EqualTo(1));
        }

        [Test]
        public void AStaleBookingEditCannotOverwriteAnotherStaffDecision()
        {
            var fixture = Create();
            var offer = Offer(fixture);
            Book(fixture, offer);
            var reservation = Reservation(fixture, offer.Id);
            int staleRevision = reservation.Revision;
            Require(fixture.Hotel.SetBookingPrice(1, reservation.Id, 200, staleRevision));
            string before = State(fixture);
            Assert.That(fixture.Hotel.SetBookingPrice(0, reservation.Id, 220, staleRevision).Success, Is.False);
            Assert.That(fixture.Hotel.CancelBooking(0, reservation.Id, staleRevision).Success, Is.False);
            Assert.That(State(fixture), Is.EqualTo(before));
            Require(fixture.Hotel.CancelBooking(0, reservation.Id, reservation.Revision));
            Assert.That(reservation.Status, Is.EqualTo(ReservationStatus.Cancelled));
        }

        [Test]
        public void RollingApplicationsContinueAfterDayThreeWithoutPriceEscalationOrForcedFailure()
        {
            var fixture = Create();
            AdvanceTo(fixture, fixture.Hotel.Calendar.At(4, 8));
            Assert.That(fixture.Hotel.CalendarDay, Is.EqualTo(4));
            Assert.That(fixture.Hotel.BookingOffers.Count(offer => offer.ArrivalDay == 4), Is.EqualTo(8));
            Assert.That(fixture.Hotel.BookingOffers.Count(offer => offer.ArrivalDay == 5), Is.EqualTo(8));
            Assert.That(fixture.Hotel.BookingOffers.Count, Is.EqualTo(16));
            Assert.That(fixture.Hotel.BookingOffers.Where(offer => offer.Application.Archetype.Kind == GuestKind.Business)
                .All(offer => offer.Application.ReferencePrice == 450), Is.True);
            Assert.That(fixture.Hotel.Boiler.Failed, Is.False, "The isolated healthy fixture has no demand/wear cause; calendar day is not a failure trigger.");
            Assert.That(fixture.Hotel.Running && !fixture.Hotel.IsServiceComplete, Is.True);
            Book(fixture, Offer(fixture, 4));
            Assert.That(fixture.Hotel.Reservations.Any(item => item.Offer.ArrivalDay == 4 && item.Status == ReservationStatus.Reserved), Is.True);
        }

        [Test]
        public void CheckoutPostsIncomeOnceAndFollowingReportDisplaysItWithoutPayingAgain()
        {
            var fixture = Create();
            var offer = Offer(fixture);
            Book(fixture, offer);
            var guest = CheckIn(fixture, offer);
            AdvanceTo(fixture, 660);
            Assert.That(fixture.Hotel.DayReports.Single().Receipts, Is.Empty);
            Assert.That(fixture.Hotel.Economy.Cash, Is.EqualTo(-100));
            AdvanceTo(fixture, offer.CheckoutAt - .25f);
            int beforeCheckout = fixture.Hotel.Economy.Cash;
            AdvanceTo(fixture, offer.CheckoutAt + .25f);
            Assert.That(guest.ReceiptPosted, Is.True);
            int afterCheckout = fixture.Hotel.Economy.Cash;
            Assert.That(afterCheckout - beforeCheckout, Is.EqualTo(guest.Price), "This warm, normally priced single-guest fixture has no refund cause.");
            AdvanceTo(fixture, offer.CheckoutAt + 4);
            Assert.That(fixture.Hotel.Economy.Cash, Is.EqualTo(afterCheckout));
            if (guest.Agent.State == GuestAgentState.Leaving)
                Require(fixture.Hotel.SignalGuestLeft(guest.GuestId));
            Assert.That(fixture.Hotel.SignalGuestLeft(guest.GuestId).Success, Is.False);
            Assert.That(fixture.Hotel.Economy.Cash, Is.EqualTo(afterCheckout), "Repeated exit callbacks cannot bill the stay again.");
            AdvanceTo(fixture, 1380);
            var report = fixture.Hotel.LastReport;
            var receipt = report.Receipts.Single(item => item.GuestId == guest.GuestId);
            Assert.That(receipt.Net, Is.EqualTo(afterCheckout - beforeCheckout));
            Assert.That(report.Gross, Is.EqualTo(guest.Price));
            Assert.That(report.OpeningCash, Is.EqualTo(-100));
            Assert.That(fixture.Hotel.Economy.Cash, Is.EqualTo(afterCheckout - 450));
            AdvanceTo(fixture, 2100);
            Assert.That(fixture.Hotel.LastReport.Receipts.Any(item => item.GuestId == guest.GuestId), Is.False);
            Assert.That(fixture.Hotel.DayReports.SelectMany(item => item.Receipts).Count(item => item.GuestId == guest.GuestId), Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AGuestWithoutActualRoomTimeIsNeverCharged(bool handOverKeyWithoutRoomArrival)
        {
            var fixture = Create();
            var offer = Offer(fixture);
            Book(fixture, offer);
            var guest = ReachReception(fixture, offer);
            if (handOverKeyWithoutRoomArrival)
                Require(ModelKeyHandoff.CheckIn(fixture.Hotel, 0, guest.GuestId));
            Assert.That(guest.Agent.HasReachedRoom, Is.False);
            AdvanceTo(fixture, offer.CheckoutAt + .25f);
            Assert.That(guest.ReceiptPosted, Is.True);
            Assert.That(fixture.Hotel.Economy.Cash, Is.EqualTo(-100));
            AdvanceTo(fixture, 1380);
            var receipt = fixture.Hotel.LastReport.Receipts.Single(item => item.GuestId == guest.GuestId);
            Assert.That(receipt.Price, Is.Zero);
            Assert.That(receipt.Net, Is.Zero);
            Assert.That(fixture.Hotel.Economy.Cash, Is.EqualTo(350 - 2 * 450));
        }

        [Test]
        public void InvalidBookingCommandsAreAtomicAndDoNotEmitSuccessEvents()
        {
            var fixture = Create();
            var offer = Offer(fixture);
            string before = State(fixture);
            Assert.That(fixture.Hotel.AcceptBooking(-1, offer.Id, 101, 180).Success, Is.False);
            Assert.That(fixture.Hotel.AcceptBooking(2, offer.Id, 101, 180).Success, Is.False);
            Assert.That(fixture.Hotel.AcceptBooking(0, "unknown-offer", 101, 180).Success, Is.False);
            Assert.That(fixture.Hotel.AcceptBooking(0, offer.Id, 999, 180).Success, Is.False);
            foreach (int price in new[] { -1, 119, 185, 651 })
                Assert.That(fixture.Hotel.AcceptBooking(0, offer.Id, 101, price).Success, Is.False);
            Assert.That(fixture.Hotel.CanReserveRoom(999, offer).Success, Is.False);
            Assert.That(fixture.Hotel.CanReserveRoom(101, null).Success, Is.False);
            Assert.That(fixture.Hotel.CancelBooking(0, "unknown-reservation").Success, Is.False);
            Assert.That(fixture.Hotel.SetBookingPrice(0, "unknown-reservation", 180).Success, Is.False);
            Assert.That(State(fixture), Is.EqualTo(before));
            Book(fixture, offer);
            before = State(fixture);
            Assert.That(fixture.Hotel.SetBookingPrice(2, offer.Id, 200).Success, Is.False);
            Assert.That(fixture.Hotel.SetBookingPrice(0, offer.Id, 185).Success, Is.False);
            Assert.That(fixture.Hotel.CancelBooking(-1, offer.Id).Success, Is.False);
            Assert.That(State(fixture), Is.EqualTo(before));
        }

        [Test]
        public void ReadOnlyMirrorCannotAcceptCancelOrEditBookings()
        {
            var fixture = Create();
            var offer = Offer(fixture);
            Book(fixture, offer);
            var next = Offer(fixture, 2);
            fixture.Hotel.EnableReadOnlyMirror();
            string before = State(fixture);
            Assert.That(fixture.Hotel.AcceptBooking(0, next.Id, 101, next.Application.ReferencePrice).Success, Is.False);
            Assert.That(fixture.Hotel.CancelBooking(0, offer.Id).Success, Is.False);
            Assert.That(fixture.Hotel.SetBookingPrice(0, offer.Id, 200).Success, Is.False);
            fixture.Hotel.Tick(offer.ArrivalAt + 1);
            Assert.That(State(fixture), Is.EqualTo(before));
        }
    }
}
