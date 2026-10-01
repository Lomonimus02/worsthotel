using System;
using System.Linq;

namespace WorstHotel
{
    internal static partial class SnapshotData
    {
        // JsonUtility represents a null string as an empty string. Only optional IDs use
        // this equivalence; do not trim or otherwise repair a nonempty wire identifier.
        internal static string OptionalId(string id) => string.IsNullOrEmpty(id) ? null : id;
        internal static bool SameIdentity(GuestStay a,GuestStay b)
        {
            if(a.Application.SpecialKind!=b.Application.SpecialKind || a.GuestId!=b.GuestId || a.Name!=b.Name || a.Price!=b.Price || a.Application.ReferencePrice!=b.Application.ReferencePrice || (a.Agent==null)!=(b.Agent==null))return false;
            var x=a.Application.Archetype;var y=b.Application.Archetype;
            if(x.Kind!=y.Kind || x.Traits!=y.Traits || x.Label!=y.Label || x.Description!=y.Description || x.ReferencePrice!=y.ReferencePrice)return false;
            var xn=x.Needs;var yn=y.Needs;
            if(!new[]{x.HeatingDemand,x.ColdThreshold,x.ColdPenaltyWeight,x.PriceSensitivity,x.Patience,x.NoiseTolerance,xn.PreferredTemperatureMin,xn.PreferredTemperatureMax,xn.ToleranceTemperatureMin,xn.ToleranceTemperatureMax,xn.PreferredNoise,xn.NoiseTolerance,xn.PatienceSeconds}
                .SequenceEqual(new[]{y.HeatingDemand,y.ColdThreshold,y.ColdPenaltyWeight,y.PriceSensitivity,y.Patience,y.NoiseTolerance,yn.PreferredTemperatureMin,yn.PreferredTemperatureMax,yn.ToleranceTemperatureMin,yn.ToleranceTemperatureMax,yn.PreferredNoise,yn.NoiseTolerance,yn.PatienceSeconds}))return false;
            if(a.Agent==null)return true;
            return a.Agent.WaitingPatience==b.Agent.WaitingPatience && a.Agent.ArrivalTime==b.Agent.ArrivalTime && a.Agent.Schedule.SleepTime==b.Agent.Schedule.SleepTime && a.Agent.Schedule.WakeTime==b.Agent.Schedule.WakeTime &&
                a.Agent.Schedule.MorningActivityIndex==b.Agent.Schedule.MorningActivityIndex && a.Agent.Schedule.OutingReturnAt==b.Agent.Schedule.OutingReturnAt && a.Agent.Schedule.Activities.SequenceEqual(b.Agent.Schedule.Activities);
        }
        internal static ProfileSnapshot Capture(GuestProfile p) => new ProfileSnapshot
        {
            Kind=p.Kind, Traits=p.Traits, Label=p.Label, Description=p.Description, ReferencePrice=p.ReferencePrice,
            HeatingDemand=p.HeatingDemand, ColdThreshold=p.ColdThreshold, ColdPenaltyWeight=p.ColdPenaltyWeight,
            PriceSensitivity=p.PriceSensitivity, Patience=p.Patience, NoiseTolerance=p.NoiseTolerance,
            PreferredTemperatureMin=p.Needs.PreferredTemperatureMin, PreferredTemperatureMax=p.Needs.PreferredTemperatureMax,
            ToleranceTemperatureMin=p.Needs.ToleranceTemperatureMin, ToleranceTemperatureMax=p.Needs.ToleranceTemperatureMax,
            PreferredNoise=p.Needs.PreferredNoise, NeedNoiseTolerance=p.Needs.NoiseTolerance, NeedPatience=p.Needs.PatienceSeconds
        };
        internal static GuestProfile Profile(ProfileSnapshot p) => new GuestProfile(p.Kind,p.Label,p.Description,p.ReferencePrice,
            p.HeatingDemand,p.ColdThreshold,p.ColdPenaltyWeight,p.PriceSensitivity,p.Patience,p.NoiseTolerance,p.Traits,
            new NeedProfile(p.PreferredTemperatureMin,p.PreferredTemperatureMax,p.ToleranceTemperatureMin,p.ToleranceTemperatureMax,
                p.PreferredNoise,p.NeedNoiseTolerance,p.NeedPatience));
        internal static BookingSnapshot Capture(BookingApplication b) => new BookingSnapshot
            { Id=b.Id, GuestName=b.GuestName, ReferencePrice=b.ReferencePrice, SpecialKind=b.SpecialKind, Profile=Capture(b.Archetype) };
        internal static BookingApplication Booking(BookingSnapshot b) => new BookingApplication(b.Id,b.GuestName,Profile(b.Profile),b.ReferencePrice,b.SpecialKind);
        internal static AssignmentSnapshot Capture(BookingAssignment a) => new AssignmentSnapshot
            { RoomId=a.RoomId, BookingId=a.BookingId, Price=a.Price, ActorId=a.ActorId };
        internal static BookingAssignment Assignment(AssignmentSnapshot a) => new BookingAssignment(a.RoomId,a.BookingId,a.Price,a.ActorId);
        internal static NeedSnapshot Capture(GuestNeedSnapshot n) => new NeedSnapshot
            { Severity=n.Severity,ExposureSeconds=n.ExposureSeconds,Dissatisfaction=n.Dissatisfaction };
        internal static GuestNeedSnapshot Need(NeedSnapshot n) => new GuestNeedSnapshot(n.Severity,n.ExposureSeconds,n.Dissatisfaction);
        internal static AgentSnapshot Capture(GuestAgent a) => a == null ? null : new AgentSnapshot
        {
            State=a.State,Activity=a.Activity,ArrivalTime=a.ArrivalTime,SleepTime=a.Schedule.SleepTime,CheckoutTime=a.CheckoutTime,
            HasWakeTime=Number.IsFinite(a.Schedule.WakeTime),WakeTime=Number.IsFinite(a.Schedule.WakeTime)?a.Schedule.WakeTime:0,
            MorningActivityIndex=a.Schedule.MorningActivityIndex,OutingReturnAt=a.Schedule.OutingReturnAt,
            StateChangedAt=a.StateChangedAt,WaitingSeconds=a.WaitingSeconds,WaitingPatience=a.WaitingPatience,
            HeatingDemandMultiplier=a.HeatingDemandMultiplier,NoiseOutput=a.NoiseOutput,QuietUntil=a.QuietUntil,
            HasNextActivityTime=Number.IsFinite(a.NextActivityTime),NextActivityTime=Number.IsFinite(a.NextActivityTime)?a.NextActivityTime:0,
            HasActivityEnd=Number.IsFinite(a.ActivityEndsAt),ActivityEndsAt=Number.IsFinite(a.ActivityEndsAt)?a.ActivityEndsAt:0,
            CheckedIn=a.CheckedIn,HasReachedRoom=a.HasReachedRoom,IsRelocating=a.IsRelocating,
            PendingMoveRoomId=a.PendingMoveRoomId??0,TransferFromRoomId=a.TransferFromRoomId??0,
            ActivityIndex=a.ActivityIndex,PatienceEventSent=a.PatienceEventSent,SleepStarted=a.SleepStarted,
            ResponseActionId=OptionalId(a.ResponseActionId),ResponseActionVersion=a.ResponseActionVersion,
            DirectServiceIntentId=OptionalId(a.DirectServiceIntentId),
            RequiresActivityStaging=a.RequiresActivityStaging,ActivityStaged=a.ActivityStaged,TemporarySleep=a.TemporarySleep,
            HasPendingActivityDuration=Number.IsFinite(a.PendingActivityDuration),PendingActivityDuration=Number.IsFinite(a.PendingActivityDuration)?a.PendingActivityDuration:0,
            HasAwayReturnTime=Number.IsFinite(a.AwayReturnTime),AwayReturnTime=Number.IsFinite(a.AwayReturnTime)?a.AwayReturnTime:0,
            Schedule=a.Schedule.Activities.Select(s=>new ActivitySnapshot{Activity=s.Activity,Duration=s.Duration}).ToArray()
        };
        internal static GuestSnapshot Capture(GuestStay g) => new GuestSnapshot
        {
            Application=Capture(g.Application),RoomId=g.RoomId,Price=g.Price,CompensationCredit=g.CompensationCredit,Compensated=g.Compensated,
            ReceiptPosted=g.ReceiptPosted,LockedOut=g.LockedOut,KeyLossConsidered=g.KeyLossConsidered,AbandonedCheckIn=g.AbandonedCheckIn,
            LockoutSeconds=g.LockoutSeconds,LuggageDelaySeconds=g.LuggageDelaySeconds,
            CheckInWaitingSeconds=g.CheckInWaitingSeconds,CheckInDelayPenaltySeconds=g.CheckInDelayPenaltySeconds,
            Elapsed=g.Elapsed,QualityIntegral=g.QualityIntegral,ExpiredComplaintSeconds=g.ExpiredComplaintSeconds,
            ColdExposureSeconds=g.ColdExposureSeconds,HotExposureSeconds=g.HotExposureSeconds,NoiseExposureSeconds=g.NoiseExposureSeconds,
            DirtyExposureSeconds=g.DirtyExposureSeconds,FixtureExposureSeconds=g.FixtureExposureSeconds,PowerLossExposureSeconds=g.PowerLossExposureSeconds,
            HasNeeds=g.Needs!=null,Temperature=g.Needs!=null?Capture(g.Needs.Temperature):null,Noise=g.Needs!=null?Capture(g.Needs.Noise):null,
            RoomCondition=g.Needs!=null?Capture(g.Needs.RoomCondition):null,Service=g.Needs!=null?Capture(g.Needs.Service):null,
            CombinedRoomDeficit=g.Needs?.CombinedRoomDeficit??0,ServiceIntegral=g.Needs?.ServiceIntegral??0,
            ExpiredRoomComplaint=g.Needs?.ExpiredRoomComplaint??false,Agent=Capture(g.Agent),
            Memory=Capture(g.Memory),Perception=Capture(g.Perception),EarlyCheckout=Capture(g.EarlyCheckout),
            BlanketComfortBonus=g.BlanketComfortBonus,ServiceSatisfactionAdjustment=g.ServiceSatisfactionAdjustment
        };
        internal static GuestStay Guest(GuestSnapshot s)
        {
            var g = new GuestStay(Booking(s.Application),s.RoomId,s.Price);
            if(s.Agent!=null)
            {
                var a=s.Agent;
                g.Agent=new GuestAgent(g.GuestId,new GuestSchedule(g.GuestId,a.ArrivalTime,a.SleepTime,a.CheckoutTime,
                    a.Schedule.Select(x=>new GuestScheduleEntry(x.Activity,x.Duration)).ToArray(),a.HasWakeTime?a.WakeTime:float.PositiveInfinity,
                    a.MorningActivityIndex,a.OutingReturnAt),a.WaitingPatience);
            }
            Restore(g,s);
            return g;
        }
        internal static void Restore(GuestStay g, GuestSnapshot s)
        {
            Restore(g.Memory,s.Memory);Restore(g.Perception,s.Perception);
            Restore(g.EarlyCheckout,s.EarlyCheckout);
            g.RoomId=s.RoomId;g.CompensationCredit=s.CompensationCredit;g.Compensated=s.Compensated;
            g.ReceiptPosted=s.ReceiptPosted;g.LockedOut=s.LockedOut;g.KeyLossConsidered=s.KeyLossConsidered;g.AbandonedCheckIn=s.AbandonedCheckIn;
            g.LockoutSeconds=s.LockoutSeconds;g.LuggageDelaySeconds=s.LuggageDelaySeconds;
            g.BlanketComfortBonus=s.BlanketComfortBonus;g.ServiceSatisfactionAdjustment=s.ServiceSatisfactionAdjustment;
            g.CheckInWaitingSeconds=s.CheckInWaitingSeconds;g.CheckInDelayPenaltySeconds=s.CheckInDelayPenaltySeconds;
            g.Elapsed=s.Elapsed;g.QualityIntegral=s.QualityIntegral;g.ExpiredComplaintSeconds=s.ExpiredComplaintSeconds;
            g.ColdExposureSeconds=s.ColdExposureSeconds;g.HotExposureSeconds=s.HotExposureSeconds;g.NoiseExposureSeconds=s.NoiseExposureSeconds;
            g.DirtyExposureSeconds=s.DirtyExposureSeconds;g.FixtureExposureSeconds=s.FixtureExposureSeconds;g.PowerLossExposureSeconds=s.PowerLossExposureSeconds;
            if(s.HasNeeds)
            {
                if(g.Needs==null)g.Needs=new GuestNeeds();
                g.Needs.Temperature=Need(s.Temperature);g.Needs.Noise=Need(s.Noise);g.Needs.RoomCondition=Need(s.RoomCondition);g.Needs.Service=Need(s.Service);
                g.Needs.CombinedRoomDeficit=s.CombinedRoomDeficit;g.Needs.ServiceIntegral=s.ServiceIntegral;g.Needs.ExpiredRoomComplaint=s.ExpiredRoomComplaint;
            }
            else g.Needs=null;
            if(s.Agent==null){g.Agent=null;return;}
            var a=s.Agent;var target=g.Agent;
            target.Schedule.CheckoutTime=a.CheckoutTime;
            target.State=a.State;target.Activity=a.Activity;target.StateChangedAt=a.StateChangedAt;target.WaitingSeconds=a.WaitingSeconds;
            target.HeatingDemandMultiplier=a.HeatingDemandMultiplier;target.NoiseOutput=a.NoiseOutput;target.QuietUntil=a.QuietUntil;
            target.NextActivityTime=a.HasNextActivityTime?a.NextActivityTime:float.PositiveInfinity;
            target.ActivityEndsAt=a.HasActivityEnd?a.ActivityEndsAt:float.PositiveInfinity;
            target.CheckedIn=a.CheckedIn;target.HasReachedRoom=a.HasReachedRoom;target.IsRelocating=a.IsRelocating;
            target.PendingMoveRoomId=a.PendingMoveRoomId==0?(int?)null:a.PendingMoveRoomId;
            target.TransferFromRoomId=a.TransferFromRoomId==0?(int?)null:a.TransferFromRoomId;
            target.ActivityIndex=a.ActivityIndex;target.PatienceEventSent=a.PatienceEventSent;target.SleepStarted=a.SleepStarted;
            target.ResponseActionId=OptionalId(a.ResponseActionId);target.ResponseActionVersion=a.ResponseActionVersion;
            target.DirectServiceIntentId=OptionalId(a.DirectServiceIntentId);
            target.RequiresActivityStaging=a.RequiresActivityStaging;target.ActivityStaged=a.ActivityStaged;target.TemporarySleep=a.TemporarySleep;
            target.PendingActivityDuration=a.HasPendingActivityDuration?a.PendingActivityDuration:float.PositiveInfinity;
            target.AwayReturnTime=a.HasAwayReturnTime?a.AwayReturnTime:float.PositiveInfinity;
        }
        internal static RoomSnapshot Capture(RoomState r) => new RoomSnapshot
        {
            Id=r.Profile.Id,Temperature=r.Temperature,Noise=r.Noise,SourceNoise=r.SourceNoise,ReceivedNoise=r.ReceivedNoise,
            RadiatorSetting=r.RadiatorSetting,LampCondition=r.LampCondition,LampBroken=r.LampBroken,
            UsedHours=r.UsedHours,ShowerHours=r.ShowerHours,DisplacedHours=r.DisplacedHours,Disorder=r.Disorder,
            PowerLossConditionSeverity=r.PowerLossConditionSeverity,HasPower=r.HasPower,CircuitId=OptionalId(r.CircuitId),
            GuestId=OptionalId(r.GuestId),ReservedGuestId=OptionalId(r.ReservedGuestId),DepartingGuestId=OptionalId(r.DepartingGuestId),
            Cleanliness=r.Cleanliness,RepairState=r.RepairState,TurnoverState=r.TurnoverState,
            OccupancyState=r.OccupancyState,PrivacyState=r.PrivacyState,DoorState=r.DoorState
        };
        internal static void Restore(RoomState r, RoomSnapshot s)
        {
            r.RadiatorSetting=s.RadiatorSetting;r.LampCondition=s.LampCondition;r.LampBroken=s.LampBroken;
            r.UsedHours=s.UsedHours;r.ShowerHours=s.ShowerHours;r.DisplacedHours=s.DisplacedHours;r.Disorder=s.Disorder;
            r.Temperature=s.Temperature;r.Noise=s.Noise;r.SourceNoise=s.SourceNoise;r.ReceivedNoise=s.ReceivedNoise;
            r.PowerLossConditionSeverity=s.PowerLossConditionSeverity;r.HasPower=s.HasPower;r.CircuitId=OptionalId(s.CircuitId);
            r.GuestId=OptionalId(s.GuestId);r.ReservedGuestId=OptionalId(s.ReservedGuestId);r.DepartingGuestId=OptionalId(s.DepartingGuestId);
            r.Cleanliness=s.Cleanliness;r.RepairState=s.RepairState;r.TurnoverState=s.TurnoverState;
            r.OccupancyState=s.OccupancyState;r.PrivacyState=s.PrivacyState;r.DoorState=s.DoorState;
        }
        internal static ReportSnapshot Capture(DayReport r) => r == null ? null : new ReportSnapshot
        {
            Day=r.DayNumber,OpeningCash=r.OpeningCash,OperatingCost=r.OperatingCost,Cash=r.Cash,Reputation=r.Reputation,ServiceSeconds=r.ServiceSeconds,MaintenanceSpend=r.MaintenanceSpend,CapitalSpend=r.CapitalSpend,
            LaundrySpend=r.LaundrySpend,BulbSpend=r.BulbSpend,
            Receipts=r.Receipts.Select(Capture).ToArray(),ContractPayment=Capture(r.ContractPayment)
        };
        internal static DayReport Report(ReportSnapshot r) => r == null ? null : new DayReport(r.Day,r.Receipts.Select(Receipt),
            r.OpeningCash,r.OperatingCost,r.Cash,r.Reputation,r.ServiceSeconds,r.MaintenanceSpend,r.CapitalSpend,Payment(r.ContractPayment),
            laundrySpend:r.LaundrySpend,bulbSpend:r.BulbSpend);
        internal static ContractPaymentSnapshot Capture(ContractPayment payment) => payment == null ? null : new ContractPaymentSnapshot
        { Due=payment.Due,PaidAmount=payment.PaidAmount,FundsBeforePayment=payment.FundsBeforePayment,AssessedRooms=payment.AssessedRooms,
            Period=payment.Period,DueAt=payment.DueAt };
        internal static ContractPayment Payment(ContractPaymentSnapshot payment) => payment == null ? null :
            new ContractPayment(payment.Due,payment.PaidAmount,payment.FundsBeforePayment,payment.AssessedRooms,payment.Period,payment.DueAt);

