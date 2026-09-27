using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace WorstHotel.Tests
{
    public sealed partial class Phase1PlayModeTests
    {
        NeedConfig earlyExitFixtureNeeds;

        [UnityTearDown]
        public IEnumerator DestroyEarlyExitFixtureNeeds()
        {
            if (earlyExitFixtureNeeds) Object.Destroy(earlyExitFixtureNeeds);
            earlyExitFixtureNeeds = null;
            yield return null;
        }

        [UnityTest, Category("ContinuousOperations")]
        public IEnumerator WarnedEarlyGuestPaysOnceButRoomWaitsForActualDoorLobbyAndExteriorExit()
        {
            ManagementUI.Instance.Close(); bootstrap.ConfigureSolo();
            InputSystem.RemoveDevice(padB); padB = null;
            var session = GameSession.Instance;
            // Explicit route fixture: accelerated policy thresholds and sustained physical
            // cold isolate a rare departure. This does not measure production balance.
            waitScenarioSessionConfig = Object.Instantiate(session.config);
            waitScenarioLivingConfig = Object.Instantiate(session.config.living);
            waitScenarioLivingConfig.firstActivityDelay = 1000;
            waitScenarioSessionConfig.living = waitScenarioLivingConfig;
            earlyExitFixtureNeeds = Object.Instantiate(session.config.needs);
            earlyExitFixtureNeeds.earlyCheckoutEnabled = true;
            earlyExitFixtureNeeds.earlyCheckoutSevereHours = .05f;
            earlyExitFixtureNeeds.earlyCheckoutGraceHours = .1f;
            earlyExitFixtureNeeds.earlyCheckoutRecoveryHours = .01f;
            earlyExitFixtureNeeds.complaintExposureSeconds = .25f;
            earlyExitFixtureNeeds.escalatedExposureSeconds = .5f;
            earlyExitFixtureNeeds.criticalExposureSeconds = 1;
            earlyExitFixtureNeeds.complaintDissatisfaction = .01f;
            earlyExitFixtureNeeds.escalatedDissatisfaction = .02f;
            earlyExitFixtureNeeds.criticalDissatisfaction = .03f;
            waitScenarioSessionConfig.needs = earlyExitFixtureNeeds;
            serviceUIConfig = Object.Instantiate(session.config.services);
            serviceUIConfig.naturalCommunicationEnabled = true; serviceUIConfig.eligibility = 0;
            serviceUIConfig.observationSeconds = .1f;
            serviceUIConfig.selfResponseObserveSeconds = serviceUIConfig.toleranceSeconds = 1000;
            waitScenarioSessionConfig.services = serviceUIConfig;
            session.config = waitScenarioSessionConfig; session.NewGame(); ManagementUI.Instance.Close();
            yield return null; yield return null;
            var model = session.Simulation;
            var presentation = Object.FindAnyObjectByType<GuestPresentation>();
            Assert.That(presentation.enabled, Is.True);
            // Empty staff observation pose keeps the real reception and door routes clear.
            var observer = new GameObject("Early departure fixture staff observation pose");
            observer.transform.SetPositionAndRotation(new Vector3(0, .08f, 32), Quaternion.identity);
            bootstrap.Players[0].ResetToSpawn(observer.transform); Object.Destroy(observer);
            var offer = model.BookingOffers.First(item => item.ArrivalDay == 1 && item.Application.Archetype.Kind == GuestKind.Business);
            Assert.That(session.AcceptBooking(0, offer.Id, 101, session.Economy.MinPrice).Success, Is.True);
            session.AdvanceTime(offer.ArrivalAt - model.Elapsed + .2f);
            var guest = model.Guests.Single();
            yield return WaitForCondition(() => guest.Agent.State == GuestAgentState.WaitingForCheckIn, 25,
                "The actual guest must reach reception from the exterior; no arrival callback is injected.");
            // Numbered-key model handoff is the only check-in adapter, as in existing route tests.
            Assert.That(CheckInWithModelKeyFixture(session, 0, guest.GuestId).Success, Is.True);
            yield return WaitForCondition(() => guest.Agent.InAssignedRoom && guest.Agent.ActivityStaged, 40,
                "The real guest must cross the assigned door and settle before experiencing cold.");
            Assert.That(presentation.TryGetGuestTransform(guest.GuestId, out var body), Is.True);
            var marker = presentation.roomMarkers.Single(item => item.roomId == 101);
            var room = session.Rooms.Single(item => item.Profile.Id == 101);
            float contracted = guest.Agent.Schedule.CheckoutTime;
            yield return WaitWithActualSevereCold(guest, () => model.Incidents.Items.Any(item => item.GuestId == guest.GuestId &&
                item.Active && item.Reason == IncidentReason.Temperature && item.Stage >= SituationStage.Escalated), 8,
                "Controlled room cold must cause an actual measured serious incident.");
            var incident = model.Incidents.Items.Single(item => item.GuestId == guest.GuestId && item.Active && item.Reason == IncidentReason.Temperature);
            // Explicit staff-conversation model adapter. No guest route, departure, warning,
            // perception or receipt is fabricated; physical dialogue has separate controller tests.
            Assert.That(model.DiscussRoomConcern(0, guest.GuestId, incident.Response.Id).Success, Is.True);
            Assert.That(model.AcceptConsequences(0, guest.GuestId).Success, Is.True);
            yield return WaitWithActualSevereCold(guest, () => guest.EarlyCheckout.State == EarlyCheckoutState.Warning, 8,
                "The guest must warn and retain their room before deciding to leave.");
            Assert.That(guest.ReceiptPosted, Is.False);
            Assert.That(room.GuestId, Is.EqualTo(guest.GuestId));
            int cash = model.Economy.Cash;
            float fixedStep = Time.fixedDeltaTime;
            yield return WaitWithActualSevereCold(guest, () => guest.EarlyCheckout.State == EarlyCheckoutState.Committed, 10,
                "Continued real cold after warning/grace must start the common checkout lifecycle.");
            Assert.That(guest.Agent.State, Is.EqualTo(GuestAgentState.CheckingOut));
            Assert.That(guest.ReceiptPosted, Is.True);
            Assert.That(guest.Agent.Schedule.CheckoutTime, Is.EqualTo(contracted));
            Assert.That(model.Elapsed, Is.LessThan(contracted));
            Assert.That(room.GuestId, Is.Null);
            Assert.That(room.DepartingGuestId, Is.EqualTo(guest.GuestId));
            Assert.That(room.Cleanliness, Is.EqualTo(Cleanliness.Dirty));
            Assert.That(model.PickUpLinen(0, "dirty:101").Success, Is.False,
                "Billing does not permit staff to strip a room while the departing guest is still inside.");
            Assert.That(model.Keys.Find(101).Location, Is.EqualTo(RoomKeyLocation.Returned));
            Assert.That(model.Keys.Find(101).GuestId, Is.Null);
            var receipt = model.CaptureSnapshot(401, 1).Operations.PeriodReceipts.Single();
            Assert.That(receipt.EarlyCheckout, Is.True);
            Assert.That(model.Economy.Cash, Is.EqualTo(cash + receipt.Price - receipt.Compensation));
            int settledCash = model.Economy.Cash;
            Vector3 start = body.position;
            float nearestExit = float.MaxValue;
            bool crossedDoor = false, crossedLobby = false, sawLeaving = false;
            float deadline = Time.realtimeSinceStartup + 45;
            while (guest.Agent.State != GuestAgentState.Left && Time.realtimeSinceStartup < deadline)
            {
                if (body)
                {
                    sawLeaving |= guest.Agent.State == GuestAgentState.Leaving;
                    bool inside = AuthoredGuestRoute.IsOnRoomSide(body.position, marker);
                    if (inside) Assert.That(room.DepartingGuestId, Is.EqualTo(guest.GuestId));
                    if (room.DepartingGuestId == null)
                    { crossedDoor = true; Assert.That(inside, Is.False, "Only the real doorway crossing clears the vacancy token."); }
                    crossedLobby |= body.position.z < 5 && Mathf.Abs(body.position.x) < 2.5f;
                    nearestExit = Mathf.Min(nearestExit, HorizontalDistance(body.position, presentation.arrivalSpawn.position));
                }
                Assert.That(model.Economy.Cash, Is.EqualTo(settledCash), "The walking body cannot settle a second bill.");
                yield return null;
            }
            Assert.That(guest.Agent.State, Is.EqualTo(GuestAgentState.Left));
            Assert.That(sawLeaving && crossedDoor && crossedLobby, Is.True);
            Assert.That(nearestExit, Is.LessThan(.35f), "The real route must reach the authored exterior exit.");
            Assert.That(HorizontalDistance(start, presentation.arrivalSpawn.position), Is.GreaterThan(8));
            Assert.That(room.DepartingGuestId, Is.Null);
            Assert.That(room.Cleanliness, Is.EqualTo(Cleanliness.Dirty));
            Assert.That(model.CaptureSnapshot(401, 2).Operations.PeriodReceipts.Length, Is.EqualTo(1));
            Assert.That(model.Economy.Cash, Is.EqualTo(settledCash));
            Assert.That(Time.timeScale, Is.EqualTo(1));
            Assert.That(Time.fixedDeltaTime, Is.EqualTo(fixedStep));
            Assert.That(ManagementUI.Instance.IsOpen, Is.False);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
