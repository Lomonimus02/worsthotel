using System;
using System.Linq;
using NUnit.Framework;

namespace WorstHotel.Tests
{
    public sealed class EarlyCheckoutLifecycleTests
    {
        sealed class Fixture
        {
            public HotelSimulation Hotel;
            public RoomState[] Rooms;
            public GuestStay Guest;
            public EconomySettings Economy;
        }

        static void Require(CommandResult result) => Assert.That(result.Success, Is.True, result.Message);

        static Fixture Create(float creditRate = .2f, bool populate = true)
        {
            var needs = new NeedProfile(21, 25, 18, 28, .125f, .25f, 65);
            var profiles = new[]
            {
                new GuestProfile(GuestKind.Budget, "Budget", "", 180, .85f, 18, .7f, 28, 90, .55f, needs: needs),
                new GuestProfile(GuestKind.ColdSensitive, "Cold", "", 300, 1.05f, 20, 1.35f, 16, 65, .4f, needs: needs),
                new GuestProfile(GuestKind.Business, "Business", "", 450, 1, 19.5f, 1, 10, 45, .25f, needs: needs)
            };
            var economy = new EconomySettings(compensationRate: creditRate);
            var settings = new SessionSettings(profiles, Enumerable.Range(101, 6).Select(id =>
                new RoomProfile(id, "Room " + id, noise: 0, temperature: 22)), new BoilerSettings(), economy);
            var rooms = settings.Rooms.Select(profile => new RoomState(profile)).ToArray();
            // Explicit fast lifecycle fixture, not production pacing: shorter exposure/grace,
            // stable ordinary rest, controlled actual temperature, headless route/key adapters.
            var hotel = new HotelSimulation(settings, rooms,
                new LivingHotelSettings(firstActivityDelay: 1000, quietDurationMin: 1000, quietDurationMax: 1000),
                new NeedSettings(complaintDissatisfaction: .01f, escalatedDissatisfaction: .02f, criticalDissatisfaction: .03f,
                    complaintExposureSeconds: .25f, escalatedExposureSeconds: .5f, criticalExposureSeconds: 1,
                    compensationReliefSeconds: .5f, earlyCheckout: new EarlyCheckoutSettings(enabled: true,
                        severeHours: .05f, graceHours: .05f, recoveryHours: .01f, minimumRemainingStayHours: .1f)),
                services: new GuestServiceSettings(maxCasesPerShift: 8, maxCasesPerGuest: 4, eligibility: 0,
                    observationSeconds: .1f, naturalCommunicationEnabled: true, selfResponseObserveSeconds: 1000,
                    toleranceSeconds: 1000), operations: new OperationsSettings());
            var fixture = new Fixture { Hotel = hotel, Rooms = rooms, Economy = economy };
            if (!populate) return fixture;
            Require(hotel.StartOperations());
            var offer = hotel.BookingOffers.First(item => item.ArrivalDay == 1 && item.Application.Archetype.Kind == GuestKind.Business);
            Require(hotel.AcceptBooking(0, offer.Id, 101, 300));
            Advance(hotel, offer.ArrivalAt + .25f);
            fixture.Guest = hotel.Guests.Single();
            Require(hotel.SignalGuestReachedReception(offer.Id));
            Require(ModelKeyHandoff.CheckIn(hotel, 0, offer.Id));
            Require(hotel.SignalGuestReachedRoom(offer.Id));
            return fixture;
        }

        static void Advance(HotelSimulation hotel, float target)
        { while (hotel.Elapsed < target) hotel.Tick(Math.Min(.25f, target - hotel.Elapsed)); }

        static void ColdTick(Fixture fixture)
        { Require(fixture.Hotel.SetRoomTemperature(fixture.Guest.RoomId, 5)); fixture.Hotel.Tick(.25f); }

