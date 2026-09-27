#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Text;
using UnityEngine;

namespace WorstHotel
{
    public sealed partial class DeveloperPanel
    {
        int livingGuestIndex;
        void DrawGuestDebug()
        {
            var simulation = Session.Simulation;
            if (!simulation.LivingEnabled) return;
            GUILayout.Space(8);
            GUILayout.Label("LIVING GUESTS", heading);
            var guests = simulation.Guests;
            if (guests.Count == 0) { GUILayout.Label(simulation.ContinuousOperations ?
                "Guest schedules appear when dated reservations arrive." : "Commit a plan to inspect its guest schedules.", body); return; }
            livingGuestIndex = Mathf.Clamp(livingGuestIndex, 0, guests.Count - 1);
            GUILayout.BeginHorizontal();
            if (Button("‹ Guest")) livingGuestIndex = (livingGuestIndex + guests.Count - 1) % guests.Count;
            if (Button("Guest ›")) livingGuestIndex = (livingGuestIndex + 1) % guests.Count;
            GUILayout.EndHorizontal();
            var guest = guests[livingGuestIndex];
            var agent = guest.Agent;
            if (agent == null) return;
            GUILayout.Label(guest.Name + " / Room " + guest.RoomId + " / " + GuestLabels.State(agent) +
                "\n" + GuestLabels.Traits(guest.Application.Archetype.Traits) +
                "\nTendencies: " + GuestLabels.Tendencies(guest.Application.Archetype) +
                "\nCurrentLocation: " + agent.CurrentLocation + " / CurrentActivity: " + agent.CurrentActivity +
                "\nNextActivity: " + agent.NextActivity + " / AssignedRoom: " + guest.RoomId +
                "\nArrival " + agent.ArrivalTime.ToString("F1") + "s / checkout " + agent.CheckoutTime.ToString("F1") +
                "s\nReception wait " + agent.WaitingSeconds.ToString("F1") + "s / patience left " + agent.WaitingPatienceRemaining.ToString("F1") +
                "s\nHeating multiplier " + agent.HeatingDemandMultiplier.ToString("F2") + " / activity noise " + agent.NoiseOutput.ToString("F2") +
                "\nNext activity " + agent.NextActivityTime.ToString("F1") + "s / activity ends " + agent.ActivityEndsAt.ToString("F1") + "s", body);
            var presentation = FindAnyObjectByType<GuestPresentation>();
            if (presentation && presentation.TryGetGuestDebugSnapshot(guest.GuestId, out var visual))
                GUILayout.Label("CurrentState: " + visual.CurrentState + "\nCurrentActivity: " + visual.CurrentActivity +
                    "\nDestination: " + visual.Destination + "\nAssignedRoom: " + visual.AssignedRoom +
                    "\nPrivacyState: " + visual.PrivacyState + "\nPathStatus: " + visual.PathStatus +
                    "\nNextScheduledActivity: " + visual.NextScheduledActivity +
                    "\nAt anchor: " + visual.AtActivityAnchor + " / body visible: " + visual.BodyVisible, body);
            else GUILayout.Label("No physical guest body yet. State: " + agent.State +
                " / next scheduled activity: " + agent.NextActivity, body);
            GUILayout.BeginHorizontal();
            if (Button("Force Sleep", agent.InAssignedRoom)) Apply(() => simulation.ForceSleep(guest.GuestId));
            if (Button("Force Shower", agent.InAssignedRoom)) Apply(() => simulation.ForceActivity(guest.GuestId, GuestActivity.Shower));
            if (Button("Force Rest", agent.InAssignedRoom)) Apply(() => simulation.ForceActivity(guest.GuestId, GuestActivity.QuietRest));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (Button("Force Work", agent.InAssignedRoom)) Apply(() => simulation.ForceActivity(guest.GuestId, GuestActivity.Work));
            if (Button("Force Phone", agent.InAssignedRoom)) Apply(() => simulation.ForceActivity(guest.GuestId, GuestActivity.PhoneCall));
            if (Button("Force Quiet TV", agent.InAssignedRoom)) Apply(() => simulation.ForceActivity(guest.GuestId, GuestActivity.WatchTV));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (Button("Force TV / music", agent.InAssignedRoom)) Apply(() => simulation.ForceActivity(guest.GuestId, GuestActivity.LoudRoom));
            if (Button("Force LeaveRoom", agent.InAssignedRoom)) Apply(() => simulation.ForceLeaveRoom(guest.GuestId));
            if (Button("Force ReturnRoom", agent.State == GuestAgentState.GuestAway)) Apply(() => simulation.ForceReturnRoom(guest.GuestId));
            GUILayout.EndHorizontal();
            if (Button("Skip to next scheduled activity", agent.InAssignedRoom)) Apply(() => simulation.SkipActivity(guest.GuestId));
            GUILayout.Label("Quiet agreement until " + agent.QuietUntil.ToString("F1") + "s", body);
            if (Button("Request quiet (staff 1)", ManagementUI.CanAskForQuiet(simulation, guest)))
                Apply(() => simulation.RequestQuiet(0, guest.GuestId));
            DrawGuestNeedsDebug(guest);
            DrawGuestServicesDebug(guest);
            if (Button("Move to selected room " + Session.Rooms[roomIndex].Profile.Id, agent.InAssignedRoom))
                Apply(() => simulation.MoveGuest(0, guest.GuestId, Session.Rooms[roomIndex].Profile.Id));
            if (Button("Accept this guest's current consequences", agent.InAssignedRoom))
                Apply(() => simulation.AcceptConsequences(0, guest.GuestId));
            if (Button("Force checkout selected guest", simulation.Running && agent.State != GuestAgentState.CheckingOut &&
                agent.State != GuestAgentState.Leaving && agent.State != GuestAgentState.Left))
                Apply(() => simulation.DebugCheckoutGuest(guest.GuestId));
            var schedule = new StringBuilder("Seeded activity plan:\n");
            foreach (var entry in agent.Schedule.Activities)
                schedule.Append(GuestLabels.Activity(entry.Activity)).Append(" (").Append(entry.Duration.ToString("F0")).Append("s)  →  ");
            GUILayout.Label(schedule.ToString(), body);
        }
    }
}
#endif
