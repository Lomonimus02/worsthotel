using System;
using System.Collections.Generic;
using System.Linq;

namespace WorstHotel
{
    public sealed partial class HotelSimulation
    {
        readonly List<ScheduledBookingOffer> bookingOffers = new List<ScheduledBookingOffer>();
        readonly List<HotelReservation> reservations = new List<HotelReservation>();
        public IReadOnlyList<ScheduledBookingOffer> BookingOffers => bookingOffers.AsReadOnly();
        public IReadOnlyList<HotelReservation> Reservations => reservations.AsReadOnly();
        public HotelReservation FindReservation(string id) => reservations.FirstOrDefault(item => item.Id == id);
        int offersThroughDay;
        int operatingServiceDay;

        GuestDailyTiming BookingTiming(BookingApplication application, int arrivalDay, float arrivalAt)
        {
            if (Schedules != null) return Schedules.DatedTimingFor(application, arrivalDay, arrivalAt, Calendar);
            float sleep = Calendar.At(arrivalDay, Operations.SleepHour);
            float checkout = Calendar.At(arrivalDay + 1, Operations.CheckoutHour);
            return new GuestDailyTiming(sleep, Math.Max(sleep + (checkout - sleep) * .5f,
                checkout - Operations.SecondsPerDay / 12), -1);
        }

        internal void SyncReservationRoom(GuestStay guest)
        {
            if (!ContinuousOperations || guest == null) return;
            var reservation = FindReservation(guest.GuestId);
            if (reservation != null && reservation.RoomId != guest.RoomId)
            { reservation.RoomId = guest.RoomId; reservation.Revision++; }
        }

        CommandResult DebugSpawnContinuousGuest(GuestKind kind, int roomId)
        {
            var profile = settings.GuestArchetypes.FirstOrDefault(item => item.Kind == kind);
            if (profile == null) return CommandResult.Fail("Unknown guest archetype.");
            float arrival = Elapsed + 1;
            int day = Calendar.DayAt(arrival);
            float sleep = Calendar.At(day, Operations.SleepHour);
            if (arrival >= sleep) return CommandResult.Fail("This walk-in cannot complete an evening arrival now. Advance to the next morning.");
            int priceStep = settings.Economy.PriceStep;
            int gridMax = settings.Economy.MinPrice + (settings.Economy.MaxPrice - settings.Economy.MinPrice) / priceStep * priceStep;
            int price = Math.Max(settings.Economy.MinPrice, Math.Min(gridMax,
                settings.Economy.MinPrice + (int)Math.Round((profile.ReferencePrice - settings.Economy.MinPrice) / (double)priceStep) * priceStep));
            var application = new BookingApplication("stay-" + day + "-debug" + (++debugGuestCounter), profile.Label + " walk-in", profile, profile.ReferencePrice);
            var timing = BookingTiming(application, day, arrival);
            var offer = new ScheduledBookingOffer(application, day, arrival, timing.SleepAt,
                timing.WakeAt, Calendar.At(day + 1, Operations.CheckoutHour));
            bookingOffers.Add(offer);
            var result = CommitBooking(offer, roomId, price, 0, false);
            bookingOffers.Remove(offer);
            return result;
        }

        CommandResult BookingCommandGate(int actorId)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            if (!ContinuousOperations || !Running) return CommandResult.Fail("Open continuous operations before making a booking.");
            if (actorId < 0 || actorId > 1) return CommandResult.Fail("Unknown staff actor.");
            return CommandResult.Ok();
        }

        bool ValidBookingPrice(int price) => price >= settings.Economy.MinPrice && price <= settings.Economy.MaxPrice &&
            (price - settings.Economy.MinPrice) % settings.Economy.PriceStep == 0;

        public CommandResult CanReserveRoom(int roomId, ScheduledBookingOffer offer)
        {
            if (offer == null || !rooms.TryGetValue(roomId, out var room)) return CommandResult.Fail("Choose an available offer and a real room.");
            if (offer.ArrivalAt <= Elapsed) return CommandResult.Fail("This arrival window has already started.");
            var available = CanReserveInterval(roomId, offer.ArrivalAt, offer.CheckoutAt);
            if (!available.Success) return available;
            return CommandResult.Ok(room.Cleanliness == Cleanliness.Dirty ? "Room needs fresh linen before the new arrival." :
                room.DepartingGuestId != null ? "The previous guest must leave before preparation and check-in." : "Room is available for this night.");
        }

