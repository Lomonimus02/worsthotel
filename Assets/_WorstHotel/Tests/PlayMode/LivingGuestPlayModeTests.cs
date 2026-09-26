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
        public IEnumerator RealReceptionArrivalStopsBothPlayersWaitWhenStaffCanActuallyCheckIn()
        {
            StartOrdinaryTestShift();
            var simulation = GameSession.Instance.Simulation;
            Assert.That(simulation.LivingEnabled, Is.True);
            var first = simulation.Guests.OrderBy(guest => guest.Agent.ArrivalTime).First();
            Assert.That(first.Agent.State, Is.EqualTo(GuestAgentState.Scheduled));
            yield return ConsentToWait();
            yield return WaitForCondition(() => first.Agent.State == GuestAgentState.WaitingForCheckIn, 12,
                "The guest's actual reception arrival did not interrupt WAIT.");
            Assert.That(simulation.Clock.Speed, Is.EqualTo(1));
            Assert.That(simulation.Elapsed, Is.GreaterThanOrEqualTo(first.Agent.ArrivalTime),
                "A scheduled exterior arrival is ambient; WAIT stops when the real guest can contact reception.");
            Assert.That(Waiter.HasVoted(0) || Waiter.HasVoted(1), Is.False);
            Assert.That(Waiter.IsAwaitingRelease(0) && Waiter.IsAwaitingRelease(1), Is.True);
            Assert.That(first.Agent.CheckedIn, Is.False);
            Assert.That(GameSession.Instance.Rooms.Any(room => room.Occupied), Is.False);
            yield return new WaitForSecondsRealtime(WaitHoldSeconds + 0.15f);
            Assert.That(Waiter.IsWaiting, Is.False, "Buttons held across an arrival cannot automatically consent again.");
            Assert.That(Time.timeScale, Is.EqualTo(1));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator GuestWalksToReceptionReceivesPhysicalKeyThenWalksThroughDoorAndUsesRoom()
        {
            var session = GameSession.Instance;
            Assert.That(session.Simulation.LivingEnabled, Is.True);
            var offer = session.Plan.Applications.First();
            int min = session.Economy.MinPrice, step = session.Economy.PriceStep;
            int price = min + Mathf.RoundToInt((offer.ReferencePrice - min) / (float)step) * step;
            session.Assign(0, offer.Id, 101, price);
            session.CommitPlan(0);
            Assert.That(session.Phase, Is.EqualTo(DayPhase.Service));
            var guest = session.Simulation.Guests.Single();
            var presentation = UnityEngine.Object.FindAnyObjectByType<GuestPresentation>();
            Assert.That(presentation, Is.Not.Null);

            // Skip only the scheduled pre-arrival interval. Real scene routing supplies every travel-completion command.
            session.AdvanceTime(guest.Agent.ArrivalTime + 0.2f - session.Simulation.Elapsed);
            yield return null;
            Assert.That(presentation.TryGetGuestTransform(guest.GuestId, out var visual), Is.True);
            Vector3 entryPosition = visual.position;
            yield return WaitForCondition(() => guest.Agent.State == GuestAgentState.WaitingForCheckIn, 18,
                "Guest did not physically reach the authored reception queue.");
            Assert.That(Vector3.Distance(entryPosition, visual.position), Is.GreaterThan(1),
                "Arrival must produce visible travel instead of only changing a data state.");
            Assert.That(guest.Agent.CheckedIn, Is.False);
            Assert.That(session.Rooms.Single(room => room.Profile.Id == 101).Occupied, Is.False);
            Assert.That(session.Simulation.Boiler.Load, Is.Zero);

            var reception = UnityEngine.Object.FindObjectsByType<GuestReceptionInteraction>(FindObjectsSortMode.None)
                .Single(target => target.GuestId == guest.GuestId);
            var capsule = reception.GetComponent<CapsuleCollider>();
            Assert.That(capsule.enabled && !capsule.isTrigger, Is.True, "Check-in requires a real raycast surface.");
            yield return FaceStation(bootstrap.Players[0], padA, reception,
                reception.transform.TransformPoint(capsule.center));
            QueueUse(padA, true);
            yield return new WaitForSecondsRealtime(.25f);
            QueueUse(padA, false);
            yield return null; yield return null;
            Assert.That(guest.Agent.CheckedIn, Is.False, "An empty hand cannot generate an imaginary room key.");
            Assert.That(reception.HoldProgress, Is.Zero);
            yield return TakeRoomKeyFromActualRack(0, 101);
            yield return CarryRackKeyToReceptionGuest(0, 101, reception);
            yield return GiveHeldKeyByInstantUse(0, guest, 101);
            Assert.That(session.Rooms.Single(room => room.Profile.Id == 101).GuestId, Is.EqualTo(guest.GuestId));
            Vector3 receptionPosition = visual.position;
            yield return WaitForCondition(() => guest.Agent.HasReachedRoom, 35,
                "The checked-in guest did not walk through the authored doorway to the room.");
            Assert.That(Vector3.Distance(receptionPosition, visual.position), Is.GreaterThan(4));
            var roomTarget = GameObject.Find("RoomTarget101");
            Assert.That(roomTarget, Is.Not.Null);
            Assert.That(HorizontalDistance(visual.position, roomTarget.transform.position), Is.LessThan(0.25f));
            Assert.That(guest.Agent.InAssignedRoom, Is.True);
            Assert.That(session.Simulation.Boiler.Load, Is.GreaterThan(0));
            Assert.That(session.Simulation.ForceActivity(guest.GuestId, GuestActivity.Shower).Success, Is.True);
            Assert.That(guest.Agent.ActivityStaged, Is.False, "A shower request must first walk to its actual shower anchor.");
            Assert.That(session.Simulation.Boiler.Load, Is.LessThan(guest.Application.Archetype.HeatingDemand * session.Simulation.LivingSettings.ShowerDemandMultiplier));
            yield return WaitForCondition(() => guest.Agent.ActivityStaged, 15, "The shower activity did not reach and acknowledge its physical anchor.");
            Assert.That(guest.Agent.Activity, Is.EqualTo(GuestActivity.Shower));
            Assert.That(session.Simulation.Boiler.Load,
                Is.EqualTo(guest.Application.Archetype.HeatingDemand * session.Simulation.LivingSettings.ShowerDemandMultiplier).Within(0.01f));
            Assert.That(session.Simulation.DebugCheckoutGuest(guest.GuestId).Success, Is.True);
            session.RaiseChanged();
            yield return null; yield return null;
            var returnedKey = PhysicalKey(101);
            Assert.That(returnedKey.State.Location, Is.EqualTo(RoomKeyLocation.Returned));
            Assert.That(returnedKey.State.GuestId, Is.Null);
            Assert.That(Vector3.Distance(returnedKey.Body.position, returnedKey.rackAnchor.position), Is.LessThan(.04f),
                "Logical checkout must return the same physical numbered key to its fixed rack slot.");
            Assert.That(returnedKey.Body.isKinematic, Is.False);
            Assert.That(returnedKey.GetComponents<Collider>().Any(shape => shape.enabled && !shape.isTrigger), Is.True);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