        static HotelIncident DiscussSevereCold(Fixture fixture, bool credit = false)
        {
            var hotel = fixture.Hotel; var guest = fixture.Guest;
            for (int step = 0; step < 32 && !hotel.Incidents.Items.Any(item => item.GuestId == guest.GuestId &&
                item.Active && item.Reason == IncidentReason.Temperature && item.Stage >= SituationStage.Escalated); step++) ColdTick(fixture);
            var incident = hotel.Incidents.Items.Single(item => item.GuestId == guest.GuestId && item.Active && item.Reason == IncidentReason.Temperature);
            Require(hotel.DiscussRoomConcern(0, guest.GuestId, incident.Response.Id));
            Assert.That(hotel.Services.CompensationDiscussion(guest.GuestId), Is.Not.Null);
            Require(credit ? hotel.OfferCompensation(guest.GuestId) : hotel.AcceptConsequences(0, guest.GuestId));
            return incident;
        }

        static void AwaitEarlyCheckout(Fixture fixture)
        {
            bool warned = fixture.Guest.EarlyCheckout.State == EarlyCheckoutState.Warning;
            for (int step = 0; step < 120 && !fixture.Guest.ReceiptPosted; step++)
            {
                ColdTick(fixture);
                warned |= fixture.Guest.EarlyCheckout.State == EarlyCheckoutState.Warning;
            }
            Assert.That(warned, Is.True, "The policy must give a warning/grace before its terminal outcome.");
            Assert.That(fixture.Guest.EarlyCheckout.State, Is.EqualTo(EarlyCheckoutState.Committed));
            Assert.That(fixture.Guest.ReceiptPosted, Is.True);
        }

        static PromiseWakeUp AgreeFutureWake(Fixture fixture)
        {
            var hotel = fixture.Hotel; var guest = fixture.Guest;
            Advance(hotel, guest.Agent.Schedule.SleepTime - 20);
            Require(hotel.DebugForceService(guest.GuestId, ServiceKind.WakeUpCall));
            var request = hotel.Services.Cases.Single(item => item.Kind == ServiceKind.WakeUpCall);
            Require(hotel.DebugBeginGuestContact(guest.GuestId, GuestContactChannel.Phone));
            Require(hotel.SignalGuestResponseAnchorReached(guest.GuestId, guest.Agent.ResponseActionId,
                guest.Agent.ResponseActionVersion, GuestResponseAnchor.RoomPhone));
            Require(hotel.AnswerIncomingServiceCall(0, request.Response.Id));
            Require(hotel.RespondToService(0, request.Id, true));
            return hotel.Services.FindPromise(request.Id);
        }

        static GuestReceipt CurrentReceipt(HotelSimulation hotel)
        {
            var value = hotel.CaptureSnapshot(90, 1).Operations.PeriodReceipts.Single();
            return new GuestReceipt(value.GuestId, value.Name, value.RoomId, value.Price, value.Satisfaction,
                value.Compensation, value.Review, value.EarlyCheckout, value.CheckoutAt, value.DepartureReason);
        }

        static void AssertMirror(HotelSimulation hotel)
        {
            var mirror = Create(populate: false).Hotel; mirror.EnableReadOnlyMirror();
            Require(mirror.ApplySnapshot(hotel.CaptureSnapshot(95, 1)));
        }

