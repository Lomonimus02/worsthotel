using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace WorstHotel.Tests
{
    public sealed partial class Phase1PlayModeTests
    {
        GuestServiceConfig noiseConversationServices;

        [UnityTearDown]
        public IEnumerator DisposeNoiseConversationServices()
        {
            if (noiseConversationServices) Object.Destroy(noiseConversationServices);
            noiseConversationServices = null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator PhysicalGuestConversationCancelsWithoutChangingNoiseThenSelectedQuietResponseLowersSource()
        {
            var session = GameSession.Instance;
            // Explicit adapter setup isolates this physical interaction. Separate tests cover the
            // complete arrival/key/door route; shared teardown owns these private configuration clones.
            waitScenarioSessionConfig = Object.Instantiate(session.config);
            waitScenarioLivingConfig = Object.Instantiate(session.config.living);
            waitScenarioLivingConfig.firstArrivalSeconds = .2f;
            waitScenarioLivingConfig.arrivalJitterSeconds = 0;
            waitScenarioLivingConfig.firstActivityDelay = 1000;
            waitScenarioLivingConfig.activityDurationMin = 40;
            waitScenarioLivingConfig.activityDurationMax = 40;
            waitScenarioSessionConfig.living = waitScenarioLivingConfig;
            session.config = waitScenarioSessionConfig;
            session.NewGame();
            var offer = session.Plan.Applications.First();
            int min = session.Economy.MinPrice, step = session.Economy.PriceStep;
            int price = min + Mathf.RoundToInt((offer.ReferencePrice - min) / (float)step) * step;
            session.Assign(0, offer.Id, 101, price);
            session.CommitPlan(0);
            var simulation = session.Simulation;
            Assert.That(simulation.LivingEnabled, Is.True);
            var guest = simulation.Guests.Single();
            session.AdvanceTime(guest.Agent.ArrivalTime + .2f);
            Assert.That(session.ReportGuestReachedReception(guest.GuestId).Success, Is.True);
            Assert.That(CheckInWithModelKeyFixture(session, 0, guest.GuestId).Success, Is.True);
            Assert.That(session.ReportGuestReachedRoom(guest.GuestId).Success, Is.True);
            Assert.That(simulation.ForceActivity(guest.GuestId, GuestActivity.LoudRoom).Success, Is.True);
            session.RaiseChanged();
            yield return null;

            var presentation = Object.FindAnyObjectByType<GuestPresentation>();
            Assert.That(presentation, Is.Not.Null);
            Assert.That(presentation.TryGetGuestTransform(guest.GuestId, out var visual), Is.True);
            var markers = presentation.roomMarkers.Single(room => room.roomId == 101);
            yield return WaitForCondition(() => HorizontalDistance(visual.position, markers.loud.position) < .08f, 15,
                "The loud guest must finish the authored room route before a stationary conversation.");
            yield return null;
            var target = visual.GetComponent<GuestReceptionInteraction>();
            Assert.That(target, Is.Not.Null);
            var capsule = target.GetComponent<CapsuleCollider>();
            Assert.That(capsule.enabled && !capsule.isTrigger, Is.True, "The in-room guest needs a real raycast target.");
            Assert.That(Physics.GetIgnoreCollision(capsule, bootstrap.Players[0].BodyCollider), Is.True,
                "A guest interaction target must not trap staff in the room.");
            yield return FaceQuietGuestFromInnerLane(bootstrap.Players[0], padA, target, capsule);

            var source = session.Rooms.Single(room => room.Profile.Id == 101);
            var neighbour = session.Rooms.Single(room => room.Profile.Id == 103);
            float originalSource = source.SourceNoise, originalReceived = neighbour.ReceivedNoise;
            float scheduledActivityEnd = guest.Agent.NextActivityTime;
            Assert.That(originalSource, Is.GreaterThan(0));
            Assert.That(originalReceived, Is.GreaterThan(0), "The actual shared-wall neighbour must receive this source.");
            Assert.That(target.GetPrompt(bootstrap.Players[0].Interactor), Does.Contain("noise"));

            QueueUse(padA, true);
            yield return WaitForCondition(() => ManagementUI.Instance.IsGuestContextOpen, 2, "Talking to the real guest must open contextual choices.");
            Assert.That(ManagementUI.Instance.ContextGuestId, Is.EqualTo(guest.GuestId));
            Assert.That(bootstrap.Players[0].IsUIBlocked, Is.True);
            Assert.That(guest.Agent.QuietUntil, Is.Zero);
            QueueUse(padA, false);
            yield return null; yield return null;
            InputSystem.QueueStateEvent(padA, new GamepadState().WithButton(GamepadButton.East));
            yield return null; yield return null;
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That(ManagementUI.Instance.IsOpen, Is.False, "Cancel must close the choice panel.");
            Assert.That(source.SourceNoise, Is.EqualTo(originalSource).Within(.0001f),
                "A cancelled request must not secretly reduce the authoritative noise source.");

            QueueUse(padA, true);
            yield return WaitForCondition(() => ManagementUI.Instance.IsGuestContextOpen, 2, "The same physical guest must reopen the contextual conversation.");
            QueueUse(padA, false); yield return null; yield return null; yield return null;
            QueueUse(padA, true);
            yield return WaitForCondition(() => guest.Agent.QuietUntil > simulation.Elapsed,
                2, "Choosing the first contextual action with the gamepad must submit the actual quiet request.");
            QueueUse(padA, false);
            yield return null; yield return null;
            InputSystem.QueueStateEvent(padA, new GamepadState().WithButton(GamepadButton.East));
            yield return null; yield return null;
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That(target.OwnerActorId, Is.EqualTo(-1));
            Assert.That(target.HoldProgress, Is.Zero);
            float multiplier = simulation.NoiseSettings.QuietSourceMultiplier;
            Assert.That(source.SourceNoise, Is.EqualTo(originalSource * multiplier).Within(.0001f));
            Assert.That(neighbour.ReceivedNoise, Is.EqualTo(originalReceived * multiplier).Within(.0001f));
            Assert.That(guest.Agent.Activity, Is.EqualTo(GuestActivity.LoudRoom), "A quiet request must not skip the scheduled activity.");
            Assert.That(guest.Agent.NextActivityTime, Is.EqualTo(scheduledActivityEnd));
            Assert.That(target.CanInteract(bootstrap.Players[0].Interactor), Is.True,
                "Ordinary conversation stays available for other concerns during the finite quiet agreement.");
            Assert.That(target.GetPrompt(bootstrap.Players[0].Interactor), Does.Contain("Talk to guest").And.Not.Contain("room key"));
            float quietDeadline = guest.Agent.QuietUntil;
            QueueUse(padA, true);
            yield return WaitForCondition(() => ManagementUI.Instance.IsGuestContextOpen, 2, "Quiet guest still permits an ordinary conversation.");
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That(ManagementUI.Instance.ContextOptionTitles, Does.Not.Contain("Ask to keep it down"),
                "The actual input collection must not offer a duplicate quiet request during its agreement.");
            Assert.That(guest.Agent.QuietUntil, Is.EqualTo(quietDeadline), "Repeated use must not refresh the temporary agreement.");
            ManagementUI.Instance.Close();
            Assert.That(Time.timeScale, Is.EqualTo(1));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ClosedDoorKnockAndSeparateAskReduceRealNeighbourNoiseThenMeasuredComplaintRecovers()
        {
            var session = GameSession.Instance;
            // Arrival/key/path tests own physical check-in. This explicit model adapter puts two
            // real room occupants into a sustained acoustic scenario without changing noise/needs.
            waitScenarioSessionConfig = Object.Instantiate(session.config);
            waitScenarioLivingConfig = Object.Instantiate(session.config.living);
            waitScenarioLivingConfig.firstArrivalSeconds = .2f;
            waitScenarioLivingConfig.arrivalJitterSeconds = 0;
            waitScenarioLivingConfig.firstActivityDelay = 1000;
            waitScenarioLivingConfig.activityDurationMin = 120;
            waitScenarioLivingConfig.activityDurationMax = 120;
            waitScenarioSessionConfig.living = waitScenarioLivingConfig;
            // The synchronous exposure adapter below cannot execute competing physical contact
            // routes. Isolate this source-door input test from optional preferences/self-help,
            // retaining natural disclosure and all production noise/need/recovery settings.
            noiseConversationServices = Object.Instantiate(session.config.services);
            noiseConversationServices.naturalCommunicationEnabled = true;
            noiseConversationServices.eligibility = 0;
            noiseConversationServices.selfResponseObserveSeconds = 1000;
            noiseConversationServices.toleranceSeconds = 1000;
            waitScenarioSessionConfig.services = noiseConversationServices;
            session.config = waitScenarioSessionConfig;
            session.NewGame();
            var offers = new[]
            {
                session.Plan.Applications.First(offer => offer.Archetype.Kind == GuestKind.Budget),
                session.Plan.Applications.First(offer => offer.Archetype.Kind == GuestKind.Business)
            };
            int[] roomIds = { 103, 102 };
            for (int i = 0; i < offers.Length; i++)
            {
                int price = session.Economy.MinPrice + Mathf.RoundToInt((offers[i].ReferencePrice - session.Economy.MinPrice) /
                    (float)session.Economy.PriceStep) * session.Economy.PriceStep;
                session.Assign(i, offers[i].Id, roomIds[i], price);
            }
            session.CommitPlan(0);
            var simulation = session.Simulation;
            Assert.That(session.Phase, Is.EqualTo(DayPhase.Service));
            Assert.That(simulation.Guests.Count, Is.EqualTo(2));
            foreach (var guest in simulation.Guests.OrderBy(stay => stay.Agent.ArrivalTime))
            {
                session.AdvanceTime(Mathf.Max(0, guest.Agent.ArrivalTime + .2f - simulation.Elapsed));
                Assert.That(session.ReportGuestReachedReception(guest.GuestId).Success, Is.True);
                Assert.That(CheckInWithModelKeyFixture(session, 0, guest.GuestId).Success, Is.True);
                Assert.That(session.ReportGuestReachedRoom(guest.GuestId).Success, Is.True);
            }
            var sourceGuest = simulation.Guests.Single(stay => stay.RoomId == 103);
            var affectedGuest = simulation.Guests.Single(stay => stay.RoomId == 102);
            // Let the scene register its physical actors before forcing the acoustic case.
            // A guest on the way to the radio now correctly emits quiet movement, not music.
            yield return null; yield return null;
            Assert.That(simulation.ForceActivity(sourceGuest.GuestId, GuestActivity.LoudRoom).Success, Is.True);
            session.RaiseChanged();
            yield return WaitForCondition(() => sourceGuest.Agent.ActivityStaged && affectedGuest.Agent.ActivityStaged,
                20, "Both guests must reach their actual activity anchors before measuring a sustained noise complaint.");
            HotelIncident concern = null;
            for (int tick = 0; tick < 225 && concern == null; tick++)
            {
                session.AdvanceTime(.2f);
                concern = simulation.Incidents.Items.SingleOrDefault(item => item.GuestId == affectedGuest.GuestId &&
                    item.Reason == IncidentReason.Noise && item.Active && item.Stage >= SituationStage.Complaint &&
                    item.Response != null && item.Response.DwellSeconds >= simulation.Services.Settings.ObservationSeconds);
            }
            Assert.That(concern, Is.Not.Null, "The measured 103-to-102 noise link must first produce a real private concern.");
            Assert.That(concern.HasContactedStaff, Is.False);
            // Labelled model conversation adapter isolates the separately tested victim disclosure.
            // The subject below remains the real closed-source-door knock and selected quiet action.
            var disclosure = simulation.DiscussRoomConcern(0, affectedGuest.GuestId, concern.Response.Id);
            Assert.That(disclosure.Success, Is.True, disclosure.Message + " / guest=" + affectedGuest.Agent.State +
                " activity=" + affectedGuest.Agent.Activity + " activeResponse=" + affectedGuest.Agent.ResponseActionId +
                " targetResponse=" + concern.Response.Id + " phase=" + concern.Response.Phase +
                " dwell=" + concern.Response.DwellSeconds + " severity=" + concern.Severity + " stage=" + concern.Stage);
            var request = simulation.Requests.Items.Single(item => item.Id == concern.Id);
            yield return null; yield return null;
            var target = Object.FindObjectsByType<RoomNoiseInteraction>(FindObjectsSortMode.None).Single(item => item.roomId == 103);
            var door = target.GetComponentInParent<DoorInteractable>();
            Assert.That(door, Is.Not.Null);
            Assert.That(door.IsOpen || door.IsPassageOpen, Is.False, "This counterplay must work through the closed guest door.");
            Assert.That(target.knocker, Is.Not.Null);
            var collider = target.GetComponent<BoxCollider>();
            Assert.That(collider.enabled && !collider.isTrigger, Is.True);
            var actor = bootstrap.Players[0];
            Assert.That(actor.Interactor.HeldBody, Is.Null);
            var pose = new GameObject("Closed-door knocker approach");
            Vector3 aim = collider.bounds.center;
            pose.transform.position = new Vector3(-.75f, .08f, aim.z);
            Vector3 forward = aim - pose.transform.position; forward.y = 0;
            pose.transform.rotation = Quaternion.LookRotation(forward);
            actor.ResetToSpawn(pose.transform);
            Object.Destroy(pose);
            yield return WaitForGroundContact(actor);
            yield return AimAtKeyScenarioPoint(actor, padA, () => collider.bounds.center);
            Assert.That(actor.Interactor.Focused, Is.SameAs(target));

            var source = session.Rooms.Single(room => room.Profile.Id == 103);
            var neighbour = session.Rooms.Single(room => room.Profile.Id == 102);
            float originalSource = source.SourceNoise, originalReceived = neighbour.ReceivedNoise;
            float activityEnd = sourceGuest.Agent.NextActivityTime;
            Assert.That(session.RequestQuiet(0, sourceGuest.GuestId).Success, Is.False,
                "Focusing the door alone must not bypass the unanswered knock.");
            QueueUse(padA, true); yield return null; yield return null;
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That(target.HasAnswered(0), Is.True);
            Assert.That(target.HasAnswered(1), Is.False, "An answer belongs to the player who knocked.");
            Assert.That(sourceGuest.Agent.QuietUntil, Is.Zero);
            Assert.That(source.SourceNoise, Is.EqualTo(originalSource).Within(.0001f));
            Assert.That(neighbour.ReceivedNoise, Is.EqualTo(originalReceived).Within(.0001f));
            Assert.That(request.Resolved, Is.False, "Knocking is not an acoustic fix or a completed complaint.");
            Assert.That(session.RequestQuiet(1, sourceGuest.GuestId).Success, Is.False);
            yield return new WaitForSecondsRealtime(RoomNoiseInteraction.AnswerWindowSeconds + .15f);
            Assert.That(target.HasAnswered(0), Is.False, "A stale response must not authorize a later remote request.");
            Assert.That(session.RequestQuiet(0, sourceGuest.GuestId).Success, Is.False);
            Assert.That(source.SourceNoise, Is.EqualTo(originalSource).Within(.0001f));

            QueueUse(padA, true); yield return null; yield return null;
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That(target.HasAnswered(0), Is.True);
            Assert.That(sourceGuest.Agent.QuietUntil, Is.Zero);
            float beforeAsk = Time.realtimeSinceStartup;
            QueueUse(padA, true); yield return null; yield return null;
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That(ManagementUI.Instance.IsGuestContextOpen, Is.True, "The second press must offer a choice, not silently choose quiet.");
            Assert.That(sourceGuest.Agent.QuietUntil, Is.Zero);
            QueueUse(padA, true); yield return null; yield return null; yield return null;
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That(sourceGuest.Agent.QuietUntil, Is.GreaterThan(simulation.Elapsed), "Selecting ask for quiet must submit the actual request.");
            Assert.That(Time.realtimeSinceStartup - beforeAsk, Is.LessThan(2), "A door conversation must not become a generic hold task.");
            InputSystem.QueueStateEvent(padA, new GamepadState().WithButton(GamepadButton.East));
            yield return null; yield return null;
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That(source.SourceNoise, Is.EqualTo(originalSource * simulation.NoiseSettings.QuietSourceMultiplier).Within(.0001f));
            Assert.That(neighbour.ReceivedNoise, Is.EqualTo(originalReceived * simulation.NoiseSettings.QuietSourceMultiplier).Within(.0001f));
            Assert.That(sourceGuest.Agent.NextActivityTime, Is.EqualTo(activityEnd));
            Assert.That(sourceGuest.Agent.Activity, Is.EqualTo(GuestActivity.LoudRoom));
            Assert.That(request.Resolved, Is.False, "Lower volume starts sustained recovery; it does not directly close the situation.");
            float exposure = affectedGuest.Needs.Noise.ExposureSeconds;
            session.AdvanceTime(simulation.NeedsSettings.RecoverySeconds * .5f);
            Assert.That(request.Resolved, Is.False);
            session.AdvanceTime(simulation.NeedsSettings.RecoverySeconds * .5f + .4f);
            Assert.That(affectedGuest.Needs.Noise.Severity, Is.LessThanOrEqualTo(simulation.NeedsSettings.RecoverySeverityThreshold));
            Assert.That(request.Resolved, Is.True);
            Assert.That(request.ResponseAccepted || request.Compensated, Is.False);
            Assert.That(request.Stage, Is.EqualTo(SituationStage.Resolved));
            Assert.That(affectedGuest.Needs.Noise.ExposureSeconds, Is.EqualTo(exposure).Within(.002f));
            Assert.That(door.IsOpen || door.IsPassageOpen, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1));
            LogAssert.NoUnexpectedReceived();
        }

        IEnumerator FaceQuietGuestFromInnerLane(FirstPersonController player, Gamepad pad,
            GuestReceptionInteraction target, CapsuleCollider capsule)
        {
            QueueUse(pad, false);
            Vector3 aim = target.transform.TransformPoint(capsule.center);
            var pose = new GameObject("Quiet request clear room approach");
            // Loud anchors sit in the clear strip at the foot of the bed. The ordinary station
            // helper approaches from the south wall, so this fixture approaches from the inner lane.
            pose.transform.position = new Vector3(aim.x - Mathf.Sign(aim.x) * 1.6f, .08f, aim.z);
            Vector3 forward = aim - pose.transform.position; forward.y = 0;
            pose.transform.rotation = Quaternion.LookRotation(forward);
            player.ResetToSpawn(pose.transform);
            Object.Destroy(pose);
            yield return WaitForGroundContact(player);
            float deadline = Time.realtimeSinceStartup + 5;
            while (Time.realtimeSinceStartup < deadline)
            {
                Vector3 delta = aim - player.PlayerCamera.transform.position;
                float wantedPitch = -Mathf.Atan2(delta.y, new Vector2(delta.x, delta.z).magnitude) * Mathf.Rad2Deg;
                float currentPitch = Mathf.DeltaAngle(0, player.PlayerCamera.transform.localEulerAngles.x);
                float error = wantedPitch - currentPitch;
                if (Mathf.Abs(error) < 2.5f) break;
                InputSystem.QueueStateEvent(pad, new GamepadState { rightStick = new Vector2(0, -Mathf.Sign(error) * .35f) });
                yield return null;
            }
            QueueUse(pad, false);
            yield return null; yield return null;
            Assert.That(player.Interactor.Focused, Is.SameAs(target),
                "The clear room approach must focus the real guest capsule through the normal input raycast.");
        }
    }
}
