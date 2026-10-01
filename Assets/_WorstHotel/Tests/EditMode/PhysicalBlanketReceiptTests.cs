using NUnit.Framework;
using UnityEngine;

namespace WorstHotel.Tests
{
    public sealed partial class ContinuousGuestServiceTests
    {
        [Test]
        public void PhysicalBlanketCollectionReplicatesAndNeedsOneActualReceipt()
        {
            var fixture = IntentFixture(); var hotel = fixture.Hotel; var guest = fixture.Business;
            CommunicateBlanket(fixture);
            Require(hotel.RegisterGuestPhysicalStaging(guest.GuestId));
            Require(hotel.SignalGuestActivityReady(guest.GuestId, guest.Agent.State, guest.Agent.Activity));
            var intent = PlacePendingBlanket(fixture);
            hotel.Tick(.25f);
            Assert.That(intent.Status, Is.EqualTo(ServiceIntentStatus.AwaitingReceipt));
            Assert.That(guest.BlanketComfortBonus, Is.Zero);
            Require(hotel.Services.BeginBlanketCollection(intent.Id, intent.Revision));
            Assert.That(intent.Collecting, Is.True);
            var mirror = IntentFixture().Hotel; mirror.EnableReadOnlyMirror();
            Require(mirror.ApplySnapshot(JsonUtility.FromJson<HotelModelSnapshot>(JsonUtility.ToJson(hotel.CaptureSnapshot(968, 1)))));
            Assert.That(mirror.Services.FindIntent(intent.Id).Collecting, Is.True);
            Assert.That(mirror.Services.ReceivePhysicalBlanket(intent.Id, intent.Revision).Success, Is.False);
            hotel.Tick(.5f);
            Assert.That(guest.Memory.BlanketsDelivered, Is.Zero, "Walking time cannot grant a receipt.");
            Assert.That(hotel.Services.ReceivePhysicalBlanket(intent.Id, intent.Revision-1).Success, Is.False);
            // Explicit headless physical-arrival adapter. The scene case checks the real shelf walk.
            Require(hotel.Services.ReceivePhysicalBlanket(intent.Id, intent.Revision));
            Assert.That(intent.Collecting, Is.False);
            Assert.That(intent.Status, Is.EqualTo(ServiceIntentStatus.Completed));
            Assert.That(guest.BlanketComfortBonus, Is.GreaterThan(0));
            Assert.That(guest.Memory.BlanketsDelivered, Is.EqualTo(1));
            Assert.That(hotel.Services.ReceivePhysicalBlanket(intent.Id, intent.Revision).Success, Is.False);
            Require(mirror.ApplySnapshot(JsonUtility.FromJson<HotelModelSnapshot>(JsonUtility.ToJson(hotel.CaptureSnapshot(968, 2)))));
            Assert.That(mirror.Services.FindIntent(intent.Id).Status, Is.EqualTo(ServiceIntentStatus.Completed));
        }
    }
}
