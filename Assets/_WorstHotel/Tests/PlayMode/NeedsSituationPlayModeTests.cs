using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace WorstHotel.Tests
{
    public sealed partial class Phase1PlayModeTests
    {
        [UnityTest]
        public IEnumerator ActualColdRoomAdvancesToComplaintStopsWaitAndRecoversFromRestoredTemperature()
        {
            var session = GameSession.Instance;
            // The existing fixture teardown destroys these private asset clones. Living mode stays active.
            waitScenarioSessionConfig = UnityEngine.Object.Instantiate(session.config);
            waitScenarioLivingConfig = UnityEngine.Object.Instantiate(session.config.living);
            waitScenarioLivingConfig.firstArrivalSeconds = 0.2f;
            waitScenarioLivingConfig.arrivalJitterSeconds = 0;
            waitScenarioLivingConfig.firstActivityDelay = 1000;
            waitScenarioSessionConfig.living = waitScenarioLivingConfig;
            session.config = waitScenarioSessionConfig;
            session.NewGame();
            var offer = session.Plan.Applications.First(candidate => candidate.Archetype.Kind == GuestKind.ColdSensitive);
            int min = session.Economy.MinPrice, step = session.Economy.PriceStep;
            int price = min + Mathf.RoundToInt((offer.ReferencePrice - min) / (float)step) * step;
            session.Assign(0, offer.Id, 101, price);
            session.CommitPlan(0);
            var simulation = session.Simulation;
            Assert.That(simulation.LivingEnabled, Is.True);
            var guest = simulation.Guests.Single();

            // Explicit adapter setup isolates situation/WAIT integration. Physical check-in and walking
            // are covered by GuestWalksToReceptionReceivesPhysicalKeyThenWalksThroughDoorAndUsesRoom.
            session.AdvanceTime(guest.Agent.ArrivalTime + 0.2f);
            Assert.That(session.ReportGuestReachedReception(guest.GuestId).Success, Is.True);
            Assert.That(CheckInWithModelKeyFixture(session, 0, guest.GuestId).Success, Is.True);
            Assert.That(session.ReportGuestReachedRoom(guest.GuestId).Success, Is.True);
            Assert.That(simulation.SetRoomTemperature(101, 5).Success, Is.True);
            session.AdvanceTime(0.2f);
            var situation = simulation.Incidents.Items.Single(item => item.GuestId == guest.GuestId && item.Reason == IncidentReason.Temperature);
            Assert.That(situation.Stage, Is.EqualTo(SituationStage.Observed));
            Assert.That(simulation.Requests.ActiveCount, Is.Zero, "A cold reading is not an instant complaint.");
            yield return ConsentToWait();
            yield return WaitForCondition(() => simulation.Services.IncomingCall != null ||
                guest.Agent.State == GuestAgentState.WaitingAtServiceReception, 22,
                "The real private cold episode must lead to a physical phone/reception contact that interrupts WAIT.");
            Assert.That(simulation.Clock.Speed, Is.EqualTo(1));
            Assert.That(Waiter.HasVoted(0) || Waiter.HasVoted(1), Is.False);
            Assert.That(situation.Stage, Is.GreaterThanOrEqualTo(SituationStage.Complaint));
            Assert.That(situation.ExposureSeconds, Is.GreaterThanOrEqualTo(simulation.NeedsSettings.ComplaintExposureSeconds));
            Assert.That(situation.Dissatisfaction, Is.GreaterThanOrEqualTo(simulation.NeedsSettings.ComplaintDissatisfaction));
            Assert.That(situation.HasContactedStaff, Is.False);
            Assert.That(simulation.Requests.ActiveCount, Is.Zero, "A ringing/waiting cue must not reveal the private complaint.");
            var response = simulation.Services.Responses.Single(item => item.GuestId == guest.GuestId && item.Phase == GuestResponsePhase.Contacting);
            // Explicit model communication adapter: this test exercises needs, natural contact
            // interruption and recovery. Separate UI tests cover the staff phone/body input grant.
            var heard = response.Channel == GuestContactChannel.Phone ? simulation.AnswerIncomingServiceCall(0, response.Id) :
                simulation.TalkToServiceGuest(0, guest.GuestId, response.Id);
            Assert.That(heard.Success, Is.True);
            var request = simulation.Requests.Items.Single();
            Assert.That(request.Reason, Is.EqualTo(IncidentReason.Temperature));
            Assert.That(request.Resolved, Is.False);
            yield return new WaitForSecondsRealtime(WaitHoldSeconds + 0.15f);
            Assert.That(Waiter.IsWaiting, Is.False, "Held buttons must not resume WAIT across a contact or communication event.");

            float historicalExposure = guest.Needs.Temperature.ExposureSeconds;
            Assert.That(simulation.SetRoomTemperature(101, 22.5f).Success, Is.True);
            yield return new WaitForSecondsRealtime(0.4f);
            Assert.That(request.Resolved, Is.False, "Recovery requires sustained real conditions.");
            yield return WaitForCondition(() => guest.Agent.InAssignedRoom, 35,
                "A guest who visited reception must physically return before experiencing the restored room.");
            yield return WaitForCondition(() => request.Resolved, simulation.NeedsSettings.RecoverySeconds + 3,
                "Restored room temperature did not resolve the underlying cold situation.");
            Assert.That(situation.Stage, Is.EqualTo(SituationStage.Resolved));
            Assert.That(simulation.Requests.Items.Single(), Is.SameAs(request));
            Assert.That(guest.Needs.Temperature.ExposureSeconds, Is.GreaterThanOrEqualTo(historicalExposure));
            Assert.That(Time.timeScale, Is.EqualTo(1));
            LogAssert.NoUnexpectedReceived();
        }
    }
}