        public CommandResult CanReserveInterval(int roomId, float arrivalAt, float checkoutAt, string exceptGuestId = null)
        {
            if (!IsRoomOperational(roomId)) return CommandResult.Fail("This room is in the closed North Wing. Restore it in the renovation ledger.");
            if (!rooms.TryGetValue(roomId, out var room) || !Number.IsFinite(arrivalAt) || !Number.IsFinite(checkoutAt) ||
                arrivalAt < 0 || checkoutAt <= arrivalAt) return CommandResult.Fail("Choose a valid room and stay interval.");
            foreach (var reservation in reservations.Where(item => item.Active && item.Id != exceptGuestId))
            {
                var stay = guests.FirstOrDefault(item => item.GuestId == reservation.Id);
                int actualRoom = stay?.Agent?.CheckedIn == true ? stay.RoomId : reservation.RoomId;
                if (actualRoom != roomId && stay?.Agent?.PendingMoveRoomId != roomId) continue;
                float checkout = stay?.Agent?.CheckoutTime ?? reservation.Offer.CheckoutAt;
                if (arrivalAt < checkout && checkoutAt > reservation.Offer.ArrivalAt)
                    return CommandResult.Fail("This room already has an overlapping stay.");
            }
            if (room.Occupied && room.GuestId != exceptGuestId)
            {
                var occupant = guests.FirstOrDefault(item => item.GuestId == room.GuestId);
                if (occupant?.Agent == null || occupant.Agent.CheckoutTime > arrivalAt)
                    return CommandResult.Fail("The current guest is still expected to occupy this room at arrival.");
            }
            return CommandResult.Ok();
        }

        public CommandResult AcceptBooking(int actorId, string offerId, int roomId, int price)
        {
            var gate = BookingCommandGate(actorId); if (!gate.Success) return gate;
            if (AutomaticBookingsEnabled)
                return CommandResult.Fail("Ordinary reservations arrive automatically. Manage room sales and rates instead.");
            var offer = bookingOffers.FirstOrDefault(item => item.Id == offerId);
            return CommitBooking(offer, roomId, price, actorId, false);
        }

        CommandResult CommitBooking(ScheduledBookingOffer offer, int roomId, int price, int actorId, bool automatic)
        {
            if (offer == null) return CommandResult.Fail("This booking enquiry is no longer available.");
            if (FindReservation(offer.Id) != null) return CommandResult.Fail("This enquiry has already been decided.");
            if (!ValidBookingPrice(price)) return CommandResult.Fail("Choose a price on the hotel's allowed price grid.");
            var availability = CanReserveRoom(roomId, offer); if (!availability.Success) return availability;
            PruneCompletedOperatingHistory();
            // Protected bodies/items are never erased just to make a booking fit on the wire.
            if (reservations.Count >= 128)
                return CommandResult.Fail("The guest ledger is full. Finish departures and store unclaimed luggage before accepting more stays.");
            reservations.Add(new HotelReservation(offer, roomId, price, actorId, automatic));
            SignalEvent((automatic ? "New reservation: " : "Booking accepted: ") + offer.Application.GuestName +
                ", room " + roomId + ", day " + offer.ArrivalDay);
            return CommandResult.Ok("One-night booking accepted. " + availability.Message);
        }

        public CommandResult CancelBooking(int actorId, string reservationId, int expectedRevision = -1)
        {
            var gate = EditableBooking(actorId, reservationId, expectedRevision, out var reservation);
            if (!gate.Success) return gate;
            if (reservation.Revision == int.MaxValue) return CommandResult.Fail("The booking revision is exhausted.");
            reservation.Status = ReservationStatus.Cancelled; reservation.Revision++;
            SignalEvent("Booking cancelled: " + reservation.Offer.Application.GuestName);
            return CommandResult.Ok("Future booking cancelled.");
        }

