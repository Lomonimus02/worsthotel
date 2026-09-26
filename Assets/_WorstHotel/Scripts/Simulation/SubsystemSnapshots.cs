using System;
using System.Collections.Generic;
using System.Linq;

namespace WorstHotel
{
    public sealed partial class HotelGameClock
    {
        internal bool ReadOnlyMirror;
        internal void RestoreSnapshot(float time,float speed) { SimulationTime=time;Speed=speed; }
    }
    public sealed partial class BoilerSystem
    {
        internal bool ReadOnlyMirror;
        internal BoilerSnapshot CaptureSnapshot() => new BoilerSnapshot{Condition=Condition,Load=Load,HeatingOutput=HeatingOutput,Pressure=Pressure,Failed=Failed,
            ReliefActorId=ReliefActorId,FailureExposure=FailureExposure,OccupancyLoad=occupancyLoad,HasLoadOverride=LoadOverride.HasValue,LoadOverride=LoadOverride??0,Stress01=Stress01};
        internal void RestoreSnapshot(BoilerSnapshot s)
        {Condition=s.Condition;Load=s.Load;HeatingOutput=s.HeatingOutput;Pressure=s.Pressure;Failed=s.Failed;ReliefActorId=s.ReliefActorId;FailureExposure=s.FailureExposure;occupancyLoad=s.OccupancyLoad;LoadOverride=s.HasLoadOverride?s.LoadOverride:(float?)null;Stress01=s.Stress01;}
    }
    public sealed partial class EconomySystem
    {
        internal bool ReadOnlyMirror;
        internal void RestoreSnapshot(int cash,float reputation,IEnumerable<DayReport> reports)
        {Cash=cash;Reputation=reputation;settledDays.Clear();foreach(var report in reports)settledDays.Add(report.DayNumber,report);}
    }
    public sealed partial class GuestScheduleSystem
    {
        internal bool ReadOnlyMirror;
        internal void RestoreSnapshot(IEnumerable<GuestStay> guests) {schedules.Clear();schedules.AddRange(guests.Where(g=>g.Agent!=null).Select(g=>g.Agent.Schedule));}
    }
    public sealed partial class HeaterSystem
    {
        internal bool ReadOnlyMirror;
        internal HeaterSnapshot[] CaptureSnapshot() => ordered.Select(h=>new HeaterSnapshot{Id=h.Id,RoomId=h.RoomId??0,SwitchedOn=h.SwitchedOn,Powered=h.Powered,HeatOutput=h.Settings.HeatOutput,ElectricalLoad=h.Settings.ElectricalLoad}).ToArray();
        internal void RestoreSnapshot(HeaterSnapshot[] data)
        {
            var previous=new Dictionary<string,PortableHeaterState>(heaters);heaters.Clear();ordered.Clear();
            foreach(var s in data)
            {
                if(!previous.TryGetValue(s.Id,out var h) || h.Settings.HeatOutput!=s.HeatOutput || h.Settings.ElectricalLoad!=s.ElectricalLoad)
                    h=new PortableHeaterState(s.Id,new HeaterSettings(s.HeatOutput,s.ElectricalLoad));
                h.RoomId=s.RoomId==0?(int?)null:s.RoomId;h.SwitchedOn=s.SwitchedOn;h.Powered=s.Powered;heaters.Add(h.Id,h);ordered.Add(h);
            }
        }
    }
    public sealed partial class ElectricalSystem
    {
        internal bool ReadOnlyMirror;
        internal CircuitSnapshot[] CaptureSnapshot() => Circuits.Select(c=>new CircuitSnapshot{Id=c.Id,ActualRequestedLoad=c.ActualRequestedLoad,HasLoadOverride=c.LoadOverride.HasValue,LoadOverride=c.LoadOverride??0,OverloadSeconds=c.OverloadSeconds,Warning=c.Warning,Tripped=c.Tripped,TripCount=c.TripCount}).ToArray();
        internal ConsumerSnapshot[] CaptureConsumers() => Consumers.Select(c=>new ConsumerSnapshot{Id=c.Id,RoomId=c.RoomId??0,CircuitId=SnapshotData.OptionalId(c.CircuitId),RequestedLoad=c.RequestedLoad,DeliveredLoad=c.DeliveredLoad}).ToArray();
        internal void RestoreSnapshot(CircuitSnapshot[] circuits,ConsumerSnapshot[] consumers)
        {
            foreach(var s in circuits){var c=Find(s.Id);c.ActualRequestedLoad=s.ActualRequestedLoad;c.LoadOverride=s.HasLoadOverride?s.LoadOverride:(float?)null;c.OverloadSeconds=s.OverloadSeconds;c.Warning=s.Warning;c.Tripped=s.Tripped;c.TripCount=s.TripCount;}
            Consumers=Array.AsReadOnly(consumers.Select(c=>new PowerConsumer(c.Id,c.RoomId==0?(int?)null:c.RoomId,SnapshotData.OptionalId(c.CircuitId),c.RequestedLoad,c.DeliveredLoad)).ToArray());
        }
    }
    public sealed partial class RoomKeySystem
    {
        internal bool ReadOnlyMirror;
        internal KeySnapshot[] CaptureSnapshot() => Items.Select(k=>new KeySnapshot{RoomId=k.RoomId,PlayerId=k.PlayerId??-1,GuestId=SnapshotData.OptionalId(k.GuestId),Location=k.Location}).ToArray();
        internal void RestoreSnapshot(KeySnapshot[] data)
        {foreach(var s in data){var k=Find(s.RoomId);k.PlayerId=s.PlayerId<0?(int?)null:s.PlayerId;k.GuestId=SnapshotData.OptionalId(s.GuestId);k.Location=s.Location;}}
    }
    public sealed partial class HousekeepingSystem
    {
        internal bool ReadOnlyMirror;
        internal LinenSnapshot[] CaptureLinens() => Linens.Select(l=>new LinenSnapshot{Id=l.Id,Kind=l.Kind,Location=l.Location,PlayerId=l.PlayerId??-1,SourceRoomId=l.SourceRoomId??0,Generation=l.Generation,ShelfSlotIndex=l.ShelfSlotIndex}).ToArray();
        internal TurnoverSnapshot[] CaptureTasks() => ordered.Select(t=>new TurnoverSnapshot{RoomId=t.RoomId,Generation=t.Generation,WorkingPlayerId=t.WorkingPlayerId??-1,CleanLinenGeneration=t.CleanLinenGeneration,DirtyLinenId=t.DirtyLinenId,CleanLinenId=SnapshotData.OptionalId(t.CleanLinenId),QueuedOrder=t.QueuedOrder,Step=t.Step,State=t.State,ProgressSeconds=t.ProgressSeconds,RequiredSeconds=t.RequiredSeconds}).ToArray();
        internal void ValidateSnapshot(LinenSnapshot[] data)
        {
            foreach(var s in data){var l=FindLinen(s.Id);SnapshotValidation.Require(l!=null && l.Kind==s.Kind && (l.SourceRoomId??0)==s.SourceRoomId && l.ShelfSlotIndex==s.ShelfSlotIndex,"Linen slot differs from this hotel.");}
        }
        internal void RestoreSnapshot(LinenSnapshot[] data,TurnoverSnapshot[] work,int refillDay,bool preserve)
        {
            foreach(var s in data){var l=FindLinen(s.Id);l.Location=s.Location;l.PlayerId=s.PlayerId<0?(int?)null:s.PlayerId;l.Generation=s.Generation;}
            var previous=new Dictionary<int,HousekeepingTask>(tasks);tasks.Clear();ordered.Clear();
            foreach(var s in work)
            {
                if(!preserve || !previous.TryGetValue(s.RoomId,out var t) || t.Generation!=s.Generation || t.DirtyLinenId!=s.DirtyLinenId || t.RequiredSeconds!=s.RequiredSeconds || t.QueuedOrder!=s.QueuedOrder)
                    t=new HousekeepingTask(s.RoomId,s.Generation,s.DirtyLinenId,s.RequiredSeconds,s.QueuedOrder);
                t.Step=s.Step;t.State=s.State;t.CleanLinenId=SnapshotData.OptionalId(s.CleanLinenId);t.CleanLinenGeneration=s.CleanLinenGeneration;t.WorkingPlayerId=s.WorkingPlayerId<0?(int?)null:s.WorkingPlayerId;t.ProgressSeconds=s.ProgressSeconds;
                tasks.Add(t.RoomId,t);ordered.Add(t);
            }
            queuedSequence=work.Length==0?0:work.Max(t=>t.QueuedOrder);LastRefillDay=refillDay;
        }
    }
    public sealed partial class NoiseSystem
    {
        internal bool ReadOnlyMirror;
        internal NoiseOverrideSnapshot[] CaptureSnapshot() => overrides.OrderBy(x=>x.Key).Select(x=>new NoiseOverrideSnapshot{RoomId=x.Key,Value=x.Value}).ToArray();
        internal void RestoreSnapshot(NoiseOverrideSnapshot[] data){overrides.Clear();foreach(var s in data)overrides.Add(s.RoomId,s.Value);}
    }
    public sealed partial class IncidentSystem
    {
        internal bool ReadOnlyMirror;
        internal IncidentSnapshot[] CaptureSnapshot() => incidents.Values.OrderBy(i=>i.Id).Select(i=>new IncidentSnapshot
        {
            Id=i.Id,GuestId=i.GuestId,RoomId=i.RoomId,Reason=i.Reason,Stage=i.Stage,Active=i.Active,Resolved=i.Resolved,Age=i.Age,
            MeasuredCause=i.MeasuredCause,Severity=i.Severity,ExposureSeconds=i.ExposureSeconds,Dissatisfaction=i.Dissatisfaction,StageAge=i.StageAge,
            PausedForTransfer=i.PausedForTransfer,AttentionAcknowledged=i.AttentionAcknowledged,ResponseAccepted=i.ResponseAccepted,
            ResponseReliefRemainingSeconds=i.ResponseReliefRemainingSeconds,ResolutionReason=i.ResolutionReason,HasOccurred=i.HasOccurred,
            ConditionSeconds=i.ConditionSeconds,RecoverySeconds=i.RecoverySeconds,ExposureBaseline=i.ExposureBaseline,LastNeedExposure=i.LastNeedExposure,ReopenCooldown=i.ReopenCooldown,
            Cause=SnapshotData.Capture(i.Cause),EffectivePatienceMultiplier=i.EffectivePatienceMultiplier,EpisodeCount=i.EpisodeCount,
            IgnoreRecorded=i.IgnoreRecorded,HasContactedStaff=i.HasContactedStaff,ComplaintRecorded=i.ComplaintRecorded,
            ResponseId=SnapshotData.OptionalId(i.Response?.Id),
            History=i.History.Select(h=>new SituationHistorySnapshot{Time=h.Time,Reason=h.Reason}).ToArray()
        }).ToArray();
        internal HotelIncident SnapshotIncident(string id)=>incidents[id];
        internal void RestoreSnapshot(IncidentSnapshot[] data,IReadOnlyDictionary<string,GuestStay> guests,bool preserve)
        {
            var previous=new Dictionary<string,HotelIncident>(incidents);incidents.Clear();livingRooms.Clear();
            foreach(var s in data)
            {
                if(!preserve || !previous.TryGetValue(s.Id,out var i) || i.GuestName!=guests[s.GuestId].Name)i=new HotelIncident(guests[s.GuestId],s.Reason,LivingEnabled,s.Cause?.SourceEntityId);
                i.RoomId=s.RoomId;i.Active=s.Active;i.Resolved=s.Resolved;i.Age=s.Age;i.MeasuredCause=s.MeasuredCause;i.Stage=s.Stage;i.Severity=s.Severity;i.ExposureSeconds=s.ExposureSeconds;i.Dissatisfaction=s.Dissatisfaction;i.StageAge=s.StageAge;
                i.PausedForTransfer=s.PausedForTransfer;i.AttentionAcknowledged=s.AttentionAcknowledged;i.ResponseAccepted=s.ResponseAccepted;i.ResponseReliefRemainingSeconds=s.ResponseReliefRemainingSeconds;i.ResolutionReason=s.ResolutionReason;
                i.HasOccurred=s.HasOccurred;i.ConditionSeconds=s.ConditionSeconds;i.RecoverySeconds=s.RecoverySeconds;i.ExposureBaseline=s.ExposureBaseline;i.LastNeedExposure=s.LastNeedExposure;i.ReopenCooldown=s.ReopenCooldown;incidents.Add(i.Id,i);
                i.Cause=SnapshotData.Cause(s.Cause);i.EffectivePatienceMultiplier=s.EffectivePatienceMultiplier;i.EpisodeCount=s.EpisodeCount;
                i.IgnoreRecorded=s.IgnoreRecorded;i.HasContactedStaff=s.HasContactedStaff;i.ComplaintRecorded=s.ComplaintRecorded;i.Response=null;i.history.Clear();
                i.history.AddRange(s.History.Select(h=>new SituationHistoryEntry(h.Time,h.Reason)));
            }
        }
    }
    public sealed partial class RequestSystem
    {
        internal bool ReadOnlyMirror;
        internal RequestSnapshot[] CaptureSnapshot() => Items.Select(r=>new RequestSnapshot{Id=r.Id,MeasuredCause=r.MeasuredCause,Age=r.Age,Resolved=r.Resolved,Compensated=r.Compensated}).ToArray();
        internal void RestoreSnapshot(RequestSnapshot[] data,bool preserve)
        {
            var previous=new Dictionary<string,HotelRequest>(requests);requests.Clear();compensatedGuests.Clear();
            foreach(var s in data){var source=incidents.SnapshotIncident(s.Id);if(!preserve || !previous.TryGetValue(s.Id,out var r) || !ReferenceEquals(r.Source,source))r=new HotelRequest(source);
                r.MeasuredCause=s.MeasuredCause;r.Age=s.Age;r.Resolved=s.Resolved;r.Compensated=s.Compensated;requests.Add(r.Id,r);if(r.Compensated)compensatedGuests.Add(r.GuestId);}
        }
    }
    public sealed partial class PlanningSystem
    {
        public bool IsReadOnlyMirror { get; private set; }
        public PlanningSnapshot CaptureSnapshot()=>new PlanningSnapshot{Applications=Applications.Select(SnapshotData.Capture).ToArray(),Assignments=Assignments.Select(SnapshotData.Capture).ToArray(),IsCommitted=IsCommitted};
        public static PlanningSystem FromSnapshot(IEnumerable<RoomState> rooms,EconomySettings economy,BoilerSettings boiler,PlanningSnapshot snapshot)
        {
            if(rooms==null)throw new ArgumentNullException(nameof(rooms));var states=rooms.ToArray();SnapshotValidation.Plan(snapshot,states.Select(r=>r.Profile.Id),economy);
            var plan=new PlanningSystem(states,snapshot.Applications.Select(SnapshotData.Booking),economy,boiler);
            foreach(var a in snapshot.Assignments)plan.assignments.Add(a.RoomId,SnapshotData.Assignment(a));plan.IsCommitted=snapshot.IsCommitted;plan.IsReadOnlyMirror=true;return plan;
        }
    }
}

