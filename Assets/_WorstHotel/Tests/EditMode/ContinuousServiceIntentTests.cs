using System;
using System.Linq;
using NUnit.Framework;

namespace WorstHotel.Tests
{
    public sealed partial class ContinuousGuestServiceTests
    {
        static Fixture IntentFixture() => Create(new GuestServiceSettings(maxCasesPerShift: 8,
            eligibility: 0, naturalCommunicationEnabled: true));

        static ServiceCase CommunicateBlanket(Fixture fixture, bool accept = true)
        {
            var hotel = fixture.Hotel; var guest = fixture.Business;
            Require(hotel.DebugSetMildCold(guest.GuestId)); hotel.Tick(.25f);
            Require(hotel.DebugForceService(guest.GuestId, ServiceKind.ExtraBlanket));
            var request = hotel.Services.Cases.Single(item => item.GuestId == guest.GuestId);
            Require(hotel.DebugBeginGuestContact(guest.GuestId, GuestContactChannel.Phone));
            // Explicit headless route adapter; scene tests separately prove actual phone/room movement.
            Require(hotel.SignalGuestResponseAnchorReached(guest.GuestId, guest.Agent.ResponseActionId,
                guest.Agent.ResponseActionVersion, GuestResponseAnchor.RoomPhone));
            Require(hotel.AnswerIncomingServiceCall(0, request.Response.Id));
            if (accept) Require(hotel.RespondToService(0, request.Id, true));
            return request;
        }

        static ServiceCase CommunicateWakeDecision(Fixture fixture)
        {
            var hotel = fixture.Hotel; var guest = fixture.Business;
            AdvanceTo(hotel, guest.Agent.Schedule.SleepTime - 20);
            Require(hotel.DebugForceService(guest.GuestId, ServiceKind.WakeUpCall));
            var request = hotel.Services.Cases.Single(item => item.GuestId == guest.GuestId);
            Require(hotel.DebugBeginGuestContact(guest.GuestId, GuestContactChannel.Phone));
            Require(hotel.SignalGuestResponseAnchorReached(guest.GuestId, guest.Agent.ResponseActionId,
                guest.Agent.ResponseActionVersion, GuestResponseAnchor.RoomPhone));
            Require(hotel.AnswerIncomingServiceCall(0, request.Response.Id));
            return request;
        }

        static GuestServiceIntent PlacePendingBlanket(Fixture fixture)
        {
            var hotel = fixture.Hotel; var guest = fixture.Business;
            var intent = hotel.Services.DropOffIntent(guest.GuestId);
            Require(hotel.TakeServiceItem(0, "blanket:0"));
            var item = hotel.Services.HeldBy(0);
            Require(hotel.DropOffBlanket(0, guest.GuestId, guest.RoomId, intent.Revision, item.Generation));
            Assert.That(intent.Status, Is.EqualTo(ServiceIntentStatus.AwaitingReceipt));
            return intent;
        }

        [Test]
        public void DirectDecisionHoldsAcrossBedtimeThenTimesOutExactlyOnceAndReleasesThePhoneAction()
        {
            var fixture = IntentFixture(); var hotel = fixture.Hotel; var guest = fixture.Business;
            var request = CommunicateWakeDecision(fixture);
            var intent = hotel.Services.DirectIntent(guest.GuestId);
            Assert.That(intent, Is.Not.Null); Assert.That(intent.Kind, Is.EqualTo(ServiceIntentKind.Direct));
            Assert.That(guest.Agent.DirectServiceIntentId, Is.EqualTo(intent.Id));
            Assert.That(guest.Agent.ResponseActionId, Is.EqualTo(request.Response.Id));
            float deadline = intent.Deadline;
            Require(hotel.AcknowledgeService(0, request.Id));
            Assert.That(intent.Deadline, Is.EqualTo(deadline), "Acknowledgement does not renew the finite wait.");
            Assert.That(hotel.ForceSleep(guest.GuestId).Success, Is.False);
            Assert.That(hotel.ForceLeaveRoom(guest.GuestId).Success, Is.False);
            Assert.That(hotel.ForceActivity(guest.GuestId, GuestActivity.Shower).Success, Is.False);
            Assert.That(hotel.RequestGuestMove(0, guest.GuestId, 105).Success, Is.False);
            AdvanceTo(hotel, guest.Agent.Schedule.SleepTime + 1);
            Assert.That(guest.Agent.State, Is.Not.EqualTo(GuestAgentState.Sleeping));
            Assert.That(hotel.Services.DirectIntent(guest.GuestId), Is.SameAs(intent));
            AdvanceTo(hotel, deadline + .5f);
            Assert.That(intent.Status, Is.EqualTo(ServiceIntentStatus.TimedOut));
            Assert.That(request.Status, Is.EqualTo(ServiceStatus.Expired));
            Assert.That(guest.Agent.DirectServiceIntentId, Is.Null);
            Assert.That(guest.Agent.ResponseActionId, Is.Null);
            Assert.That(hotel.Services.Promises, Is.Empty);
            Assert.That(guest.Agent.State, Is.EqualTo(GuestAgentState.Sleeping));
            int revision = intent.Revision; float resolved = intent.ResolutionAt;
            hotel.Tick(2);
            Assert.That(intent.Revision, Is.EqualTo(revision)); Assert.That(intent.ResolutionAt, Is.EqualTo(resolved));
        }

