using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace WorstHotel.Tests
{
    public sealed partial class Phase1PlayModeTests
    {
        [UnityTest, Category("ContinuousOperations"), Category("AutomaticSales"), Category("RhythmDaily")]
        public IEnumerator DatedMorningWakeWalksFromRealBedToShowerBeforeHotWaterStarts()
        {
            var session = GameSession.Instance;
            // Only incidental service requests/self-help are isolated. The production
            // calendar, ordinary sales, daily itinerary and physical activity durations stay intact.
            waitScenarioSessionConfig = Object.Instantiate(session.config);
            serviceUIConfig = Object.Instantiate(session.config.services);
            serviceUIConfig.eligibility = 0;
            serviceUIConfig.selfResponseObserveSeconds = serviceUIConfig.toleranceSeconds = 10000;
            waitScenarioSessionConfig.services = serviceUIConfig;
            session.config = waitScenarioSessionConfig;
            session.NewGame(); ManagementUI.Instance.Close();
            var model = session.Simulation;
            Assert.That(model.ContinuousOperations && model.AutomaticBookingsEnabled, Is.True);
            Assert.That(model.LivingSettings.Rhythm.Enabled, Is.True,
                "This acceptance case must use the production dated rhythm, not a legacy schedule clone.");

            // A normal room-sales policy supplies exactly one automatic dated stay.
            // No manual AcceptBooking, developer guest creation or activity selection is used.
            foreach (var policy in model.RoomSalesPolicies)
                Assert.That(model.SetRoomSalesPolicy(0, policy.RoomId, policy.RoomId == 101,
                    session.Economy.MinPrice, policy.Revision).Success, Is.True);
            session.AdvanceTime(model.NextSalesDecisionAt - model.Elapsed + .25f);
            var reservation = model.Reservations.Single();
            Assert.That(reservation.IsAutomatic && reservation.ActorId == -1, Is.True);
            var roomSales = model.RoomSalesPolicies.Single(policy => policy.RoomId == 101);
            Assert.That(model.SetRoomSalesPolicy(0, 101, false, roomSales.Price, roomSales.Revision).Success, Is.True);
            session.AdvanceTime(reservation.Offer.ArrivalAt - model.Elapsed + .25f);
            var guest = model.Guests.Single();

            // Labelled initial check-in/key/room-arrival adapter only. From this point
            // every bed, wake and shower arrival is produced by the real scene guest.
            Assert.That(session.ReportGuestReachedReception(guest.GuestId).Success, Is.True);
            Assert.That(CheckInWithModelKeyFixture(session, 0, guest.GuestId).Success, Is.True);
            Assert.That(session.ReportGuestReachedRoom(guest.GuestId).Success, Is.True);
            session.RaiseChanged(); yield return null; yield return null;
            var agent = guest.Agent;
            var presentation = Object.FindAnyObjectByType<GuestPresentation>();
            var markers = presentation.roomMarkers.Single(room => room.roomId == 101);
            Assert.That(presentation.TryGetGuestTransform(guest.GuestId, out var guestRoot), Is.True);
            var body = guestRoot.Find("Body");
            Assert.That(agent.RequiresActivityStaging && agent.Schedule.HasDailyRhythm, Is.True);
            Assert.That(agent.Schedule.Activities[agent.Schedule.MorningActivityIndex].Activity, Is.EqualTo(GuestActivity.Shower));
            yield return WaitForCondition(() => agent.InAssignedRoom && agent.ActivityStaged, 20,
                "The initial room activity must settle through its actual scene anchor.");

            // Labelled idle-calendar advance; no sleep command or fabricated anchor callback.
            session.AdvanceTime(agent.Schedule.SleepTime - model.Elapsed + .25f);
            yield return WaitForCondition(() => agent.State == GuestAgentState.Sleeping && agent.ActivityStaged, 55,
                "The ordinary dated bedtime must send the guest to their actual bed.");
            yield return null;
            Assert.That(HorizontalDistance(guestRoot.position, markers.bedApproach.position), Is.LessThan(.03f));
            Assert.That(Vector3.Distance(body.position, markers.bedAnchor.position), Is.LessThan(.03f));
            Assert.That(Mathf.Abs(Vector3.Dot(body.up, Vector3.up)), Is.LessThan(.03f));
            Assert.That(model.HeatingDemands.Single(row => row.RoomId == 101).HotWater, Is.Zero);
            Assert.That(agent.NextPlannedActivity, Is.EqualTo(GuestActivity.Shower));
            Assert.That(presentation.TryGetGuestDebugSnapshot(guest.GuestId, out var sleepingVisual), Is.True);
            Assert.That(sleepingVisual.NextScheduledActivity, Does.Contain("Shower"),
                "Sleeping guests should advertise the morning shower, not an obsolete checkout caption.");

            float untilWake = agent.Schedule.WakeTime - model.Elapsed;
            Assert.That(untilWake, Is.GreaterThan(1));
            session.AdvanceTime(untilWake - .4f);
            Assert.That(agent.State, Is.EqualTo(GuestAgentState.Sleeping));
            Vector3 beforeWake = guestRoot.position;
            yield return WaitForCondition(() => agent.State == GuestAgentState.PerformingActivity &&
                agent.Activity == GuestActivity.Shower, 3,
                "The normal session Update must begin the planned shower when the wake time arrives.");
            Assert.That(agent.ActivityStaged, Is.False,
                "A dated wake cannot immediately claim the shower while the guest is still getting out of bed.");
            Assert.That(HorizontalDistance(guestRoot.position, markers.shower.position), Is.GreaterThan(.5f));
            int morningCursor = RhythmActivityCursor(model, guest.GuestId);
            Assert.That(morningCursor, Is.EqualTo(agent.Schedule.MorningActivityIndex + 1));
            bool movedBeforeStaging = false;
            float deadline = Time.realtimeSinceStartup + 20;
            while (!agent.ActivityStaged && Time.realtimeSinceStartup < deadline)
            {
                Assert.That(agent.Activity, Is.EqualTo(GuestActivity.Shower));
                Assert.That(model.HeatingDemands.Single(row => row.RoomId == 101).HotWater, Is.Zero,
                    "Walking toward a shower must not consume hot water before the actual anchor acknowledgment.");
                movedBeforeStaging |= HorizontalDistance(guestRoot.position, beforeWake) > .10f;
                yield return null;
            }
            Assert.That(agent.ActivityStaged, Is.True, "The real bed-to-shower route must complete within its bounded watchdog.");
            Assert.That(movedBeforeStaging, Is.True, "The guest must visibly walk, rather than teleport or report an injected arrival.");
            yield return null; yield return null;
            Assert.That(HorizontalDistance(guestRoot.position, markers.shower.position), Is.LessThan(.03f));
            Assert.That(markers.showerWater.activeSelf && markers.showerCurtain.activeSelf, Is.True);
            Assert.That(body.gameObject.activeSelf, Is.False, "The real shower still conceals the guest's body.");
            var demand = model.HeatingDemands.Single(row => row.RoomId == 101);
            Assert.That(demand.GuestId, Is.EqualTo(guest.GuestId));
            Assert.That(demand.HotWater, Is.GreaterThan(0));
            Assert.That(model.HeatingDemands.Where(row => row.RoomId != 101).All(row => row.HotWater == 0), Is.True);
            Assert.That(model.Boiler.Load, Is.EqualTo(model.HeatingDemands.Sum(row => row.Total)).Within(.0001f));
            Assert.That(RhythmActivityCursor(model, guest.GuestId), Is.EqualTo(morningCursor), "Physical staging must not consume a second morning entry.");

            session.AdvanceTime(agent.NextActivityTime - model.Elapsed + .25f);
            yield return WaitForCondition(() => agent.InAssignedRoom && agent.ActivityStaged &&
                agent.Activity != GuestActivity.Shower, 20,
                "The morning routine must continue through the next actual room activity after the shower ends.");
            yield return null;
            Assert.That(agent.State, Is.Not.EqualTo(GuestAgentState.Sleeping));
            Assert.That(agent.Activity, Is.Not.EqualTo(GuestActivity.LeaveHotel).And.Not.EqualTo(GuestActivity.Unpack));
            Assert.That(RhythmActivityCursor(model, guest.GuestId), Is.EqualTo(agent.Activity == GuestActivity.Pack ? morningCursor : morningCursor + 1),
                "Normal checkout packing may take priority; neither continuation may restart or consume a second morning shower.");
            Assert.That(model.HeatingDemands.Single(row => row.RoomId == 101).HotWater, Is.Zero);
            Assert.That(markers.showerWater.activeSelf || markers.showerCurtain.activeSelf, Is.False);
            Assert.That(model.Keys.Find(101).GuestId, Is.EqualTo(guest.GuestId));
            Assert.That(session.Simulation, Is.SameAs(model));
            Assert.That(session.Phase, Is.EqualTo(DayPhase.Service));
            LogAssert.NoUnexpectedReceived();
        }

        static int RhythmActivityCursor(HotelSimulation model, string guestId) =>
            model.CaptureSnapshot(941, 1).Guests.Single(guest => guest.Application.Id == guestId).Agent.ActivityIndex;
    }
}
