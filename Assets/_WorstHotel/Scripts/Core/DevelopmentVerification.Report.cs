#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace WorstHotel
{
    public sealed partial class DevelopmentVerification
    {
        sealed class DaySample
        {
            public int Day, Bookings;
            public GuestStay[] Stays;
            public readonly HashSet<string> CheckedIn = new HashSet<string>();
            public readonly HashSet<string> ReachedRoom = new HashSet<string>();
            public readonly HashSet<string> NaturallyCold = new HashSet<string>();
            public readonly Dictionary<string, Vector3> Positions = new Dictionary<string, Vector3>();
            public readonly Dictionary<string, string> LastGuestStates = new Dictionary<string, string>();
            public float WalkedMetres;
            public bool NaturalBoilerFailure;
            public int ChargedContacts, PrivateCases, SelfHelpActions, ContactAttempts, HeardConcerns;
        }

        DayReport[] completedReports;
        float finalCash, finalReputation;

        void ObserveGuest(GuestStay guest)
        {
            var agent = guest.Agent;
            var room = session.Rooms.First(r => r.Profile.Id == guest.RoomId);
            string visualState = "no visible body";
            if (guests.TryGetGuestDebugSnapshot(guest.GuestId, out var debug))
                visualState = "destination=" + debug.Destination + " path=" + debug.PathStatus;
            string position = guests.TryGetGuestTransform(guest.GuestId, out var body) ? body.position.ToString("F2") : "hidden";
            currentDay.LastGuestStates[guest.GuestId] = "guest=" + guest.GuestId + " room=" + guest.RoomId +
                " state=" + agent.State + " checkedIn=" + agent.CheckedIn + " reached=" + agent.HasReachedRoom +
                " wait=" + agent.WaitingSeconds.ToString("F1") + " hotelTime=" + session.Simulation.Elapsed.ToString("F1") +
                " cleanliness=" + room.Cleanliness + " turnover=" + room.TurnoverState + " departing=" + room.DepartingGuestId +
                " key=" + session.Simulation.Keys.Find(guest.RoomId)?.Location + " position=" + position + " " + visualState;
            if (guest.Agent.CheckedIn) currentDay.CheckedIn.Add(guest.GuestId);
            if (guest.Agent.HasReachedRoom) currentDay.ReachedRoom.Add(guest.GuestId);
            if (guests.TryGetGuestTransform(guest.GuestId, out var visual))
            {
                if (currentDay.Positions.TryGetValue(guest.GuestId, out var previous))
                    currentDay.WalkedMetres += Vector3.Distance(previous, visual.position);
                currentDay.Positions[guest.GuestId] = visual.position;
            }
            if (guest.Agent.InAssignedRoom && guest.Needs.Temperature.Severity > .03f &&
                session.Rooms.First(r => r.Profile.Id == guest.RoomId).Temperature < guest.Application.Archetype.Needs.PreferredTemperatureMin)
                currentDay.NaturallyCold.Add(guest.GuestId);
        }

        void WriteReport(string outcome)
        {
            if (string.IsNullOrEmpty(output)) return;
            if (soloSleepFixture) { WriteSoloSleepReport(outcome); return; }
            if (continuousTour) { WriteContinuousReport(outcome); return; }
            var text = new StringBuilder();
            text.AppendLine((operationsUI ? "Built-player operations UI diagnostic only / " : "Built-player living-hotel diagnostic tour / ") + (soloTour ? "SOLO" : "local development"));
            text.AppendLine("ApplicationVersion=" + Application.version + " UnityVersion=" + Application.unityVersion +
                " InputBackgroundPolicy=" + UnityEngine.InputSystem.InputSystem.settings.backgroundBehavior +
                " VerificationPadLayout=" + VerificationPadLayout);
            text.AppendLine("Outcome=" + outcome + " Errors=" + errors + " ResetVerified=" + resetVerified);
            text.AppendLine("Mode=" + (soloTour ? "Solo" : "LocalDevelopment") + " SyntheticPads=" + ActiveActors);
            text.AppendLine("OperationsUIOnly=" + operationsUI + " OperationsUIVerified=" + operationsUIVerified);
            if (operationsUI) text.AppendLine("This run verifies operations controller state and records real player UI candidates for separate visual review; it does not verify a three-day lifecycle or natural pacing.");
            text.AppendLine("PresenceFixturesRequested=" + presenceFixtures + " GuestPresenceVerified=" + presenceVerified);
            text.AppendLine("ServiceFixturesRequested=" + serviceFixtures + " GuestServicesVerified=" + serviceVerified);
            if (!operationsUI) text.AppendLine("Owned synthetic input; diagnostic8x clock is NOT player WAIT. Production GameSession.Update, guest schedules, doors and routes remain active. No starting housekeeper.");
            if (!operationsUI) text.AppendLine("Diagnostic model key pickup/giving follows a rack-retrieval time estimate at real reception; physical rack grab and instant handoff are tested separately in PlayMode. There is no check-in hold timer.");
            text.AppendLine("Graphics=" + SystemInfo.graphicsDeviceName + " Resolution=" + Screen.width + "x" + Screen.height +
                " RuntimeSeconds=" + (Time.realtimeSinceStartup - began).ToString("F1"));
            text.AppendLine("Hidden-window player-loop timing is NOT a rendered-performance benchmark. GPU captures require manual visual review.");
            text.AppendLine("OffscreenGPUAvailable=" + offscreenAvailable + " Captures=" + captures.Count);
            var reports = completedReports ?? (session != null ? session.Reports.ToArray() : Array.Empty<DayReport>());
            text.AppendLine("Reports=" + reports.Length + " FinalCash=" + finalCash + " FinalReputation=" + finalReputation);
            text.AppendLine("PhysicalRelocations=" + actualRoomMoves + " ModelBedActions=" + cleaningArrivals + " ModelLinenTurnovers=" + cleaningCompletions);
            foreach (var day in days)
            {
                var report = reports.FirstOrDefault(r => r.DayNumber == day.Day);
                text.AppendLine("Day " + day.Day + ": booked=" + day.Bookings + " checkedIn=" + day.CheckedIn.Count + " actualRoomArrivals=" + day.ReachedRoom.Count +
                    " guestWalkMetres=" + day.WalkedMetres.ToString("F1") + " minimumRoomSeconds=" + (day.Stays == null ? 0 : day.Stays.Min(g => g.Elapsed)).ToString("F1") +
                    " naturallyColdGuests=" + day.NaturallyCold.Count + " naturalBoilerFailure=" + day.NaturalBoilerFailure +
                    " serviceRequests=" + (day.Stays?.Sum(g => g.Memory.ServicesRequested) ?? 0) +
                    " guestsWithoutRequests=" + (day.Stays?.Count(g => g.Memory.ServicesRequested == 0) ?? 0) +
                    " servicesFulfilled=" + (day.Stays?.Sum(g => g.Memory.ServicesFulfilled) ?? 0) +
                    " chargedContacts=" + day.ChargedContacts + " privateCases=" + day.PrivateCases +
                    " selfHelpActions=" + day.SelfHelpActions + " contactAttempts=" + day.ContactAttempts + " heardConcerns=" + day.HeardConcerns +
                    (report == null ? " report=pending" : " paidStays=" + report.Receipts.Count(r => r.Price > 0 && r.Net > 0) + " gross=" + report.Gross + " refunds=" + report.Compensation + " net=" + report.Net));
                if (outcome != "PASS") foreach (var state in day.LastGuestStates.Values) text.AppendLine("Last observed Day " + day.Day + ": " + state);
            }
            text.AppendLine("Heater powered-interval temperature: start=" + heaterStartTemperature.ToString("F2") + " peak=" + heaterPeakTemperature.ToString("F2") +
                ". This observation alone is not a thermal counterfactual.");
            foreach (string fact in facts) text.AppendLine(fact);
            File.WriteAllText(Path.Combine(output, "runtime-verification.txt"), text.ToString());
        }
    }
}
#endif
