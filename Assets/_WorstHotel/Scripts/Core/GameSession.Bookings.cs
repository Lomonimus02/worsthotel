namespace WorstHotel
{
    public sealed partial class GameSession
    {
        public CommandResult AcceptBooking(int actorId, string offerId, int roomId, int price)
        {
            if (ForwardLan(LanCommandKind.AcceptBooking, offerId, roomId, price)) return CommandResult.Ok(LastMessage);
            return GuestCommand(Simulation.AcceptBooking(actorId, offerId, roomId, price));
        }

        public CommandResult CancelBooking(int actorId, string reservationId, int expectedRevision = -1)
        {
            if (ForwardLan(LanCommandKind.CancelBooking, reservationId, reservationRevision: expectedRevision)) return CommandResult.Ok(LastMessage);
            return GuestCommand(Simulation.CancelBooking(actorId, reservationId, expectedRevision));
        }

        public CommandResult SetBookingPrice(int actorId, string reservationId, int price, int expectedRevision = -1)
        {
            if (ForwardLan(LanCommandKind.SetBookingPrice, reservationId, amount: price, reservationRevision: expectedRevision)) return CommandResult.Ok(LastMessage);
            return GuestCommand(Simulation.SetBookingPrice(actorId, reservationId, price, expectedRevision));
        }
    }
}
