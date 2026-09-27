using System;

namespace WorstHotel
{
    public sealed partial class BoilerSystem
    {
        public bool EmergencyPatchActive { get; private set; }
        /// <summary>Absolute hotel time, or zero when no paid maintenance is in progress.</summary>
        public float MaintenanceEndsAt { get; private set; }
        public bool MaintenanceInProgress => MaintenanceEndsAt > 0;
        public BoilerServiceKind ActiveServiceKind { get; private set; }
        public int MaintenanceRevision { get; private set; }

        public float MaintenanceDurationHours(BoilerServiceKind kind)
        {
            if (kind == BoilerServiceKind.Basic) return settings.Capacity.BasicMaintenanceHours;
            if (kind == BoilerServiceKind.Full) return settings.Capacity.MaintenanceHours;
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        public float MaintenanceRemaining(float now)
        {
            if (!Number.IsFinite(now) || now < 0) throw new ArgumentOutOfRangeException(nameof(now));
            return Math.Max(0, MaintenanceEndsAt - now);
        }

        internal CommandResult CanApplyEmergencyPatch(int actorId)
        {
            if (!CapacityModelEnabled) return CommandResult.Fail("Emergency patching requires continuous hotel operations.");
            var allowed = CanRestart(actorId);
            if (!allowed.Success) return allowed;
            return MaintenanceRevision == int.MaxValue ? CommandResult.Fail("The boiler cannot record another maintenance change.") : CommandResult.Ok();
        }

        internal CommandResult ApplyEmergencyPatch(int actorId)
        {
            var allowed = CanApplyEmergencyPatch(actorId);
            if (!allowed.Success) return allowed;
            CommitMaintenanceOutcome(settings.Capacity.EmergencyPatchCondition, settings.Capacity.EmergencyPatchStress,
                true, settings.RestartPressure);
            return CommandResult.Ok("Emergency patch fitted. Heating is working, but the worn boiler remains vulnerable to overload.");
        }

        internal CommandResult CanBeginMaintenance(float now) => CanBeginMaintenance(now, BoilerServiceKind.Full);

        internal CommandResult CanBeginMaintenance(float now, BoilerServiceKind kind)
        {
            if (ReadOnlyMirror) return CommandResult.Fail(HotelSimulation.MirrorMessage);
            if (!CapacityModelEnabled) return CommandResult.Fail("Timed maintenance requires continuous hotel operations.");
            if (!Number.IsFinite(now) || now < 0) return CommandResult.Fail("Maintenance requires a valid hotel time.");
            if (kind != BoilerServiceKind.Basic && kind != BoilerServiceKind.Full) return CommandResult.Fail("Choose Basic or Full boiler service.");
            if (MaintenanceInProgress) return CommandResult.Fail("The boiler is already offline for maintenance.");
            // Reserve both the start and the completion revision before payment.
            if (MaintenanceRevision > int.MaxValue - 2) return CommandResult.Fail("The boiler cannot record another complete maintenance job.");
            if (kind == BoilerServiceKind.Basic)
            {
                if (Failed) return CommandResult.Fail("Basic service requires a working boiler. Repair the failure or choose Full service.");
                if (BasicConditionOutcome == Condition && BasicStressOutcome == Stress01)
                    return CommandResult.Fail("Basic service would not improve this boiler yet.");
            }
            double deadline = now + (double)operatingSecondsPerDay.Value * MaintenanceDurationHours(kind) / 24;
            if (deadline > float.MaxValue || (float)deadline <= now)
                return CommandResult.Fail("The hotel clock cannot represent this maintenance deadline.");
            return CommandResult.Ok();
        }

        internal CommandResult BeginMaintenance(float now) => BeginMaintenance(now, BoilerServiceKind.Full);

        internal CommandResult BeginMaintenance(float now, BoilerServiceKind kind)
        {
            var allowed = CanBeginMaintenance(now, kind);
            if (!allowed.Success) return allowed;
            ActiveServiceKind = kind;
            MaintenanceEndsAt = (float)(now + (double)operatingSecondsPerDay.Value * MaintenanceDurationHours(kind) / 24);
            MaintenanceRevision++;
            ReliefActorId = -1;
            CancelSoloLatch();
            // A failed boiler is still failed until the job actually finishes. No recovery event at purchase time.
            UpdateOutput();
            return CommandResult.Ok("Boiler maintenance started. Central heating is offline until the work is complete.");
        }

        internal bool CompleteMaintenance(float now)
        {
            if (ReadOnlyMirror) return false;
            if (!Number.IsFinite(now) || now < 0) throw new ArgumentOutOfRangeException(nameof(now));
            if (!MaintenanceInProgress || now < MaintenanceEndsAt) return false;
            if (ActiveServiceKind == BoilerServiceKind.Basic)
                CommitMaintenanceOutcome(BasicConditionOutcome, BasicStressOutcome, EmergencyPatchActive, settings.StartPressure);
            else
                CommitMaintenanceOutcome(Math.Max(Condition, settings.Capacity.ProperMaintenanceCondition), 0, false, settings.StartPressure);
            return true;
        }

        private float BasicConditionOutcome => Math.Max(Condition,
            Math.Min(settings.Capacity.BasicMaintenanceConditionCap, Condition + settings.Capacity.BasicMaintenanceConditionGain));
        private float BasicStressOutcome => Math.Max(0, Stress01 - settings.Capacity.BasicMaintenanceStressReduction);

        private void CommitMaintenanceOutcome(float condition, float stress, bool patched, float pressure)
        {
            bool wasFailed = Failed;
            float previousCondition = Condition, previousPressure = Pressure, previousOutput = HeatingOutput;
            // Commit every observable field before callbacks: cash/accounting were already committed by the simulation.
            Condition = condition; Stress01 = stress; EmergencyPatchActive = patched; MaintenanceEndsAt = 0;
            ActiveServiceKind = BoilerServiceKind.None; MaintenanceRevision++;
            Failed = false; FailureExposure = 0; ReliefActorId = -1;
            CancelSoloLatch();
            Pressure = Number.Clamp(pressure, 0, settings.MaxPressure);
            HeatingOutput = CapacityHeatOutput;
            if (previousCondition != Condition) OnConditionChanged?.Invoke(Condition);
            if (previousPressure != Pressure) OnPressureChanged?.Invoke(Pressure);
            if (previousOutput != HeatingOutput) OnHeatingOutputChanged?.Invoke(HeatingOutput);
            if (wasFailed) OnFailureResolved?.Invoke();
        }
    }
}
