using System.Linq;
using NUnit.Framework;

namespace WorstHotel.Tests
{
    public sealed partial class Phase1PlayModeTests
    {
        // Diagnostic arrangement for tests of electricity, needs and routes. This runs the real
        // model key transaction but supplies no evidence of a player physically fetching/giving it.
        // RoomKeyPlayModeTests and the complete living-guest route exercise that physical boundary.
        private static CommandResult CheckInWithModelKeyFixture(GameSession session, int actorId, string guestId)
        {
            var simulation = session.Simulation;
            var guest = simulation.Guests.Single(item => item.GuestId == guestId);
            var pickup = simulation.Keys.PickUp(actorId, guest.RoomId);
            Assert.That(pickup.Success, Is.True, pickup.Message);
            var result = simulation.CheckIn(actorId, guestId);
            session.RaiseChanged();
            return result;
        }

        private static CommandResult MoveWithModelKeyFixture(GameSession session, int actorId, string guestId, int targetRoomId)
        {
            var pending = session.MoveGuest(actorId, guestId, targetRoomId);
            Assert.That(pending.Success, Is.True, pending.Message);
            var pickup = session.Simulation.Keys.PickUp(actorId, targetRoomId);
            Assert.That(pickup.Success, Is.True, pickup.Message);
            var result = session.Simulation.MoveGuest(actorId, guestId, targetRoomId);
            session.RaiseChanged();
            return result;
        }
    }
}
