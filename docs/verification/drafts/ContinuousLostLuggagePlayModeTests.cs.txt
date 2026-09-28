using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace WorstHotel.Tests
{
    public sealed partial class Phase1PlayModeTests
    {
        GuestServiceConfig lostLuggageServices;

        [UnityTearDown]
        public IEnumerator DisposeLostLuggageConfiguration()
        {
            if (lostLuggageServices) Object.Destroy(lostLuggageServices);
            lostLuggageServices = null;
            yield return null;
        }

        [UnityTest, Category("ContinuousOperations")]
        public IEnumerator ActualCarriedSuitcaseSurvivesOwnerExitAndCanBePlacedAsLostPropertyWithoutServiceCredit()
        {
            var session = GameSession.Instance; var actor = bootstrap.Players[0];
            waitScenarioSessionConfig = Object.Instantiate(session.config);
            waitScenarioSessionConfig.continuousOperations = true;
            waitScenarioSessionConfig.hotelDaySeconds = 720;
            waitScenarioSessionConfig.openingHour = 8;
            waitScenarioSessionConfig.reportHour = 6;
            lostLuggageServices = Object.Instantiate(session.config.services);
            lostLuggageServices.eligibility = 0;
            lostLuggageServices.naturalCommunicationEnabled = true;
            waitScenarioSessionConfig.services = lostLuggageServices;
            session.config = waitScenarioSessionConfig;
            session.NewGame(); ManagementUI.Instance.Close();
            var hotel = session.Simulation;
            Assert.That(hotel.DebugMarkRoomDirty(106).Success, Is.True);
            var offer = hotel.BookingOffers.Where(item => item.ArrivalDay == 1).OrderBy(item => item.ArrivalAt).First();
            Assert.That(hotel.AcceptBooking(0, offer.Id, 106, session.Economy.MinPrice).Success, Is.True);
            // Only idle calendar time is skipped. Arrival and departure use real authored routes.
            session.AdvanceTime(offer.ArrivalAt + .25f);
            var guest = hotel.Guests.Single();
            yield return WaitForCondition(() => guest.Agent.State == GuestAgentState.WaitingForCheckIn, 30,
                "The luggage owner must actually walk to reception before a suitcase can be collected.");
            Assert.That(hotel.DebugForceService(guest.GuestId, ServiceKind.LuggageStorage).Success, Is.True);
            var request = hotel.Services.Cases.Single(item => item.GuestId == guest.GuestId);
            // Model conversation adapter isolates the actual suitcase input, carry and placement.
            Assert.That(hotel.DebugBeginGuestContact(guest.GuestId, GuestContactChannel.Reception).Success, Is.True);
            Assert.That(hotel.TalkToServiceGuest(0, guest.GuestId, request.Response.Id).Success, Is.True);
            Assert.That(hotel.RespondToService(0, request.Id, true).Success, Is.True);
            var suitcase = PhysicalSupply("luggage:" + guest.GuestId);
            var presentation = Object.FindAnyObjectByType<GuestPresentation>();
            Assert.That(presentation.TryGetGuestTransform(guest.GuestId, out var guestBody), Is.True);
            // Reception arrival is reported before the guest finishes their normal turn.
            // Wait for the real idle pose and suitcase dock before choosing its outer side;
            // using the arrival-facing right vector sends staff down the guest's inner side.
            yield return WaitForCondition(() => Vector3.Angle(guestBody.forward, Vector3.back) < 1 &&
                Quaternion.Angle(suitcase.Body.rotation, guestBody.rotation) < 1 &&
                Vector3.Distance(suitcase.Body.position,
                    guestBody.position + guestBody.right * .68f + Vector3.up * .40f) < .04f,
                4, "The guest and their suitcase must physically finish the reception idle turn.");
            Vector3 approach = suitcase.Body.position + suitcase.Body.rotation * Vector3.right * 1.45f;
            Assert.That(approach.x, Is.LessThan(guestBody.position.x - 1.5f),
                "Use the clear outer reception lane, away from the guest capsule and loose lobby cases.");
            approach.y = .08f;
            yield return PositionEmptyActorForLinen(0, approach, suitcase.Body.worldCenterOfMass);
            yield return GrabServiceSupply(suitcase);
            yield return CarryServiceSupply(suitcase, new Vector3(approach.x, 0, -2.3f));
            // Hold the actual joint across the checkout/calendar boundary. No staff or item teleport.
            session.AdvanceTime(guest.Agent.CheckoutTime + .25f - hotel.Elapsed);
            yield return WaitForCondition(() => guest.Agent.State == GuestAgentState.Left, 30,
                "The unserved guest must physically reach the exterior exit while staff keep their suitcase.");
            Assert.That(actor.Interactor.HeldBody, Is.SameAs(suitcase.Body));
            Assert.That(suitcase.State.Location, Is.EqualTo(ServiceItemLocation.HeldByPlayer));
            Assert.That(suitcase.GetComponentsInChildren<Renderer>().All(renderer => renderer.enabled), Is.True,
                "The owner leaving cannot hide a suitcase that is still being carried.");
            Assert.That(request.Status, Is.EqualTo(ServiceStatus.Expired));
            int fulfilled = guest.Memory.ServicesFulfilled, stored = guest.Memory.LuggageStored;
            float adjustment = guest.ServiceSatisfactionAdjustment;
            yield return CarryServiceSupply(suitcase, new Vector3(-7.3f, 0, -2.3f));
            var zone = Object.FindAnyObjectByType<LuggageStorageZone>();
            yield return AimAtKeyScenarioPoint(actor, padA, () => zone.storageBounds.bounds.center);
            Assert.That(actor.Interactor.Focused, Is.SameAs(zone));
            StringAssert.Contains("lost-property", zone.GetPrompt(actor.Interactor));
            QueueUse(padA, true); yield return null; yield return null;
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That(actor.Interactor.HeldBody, Is.Null);
            Assert.That(suitcase.State.Location, Is.EqualTo(ServiceItemLocation.Stored));
            Assert.That(Vector3.Distance(suitcase.Body.position, zone.StorageAnchor(suitcase.luggageSlot).position), Is.LessThan(.04f));
            Assert.That(zone.storageBounds.bounds.Contains(suitcase.PlacementCollider.bounds.min) &&
                zone.storageBounds.bounds.Contains(suitcase.PlacementCollider.bounds.max), Is.True);
            Assert.That(request.Status, Is.EqualTo(ServiceStatus.Expired));
            Assert.That(guest.Memory.ServicesFulfilled, Is.EqualTo(fulfilled));
            Assert.That(guest.Memory.LuggageStored, Is.EqualTo(stored));
            Assert.That(guest.ServiceSatisfactionAdjustment, Is.EqualTo(adjustment));
            Assert.That(hotel.Running && session.Phase == DayPhase.Service, Is.True);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
