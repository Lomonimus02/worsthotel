#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace WorstHotel
{
    public sealed partial class DevelopmentVerification
    {
        // Explicit staging fixtures run in their own disposable hotel before the three-day tour.
        // Debug commands request activities; only the real routes/pose transitions acknowledge them.
        IEnumerator VerifyGuestPresence()
        {
            Require(soloTour, "presence visual fixtures use one actual SOLO camera");
            facts.Add("PRESENCE FIXTURE: disposable preliminary session uses ForceSleep/Shower/Rest/LeaveRoom/ReturnRoom. " +
                "It is reset before the separate natural three-day tour; no route completion callback is injected.");
            AssignPlan(1);
            session.CommitPlan(0);
            ManagementUI.Instance.Close();
            var simulation = session.Simulation;
            var stay = simulation.Guests.Single();
            var marker = guests.roomMarkers.Single(r => r.roomId == stay.RoomId);
            yield return Until(() => stay.Agent.State == GuestAgentState.WaitingForCheckIn, 30, "presence guest reaches reception");
            Require(simulation.Keys.PickUp(0, stay.RoomId).Success && simulation.CheckIn(0, stay.GuestId).Success,
                "presence fixture model key adapter");
            yield return Until(() => stay.Agent.InAssignedRoom, 35, "presence guest enters through real door");
            driveSpeed = 1;
            Require(simulation.ForceSleep(stay.GuestId).Success, "request sleep fixture");
            yield return Until(() => AtAnchor(stay.GuestId) && stay.Agent.State == GuestAgentState.Sleeping && stay.Agent.ActivityStaged,
                25, "guest walks to bed and finishes sleep transition");
            yield return Until(() => !marker.door.IsOpen && !marker.door.IsPassageOpen, 4, "sleeping guest closes the room door");
            var sleeper = guests.CaptureLanGuests().Single(g => g.id == stay.GuestId);
            float vertical = Mathf.Abs(Vector3.Dot(sleeper.bodyRotation * Vector3.up, Vector3.up));
            Require(vertical < .15f && sleeper.bodyVisible, "horizontal visible sleep pose captured for LAN");
            Require(guests.TryGetGuestTransform(stay.GuestId, out var guestRoot), "sleep body exists");
            Position(coop.Players[0], new Vector3(-4.5f, .08f, 8.35f), guestRoot.Find("Body").TransformPoint(new Vector3(0, 1.1f, 0)));
            yield return Capture("guest-sleep", "DIAGNOSTIC ForceSleep / real bed route, transition, horizontal pose and closed door");

            Require(simulation.ForceActivity(stay.GuestId, GuestActivity.Shower).Success, "request shower fixture");
            yield return Until(() => AtAnchor(stay.GuestId) && stay.Agent.ActivityStaged && marker.showerWater.activeSelf,
                30, "wake, walk to shower, then start water and real demand");
            var shower = guests.CaptureLanGuests().Single(g => g.id == stay.GuestId);
            Require(!shower.bodyVisible, "guest is concealed in the enclosed shower, including LAN pose");
            Require(stay.Agent.HeatingDemandMultiplier == simulation.LivingSettings.ShowerDemandMultiplier,
                "staged shower requests real hot water");
            // Stand in the clear north aisle, beyond the headboard, to show the curtain and steam.
            Position(coop.Players[0], new Vector3(-5.8f, .08f, 12.65f), marker.shower.position + Vector3.up * 1.4f);
            yield return Capture("guest-shower", "DIAGNOSTIC shower / enclosed anchor, concealed guest, water feedback and infrastructure demand");
            Position(coop.Players[0], new Vector3(-.45f, .08f, 10), marker.door.transform.position + Vector3.up * 1.3f);
            yield return Capture("guest-privacy", "Occupied closed door / private shower / ordinary staff approach");

            Require(simulation.ForceActivity(stay.GuestId, GuestActivity.QuietRest).Success, "request rest fixture");
            yield return Until(() => AtAnchor(stay.GuestId) && stay.Agent.ActivityStaged &&
                guests.CaptureLanGuests().Single(g => g.id == stay.GuestId).bodyVisible, 25, "guest leaves shower and rests at anchor");
            Position(coop.Players[0], new Vector3(-4.5f, .08f, 8.35f), marker.rest.position + Vector3.up * 1.1f);
            yield return Capture("guest-rest", "DIAGNOSTIC rest / visible guest restored and positioned at the rest anchor");
            var panel = FindAnyObjectByType<DeveloperPanel>();
            var scrollField = typeof(DeveloperPanel).GetField("scroll", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(panel && scrollField != null, "guest developer panel and scroll");
            panel.SendMessage("Toggle");
            try
            {
                // Show the actual extended guest section, without invoking GUI buttons by reflection.
                scrollField.SetValue(panel, new Vector2(0, 820));
                yield return Capture("guest-debug", "Actual F2 guest state, destination, privacy, route and force controls");
            }
            finally { scrollField.SetValue(panel, Vector2.zero); panel.SendMessage("Toggle"); }
            Require(simulation.ForceLeaveRoom(stay.GuestId).Success, "request temporary room departure");
            yield return Until(() => stay.Agent.State == GuestAgentState.GuestAway, 25, "guest exits room without checking out");
            yield return Until(() => !marker.door.IsOpen && !marker.door.IsPassageOpen, 4, "away guest closed room door");
            Require(simulation.ForceReturnRoom(stay.GuestId).Success, "request return to assigned room");
            yield return Until(() => stay.Agent.InAssignedRoom && AtAnchor(stay.GuestId), 25, "guest returns using real doorway and anchor");
            facts.Add("PRESENCE FIXTURE PASS: horizontalUpDot=" + vertical.ToString("F3") +
                "; ShowerConcealed=True; RealShowerDemand=True; LeaveReturn=True; route callbacks are production only.");
            presenceVerified = true;

            observedSimulation.Housekeeping.Changed -= ObserveCleaning;
            session.NewGame();
            for (int i = 0; i < 15; i++) yield return null;
            observedSimulation = session.Simulation;
            observedSimulation.Housekeeping.Changed += ObserveCleaning;
            session.Wait.Stop("Separate natural three-day SOLO diagnostic tour"); session.Wait.enabled = false;
            driveSpeed = 8;
            VerifySoloComposition();
        }

        bool AtAnchor(string guestId) => guests.TryGetGuestDebugSnapshot(guestId, out var state) && state.AtActivityAnchor;
    }
}
#endif
