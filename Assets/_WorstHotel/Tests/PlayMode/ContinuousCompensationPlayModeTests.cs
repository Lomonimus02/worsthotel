using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace WorstHotel.Tests
{
    public sealed partial class Phase1PlayModeTests
    {
        IEnumerator PrepareContinuousCompensationGuest()
        {
            var session = GameSession.Instance;
            // Labelled initial booking/key/room adapters isolate the discussion. Subsequent
            // reception and phone arrivals below use real guest routes and production callbacks.
            waitScenarioSessionConfig = Object.Instantiate(session.config);
            waitScenarioLivingConfig = Object.Instantiate(session.config.living);
            waitScenarioLivingConfig.firstActivityDelay = 1000;
            waitScenarioLivingConfig.activityDurationMin = waitScenarioLivingConfig.activityDurationMax = 120;
            waitScenarioSessionConfig.living = waitScenarioLivingConfig;
            serviceUIConfig = Object.Instantiate(session.config.services);
            serviceUIConfig.naturalCommunicationEnabled = true; serviceUIConfig.eligibility = 0;
            serviceUIConfig.selfResponseObserveSeconds = serviceUIConfig.toleranceSeconds = 1000;
            serviceUIConfig.phoneRingSeconds = serviceUIConfig.receptionWaitSeconds = 90;
            waitScenarioSessionConfig.services = serviceUIConfig;
            session.config = waitScenarioSessionConfig; session.NewGame(); ManagementUI.Instance.Close();
            var model = session.Simulation;
            Assert.That(model.ContinuousOperations, Is.True);
            var offer = model.BookingOffers.First(value => value.ArrivalDay == 1);
            Assert.That(session.AcceptBooking(0, offer.Id, 101, session.Economy.MinPrice).Success, Is.True);
            session.AdvanceTime(offer.ArrivalAt - model.Elapsed + .2f);
            var guest = model.Guests.Single();
            Assert.That(session.ReportGuestReachedReception(guest.GuestId).Success, Is.True);
            Assert.That(CheckInWithModelKeyFixture(session, 0, guest.GuestId).Success, Is.True);
            Assert.That(session.ReportGuestReachedRoom(guest.GuestId).Success, Is.True);
            yield return null; yield return null;
            yield return WaitForCondition(() => guest.Agent.ActivityStaged, 15, "Initial guest body must be staged before creating real exposure.");
            // Real temperature and bounded diagnostic clock, not a fabricated complaint/contact.
            for (int index = 0; index < 160 && !model.Incidents.Items.Any(value => value.GuestId == guest.GuestId &&
                value.Reason == IncidentReason.Temperature && value.Stage >= SituationStage.Complaint); index++)
            {
                Assert.That(session.DebugForceSevereCold(guest.GuestId).Success, Is.True);
                session.AdvanceTime(.25f);
            }
            Assert.That(model.Incidents.Items.Any(value => value.GuestId == guest.GuestId && value.Reason == IncidentReason.Temperature &&
                value.Active && value.Stage >= SituationStage.Complaint), Is.True);
            Assert.That(model.Services.CompensationDiscussion(guest.GuestId), Is.Null, "A remote room problem alone never holds the guest.");
        }

        IEnumerator WaitWithActualSevereCold(GuestStay guest, Func<bool> condition, float seconds, string reason)
        {
            var model = GameSession.Instance.Simulation;
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < end)
            {
                // Explicit maintained physical temperature for this diagnostic route fixture.
                model.SetRoomTemperature(guest.RoomId, guest.Application.Archetype.Needs.PreferredTemperatureMin - 8);
                yield return null;
            }
            Assert.That(condition(), Is.True, reason);
        }

        IEnumerator ChooseDiscussionOption(string prefix, bool phone)
        {
            var ui = ManagementUI.Instance;
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That((phone ? ui.ServiceOptionTitles : ui.ContextOptionTitles).Any(value => value.StartsWith(prefix, StringComparison.Ordinal)), Is.True,
                "Missing physical discussion choice: " + prefix);
            int attempts = 0;
            while ((phone ? ui.FocusedServiceOption : ui.FocusedContextOption)?.StartsWith(prefix, StringComparison.Ordinal) != true)
            {
                Assert.That(attempts++, Is.LessThan(12));
                InputSystem.QueueStateEvent(padA, new GamepadState().WithButton(GamepadButton.DpadDown));
                yield return null; yield return null;
                QueueUse(padA, false); yield return null; yield return null;
            }
            QueueUse(padA, true); yield return null; yield return null;
            QueueUse(padA, false); yield return null; yield return null; yield return null;
        }

        [UnityTest, Category("ContinuousOperations")]
        public IEnumerator ActualReceptionCompensationKeepsFiniteWaitAcrossCloseAndRejectsStaleOrAbsentStaff()
        {
            yield return PrepareContinuousCompensationGuest();
            var session = GameSession.Instance; var model = session.Simulation; var guest = model.Guests.Single();
            var incident = model.Incidents.Items.Single(value => value.GuestId == guest.GuestId && value.Reason == IncidentReason.Temperature && value.Active);
            Assert.That(session.DebugBeginGuestContact(guest.GuestId, GuestContactChannel.Reception).Success, Is.True);
            yield return WaitWithActualSevereCold(guest, () => guest.Agent.State == GuestAgentState.WaitingAtServiceReception, 35,
                "The actual guest body must reach the reception desk.");
            var target = Object.FindObjectsByType<GuestReceptionInteraction>(FindObjectsSortMode.None).Single(value => value.GuestId == guest.GuestId);
            yield return FaceStation(bootstrap.Players[0], padA, target, target.GetComponent<Collider>().bounds.center);
            QueueUse(padA, true);
            yield return WaitForCondition(() => ManagementUI.Instance.IsGuestContextOpen, 2, "Actual body interaction opens the discussion.");
            QueueUse(padA, false); yield return null; yield return null;
            var intent = model.Services.CompensationDiscussion(guest.GuestId);
            Assert.That(intent, Is.Not.Null); float deadline = intent.Deadline;
            Assert.That(intent.ResponseId, Is.EqualTo(incident.Response.Id));
            Assert.That(guest.Agent.State, Is.EqualTo(GuestAgentState.WaitingAtServiceReception));
            Assert.That(GuestLabels.GuestIntentStatus(guest, model), Does.Contain("Discussing compensation"));
            QueueUse(padA, false, true); yield return null; yield return null;
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That(ManagementUI.Instance.IsOpen, Is.False);
            Assert.That(model.Services.CompensationDiscussion(guest.GuestId), Is.SameAs(intent));
            Assert.That(intent.Deadline, Is.EqualTo(deadline));
            string before = JsonUtility.ToJson(model.CaptureSnapshot(71, 1));
            Assert.That(session.OfferCompensation(0, guest.GuestId, intent.Id, intent.Revision).Success, Is.False,
                "A closed menu revokes physical authority without extending or ending the wait.");
            Assert.That(JsonUtility.ToJson(model.CaptureSnapshot(71, 1)), Is.EqualTo(before));
            yield return FaceStation(bootstrap.Players[0], padA, target, target.GetComponent<Collider>().bounds.center);
            QueueUse(padA, true);
            yield return WaitForCondition(() => ManagementUI.Instance.IsGuestContextOpen, 2, "The same physical guest can be spoken to again.");
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That(intent.Deadline, Is.EqualTo(deadline));
            before = JsonUtility.ToJson(model.CaptureSnapshot(71, 1));
            Assert.That(session.OfferCompensation(0, guest.GuestId, intent.Id, intent.Revision + 1).Success, Is.False);
            Assert.That(session.AcceptConsequences(0, guest.GuestId, "obsolete-discussion", intent.Revision).Success, Is.False);
            var envelope = new LanCommand { epoch = 71, sequence = 1, day = session.Day, phase = DayPhase.Service,
                kind = LanCommandKind.OfferCredit, subject = guest.GuestId,
                expectedDirectIntentId = intent.Id, expectedDirectIntentRevision = intent.Revision };
            var decoded = JsonUtility.FromJson<LanCommand>(JsonUtility.ToJson(envelope));
            Assert.That(LanProtocol.ValidCommand(decoded, 71, 0, session.Day, DayPhase.Service, true), Is.True);
            session.ExecuteLanCommand(1, decoded);
            Assert.That(guest.Compensated, Is.False, "A partner cannot borrow this actor's actual conversation grant.");
            Assert.That(JsonUtility.ToJson(model.CaptureSnapshot(71, 1)), Is.EqualTo(before));
            bootstrap.SetPaused(true);
            Assert.That(session.OfferCompensation(0, guest.GuestId, intent.Id, intent.Revision).Success, Is.False);
            bootstrap.SetPaused(false);
            Assert.That(model.Services.CompensationDiscussion(guest.GuestId), Is.SameAs(intent));
            // Reflect only the existing expiry to simulate 31 seconds of wall-clock age.
            // There is no production clock override and no synthetic body/arrival callback.
            var grantsField = typeof(GameSession).GetField("conversations", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(grantsField, Is.Not.Null);
            var grant = ((Array)grantsField.GetValue(session)).GetValue(0);
            Assert.That(grant, Is.Not.Null);
            var expiryField = grant.GetType().GetField("expires");
            float originalExpiry = (float)expiryField.GetValue(grant);
            Assert.That(originalExpiry - Time.unscaledTime, Is.GreaterThan(35), "Production continuous grant must cover the configured 40-second model wait with margin.");
            expiryField.SetValue(grant, originalExpiry - 31);
            session.AdvanceTime(Mathf.Max(0, intent.CreatedAt + 31 - model.Elapsed));
            Assert.That(model.Elapsed, Is.GreaterThanOrEqualTo(intent.CreatedAt + 30.9f));
            Assert.That(model.Elapsed, Is.LessThan(intent.Deadline));
            Assert.That(session.HasGuestConversation(0, guest.GuestId), Is.True,
                "An open physical conversation must remain authorized after 30 seconds while its visible 40-second decision is pending.");
            yield return ChooseDiscussionOption("Offer $", false);
            Assert.That(guest.Compensated, Is.True, session.LastMessage);
            Assert.That(guest.Memory.CompensationReceived, Is.EqualTo(guest.CompensationCredit));
            Assert.That(guest.CompensationCredit, Is.GreaterThan(0));
            Assert.That(intent.Status, Is.EqualTo(ServiceIntentStatus.Completed));
            Assert.That(guest.Agent.State, Is.EqualTo(GuestAgentState.ReturningFromServiceReception));
            Assert.That(incident.Active, Is.True, "Money does not repair the cold room.");
            Assert.That(session.OfferCompensation(0, guest.GuestId, intent.Id, 1).Success, Is.False);
            ManagementUI.Instance.Close(); LogAssert.NoUnexpectedReceived();
        }

        [UnityTest, Category("ContinuousOperations")]
        public IEnumerator ActualAnsweredPhoneBindsCompensationToThatGuestAndFinishesOnlyTheDiscussion()
        {
            yield return PrepareContinuousCompensationGuest();
            var session = GameSession.Instance; var model = session.Simulation; var guest = model.Guests.Single();
            Assert.That(session.DebugBeginGuestContact(guest.GuestId, GuestContactChannel.Phone).Success, Is.True);
            yield return WaitWithActualSevereCold(guest, () => model.Services.IncomingCall != null, 25,
                "The real guest must reach the authored room phone before it rings.");
            var response = model.Services.IncomingCall;
            Assert.That(session.AnswerIncomingServiceCall(0, response.Id).Success, Is.False);
            var phone = Object.FindAnyObjectByType<ReceptionPhoneInteraction>();
            yield return FaceStation(bootstrap.Players[0], padA, phone, phone.GetComponent<Collider>().bounds.center);
            QueueUse(padA, true);
            yield return WaitForCondition(() => ManagementUI.Instance.IsWakePhoneOpen, 2, "Use the physical reception handset.");
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That(model.Services.CompensationDiscussion(guest.GuestId), Is.Null, "Opening the phone is not answering a guest.");
            yield return ChooseDiscussionOption("Answer reception call", true);
            var intent = model.Services.CompensationDiscussion(guest.GuestId);
            Assert.That(intent, Is.Not.Null);
            Assert.That(intent.ResponseId, Is.EqualTo(response.Id));
            Assert.That(guest.Agent.Activity, Is.EqualTo(GuestActivity.CallReception));
            Assert.That(guest.Agent.ActivityStaged, Is.True);
            Assert.That(session.OfferCompensation(1, guest.GuestId, intent.Id, intent.Revision).Success, Is.False);
            Assert.That(session.OfferCompensation(0, "another-guest", intent.Id, intent.Revision).Success, Is.False);
            Assert.That(session.OfferCompensation(0, guest.GuestId, intent.Id, intent.Revision + 1).Success, Is.False);
            Assert.That(guest.Compensated, Is.False);
            yield return ChooseDiscussionOption("Offer compensation", true);
            Assert.That(guest.Compensated, Is.True, session.LastMessage);
            Assert.That(intent.Status, Is.EqualTo(ServiceIntentStatus.Completed));
            Assert.That(guest.Agent.DirectServiceIntentId, Is.Null);
            Assert.That(guest.Agent.ResponseActionId, Is.Null);
            Assert.That(model.Incidents.Items.Single(value => value.Id == intent.IncidentId).Active, Is.True);
            yield return ChooseDiscussionOption("End conversation / other calls", true);
            Assert.That(ManagementUI.Instance.IsWakePhoneOpen, Is.True, "Ending one discussion preserves outgoing wake-call access.");
            Assert.That(ManagementUI.Instance.PhoneResponseId, Is.Null);
            ManagementUI.Instance.Close(); LogAssert.NoUnexpectedReceived();
        }
    }
}
