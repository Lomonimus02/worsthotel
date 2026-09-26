using System;

namespace WorstHotel
{
    public enum ReservationStatus { Reserved, Arrived, Completed, Cancelled }

    public sealed class ScheduledBookingOffer
    {
        public string Id => Application.Id;
        public BookingApplication Application { get; }
        public int ArrivalDay { get; }
        public float ArrivalAt { get; }
        public float SleepAt { get; }
        public float WakeAt { get; }
        public float CheckoutAt { get; }
        public ScheduledBookingOffer(BookingApplication application, int arrivalDay, float arrivalAt, float sleepAt, float wakeAt, float checkoutAt)
        {
            if (application == null || arrivalDay < 1 || !Number.IsFinite(arrivalAt) || !Number.IsFinite(sleepAt) ||
                !Number.IsFinite(wakeAt) || !Number.IsFinite(checkoutAt) || arrivalAt < 0 ||
                sleepAt <= arrivalAt || wakeAt <= sleepAt || checkoutAt <= wakeAt)
                throw new ArgumentException("A booking requires an ordered one-night schedule.");
            Application = application; ArrivalDay = arrivalDay; ArrivalAt = arrivalAt;
            SleepAt = sleepAt; WakeAt = wakeAt; CheckoutAt = checkoutAt;
        }
    }

    public sealed class HotelReservation
    {
        public string Id => Offer.Id;
        public ScheduledBookingOffer Offer { get; }
        public int RoomId { get; internal set; }
        public int Price { get; internal set; }
        public int ActorId { get; internal set; }
        public int Revision { get; internal set; } = 1;
        public ReservationStatus Status { get; internal set; }
        public bool Active => Status == ReservationStatus.Reserved || Status == ReservationStatus.Arrived;
        internal HotelReservation(ScheduledBookingOffer offer, int roomId, int price, int actorId)
        { Offer = offer; RoomId = roomId; Price = price; ActorId = actorId; }
    }
}