        [Test]
        public void AcceptingFutureWakePromiseEndsOnlyTheDirectDecisionAndRetainsThePromise()
        {
            var fixture = IntentFixture(); var hotel = fixture.Hotel; var guest = fixture.Business;
            var request = CommunicateWakeDecision(fixture); var intent = hotel.Services.DirectIntent(guest.GuestId);
            Require(hotel.RespondToService(0, request.Id, true));
            Assert.That(intent.Status, Is.EqualTo(ServiceIntentStatus.Completed));
            Assert.That(hotel.Services.DirectIntent(guest.GuestId), Is.Null);
            Assert.That(guest.Agent.ResponseActionId, Is.Null);
            Assert.That(hotel.Services.FindPromise(request.Id).Status, Is.EqualTo(PromiseStatus.Accepted));
            Assert.That(request.Status, Is.EqualTo(ServiceStatus.InProgress));
            AdvanceTo(hotel, guest.Agent.Schedule.SleepTime + 1);
            Assert.That(guest.Agent.State, Is.EqualTo(GuestAgentState.Sleeping));
            Assert.That(hotel.Services.FindPromise(request.Id).Status, Is.EqualTo(PromiseStatus.Accepted));
        }

        [Test]
        public void RoomMoveWaitDoesNotRenewOnRepeatedProposalAndTimeoutReleasesOnlyDestinationReservation()
        {
            var fixture = IntentFixture(); var hotel = fixture.Hotel; var guest = fixture.Business;
            Require(hotel.RequestGuestMove(0, guest.GuestId, 105));
            var intent = hotel.Services.DirectIntent(guest.GuestId); float deadline = intent.Deadline;
            hotel.Tick(2); Require(hotel.RequestGuestMove(1, guest.GuestId, 105));
            Assert.That(intent.Deadline, Is.EqualTo(deadline));
            Assert.That(hotel.SkipActivity(guest.GuestId).Success, Is.False);
            AdvanceTo(hotel, deadline + .25f);
            Assert.That(intent.Status, Is.EqualTo(ServiceIntentStatus.TimedOut));
            Assert.That(guest.Agent.PendingMoveRoomId, Is.Null);
            Assert.That(fixture.Rooms.Single(room => room.Profile.Id == 105).ReservedGuestId, Is.Null);
            Assert.That(guest.RoomId, Is.EqualTo(101)); Assert.That(hotel.Keys.Find(101).GuestId, Is.EqualTo(guest.GuestId));
            Require(hotel.RequestGuestMove(0, guest.GuestId, 105));
            Assert.That(hotel.Services.DirectIntent(guest.GuestId).Id, Is.Not.EqualTo(intent.Id));
            Require(hotel.CancelGuestMove(0, guest.GuestId));
            Assert.That(guest.Agent.DirectServiceIntentId, Is.Null);
        }

