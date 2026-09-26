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
        public IEnumerator AllSixGuestsPhysicallyReachReceptionWithSolidLuggageStorageEnabled()
        {
            var session = GameSession.Instance;
            // Labelled calendar fixture: close an unserved first day to obtain the real
            // second-day offers. No guest arrival or other route-completion callback is supplied.
            var firstOffer = session.Plan.Applications.First();
            session.Assign(0, firstOffer.Id, 101, session.Economy.MinPrice);
            session.CommitPlan(0);
            Assert.That(session.Phase, Is.EqualTo(DayPhase.Service), session.LastMessage);
            session.EndShift();
            session.ContinueAfterSettlement(0);
            session.ChooseMaintenance(0, MaintenanceChoice.Defer);
            Assert.That(session.Day, Is.EqualTo(2));
            Assert.That(session.Phase, Is.EqualTo(DayPhase.Planning));

            var offers = session.Plan.Applications.Take(6).ToArray();
            Assert.That(offers, Has.Length.EqualTo(6));
            for (int index = 0; index < offers.Length; index++)
                session.Assign(0, offers[index].Id, 101 + index, session.Economy.MinPrice);
            session.CommitPlan(0);
            Assert.That(session.Phase, Is.EqualTo(DayPhase.Service), session.LastMessage);
            ManagementUI.Instance.Close();
            var stays = session.Simulation.Guests.ToArray();
            var presentation = Object.FindAnyObjectByType<GuestPresentation>();
            var storage = Object.FindAnyObjectByType<LuggageStorageZone>();
            Assert.That(presentation != null && presentation.enabled, Is.True);
            Assert.That(storage, Is.Not.Null);
            var storageColliders = storage.GetComponentsInChildren<Collider>();
            Assert.That(storageColliders.Length, Is.GreaterThanOrEqualTo(3),
                "Use volume, platform and signpost must retain their production collisions.");
            Assert.That(storageColliders.All(shape => shape.enabled && !shape.isTrigger), Is.True);

            // Skip only the scheduled pre-arrival interval. Production presentation must
            // walk every body from the exterior spawn through the actual lobby geometry.
            session.AdvanceTime(stays.Max(guest => guest.Agent.ArrivalTime) + .2f - session.Simulation.Elapsed);
            yield return null;
            var starts = stays.ToDictionary(guest => guest.GuestId, guest =>
            {
                Assert.That(presentation.TryGetGuestTransform(guest.GuestId, out var body), Is.True);
                return body.position;
            });
            float deadline = Time.realtimeSinceStartup + 25;
            while (stays.Any(guest => guest.Agent.State != GuestAgentState.WaitingForCheckIn) && Time.realtimeSinceStartup < deadline)
                yield return null;

            for (int index = 0; index < stays.Length; index++)
            {
                var guest = stays[index];
                presentation.TryGetGuestDebugSnapshot(guest.GuestId, out var debug);
                string context = guest.GuestId + ": " + debug.CurrentState + ", " + debug.PathStatus + ", target " + debug.Destination;
                Assert.That(guest.Agent.State, Is.EqualTo(GuestAgentState.WaitingForCheckIn), context);
                Assert.That(presentation.TryGetGuestTransform(guest.GuestId, out var body), Is.True, context);
                Assert.That(HorizontalDistance(starts[guest.GuestId], body.position), Is.GreaterThan(1),
                    "Arrival must include physical travel. " + context);
                Assert.That(HorizontalDistance(body.position, presentation.receptionPlaces[index].position), Is.LessThan(.05f), context);
                Assert.That(guest.Agent.CheckedIn || guest.Agent.HasReachedRoom, Is.False,
                    "This regression stops at reception and performs no key handoff.");
            }
            Assert.That(storageColliders.All(shape => shape.enabled && !shape.isTrigger), Is.True,
                "Arrival may not succeed by disabling the production luggage area.");
            LogAssert.NoUnexpectedReceived();
        }
    }
}