        [TestCase(.2f, .5f)]
        [TestCase(.8f, .8f)]
        public void EarlyBillPreservesContractAndTakesOneMaximumRefundAcrossReceiptAndReport(float creditRate, float expectedRefundRate)
        {
            var fixture = Create(creditRate); var hotel = fixture.Hotel; var guest = fixture.Guest;
            float contracted = guest.Agent.Schedule.CheckoutTime;
            int initialCash = hotel.Economy.Cash;
            var incident = DiscussSevereCold(fixture, true);
            AwaitEarlyCheckout(fixture);
            var receipt = CurrentReceipt(hotel);
            int expectedRefund = (int)Math.Round(guest.Price * expectedRefundRate, MidpointRounding.AwayFromZero);
            Assert.That(receipt.EarlyCheckout, Is.True);
            Assert.That(receipt.Price, Is.EqualTo(guest.Price));
            Assert.That(receipt.Compensation, Is.EqualTo(expectedRefund));
            Assert.That(receipt.Net, Is.EqualTo(guest.Price - expectedRefund));
            Assert.That(receipt.Satisfaction, Is.LessThanOrEqualTo(fixture.Economy.SevereRefundThreshold));
            Assert.That(receipt.CheckoutAt, Is.EqualTo(guest.EarlyCheckout.CommittedAt).And.LessThan(contracted));
            Assert.That(receipt.DepartureReason, Is.EqualTo(guest.EarlyCheckout.CauseDescription));
            Assert.That(receipt.Review, Does.Contain(receipt.DepartureReason));
            Assert.That(guest.Agent.Schedule.CheckoutTime, Is.EqualTo(contracted));
            Assert.That(hotel.Reservations.Single().Offer.CheckoutAt, Is.EqualTo(contracted));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(initialCash + receipt.Net));
            Assert.That(incident.Active, Is.False);
            Assert.That(fixture.Rooms[0].DepartingGuestId, Is.EqualTo(guest.GuestId));
            Assert.That(fixture.Rooms[0].Cleanliness, Is.EqualTo(Cleanliness.Dirty));
            Assert.That(hotel.Keys.Find(101).Location, Is.EqualTo(RoomKeyLocation.Returned));
            Assert.That(hotel.Keys.Find(101).GuestId, Is.Null);