        [Test]
        public void RealKeyExchangeCompletesDirectIntentBeforeTheGuestStartsTheirTransfer()
        {
            var fixture = IntentFixture(); var hotel = fixture.Hotel; var guest = fixture.Business;
            Require(hotel.RequestGuestMove(0, guest.GuestId, 105));
            var intent = hotel.Services.DirectIntent(guest.GuestId);
            Require(ModelKeyHandoff.MoveGuest(hotel, 0, guest.GuestId, 105));
            Assert.That(intent.Status, Is.EqualTo(ServiceIntentStatus.Completed));
            Assert.That(guest.Agent.DirectServiceIntentId, Is.Null);
            Assert.That(guest.Agent.IsRelocating, Is.True);
            Assert.That(guest.Agent.State, Is.EqualTo(GuestAgentState.GoingToRoom));
            Assert.That(hotel.Keys.Find(105).GuestId, Is.EqualTo(guest.GuestId));
        }

        [Test]
        public void DropOffWhileAwayCrossesMidnightWithoutComfortOrRefillThenActualReturnReceivesOnce()
        {
            var fixture = IntentFixture(); var hotel = fixture.Hotel; var guest = fixture.Business;
            var request = CommunicateBlanket(fixture);
            Assert.That(hotel.Services.DirectIntent(guest.GuestId), Is.Null);
            Require(hotel.ForceLeaveRoom(guest.GuestId)); Require(hotel.SignalGuestLeftRoom(guest.GuestId));
            var intent = PlacePendingBlanket(fixture); var parcel = hotel.Services.FindItem(intent.ItemId);
            int generation = parcel.Generation;
            AdvanceTo(hotel, hotel.Calendar.At(2, 0) + 1);
            Assert.That(intent.Status, Is.EqualTo(ServiceIntentStatus.AwaitingReceipt));
            Assert.That(parcel.Location, Is.EqualTo(ServiceItemLocation.AwaitingReceipt));
            Assert.That(parcel.Generation, Is.EqualTo(generation));
            Assert.That(guest.BlanketComfortBonus, Is.Zero); Assert.That(guest.Memory.BlanketsDelivered, Is.Zero);
            Assert.That(guest.Memory.ServicesFulfilled, Is.Zero); Assert.That(request.Status, Is.EqualTo(ServiceStatus.InProgress));
            AdvanceTo(hotel, guest.Agent.Schedule.WakeTime + .25f);
            if (guest.Agent.State == GuestAgentState.GuestAway) Require(hotel.ForceReturnRoom(guest.GuestId));
            Require(hotel.SignalGuestReturnedRoom(guest.GuestId)); hotel.Tick(.25f);
            Assert.That(intent.Status, Is.EqualTo(ServiceIntentStatus.Completed)); Assert.That(intent.ReceivedAt, Is.GreaterThan(intent.DeliveredAt));
            Assert.That(parcel.Location, Is.EqualTo(ServiceItemLocation.Delivered));
            Assert.That(guest.BlanketComfortBonus, Is.EqualTo(hotel.Services.Settings.BlanketComfortBonus));
            Assert.That(guest.Memory.BlanketsDelivered, Is.EqualTo(1)); Assert.That(guest.Memory.ServicesFulfilled, Is.EqualTo(1));
            float adjustment = guest.ServiceSatisfactionAdjustment; hotel.Tick(2);
            Assert.That(guest.Memory.BlanketsDelivered, Is.EqualTo(1)); Assert.That(guest.ServiceSatisfactionAdjustment, Is.EqualTo(adjustment));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void PrivateSleepingOrShoweringGuestDoesNotReceivePendingBlanketUntilAvailable(bool sleeping)
        {
            var fixture = IntentFixture(); var hotel = fixture.Hotel; var guest = fixture.Business;
            CommunicateBlanket(fixture);
            Require(sleeping ? hotel.ForceSleep(guest.GuestId) : hotel.ForceActivity(guest.GuestId, GuestActivity.Shower));
            Assert.That(hotel.RequestStaffRoomAccess(0, guest.RoomId).Success, Is.False);
            var intent = PlacePendingBlanket(fixture); hotel.Tick(2);
            Assert.That(intent.Status, Is.EqualTo(ServiceIntentStatus.AwaitingReceipt));
            Assert.That(guest.BlanketComfortBonus, Is.Zero); Assert.That(guest.Memory.ServicesFulfilled, Is.Zero);
            Require(hotel.ForceActivity(guest.GuestId, GuestActivity.QuietRest)); hotel.Tick(.25f);
            Assert.That(intent.Status, Is.EqualTo(ServiceIntentStatus.Completed));
            Assert.That(guest.Memory.BlanketsDelivered, Is.EqualTo(1));
        }

        [Test]
        public void DropOffRejectsUnacceptedWrongActorRoomRevisionAndGenerationWithoutConsumingTheBlanket()
        {
            var fixture = IntentFixture(); var hotel = fixture.Hotel; var guest = fixture.Business;
            var request = CommunicateBlanket(fixture, false); var intent = hotel.Services.DropOffIntent(guest.GuestId);
            Require(hotel.TakeServiceItem(0, "blanket:0")); var parcel = hotel.Services.HeldBy(0);
            Assert.That(hotel.DropOffBlanket(0, guest.GuestId, guest.RoomId, intent.Revision, parcel.Generation).Success, Is.False);
            Require(hotel.RespondToService(0, request.Id, true)); int events = hotel.EventRevision;
            Assert.That(hotel.DropOffBlanket(1, guest.GuestId, guest.RoomId, intent.Revision, parcel.Generation).Success, Is.False);
            Assert.That(hotel.DropOffBlanket(0, guest.GuestId, 103, intent.Revision, parcel.Generation).Success, Is.False);
            Assert.That(hotel.DropOffBlanket(0, guest.GuestId, guest.RoomId, intent.Revision + 1, parcel.Generation).Success, Is.False);
            Assert.That(hotel.DropOffBlanket(0, guest.GuestId, guest.RoomId, intent.Revision, parcel.Generation + 1).Success, Is.False);
            Assert.That(hotel.EventRevision, Is.EqualTo(events)); Assert.That(parcel.Location, Is.EqualTo(ServiceItemLocation.HeldByPlayer));
            Assert.That(intent.ItemId, Is.Null); Assert.That(guest.Memory.BlanketsDelivered, Is.Zero);
            Require(hotel.DropOffBlanket(0, guest.GuestId, guest.RoomId, intent.Revision, parcel.Generation));
            Assert.That(hotel.DropOffBlanket(0, guest.GuestId, guest.RoomId, intent.Revision, parcel.Generation).Success, Is.False);
        }

        [Test]
        public void CancelledUnreceivedParcelBecomesReclaimableWithNewGenerationAndNoDeliveryReward()
        {
            var fixture = IntentFixture(); var hotel = fixture.Hotel; var guest = fixture.Business;
            var request = CommunicateBlanket(fixture); Require(hotel.ForceSleep(guest.GuestId));
            var intent = PlacePendingBlanket(fixture); var parcel = hotel.Services.FindItem(intent.ItemId); int generation = parcel.Generation;
            Require(hotel.RespondToService(0, request.Id, false));
            Assert.That(intent.Status, Is.EqualTo(ServiceIntentStatus.Cancelled));
            Assert.That(parcel.Location, Is.EqualTo(ServiceItemLocation.Dropped)); Assert.That(parcel.Generation, Is.EqualTo(generation + 1));
            Assert.That(parcel.GuestId, Is.Null); Assert.That(parcel.RoomId, Is.Null);
            Assert.That(guest.Memory.BlanketsDelivered, Is.Zero); Assert.That(guest.Memory.ServicesFulfilled, Is.Zero);
            Require(hotel.TakeServiceItem(1, parcel.Id)); Require(hotel.ReturnServiceItem(1, parcel.Id));
            Assert.That(parcel.Location, Is.EqualTo(ServiceItemLocation.OnShelf));
            Assert.That(hotel.Services.BlanketsAvailable, Is.EqualTo(3));
        }

        [Test]
        public void RelocationCancelsOldRoomParcelAndSynchronizesCurrentRemoteIntentImmediately()
        {
            var fixture = IntentFixture(); var hotel = fixture.Hotel; var guest = fixture.Business;
            var request = CommunicateBlanket(fixture); Require(hotel.ForceSleep(guest.GuestId));
            var intent = PlacePendingBlanket(fixture); var parcel = hotel.Services.FindItem(intent.ItemId);
            Require(hotel.ForceActivity(guest.GuestId, GuestActivity.QuietRest));
            Require(ModelKeyHandoff.MoveGuest(hotel, 0, guest.GuestId, 105));
            Assert.That(intent.Status, Is.EqualTo(ServiceIntentStatus.Cancelled)); Assert.That(intent.RoomId, Is.EqualTo(101));
            Assert.That(parcel.Location, Is.EqualTo(ServiceItemLocation.Dropped)); Assert.That(parcel.RoomId, Is.Null);
            Assert.That(request.Status, Is.EqualTo(ServiceStatus.Expired)); Assert.That(guest.Memory.BlanketsDelivered, Is.Zero);
            Assert.That(hotel.Services.Intents.Where(item => item.GuestId == guest.GuestId && item.Active)
                .All(item => item.RoomId == 105), Is.True);
            Assert.DoesNotThrow(() => hotel.CaptureSnapshot(1, 1));
        }

        [Test]
        public void InteriorProactiveDeliveryRecordsRealReceiptWithoutLeavingAnEmptyCompletedDropOff()
        {
            var fixture = IntentFixture(); var hotel = fixture.Hotel; var guest = fixture.Business;
            CommunicateBlanket(fixture); var intent = hotel.Services.DropOffIntent(guest.GuestId);
            Require(hotel.TakeServiceItem(0, "blanket:0")); Require(hotel.DeliverBlanket(0, guest.GuestId));
            Assert.That(intent.Status, Is.EqualTo(ServiceIntentStatus.Completed)); Assert.That(intent.ItemId, Is.EqualTo("blanket:0"));
            Assert.That(intent.DeliveredAt, Is.EqualTo(hotel.Elapsed)); Assert.That(intent.ReceivedAt, Is.EqualTo(hotel.Elapsed));
            Assert.That(guest.Memory.ServicesFulfilled, Is.EqualTo(1)); Assert.That(guest.Memory.BlanketsDelivered, Is.EqualTo(1));
        }

        [Test]
        public void CheckoutOutranksDirectWaitAndReclaimsAnyUnreceivedParcelWithoutComfort()
        {
            var fixture = IntentFixture(); var hotel = fixture.Hotel; var guest = fixture.Business;
            CommunicateBlanket(fixture); Require(hotel.ForceSleep(guest.GuestId));
            var parcelIntent = PlacePendingBlanket(fixture); var parcel = hotel.Services.FindItem(parcelIntent.ItemId);
            Require(hotel.ForceActivity(guest.GuestId, GuestActivity.QuietRest));
            Require(hotel.RequestGuestMove(0, guest.GuestId, 105));
            var direct = hotel.Services.DirectIntent(guest.GuestId);
            Require(hotel.DebugCheckoutGuest(guest.GuestId));
            Assert.That(guest.Agent.State, Is.EqualTo(GuestAgentState.CheckingOut));
            Assert.That(direct.Status, Is.EqualTo(ServiceIntentStatus.Cancelled));
            Assert.That(guest.Agent.DirectServiceIntentId, Is.Null); Assert.That(guest.Agent.PendingMoveRoomId, Is.Null);
            Assert.That(parcelIntent.Status, Is.EqualTo(ServiceIntentStatus.Cancelled));
            Assert.That(parcel.Location, Is.EqualTo(ServiceItemLocation.Dropped)); Assert.That(parcel.GuestId, Is.Null);
            Assert.That(guest.Memory.BlanketsDelivered, Is.Zero); Assert.That(guest.Memory.ServicesFulfilled, Is.Zero);
        }

        [Test]
        public void RecoveredTemperatureCancelsAnUnneededBlanketRequestWithoutInventingAReceivedItem()
        {
            var fixture = IntentFixture(); var hotel = fixture.Hotel; var guest = fixture.Business;
            var request = CommunicateBlanket(fixture); var intent = hotel.Services.DropOffIntent(guest.GuestId);
            Require(hotel.SetRoomTemperature(guest.RoomId, 23));
            AdvanceTo(hotel, hotel.Elapsed + hotel.NeedsSettings.RecoverySeconds + 1);
            Assert.That(request.Status, Is.EqualTo(ServiceStatus.Expired));
            Assert.That(intent.Status, Is.EqualTo(ServiceIntentStatus.Cancelled)); Assert.That(intent.ItemId, Is.Null);
            Assert.That(guest.Memory.BlanketsDelivered, Is.Zero); Assert.That(guest.Memory.ServicesFulfilled, Is.Zero);
        }

        [Test]
        public void OrdinaryRequestGenerationDefersDuringTheFinalContactLeadBeforeSleep()
        {
            var fixture = IntentFixture(); var hotel = fixture.Hotel; var guest = fixture.Business;
            AdvanceTo(hotel, guest.Agent.Schedule.SleepTime - hotel.Services.Settings.ContactLeadSeconds + 1);
            Assert.That(hotel.DebugForceService(guest.GuestId, ServiceKind.WakeUpCall).Success, Is.False);
            Assert.That(hotel.Services.Cases.Any(item => item.GuestId == guest.GuestId), Is.False);
            Assert.That(hotel.RequestGuestMove(0, guest.GuestId, 105).Success, Is.True,
                "A deliberate staff-initiated key exchange remains a bounded direct action.");
            Assert.That(hotel.Services.DirectIntent(guest.GuestId).Deadline - hotel.Elapsed,
                Is.EqualTo(hotel.Services.Settings.DirectWaitSeconds).Within(.001f));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void CorrectKeyHandoffEndsObsoleteLuggageContactAndItsImmediateSnapshotAppliesWithoutATick(bool disclosed)
        {
            var fixture = IntentFixture(); var hotel = fixture.Hotel;
            Require(hotel.DebugSpawnGuest(GuestKind.Budget, 105));
            var reservation = hotel.Reservations.Single(item => item.RoomId == 105);
            Assert.That(hotel.Guests.Any(item => item.GuestId == reservation.Id), Is.False,
                "A future walk-in remains a booking until its scheduled arrival.");
            Require(hotel.DebugMarkRoomDirty(105));
            AdvanceTo(hotel, reservation.Offer.ArrivalAt + .25f);
            var guest = hotel.Guests.Single(item => item.GuestId == reservation.Id);
            Assert.That(guest.Agent.State, Is.EqualTo(GuestAgentState.Arriving));
            Require(hotel.SignalGuestReachedReception(guest.GuestId));
            Require(hotel.DebugForceService(guest.GuestId, ServiceKind.LuggageStorage));
            var request = hotel.Services.Cases.Single(item => item.GuestId == guest.GuestId);
            Require(hotel.DebugBeginGuestContact(guest.GuestId, GuestContactChannel.Reception));
            if (disclosed) Require(hotel.TalkToServiceGuest(0, guest.GuestId, request.Response.Id));
            var intent = hotel.Services.DirectIntent(guest.GuestId);
            Assert.That(intent != null, Is.EqualTo(disclosed));
            // Explicit model linen/key adapters execute the normal ownership commands. No hotel
            // tick between the physical-readiness result, handoff and captured network frame.
            ManualTurnoverModelAdapter.PrepareRoom(hotel, 105);
            Require(ModelKeyHandoff.CheckIn(hotel, 0, guest.GuestId));
            Assert.That(guest.Agent.State, Is.EqualTo(GuestAgentState.GoingToRoom));
            Assert.That(guest.Agent.ResponseActionId, Is.Null); Assert.That(guest.Agent.DirectServiceIntentId, Is.Null);
            Assert.That(request.Status, Is.EqualTo(ServiceStatus.Expired));
            if (intent != null) Assert.That(intent.Status, Is.EqualTo(ServiceIntentStatus.Cancelled));
            Assert.That(guest.Memory.ServicesFulfilled, Is.Zero);
            Assert.That(hotel.Services.FindItem("luggage:" + guest.GuestId).Location, Is.EqualTo(ServiceItemLocation.OnShelf));
            var frame = hotel.CaptureSnapshot(77, 1);
            var mirror = IntentFixture().Hotel; mirror.EnableReadOnlyMirror();
            Require(mirror.ApplySnapshot(frame));
            var copied = mirror.Guests.Single(item => item.GuestId == guest.GuestId);
            Assert.That(copied.Agent.State, Is.EqualTo(GuestAgentState.GoingToRoom));
            Assert.That(copied.Agent.ResponseActionId, Is.Null); Assert.That(copied.Agent.DirectServiceIntentId, Is.Null);
        }
    }
}
