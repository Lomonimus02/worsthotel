using System;

namespace WorstHotel
{
    // Explicit wire data only: arrays, strings, enums and finite scalar values. No Unity or transport API.
    [Serializable] public sealed class HotelModelSnapshot
    {
        public const int ProtocolVersion = 11;
        public int Version = ProtocolVersion;
        public long Epoch, Sequence;
        public int Day, LastMaintenanceDay, DebugGuestCounter, EventRevision, LastReportDay, Cash, LastRefillDay;
        public bool Running, BoilerFailureAcknowledged;
        public float Time, Speed, Reputation;
        public string LastEvent, UpgradedCircuitId;
        public RoomSnapshot[] Rooms;
        public GuestSnapshot[] Guests;
        public BoilerSnapshot Boiler;
        public CircuitSnapshot[] Circuits;
        public ConsumerSnapshot[] Consumers;
        public HeaterSnapshot[] Heaters;
        public KeySnapshot[] Keys;
        public LinenSnapshot[] Linens;
        public TurnoverSnapshot[] Turnover;
        public IncidentSnapshot[] Incidents;
        public RequestSnapshot[] Requests;
        public ReportSnapshot[] Reports;
        public MaintenanceSnapshot[] Maintenance;
        public NoiseOverrideSnapshot[] NoiseOverrides;
        public NoiseSourceSnapshot[] NoiseSources;
        public float SituationTime;
        public bool HasServices;
        public ServiceLayerSnapshot ServiceLayer;
        public bool HasOperations;
        public OperationsSnapshot Operations;
    }
    [Serializable] public sealed class PlanningSnapshot
    { public BookingSnapshot[] Applications; public AssignmentSnapshot[] Assignments; public bool IsCommitted; }
    [Serializable] public sealed class ProfileSnapshot
    {
        public GuestKind Kind; public GuestTraits Traits;
        public string Label, Description;
        public int ReferencePrice;
        public float HeatingDemand, ColdThreshold, ColdPenaltyWeight, PriceSensitivity, Patience, NoiseTolerance;
        public float PreferredTemperatureMin, PreferredTemperatureMax, ToleranceTemperatureMin, ToleranceTemperatureMax;
        public float PreferredNoise, NeedNoiseTolerance, NeedPatience;
    }
    [Serializable] public sealed class BookingSnapshot
    { public string Id, GuestName; public int ReferencePrice; public ProfileSnapshot Profile; }
    [Serializable] public sealed class AssignmentSnapshot
    { public int RoomId, Price, ActorId; public string BookingId; }
    [Serializable] public sealed class RoomSnapshot
    {
        public int Id; public float Temperature, Noise, SourceNoise, ReceivedNoise, PowerLossConditionSeverity;
        public bool HasPower; public string CircuitId, GuestId, ReservedGuestId, DepartingGuestId;
        public Cleanliness Cleanliness; public RepairState RepairState; public HousekeepingState TurnoverState;
        public RoomOccupancyState OccupancyState; public RoomPrivacyState PrivacyState; public RoomDoorState DoorState;
        public int RadiatorSetting; public float LampCondition; public bool LampBroken;
    }
    [Serializable] public sealed class NeedSnapshot
    { public float Severity, ExposureSeconds, Dissatisfaction; }
    [Serializable] public sealed class GuestSnapshot
    {
        public BookingSnapshot Application; public int RoomId, Price, CompensationCredit;
        public bool Compensated, HasNeeds, ExpiredRoomComplaint, ReceiptPosted;
        public float CheckInWaitingSeconds, CheckInDelayPenaltySeconds, Elapsed, QualityIntegral, ExpiredComplaintSeconds;
        public float ColdExposureSeconds, HotExposureSeconds, NoiseExposureSeconds, DirtyExposureSeconds, FixtureExposureSeconds, PowerLossExposureSeconds;
        public float CombinedRoomDeficit, ServiceIntegral;
        public float BlanketComfortBonus, ServiceSatisfactionAdjustment;
        public NeedSnapshot Temperature, Noise, RoomCondition, Service;
        public AgentSnapshot Agent;
        public GuestMemorySnapshot Memory;
        public GuestPerceptionSnapshot Perception;
    }
    [Serializable] public sealed class ActivitySnapshot
    { public GuestActivity Activity; public float Duration; }
    [Serializable] public sealed class AgentSnapshot
    {
        public GuestAgentState State; public GuestActivity Activity;
        public float ArrivalTime, SleepTime, CheckoutTime, StateChangedAt, WaitingSeconds, WaitingPatience;
        public bool HasWakeTime;
        public float WakeTime;
        public float HeatingDemandMultiplier, NoiseOutput, QuietUntil, NextActivityTime, ActivityEndsAt;
        public bool CheckedIn, HasReachedRoom, IsRelocating, HasNextActivityTime, HasActivityEnd, PatienceEventSent, SleepStarted;
        public bool RequiresActivityStaging, ActivityStaged, TemporarySleep, HasPendingActivityDuration, HasAwayReturnTime;
        public float PendingActivityDuration, AwayReturnTime;
        public int PendingMoveRoomId, TransferFromRoomId, ActivityIndex;
        public string ResponseActionId;
        public string DirectServiceIntentId;
        public int ResponseActionVersion;
        public ActivitySnapshot[] Schedule;
    }
    [Serializable] public sealed class BoilerSnapshot
    {
        public float Condition, Load, HeatingOutput, Pressure, FailureExposure, OccupancyLoad, LoadOverride, Stress01, MaintenanceEndsAt;
        public bool Failed, HasLoadOverride, EmergencyPatchActive, CapacityUpgradePurchased; public int ReliefActorId;
    }
    [Serializable] public sealed class CircuitSnapshot
    { public string Id; public float ActualRequestedLoad, LoadOverride, OverloadSeconds; public bool HasLoadOverride, Warning, Tripped; public int TripCount; }
    [Serializable] public sealed class ConsumerSnapshot
    { public string Id, CircuitId; public int RoomId; public float RequestedLoad, DeliveredLoad; }
    [Serializable] public sealed class HeaterSnapshot
    { public string Id; public int RoomId; public bool SwitchedOn, Powered; public float HeatOutput, ElectricalLoad; }
    [Serializable] public sealed class KeySnapshot
    { public int RoomId, PlayerId; public string GuestId; public RoomKeyLocation Location; }
    [Serializable] public sealed class LinenSnapshot
    { public string Id; public LinenKind Kind; public LinenLocation Location; public int PlayerId, SourceRoomId, Generation, ShelfSlotIndex; }
    [Serializable] public sealed class TurnoverSnapshot
    {
        public int RoomId, Generation, WorkingPlayerId, CleanLinenGeneration;
        public string DirtyLinenId, CleanLinenId; public long QueuedOrder;
        public RoomPreparationStep Step; public HousekeepingState State; public float ProgressSeconds, RequiredSeconds;
    }
    [Serializable] public sealed class IncidentSnapshot
    {
        public string Id, GuestId, MeasuredCause, ResolutionReason; public int RoomId;
        public IncidentReason Reason; public SituationStage Stage;
        public bool Active, Resolved, PausedForTransfer, AttentionAcknowledged, ResponseAccepted, HasOccurred;
        public float Age, Severity, ExposureSeconds, Dissatisfaction, StageAge, ResponseReliefRemainingSeconds;
        public float ConditionSeconds, RecoverySeconds, ExposureBaseline, LastNeedExposure, ReopenCooldown;
        public SituationCauseSnapshot Cause;
        public float EffectivePatienceMultiplier;
        public int EpisodeCount;
        public bool IgnoreRecorded, HasContactedStaff, ComplaintRecorded;
        public string ResponseId;
        public SituationHistorySnapshot[] History;
    }
    [Serializable] public sealed class RequestSnapshot
    { public string Id, MeasuredCause; public float Age; public bool Resolved, Compensated; }
    [Serializable] public sealed class ReceiptSnapshot
    { public string GuestId, Name, Review; public int RoomId, Price, Compensation; public float Satisfaction; }
    [Serializable] public sealed class ReportSnapshot
    { public int Day, OpeningCash, OperatingCost, Cash, MaintenanceSpend, CapitalSpend; public float Reputation, ServiceSeconds; public ReceiptSnapshot[] Receipts; }
    [Serializable] public sealed class MaintenanceSnapshot
    { public int Day, ActorId, Cost, CashAfter; public MaintenanceChoice Choice; public float ConditionBefore, ConditionAfter; }
    [Serializable] public sealed class NoiseOverrideSnapshot
    { public int RoomId; public float Value; }
}
