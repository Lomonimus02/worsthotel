using System;

namespace WorstHotel
{
    public sealed partial class BoilerSystem
    {
        public float Condition { get; private set; }
        public float Load { get; private set; }
        public float HeatingOutput { get; private set; } = 1;
        public float Pressure { get; private set; }
        public bool Failed { get; private set; }
        public int ReliefActorId { get; private set; } = -1;
        public bool InRepairBand => Pressure >= settings.RepairSafeMin && Pressure <= settings.RepairSafeMax;
        public float FailureExposure { get; private set; }
        public float? LoadOverride { get; private set; }
        public float Overload => CapacityModelEnabled ? Math.Max(0, LoadRatio - 1) : Math.Max(0, Load / settings.SafeLoad - 1);
        public event Action<float> OnConditionChanged;
        public event Action<float> OnLoadChanged;
        public event Action<float> OnHeatingOutputChanged;
        public event Action<float> OnPressureChanged;
        public event Action OnFailureStarted;
        public event Action OnFailureResolved;
        private readonly BoilerSettings settings;
        private float occupancyLoad;

        public BoilerSystem(BoilerSettings settings, float? operatingSecondsPerDay = null)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            if (operatingSecondsPerDay.HasValue && (!Number.IsFinite(operatingSecondsPerDay.Value) || operatingSecondsPerDay.Value <= 0))
                throw new ArgumentOutOfRangeException(nameof(operatingSecondsPerDay));
            this.operatingSecondsPerDay = operatingSecondsPerDay;
            Condition = settings.InitialCondition; Pressure = settings.StartPressure;
            UpdateOutput();
        }

        public void BeginService()
        {
            if (ReadOnlyMirror) return;
            ReliefActorId = -1;
            CancelSoloLatch();
            if (!Failed && !CapacityModelEnabled) { SetPressure(settings.StartPressure); FailureExposure = 0; }
            UpdateOutput();
        }

        public void SetLoad(float occupancyDemand)
        {
            if (ReadOnlyMirror) return;
            if (!Number.IsFinite(occupancyDemand) || occupancyDemand < 0) throw new ArgumentOutOfRangeException(nameof(occupancyDemand));
            occupancyLoad = occupancyDemand;
            float next = LoadOverride ?? occupancyLoad;
            if (Load != next) { Load = next; OnLoadChanged?.Invoke(Load); }
            UpdateOutput();
        }

        public void OverrideLoad(float? demand)
        {
            if (ReadOnlyMirror) return;
            if (demand.HasValue && (!Number.IsFinite(demand.Value) || demand.Value < 0)) throw new ArgumentOutOfRangeException(nameof(demand));
            LoadOverride = demand;
            SetLoad(occupancyLoad);
        }

        public void SetCondition(float condition)
        {
            if (ReadOnlyMirror) return;
            if (!Number.IsFinite(condition)) throw new ArgumentOutOfRangeException(nameof(condition));
            float next = Number.Clamp(condition, 0, 100);
            if (Condition != next) { Condition = next; OnConditionChanged?.Invoke(Condition); }
            UpdateOutput();
        }

        public void Tick(float dt)
        {
            if (ReadOnlyMirror) return;
            if (!Number.IsFinite(dt) || dt < 0) throw new ArgumentOutOfRangeException(nameof(dt));
            if (dt == 0) return;
            if (CapacityModelEnabled) { TickCapacity(dt); return; }
            SetCondition(Condition - (settings.BaseWearPerMinute + settings.OverloadWearPerMinute * Overload) * dt / 60);
            if (Failed)
            {
                if (ReliefActorId >= 0 || SoloValveLatched)
                {
                    float difference = settings.ReliefTarget - Pressure;
                    SetPressure(Pressure + Math.Sign(difference) * Math.Min(Math.Abs(difference), settings.ReliefRate * dt));
                }
                else SetPressure(Pressure + settings.FailedPressureRise * dt);
                return;
            }
            float target = settings.PressureBase + settings.PressureOverloadFactor * Overload
                + settings.PressureConditionFactor * Math.Max(0, settings.PressureConditionThreshold - Condition);
            SetPressure(Pressure + (target - Pressure) * (1 - (float)Math.Exp(-dt / settings.PressureTimeConstant)));
            FailureExposure = Pressure >= settings.FailurePressure ? FailureExposure + dt : 0;
            if (FailureExposure >= settings.FailureExposureSeconds) ForceFailure();
        }