        public CommandResult SetBookingPrice(int actorId, string reservationId, int price, int expectedRevision = -1)
        {
            var gate = EditableBooking(actorId, reservationId, expectedRevision, out var reservation);
            if (!gate.Success) return gate;
            if (AutomaticBookingsEnabled)
                return CommandResult.Fail("This guest's agreed price is fixed. Change room sale rates for future reservations.");
            if (!ValidBookingPrice(price)) return CommandResult.Fail("Choose a price on the hotel's allowed price grid.");
            if (reservation.Price == price) return CommandResult.Ok("The agreed price is unchanged.");
            if (reservation.Revision >= int.MaxValue - 2)
                return CommandResult.Fail("The booking must retain revisions for arrival and checkout.");
            reservation.Price = price; reservation.ActorId = actorId; reservation.Revision++;
            SignalEvent("Future booking price updated");
            return CommandResult.Ok("Agreed price updated before arrival.");
        }

        public CommandResult ReassignBooking(int actorId, string reservationId, int roomId, int expectedRevision = -1)
        {
            var gate = EditableBooking(actorId, reservationId, expectedRevision, out var reservation);
            if (!gate.Success) return gate;
            var available = CanReserveInterval(roomId, reservation.Offer.ArrivalAt, reservation.Offer.CheckoutAt, reservation.Id);
            if (!available.Success) return available;
            if (reservation.RoomId == roomId) return CommandResult.Ok("This reservation is already assigned to that room.");
            if (reservation.Revision >= int.MaxValue - 2)
                return CommandResult.Fail("The booking must retain revisions for arrival and checkout.");
            // A closed sales room may receive an existing contract. Price and dated identity stay fixed.
            reservation.RoomId = roomId; reservation.ActorId = actorId; reservation.Revision++;
            SignalEvent("Booking room changed: " + reservation.Offer.Application.GuestName + ", room " + roomId);
            return CommandResult.Ok("Room assignment updated. The guest keeps the agreed price and dates.");
        }

        CommandResult EditableBooking(int actorId, string id, int revision, out HotelReservation reservation)
        {
            reservation = null;
            var gate = BookingCommandGate(actorId); if (!gate.Success) return gate;
            reservation = FindReservation(id);
            if (reservation == null || reservation.Status != ReservationStatus.Reserved || reservation.Offer.ArrivalAt <= Elapsed)
                return CommandResult.Fail("Only a future booking can be changed or cancelled.");
            if (revision >= 0 && revision != reservation.Revision) return CommandResult.Fail("This booking changed; refresh it before editing.");
            return CommandResult.Ok();
        }

        public float LatestCheckoutForRoom(int roomId, string guestId)
        {
            if (!ContinuousOperations) return float.PositiveInfinity;
            float nextArrival = reservations.Where(item => item.Active && item.RoomId == roomId && item.Id != guestId && item.Offer.ArrivalAt > Elapsed)
                .Select(item => item.Offer.ArrivalAt - 15f).DefaultIfEmpty(float.PositiveInfinity).Min();
            var current = FindReservation(guestId);
            float singleNightEnd = current == null ? float.PositiveInfinity : Calendar.At(current.Offer.ArrivalDay + 2, 0) - 1;
            return Math.Min(nextArrival, singleNightEnd);
        }

