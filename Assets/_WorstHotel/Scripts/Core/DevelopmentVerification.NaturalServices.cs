#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem.LowLevel;

namespace WorstHotel
{
    public sealed partial class DevelopmentVerification
    {
        IEnumerator VerifyNaturalServiceContacts()
        {
            facts.Add("NATURAL CONTACT FIXTURES: two separate hotels use natural communication with production observation, self-help, tolerance, ring, reception and retry timing. Eligibility=0 suppresses unrelated preferences; one explicit private cold intent bypasses only random selection. Actual room temperature is held at preferred minimum minus 1.5C during the causal route waits, making failed self-help reproducible. No guest state, pose, response phase, action version, or arrival callback is injected. Empty staff viewpoints/model initial keys remain labelled adapters. Staff answer/talk use the owned controller and real interaction ray.");
            yield return ResetAgencyHotel();
            yield return StartServiceFixture(GuestKind.Business, false);
            var caller = agencyModel.Guests.Single();
            agencyActivities.Remove(caller.GuestId); // Do not overwrite the guest's real response activities.
            driveSpeed = 1;
            yield return PrepareNaturalColdIntent(caller);
            var request = agencyModel.Services.Cases.Single(c => c.Active);
            var response = request.Response;
            var room = session.Rooms.Single(r => r.Profile.Id == caller.RoomId);
            var marker = guests.roomMarkers.Single(r => r.roomId == caller.RoomId);
            Require(guests.TryGetGuestTransform(caller.GuestId, out var body), "natural caller has a real body");
            int setting = room.RadiatorSetting;
            float load = agencyModel.Boiler.Load;
            yield return WaitNaturalCold(caller, () => response.Phase == GuestResponsePhase.SelfResponding, 12,
                "production observation starts a radiator visit");
            Require(!response.SelfResponseAttempted && room.RadiatorSetting == setting,
                "guest cannot remotely turn the radiator before arrival");
            yield return WaitNaturalCold(caller, () => response.SelfResponseApplied, 25,
                "actual guest reaches radiator and turns real valve");
            Require(ServiceDistance(body.position, marker.radiatorAnchor.position) < .08f &&
                room.RadiatorSetting == setting + 1 && agencyModel.Boiler.Load > load && response.StaffActionAt < 0,
                "self-help is physical, changes real boiler demand and is not staff credit");
            yield return PlaceServiceStaff(new Vector3(-7.98f, .08f, 9.2f), marker.radiatorTarget.position);
            yield return Capture("natural-self-help", "DIAGNOSTIC sustained real cold / production observation and actual radiator arrival changed the room valve / no staff intervention");
            var phone = FindAnyObjectByType<ReceptionPhoneInteraction>();
            yield return PlaceServiceStaff(phone.transform.position + Vector3.back * 1.45f, phone.GetComponent<Collider>().bounds.center);
            yield return WaitNaturalCold(caller, () => agencyModel.Services.IncomingCall == response, 40,
                "production tolerance and real room-phone arrival produce incoming call");
            Require(ServiceDistance(body.position, marker.roomPhoneAnchor.position) < .08f &&
                caller.Agent.Activity == GuestActivity.CallReception && caller.Agent.ActivityStaged &&
                !request.IsKnownToHotel && caller.Memory.ServicesRequested == 0,
                "ringing is a physically reached phone, not disclosed or accepted service");
            Require(guests.CaptureLanGuests().Single(g => g.id == caller.GuestId).phoneVisible,
                "actual caller holds their visible room telephone");
            Require(FindAnyObjectByType<IncomingServicePhoneCue>().IsRinging, "actual reception ring cue is active");
            Require(!session.AnswerIncomingServiceCall(0, response.Id).Success,
                "a known response ID cannot replace picking up the physical phone");
            yield return Until(() => coop.Players[0].Interactor.Focused == phone, 3, "reception phone is the actual input target");
            yield return PressMenu(GamepadButton.South);
            Require(ManagementUI.Instance.IsWakePhoneOpen && !request.IsKnownToHotel,
                "opening the phone menu does not answer or reveal its caller");
            yield return Capture("natural-phone-ringing", "Actual reception telephone and generic incoming-call choice before disclosure / no room or reason exposed");
            yield return ChooseServiceAction("Answer reception call");
            Require(response.CommunicatedAt >= 0 && request.IsKnownToHotel && caller.Memory.ServicesRequested == 1 &&
                request.Status != ServiceStatus.InProgress && request.Status != ServiceStatus.Fulfilled &&
                agencyModel.Services.IncomingCall == null && caller.BlanketComfortBonus == 0 && caller.Memory.ServicesFulfilled == 0,
                "actual staff answer discloses one existing case without accepting or fulfilling it");
            yield return Capture("natural-phone-heard", "Actual controller answered the physical incoming call / guest explains a problem / no help chosen automatically");
            facts.Add("NATURAL PHONE PASS: response=" + response.Id + "; selfValve=" + setting + "->" + room.RadiatorSetting +
                "; selfAt=" + response.SelfResponseAt.ToString("F2") + "; heardAt=" + response.CommunicatedAt.ToString("F2") +
                "; physicalRoomPhone=True; actualReceptionAnswer=True; requestsRemembered=" + caller.Memory.ServicesRequested +
                "; acceptedAutomatically=False; fulfilledAutomatically=False; productionContactTiming=True.");
            ManagementUI.Instance.Close();

            yield return ResetAgencyHotel();
            yield return StartServiceFixture(GuestKind.Business, false);
            var visitor = agencyModel.Guests.Single();
            agencyActivities.Remove(visitor.GuestId); driveSpeed = 1;
            yield return PrepareNaturalColdIntent(visitor);
            request = agencyModel.Services.Cases.Single(c => c.Active); response = request.Response;
            float checkout = visitor.Agent.CheckoutTime;
            Require(session.DebugBeginGuestContact(visitor.GuestId, GuestContactChannel.Reception).Success,
                "explicit channel selection starts a real desk trip from the measured cold concern");
            Require(visitor.Agent.State == GuestAgentState.GoingToServiceReception && response.AttemptStartedAt < 0,
                "a scheduled reception trip is not an arrival or disclosed request");
            // Staff wait away from the guest's route, then approach the stationary body from the front.
            yield return PlaceServiceStaff(new Vector3(.2f, .08f, -2.3f), new Vector3(-3, 1.3f, 1.3f));
            yield return WaitNaturalCold(visitor, () => visitor.Agent.State == GuestAgentState.WaitingAtServiceReception, 35,
                "guest leaves through their own door and physically reaches reception");
            Require(guests.TryGetGuestTransform(visitor.GuestId, out body) &&
                guests.receptionPlaces.Any(p => ServiceDistance(p.position, body.position) < .08f),
                "guest body has reached an authored reception position");
            var target = FindObjectsByType<GuestReceptionInteraction>(FindObjectsSortMode.None).Single(t => t.GuestId == visitor.GuestId);
            Vector3 aim = target.GetComponent<Collider>().bounds.center;
            yield return PlaceServiceStaff(new Vector3(aim.x, .08f, aim.z - 1.45f), aim);
            Require(!request.IsKnownToHotel && !target.GetPrompt(coop.Players[0].Interactor).Contains("cold"),
                "waiting guest provides only a generic conversation cue");
            yield return Capture("natural-reception-waiting", "DIAGNOSTIC contact channel selected / actual room-to-desk trip completed / generic guest cue before staff conversation");
            yield return Until(() => coop.Players[0].Interactor.Focused == target, 3, "waiting guest body is the real input target");
            yield return PressMenu(GamepadButton.South);
            Require(ManagementUI.Instance.IsGuestContextOpen && response.CommunicatedAt >= 0 && request.IsKnownToHotel &&
                response.Channel == GuestContactChannel.Reception && visitor.Memory.ServicesRequested == 1 &&
                request.Status != ServiceStatus.InProgress && request.Status != ServiceStatus.Fulfilled,
                "real desk conversation hears the existing concern without choosing help");
            Require(visitor.RoomId == 101 && session.Rooms.Single(r => r.Profile.Id == 101).GuestId == visitor.GuestId &&
                agencyModel.Keys.Find(101).Location == RoomKeyLocation.HeldByGuest &&
                agencyModel.Keys.Find(101).GuestId == visitor.GuestId && visitor.Agent.CheckoutTime == checkout,
                "temporary reception conversation retains the room, key and checkout agreement");
            yield return Capture("natural-reception-heard", "Actual controller conversation with the visiting guest / contextual choices after disclosure / room and key retained");
            ManagementUI.Instance.Close();
            yield return WaitNaturalCold(visitor, () => visitor.Agent.InAssignedRoom && visitor.Agent.ActivityStaged && AtAnchor(visitor.GuestId),
                40, "heard guest walks back through the same room door and settles");
            Require(visitor.Agent.ResponseActionId == null && visitor.Memory.ServicesRequested == 1 && visitor.BlanketComfortBonus == 0,
                "the natural return does not repeat the case or deliver a blanket");
            facts.Add("NATURAL RECEPTION PASS: response=" + response.Id + "; explicitChannelSelection=True; realOutboundAndReturn=True; actualStaffConversation=True; roomKeyAndCheckoutRetained=True; requestedMemory=1; acceptedAutomatically=False; fulfilledAutomatically=False.");
        }

