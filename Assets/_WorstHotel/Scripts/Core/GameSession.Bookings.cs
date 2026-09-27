namespace WorstHotel
{
    public sealed partial class GameSession
    {
        public CommandResult AcceptBooking(int actorId, string offerId, int roomId, int price)
        {
            if (Simulation?.AutomaticBookingsEnabled == true)
                return GuestCommand(CommandResult.Fail("Ordinary bookings arrive automatically. Manage room sales and rates instead."));
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
            if (Simulation?.AutomaticBookingsEnabled == true)
                return GuestCommand(CommandResult.Fail("This guest's agreed price is fixed. Change the room's rate for future sales."));
            if (ForwardLan(LanCommandKind.SetBookingPrice, reservationId, amount: price, reservationRevision: expectedRevision)) return CommandResult.Ok(LastMessage);
            return GuestCommand(Simulation.SetBookingPrice(actorId, reservationId, price, expectedRevision));
        }

        CommandResult CanManageFutureBookings(int actorId)
        {
            if (Simulation == null || !Simulation.ContinuousOperations || Phase != DayPhase.Service || actorId < 0 || actorId > 1)
                return CommandResult.Fail("Room sales require an active continuous hotel.");
            var staff = LocalCoopBootstrap.Instance;
            if (!staff || actorId >= staff.Players.Length || !staff.Players[actorId] ||
                !staff.Players[actorId].DeviceReady || staff.IsPaused)
                return CommandResult.Fail("An active staff member must manage the bookings.");
            return CommandResult.Ok();
        }

        public CommandResult SetRoomSalesPolicy(int actorId, int roomId, bool open, int price, int expectedPolicyRevision = -1)
        {
            var allowed = CanManageFutureBookings(actorId);
            if (!allowed.Success) return GuestCommand(allowed);
            if (!Simulation.AutomaticBookingsEnabled || expectedPolicyRevision < 1)
                return GuestCommand(CommandResult.Fail("Refresh this room's sales policy before applying changes."));
            if (ForwardLan(LanCommandKind.SetRoomSalesPolicy, room: roomId, amount: price,
                policyRevision: expectedPolicyRevision, openForSale: open)) return CommandResult.Ok(LastMessage);
            return GuestCommand(Simulation.SetRoomSalesPolicy(actorId, roomId, open, price, expectedPolicyRevision));
        }

        public CommandResult ReassignBooking(int actorId, string reservationId, int roomId, int expectedReservationRevision = -1)
        {
            var allowed = CanManageFutureBookings(actorId);
            if (!allowed.Success) return GuestCommand(allowed);
            if (expectedReservationRevision < 1)
                return GuestCommand(CommandResult.Fail("Refresh this booking before choosing another room."));
            if (ForwardLan(LanCommandKind.ReassignBooking, reservationId, roomId,
                reservationRevision: expectedReservationRevision)) return CommandResult.Ok(LastMessage);
            return GuestCommand(Simulation.ReassignBooking(actorId, reservationId, roomId, expectedReservationRevision));
        }
    }
}