        // Unity may deserialize an absent nested class as an all-default object. Only the
        // complete empty representation denotes absence; partial/invalid records stay visible
        // to validation. Sequence and interval checks still reject missing required payments.
        internal static ContractPaymentSnapshot OptionalPayment(ContractPaymentSnapshot payment) =>
            payment != null && payment.Due == 0 && payment.PaidAmount == 0 && payment.FundsBeforePayment == 0 &&
            payment.AssessedRooms == 0 && payment.Period == 0 && payment.DueAt == 0 ? null : payment;

        internal static ReportSnapshot OptionalFinancialReport(ReportSnapshot report) =>
            report != null && report.Day == 0 && report.OpeningCash == 0 && report.OperatingCost == 0 && report.Cash == 0 &&
            report.MaintenanceSpend == 0 && report.CapitalSpend == 0 && report.LaundrySpend == 0 && report.BulbSpend == 0 &&
            report.Reputation == 0 && report.ServiceSeconds == 0 &&
            (report.Receipts == null || report.Receipts.Length == 0) && OptionalPayment(report.ContractPayment) == null ? null : report;

        internal static OwnershipContractSettingsSnapshot OptionalContract(OwnershipContractSettingsSnapshot contract) =>
            contract != null && contract.BaseDue == 0 && contract.DailyIncrease == 0 && contract.ExtraRoomCharge == 0 &&
            contract.FirstPaymentDay == 0 && contract.PaymentHour == 0 ? null : contract;
    }
}
