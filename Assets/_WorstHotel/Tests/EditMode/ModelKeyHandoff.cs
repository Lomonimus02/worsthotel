using System.Linq;
using NUnit.Framework;

namespace WorstHotel.Tests
{
    /// <summary>Explicit headless physical-boundary adapter for tests of other simulation systems.
    /// It executes the real key commands; it does not bypass key ownership or change room state.</summary>
    internal static class ModelKeyHandoff
    {
        internal static CommandResult CheckIn(HotelSimulation simulation, int actorId, string guestId)
        {
            var guest = simulation.Guests.Single(item => item.GuestId == guestId);
            TakeKey(simulation, actorId, guest.RoomId);
            return simulation.CheckIn(actorId, guestId);
        }

        internal static CommandResult MoveGuest(HotelSimulation simulation, int actorId, string guestId, int targetRoomId)
        {
            var request = simulation.RequestGuestMove(actorId, guestId, targetRoomId);
            Assert.That(request.Success, Is.True, request.Message);
            TakeKey(simulation, actorId, targetRoomId);
            return simulation.MoveGuest(actorId, guestId, targetRoomId);
        }

        internal static void TakeKey(HotelSimulation simulation, int actorId, int roomId)
        {
            var key = simulation.Keys.Find(roomId);
            if (key.Location == RoomKeyLocation.HeldByPlayer && key.PlayerId == actorId) return;
            var result = simulation.Keys.PickUp(actorId, roomId);
            Assert.That(result.Success, Is.True, "Headless rack pickup: " + result.Message);
        }
    }
}
