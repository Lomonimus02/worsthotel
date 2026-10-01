using System;
using System.Linq;
using NUnit.Framework;

namespace WorstHotel.Tests
{
    /// <summary>Continuous model fixtures; all route/key callbacks below are explicit headless adapters.</summary>
    public sealed partial class ContinuousGuestServiceTests
    {
        sealed class Fixture
        {
            public HotelSimulation Hotel;
            public RoomState[] Rooms;
            public GuestStay Business, Other;
        }

        static void Require(CommandResult result) => Assert.That(result.Success, Is.True, result.Message);

        static Fixture Create(GuestServiceSettings serviceSettings = null)
        {
            var profiles = new[]
            {
                new GuestProfile(GuestKind.Budget, "Budget", "", 180, .85f, 18, .7f, 28, 90, .55f,
                    needs: new NeedProfile(21, 25, 18, 28, .125f, .25f, 65)),
                new GuestProfile(GuestKind.ColdSensitive, "Cold", "", 300, 1.05f, 20, 1.35f, 16, 65, .4f,
                    needs: new NeedProfile(21, 25, 18, 28, .125f, .25f, 65)),
                new GuestProfile(GuestKind.Business, "Business", "", 450, 1, 19.5f, 1, 10, 45, .25f,
                    needs: new NeedProfile(21, 25, 18, 28, .125f, .25f, 65))
            };
            var settings = new SessionSettings(profiles, Enumerable.Range(101, 6).Select(id =>
                new RoomProfile(id, "Room " + id, noise: 0, temperature: 22)),
                new BoilerSettings(baseWearPerMinute: 0, overloadWearPerMinute: 0), new EconomySettings());
            var rooms = settings.Rooms.Select(profile => new RoomState(profile)).ToArray();
            var hotel = new HotelSimulation(settings, rooms, new LivingHotelSettings(firstActivityDelay: 1000),
                services: serviceSettings ?? new GuestServiceSettings(maxCasesPerShift: 1, eligibility: 0,
                    naturalCommunicationEnabled: true), operations: new OperationsSettings());
            Require(hotel.StartOperations());
            // Since 0.6.6 the opening bed is genuinely unfinished. This service fixture
            // must prepare it through linen commands before its labelled key adapter.
            var startingBed = hotel.Housekeeping.Find(101);
            Require(hotel.PickUpLinen(0, startingBed.DirtyLinenId));
            Require(hotel.DepositDirtyLinen(0, startingBed.DirtyLinenId));
            Require(hotel.PickUpLinen(0, "clean:0"));
            Require(hotel.BeginMakeBed(0, 101, "clean:0"));
            Require(hotel.AdvanceMakeBed(0, 101, hotel.Housekeeping.Settings.MakeBedSeconds));
            var first = hotel.BookingOffers.First(offer => offer.ArrivalDay == 1 && offer.Application.Archetype.Kind == GuestKind.Business);
            var second = hotel.BookingOffers.First(offer => offer.ArrivalDay == 1 && offer.Id != first.Id);
            Require(hotel.AcceptBooking(0, first.Id, 101, settings.Economy.MinPrice));
            Require(hotel.AcceptBooking(0, second.Id, 103, settings.Economy.MinPrice));
            AdvanceTo(hotel, Math.Max(first.ArrivalAt, second.ArrivalAt) + 1);
            var business = hotel.Guests.Single(guest => guest.GuestId == first.Id);
            var other = hotel.Guests.Single(guest => guest.GuestId == second.Id);
            foreach (var guest in new[] { business, other })
            {
                // Model fixture only: physical route and key handling have separate scene tests.
                Require(hotel.SignalGuestReachedReception(guest.GuestId));
                Require(ModelKeyHandoff.CheckIn(hotel, 0, guest.GuestId));
                Require(hotel.SignalGuestReachedRoom(guest.GuestId));
            }
            return new Fixture { Hotel = hotel, Rooms = rooms, Business = business, Other = other };
        }

        static void AdvanceTo(HotelSimulation hotel, float target)
        {
            while (hotel.Elapsed < target) hotel.Tick(Math.Min(.25f, target - hotel.Elapsed));
        }

        static ServiceCase CreateWakePromise(Fixture fixture)
        {
            var hotel = fixture.Hotel;
            AdvanceTo(hotel, fixture.Business.Agent.Schedule.SleepTime - hotel.Services.Settings.ReplySeconds + 1);
            Require(hotel.DebugForceService(fixture.Business.GuestId, ServiceKind.WakeUpCall));
            var request = hotel.Services.Cases.Single(item => item.GuestId == fixture.Business.GuestId);
            Require(hotel.DebugBeginGuestContact(fixture.Business.GuestId, GuestContactChannel.Phone));
            var action = fixture.Business.Agent;
            Require(hotel.SignalGuestResponseAnchorReached(fixture.Business.GuestId, action.ResponseActionId,
                action.ResponseActionVersion, GuestResponseAnchor.RoomPhone));
            Require(hotel.AnswerIncomingServiceCall(0, request.Response.Id));
            Require(hotel.RespondToService(0, request.Id, true));
            return request;
        }

