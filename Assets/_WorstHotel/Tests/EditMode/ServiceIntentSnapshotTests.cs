using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace WorstHotel.Tests
{
    public sealed class ServiceIntentSnapshotTests
    {
        sealed class Fixture
        {
            public HotelSimulation Hotel;
            public RoomState[] Rooms;
            public string BlanketGuestId, DirectGuestId;
            public GuestStay BlanketGuest => Hotel.Guests.Single(guest => guest.GuestId == BlanketGuestId);
            public GuestStay DirectGuest => Hotel.Guests.Single(guest => guest.GuestId == DirectGuestId);
            public GuestServiceIntent Drop => Hotel.Services.Intents.Single(intent => intent.Kind == ServiceIntentKind.DropOff);
            public GuestServiceIntent Direct => Hotel.Services.Intents.Single(intent => intent.Kind == ServiceIntentKind.Direct);
        }

        static void Require(CommandResult result) => Assert.That(result.Success, Is.True, result.Message);

        static Fixture Create(bool continuous = true)
        {
            var needs = new NeedProfile(21, 25, 18, 28, .125f, .25f, 65);
            var profiles = new[]
            {
                new GuestProfile(GuestKind.Budget, "Budget", "", 180, .85f, 18, .7f, 28, 90, .55f, needs: needs),
                new GuestProfile(GuestKind.ColdSensitive, "Cold", "", 300, 1.05f, 20, 1.35f, 16, 65, .4f, needs: needs),
                new GuestProfile(GuestKind.Business, "Business", "", 450, 1, 19.5f, 1, 10, 45, .25f, needs: needs)
            };
            var settings = new SessionSettings(profiles, Enumerable.Range(101, 6).Select(id =>
                new RoomProfile(id, "Room " + id, noise: 0, temperature: 22.5f)),
                new BoilerSettings(safeLoad: 100, baseWearPerMinute: 0, overloadWearPerMinute: 0), new EconomySettings());
            var rooms = settings.Rooms.Select(profile => new RoomState(profile)).ToArray();
            var hotel = new HotelSimulation(settings, rooms,
                new LivingHotelSettings(firstArrivalSeconds: .2f, arrivalSpacingSeconds: .2f, arrivalJitterSeconds: 0,
                    firstActivityDelay: 10000, quietDurationMin: 10000, quietDurationMax: 10000,
                    awayDurationMin: 10000, awayDurationMax: 10000),
                services: new GuestServiceSettings(eligibility: 0, naturalCommunicationEnabled: true),
                infrastructure: new RoomInfrastructureSettings(lampWearPerSecond: 0),
                operations: continuous ? new OperationsSettings() : null);
            var fixture = new Fixture { Hotel = hotel, Rooms = rooms };
            if (continuous) Require(hotel.StartOperations());
            else
            {
                var offer = new BookingApplication("legacy-intent-wire", "Legacy guest", profiles[0], 180);
                Require(hotel.StartShift(new[] { new BookingAssignment(101, offer.Id, 180, 0) }, new[] { offer }));
                AdvanceTo(fixture, 1);
            }
            return fixture;
        }

        static void AdvanceTo(Fixture fixture, float target)
        {
            while (fixture.Hotel.Elapsed < target)
            {
                fixture.Hotel.Tick(Math.Min(.25f, target - fixture.Hotel.Elapsed));
                // Explicit headless route adapters, using the same key and room ownership commands.
                foreach (var guest in fixture.Hotel.Guests.Where(guest => guest.Agent.State == GuestAgentState.Arriving).ToArray())
                {
                    Require(fixture.Hotel.SignalGuestReachedReception(guest.GuestId));
                    Require(ModelKeyHandoff.CheckIn(fixture.Hotel, 0, guest.GuestId));
                    Require(fixture.Hotel.SignalGuestReachedRoom(guest.GuestId));
                    Require(fixture.Hotel.ForceActivity(guest.GuestId, GuestActivity.QuietRest));
                }
            }
        }

        static Fixture PendingHost(bool justBeforeMidnight = false, bool additionalRoomCause = false)
        {
            var fixture = Create(); var hotel = fixture.Hotel;
            var budget = hotel.BookingOffers.First(offer => offer.ArrivalDay == 1 && offer.Application.Archetype.Kind == GuestKind.Budget);
            var business = hotel.BookingOffers.First(offer => offer.ArrivalDay == 1 && offer.Application.Archetype.Kind == GuestKind.Business);
            fixture.BlanketGuestId = budget.Id; fixture.DirectGuestId = business.Id;
            Require(hotel.AcceptBooking(0, budget.Id, 101, budget.Application.ReferencePrice));
            Require(hotel.AcceptBooking(0, business.Id, 103, business.Application.ReferencePrice));
            AdvanceTo(fixture, Math.Max(budget.ArrivalAt, business.ArrivalAt) + 1);
            if (justBeforeMidnight)
            {
                AdvanceTo(fixture, hotel.Calendar.At(2, 0) - 5);
                // Diagnostic late-night availability isolates a calendar boundary. This does
                // not claim the production scheduler normally wakes these guests for service.
                Require(hotel.ForceActivity(budget.Id, GuestActivity.QuietRest));
                Require(hotel.ForceActivity(business.Id, GuestActivity.QuietRest));
            }
            Require(hotel.DebugSetMildCold(budget.Id));
            hotel.Tick(.25f);
            Require(hotel.DebugForceService(budget.Id, ServiceKind.ExtraBlanket));
            Require(hotel.DebugBeginGuestContact(budget.Id, GuestContactChannel.Phone));
            var response = hotel.Services.FindResponse(fixture.BlanketGuest.Agent.ResponseActionId);
            // Acknowledgement represents a physically reached room-phone anchor in this model test.
            Require(hotel.SignalGuestResponseAnchorReached(budget.Id, response.Id, response.ActionVersion, GuestResponseAnchor.RoomPhone));
            Require(hotel.AnswerIncomingServiceCall(0, response.Id));
            Require(hotel.RespondToService(0, response.ServiceCaseId, true));
            if (additionalRoomCause)
            {
                // A second real observation for this same guest lets malformed-wire tests
                // distinguish cross-cause corruption from merely an unknown or wrong owner.
                Require(hotel.BreakRoomLamp(101));
                hotel.Tick(.25f);
                hotel.Tick(.25f);
            }
            Require(hotel.ForceLeaveRoom(budget.Id));
            Require(hotel.SignalGuestLeftRoom(budget.Id));
            Require(hotel.TakeServiceItem(0, "blanket:0"));
            var parcel = hotel.Services.FindItem("blanket:0");
            Require(hotel.DropOffBlanket(0, budget.Id, 101, fixture.Drop.Revision, parcel.Generation));
            Require(hotel.RequestGuestMove(0, business.Id, 102));
            Assert.That(fixture.Direct.Status, Is.EqualTo(ServiceIntentStatus.Active));
            Assert.That(fixture.Drop.Status, Is.EqualTo(ServiceIntentStatus.AwaitingReceipt));
            Assert.That(fixture.BlanketGuest.Memory.BlanketsDelivered, Is.Zero);
            return fixture;
        }

        static HotelModelSnapshot Wire(HotelSimulation hotel, long sequence) =>
            JsonUtility.FromJson<HotelModelSnapshot>(JsonUtility.ToJson(hotel.CaptureSnapshot(404, sequence)));
        static string State(HotelSimulation hotel) => JsonUtility.ToJson(hotel.CaptureSnapshot(404, 1));
        static void Apply(HotelSimulation host, HotelSimulation mirror, long sequence)
        {
            Require(mirror.ApplySnapshot(Wire(host, sequence)));
            Assert.That(State(mirror), Is.EqualTo(State(host)));
        }

        [Test]
        public void DirectWaitAndPhysicalParcelCrossMidnightAndJsonWithoutEarlyReceiptOrLostGuestIdentity()
        {
            var fixture = PendingHost(true); var host = fixture.Hotel;
            var mirror = Create().Hotel; mirror.EnableReadOnlyMirror();
            string dropId = fixture.Drop.Id, directId = fixture.Direct.Id, itemId = fixture.Drop.ItemId;
            int generation = fixture.Drop.ItemGeneration;
            float deadline = fixture.Direct.Deadline;
            Apply(host, mirror, 1);
            var guest = mirror.Guests.Single(item => item.GuestId == fixture.BlanketGuestId);
            var agent = guest.Agent;

            AdvanceTo(fixture, host.Calendar.At(2, 0) + 1);
            Apply(host, mirror, 2);

            Assert.That(mirror.CalendarDay, Is.EqualTo(2));
            Assert.That(mirror.Guests.Single(item => item.GuestId == guest.GuestId), Is.SameAs(guest));
            Assert.That(guest.Agent, Is.SameAs(agent));
            Assert.That(mirror.Services.FindIntent(directId).Status, Is.EqualTo(ServiceIntentStatus.Active));
            Assert.That(mirror.Services.FindIntent(directId).Deadline, Is.EqualTo(deadline));
            Assert.That(mirror.Guests.Single(item => item.GuestId == fixture.DirectGuestId).Agent.DirectServiceIntentId, Is.EqualTo(directId));
            Assert.That(mirror.Services.FindIntent(dropId).Status, Is.EqualTo(ServiceIntentStatus.AwaitingReceipt));
            Assert.That(mirror.Services.FindItem(itemId).Location, Is.EqualTo(ServiceItemLocation.AwaitingReceipt));
            Assert.That(mirror.Services.FindItem(itemId).Generation, Is.EqualTo(generation));
            Assert.That(guest.BlanketComfortBonus, Is.Zero);
            Assert.That(guest.Memory.BlanketsDelivered, Is.Zero);
            string before = State(mirror);
            Assert.That(mirror.DropOffBlanket(0, guest.GuestId, 101, fixture.Drop.Revision, generation).Success, Is.False);
            Assert.That(mirror.CancelGuestMove(0, fixture.DirectGuestId).Success, Is.False);
            mirror.Tick(100);
            Assert.That(State(mirror), Is.EqualTo(before));

            Require(host.ForceReturnRoom(fixture.BlanketGuestId));
            Require(host.SignalGuestReturnedRoom(fixture.BlanketGuestId));
            host.Tick(.25f);
            Apply(host, mirror, 3);
            Assert.That(mirror.Services.FindIntent(dropId).Status, Is.EqualTo(ServiceIntentStatus.Completed));
            Assert.That(mirror.Services.FindIntent(dropId).ReceivedAt, Is.GreaterThanOrEqualTo(mirror.Services.FindIntent(dropId).DeliveredAt));
            Assert.That(mirror.Services.FindItem(itemId).Location, Is.EqualTo(ServiceItemLocation.Delivered));
            Assert.That(guest.Memory.BlanketsDelivered, Is.EqualTo(1));
            Assert.That(guest.Memory.ServicesFulfilled, Is.EqualTo(1));
            Apply(host, mirror, 4);
            Assert.That(guest.Memory.BlanketsDelivered, Is.EqualTo(1));
        }

        [TestCase("owner")]
        [TestCase("revision")]
        [TestCase("deadline")]
        [TestCase("agent-link")]
        [TestCase("missing-case-purpose")]
        [TestCase("item-generation")]
        [TestCase("duplicate-parcel-owner")]
        [TestCase("wrong-item-recipient")]
        [TestCase("remote-awaiting-receipt")]
        [TestCase("unplaced-drop-with-parcel")]
        [TestCase("remote-response-cause")]
        [TestCase("agreement-cause")]
        public void InvalidIntentRelationsRejectAtomicallyWithoutConsumingTheSnapshotSequence(string defect)
        {
            bool compareCauses = defect == "remote-response-cause" || defect == "agreement-cause";
            var fixture = PendingHost(additionalRoomCause: compareCauses); var host = fixture.Hotel;
            var mirror = Create().Hotel; mirror.EnableReadOnlyMirror();
            Apply(host, mirror, 1);
            string before = State(mirror);
            var originalGuest = mirror.Guests.Single(item => item.GuestId == fixture.BlanketGuestId);
            var packet = Wire(host, 2);
            var direct = packet.ServiceLayer.Intents.Single(intent => intent.Kind == ServiceIntentKind.Direct);
            var drop = packet.ServiceLayer.Intents.Single(intent => intent.Kind == ServiceIntentKind.DropOff);
            switch (defect)
            {
                case "owner": direct.GuestId = "unknown-intent-owner"; break;
                case "revision": direct.Revision = 0; break;
                case "deadline": direct.Deadline = float.NaN; break;
                case "agent-link": packet.Guests.Single(guest => guest.Application.Id == direct.GuestId).Agent.DirectServiceIntentId = ""; break;
                case "missing-case-purpose": direct.Purpose = ServiceIntentPurpose.ServiceDecision; direct.CaseId = ""; break;
                case "item-generation": drop.ItemGeneration++; break;
                case "duplicate-parcel-owner":
                    var duplicate = JsonUtility.FromJson<ServiceIntentSnapshot>(JsonUtility.ToJson(drop));
                    duplicate.Id += "/duplicate";
                    packet.ServiceLayer.Intents = packet.ServiceLayer.Intents.Concat(new[] { duplicate }).ToArray();
                    break;
                case "wrong-item-recipient": packet.ServiceLayer.Items.Single(item => item.Id == drop.ItemId).GuestId = fixture.DirectGuestId; break;
                case "remote-awaiting-receipt":
                    packet.ServiceLayer.Intents.Single(intent => intent.Kind == ServiceIntentKind.Remote).Status = ServiceIntentStatus.AwaitingReceipt;
                    break;
                case "unplaced-drop-with-parcel":
                    drop.Status = ServiceIntentStatus.Active;
                    // Make the stock record an ordinary unassigned floor item, so rejection
                    // specifically requires an unplaced intent not to claim a delivery.
                    var loose = packet.ServiceLayer.Items.Single(item => item.Id == drop.ItemId);
                    loose.Location = ServiceItemLocation.Dropped; loose.GuestId = ""; loose.RoomId = 0;
                    break;
                case "remote-response-cause":
                case "agreement-cause":
                    var cold = packet.ServiceLayer.Intents.Single(intent => intent.Kind == ServiceIntentKind.Remote && intent.IncidentId == drop.IncidentId);
                    var lamp = packet.ServiceLayer.Intents.Single(intent => intent.Kind == ServiceIntentKind.Remote && intent.IncidentId != drop.IncidentId);
                    Assert.That(lamp.GuestId, Is.EqualTo(cold.GuestId));
                    Assert.That(lamp.ResponseId, Is.Not.Null.And.Not.Empty);
                    Assert.That(packet.Incidents.Single(incident => incident.Id == lamp.IncidentId).Cause.SourceEntityId, Is.EqualTo("room/101/lamp"));
                    if (defect == "remote-response-cause") cold.ResponseId = lamp.ResponseId;
                    else drop.IncidentId = lamp.IncidentId;
                    break;
            }

            Assert.That(mirror.ApplySnapshot(packet).Success, Is.False, defect);
            Assert.That(State(mirror), Is.EqualTo(before), defect);
            Assert.That(mirror.AppliedSnapshotSequence, Is.EqualTo(1));
            Assert.That(mirror.Guests.Single(item => item.GuestId == originalGuest.GuestId), Is.SameAs(originalGuest));
            // The failed packet must not burn sequence 2 or partially install relations.
            Apply(host, mirror, 2);
            Assert.That(mirror.AppliedSnapshotSequence, Is.EqualTo(2));
        }

        [Test]
        public void LegacyShiftWithEmptyIntentsStillRoundTrips()
        {
            var host = Create(false).Hotel;
            var mirror = Create(false).Hotel; mirror.EnableReadOnlyMirror();
            var packet = Wire(host, 1);
            Assert.That(packet.HasOperations, Is.False);
            Assert.That(packet.ServiceLayer.Intents, Is.Empty);
            Require(mirror.ApplySnapshot(packet));
            Assert.That(State(mirror), Is.EqualTo(State(host)));
            Assert.That(mirror.Services.Intents, Is.Empty);
        }
    }
}
