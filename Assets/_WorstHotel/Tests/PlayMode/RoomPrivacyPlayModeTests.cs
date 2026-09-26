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
        [UnityTest]
        public IEnumerator ActualDoorRaycastKnocksRequestsPermissionRefusesPrivateEntryAndRequiresDeliberateEmergencyHold()
        {
            // The model fixture owns room assignment; this case measures real door colliders,
            // actor focus and input edges. The guest route/pose has its own physical scenario.
            Object.FindAnyObjectByType<GuestPresentation>().enabled = false;
            var session = GameSession.Instance;
            var offer = session.Plan.Applications.First();
            int price = session.Economy.MinPrice + Mathf.RoundToInt((offer.ReferencePrice - session.Economy.MinPrice) / (float)session.Economy.PriceStep) * session.Economy.PriceStep;
            session.Assign(0, offer.Id, 101, price);
            Assert.That(session.Plan.Assignments.Count, Is.EqualTo(1));
            session.CommitPlan(0);
            var simulation = session.Simulation;
            var guest = simulation.Guests.Single();
            session.AdvanceTime(guest.Agent.ArrivalTime + .2f);
            Assert.That(session.ReportGuestReachedReception(guest.GuestId).Success, Is.True);
            Assert.That(CheckInWithModelKeyFixture(session, 0, guest.GuestId).Success, Is.True);
            Assert.That(session.ReportGuestReachedRoom(guest.GuestId).Success, Is.True);
            var door = GameObject.Find("Door101").GetComponent<DoorInteractable>();
            var actor = bootstrap.Players[0];
            yield return ApproachPrivacyDoor(actor, false, door);
            Assert.That(door.IsOpen, Is.False);
            QueueUse(padA, true); yield return null; yield return null;
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That(door.Conversation.HasAnswered(0), Is.True);
            Assert.That(door.IsOpen, Is.False, "First normal use is a knock, never entry.");
            QueueUse(padA, true); yield return null; yield return null;
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That(ManagementUI.Instance.IsGuestContextOpen, Is.True);
            Assert.That(door.IsOpen, Is.False, "Talking offers permission as a choice; it does not open the door automatically.");
            QueueUse(padA, true); yield return null; yield return null; yield return null;
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That(door.IsOpen, Is.True, "Selecting permission obtains a resting guest's consent.");
            yield return WaitForCondition(() => !ManagementUI.Instance.IsOpen, 2, "Granted entry returns control to the player.");

            simulation.ForceSleep(guest.GuestId);
            door.RequestClose();
            yield return new WaitForSecondsRealtime(1);
            yield return ApproachPrivacyDoor(actor, false, door);
            Assert.That(door.IsLocked, Is.True);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                QueueUse(padA, true); yield return null; yield return null;
                QueueUse(padA, false); yield return null; yield return null;
                Assert.That(door.IsOpen, Is.False, "Private activity refuses repeated casual use.");
            }
            Assert.That(door.GetPrompt(actor.Interactor), Does.Contain("I'm sleeping. Please come back later"));

            InputSystem.QueueStateEvent(padA, new GamepadState().WithButton(GamepadButton.West));
            yield return null; yield return null;
            QueueUse(padA, false); yield return null;
            Assert.That(door.GetPrompt(actor.Interactor), Does.Contain("EMERGENCY"));
            QueueUse(padA, true);
            yield return new WaitForSecondsRealtime(.5f);
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That(door.IsOpen, Is.False, "A tap or interrupted hold does not bypass privacy.");
            QueueUse(padA, true);
            yield return WaitForCondition(() => door.IsOpen, RoomNoiseInteraction.EmergencyHoldSeconds + 2, "Deliberate emergency entry did not open the physical door.");
            QueueUse(padA, false); yield return null;
            Assert.That(simulation.LastEvent, Does.Contain("emergency access"));

            door.RequestClose();
            yield return new WaitForSecondsRealtime(1);
            yield return ApproachPrivacyDoor(actor, true, door);
            QueueUse(padA, true); yield return null; yield return null;
            QueueUse(padA, false); yield return null;
            Assert.That(door.IsOpen, Is.True, "Privacy must never trap a member of staff already inside.");
            Assert.That(simulation.DebugCheckoutGuest(guest.GuestId).Success, Is.True);
            Assert.That(session.Rooms.Single(room => room.Profile.Id == 101).PrivacyState, Is.EqualTo(RoomPrivacyState.Public));
            LogAssert.NoUnexpectedReceived();
        }

        IEnumerator ApproachPrivacyDoor(FirstPersonController actor, bool inside, DoorInteractable door)
        {
            QueueUse(padA, false);
            var pose = new GameObject("Privacy door approach");
            var position = door.transform.TransformPoint(new Vector3(0, 0, inside ? 1.7f : -1.7f));
            position.y = .08f;
            var aim = door.transform.TransformPoint(new Vector3(0, 1.55f, 0));
            var forward = aim - position; forward.y = 0;
            pose.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward));
            actor.ResetToSpawn(pose.transform);
            Object.Destroy(pose);
            yield return WaitForGroundContact(actor);
            yield return AimAtKeyScenarioPoint(actor, padA, () => aim);
            Assert.That(actor.Interactor.Focused, Is.SameAs(door), "The real moving door leaf must receive the player's use input.");
        }
    }
}
