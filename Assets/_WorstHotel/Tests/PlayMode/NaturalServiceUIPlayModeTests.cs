using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace WorstHotel.Tests
{
    public sealed partial class Phase1PlayModeTests
    {
        ServiceCase PreparePrivateColdConversation()
        {
            var session = GameSession.Instance;
            var guest = session.Simulation.Guests.Single();
            Assert.That(session.DebugSetMildCold(guest.GuestId).Success, Is.True);
            session.AdvanceTime(.2f); // The real needs/incident tick establishes the source before the private intent.
            Assert.That(session.DebugForceService(guest.GuestId, ServiceKind.ExtraBlanket).Success, Is.True);
            var item = session.Simulation.Services.Cases.Single(c => c.Active);
            Assert.That(item.Response, Is.Not.Null);
            Assert.That(item.IsKnownToHotel, Is.False);
            Assert.That(guest.Memory.ServicesRequested, Is.Zero);
            return item;
        }

        [UnityTest]
        public IEnumerator NaturalIncomingPhoneHidesPrivateCaseUntilControllerAnswersAndDoesNotAcceptOrDeliverForThem()
        {
            yield return PrepareServiceGuestFixture(true);
            var session = GameSession.Instance;
            var guest = session.Simulation.Guests.Single();
            var item = PreparePrivateColdConversation();
            var ui = ManagementUI.Instance;
            ui.OpenReceptionServiceBoard(0);
            yield return null; yield return null; yield return null;
            Assert.That(ui.ServiceOptionTitles.Any(t => t.Contains("101") || t.Contains("cold")), Is.False,
                "Actual board input choices must hide the private request, including its room and reason.");
            var board = Object.FindAnyObjectByType<ReceptionServiceBoardInteraction>();
            Assert.That(board.summary.text, Does.Contain("0 REQUESTS"),
                "The physical board must not count a private intent as a request already heard by staff.");
            Assert.That(session.RespondToService(0, item.Id, true).Success, Is.False);
            ui.Close();
            // Explicit caller-anchor adapter prepares a ringing call. This fixture proves physical
            // staff input/disclosure, not guest walking; natural route fixtures cover that separately.
            Object.FindAnyObjectByType<GuestPresentation>().enabled = false;
            Assert.That(session.DebugBeginGuestContact(guest.GuestId, GuestContactChannel.Phone).Success, Is.True);
            Assert.That(session.Simulation.SignalGuestResponseAnchorReached(guest.GuestId, item.Response.Id,
                item.Response.ActionVersion, GuestResponseAnchor.RoomPhone).Success, Is.True);
            Assert.That(session.AnswerIncomingServiceCall(0, item.Response.Id).Success, Is.False,
                "Knowing a response ID cannot replace the physical telephone grant.");
            var phone = Object.FindAnyObjectByType<ReceptionPhoneInteraction>();
            yield return FaceStation(bootstrap.Players[0], padA, phone, phone.GetComponent<Collider>().bounds.center);
            Assert.That(phone.GetPrompt(bootstrap.Players[0].Interactor), Does.Not.Contain("101"));
            QueueUse(padA, true);
            yield return WaitForCondition(() => ui.IsWakePhoneOpen, 2, "Use the real telephone to pick up its menu.");
            QueueUse(padA, false); yield return null; yield return null; yield return null;
            Assert.That(item.IsKnownToHotel, Is.False, "Picking up the phone menu is not answering its call.");
            Assert.That(ui.ServiceOptionTitles.First(), Is.EqualTo("Answer reception call"));
            QueueUse(padA, true);
            yield return WaitForCondition(() => item.IsKnownToHotel, 2, "Controller answer must disclose the existing case.");
            QueueUse(padA, false); yield return null; yield return null; yield return null;
            Assert.That(ui.PhoneResponseId, Is.EqualTo(item.Response.Id));
            Assert.That(item.Status, Is.EqualTo(ServiceStatus.Requested));
            Assert.That(guest.Memory.ServicesRequested, Is.EqualTo(1));
            Assert.That(session.Simulation.Services.IncomingCall, Is.Null);
            int stock = session.Simulation.Services.BlanketsAvailable;
            QueueUse(padA, true);
            yield return WaitForCondition(() => item.Status == ServiceStatus.InProgress, 2, "A separate choice accepts help.");
            QueueUse(padA, false);
            Assert.That(item.Active, Is.True);
            Assert.That(guest.BlanketComfortBonus, Is.Zero);
            Assert.That(session.Simulation.Services.BlanketsAvailable, Is.EqualTo(stock));
            Assert.That(guest.Memory.ServicesFulfilled, Is.Zero);
            ui.Close(); ui.OpenReceptionServiceBoard(0);
            yield return null; yield return null; yield return null;
            Assert.That(ui.ServiceOptionTitles.Any(t => t.Contains("101") && t.Contains("cold")), Is.True);
            Assert.That(board.summary.text, Does.Contain("1 REQUESTS"));
            Assert.That(session.Simulation.Services.Cases.Count(c => c.GuestId == guest.GuestId), Is.EqualTo(1));
            Assert.That(guest.Memory.ServicesRequested, Is.EqualTo(1));
            ui.Close(); LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator NaturalGuestWalksToReceptionThenControllerConversationDisclosesTheConcern()
        {
            yield return PrepareServiceGuestFixture(true);
            var session = GameSession.Instance;
            var guest = session.Simulation.Guests.Single();
            var item = PreparePrivateColdConversation();
            Assert.That(session.DebugBeginGuestContact(guest.GuestId, GuestContactChannel.Reception).Success, Is.True);
            // The initial room assignment above is an explicit model fixture. This new contact
            // trip uses the production scene route and callback: no reception arrival is injected.
            yield return WaitForCondition(() => guest.Agent.State == GuestAgentState.WaitingAtServiceReception,
                30, "The actual guest body must reach reception before the desk conversation.");
            Assert.That(item.IsKnownToHotel, Is.False);
            Assert.That(GuestLabels.ContactCue(session.Simulation), Does.Contain("reception"));
            var target = Object.FindObjectsByType<GuestReceptionInteraction>(FindObjectsSortMode.None).Single(t => t.GuestId == guest.GuestId);
            yield return FaceStation(bootstrap.Players[0], padA, target, target.GetComponent<Collider>().bounds.center);
            Assert.That(target.GetPrompt(bootstrap.Players[0].Interactor), Does.Not.Contain("cold"));
            QueueUse(padA, true);
            yield return WaitForCondition(() => ManagementUI.Instance.IsGuestContextOpen && item.IsKnownToHotel, 2,
                "Talk to the waiting body through the real interaction ray and controller.");
            QueueUse(padA, false);
            Assert.That(item.Response.Channel, Is.EqualTo(GuestContactChannel.Reception));
            Assert.That(item.Status, Is.EqualTo(ServiceStatus.Requested));
            Assert.That(guest.Memory.ServicesRequested, Is.EqualTo(1));
            Assert.That(guest.RoomId, Is.EqualTo(101));
            Assert.That(session.Rooms.Single(r => r.Profile.Id == 101).GuestId, Is.EqualTo(guest.GuestId));
            Assert.That(guest.BlanketComfortBonus, Is.Zero);
            ManagementUI.Instance.Close(); LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator NaturalRoomKnockDisclosesOnlyAfterActualSecondUseAndKeepsPermissionSeparate()
        {
            yield return PrepareServiceGuestFixture(true);
            Object.FindAnyObjectByType<GuestPresentation>().enabled = false;
            var session = GameSession.Instance;
            var item = PreparePrivateColdConversation();
            var guest = session.Simulation.Guests.Single();
            var door = GameObject.Find("Door101").GetComponent<DoorInteractable>();
            yield return ApproachPrivacyDoor(bootstrap.Players[0], false, door);
            Assert.That(session.DiscussRoomConcern(0, guest.GuestId, item.Response.Id).Success, Is.False);
            QueueUse(padA, true); yield return null; yield return null;
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That(door.Conversation.HasAnswered(0), Is.True);
            Assert.That(item.IsKnownToHotel, Is.False, "The first knock is an answer, not a disclosed concern.");
            Assert.That(door.IsOpen, Is.False);
            QueueUse(padA, true);
            yield return WaitForCondition(() => ManagementUI.Instance.IsGuestContextOpen && item.IsKnownToHotel, 2,
                "A physical follow-up conversation must disclose the room concern.");
            QueueUse(padA, false);
            Assert.That(item.Response.Channel, Is.EqualTo(GuestContactChannel.RoomConversation));
            Assert.That(item.Status, Is.EqualTo(ServiceStatus.Requested));
            Assert.That(door.IsOpen, Is.False, "Discussing the concern never silently grants entry.");
            Assert.That(guest.Memory.ServicesRequested, Is.EqualTo(1));
            Assert.That(guest.BlanketComfortBonus, Is.Zero);
            ManagementUI.Instance.Close(); LogAssert.NoUnexpectedReceived();
        }
    }
}