        void RefreshBookingSchedule(float now)
        {
            int day = Calendar.DayAt(now);
            if (operatingServiceDay != day)
            {
                PruneCompletedOperatingHistory();
                Services?.BeginOperatingDay(day, Calendar.At(day + 2, 0));
                // Only used/consumed linen slots are replenished by the existing system.
                Housekeeping?.RefillForDay(day);
                operatingServiceDay = day;
            }
            for (int offerDay = Math.Max(day, offersThroughDay + 1); offerDay <= day + 1; offerDay++)
            {
                var offers = GuestSystem.GenerateContinuousApplications(offerDay, settings.GuestArchetypes);
                for (int index = 0; index < offers.Length; index++)
                {
                    float arrivalHour = Operations.ArrivalStartHour + (Operations.ArrivalEndHour - Operations.ArrivalStartHour) * index / Math.Max(1, offers.Length - 1);
                    float arrival = Calendar.At(offerDay, arrivalHour);
                    if (arrival <= now) continue;
                    var timing = BookingTiming(offers[index], offerDay, arrival);
                    bookingOffers.Add(new ScheduledBookingOffer(offers[index], offerDay, arrival,
                        timing.SleepAt, timing.WakeAt,
                        Calendar.At(offerDay + 1, Operations.CheckoutHour)));
                }
                offersThroughDay = offerDay;
            }
            bookingOffers.RemoveAll(item => item.ArrivalDay < day || item.ArrivalAt < now && FindReservation(item.Id) == null);
            foreach (var reservation in reservations.Where(item => item.Status == ReservationStatus.Reserved && item.Offer.ArrivalAt <= now).ToArray())
            {
                var stay = new GuestStay(reservation.Offer.Application, reservation.RoomId, reservation.Price);
                guests.Add(stay);
                if (LivingEnabled) Schedules.AttachStay(stay, reservation.Offer.ArrivalDay, reservation.Offer.ArrivalAt,
                    reservation.Offer.SleepAt, reservation.Offer.CheckoutAt, reservation.Offer.WakeAt,
                    BookingTiming(reservation.Offer.Application, reservation.Offer.ArrivalDay, reservation.Offer.ArrivalAt).OutingReturnAt);
                else rooms[stay.RoomId].GuestId = stay.GuestId;
                reservation.Status = ReservationStatus.Arrived; reservation.Revision++;
                SignalEvent(stay.Name + " is arriving for room " + stay.RoomId);
            }
            foreach (var reservation in reservations.Where(item => item.Status == ReservationStatus.Arrived))
            {
                var stay = guests.FirstOrDefault(item => item.GuestId == reservation.Id);
                if (stay?.Agent?.CheckedIn == true && reservation.RoomId != stay.RoomId)
                { reservation.RoomId = stay.RoomId; reservation.Revision++; }
                var room = rooms[reservation.RoomId];
                if (LivingEnabled && stay?.Agent != null && !stay.Agent.CheckedIn && !stay.ReceiptPosted &&
                    now < stay.Agent.CheckoutTime && !room.Occupied && !room.Reserved)
                    room.ReservedGuestId = stay.GuestId;
            }
            // Retain recent decisions; never discard a still-active stay to fit a calendar page.
            reservations.RemoveAll(item => !item.Active && item.Offer.ArrivalDay < day - 2 &&
                !guests.Any(guest => guest.GuestId == item.Id));
        }

        void PostCompletedStays(float now)
        {
            foreach (var guest in guests.Where(item => !item.ReceiptPosted).ToArray())
            {
                var reservation = FindReservation(guest.GuestId);
                if (reservation == null) continue;
                bool ended = LivingEnabled ? guest.Agent.State == GuestAgentState.CheckingOut || guest.Agent.State == GuestAgentState.Leaving ||
                    guest.Agent.State == GuestAgentState.Left : now >= reservation.Offer.CheckoutAt;
                if (!ended) continue;
                var receipt = LivingEnabled && (!guest.Agent.HasReachedRoom || guest.Elapsed <= 0) ?
                    new GuestReceipt(guest.GuestId, guest.Name, guest.RoomId, 0,
                        guest.Agent.WaitingSeconds > 0 || guest.Agent.CheckedIn ? 0 : 75, 0,
                        "No room time was received. No stay was charged.") : Economy.CalculateReceipt(guest, Satisfaction.Evaluate(guest));
                Economy.PostCheckout(receipt);
                periodReceipts.Add(receipt);
                guest.ReceiptPosted = true;
                reservation.Status = ReservationStatus.Completed; reservation.Revision++;
                if (!LivingEnabled) ReleaseRoom(guest);
                SignalEvent(guest.Name + (receipt.EarlyCheckout ? " checked out early" : " checked out") +
                    " — $" + receipt.Net + " received" + (receipt.EarlyCheckout ? "; " + receipt.DepartureReason : ""));
            }
        }
    }
}
