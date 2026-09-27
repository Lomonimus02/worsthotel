using System;
using System.Linq;

namespace WorstHotel
{
    public sealed partial class HotelSimulation
    {
        private void RefreshGuestLoad()
        {
            if (Boiler.CapacityModelEnabled && LivingEnabled)
            {
                Boiler.SetLoad(HeatingDemands.Sum(row => row.Total));
                return;
            }
            // Historical shifts and the no-agent diagnostic model retain their old calculation.
            // Production continuous operations require living guests and use the room sources above.
            Boiler.SetLoad(Math.Max(0, guests.Where(guest => !guest.ReceiptPosted).Sum(guest => guest.Application.Archetype.HeatingDemand *
                (LivingEnabled ? guest.Agent.HeatingDemandMultiplier : 1)) + (LivingEnabled ? roomSystem.ExtraBoilerDemand(rooms.Values) : 0)));
        }

        private void ReleaseRoom(GuestStay guest)
        {
            Services?.EndGuestStay(guest);
            ReleasePendingMove(guest);
            if (LivingEnabled) NeedEvaluator.ClearInstantaneous(guest);
            if (LivingEnabled)
            {
                // Record only existing staff-contact cases before ending them. Background
                // Observed discomfort is filtered by RecordIgnored and is not a missed request.
                Incidents.RecordIgnored(guest);
                Incidents.EndGuestStay(guest.GuestId);
            }
            if (guest.Agent != null) { guest.Agent.IsRelocating = false; guest.Agent.QuietUntil = 0; }
            var room = rooms[guest.RoomId];
            ReleaseOwnedRoom(guest, room);
            if (room.ReservedGuestId == guest.GuestId) room.ReservedGuestId = null;
            // Simple prototype return policy: guest-owned keys return at checkout. A stale guest
            // identity cannot reclaim a key already picked up by staff or issued to another stay.
            Keys.ReturnGuestKeys(guest.GuestId);
        }

        // Normal deadline and an accepted early-departure outcome use the same real exit.
        // Do not change the contracted schedule to make a departure happen earlier.
        internal void BeginGuestCheckout(GuestStay guest, float now)
        {
            if (IsReadOnlyMirror || guest?.Agent == null || guest.ReceiptPosted) return;
            var agent = guest.Agent;
            if (agent.State == GuestAgentState.CheckingOut || agent.State == GuestAgentState.Leaving || agent.State == GuestAgentState.Left) return;
            bool alreadyOutside = agent.State == GuestAgentState.GuestAway;
            // Service/item/key observers may run during ReleaseRoom. Commit the terminal
            // agent first, so a captured early outcome never still describes an in-room actor.
            agent.State = alreadyOutside ? GuestAgentState.Left : agent.CheckedIn ? GuestAgentState.CheckingOut : GuestAgentState.Leaving;
            agent.StateChangedAt = now;
            agent.HeatingDemandMultiplier = agent.NoiseOutput = 0;
            agent.NextActivityTime = agent.ActivityEndsAt = float.PositiveInfinity;
            ReleaseRoom(guest);
            if (alreadyOutside) SignalGuestVacatedRoom(guest.GuestId, guest.RoomId);
            RefreshRoomPresence(); RefreshGuestLoad(); RefreshElectrical();
            SignalEvent(guest.EarlyCheckout.State == EarlyCheckoutState.Committed ?
                guest.Name + " is checking out early: " + guest.EarlyCheckout.CauseDescription :
                agent.CheckedIn ? guest.Name + " is checking out" : guest.Name + " left without checking in");
        }

        private GuestStay FindLivingGuest(string id) => Running && LivingEnabled && id != null ? guests.FirstOrDefault(guest => guest.GuestId == id) : null;

