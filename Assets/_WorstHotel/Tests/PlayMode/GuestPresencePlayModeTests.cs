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
        public IEnumerator GuestUsesRealBedAndEnclosedShowerThenLeavesAndReturnsThroughClosingDoor()
        {
            var session = GameSession.Instance;
            // Explicit model check-in isolates staging; the existing physical-key test still
            // exercises the complete reception handoff and room-entry route.
            waitScenarioSessionConfig = Object.Instantiate(session.config);
            waitScenarioLivingConfig = Object.Instantiate(session.config.living);
            waitScenarioLivingConfig.firstArrivalSeconds = .2f;
            waitScenarioLivingConfig.arrivalJitterSeconds = 0;
            waitScenarioLivingConfig.firstActivityDelay = 1000;
            waitScenarioLivingConfig.activityDurationMin = 120;
            waitScenarioLivingConfig.activityDurationMax = 120;
            waitScenarioSessionConfig.living = waitScenarioLivingConfig;
            session.config = waitScenarioSessionConfig;
            session.NewGame();
            var offer = session.Plan.Applications.First();
            int price = session.Economy.MinPrice + Mathf.RoundToInt((offer.ReferencePrice - session.Economy.MinPrice) /
                (float)session.Economy.PriceStep) * session.Economy.PriceStep;
            session.Assign(0, offer.Id, 101, price); session.CommitPlan(0);
            var simulation = session.Simulation;
            var guest = simulation.Guests.Single();
            session.AdvanceTime(guest.Agent.ArrivalTime + .2f);
            Assert.That(session.ReportGuestReachedReception(guest.GuestId).Success, Is.True);
            Assert.That(CheckInWithModelKeyFixture(session, 0, guest.GuestId).Success, Is.True);
            Assert.That(session.ReportGuestReachedRoom(guest.GuestId).Success, Is.True);
            session.RaiseChanged();
            yield return null; yield return null;
            var presentation = Object.FindAnyObjectByType<GuestPresentation>();
            var markers = presentation.roomMarkers.Single(room => room.roomId == 101);
            Assert.That(presentation.TryGetGuestTransform(guest.GuestId, out var root), Is.True);
            var body = root.Find("Body");
            Assert.That(guest.Agent.RequiresActivityStaging, Is.True);

            Assert.That(simulation.ForceSleep(guest.GuestId).Success, Is.True);
            session.RaiseChanged();
            yield return null;
            Assert.That(guest.Agent.ActivityStaged, Is.False);
            Assert.That(Mathf.Abs(Vector3.Dot(body.up, Vector3.up)), Is.GreaterThan(.9f), "Sleep must start with an approach, not a pose teleport.");
            yield return WaitForCondition(() => guest.Agent.ActivityStaged, 15, "Guest did not settle onto the bed.");
            yield return null;
            Assert.That(HorizontalDistance(root.position, markers.bedApproach.position), Is.LessThan(.03f));
            Assert.That(Vector3.Distance(body.position, markers.bedAnchor.position), Is.LessThan(.03f));
            Assert.That(Mathf.Abs(Vector3.Dot(body.up, Vector3.up)), Is.LessThan(.03f), "Sleeping body must be horizontal.");
            Assert.That(body.Find("Head").position.y, Is.GreaterThan(1.1f), "Head must remain above the mattress.");
            Assert.That(root.GetComponent<CapsuleCollider>().enabled, Is.False, "No standing interaction capsule beside a sleeping body.");
            Assert.That(markers.door.IsOpen, Is.False);
            var sleepFrame = presentation.CaptureLanGuests().Single(item => item.id == guest.GuestId);
            var replicaParent = new GameObject("Guest pose replication test");
            var replica = presentation.CreateLanReplica(replicaParent.transform, sleepFrame);
            Assert.That(Vector3.Distance(replica.body.position, body.position), Is.LessThan(.001f));
            Assert.That(Quaternion.Angle(replica.body.rotation, body.rotation), Is.LessThan(.01f));

            float quietDemand = simulation.Boiler.Load;
            Assert.That(simulation.ForceActivity(guest.GuestId, GuestActivity.Shower).Success, Is.True);
            session.RaiseChanged();
            yield return new WaitForSecondsRealtime(.25f);
            Assert.That(guest.Agent.ActivityStaged, Is.False);
            Assert.That(simulation.Boiler.Load, Is.EqualTo(quietDemand).Within(.001f));
            Assert.That(Mathf.Abs(Vector3.Dot(body.up, Vector3.up)), Is.LessThan(.95f), "Wake-up should take visible time before walking.");
            yield return WaitForCondition(() => guest.Agent.ActivityStaged, 20, "Guest did not walk into the shower enclosure.");
            yield return null;
            Assert.That(HorizontalDistance(root.position, markers.shower.position), Is.LessThan(.03f));
            Assert.That(body.gameObject.activeSelf, Is.False);
            Assert.That(markers.showerWater.activeSelf && markers.showerCurtain.activeSelf, Is.True);
            Assert.That(root.GetComponent<CapsuleCollider>().enabled, Is.False);
            Assert.That(simulation.Boiler.Load, Is.EqualTo(guest.Application.Archetype.HeatingDemand *
                simulation.LivingSettings.ShowerDemandMultiplier).Within(.001f));
            var showerFrame = presentation.CaptureLanGuests().Single(item => item.id == guest.GuestId);
            Assert.That(showerFrame.bodyVisible, Is.False);
            replica.Apply(showerFrame);
            Assert.That(replica.body.gameObject.activeSelf, Is.False, "LAN must not reveal a hidden showering guest.");

            Assert.That(simulation.ForceLeaveRoom(guest.GuestId).Success, Is.True);
            session.RaiseChanged();
            yield return WaitForCondition(() => guest.Agent.State == GuestAgentState.GuestAway, 30, "Guest did not physically leave through the room door, lobby and exterior exit.");
            yield return new WaitForSecondsRealtime(.9f);
            Assert.That(body.gameObject.activeSelf, Is.False, "Guests away outside the hotel have no visible body at a corridor parking point.");
            Assert.That(guest.Agent.CurrentLocation, Is.EqualTo(GuestLocation.Away));
            Assert.That(HorizontalDistance(root.position, presentation.arrivalSpawn.position), Is.LessThan(.03f));
            Assert.That(markers.showerWater.activeSelf || markers.showerCurtain.activeSelf, Is.False);
            Assert.That(AuthoredGuestRoute.IsOnRoomSide(root.position, markers), Is.False);
            Assert.That(markers.door.IsOpen || markers.door.IsPassageOpen, Is.False, "Guest must close the door after exiting.");
            Assert.That(session.Rooms.Single(room => room.Profile.Id == 101).GuestId, Is.EqualTo(guest.GuestId));
            Assert.That(simulation.ForceReturnRoom(guest.GuestId).Success, Is.True);
            session.RaiseChanged();
            yield return WaitForCondition(() => guest.Agent.InAssignedRoom && guest.Agent.ActivityStaged, 30,
                "Guest did not return and settle into the same owned room.");
            Assert.That(markers.door.IsOpen, Is.False);
            Assert.That(presentation.TryGetGuestDebugSnapshot(guest.GuestId, out var debug), Is.True);
            Assert.That(debug.AtActivityAnchor && debug.BodyVisible, Is.True);
            Assert.That(debug.Destination, Does.Contain("RestAnchor"));
            replica.Apply(presentation.CaptureLanGuests().Single(item => item.id == guest.GuestId));
            Assert.That(replica.body.gameObject.activeSelf, Is.True);
            // New background activities must physically move to their own world fixtures.
            foreach (var activity in new[] { GuestActivity.Unpack, GuestActivity.Work, GuestActivity.PhoneCall })
            {
                Assert.That(simulation.ForceActivity(guest.GuestId, activity).Success, Is.True);
                session.RaiseChanged();
                yield return WaitForCondition(() => guest.Agent.ActivityStaged, 20, activity + " never reached its physical fixture.");
                yield return null;
                var anchor = activity == GuestActivity.Work ? markers.deskAnchor :
                    activity == GuestActivity.PhoneCall ? markers.phoneAnchor : markers.unpackAnchor;
                Assert.That(HorizontalDistance(root.position, anchor.position), Is.LessThan(.03f));
                Assert.That(body.gameObject.activeSelf, Is.True);
                var frame = presentation.CaptureLanGuests().Single(item => item.id == guest.GuestId);
                Assert.That(frame.phoneVisible, Is.EqualTo(activity == GuestActivity.PhoneCall));
                replica.Apply(frame);
                Assert.That(replica.phone.gameObject.activeSelf, Is.EqualTo(activity == GuestActivity.PhoneCall));
                Assert.That(markers.showerWater.activeSelf, Is.False);
            }
            presentation.ReleaseLanReplica(guest.GuestId, replica.root);
            Object.Destroy(replicaParent);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
