using System;

namespace WorstHotel
{
    public enum GuestAgentState { Scheduled, Arriving, WaitingForCheckIn, GoingToRoom, InRoom, PerformingActivity, Sleeping, CheckingOut, Leaving, Left, LeavingRoom, GuestAway, ReturningToRoom,
        GoingToServiceReception = 13, WaitingAtServiceReception = 14, ReturningFromServiceReception = 15 }
    // Values 0–2 are serialized in existing diagnostics and network snapshots.
    public enum GuestActivity { QuietRest = 0, Shower = 1, LoudRoom = 2, Unpack = 3, Work = 4, PhoneCall = 5, WatchTV = 6, LeaveHotel = 7, Pack = 8,
        AdjustRadiator = 9, CallReception = 10 }
    public enum GuestLocation { OutsideHotel, Lobby, Travelling, AssignedRoom, Away, Departed }

    /// <summary>Authoritative guest state. Scene navigation reports completion instead of inventing travel time here.</summary>
    public sealed class GuestAgent
    {
        public string GuestId { get; }
        public GuestAgentState State { get; internal set; } = GuestAgentState.Scheduled;
        public GuestActivity Activity { get; internal set; } = GuestActivity.QuietRest;
        public GuestSchedule Schedule { get; }
        public float ArrivalTime => Schedule.ArrivalTime;
        public float CheckoutTime => Schedule.CheckoutTime;
        public float NextActivityTime { get; internal set; } = float.PositiveInfinity;
        public float ActivityEndsAt { get; internal set; } = float.PositiveInfinity;
        public float StateChangedAt { get; internal set; }
        public bool CheckedIn { get; internal set; }
        public bool HasReachedRoom { get; internal set; }
        public bool IsRelocating { get; internal set; }
        public int? PendingMoveRoomId { get; internal set; }
        public int? TransferFromRoomId { get; internal set; }
        public float WaitingSeconds { get; internal set; }
        public float WaitingPatienceRemaining => Math.Max(0, WaitingPatience - WaitingSeconds);
        public float HeatingDemandMultiplier { get; internal set; }
        public float NoiseOutput { get; internal set; }
        public float QuietUntil { get; internal set; }
        public bool RequiresActivityStaging { get; internal set; }
        public bool ActivityStaged { get; internal set; } = true;
        public string ResponseActionId { get; internal set; }
        public int ResponseActionVersion { get; internal set; }
        public bool IsServiceReceptionTrip => State == GuestAgentState.GoingToServiceReception ||
            State == GuestAgentState.WaitingAtServiceReception || State == GuestAgentState.ReturningFromServiceReception;
        internal float PendingActivityDuration;
        internal bool TemporarySleep;
        internal float AwayReturnTime = float.PositiveInfinity;
        public GuestActivity NextPlannedActivity => Schedule.Activities[ActivityIndex % Schedule.Activities.Count].Activity;
        public bool IsRoomState => State == GuestAgentState.InRoom || State == GuestAgentState.PerformingActivity || State == GuestAgentState.Sleeping;
        // GoingToRoom / ReturningToRoom become room states only after a physical arrival.
        // Moving between anchors inside that room still exposes the guest to its conditions.
        public bool InAssignedRoom => IsRoomState;
        public GuestLocation CurrentLocation => State == GuestAgentState.GuestAway ? GuestLocation.Away :
            State == GuestAgentState.Left ? GuestLocation.Departed : State == GuestAgentState.Scheduled ? GuestLocation.OutsideHotel :
            State == GuestAgentState.WaitingForCheckIn || State == GuestAgentState.WaitingAtServiceReception ? GuestLocation.Lobby : InAssignedRoom ? GuestLocation.AssignedRoom : GuestLocation.Travelling;
        public string CurrentActivity => State == GuestAgentState.Sleeping ? "Sleep" : State == GuestAgentState.ReturningToRoom ? "ReturnToHotel" :
            State == GuestAgentState.GoingToServiceReception ? "GoToReception" : State == GuestAgentState.WaitingAtServiceReception ? "ServiceConversation" :
            State == GuestAgentState.ReturningFromServiceReception ? "ReturnFromReception" :
            State == GuestAgentState.GuestAway ? "Away" : State == GuestAgentState.LeavingRoom ? "LeaveHotel" :
            State == GuestAgentState.CheckingOut || State == GuestAgentState.Leaving ? "Checkout" : Activity.ToString();
        public string NextActivity => State == GuestAgentState.GuestAway || State == GuestAgentState.LeavingRoom ? "ReturnToHotel" :
            IsServiceReceptionTrip ? "Return to assigned room" :
            State == GuestAgentState.Sleeping && !TemporarySleep ? "Pack / Checkout" : NextPlannedActivity.ToString();
        internal float WaitingPatience { get; }
        internal int ActivityIndex;
        internal bool PatienceEventSent;
        internal bool SleepStarted;

        internal GuestAgent(string id, GuestSchedule schedule, float waitingPatience)
        { GuestId = id; Schedule = schedule; WaitingPatience = waitingPatience; }
    }
}