        public void ForceFailure()
        {
            if (ReadOnlyMirror) return;
            if (MaintenanceInProgress) return;
            if (Failed) return;
            Failed = true; ReliefActorId = -1;
            CancelSoloLatch();
            SetPressure(Math.Max(Pressure, settings.FailurePressure));
            UpdateOutput();
            OnFailureStarted?.Invoke();
        }

        public CommandResult SetRelief(int actorId, bool held)
        {
            if (ReadOnlyMirror) return CommandResult.Fail(HotelSimulation.MirrorMessage);
            if (actorId < 0 || actorId > 1) return CommandResult.Fail("Unknown staff actor.");
            if (!held)
            {
                if (ReliefActorId == actorId) { ReliefActorId = -1; ReleaseSoloValveCharge(); }
                return CommandResult.Ok("Relief valve released.");
            }
            if (MaintenanceInProgress) return CommandResult.Fail("The boiler is offline for maintenance.");
            if (!Failed) return CommandResult.Fail("The boiler is running; emergency relief is not required.");
            if (ReliefActorId >= 0 && ReliefActorId != actorId) return CommandResult.Fail("The other staff member is operating this valve.");
            ReliefActorId = actorId;
            return CommandResult.Ok("Holding pressure relief. Keep watching the gauge.");
        }

        public CommandResult CanRestart(int operatorActorId)
        {
            if (ReadOnlyMirror) return CommandResult.Fail(HotelSimulation.MirrorMessage);
            if (operatorActorId < 0 || operatorActorId > 1) return CommandResult.Fail("Unknown staff actor.");
            if (MaintenanceInProgress) return CommandResult.Fail("The boiler is offline for maintenance.");
            if (!Failed) return CommandResult.Fail("The boiler is already running.");
            if (!(SoloValveLatched && operatorActorId == 0) && (ReliefActorId < 0 || ReliefActorId == operatorActorId))
                return CommandResult.Fail("A different staff member must hold the relief valve, or the solo catch must be secured.");
            if (!InRepairBand) return CommandResult.Fail("Pressure must stay inside the marked repair band.");
            return CommandResult.Ok();
        }

        public CommandResult Restart(int operatorActorId)
        {
            var allowed = CanRestart(operatorActorId);
            if (!allowed.Success) return allowed;
            Failed = false; FailureExposure = 0; ReliefActorId = -1;
            Stress01 = 0;
            CancelSoloLatch();
            SetPressure(settings.RestartPressure);
            UpdateOutput();
            OnFailureResolved?.Invoke();
            return CommandResult.Ok("Heating restarted. Mechanical wear remains until maintenance.");
        }

        public void ApplyPaidMaintenance(float restoredCondition)
        {
            if (ReadOnlyMirror) return;
            if (!Number.IsFinite(restoredCondition)) throw new ArgumentOutOfRangeException(nameof(restoredCondition));
            if (MaintenanceInProgress) return;
            bool wasFailed = Failed;
            EmergencyPatchActive = false;
            Failed = false; FailureExposure = 0; ReliefActorId = -1;
            Stress01 = 0;
            CancelSoloLatch();
            SetCondition(restoredCondition);
            SetPressure(settings.StartPressure);
            UpdateOutput();
            if (wasFailed) OnFailureResolved?.Invoke();
        }

        private void SetPressure(float pressure)
        {
            float next = Number.Clamp(pressure, 0, settings.MaxPressure);
            if (Pressure != next) { Pressure = next; OnPressureChanged?.Invoke(Pressure); }
        }

        private void UpdateOutput()
        {
            float next = MaintenanceInProgress ? 0 : Failed ? settings.FailedHeatOutput : CapacityModelEnabled ? CapacityHeatOutput : Number.Clamp(1 - settings.HeatOverloadLoss * Overload
                - settings.HeatConditionLoss * Math.Max(0, settings.HeatConditionThreshold - Condition), settings.MinimumHeatOutput, 1);
            if (HeatingOutput != next) { HeatingOutput = next; OnHeatingOutputChanged?.Invoke(HeatingOutput); }
        }
    }
}