        IEnumerator PrepareNaturalColdIntent(GuestStay guest)
        {
            Require(agencyModel.Services.Settings.NaturalCommunicationEnabled, "natural communication fixture is enabled");
            Require(session.DebugSetMildCold(guest.GuestId).Success, "explicit measured mild-cold cause setup");
            yield return WaitNaturalCold(guest, () => agencyModel.Services.Responses.Any(r => r.GuestId == guest.GuestId &&
                r.IncidentId != null && r.CommunicatedAt < 0), 3, "real needs tick creates the private incident response");
            Require(session.DebugForceService(guest.GuestId, ServiceKind.ExtraBlanket).Success,
                "explicit private intent bypasses random service selection, not communication");
            var item = agencyModel.Services.Cases.Single(c => c.Active);
            Require(item.Response != null && !item.IsKnownToHotel && guest.Memory.ServicesRequested == 0,
                "private intent is linked to one measured response and not reported to staff");
        }

        IEnumerator WaitNaturalCold(GuestStay guest, Func<bool> condition, float seconds, string reason)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                // Explicit environment fixture only. Production navigation and fixed-step needs
                // still decide perception, response phases and real anchor completion.
                Require(agencyModel.SetRoomTemperature(guest.RoomId,
                    guest.Application.Archetype.Needs.PreferredTemperatureMin - 1.5f).Success, "sustain labelled cold environment");
                yield return null;
            }
            guests.TryGetGuestDebugSnapshot(guest.GuestId, out var state);
            Require(condition(), reason + " / guest=" + guest.Agent.State + " activity=" + guest.Agent.Activity +
                " path=" + state.PathStatus + " destination=" + state.Destination);
        }
    }
}
#endif
