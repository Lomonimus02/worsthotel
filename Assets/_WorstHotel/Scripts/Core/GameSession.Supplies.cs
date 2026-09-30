using UnityEngine;

namespace WorstHotel
{
    public sealed partial class GameSession
    {
        CommandResult CanAuthorizeSupplyOrder(int actorId)
        {
            var active = CanManageFutureBookings(actorId);
            if (!active.Success) return active;
            var book = DiegeticBookInteraction.Find(HotelBook.Supplies);
            var staff = LocalCoopBootstrap.Instance.Players[actorId];
            var ui = ManagementUI.Instance;
            bool reading = ui && ui.Owner == actorId && ui.ReadingBook == HotelBook.Supplies;
            if (actorId == 1 && LanSession.Instance && LanSession.Instance.IsRemoteReadingBook(HotelBook.Supplies)) reading = true;
            if (!reading || !book || Vector3.Distance(staff.transform.position, book.transform.position) > 3.5f)
                return CommandResult.Fail("Order at the supply ledger in the linen room.");
            return CommandResult.Ok();
        }

        public CommandResult OrderLaundry(int actorId, int expectedRevision)
        {
            var allowed = CanAuthorizeSupplyOrder(actorId);
            if (!allowed.Success) return GuestCommand(allowed);
            if (ForwardLan(LanCommandKind.OrderLaundry, supplyRevision: expectedRevision)) return CommandResult.Ok(LastMessage);
            var result = Simulation.OrderLaundry(actorId, expectedRevision);
            Cash = Simulation.Economy.Cash;
            return GuestCommand(result);
        }

        public CommandResult OrderBulbs(int actorId, int expectedRevision)
        {
            var allowed = CanAuthorizeSupplyOrder(actorId);
            if (!allowed.Success) return GuestCommand(allowed);
            if (ForwardLan(LanCommandKind.OrderBulbs, supplyRevision: expectedRevision)) return CommandResult.Ok(LastMessage);
            var result = Simulation.OrderBulbs(actorId, expectedRevision);
            Cash = Simulation.Economy.Cash;
            return GuestCommand(result);
        }
    }
}
