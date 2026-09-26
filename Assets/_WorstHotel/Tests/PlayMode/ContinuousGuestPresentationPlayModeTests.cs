using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace WorstHotel.Tests
{
    public sealed partial class Phase1PlayModeTests
    {
        [UnityTest, Category("ContinuousOperations")]
        public IEnumerator LaterDatedArrivalsReuseFreePhysicalSlotsWithoutChangingAnExistingGuestOrSuitcase()
        {
            var session = GameSession.Instance;
            waitScenarioSessionConfig = Object.Instantiate(session.config);
            waitScenarioSessionConfig.continuousOperations = true;
            waitScenarioSessionConfig.hotelDaySeconds = 720;
            waitScenarioSessionConfig.openingHour = 8;
            waitScenarioSessionConfig.reportHour = 6;
            session.config = waitScenarioSessionConfig;
            session.NewGame();
            ManagementUI.Instance.Close();
            var model = session.Simulation;
            var presentation = Object.FindAnyObjectByType<GuestPresentation>();
            var suitcaseBodies = Object.FindObjectsByType<ServiceSupplyItem>(FindObjectsSortMode.None)
                .Where(item => item.luggageSlot >= 0).ToArray();
            Assert.That(suitcaseBodies.Length, Is.EqualTo(6));
            var firstOffers = model.BookingOffers.Where(offer => offer.ArrivalDay == 1)
                .OrderBy(offer => offer.ArrivalAt).Take(6).ToArray();
            Assert.That(firstOffers.Length, Is.EqualTo(6));
            for (int index = 0; index < firstOffers.Length; index++)
                Assert.That(model.AcceptBooking(0, firstOffers[index].Id, 101 + index, session.Economy.MinPrice).Success, Is.True);

            // Labelled calendar fixture: skip idle scheduled time only. Every visible body
            // walks from the exterior and reports its own real reception/exit arrivals.
            session.AdvanceTime(firstOffers.Max(offer => offer.ArrivalAt) + .25f - model.Elapsed);
            yield return WaitForCondition(() => model.Guests.Count == 6 &&
                model.Guests.All(guest => guest.Agent.State == GuestAgentState.WaitingForCheckIn), 30,
                "All six first-night guests must physically reach their own reception place.");
            var firstStays = model.Guests.ToArray();
            var firstSlots = firstStays.Select(guest =>
            {
                Assert.That(presentation.TryGetGuestReceptionSlot(guest.GuestId, out int slot), Is.True);
                return slot;
            }).ToArray();
            Assert.That(firstSlots.Distinct().Count(), Is.EqualTo(6));

            // Intentionally unserved first cohort: checkout is the real deadline and makes
            // these guests leave normally. No DebugCheckout or route callback is injected.
            session.AdvanceTime(firstStays.Max(guest => guest.Agent.CheckoutTime) + .25f - model.Elapsed);
            yield return WaitForCondition(() => firstStays.All(guest => guest.Agent.State == GuestAgentState.Left), 30,
                "The no-room-time guests must finish their real exterior exit before slots are reused.");
            var nextOffers = model.BookingOffers.Where(offer => offer.ArrivalDay == 2).OrderBy(offer => offer.ArrivalAt).ToArray();
            var early = nextOffers.First(); var later = nextOffers.Last();
            Assert.That(early.Id, Is.Not.EqualTo(later.Id));
            Assert.That(model.AcceptBooking(0, early.Id, 101, session.Economy.MinPrice).Success, Is.True);
            Assert.That(model.AcceptBooking(0, later.Id, 103, session.Economy.MinPrice).Success, Is.True);

            session.AdvanceTime(early.ArrivalAt + .25f - model.Elapsed);
            yield return WaitForCondition(() => model.Guests.Any(guest => guest.GuestId == early.Id &&
                guest.Agent.State == GuestAgentState.WaitingForCheckIn), 25,
                "A later calendar day's guest must acquire an actual body and complete the lobby route.");
            Assert.That(presentation.TryGetGuestTransform(early.Id, out var earlyBody), Is.True);
            Assert.That(presentation.TryGetGuestReceptionSlot(early.Id, out int earlySlot), Is.True);
            int appearance = presentation.CaptureLanGuests().Single(guest => guest.id == early.Id).appearanceIndex;
            var earlySuitcase = suitcaseBodies.SingleOrDefault(item => item.ItemId == "luggage:" + early.Id);
            Assert.That(earlySuitcase, Is.Not.Null, "The seventh materialized guest must have one of the six authored luggage bodies.");
            Assert.That(earlySuitcase.GetComponentsInChildren<Renderer>().Any(item => item.enabled), Is.True);
            Assert.That(earlySuitcase.PlacementCollider.enabled, Is.True);
            Assert.That(HorizontalDistance(earlyBody.position, presentation.receptionPlaces[earlySlot].position), Is.LessThan(.05f));

            session.AdvanceTime(later.ArrivalAt + .25f - model.Elapsed);
            yield return WaitForCondition(() => model.Guests.Any(guest => guest.GuestId == later.Id &&
                guest.Agent.State == GuestAgentState.WaitingForCheckIn), 25,
                "Appending the eighth stay must not steal the earlier guest's reception or luggage slot.");
            Assert.That(presentation.TryGetGuestTransform(early.Id, out var retainedBody), Is.True);
            Assert.That(retainedBody, Is.SameAs(earlyBody));
            Assert.That(presentation.TryGetGuestReceptionSlot(early.Id, out int retainedSlot), Is.True);
            Assert.That(retainedSlot, Is.EqualTo(earlySlot));
            Assert.That(presentation.CaptureLanGuests().Single(guest => guest.id == early.Id).appearanceIndex, Is.EqualTo(appearance));
            Assert.That(earlySuitcase.ItemId, Is.EqualTo("luggage:" + early.Id));
            Assert.That(presentation.TryGetGuestReceptionSlot(later.Id, out int laterSlot), Is.True);
            Assert.That(laterSlot, Is.Not.EqualTo(earlySlot));
            Assert.That(suitcaseBodies.Count(item => item.ItemId == "luggage:" + later.Id), Is.EqualTo(1));
            Assert.That(suitcaseBodies.Count(item => item.ItemId == "luggage:" + early.Id), Is.EqualTo(1));
            Assert.That(model.Running && session.Phase == DayPhase.Service, Is.True);
            Assert.That(ManagementUI.Instance.IsOpen, Is.False);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
