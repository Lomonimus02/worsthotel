using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace WorstHotel.Tests
{
    public sealed class AutomaticBookingTests
    {
        const string AssetPath = "Assets/_WorstHotel/ScriptableObjects/PrototypeSession.asset";
        static HotelSimulation Create(SalesSettings sales = null)
        {
            var config = AssetDatabase.LoadAssetAtPath<SessionConfig>(AssetPath);
            Assert.That(config, Is.Not.Null);
            var settings = config.ToData();
            var calendar = config.OperationsData();
            var operations = new OperationsSettings(calendar.SecondsPerDay, calendar.StartHour, calendar.ReportHour,
                calendar.ArrivalStartHour, calendar.ArrivalEndHour, calendar.SleepHour, calendar.CheckoutHour,
                calendar.ReportHistoryLimit, sales ?? calendar.Sales);
            var model = new HotelSimulation(settings, settings.Rooms.Select(room => new RoomState(room)).ToArray(),
                config.living.ToData(), config.needs.ToData(), config.noise.ToData(), config.heater.ToData(),
                config.electricity.ToData(), config.housekeeping.ToData(), config.services.ToData(),
                config.infrastructure.ToData(), operations);
            Require(model.StartOperations());
            return model;
        }
        static void Require(CommandResult result) => Assert.That(result.Success, Is.True, result.Message);
        static void Advance(HotelSimulation model, float target, float step = 1)
        {
            while (model.Elapsed < target) model.Tick(Math.Min(step, target - model.Elapsed));
        }
        static string SalesState(HotelSimulation model) => model.Elapsed + ";" + model.EventRevision + ";" + model.Economy.Cash + ";" +
            string.Join("|", model.RoomSalesPolicies.Select(row => row.RoomId + ":" + row.OpenForSale + ":" + row.Price + ":" + row.Revision)) + ";" +
            string.Join("|", model.SalesDecisionCursors.Select(row => row.ArrivalDay + ":" + row.NextOfferIndex)) + ";" + Bookings(model);
        static string Bookings(HotelSimulation model) => string.Join("|", model.Reservations.OrderBy(row => row.Id).Select(row =>
            row.Id + ":" + row.RoomId + ":" + row.Price + ":" + row.Status + ":" + row.Revision + ":" + row.IsAutomatic + ":" + row.ActorId));
        static void Set(HotelSimulation model, int roomId, bool open, int price)
        {
            var row = model.RoomSalesPolicies.Single(item => item.RoomId == roomId);
            Require(model.SetRoomSalesPolicy(0, roomId, open, price, row.Revision));
        }

        [Test]
        public void ProductionEnablesTimedSalesButRawHistoricalCalendarRemainsManual()
        {
            var asset = AssetDatabase.LoadAssetAtPath<SessionConfig>(AssetPath);
            Assert.That(asset.automaticBookings && asset.OperationsData().Sales.Enabled, Is.True);
            Assert.That(new OperationsSettings().Sales.Enabled, Is.False);
            var model = Create();
            Assert.That(model.AutomaticBookingsEnabled, Is.True);
            Assert.That(model.RoomSalesPolicies.Count, Is.EqualTo(6));
            Assert.That(model.RoomSalesPolicies.Count(row => row.OpenForSale), Is.EqualTo(4));
            Assert.That(model.RoomSalesPolicies.All(row => row.Price == 180 && row.Revision == 1), Is.True);
            Assert.That(model.Reservations, Is.Empty);
            Assert.That(model.Guests, Is.Empty);
            Assert.That(model.SalesDecisionCursors.Count, Is.EqualTo(2));
            Assert.That(model.SalesDecisionAt(1, 0), Is.EqualTo(model.Calendar.At(1, 8.5f)));
            Assert.That(model.SalesDecisionAt(2, 0), Is.EqualTo(model.Calendar.At(1, 16)));
        }

        [Test]
        public void OpeningOrRepricingRoomDoesNotInstantlyFillItOrRerollConsumedEnquiries()
        {
            var model = Create(new SalesSettings(enabled: true, initiallyOpenRooms: 0, baseDemand: 1));
            int cash = model.Economy.Cash;
            Set(model, 101, true, 180);
            Assert.That(model.Reservations, Is.Empty);
            Advance(model, model.SalesDecisionAt(1, 0) - .1f);
            Assert.That(model.Reservations, Is.Empty);
            Advance(model, model.SalesDecisionAt(1, 0));
            var reservation = model.Reservations.Single();
            Assert.That(reservation.IsAutomatic && reservation.ActorId == -1, Is.True);
            Assert.That(reservation.Price, Is.EqualTo(180));
            Assert.That(model.Guests, Is.Empty);
            Assert.That(model.Economy.Cash, Is.EqualTo(cash), "A reservation is unpaid until a real stay checks out.");
            Require(model.CancelBooking(0, reservation.Id, reservation.Revision));
            Set(model, 101, false, 360); Set(model, 101, true, 180);
            model.Tick(.1f);
            Assert.That(model.Reservations.Count, Is.EqualTo(1));
            Assert.That(model.Reservations.Single().Status, Is.EqualTo(ReservationStatus.Cancelled));
            Assert.That(model.SalesDecisionCursors.Single(row => row.ArrivalDay == 1).NextOfferIndex, Is.EqualTo(1));
            Advance(model, model.SalesDecisionAt(1, 1));
            Assert.That(model.Reservations.Count(row => row.Active), Is.EqualTo(1));
            Assert.That(model.Reservations.Single(row => row.Active).Id, Is.Not.EqualTo(reservation.Id));
        }

        [Test]
        public void AllClosedConsumesEightDecisionsWithoutCatchUpBurstWhenRoomsReopen()
        {
            var model = Create(new SalesSettings(enabled: true, initiallyOpenRooms: 0, baseDemand: 1));
            Advance(model, model.Calendar.At(1, 12.1f));
            Assert.That(model.SalesDecisionCursors.Single(row => row.ArrivalDay == 1).NextOfferIndex, Is.EqualTo(8));
            foreach (var room in model.RoomSalesPolicies) Set(model, room.RoomId, true, 180);
            model.Tick(1);
            Assert.That(model.Reservations, Is.Empty, "Passed demand is not stored as an instant reopening queue.");
            Advance(model, model.SalesDecisionAt(2, 0));
            Assert.That(model.Reservations.Single().Offer.ArrivalDay, Is.EqualTo(2));
        }

        [Test]
        public void CoarseAndFineTicksProduceIdenticalDatedReservationsAndBoundedCursors()
        {
            var fine = Create(); var coarse = Create();
            float until = fine.Calendar.At(2, 6.2f);
            Advance(fine, until, .25f); Advance(coarse, until, 137);
            Assert.That(Bookings(coarse), Is.EqualTo(Bookings(fine)));
            Assert.That(coarse.SalesDecisionCursors.Select(row => row.ArrivalDay + ":" + row.NextOfferIndex),
                Is.EqualTo(fine.SalesDecisionCursors.Select(row => row.ArrivalDay + ":" + row.NextOfferIndex)));
            Assert.That(coarse.SalesDecisionCursors.Count, Is.EqualTo(2));
            Assert.That(coarse.SalesDecisionCursors.All(row => row.NextOfferIndex >= 0 && row.NextOfferIndex <= 8), Is.True);
            Assert.That(coarse.ReportSequence, Is.EqualTo(1));
            Assert.That(coarse.Reservations.Count(row => row.Offer.ArrivalDay == 2), Is.GreaterThan(0));
        }

        [Test]
        public void SameProductionSeedHigherPriceReducesBookingsWithoutChangingEnquiriesOrCreditingRevenue()
        {
            var normal = Create(new SalesSettings(enabled: true, initiallyOpenRooms: 6, initialPrice: 180));
            var premium = Create(new SalesSettings(enabled: true, initiallyOpenRooms: 6, initialPrice: 360));
            Assert.That(normal.BookingOffers.Select(row => row.Id), Is.EqualTo(premium.BookingOffers.Select(row => row.Id)));
            Advance(normal, normal.Calendar.At(1, 19.6f)); Advance(premium, premium.Calendar.At(1, 19.6f));
            TestContext.Out.WriteLine("Same production seed, six saleable rooms, two arrival dates / sixteen timed enquiries: $180=" + normal.Reservations.Count + "; $360=" + premium.Reservations.Count);
            Assert.That(normal.Reservations.Count, Is.GreaterThan(premium.Reservations.Count));
            Assert.That(premium.Reservations.Count, Is.GreaterThan(0), "Expensive rooms reduce demand rather than disable the hotel.");
            Assert.That(normal.Reservations.All(row => row.Price == 180) && premium.Reservations.All(row => row.Price == 360), Is.True);
            Assert.That(normal.Guests.All(guest => !guest.Agent.CheckedIn) && premium.Guests.All(guest => !guest.Agent.CheckedIn), Is.True);
            Assert.That(premium.Economy.Cash, Is.EqualTo(normal.Economy.Cash));
            Assert.That(normal.PeriodCheckoutIncome + premium.PeriodCheckoutIncome, Is.Zero);
        }

        [Test]
        public void ClosingAndChangingAdvertisedRatePreservesExistingGuestContractAndAssignmentCanUseClosedRoom()
        {
            var model = Create(new SalesSettings(enabled: true, initiallyOpenRooms: 2, baseDemand: 1));
            Advance(model, model.SalesDecisionAt(1, 1));
            var first = model.Reservations.OrderBy(row => row.Offer.ArrivalAt).First();
            var other = model.Reservations.Single(row => row.Id != first.Id);
            int price = first.Price; var offer = first.Offer; int revision = first.Revision;
            Set(model, first.RoomId, false, 360);
            Assert.That(first.Price, Is.EqualTo(price)); Assert.That(first.Revision, Is.EqualTo(revision));
            Assert.That(first.Status, Is.EqualTo(ReservationStatus.Reserved));
            string before = SalesState(model);
            Assert.That(model.ReassignBooking(0, first.Id, other.RoomId, revision).Success, Is.False);
            Assert.That(SalesState(model), Is.EqualTo(before), "An overlap refusal is atomic.");
            var forecast = model.ForecastBookingLoad(first.Id, 106);
            Assert.That(forecast.Available, Is.True, forecast.Reason);
            Assert.That(forecast.MaxConcurrentGuests, Is.EqualTo(2), "Moving a reservation must self-exclude its old room, not invent a third stay.");
            Assert.That(SalesState(model), Is.EqualTo(before), "Reading the alternate assignment is pure.");
            Require(model.ReassignBooking(1, first.Id, 106, revision));
            Assert.That(model.FindReservation(first.Id), Is.SameAs(first)); Assert.That(first.Offer, Is.SameAs(offer));
            Assert.That(first.RoomId, Is.EqualTo(106)); Assert.That(first.Price, Is.EqualTo(price));
            Assert.That(first.IsAutomatic && first.ActorId == 1, Is.True);
            Assert.That(first.Revision, Is.EqualTo(revision + 1));
            Assert.That(model.ForecastBookingLoad(first.Id, 106).MaxConcurrentGuests, Is.EqualTo(2));
            Assert.That(model.Keys.Find(106).Location, Is.EqualTo(RoomKeyLocation.OnRack));
            Assert.That(model.Guests, Is.Empty);
        }

        [Test]
        public void InvalidStaleAndManualApprovalCommandsDoNotMutateAutomaticSales()
        {
            var model = Create(new SalesSettings(enabled: true, initiallyOpenRooms: 1, baseDemand: 1));
            var policy = model.RoomSalesPolicies.First(); int oldRevision = policy.Revision;
            Set(model, policy.RoomId, true, 200);
            string before = SalesState(model);
            Assert.That(model.SetRoomSalesPolicy(1, policy.RoomId, false, 180, oldRevision).Success, Is.False);
            Assert.That(model.SetRoomSalesPolicy(2, policy.RoomId, false, 180, policy.Revision).Success, Is.False);
            Assert.That(model.SetRoomSalesPolicy(0, 999, true, 180).Success, Is.False);
            Assert.That(model.SetRoomSalesPolicy(0, policy.RoomId, true, 181).Success, Is.False);
            Assert.That(model.AcceptBooking(0, model.BookingOffers.First().Id, 101, 180).Success, Is.False);
            Assert.That(SalesState(model), Is.EqualTo(before));
            Require(model.SetRoomSalesPolicy(0, policy.RoomId, true, 200, policy.Revision));
            Assert.That(SalesState(model), Is.EqualTo(before), "A repeated unchanged policy is a no-op, including revision/event identity.");
            Advance(model, model.Calendar.At(1, 12.1f));
            var reservation = model.Reservations.Single(); before = SalesState(model);
            Assert.That(model.SetBookingPrice(0, reservation.Id, 360, reservation.Revision).Success, Is.False);
            Assert.That(model.ReassignBooking(0, reservation.Id, 106, reservation.Revision - 1).Success, Is.False);
            Assert.That(SalesState(model), Is.EqualTo(before));
        }

        [Test]
        public void DirtyFutureRoomCanSellButRealCheckInStillNeedsLinenAndNumberedKey()
        {
            var model = Create(new SalesSettings(enabled: true, initiallyOpenRooms: 1, baseDemand: 1));
            Require(model.DebugMarkRoomDirty(101));
            Advance(model, model.SalesDecisionAt(1, 0));
            var reservation = model.Reservations.Single();
            Advance(model, reservation.Offer.ArrivalAt + .1f);
            var guest = model.Guests.Single();
            Require(model.SignalGuestReachedReception(guest.GuestId)); // Labelled model route boundary; physical sales test uses real navigation.
            Require(model.Keys.PickUp(0, 101));
            Assert.That(model.CheckIn(0, guest.GuestId).Success, Is.False);
            Assert.That(guest.Agent.CheckedIn, Is.False);
            Require(model.PickUpLinen(1, "dirty:101")); Require(model.DepositDirtyLinen(1, "dirty:101"));
            var clean = model.Housekeeping.Linens.First(item => item.Kind == LinenKind.Clean && item.Location == LinenLocation.OnShelf);
            Require(model.PickUpLinen(1, clean.Id)); Require(model.BeginMakeBed(1, 101, clean.Id));
            Require(model.AdvanceMakeBed(1, 101, model.Housekeeping.Settings.MakeBedSeconds));
            Require(model.CheckIn(0, guest.GuestId));
            Assert.That(model.Keys.Find(101).Location, Is.EqualTo(RoomKeyLocation.HeldByGuest));
            Assert.That(model.Keys.Find(101).GuestId, Is.EqualTo(guest.GuestId));
            Assert.That(guest.RoomId, Is.EqualTo(reservation.RoomId));
            string before = SalesState(model);
            Assert.That(model.ReassignBooking(0, reservation.Id, 106, reservation.Revision).Success, Is.False);
            Assert.That(SalesState(model), Is.EqualTo(before), "After arrival use the existing physical room-move process.");
        }

        [Test]
        public void ReadOnlyMirrorCanInspectDemandButCannotAdvancePolicyOrCreateReservation()
        {
            var source = Create(); Advance(source, source.Calendar.At(1, 12.1f));
            var mirror = Create();
            mirror.EnableReadOnlyMirror();
            var packet = JsonUtility.FromJson<HotelModelSnapshot>(JsonUtility.ToJson(source.CaptureSnapshot(891, 1)));
            Require(mirror.ApplySnapshot(packet));
            Assert.That(mirror.IsReadOnlyMirror, Is.True);
            string before = SalesState(mirror);
            Assert.That(mirror.SetRoomSalesPolicy(0, 106, true, 180).Success, Is.False);
            var reservation = mirror.Reservations.First();
            Assert.That(mirror.ReassignBooking(0, reservation.Id, 106).Success, Is.False);
            Assert.That(mirror.AcceptBooking(0, mirror.BookingOffers.Last().Id, 106, 180).Success, Is.False);
            mirror.Tick(10);
            Assert.That(SalesState(mirror), Is.EqualTo(before));
            Assert.That(mirror.ForecastBookingLoad(reservation.Id, 106).Available, Is.True);
        }

        [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)] [TestCase(-.1f)] [TestCase(1.1f)]
        public void InvalidDemandProbabilityIsRejected(float value) => Assert.Throws<ArgumentException>(() => new SalesSettings(baseDemand: value));

        [Test]
        public void IllegalDecisionWindowIsRejectedBeforeHotelStarts()
        {
            Assert.Throws<ArgumentException>(() => new OperationsSettings(sales: new SalesSettings(enabled: true, firstDayDecisionStartHour: 8)));
            Assert.Throws<ArgumentException>(() => new OperationsSettings(sales: new SalesSettings(enabled: true, firstDayDecisionStartHour: 12)));
            Assert.Throws<ArgumentException>(() => new SalesSettings(decisionSpacingHours: 0));
        }
    }
}
