using System;
using System.Collections.Generic;
using System.Linq;

namespace WorstHotel
{
    // A fixed, bounded protocol. Validation finishes before any replica state is changed.
    internal static partial class SnapshotValidation
    {
        internal static void Require(bool valid, string message) { if (!valid) throw new ArgumentException(message); }
        internal static void Text(string value, int max = 160, bool optional = false) => Require((optional && value == null) || (value != null && value.Length <= max && (optional || !string.IsNullOrWhiteSpace(value))), "Invalid snapshot text.");
        internal static void OptionalId(string value, int max = 160)
        { Text(value,max,true);Require(string.IsNullOrEmpty(value)||!string.IsNullOrWhiteSpace(value),"Invalid optional snapshot identity."); }
        internal static void Range(float value, float min = 0, float max = float.MaxValue) => Require(Number.IsFinite(value) && value >= min && value <= max, "Invalid snapshot number.");
        internal static void Nonnegative(params float[] values) { foreach (var value in values) Range(value); }
        internal static void Unit(params float[] values) { foreach (var value in values) Range(value,0,1); }
        internal static void EnumValue<T>(T value) where T : struct => Require(Enum.IsDefined(typeof(T),value), "Unknown snapshot enum.");
        internal static T[] Array<T>(T[] values, int maximum) where T : class
        { Require(values != null && values.Length <= maximum && values.All(x=>x!=null), "Missing or oversized snapshot array."); return values; }
        internal static void Unique<T>(IEnumerable<T> values) => Require(values.Distinct().Count()==values.Count(),"Duplicate snapshot identity.");
        internal static void Booking(BookingSnapshot b)
        {
            Require(b!=null && b.Profile!=null,"Missing booking profile."); Text(b.Id);Text(b.GuestName);Require(b.ReferencePrice>0,"Invalid reference price.");
            var p=b.Profile;EnumValue(p.Kind);Require(((int)p.Traits & ~63)==0,"Invalid traits.");Text(p.Label);Text(p.Description,2048,true);
            Require(p.ReferencePrice>0,"Invalid profile price.");Nonnegative(p.HeatingDemand,p.ColdPenaltyWeight,p.PriceSensitivity,p.Patience,p.NoiseTolerance,p.NeedPatience);
            Range(p.ColdThreshold,-100,100);Range(p.PreferredTemperatureMin,-100,100);Range(p.PreferredTemperatureMax,-100,100);
            Range(p.ToleranceTemperatureMin,-100,100);Range(p.ToleranceTemperatureMax,-100,100);Unit(p.PreferredNoise,p.NeedNoiseTolerance);
            SnapshotData.Booking(b); // Constructors also validate preference ordering and positive patience.
        }
        internal static void Need(NeedSnapshot n) { Require(n!=null,"Missing need.");Unit(n.Severity,n.Dissatisfaction);Range(n.ExposureSeconds); }
        internal static void Model(HotelModelSnapshot s, IReadOnlyCollection<int> roomIds, bool living, int linenCount, IEnumerable<string> circuitIds)
        {
            Require(s!=null && s.Version==HotelModelSnapshot.ProtocolVersion,"Unsupported snapshot protocol.");
            bool continuous = s.HasOperations;
            Require(s.Epoch>=0 && s.Sequence>=0 && s.Day>=0 && s.Day<=(continuous?1000000:3) && s.LastMaintenanceDay>=0 && s.LastMaintenanceDay<=s.Day && s.DebugGuestCounter>=0 && s.EventRevision>=0 && s.LastRefillDay>=0 && s.LastRefillDay<=(continuous?Math.Max(1,s.Day):3),"Invalid snapshot header.");
            Nonnegative(s.Time,s.SituationTime);Require(s.Speed==1 || s.Speed==4 || s.Speed==8,"Invalid clock speed.");Range(s.Reputation,0,100);Text(s.LastEvent,2048,true);
            var rs=Array(s.Rooms,6);Unique(rs.Select(x=>x.Id));Require(rs.Length==roomIds.Count && rs.All(x=>roomIds.Contains(x.Id)),"Room set differs from this hotel.");
            bool Room(int id,bool optional=false)=> (optional&&id==0)||roomIds.Contains(id);
            var gs=Array(s.Guests,continuous?128:6);foreach(var g in gs)
            {
                Booking(g.Application);Require(Room(g.RoomId) && g.Price>=0 && g.CompensationCredit>=0 && g.CompensationCredit<=g.Price,"Invalid guest room or credit.");
                Nonnegative(g.CheckInWaitingSeconds,g.CheckInDelayPenaltySeconds,g.Elapsed,g.QualityIntegral,g.ExpiredComplaintSeconds,g.ColdExposureSeconds,g.HotExposureSeconds,g.NoiseExposureSeconds,g.DirtyExposureSeconds,g.FixtureExposureSeconds,g.PowerLossExposureSeconds,g.ServiceIntegral);
                Range(g.CombinedRoomDeficit);Require((g.Agent!=null)==living && (!g.HasNeeds || living),"Guest mode differs from this hotel.");
                Range(g.BlanketComfortBonus,0,8);Range(g.ServiceSatisfactionAdjustment,-15,15);
                if(g.HasNeeds){Need(g.Temperature);Need(g.Noise);Need(g.RoomCondition);Need(g.Service);}
                if(g.Agent==null)continue;
                var a=g.Agent;EnumValue(a.State);EnumValue(a.Activity);Nonnegative(a.ArrivalTime,a.SleepTime,a.CheckoutTime,a.StateChangedAt,a.WaitingSeconds,a.WaitingPatience,a.HeatingDemandMultiplier,a.QuietUntil,a.NextActivityTime,a.ActivityEndsAt);Unit(a.NoiseOutput);
                Nonnegative(a.PendingActivityDuration,a.AwayReturnTime);
                Range(a.WakeTime);Require(!a.HasWakeTime || a.WakeTime>a.SleepTime && a.WakeTime<a.CheckoutTime,"Invalid morning wake time.");
                Require(continuous || !g.ReceiptPosted,"A legacy stay cannot have an operating checkout receipt.");
                Require(a.CheckoutTime>=a.ArrivalTime && a.WaitingPatience>0 && a.ActivityIndex>=0 && Room(a.PendingMoveRoomId,true) && Room(a.TransferFromRoomId,true),"Invalid guest schedule.");
                foreach(var entry in Array(a.Schedule,24))
                {
                    EnumValue(entry.Activity);Range(entry.Duration,float.Epsilon);
                    Require(entry.Activity != GuestActivity.AdjustRadiator && entry.Activity != GuestActivity.CallReception,
                        "A response action cannot be a scheduled leisure activity.");
                }
                Require(a.Schedule.Length>0,"Missing scheduled activities.");
            }
            Unique(gs.Select(g=>g.Application.Id));var guestIds=new HashSet<string>(gs.Select(g=>g.Application.Id));
            foreach(var g in gs)GuestAgency(g,roomIds,guestIds);
            var emitters=Array(s.NoiseSources,6);Unique(emitters.Select(n=>n.SourceEntityId));foreach(var source in emitters)NoiseSource(source,roomIds,guestIds);
            Unique(rs.Where(r=>!string.IsNullOrEmpty(r.GuestId)).Select(r=>r.GuestId));
            Unique(rs.Where(r=>!string.IsNullOrEmpty(r.ReservedGuestId)).Select(r=>r.ReservedGuestId));
            foreach(var r in rs)
            {
                Range(r.Temperature,-100,100);Unit(r.Noise,r.PowerLossConditionSeverity);Nonnegative(r.SourceNoise,r.ReceivedNoise);EnumValue(r.Cleanliness);EnumValue(r.RepairState);EnumValue(r.TurnoverState);
                EnumValue(r.OccupancyState);EnumValue(r.PrivacyState);EnumValue(r.DoorState);
                Require(r.RadiatorSetting>=0 && r.RadiatorSetting<=3,"Invalid radiator setting.");Range(r.LampCondition,0,100);
                OptionalId(r.GuestId);OptionalId(r.ReservedGuestId);OptionalId(r.DepartingGuestId);OptionalId(r.CircuitId,32);
                Require((string.IsNullOrEmpty(r.GuestId)||guestIds.Contains(r.GuestId)) && (string.IsNullOrEmpty(r.ReservedGuestId)||guestIds.Contains(r.ReservedGuestId)),"Unknown room guest.");
            }
            Require(s.Boiler!=null,"Missing boiler.");var b=s.Boiler;Range(b.Condition,0,100);Unit(b.HeatingOutput);Unit(b.Stress01);Nonnegative(b.Load,b.Pressure,b.FailureExposure,b.OccupancyLoad,b.LoadOverride);Require(b.ReliefActorId>=-1,"Invalid relief owner.");
            Nonnegative(b.MaintenanceEndsAt);
            Require(!b.Failed || !b.EmergencyPatchActive, "A new failure ends the active emergency patch.");
            Require(s.HasOperations || (b.Stress01 == 0 && !b.EmergencyPatchActive && b.MaintenanceEndsAt == 0), "Legacy boiler cannot contain continuous operating states.");
            Require(b.MaintenanceEndsAt == 0 || (s.Running && b.MaintenanceEndsAt > s.Time && b.HeatingOutput == 0 && b.ReliefActorId == -1),
                "Invalid boiler downtime state.");
            Require(continuous || !b.CapacityUpgradePurchased, "Legacy boiler cannot contain a capacity purchase.");
            var cs=Array(s.Circuits,2);Unique(cs.Select(c=>c.Id));Require(cs.Select(c=>c.Id).OrderBy(x=>x).SequenceEqual(circuitIds.OrderBy(x=>x)),"Circuit set differs.");
            OptionalId(s.UpgradedCircuitId,32);
            Require(string.IsNullOrEmpty(s.UpgradedCircuitId) || continuous && cs.Any(c=>c.Id==s.UpgradedCircuitId), "Invalid purchased branch.");
            foreach(var c in cs){Text(c.Id,32);Nonnegative(c.ActualRequestedLoad,c.LoadOverride,c.OverloadSeconds);Require(c.TripCount>=0,"Invalid trip count.");}
            var consumers=Array(s.Consumers,24);Unique(consumers.Select(c=>c.Id));foreach(var c in consumers){Text(c.Id);OptionalId(c.CircuitId,32);Require(Room(c.RoomId,true) && (string.IsNullOrEmpty(c.CircuitId)||cs.Any(x=>x.Id==c.CircuitId)),"Invalid consumer placement.");Nonnegative(c.RequestedLoad,c.DeliveredLoad);Require(c.DeliveredLoad<=c.RequestedLoad,"Invalid delivered power.");}
            var heaters=Array(s.Heaters,6);Unique(heaters.Select(h=>h.Id));foreach(var h in heaters){Text(h.Id);Require(Room(h.RoomId,true),"Invalid heater placement.");Range(h.HeatOutput,float.Epsilon);Range(h.ElectricalLoad,float.Epsilon);}
            var keys=Array(s.Keys,6);Require(keys.Length==rs.Length,"Incomplete keys.");Unique(keys.Select(k=>k.RoomId));Unique(keys.Where(k=>k.PlayerId>=0).Select(k=>k.PlayerId));
            Unique(keys.Where(k=>!string.IsNullOrEmpty(k.GuestId)).Select(k=>k.GuestId));
            foreach(var k in keys){Require(Room(k.RoomId) && k.PlayerId>=-1,"Invalid key.");EnumValue(k.Location);OptionalId(k.GuestId);Require((k.Location==RoomKeyLocation.HeldByPlayer)==(k.PlayerId>=0) && (k.Location==RoomKeyLocation.HeldByGuest)==!string.IsNullOrEmpty(k.GuestId),"Invalid key ownership.");if(k.Location==RoomKeyLocation.HeldByGuest)Require(guestIds.Contains(k.GuestId),"Unknown key guest.");}
            var ls=Array(s.Linens,12);Require(ls.Length==linenCount,"Incomplete linen slots.");Unique(ls.Select(l=>l.Id));Unique(ls.Where(l=>l.PlayerId>=0).Select(l=>l.PlayerId));
            foreach(var l in ls){Text(l.Id);EnumValue(l.Kind);EnumValue(l.Location);Require(l.Generation>=0 && l.PlayerId>=-1 && Room(l.SourceRoomId,true) && (l.Location==LinenLocation.HeldByPlayer)==(l.PlayerId>=0),"Invalid linen state.");}
            var ts=Array(s.Turnover,6);Unique(ts.Select(t=>t.RoomId));foreach(var t in ts){Require(Room(t.RoomId) && t.Generation>=1 && t.WorkingPlayerId>=-1 && t.CleanLinenGeneration>=0 && t.QueuedOrder>=0,"Invalid turnover task.");Text(t.DirtyLinenId);OptionalId(t.CleanLinenId);EnumValue(t.Step);EnumValue(t.State);Range(t.RequiredSeconds,float.Epsilon);Range(t.ProgressSeconds,0,t.RequiredSeconds);Require(ls.Any(l=>l.Id==t.DirtyLinenId && l.Kind==LinenKind.Dirty && l.SourceRoomId==t.RoomId && l.Generation==t.Generation),"Missing task dirty linen.");if(!string.IsNullOrEmpty(t.CleanLinenId))Require(ls.Any(l=>l.Id==t.CleanLinenId && l.Kind==LinenKind.Clean && l.Generation==t.CleanLinenGeneration),"Missing task clean linen.");}
            Unique(ts.Where(t=>t.WorkingPlayerId>=0).Select(t=>t.WorkingPlayerId));
            var incidents=Array(s.Incidents,256);Unique(incidents.Select(i=>i.Id));foreach(var i in incidents)
            {
                Text(i.Id,512);Text(i.GuestId);Text(i.MeasuredCause,2048,true);Text(i.ResolutionReason,2048,true);EnumValue(i.Reason);EnumValue(i.Stage);
                Require(guestIds.Contains(i.GuestId) && Room(i.RoomId),"Invalid situation room or guest.");
                if(living)
                {
                    Require(i.Cause!=null,"A living situation must identify its cause.");Cause(i.Cause,roomIds,guestIds);
                    Require(i.Id==new SituationKey(i.Reason,i.GuestId,i.Cause.SourceEntityId).ToString(),"Invalid causal situation identity.");
                    Require(i.Reason==IncidentReason.Noise || i.Reason==IncidentReason.Temperature || i.Reason==IncidentReason.RoomCondition,"Unsupported living situation family.");
                    if(i.Reason==IncidentReason.Noise)Require(!string.IsNullOrEmpty(i.Cause.SourceGuestId),"Noise situation requires an actual source guest.");
                }
                else Require(i.Id==i.GuestId+":"+i.Reason,"Invalid legacy situation identity.");
                Range(i.EffectivePatienceMultiplier,float.Epsilon,1);Require(i.EpisodeCount>=0 && i.EpisodeCount<=10000,"Invalid situation episode count.");
                foreach(var h in Array(i.History,128)){Range(h.Time);Text(h.Reason,2048);}
                Unit(i.Severity,i.Dissatisfaction);Nonnegative(i.Age,i.ExposureSeconds,i.StageAge,i.ResponseReliefRemainingSeconds,i.ConditionSeconds,i.RecoverySeconds,i.ExposureBaseline,i.LastNeedExposure,i.ReopenCooldown);
            }
            var requests=Array(s.Requests,256);Unique(requests.Select(r=>r.Id));foreach(var r in requests){Text(r.Id,512);Text(r.MeasuredCause,2048,true);Range(r.Age);Require(incidents.Any(i=>i.Id==r.Id),"Missing request source.");}
            var reports=Array(s.Reports,continuous?128:3);Unique(reports.Select(r=>r.Day));foreach(var r in reports)
            {Require(r.Day>=1 && r.Day<=s.Day && r.OperatingCost>=0,"Invalid report.");Range(r.Reputation,0,100);Range(r.ServiceSeconds);var receipts=Array(r.Receipts,continuous?128:6);Unique(receipts.Select(x=>x.GuestId));foreach(var x in receipts){Text(x.GuestId);Text(x.Name);Text(x.Review,4096,true);Require(Room(x.RoomId) && x.Price>=0 && x.Compensation>=0 && x.Compensation<=x.Price,"Invalid receipt.");Range(x.Satisfaction,0,100);}}
            foreach(var r in reports)Require(r.Receipts.Sum(x=>(long)x.Price)<=int.MaxValue && r.Receipts.Sum(x=>(long)x.Compensation)<=int.MaxValue,"Report totals overflow.");
            foreach(var r in reports)Require(r.MaintenanceSpend>=0 && r.CapitalSpend>=0 && (long)r.MaintenanceSpend+r.CapitalSpend+r.OperatingCost<=int.MaxValue &&
                (continuous || r.MaintenanceSpend==0 && r.CapitalSpend==0),"Invalid report equipment spending total.");
            Require(s.LastReportDay==0 || reports.Any(r=>r.Day==s.LastReportDay),"Missing last report.");
            var maintenance=Array(s.Maintenance,2);Unique(maintenance.Select(m=>m.Day));foreach(var m in maintenance){EnumValue(m.Choice);Require(m.Day>=1 && m.Day<=2 && m.Day<=s.LastMaintenanceDay && m.ActorId>=0 && m.Cost>=0,"Invalid maintenance.");Range(m.ConditionBefore,0,100);Range(m.ConditionAfter,0,100);}
            var overrides=Array(s.NoiseOverrides,6);Unique(overrides.Select(n=>n.RoomId));foreach(var n in overrides){Require(Room(n.RoomId),"Invalid noise override.");Unit(n.Value);}
        }
        internal static void Plan(PlanningSnapshot s,IEnumerable<int> rooms,EconomySettings economy)
        {
            Require(s!=null,"Missing plan.");var offers=Array(s.Applications,8);foreach(var b in offers)Booking(b);Unique(offers.Select(b=>b.Id));
            var assignments=Array(s.Assignments,6);Unique(assignments.Select(a=>a.RoomId));Unique(assignments.Select(a=>a.BookingId));
            foreach(var a in assignments)Require(a.ActorId>=0 && rooms.Contains(a.RoomId) && offers.Any(b=>b.Id==a.BookingId) && a.Price>=economy.MinPrice && a.Price<=economy.MaxPrice && (a.Price-economy.MinPrice)%economy.PriceStep==0,"Invalid plan assignment.");
        }
    }
}



