using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace WorstHotel.Tests
{
    public sealed partial class ContinuousGuestServiceTests
    {
        const BindingFlags HiddenInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        // Only the root retention coordinator calls these internal APIs in production.
        // These direct boundary tests supply explicit owner/identity/incident closure sets.
        static void PruneServices(HotelSimulation hotel, IEnumerable<string> owners, IEnumerable<string> identities,
            IEnumerable<string> incidents) => typeof(GuestServiceSystem).GetMethod("PruneCompletedStays", HiddenInstance)
            .Invoke(hotel.Services, new object[] { new HashSet<string>(owners), new HashSet<string>(identities), new HashSet<string>(incidents) });
        static void PruneSchedules(HotelSimulation hotel, IEnumerable<string> identities) =>
            typeof(GuestScheduleSystem).GetMethod("PruneCompletedStays", HiddenInstance)
                .Invoke(hotel.Schedules, new object[] { new HashSet<string>(identities) });

        static ServiceCase ColdRequest(Fixture fixture, GuestStay guest)
        {
            Require(fixture.Hotel.DebugSetMildCold(guest.GuestId));
            fixture.Hotel.Tick(.25f);
            Require(fixture.Hotel.DebugForceService(guest.GuestId, ServiceKind.ExtraBlanket));
            return fixture.Hotel.Services.Cases.Single(item => item.GuestId == guest.GuestId && item.Kind == ServiceKind.ExtraBlanket);
        }

        static void HearByPhone(HotelSimulation hotel, GuestStay guest, ServiceCase request)
        {
            Require(hotel.DebugBeginGuestContact(guest.GuestId, GuestContactChannel.Phone));
            Require(hotel.SignalGuestResponseAnchorReached(guest.GuestId, guest.Agent.ResponseActionId,
                guest.Agent.ResponseActionVersion, GuestResponseAnchor.RoomPhone));
            Require(hotel.AnswerIncomingServiceCall(0, request.Response.Id));
        }

        [Test]
        public void PruningKeepsActivePromiseAndRetainedIncidentLinksButDropsUnownedTerminalBranches()
        {
            var fixture = Create(new GuestServiceSettings(maxCasesPerShift: 32, eligibility: 0, naturalCommunicationEnabled: true));
            var hotel = fixture.Hotel; var oldGuest = fixture.Other;
            var oldRequest = ColdRequest(fixture, oldGuest);
            HearByPhone(hotel, oldGuest, oldRequest);
            Require(hotel.RespondToService(0, oldRequest.Id, false));
            Require(hotel.TakeServiceItem(0, "blanket:0"));
            Require(hotel.DeliverBlanket(0, oldGuest.GuestId));
            var blanket = hotel.Services.FindItem("blanket:0");
            var oldResponse = oldRequest.Response;
            string incidentId = oldResponse.IncidentId;
            // Model-only early departure, followed by explicit physical-boundary adapters.
            Require(hotel.DebugCheckoutGuest(oldGuest.GuestId));
            hotel.Tick(hotel.LivingSettings.CheckoutInteractionSeconds + .25f);
            Require(hotel.SignalGuestVacatedRoom(oldGuest.GuestId, oldGuest.RoomId));
            Require(hotel.SignalGuestLeft(oldGuest.GuestId));
            var currentRequest = CreateWakePromise(fixture);
            var promise = hotel.Services.Promises.Single();
            var currentAgent = fixture.Business.Agent;
            var owners = new[] { fixture.Business.GuestId };
            var identities = hotel.Guests.Select(guest => guest.GuestId).ToArray();

            PruneServices(hotel, owners, identities, new[] { incidentId });
            Assert.That(hotel.Services.FindCase(oldRequest.Id), Is.SameAs(oldRequest), "A retained current incident keeps its bidirectional service link.");
            Assert.That(hotel.Services.FindResponse(oldResponse.Id), Is.SameAs(oldResponse));
            Assert.That(hotel.Services.FindPromise(promise.Id), Is.SameAs(promise));
            Assert.That(promise.Status, Is.EqualTo(PromiseStatus.Accepted));
            Assert.That(hotel.Services.FindCase(currentRequest.Id), Is.SameAs(currentRequest));
            Assert.That(hotel.Services.FindItem("blanket:0"), Is.SameAs(blanket));
            Assert.That(blanket.Location, Is.EqualTo(ServiceItemLocation.Delivered), "Pruning consumed stock is not a refill.");
            Assert.That(blanket.GuestId, Is.Null);
            Assert.That(hotel.Services.FindItem("luggage:" + oldGuest.GuestId), Is.Null);

            // The coordinator has decided this old incident is also leaving the graph.
            PruneServices(hotel, owners, identities, Array.Empty<string>());
            Assert.That(hotel.Services.FindCase(oldRequest.Id), Is.Null);
            Assert.That(hotel.Services.FindResponse(oldResponse.Id), Is.Null);
            Assert.That(hotel.Services.FindPromise(promise.Id), Is.SameAs(promise));
            PruneSchedules(hotel, owners);
            Assert.That(hotel.Schedules.Items.Select(item => item.GuestId), Is.EqualTo(owners));
            Assert.That(fixture.Business.Agent, Is.SameAs(currentAgent));
            Assert.That(fixture.Business.Agent.Schedule, Is.SameAs(hotel.Schedules.Items.Single()));

            hotel.EnableReadOnlyMirror();
            PruneServices(hotel, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
            PruneSchedules(hotel, Array.Empty<string>());
            Assert.That(hotel.Services.FindCase(currentRequest.Id), Is.SameAs(currentRequest));
            Assert.That(hotel.Services.FindPromise(promise.Id), Is.SameAs(promise));
            Assert.That(hotel.Schedules.Items.Count, Is.EqualTo(1));
        }

        [Test]
        public void DepartedDroppedLuggageCanBePhysicallyReclaimedAndStoredWithoutAnAgreementOrBonus()
        {
            var fixture = Create(); var hotel = fixture.Hotel;
            Require(hotel.DebugMarkRoomDirty(105));
            Require(hotel.DebugSpawnGuest(GuestKind.Budget, 105));
            hotel.Tick(1.25f);
            var guest = hotel.Guests.Single(item => item.RoomId == 105);
            Require(hotel.SignalGuestReachedReception(guest.GuestId));
            Require(hotel.DebugForceService(guest.GuestId, ServiceKind.LuggageStorage));
            var request = hotel.Services.Cases.Single(item => item.GuestId == guest.GuestId);
            Require(hotel.DebugBeginGuestContact(guest.GuestId, GuestContactChannel.Reception));
            Require(hotel.TalkToServiceGuest(0, guest.GuestId, request.Response.Id));
            Require(hotel.RespondToService(0, request.Id, true));
            string id = "luggage:" + guest.GuestId;
            Require(hotel.TakeServiceItem(0, id));
            Require(hotel.DropServiceItem(0, id));
            Require(hotel.DebugCheckoutGuest(guest.GuestId));
            Require(hotel.SignalGuestLeft(guest.GuestId));
            hotel.Tick(.25f);
            int fulfilled = guest.Memory.ServicesFulfilled, stored = guest.Memory.LuggageStored;
            float score = guest.ServiceSatisfactionAdjustment;
            var owners = new[] { fixture.Business.GuestId, fixture.Other.GuestId };
            PruneServices(hotel, owners, hotel.Guests.Select(item => item.GuestId), Array.Empty<string>());
            var suitcase = hotel.Services.FindItem(id);
            Assert.That(suitcase, Is.Not.Null);
            Assert.That(suitcase.Location, Is.EqualTo(ServiceItemLocation.Dropped));
            Assert.That(hotel.Services.FindCase(request.Id), Is.Null, "The departed guest no longer needs a retained agreement to return lost property.");
            Require(hotel.TakeServiceItem(1, id));
            Assert.That(hotel.StoreLuggage(0, guest.GuestId).Success, Is.False);
            Assert.That(suitcase.PlayerId, Is.EqualTo(1));
            Require(hotel.StoreLuggage(1, guest.GuestId));
            Assert.That(suitcase.Location, Is.EqualTo(ServiceItemLocation.Stored));
            Assert.That(suitcase.PlayerId, Is.Null);
            Assert.That(guest.Memory.ServicesFulfilled, Is.EqualTo(fulfilled));
            Assert.That(guest.Memory.LuggageStored, Is.EqualTo(stored));
            Assert.That(guest.ServiceSatisfactionAdjustment, Is.EqualTo(score));
            PruneServices(hotel, owners, owners, Array.Empty<string>());
            Assert.That(hotel.Services.FindItem(id), Is.Null, "Putting the actual suitcase in storage releases its retention pin.");
            Assert.That(hotel.Services.Items.Count(item => item.Kind != ServiceItemKind.Luggage), Is.EqualTo(6));
        }

        static T ConstructHidden<T>(params object[] args) => (T)Activator.CreateInstance(typeof(T), HiddenInstance, null, args, null);
        static List<T> HiddenList<T>(GuestServiceSystem services, string name) =>
            (List<T>)typeof(GuestServiceSystem).GetField(name, HiddenInstance).GetValue(services);

        [TestCase(32, true)]
        [TestCase(255, true)]
        [TestCase(256, false)]
        public void ContinuousServiceAdmissionUsesTheBoundedRetainedCaseCapacity(int count, bool admitted)
        {
            var fixture = Create(); var hotel = fixture.Hotel; var guest = fixture.Other;
            Require(hotel.DebugSetMildCold(guest.GuestId)); hotel.Tick(.25f);
            // Container saturation fixture: inert historical records isolate admission limits.
            // The new request itself still needs the actual measured cold cause above.
            var entries = HiddenList<ServiceCase>(hotel.Services, "cases");
            for (int index = 0; index < count; index++)
            {
                var old = ConstructHidden<ServiceCase>("history-" + index, "old-" + index, 101, ServiceKind.ExtraBlanket,
                    0f, 1f, "room/101/temperature", 101, "Historical capacity fixture");
                typeof(ServiceCase).GetProperty(nameof(ServiceCase.Status)).SetValue(old, ServiceStatus.Expired);
                entries.Add(old);
            }
            var protectedHistory = entries.ToArray();
            Assert.That(hotel.DebugForceService(guest.GuestId, ServiceKind.ExtraBlanket).Success, Is.EqualTo(admitted));
            Assert.That(entries.Count, Is.EqualTo(count + (admitted ? 1 : 0)));
            Assert.That(entries.Take(count), Is.EqualTo(protectedHistory));
        }

        [Test]
        public void SaturatedResponseStorageRejectsAdmissionWithoutNullResponseOrHistoryEviction()
        {
            var fixture = Create(); var hotel = fixture.Hotel; var guest = fixture.Other;
            Require(hotel.DebugSetMildCold(guest.GuestId)); hotel.Tick(.25f);
            var incident = hotel.Incidents.Items.Single(item => item.GuestId == guest.GuestId && item.Active);
            var entries = HiddenList<GuestResponse>(hotel.Services, "responses");
            entries.Clear();
            // Inert historical storage fixtures do not simulate 512 physical conversations.
            for (int index = 0; index < 512; index++) entries.Add(ConstructHidden<GuestResponse>(
                "history-" + index, "old-" + index, 101, "room/101/temperature", "old-incident-" + index, 1, null, 0f));
            var history = entries.ToArray(); int revision = hotel.EventRevision;
            Assert.DoesNotThrow(() => Assert.That(hotel.DebugForceService(guest.GuestId, ServiceKind.ExtraBlanket).Success, Is.False));
            Assert.That(hotel.Services.Cases, Is.Empty);
            Assert.That(hotel.Services.Responses, Is.EqualTo(history));
            Assert.That(hotel.Services.Responses.All(item => item != null), Is.True);
            Assert.That(hotel.EventRevision, Is.EqualTo(revision));
            Assert.That(incident.Response, Is.Null, "No absent response may be published as a causal link.");
        }
    }
}