        public CommandResult SignalGuestReachedReception(string guestId)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            var guest = FindLivingGuest(guestId);
            if (guest == null || guest.Agent.State != GuestAgentState.Arriving) return CommandResult.Fail("Guest is not approaching reception.");
            Transition(guest, GuestAgentState.WaitingForCheckIn, Elapsed, guest.Name + " is waiting for check-in");
            return CommandResult.Ok("Guest reached reception.");
        }

        public CommandResult CheckIn(int actorId, string guestId)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            if (actorId < 0) return CommandResult.Fail("Unknown player identity.");
            var guest = FindLivingGuest(guestId);
            if (guest == null || guest.Agent.State != GuestAgentState.WaitingForCheckIn) return CommandResult.Fail("This guest is not waiting at reception.");
            var room = rooms[guest.RoomId];
            if (room.ReservedGuestId != guestId || room.Occupied) return CommandResult.Fail("The assigned room is not available for this booking.");
            if (room.Cleanliness != Cleanliness.Clean) return CommandResult.Fail("The assigned room must be clean before check-in.");
            if (RoomTurnoverProtected(room)) return CommandResult.Fail("Wait for the previous guest to leave and for bed preparation to finish.");
            var handoff = Keys.CanHandToGuest(actorId, guest.RoomId, guestId);
            if (!handoff.Success) return handoff;
            Keys.CommitHandToGuest(guest.RoomId, guestId);
            room.ReservedGuestId = null; room.GuestId = guestId;
            guest.Agent.CheckedIn = true;
            Services?.CompleteCheckInContext(guest);
            NeedEvaluator.ClearInstantaneous(guest);
            Transition(guest, GuestAgentState.GoingToRoom, Elapsed, guest.Name + " checked in to room " + guest.RoomId);
            if (Boiler.CapacityModelEnabled) RefreshGuestLoad();
            Keys.NotifyHandToGuest(guest.RoomId);
            return CommandResult.Ok("Room " + guest.RoomId + " key handed to " + guest.Name + " by player " + actorId + ".");
        }

        public CommandResult SignalGuestReachedRoom(string guestId)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            var guest = FindLivingGuest(guestId);
            if (guest == null || guest.Agent.State != GuestAgentState.GoingToRoom)
                return CommandResult.Fail("Guest is not travelling to their assigned room.");
            var room = rooms[guest.RoomId];
            if (room.Cleanliness != Cleanliness.Clean || RoomTurnoverProtected(room))
                return CommandResult.Fail("The assigned room is not ready for the guest's arrival.");
            if (guest.Agent.IsRelocating)
            {
                if (room.ReservedGuestId != guestId || room.Occupied)
                    return CommandResult.Fail("The relocation destination is no longer reserved for this guest.");
                room.ReservedGuestId = null; room.GuestId = guestId;
                guest.Agent.IsRelocating = false;
                guest.Agent.TransferFromRoomId = null;
                Incidents.EndTransfer(guestId, guest.RoomId);
            }
            else if (room.GuestId != guestId) return CommandResult.Fail("The assigned room no longer belongs to this guest.");
            guest.Agent.HasReachedRoom = true;
            SetActivity(guest, GuestActivity.QuietRest, Elapsed, LivingSettings.FirstActivityDelay);
            Transition(guest, GuestAgentState.InRoom, Elapsed, null);
            RefreshGuestLoad();
            RefreshElectrical();
            return CommandResult.Ok("Guest reached their room and settled in.");
        }

        public CommandResult SignalGuestLeft(string guestId)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            // Exit completion remains valid after accounts close. All other guest commands still require Running.
            var guest = LivingEnabled && guestId != null ? guests.FirstOrDefault(stay => stay.GuestId == guestId) : null;
            if (guest == null || guest.Agent.State != GuestAgentState.Leaving) return CommandResult.Fail("Guest is not leaving the hotel.");
            guest.Agent.TransferFromRoomId = null;
            Transition(guest, GuestAgentState.Left, Elapsed, null);
            return CommandResult.Ok("Guest reached the hotel exit.");
        }

        public CommandResult ForceActivity(string guestId, GuestActivity activity)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            if (!Enum.IsDefined(typeof(GuestActivity), activity)) return CommandResult.Fail("Unknown guest activity.");
            if (activity == GuestActivity.AdjustRadiator || activity == GuestActivity.CallReception)
                return CommandResult.Fail("Use the guest response controls for physical self-help or contact.");
            var guest = FindLivingGuest(guestId);
            if (guest == null || !guest.Agent.IsRoomState) return CommandResult.Fail("Activities require a guest physically in their assigned room.");
            if (Services?.DirectIntent(guestId) != null) return CommandResult.Fail("Finish or cancel the direct service before changing the guest's activity.");
            if (!string.IsNullOrEmpty(guest.Agent.ResponseActionId)) return CommandResult.Fail("Let the current guest response finish or cancel it first.");
            if (guest.Agent.State == GuestAgentState.Sleeping) guest.Agent.SleepStarted = true;
            SetActivity(guest, activity, Elapsed, activity == GuestActivity.QuietRest ? LivingSettings.QuietDurationMin : LivingSettings.ActivityDurationMin);
            RefreshGuestLoad();
            RefreshElectrical();
            return CommandResult.Ok("Developer activity started from the guest's real room.");
        }

        public CommandResult SkipActivity(string guestId)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            var guest = FindLivingGuest(guestId);
            if (guest == null || !guest.Agent.IsRoomState) return CommandResult.Fail("Guest has no room activity to skip.");
            if (Services?.DirectIntent(guestId) != null) return CommandResult.Fail("Finish or cancel the direct service before changing the guest's activity.");
            if (!string.IsNullOrEmpty(guest.Agent.ResponseActionId)) return CommandResult.Fail("Let the current guest response finish or cancel it first.");
            var entries = guest.Agent.Schedule.Activities;
            var entry = entries[guest.Agent.ActivityIndex % entries.Count];
            guest.Agent.ActivityIndex++;
            SetActivity(guest, entry.Activity, Elapsed, entry.Duration);
            RefreshGuestLoad();
            RefreshElectrical();
            return CommandResult.Ok("Advanced to the next scheduled activity.");
        }

        private void TickLivingGuests(float now, float dt)
        {
            foreach (var guest in guests)
            {
                var agent = guest.Agent;
                if (agent.State == GuestAgentState.Left || agent.State == GuestAgentState.Leaving) continue;
                if (agent.State == GuestAgentState.WaitingForCheckIn)
                {
                    float previous = agent.WaitingSeconds;
                    float waiting = Math.Min(dt, Math.Max(0, agent.CheckoutTime - (now - dt)));
                    agent.WaitingSeconds += waiting;
                    guest.CheckInWaitingSeconds += waiting;
                    guest.CheckInDelayPenaltySeconds += Math.Max(0, agent.WaitingSeconds - agent.WaitingPatience) - Math.Max(0, previous - agent.WaitingPatience);
                    if (!agent.PatienceEventSent && agent.WaitingPatienceRemaining <= 0)
                    {
                        agent.PatienceEventSent = true;
                        SignalEvent(guest.Name + " has waited too long for check-in");
                    }
                }
                if (now >= agent.CheckoutTime && agent.State != GuestAgentState.CheckingOut)
                {
                    BeginGuestCheckout(guest, now);
                    continue;
                }
                if (agent.State == GuestAgentState.CheckingOut)
                {
                    if (now - agent.StateChangedAt >= LivingSettings.CheckoutInteractionSeconds)
                        Transition(guest, GuestAgentState.Leaving, now, null);
                    continue;
                }
                if (agent.State == GuestAgentState.Scheduled)
                {
                    if (now >= agent.ArrivalTime) Transition(guest, GuestAgentState.Arriving, now, null);
                    continue;
                }
                if (agent.State == GuestAgentState.GuestAway)
                {
                    if (now >= agent.AwayReturnTime) ForceReturnRoom(guest.GuestId);
                    continue;
                }
                if (!agent.IsRoomState) continue;
                if (Services?.DirectIntent(guest.GuestId) != null) continue;
                if (!string.IsNullOrEmpty(agent.ResponseActionId)) continue;
                if (agent.RequiresActivityStaging && !agent.ActivityStaged) continue;
                if (now >= agent.CheckoutTime - LivingSettings.ActivityDurationMin && agent.Activity != GuestActivity.Pack)
                {
                    agent.SleepStarted = true;
                    SetActivity(guest, GuestActivity.Pack, now, float.PositiveInfinity);
                    continue;
                }
                if (agent.State == GuestAgentState.Sleeping && agent.TemporarySleep && now >= agent.NextActivityTime)
                {
                    SetActivity(guest, GuestActivity.QuietRest, now, LivingSettings.QuietDurationMin);
                    continue;
                }
                if (agent.State == GuestAgentState.Sleeping && !agent.TemporarySleep && now >= agent.Schedule.WakeTime)
                {
                    // Morning resumes ordinary life. SleepStarted remains true, so the past
                    // bedtime cannot immediately send this one-night guest back to sleep.
                    SetActivity(guest, GuestActivity.QuietRest, now, LivingSettings.QuietDurationMin);
                    continue;
                }
                if (now >= agent.Schedule.SleepTime && !agent.SleepStarted)
                {
                    agent.SleepStarted = true;
                    bool alreadyMorning = now >= agent.Schedule.WakeTime;
                    SetActivity(guest, GuestActivity.QuietRest, now, alreadyMorning ? LivingSettings.QuietDurationMin : float.PositiveInfinity);
                    if (!alreadyMorning) Transition(guest, GuestAgentState.Sleeping, now, null);
                    continue;
                }
                if (agent.State == GuestAgentState.Sleeping || now < agent.NextActivityTime) continue;
                var entries = agent.Schedule.Activities;
                var entry = entries[agent.ActivityIndex % entries.Count];
                agent.ActivityIndex++;
                SetActivity(guest, entry.Activity, now, entry.Duration);
            }
        }

        private void SetActivity(GuestStay guest, GuestActivity activity, float now, float duration)
        {
            var agent = guest.Agent;
            if (activity == GuestActivity.LeaveHotel)
            {
                // Never depart on a new excursion so late that the planned stay ends outside.
                // Explicit developer ForceLeaveRoom still supports testing an away checkout.
                if (now + duration + LivingSettings.ActivityDurationMin >= agent.Schedule.SleepTime)
                { activity = GuestActivity.QuietRest; duration = LivingSettings.QuietDurationMin; }
                else { StartGuestHotelTrip(guest, duration); return; }
            }
            agent.Activity = activity;
            agent.TemporarySleep = false;
            agent.ActivityStaged = !agent.RequiresActivityStaging;
            agent.PendingActivityDuration = duration;
            agent.State = activity == GuestActivity.QuietRest ? GuestAgentState.InRoom : GuestAgentState.PerformingActivity;
            agent.StateChangedAt = now;
            agent.ActivityEndsAt = agent.NextActivityTime = agent.ActivityStaged ? now + duration : float.PositiveInfinity;
            SetActivityOutput(agent, agent.ActivityStaged);
            RefreshRoomPresence();
        }

        private void Transition(GuestStay guest, GuestAgentState state, float now, string message)
        { guest.Agent.State = state; guest.Agent.StateChangedAt = now; RefreshRoomPresence(); if (!string.IsNullOrEmpty(message)) SignalEvent(message); }
        private static string ActivityLabel(GuestActivity activity) => activity == GuestActivity.Shower ? "using hot water" :
            activity == GuestActivity.LoudRoom ? "loud room activity" : "quiet rest";
    }
}

