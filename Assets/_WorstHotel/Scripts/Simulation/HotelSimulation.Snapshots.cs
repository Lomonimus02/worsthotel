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
                UpgradedCircuitIds=Electrical?.Circuits.Where(c=>Electrical.IsCapacityUpgraded(c.Id)).Select(c=>c.Id).ToArray() ?? Array.Empty<string>(),
                LastReportDay=LastReport?.DayNumber??0,LastRefillDay=Housekeeping?.LastRefillDay??0,
                Rooms=rooms.Values.OrderBy(r=>r.Profile.Id).Select(SnapshotData.Capture).ToArray(),Guests=guests.Select(SnapshotData.Capture).ToArray(),
                Boiler=Boiler.CaptureSnapshot(),Circuits=Electrical?.CaptureSnapshot()??Array.Empty<CircuitSnapshot>(),Consumers=Electrical?.CaptureConsumers()??Array.Empty<ConsumerSnapshot>(),
                Heaters=Heaters.CaptureSnapshot(),Keys=Keys.CaptureSnapshot(),Linens=Housekeeping?.CaptureLinens()??Array.Empty<LinenSnapshot>(),
                Turnover=Housekeeping?.CaptureTasks()??Array.Empty<TurnoverSnapshot>(),Incidents=Incidents.CaptureSnapshot(),Requests=Requests.CaptureSnapshot(),
                NoiseOverrides=Noise?.CaptureSnapshot()??Array.Empty<NoiseOverrideSnapshot>(),
                NoiseSources=Noise?.Sources.Select(SnapshotData.Capture).ToArray()??Array.Empty<NoiseSourceSnapshot>(),
                SituationTime=Incidents.SituationTime,Reports=reports.Select(SnapshotData.Capture).ToArray(),
                HasServices=Services!=null,ServiceLayer=Services?.CaptureSnapshot(),
                HasOperations=ContinuousOperations,Operations=CaptureOperations(),
                HasDirector=Director != null,Director=Director?.Capture(),
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
            ContractPayment incomingLastPayment, incomingPeriodPayment;
            DayReport incomingLossReport;
            try
            {
                SnapshotValidation.Model(snapshot,rooms.Keys.ToArray(),LivingEnabled,Housekeeping?.Linens.Count??0,Electrical?.Circuits.Select(c=>c.Id)??Enumerable.Empty<string>());
                SnapshotValidation.OperationsModel(snapshot, Operations, settings.Economy, rooms.Keys.ToArray(), Schedules);
                ValidateSupplies(snapshot);
                SnapshotValidation.Require(snapshot.HasDirector == (Director != null), "Director mode differs from this hotel.");
                // Unity JSON materializes an empty optional class even when capture supplied
                // null. The presence flag selects the mode, as with services and operations.
                if (snapshot.HasDirector) Director.Validate(snapshot.Director, snapshot);
                ValidateContractContinuity(snapshot);
                SnapshotValidation.SalesModel(snapshot, Operations?.Sales, settings.Economy, rooms.Keys.ToArray(), SalesDecisionAt);
                if (snapshot.Boiler.CapacityUpgradePurchased)
                {
                    double capacity = (double)settings.Boiler.SafeLoad * settings.Boiler.Capacity.CapacityUpgradeMultiplier;
                    SnapshotValidation.Require(capacity <= float.MaxValue && (float)capacity > settings.Boiler.SafeLoad,
                        "Invalid upgraded boiler capacity.");
                }
                if (snapshot.UpgradedCircuitIds.Length > 0)
                {
                    double capacity = (double)ElectricitySettings.CircuitCapacity + ElectricitySettings.CapacityUpgradeAmount;
                    SnapshotValidation.Require(capacity <= float.MaxValue && (float)capacity > ElectricitySettings.CircuitCapacity,
                        "Invalid upgraded branch capacity.");
                }
                if (ContinuousOperations && snapshot.Boiler.MaintenanceEndsAt > 0)
                    SnapshotValidation.Require(snapshot.Boiler.MaintenanceEndsAt <= (float)Math.Min(float.MaxValue,
                        (double)snapshot.Time + (double)Operations.SecondsPerDay *
                        (snapshot.Boiler.ActiveServiceKind == BoilerServiceKind.Basic ? settings.Boiler.Capacity.BasicMaintenanceHours : settings.Boiler.Capacity.MaintenanceHours) / 24),
                        "Maintenance deadline exceeds its configured duration.");
                SnapshotValidation.Services(snapshot, Services != null, rooms.Keys.ToArray(), Services?.NaturalCommunicationEnabled == true);
                SnapshotValidation.EarlyDepartures(snapshot, NeedsSettings?.EarlyCheckout, Operations, settings.Economy, rooms.Keys.ToArray());
                Housekeeping?.ValidateSnapshot(snapshot.Linens);
                foreach(var room in snapshot.Rooms)
                    SnapshotValidation.Require(SnapshotData.OptionalId(room.CircuitId)==Electrical?.CircuitForRoom(room.Id)?.Id,"Room circuit differs from this hotel.");
                incomingGuests=snapshot.Guests.Select(SnapshotData.Guest).ToArray();
                incomingReports=snapshot.Reports.Select(SnapshotData.Report).ToArray();
                incomingMaintenance=snapshot.Maintenance.Select(m=>new MaintenanceDecision(m.Day,m.ActorId,m.Choice,m.Cost,m.ConditionBefore,m.ConditionAfter,m.CashAfter)).ToArray();
                incomingLastPayment=SnapshotData.Payment(snapshot.Operations?.LastContractPayment);
                incomingPeriodPayment=SnapshotData.Payment(snapshot.Operations?.PeriodContractPayment);
                incomingLossReport=SnapshotData.Report(snapshot.Operations?.OwnershipLossReport);
            }
            catch(ArgumentException error){return CommandResult.Fail("Rejected hotel snapshot: "+error.Message);}
            // No gameplay callback is emitted while installing a packet. Observers refresh once at the GameSession boundary.
            bool sameRoster=snapshot.Epoch==AppliedSnapshotEpoch && (ContinuousOperations || snapshot.Day==dayNumber);
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
            Heaters.RestoreSnapshot(snapshot.Heaters);Electrical?.RestoreSnapshot(snapshot.Circuits,snapshot.Consumers,snapshot.UpgradedCircuitIds);Keys.RestoreSnapshot(snapshot.Keys);
            Housekeeping?.RestoreSnapshot(snapshot.Linens,snapshot.Turnover,snapshot.LastRefillDay,snapshot.Epoch==AppliedSnapshotEpoch);
            Noise?.RestoreSnapshot(snapshot.NoiseOverrides);Schedules?.RestoreSnapshot(guests);
            Noise?.RestoreMeasuredSources(snapshot.NoiseSources);
            Incidents.RestoreSnapshot(snapshot.Incidents,guests.ToDictionary(g=>g.GuestId),sameRoster);Requests.RestoreSnapshot(snapshot.Requests,sameRoster);
            Incidents.SituationTime=snapshot.SituationTime;
            Services?.RestoreSnapshot(snapshot.ServiceLayer);
            reports.Clear();reports.AddRange(incomingReports);maintenance.Clear();maintenance.AddRange(incomingMaintenance);
            Economy.RestoreSnapshot(snapshot.Cash,snapshot.Reputation,reports);LastReport=reports.FirstOrDefault(r=>r.DayNumber==snapshot.LastReportDay);
            RestoreOperations(snapshot.HasOperations?snapshot.Operations:null, incomingLastPayment, incomingPeriodPayment, incomingLossReport);
            Director?.Restore(snapshot.Director);
            dayNumber=snapshot.Day;lastMaintenanceDay=snapshot.LastMaintenanceDay;debugGuestCounter=snapshot.DebugGuestCounter;Running=snapshot.Running;
            EventRevision=snapshot.EventRevision;LastEvent=snapshot.LastEvent;BoilerFailureAcknowledged=snapshot.BoilerFailureAcknowledged;
            AppliedSnapshotEpoch=snapshot.Epoch;AppliedSnapshotSequence=snapshot.Sequence;
            return CommandResult.Ok("Host hotel state applied.");
        }

        void ValidateContractContinuity(HotelModelSnapshot snapshot)
        {
            if (snapshot.Epoch != AppliedSnapshotEpoch || !ContinuousOperations) return;
            var data = snapshot.Operations;
            SnapshotValidation.Require(snapshot.Time >= Elapsed && data.ReportSequence >= ReportSequence &&
                data.OperatingCostSequence >= OperatingCostSequence,
                "Financial clocks cannot move backwards within a host epoch.");
            foreach (var report in reports)
            {
                var incoming = snapshot.Reports.FirstOrDefault(value => value.Day == report.DayNumber);
                if (incoming != null)
                    SnapshotValidation.Require(SnapshotValidation.SameFinancialReport(SnapshotData.Capture(report), incoming),
                        "A published financial report cannot be rewritten.");
            }
            if (!ContractEnabled) return;
            SnapshotValidation.Require(data.ContractSequence >= ContractSequence && data.ContractBaseRooms == ContractBaseRooms &&
                data.ContractAssessedRooms >= ContractAssessedRooms, "Contract state cannot move backwards within a host epoch.");
            SnapshotValidation.Require(!Running || snapshot.Running || data.OwnershipLost,
                "An open contract hotel can stop only by losing ownership.");
            SnapshotValidation.Require(data.ContractSequence != ContractSequence || data.ContractAssessedRooms == ContractAssessedRooms,
                "A contract room assessment is locked until its payment is attempted.");
            var incomingPayments = snapshot.Reports.Select(value => value.ContractPayment)
                .Concat(new[] { data.LastContractPayment, data.PeriodContractPayment, data.OwnershipLossReport?.ContractPayment })
                .Where(value => value != null).ToArray();
            foreach (var payment in incomingPayments.Where(value => value.Period == ContractSequence + 1))
                SnapshotValidation.Require(payment.AssessedRooms == ContractAssessedRooms,
                    "Settlement must use the room assessment already locked for that payment.");
            var knownPayments = reports.Select(value => value.ContractPayment)
                .Concat(new[] { LastContractPayment, PeriodContractPayment, OwnershipLossReport?.ContractPayment })
                .Where(value => value != null);
            foreach (var previous in knownPayments)
            foreach (var payment in incomingPayments.Where(value => value.Period == previous.Period))
                SnapshotValidation.Require(SnapshotValidation.SamePayment(SnapshotData.Capture(previous), payment),
                    "An observed contract payment cannot be rewritten when filed or retained.");
            if (OwnershipLost)
                SnapshotValidation.Require(data.OwnershipLost && !snapshot.Running && snapshot.Time == Elapsed &&
                    data.ReportSequence == ReportSequence && data.OperatingCostSequence == OperatingCostSequence &&
                    data.ContractSequence == ContractSequence && snapshot.Cash == Economy.Cash && snapshot.Reputation == Economy.Reputation &&
                    SnapshotValidation.SameFinancialReport(SnapshotData.Capture(OwnershipLossReport), data.OwnershipLossReport),
                    "Lost ownership cannot resume or settle again within the same host epoch.");
        }
    }
}
