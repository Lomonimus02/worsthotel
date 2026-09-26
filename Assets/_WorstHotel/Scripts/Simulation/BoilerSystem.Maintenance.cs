using System;

namespace WorstHotel
{
    public sealed partial class BoilerSystem
    {
        public bool EmergencyPatchActive { get; private set; }
        /// <summary>Absolute hotel time, or zero when no paid maintenance is in progress.</summary>
        public float MaintenanceEndsAt { get; private set; }
        public bool MaintenanceInProgress => MaintenanceEndsAt > 0;

        public float MaintenanceRemaining(float now)
        {
            if (!Number.IsFinite(now) || now < 0) throw new ArgumentOutOfRangeException(nameof(now));
            return Math.Max(0, MaintenanceEndsAt - now);
        }

        internal CommandResult ApplyEmergencyPatch(int actorId)
        {
            if (!CapacityModelEnabled) return CommandResult.Fail("Emergency patching requires continuous hotel operations.");
            var allowed = CanRestart(actorId);
            if (!allowed.Success) return allowed;
            CommitMaintenanceOutcome(settings.Capacity.EmergencyPatchCondition, settings.Capacity.EmergencyPatchStress,
                true, settings.RestartPressure);
            return CommandResult.Ok("Emergency patch fitted. Heating is working, but the worn boiler remains vulnerable to overload.");
        }

        internal CommandResult CanBeginMaintenance(float now)
        {
            if (ReadOnlyMirror) return CommandResult.Fail(HotelSimulation.MirrorMessage);
            if (!CapacityModelEnabled) return CommandResult.Fail("Timed maintenance requires continuous hotel operations.");
            if (!Number.IsFinite(now) || now < 0) return CommandResult.Fail("Maintenance requires a valid hotel time.");
            if (MaintenanceInProgress) return CommandResult.Fail("The boiler is already offline for maintenance.");
            double deadline = now + (double)operatingSecondsPerDay.Value * settings.Capacity.MaintenanceHours / 24;
            if (deadline > float.MaxValue || (float)deadline <= now)
                return CommandResult.Fail("The hotel clock cannot represent this maintenance deadline.");
            return CommandResult.Ok();
        }

        internal CommandResult BeginMaintenance(float now)
        {
            var allowed = CanBeginMaintenance(now);
            if (!allowed.Success) return allowed;
            MaintenanceEndsAt = (float)(now + (double)operatingSecondsPerDay.Value * settings.Capacity.MaintenanceHours / 24);
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
            CommitMaintenanceOutcome(Math.Max(Condition, settings.Capacity.ProperMaintenanceCondition), 0,
                false, settings.StartPressure);
            return true;
        }

        private void CommitMaintenanceOutcome(float condition, float stress, bool patched, float pressure)
        {
            bool wasFailed = Failed;
            float previousCondition = Condition, previousPressure = Pressure, previousOutput = HeatingOutput;
            // Commit every observable field before callbacks: cash/accounting were already committed by the simulation.
            Condition = condition; Stress01 = stress; EmergencyPatchActive = patched; MaintenanceEndsAt = 0;
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