        [Test]
        public void MidnightPreservesAcceptedPromiseResponseIdentityAndRenewsOnlyTheDailyContactAllowance()
        {
            var fixture = Create(); var hotel = fixture.Hotel;
            var request = CreateWakePromise(fixture);
            var response = request.Response;
            var promise = hotel.Services.Promises.Single();
            Assert.That(request.BudgetDay, Is.EqualTo(1));
            var rejected = hotel.DebugForceService(fixture.Other.GuestId, ServiceKind.ExtraBlanket);
            Assert.That(rejected.Success, Is.False, "The first dated contact consumed the day-one allowance.");
            StringAssert.Contains("allowance", rejected.Message);

            AdvanceTo(hotel, hotel.Calendar.At(2, 0) + .25f);
            Assert.That(hotel.Services.FindCase(request.Id), Is.SameAs(request));
            Assert.That(request.Response, Is.SameAs(response));
            Assert.That(hotel.Services.FindPromise(promise.Id), Is.SameAs(promise));
            Assert.That(promise.Status, Is.EqualTo(PromiseStatus.Accepted));
            Assert.That(request.Status, Is.EqualTo(ServiceStatus.InProgress));
            Assert.That(request.BudgetDay, Is.EqualTo(1), "Existing agreements cannot be charged again after midnight.");
            Assert.That(fixture.Business.Memory.ServicesRequested, Is.EqualTo(1));

            // Explicit model fixture wakes the second guest to isolate allowance accounting;
            // this does not claim that ordinary contacts should wake sleeping guests.
            Require(hotel.ForceActivity(fixture.Other.GuestId, GuestActivity.QuietRest));
            Require(hotel.DebugSetMildCold(fixture.Other.GuestId));
            hotel.Tick(.25f);
            Require(hotel.DebugForceService(fixture.Other.GuestId, ServiceKind.ExtraBlanket));
            Require(hotel.DebugBeginGuestContact(fixture.Other.GuestId, GuestContactChannel.Phone));
            var other = fixture.Other.Agent;
            Require(hotel.SignalGuestResponseAnchorReached(fixture.Other.GuestId, other.ResponseActionId,
                other.ResponseActionVersion, GuestResponseAnchor.RoomPhone));
            var second = hotel.Services.Cases.Single(item => item.GuestId == fixture.Other.GuestId);
            Assert.That(second.BudgetDay, Is.EqualTo(2));
            Assert.That(second.BudgetCharged, Is.True);
            Assert.That(request.BudgetDay, Is.EqualTo(1));
            Assert.That(hotel.Services.Promises.Single(), Is.SameAs(promise));
        }

