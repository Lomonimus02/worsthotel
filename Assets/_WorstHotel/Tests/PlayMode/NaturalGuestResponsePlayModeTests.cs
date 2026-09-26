using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace WorstHotel.Tests
{
    public sealed partial class Phase1PlayModeTests
    {
        GuestServiceConfig naturalRouteServices;

        [UnityTearDown]
        public IEnumerator DisposeNaturalRouteConfiguration()
        {
            if (naturalRouteServices) Object.Destroy(naturalRouteServices);
            naturalRouteServices = null;
            yield return null;
        }

        IEnumerator PrepareNaturalGuestRoute(int roomId)
        {
            var session = GameSession.Instance;
            // Isolate physical navigation from schedule pacing. Arrival and room/response
            // completion below come from actual presentation routes, never injected callbacks.
            waitScenarioSessionConfig = Object.Instantiate(session.config);
            waitScenarioLivingConfig = Object.Instantiate(session.config.living);
            waitScenarioSessionConfig.serviceSeconds = 3000;
            waitScenarioLivingConfig.firstArrivalSeconds = .2f;
            waitScenarioLivingConfig.arrivalJitterSeconds = 0;
            waitScenarioLivingConfig.firstActivityDelay = 1000;
            waitScenarioLivingConfig.activityDurationMin = 120;
            waitScenarioLivingConfig.activityDurationMax = 120;
            waitScenarioSessionConfig.living = waitScenarioLivingConfig;
            naturalRouteServices = Object.Instantiate(session.config.services);
            naturalRouteServices.naturalCommunicationEnabled = true;
            naturalRouteServices.eligibility = 0;
            naturalRouteServices.selfResponseObserveSeconds = 1000;
            naturalRouteServices.toleranceSeconds = 1000;
            naturalRouteServices.phoneRingSeconds = 90;
            naturalRouteServices.receptionWaitSeconds = 90;
            waitScenarioSessionConfig.services = naturalRouteServices;
            session.config = waitScenarioSessionConfig;
            session.NewGame(); ManagementUI.Instance.Close();
            var offer = session.Plan.Applications.First(item => item.Archetype.Kind == GuestKind.Business);
            int price = session.Economy.MinPrice + Mathf.RoundToInt((offer.ReferencePrice - session.Economy.MinPrice) /
                (float)session.Economy.PriceStep) * session.Economy.PriceStep;
            session.Assign(0, offer.Id, roomId, price); session.CommitPlan(0);
            var guest = session.Simulation.Guests.Single();
            session.AdvanceTime(guest.Agent.ArrivalTime + .2f);
            yield return WaitForCondition(() => guest.Agent.State == GuestAgentState.WaitingForCheckIn, 18,
                "Guest must physically walk from the exterior to reception.");
            // This labelled key adapter is the only check-in shortcut; no arrival is fabricated.
            Assert.That(CheckInWithModelKeyFixture(session, 0, guest.GuestId).Success, Is.True);
            yield return WaitForCondition(() => guest.Agent.InAssignedRoom && guest.Agent.ActivityStaged, 40,
                "Guest must open the assigned door, enter and settle before the response fixture.");
            Assert.That(Object.FindAnyObjectByType<GuestPresentation>().enabled, Is.True);
        }

        IEnumerator WaitWithActualMildCold(GuestStay guest, Func<bool> condition, float seconds, string reason)
        {
            var session = GameSession.Instance;
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                // Explicit sustained environment fixture, not an injected perception or arrival.
                session.Simulation.SetRoomTemperature(guest.RoomId, guest.Application.Archetype.Needs.PreferredTemperatureMin - 1.5f);
                yield return null;
            }
            var presentation = Object.FindAnyObjectByType<GuestPresentation>();
            presentation.TryGetGuestDebugSnapshot(guest.GuestId, out var debug);
            Assert.That(condition(), Is.True, reason + " / " + debug.PathStatus + " / " + debug.Destination);
        }

        [UnityTest]
        public IEnumerator GuestReachesActualRadiatorBeforeTurningItThenUsesRoomPhoneBeforeReceptionRings()
        {
            yield return PrepareNaturalGuestRoute(106);
            var session = GameSession.Instance;
            var simulation = session.Simulation;
            var guest = simulation.Guests.Single();
            var room = session.Rooms.Single(item => item.Profile.Id == guest.RoomId);
            var presentation = Object.FindAnyObjectByType<GuestPresentation>();
            var markers = presentation.roomMarkers.Single(item => item.roomId == guest.RoomId);
            Assert.That(presentation.TryGetGuestTransform(guest.GuestId, out var bodyRoot), Is.True);
            Assert.That(markers.radiatorAnchor && markers.radiatorTarget && markers.roomPhoneAnchor && markers.roomPhoneTarget, Is.True);
            Assert.That(session.DebugSetMildCold(guest.GuestId).Success, Is.True);
            yield return WaitWithActualMildCold(guest, () => simulation.Services.Responses.Any(item => item.GuestId == guest.GuestId), 3,
                "A real measured mild-cold episode must create a private response.");
            var response = simulation.Services.Responses.First(item => item.GuestId == guest.GuestId);
            int setting = room.RadiatorSetting;
            float load = simulation.Boiler.Load;
            Assert.That(session.DebugBeginGuestSelfResponse(guest.GuestId).Success, Is.True);
            yield return null;
            Assert.That(room.RadiatorSetting, Is.EqualTo(setting), "A scheduled walk must not remotely turn the valve.");
            Assert.That(response.SelfResponseApplied, Is.False);
            yield return WaitWithActualMildCold(guest, () => response.SelfResponseApplied, 25,
                "The real guest capsule must navigate around the bed and reach the actual valve.");
            Assert.That(HorizontalDistance(bodyRoot.position, markers.radiatorAnchor.position), Is.LessThan(.06f));
            Assert.That(room.RadiatorSetting, Is.EqualTo(setting + 1));
            Assert.That(simulation.Boiler.Load, Is.GreaterThan(load), "Guest adjustment must use the actual central-heating demand.");
            Assert.That(response.StaffActionAt, Is.LessThan(0), "A guest's own adjustment is not credited as staff intervention.");
            Assert.That(session.DebugSetMildCold(guest.GuestId).Success, Is.True);
            // Explicit private service fixture bypasses random eligibility, retaining the same
            // real cold source. Phone walking, arrival and ringing remain production behavior.
            Assert.That(session.DebugForceService(guest.GuestId, ServiceKind.ExtraBlanket).Success, Is.True);
            var request = simulation.Services.Cases.Single(item => item.GuestId == guest.GuestId && item.Active);
            Assert.That(request.Response, Is.SameAs(response));
            Assert.That(session.DebugBeginGuestContact(guest.GuestId, GuestContactChannel.Phone).Success, Is.True);
            Assert.That(simulation.Services.IncomingCall, Is.Null, "En route to the phone is not an incoming call.");
            yield return WaitWithActualMildCold(guest, () => simulation.Services.IncomingCall == response, 25,
                "The real guest must reach the authored room phone before reception can ring.");
            yield return null;
            Assert.That(HorizontalDistance(bodyRoot.position, markers.roomPhoneAnchor.position), Is.LessThan(.03f));
            Assert.That(presentation.CaptureLanGuests().Single(item => item.id == guest.GuestId).phoneVisible, Is.True);
            Assert.That(Object.FindAnyObjectByType<IncomingServicePhoneCue>().IsRinging, Is.True);
            Assert.That(simulation.Noise.Sources.Any(source => source.SourceGuestId == guest.GuestId), Is.False,
                "A hotel service call must not emit an actual personal-call source; the legacy quiet output scalar is not an emitted sound.");
            Assert.That(request.IsKnownToHotel, Is.False, "Ringing is not disclosure or acceptance.");
            Assert.That(session.DebugCancelGuestContact(response.Id).Success, Is.True);
            yield return null; yield return null;
            Assert.That(simulation.Services.IncomingCall, Is.Null);
            Assert.That(Object.FindAnyObjectByType<IncomingServicePhoneCue>().IsRinging, Is.False);
            Assert.That(guest.Agent.ResponseActionId, Is.Null);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator GuestServiceReceptionVisitKeepsRoomAndKeyThenCancellationReturnsThroughTheDoor()
        {
            yield return PrepareNaturalGuestRoute(101);
            var session = GameSession.Instance;
            var simulation = session.Simulation;
            var guest = simulation.Guests.Single();
            var presentation = Object.FindAnyObjectByType<GuestPresentation>();
            var markers = presentation.roomMarkers.Single(item => item.roomId == guest.RoomId);
            Assert.That(presentation.TryGetGuestTransform(guest.GuestId, out var bodyRoot), Is.True);
            float checkout = guest.Agent.CheckoutTime;
            var next = guest.Agent.NextPlannedActivity;
            Assert.That(session.DebugSetMildCold(guest.GuestId).Success, Is.True);
            yield return WaitWithActualMildCold(guest, () => simulation.Services.Responses.Any(item => item.GuestId == guest.GuestId), 3,
                "A real current cold episode must precede a desk visit.");
            Assert.That(session.DebugForceService(guest.GuestId, ServiceKind.ExtraBlanket).Success, Is.True);
            var request = simulation.Services.Cases.Single(item => item.GuestId == guest.GuestId && item.Active);
            var response = request.Response;
            var incident = simulation.Incidents.Items.Single(item => item.Id == response.IncidentId);
            Assert.That(session.DebugBeginGuestContact(guest.GuestId, GuestContactChannel.Reception).Success, Is.True);
            Assert.That(response.AttemptStartedAt, Is.LessThan(0));
            Assert.That(guest.Agent.State, Is.EqualTo(GuestAgentState.GoingToServiceReception));
            yield return WaitWithActualMildCold(guest, () => guest.Agent.State == GuestAgentState.WaitingAtServiceReception, 30,
                "The guest must cross the occupied door and reach a real reception queue position.");
            Assert.That(guest.Agent.CurrentLocation, Is.EqualTo(GuestLocation.Lobby));
            Assert.That(guest.Agent.InAssignedRoom, Is.False);
            Assert.That(bodyRoot.Find("Body").gameObject.activeSelf, Is.True);
            Assert.That(presentation.receptionPlaces.Any(place => HorizontalDistance(bodyRoot.position, place.position) < .04f), Is.True);
            Assert.That(markers.door.IsOpen, Is.False);
            Assert.That(incident.Active, Is.True, "Temporary absence must not resolve the linked room condition.");
            Assert.That(request.IsKnownToHotel, Is.False);
            Assert.That(simulation.Keys.Find(101).Location, Is.EqualTo(RoomKeyLocation.HeldByGuest));
            Assert.That(simulation.Keys.Find(101).GuestId, Is.EqualTo(guest.GuestId));
            Assert.That(session.Rooms.Single(item => item.Profile.Id == 101).GuestId, Is.EqualTo(guest.GuestId));
            Assert.That(session.DebugCancelGuestContact(response.Id).Success, Is.True);
            Assert.That(guest.Agent.State, Is.EqualTo(GuestAgentState.ReturningFromServiceReception));
            yield return WaitWithActualMildCold(guest, () => guest.Agent.InAssignedRoom && guest.Agent.ActivityStaged, 35,
                "Cancelling a desk contact must walk the guest back through the same door and settle in the owned room.");
            yield return new WaitForSecondsRealtime(.9f);
            Assert.That(AuthoredGuestRoute.IsOnRoomSide(bodyRoot.position, markers), Is.True);
            Assert.That(HorizontalDistance(bodyRoot.position, markers.rest.position), Is.LessThan(.03f));
            Assert.That(markers.door.IsOpen || markers.door.IsPassageOpen, Is.False);
            Assert.That(guest.Agent.ResponseActionId, Is.Null);
            Assert.That(response.Phase, Is.EqualTo(GuestResponsePhase.Cancelled));
            Assert.That(guest.Agent.CheckoutTime, Is.EqualTo(checkout));
            Assert.That(guest.Agent.NextPlannedActivity, Is.EqualTo(next));
            Assert.That(session.Rooms.Single(item => item.Profile.Id == 101).GuestId, Is.EqualTo(guest.GuestId));
            Assert.That(simulation.Keys.Find(101).Location, Is.EqualTo(RoomKeyLocation.HeldByGuest));
            LogAssert.NoUnexpectedReceived();
        }
    }
}