            var mirror = Create(creditRate, false).Hotel; mirror.EnableReadOnlyMirror();
            Require(mirror.ApplySnapshot(hotel.CaptureSnapshot(91, 1)));
            Assert.That(CurrentReceipt(mirror).DepartureReason, Is.EqualTo(receipt.DepartureReason));
            // Explicit headless exit adapter: payment must not wait for these physical boundaries.
            Advance(hotel, hotel.Elapsed + 3);
            Require(hotel.SignalGuestVacatedRoom(guest.GuestId, 101));
            Require(hotel.SignalGuestLeft(guest.GuestId));
            Require(hotel.Keys.PickUp(0, 101));
            Require(hotel.Keys.ReturnGuestKeys(guest.GuestId));
            Assert.That(hotel.Keys.Find(101).Location, Is.EqualTo(RoomKeyLocation.HeldByPlayer));
            Assert.That(hotel.Keys.Find(101).PlayerId, Is.EqualTo(0), "A repeated departed-owner return cannot reclaim the staff key.");
            Require(hotel.Keys.Drop(0, 101)); Require(hotel.Keys.ReturnToRack(101));
            Advance(hotel, hotel.NextReportAt + .25f);
            Assert.That(hotel.DayReports.Single().Receipts.Count, Is.EqualTo(1));
            Assert.That(hotel.DayReports.Single().Receipts.Single().DepartureReason, Is.EqualTo(receipt.DepartureReason));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(initialCash + receipt.Net - fixture.Economy.DailyOperatingCost));
            Require(mirror.ApplySnapshot(hotel.CaptureSnapshot(91, 2)));
            int settledCash = hotel.Economy.Cash;
            hotel.Tick(1);
            Assert.That(hotel.Economy.Cash, Is.EqualTo(settledCash));
            Assert.That(hotel.CaptureSnapshot(91, 3).Operations.PeriodReceipts, Is.Empty);
        }

        [Test]
        public void LaterDirectDiscussionCanFinishButCannotRenewExhaustedDepartureGrace()
        {
            var fixture = Create(); var hotel = fixture.Hotel; var guest = fixture.Guest;
            var incident = DiscussSevereCold(fixture);
            for (int step = 0; step < 80 && guest.EarlyCheckout.State != EarlyCheckoutState.Warning; step++) ColdTick(fixture);
            Assert.That(guest.EarlyCheckout.State, Is.EqualTo(EarlyCheckoutState.Warning));
            ColdTick(fixture); // A genuinely later discussion, not the one present at warning time.
            Require(hotel.BeginCompensationDiscussion(0, guest.GuestId, incident.Id));
            var discussion = hotel.Services.CompensationDiscussion(guest.GuestId);
            Assert.That(discussion.CreatedAt, Is.GreaterThan(guest.EarlyCheckout.WarningAt));
            float deadline = discussion.Deadline;
            for (int step = 0; step < 80 && !hotel.EarlyCheckoutDecisionPending(guest); step++) ColdTick(fixture);
            Assert.That(hotel.EarlyCheckoutDecisionPending(guest), Is.True);
            Assert.That(guest.ReceiptPosted, Is.False, "The already active finite decision is allowed to finish.");
            Require(hotel.BeginCompensationDiscussion(0, guest.GuestId, incident.Id));
            Assert.That(discussion.Deadline, Is.EqualTo(deadline), "Reopening the same conversation never renews its deadline.");
            Require(hotel.EndCompensationDiscussion(0, guest.GuestId, discussion.Id, discussion.Revision));
            int count = hotel.Services.Intents.Count;
            Assert.That(hotel.BeginCompensationDiscussion(0, guest.GuestId, incident.Id).Success, Is.False);
            Assert.That(hotel.RequestGuestMove(0, guest.GuestId, 105).Success, Is.False);
            Assert.That(hotel.Services.Intents.Count, Is.EqualTo(count));
            Assert.That(guest.Agent.PendingMoveRoomId, Is.Null);
            Assert.That(guest.Agent.ResponseActionId, Is.Null);
            ColdTick(fixture);
            Assert.That(guest.EarlyCheckout.State, Is.EqualTo(EarlyCheckoutState.Committed));
            Assert.That(guest.ReceiptPosted, Is.True);
            AssertMirror(hotel);
        }

        [Test]
        public void EveningEarlyDepartureCancelsTomorrowWakeWithoutInventingABrokenPromise()
        {
            var fixture = Create(); var hotel = fixture.Hotel; var guest = fixture.Guest;
            var promise = AgreeFutureWake(fixture);
            float adjustment = guest.ServiceSatisfactionAdjustment;
            DiscussSevereCold(fixture);
            AwaitEarlyCheckout(fixture);
            Assert.That(hotel.Elapsed, Is.LessThan(promise.DueTime));
            Assert.That(promise.Status, Is.EqualTo(PromiseStatus.Cancelled));
            Assert.That(guest.Memory.PromisesBroken, Is.Zero);
            Assert.That(guest.ServiceSatisfactionAdjustment, Is.EqualTo(adjustment));
            Assert.That(hotel.Services.FindCase(promise.Id).Status, Is.EqualTo(ServiceStatus.Expired));
            Assert.That(CurrentReceipt(hotel).Review, Does.Not.Contain("nobody called"));
            AssertMirror(hotel);
            Assert.That(hotel.CompleteWakeUpCall(0, promise.Id).Success, Is.False);
            Assert.That(guest.Memory.PromisesBroken, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CheckoutPreservesAnAlreadyMissedOrCompletedWakeOutcomeExactlyOnce(bool completed)
        {
            var fixture = Create(); var hotel = fixture.Hotel; var guest = fixture.Guest;
            var promise = AgreeFutureWake(fixture);
            if (completed)
            { Advance(hotel, promise.DueTime); Require(hotel.CompleteWakeUpCall(0, promise.Id)); }
            else Advance(hotel, promise.DueTime + hotel.Services.Settings.WakeMissSeconds + .25f);
            int broken = guest.Memory.PromisesBroken, kept = guest.Memory.PromisesKept;
            float adjustment = guest.ServiceSatisfactionAdjustment;
            // This command isolates defensive stay-end cleanup, not natural early-departure eligibility.
            Require(hotel.DebugCheckoutGuest(guest.GuestId)); hotel.Tick(.25f); hotel.Tick(.25f);
            Assert.That(promise.Status, Is.EqualTo(completed ? PromiseStatus.Completed : PromiseStatus.Missed));
            Assert.That(guest.Memory.PromisesBroken, Is.EqualTo(broken));
            Assert.That(guest.Memory.PromisesKept, Is.EqualTo(kept));
            Assert.That(guest.ServiceSatisfactionAdjustment, Is.EqualTo(adjustment));
            AssertMirror(hotel);
        }

        [Test]
        public void StayEndCancelsDirectMoveAndPendingParcelButPreservesTheActualReclaimableItem()
        {
            var fixture = Create(); var hotel = fixture.Hotel; var guest = fixture.Guest;
            Require(hotel.DebugSetMildCold(guest.GuestId)); hotel.Tick(.25f);
            Require(hotel.DebugForceService(guest.GuestId, ServiceKind.ExtraBlanket));
            var request = hotel.Services.Cases.Single();
            Require(hotel.DiscussRoomConcern(0, guest.GuestId, request.Response.Id));
            Require(hotel.RespondToService(0, request.Id, true));
            var delivery = hotel.Services.DropOffIntent(guest.GuestId);
            Require(hotel.ForceSleep(guest.GuestId));
            Require(hotel.TakeServiceItem(0, "blanket:0"));
            var parcel = hotel.Services.FindItem("blanket:0");
            int generation = parcel.Generation;
            Require(hotel.DropOffBlanket(0, guest.GuestId, 101, delivery.Revision, generation));
            int deliveredRevision = delivery.Revision;
            Assert.That(parcel.Location, Is.EqualTo(ServiceItemLocation.AwaitingReceipt));
            // Wake only the model fixture; no tick/receipt occurs before a pending move/checkout.
            Require(hotel.ForceActivity(guest.GuestId, GuestActivity.QuietRest));
            Require(hotel.RequestGuestMove(0, guest.GuestId, 105));
            var direct = hotel.Services.DirectIntent(guest.GuestId);
            Require(hotel.DebugCheckoutGuest(guest.GuestId)); hotel.Tick(.25f);
            Assert.That(delivery.Status, Is.EqualTo(ServiceIntentStatus.Cancelled));
            Assert.That(direct.Status, Is.EqualTo(ServiceIntentStatus.Cancelled));
            Assert.That(guest.Agent.DirectServiceIntentId, Is.Null);
            Assert.That(guest.Agent.ResponseActionId, Is.Null);
            Assert.That(guest.Agent.PendingMoveRoomId, Is.Null);
            Assert.That(fixture.Rooms.Single(room => room.Profile.Id == 105).ReservedGuestId, Is.Null);
            Assert.That(hotel.Services.FindItem(parcel.Id), Is.SameAs(parcel));
            Assert.That(parcel.Location, Is.EqualTo(ServiceItemLocation.Dropped));
            Assert.That(parcel.Generation, Is.EqualTo(generation + 1));
            Assert.That(parcel.GuestId, Is.Null);
            Assert.That(guest.Memory.BlanketsDelivered, Is.Zero);
            Assert.That(guest.BlanketComfortBonus, Is.Zero);
            Assert.That(hotel.DropOffBlanket(0, guest.GuestId, 101, deliveredRevision, generation).Success, Is.False);
            Require(hotel.TakeServiceItem(0, parcel.Id)); Require(hotel.ReturnServiceItem(0, parcel.Id));
            Assert.That(parcel.Location, Is.EqualTo(ServiceItemLocation.OnShelf));
            Assert.That(hotel.MoveGuest(0, guest.GuestId, 105).Success, Is.False);
            AssertMirror(hotel);
        }
    }
}