        [Test]
        public void DatedRoomGuardsRejectMoveOverlapAndRecheckLateCheckoutWhenAReservationIsAdded()
        {
            // A longer extension isolates the next-arrival constraint; production tuning is unchanged.
            var fixture = Create(new GuestServiceSettings(maxCasesPerShift: 1, eligibility: 0,
                lateCheckoutExtension: 200, lateCheckoutRequestLead: 240, naturalCommunicationEnabled: true));
            var hotel = fixture.Hotel; var guest = fixture.Business;
            var laterToday = hotel.BookingOffers.Where(item => item.ArrivalDay == 1 && item.ArrivalAt > hotel.Elapsed)
                .OrderByDescending(item => item.ArrivalAt).First();
            Require(hotel.AcceptBooking(0, laterToday.Id, 105, 180));
            var destination = fixture.Rooms.Single(room => room.Profile.Id == 105);
            Assert.That(destination.ReservedGuestId, Is.Null.Or.Empty, "Future reservations do not occupy the physical room yet.");
            int revision = hotel.EventRevision;
            Assert.That(hotel.RequestGuestMove(0, guest.GuestId, 105).Success, Is.False);
            Assert.That(hotel.EventRevision, Is.EqualTo(revision));
            Assert.That(guest.RoomId, Is.EqualTo(101));
            Assert.That(guest.Agent.PendingMoveRoomId, Is.Null);
            Assert.That(hotel.Keys.Find(101).GuestId, Is.EqualTo(guest.GuestId));
            Assert.That(hotel.Keys.Find(105).Location, Is.EqualTo(RoomKeyLocation.OnRack));

            Require(hotel.CancelBooking(0, laterToday.Id));
            Require(ModelKeyHandoff.MoveGuest(hotel, 0, guest.GuestId, 105));
            Assert.That(hotel.FindReservation(guest.GuestId).RoomId, Is.EqualTo(105),
                "The committed key exchange updates the booking before another simulation tick or snapshot.");
            // Explicit headless physical-boundary adapters, not claims about navigation.
            Require(hotel.SignalGuestVacatedRoom(guest.GuestId, 101));
            Require(hotel.SignalGuestReachedRoom(guest.GuestId));
            AdvanceTo(hotel, guest.Agent.Schedule.WakeTime + .25f);
            Require(hotel.DebugForceService(guest.GuestId, ServiceKind.LateCheckout));
            var request = hotel.Services.Cases.Single(item => item.GuestId == guest.GuestId);
            float originalCheckout = guest.Agent.CheckoutTime;
            Assert.That(request.DueTime, Is.EqualTo(originalCheckout + 200));
            Require(hotel.DebugBeginGuestContact(guest.GuestId, GuestContactChannel.Phone));
            Require(hotel.SignalGuestResponseAnchorReached(guest.GuestId, guest.Agent.ResponseActionId,
                guest.Agent.ResponseActionVersion, GuestResponseAnchor.RoomPhone));
            Require(hotel.AnswerIncomingServiceCall(0, request.Response.Id));

            var nextArrival = hotel.BookingOffers.Where(item => item.ArrivalDay == 2).OrderBy(item => item.ArrivalAt).First();
            Require(hotel.AcceptBooking(1, nextArrival.Id, 105, 180));
            Require(hotel.RespondToService(0, request.Id, true));
            Assert.That(guest.Agent.CheckoutTime, Is.EqualTo(nextArrival.ArrivalAt - 15).Within(.001f));
            Assert.That(guest.Agent.CheckoutTime, Is.GreaterThan(originalCheckout));
            Assert.That(guest.Agent.CheckoutTime, Is.LessThan(request.DueTime),
                "A booking added after the request must be rechecked at acceptance.");
            Assert.That(request.Status, Is.EqualTo(ServiceStatus.Fulfilled));
            Assert.That(destination.GuestId, Is.EqualTo(guest.GuestId));
            Assert.That(hotel.Keys.Find(105).GuestId, Is.EqualTo(guest.GuestId));
        }

        [Test]
        public void CalendarRefillNeverReclaimsDeliveredActiveBlanketOrHeldAndDroppedStock()
        {
            var fixture = Create(); var hotel = fixture.Hotel;
            Require(hotel.TakeServiceItem(0, "blanket:0"));
            Require(hotel.DeliverBlanket(0, fixture.Business.GuestId));
            Require(hotel.TakeServiceItem(0, "blanket:1"));
            Require(hotel.TakeServiceItem(1, "blanket:2"));
            Require(hotel.DropServiceItem(1, "blanket:2"));
            var stock = hotel.Services.Items.Where(item => item.Kind == ServiceItemKind.Blanket).ToArray();
            var generations = stock.Select(item => item.Generation).ToArray();
            float bonus = fixture.Business.BlanketComfortBonus;
            var model = fixture.Business.Agent;
            AdvanceTo(hotel, hotel.Calendar.At(2, 0) + .25f);
            hotel.Services.BeginOperatingDay(2, hotel.Calendar.At(4, 0));

            Assert.That(fixture.Business.Agent, Is.SameAs(model));
            Assert.That(fixture.Business.BlanketComfortBonus, Is.EqualTo(bonus));
            Assert.That(fixture.Business.Memory.BlanketsDelivered, Is.EqualTo(1));
            Assert.That(hotel.Services.Items.Where(item => item.Kind == ServiceItemKind.Blanket), Is.EqualTo(stock));
            Assert.That(stock.Select(item => item.Generation), Is.EqualTo(generations));
            Assert.That(stock[0].Location, Is.EqualTo(ServiceItemLocation.Delivered));
            Assert.That(stock[0].GuestId, Is.EqualTo(fixture.Business.GuestId));
            Assert.That(stock[1].Location, Is.EqualTo(ServiceItemLocation.HeldByPlayer));
            Assert.That(stock[1].PlayerId, Is.EqualTo(0));
            Assert.That(stock[2].Location, Is.EqualTo(ServiceItemLocation.Dropped));
            Assert.That(stock[2].LastPlayerId, Is.EqualTo(1));
            Assert.That(hotel.Services.BlanketsAvailable, Is.Zero);

            hotel.EnableReadOnlyMirror();
            hotel.Services.BeginOperatingDay(3, hotel.Calendar.At(5, 0));
            Assert.That(hotel.Services.LastRefillDay, Is.EqualTo(2));
            Assert.That(stock.Select(item => item.Generation), Is.EqualTo(generations));
        }
    }
}
