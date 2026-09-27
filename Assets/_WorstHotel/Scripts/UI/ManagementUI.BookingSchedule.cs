using System.Collections.Generic;
using System.Linq;

namespace WorstHotel
{
    public sealed partial class ManagementUI
    {
        public int DisplayedBookingRevision => IsOperationsOpen && operationsOfferId != null ? operationsBookingRevision : -1;
        public int DisplayedBookingRoom => IsOperationsOpen && operationsOfferId != null ? selectedRoom : 0;

        string ConfirmedBookingSummary()
        {
            var bookings = Session.Simulation.Reservations.Where(item => item.Offer.ArrivalDay == operationsDay &&
                item.Status != ReservationStatus.Cancelled).ToArray();
            return bookings.Length + " confirmed · agreed charges $" + bookings.Sum(item => (long)item.Price) +
                " before credits/refunds · paid at checkout";
        }

        ScheduledBookingOffer OperationsOffer(string id) => Session.Simulation.FindReservation(id)?.Offer ??
            Session.Simulation.BookingOffers.FirstOrDefault(item => item.Id == id);

        IEnumerable<ScheduledBookingOffer> OperationsBookingOffers()
        {
            var model = Session.Simulation;
            // Accepted reservations own their schedule. An expired/pruned enquiry is not
            // needed to display a still-retained contract or its immutable agreed price.
            var offers = model.AutomaticBookingsEnabled ? model.Reservations.Where(item =>
                item.Status != ReservationStatus.Cancelled).Select(item => item.Offer) : model.BookingOffers;
            return offers.Where(item => item.ArrivalDay == operationsDay).OrderBy(item => item.ArrivalAt);
        }

        bool CanPreviewBookingRoom(HotelReservation reservation, int roomId) => reservation != null &&
            reservation.Status == ReservationStatus.Reserved && reservation.Offer.ArrivalAt > Session.Simulation.Elapsed &&
            Session.Simulation.CanReserveInterval(roomId, reservation.Offer.ArrivalAt, reservation.Offer.CheckoutAt, reservation.Id).Success;

        void ApplyAcceptedBooking(string id, int roomId, int price)
        {
            var result = Session.AcceptBooking(owner, id, roomId, price);
            if (result.Success && !Session.IsLanReplica) SelectOperationsOffer(id);
        }

        void ApplyBookingRoom(string id, int roomId, int revision)
        {
            var result = Session.ReassignBooking(owner, id, roomId, revision);
            if (result.Success && !Session.IsLanReplica) SelectOperationsOffer(id);
            // A sent LAN request is not an acknowledgement. Preserve the captured draft
            // until the authoritative result arrives and the player explicitly refreshes.
        }

        void ApplyBookingPrice(string id, int price, int revision)
        {
            var result = Session.SetBookingPrice(owner, id, price, revision);
            if (result.Success && !Session.IsLanReplica) SelectOperationsOffer(id);
        }
    }
}
