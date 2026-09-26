using System.Linq;
using NUnit.Framework;

namespace WorstHotel.Tests
{
    /// <summary>Explicit headless world-boundary adapter: actual guest, room key and staged television, never an ambient override.</summary>
    internal static class ModelNoiseSource
    {
        internal static GuestStay AddTelevision(HotelSimulation simulation, int roomId)
        {
            Assert.That(simulation.DebugSpawnGuest(GuestKind.Budget, roomId).Success, Is.True);
            var source = simulation.Guests.Last();
            Assert.That(simulation.SignalGuestReachedReception(source.GuestId).Success, Is.True);
            Assert.That(ModelKeyHandoff.CheckIn(simulation, 0, source.GuestId).Success, Is.True);
            Assert.That(simulation.SignalGuestReachedRoom(source.GuestId).Success, Is.True);
            Assert.That(simulation.ForceActivity(source.GuestId, GuestActivity.LoudRoom).Success, Is.True);
            return source;
        }
    }
}
