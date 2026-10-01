using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace WorstHotel.Tests
{
    public sealed partial class Phase1PlayModeTests
    {
        [UnityTest, Category("ContinuousOperations"), Category("AutomaticSales")]
        public IEnumerator OperationalRoomWorkUsesRealHoldAndPhysicalStaffKeyOpensOnlyLockout()
        {
            bootstrap.ConfigureSolo(); InputSystem.RemoveDevice(padB); padB = null;
            var session = GameSession.Instance; ManagementUI.Instance.Close();
            var h = session.Simulation; var actor = bootstrap.Players[0];
            var room = session.Rooms.Single(r => r.Profile.Id == 101);
            Assert.That(room.Cleanliness, Is.EqualTo(Cleanliness.Dirty));
            // Labelled physical fixture; causal buildup and overnight persistence have separate model coverage.
            typeof(RoomState).GetProperty("Disorder").SetValue(room, RoomDisorder.Waste | RoomDisorder.Towels | RoomDisorder.Chair);
            yield return null; yield return null;
            var resets = Object.FindObjectsByType<RoomResetInteraction>(FindObjectsSortMode.None).Where(r => r.roomId == 101).ToArray();
            Assert.That(resets.Length, Is.EqualTo(3));
            foreach (var reset in resets)
            {
                TestContext.Out.WriteLine("Physical reset: " + reset.element);
                var target = reset.element == RoomDisorder.Chair ? reset.transform.TransformPoint(new Vector3(0, .64f, -.3f)) :
                    reset.transform.position + Vector3.up * (reset.element == RoomDisorder.Waste ? .45f : .15f);
                Vector3 approach = reset.element == RoomDisorder.Chair ? new Vector3(-7.35f, .08f, 7.65f) :
                    reset.element == RoomDisorder.Towels ? new Vector3(-5.1f, .08f, 8.2f) : new Vector3(reset.transform.position.x + 1.15f, .08f, reset.transform.position.z - .5f);
                var pose = new GameObject("Room work reach fixture"); pose.transform.position = approach;
                actor.ResetToSpawn(pose.transform); Object.Destroy(pose); yield return WaitForGroundContact(actor);
                yield return AimAtKeyScenarioPoint(actor, padA, () => target);
                Assert.That(actor.Interactor.Focused, Is.SameAs(reset), "Reset target must be visible and reachable: " + reset.element +
                    " actor=" + actor.transform.position + " target=" + target);
                if (reset.element == RoomDisorder.Waste)
                {
                    System.IO.Directory.CreateDirectory("docs/screenshots/operations066");
                    var capture = VerificationOffscreenCapture.Capture(new[] { actor });
                    System.IO.File.WriteAllBytes("docs/screenshots/operations066/used-room.png", capture.EncodeToPNG()); Object.Destroy(capture);
                    QueueUse(padA, true); yield return new WaitForSecondsRealtime(.45f); QueueUse(padA, false);
                    yield return null; yield return null;
                    Assert.That(room.Disorder.HasFlag(reset.element), Is.True, "A tap cannot complete the work.");
                }
                QueueUse(padA, true);
                yield return WaitForCondition(() => !room.Disorder.HasFlag(reset.element), reset.seconds + 3, "Held reset did not finish: " + reset.element);
                QueueUse(padA, false); yield return null; yield return null;
            }
            Assert.That(room.Cleanliness, Is.EqualTo(Cleanliness.Dirty), "Removing clutter cannot bypass dirty linen.");

            GoodOperational(h.DebugSpawnGuest(GuestKind.Budget, 102)); session.AdvanceTime(1.2f);
            var g = h.Guests.Last(s => s.RoomId == 102);
            GoodOperational(h.SignalGuestReachedReception(g.GuestId));
            GoodOperational(h.Keys.PickUp(0, 102)); GoodOperational(h.CheckIn(0, g.GuestId)); GoodOperational(h.SignalGuestReachedRoom(g.GuestId));
            h.Tick(.25f);
            typeof(RoomKeySystem).GetMethod("LeaveInside", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(h.Keys, new object[] { g.GuestId, 102 });
            typeof(GuestStay).GetProperty("KeyLossConsidered").SetValue(g, true);
            GoodOperational(h.ForceLeaveRoom(g.GuestId)); GoodOperational(h.SignalGuestLeftRoom(g.GuestId));
            GoodOperational(h.ForceReturnRoom(g.GuestId)); GoodOperational(h.SignalGuestReachedReception(g.GuestId));
            TestContext.Out.WriteLine("Taking staff key");
            var key = PhysicalKey(0); yield return null; yield return null;
            var keyPose = new GameObject("Staff key reach fixture"); keyPose.transform.position = new Vector3(-4.2f, .08f, 4.65f);
            actor.ResetToSpawn(keyPose.transform); Object.Destroy(keyPose); yield return WaitForGroundContact(actor);
            yield return GrabFocusedRoomKey(actor, padA, key);
            var door = Object.FindObjectsByType<DoorInteractable>(FindObjectsSortMode.None).Single(d => d.roomId == 102);
            TestContext.Out.WriteLine("Carrying staff key to door");
            yield return WalkWithKey(actor, padA, key, new Vector3(-3.8f, .08f, 5.0f));
            yield return WalkWithKey(actor, padA, key, new Vector3(.7f, .08f, 5.0f));
            yield return WalkWithKey(actor, padA, key, new Vector3(.7f, .08f, door.transform.position.z));
            yield return AimAtKeyScenarioPoint(actor, padA, () => door.doorPivot.GetComponentsInChildren<Collider>().First().bounds.center);
            Assert.That(actor.Interactor.Focused, Is.SameAs(door));
            Assert.That(door.GetPrompt(actor.Interactor), Does.Contain("STAFF key"));
            QueueUse(padA, true); yield return null; yield return null; QueueUse(padA, false); yield return null;
            Assert.That(g.LockedOut, Is.False); Assert.That(door.IsOpen, Is.True);
            Assert.That(key.State.PlayerId, Is.EqualTo(0));
            Assert.That(h.Keys.Find(102).Location, Is.EqualTo(RoomKeyLocation.LeftInside));
            LogAssert.NoUnexpectedReceived();
        }
        static void GoodOperational(CommandResult result) => Assert.That(result.Success, Is.True, result.Message);
    }
}
