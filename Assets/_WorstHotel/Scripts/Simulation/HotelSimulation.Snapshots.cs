using System;
using System.Collections.Generic;
using System.Linq;

namespace WorstHotel
{
    public sealed partial class HotelSimulation
    {
        public bool IsReadOnlyMirror { get; private set; }
        public long AppliedSnapshotEpoch { get; private set; } = -1;
        public long AppliedSnapshotSequence { get; private set; } = -1;
        internal const string MirrorMessage = "This hotel is a read-only host replica. Send the intention to the host.";

        public void EnableReadOnlyMirror()
        {
            IsReadOnlyMirror=true;Clock.ReadOnlyMirror=true;Boiler.ReadOnlyMirror=true;Economy.ReadOnlyMirror=true;
            Heaters.ReadOnlyMirror=true;Keys.ReadOnlyMirror=true;Incidents.ReadOnlyMirror=true;Requests.ReadOnlyMirror=true;
            if(Schedules!=null)Schedules.ReadOnlyMirror=true;if(Housekeeping!=null)Housekeeping.ReadOnlyMirror=true;
            if(Electrical!=null)Electrical.ReadOnlyMirror=true;if(Noise!=null)Noise.ReadOnlyMirror=true;
            foreach(var guest in guests)guest.IsReplica=true;
        }
        public HotelModelSnapshot CaptureSnapshot(long epoch,long sequence)
        {
            if(epoch<0 || sequence<0)throw new ArgumentOutOfRangeException("Snapshot metadata must be nonnegative.");
            return new HotelModelSnapshot
            {
                Epoch=epoch,Sequence=sequence,Day=dayNumber,LastMaintenanceDay=lastMaintenanceDay,DebugGuestCounter=debugGuestCounter,
                Running=Running,Time=Elapsed,Speed=Clock.Speed,EventRevision=EventRevision,LastEvent=LastEvent,
                BoilerFailureAcknowledged=BoilerFailureAcknowledged,Cash=Economy.Cash,Reputation=Economy.Reputation,
                LastReportDay=LastReport?.DayNumber??0,LastRefillDay=Housekeeping?.LastRefillDay??0,
                Rooms=rooms.Values.OrderBy(r=>r.Profile.Id).Select(SnapshotData.Capture).ToArray(),Guests=guests.Select(SnapshotData.Capture).ToArray(),
                Boiler=Boiler.CaptureSnapshot(),Circuits=Electrical?.CaptureSnapshot()??Array.Empty<CircuitSnapshot>(),Consumers=Electrical?.CaptureConsumers()??Array.Empty<ConsumerSnapshot>(),
                Heaters=Heaters.CaptureSnapshot(),Keys=Keys.CaptureSnapshot(),Linens=Housekeeping?.CaptureLinens()??Array.Empty<LinenSnapshot>(),
                Turnover=Housekeeping?.CaptureTasks()??Array.Empty<TurnoverSnapshot>(),Incidents=Incidents.CaptureSnapshot(),Requests=Requests.CaptureSnapshot(),
                NoiseOverrides=Noise?.CaptureSnapshot()??Array.Empty<NoiseOverrideSnapshot>(),
                NoiseSources=Noise?.Sources.Select(SnapshotData.Capture).ToArray()??Array.Empty<NoiseSourceSnapshot>(),
                SituationTime=Incidents.SituationTime,Reports=reports.Select(SnapshotData.Capture).ToArray(),
                HasServices=Services!=null,ServiceLayer=Services?.CaptureSnapshot(),
                Maintenance=maintenance.Select(m=>new MaintenanceSnapshot{Day=m.DayNumber,ActorId=m.ActorId,Choice=m.Choice,Cost=m.Cost,ConditionBefore=m.ConditionBefore,ConditionAfter=m.ConditionAfter,CashAfter=m.CashAfter}).ToArray()
            };
        }
        public CommandResult ApplySnapshot(HotelModelSnapshot snapshot)
        {
            if(!IsReadOnlyMirror)return CommandResult.Fail("Only a read-only replica can apply host snapshots.");
            if(snapshot==null)return CommandResult.Fail("Missing hotel snapshot.");
            if(snapshot.Epoch<AppliedSnapshotEpoch || (snapshot.Epoch==AppliedSnapshotEpoch && snapshot.Sequence<=AppliedSnapshotSequence))
                return CommandResult.Fail("Stale hotel snapshot.");
            GuestStay[] incomingGuests;DayReport[] incomingReports;MaintenanceDecision[] incomingMaintenance;
            try
            {
                SnapshotValidation.Model(snapshot,rooms.Keys.ToArray(),LivingEnabled,Housekeeping?.Linens.Count??0,Electrical?.Circuits.Select(c=>c.Id)??Enumerable.Empty<string>());
                SnapshotValidation.Services(snapshot, Services != null, rooms.Keys.ToArray(), Services?.NaturalCommunicationEnabled == true);
                Housekeeping?.ValidateSnapshot(snapshot.Linens);
                foreach(var room in snapshot.Rooms)
                    SnapshotValidation.Require(SnapshotData.OptionalId(room.CircuitId)==Electrical?.CircuitForRoom(room.Id)?.Id,"Room circuit differs from this hotel.");
                incomingGuests=snapshot.Guests.Select(SnapshotData.Guest).ToArray();
                incomingReports=snapshot.Reports.Select(SnapshotData.Report).ToArray();
                incomingMaintenance=snapshot.Maintenance.Select(m=>new MaintenanceDecision(m.Day,m.ActorId,m.Choice,m.Cost,m.ConditionBefore,m.ConditionAfter,m.CashAfter)).ToArray();
            }
            catch(ArgumentException error){return CommandResult.Fail("Rejected hotel snapshot: "+error.Message);}
            // No gameplay callback is emitted while installing a packet. Observers refresh once at the GameSession boundary.
            bool sameRoster=snapshot.Epoch==AppliedSnapshotEpoch && snapshot.Day==dayNumber;
            var old=guests.ToDictionary(g=>g.GuestId);guests.Clear();
            for(int index=0;index<incomingGuests.Length;index++)
            {
                var candidate=incomingGuests[index];
                if(sameRoster && old.TryGetValue(candidate.GuestId,out var previous) && SnapshotData.SameIdentity(previous,candidate))
                {SnapshotData.Restore(previous,snapshot.Guests[index]);candidate=previous;}
                candidate.IsReplica=true;guests.Add(candidate);
            }
            foreach(var r in snapshot.Rooms)SnapshotData.Restore(rooms[r.Id],r);
            Boiler.RestoreSnapshot(snapshot.Boiler);Clock.RestoreSnapshot(snapshot.Time,snapshot.Speed);
            Heaters.RestoreSnapshot(snapshot.Heaters);Electrical?.RestoreSnapshot(snapshot.Circuits,snapshot.Consumers);Keys.RestoreSnapshot(snapshot.Keys);
            Housekeeping?.RestoreSnapshot(snapshot.Linens,snapshot.Turnover,snapshot.LastRefillDay,snapshot.Epoch==AppliedSnapshotEpoch);
            Noise?.RestoreSnapshot(snapshot.NoiseOverrides);Schedules?.RestoreSnapshot(guests);
            Noise?.RestoreMeasuredSources(snapshot.NoiseSources);
            Incidents.RestoreSnapshot(snapshot.Incidents,guests.ToDictionary(g=>g.GuestId),sameRoster);Requests.RestoreSnapshot(snapshot.Requests,sameRoster);
            Incidents.SituationTime=snapshot.SituationTime;
            Services?.RestoreSnapshot(snapshot.ServiceLayer);
            reports.Clear();reports.AddRange(incomingReports);maintenance.Clear();maintenance.AddRange(incomingMaintenance);
            Economy.RestoreSnapshot(snapshot.Cash,snapshot.Reputation,reports);LastReport=reports.FirstOrDefault(r=>r.DayNumber==snapshot.LastReportDay);
            dayNumber=snapshot.Day;lastMaintenanceDay=snapshot.LastMaintenanceDay;debugGuestCounter=snapshot.DebugGuestCounter;Running=snapshot.Running;
            EventRevision=snapshot.EventRevision;LastEvent=snapshot.LastEvent;BoilerFailureAcknowledged=snapshot.BoilerFailureAcknowledged;
            AppliedSnapshotEpoch=snapshot.Epoch;AppliedSnapshotSequence=snapshot.Sequence;
            return CommandResult.Ok("Host hotel state applied.");
        }
    }
}
