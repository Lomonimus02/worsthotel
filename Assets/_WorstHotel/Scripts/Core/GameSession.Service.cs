using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorstHotel
{
    public enum DayPhase { Planning, Service, Settlement, Maintenance, Results }

    public sealed partial class GameSession
    {
        public DayPhase Phase { get; private set; }
        public HotelSimulation Simulation { get; private set; }
        public SessionSettings Settings { get; private set; }
        public DayReport Report { get; private set; }
        public IReadOnlyList<DayReport> Reports => reports.AsReadOnly();
        readonly List<DayReport> reports = new List<DayReport>();
        float accumulator;
        // Synchronous developer advances must remain bounded even with extreme calendar settings.
        const int MaximumDiagnosticTicks = 20000;
        float DiagnosticAdvanceHorizon => Simulation.ContinuousOperations ? Simulation.Operations.SecondsPerDay : Settings.ServiceSeconds;

        void InitializeService()
        {
            Settings = config.ToData();
            Simulation = CreateSimulationForRooms(Rooms);
            Report = null; reports.Clear(); accumulator = 0;
        }

        HotelSimulation CreateSimulationForRooms(RoomState[] roomStates) =>
            CreateSimulationForRooms(roomStates, config.OperationsData());

        HotelSimulation CreateSimulationForRooms(RoomState[] roomStates, OperationsSettings operations) =>
            new HotelSimulation(Settings, roomStates, config.living ? config.living.ToData() : new LivingHotelSettings(),
                config.needs ? config.needs.ToData() : new NeedSettings(),
                config.noise ? config.noise.ToData() : new NoiseSettings(),
                config.heater ? config.heater.ToData() : new HeaterSettings(),
                config.electricity ? config.electricity.ToData() : new ElectricitySettings(),
                config.housekeeping ? config.housekeeping.ToData() : new HousekeepingSettings(),
                config.services ? config.services.ToData() : null,
                config.infrastructure ? config.infrastructure.ToData() : null,
                operations);

        void Update()
        {
            if (IsLanReplica || LanSession.Instance && LanSession.Instance.MenuOpen) return;
            if (Simulation == null || (Phase != DayPhase.Service && Phase != DayPhase.Planning)) return;
            var coop = LocalCoopBootstrap.Instance;
            if (coop && coop.IsPaused) { if (Wait) Wait.Stop("Hotel paused"); return; }
            accumulator += Time.deltaTime * Simulation.Clock.Speed;
            float step = 1f / Settings.TickRate;
            while (accumulator >= step && (Phase == DayPhase.Service || Phase == DayPhase.Planning))
            {
                accumulator -= step;
                float speed = Simulation.Clock.Speed;
                Tick(step);
                // An event ends the accelerated batch immediately, discarding its remaining budget.
                if (speed > 1 && Simulation.Clock.Speed == 1) { accumulator = 0; break; }
            }
        }

        void Tick(float step)
        {
            PauseDiagnostics.TickEnter(step);
            int previousEvent = Simulation.EventRevision;
            if (Phase == DayPhase.Planning) Simulation.AdvancePreparation(step);
            else Simulation.Tick(step);
            if (Wait && Wait.isActiveAndEnabled) Wait.ObserveSimulationEvents();
            if (Simulation.Clock.Speed > 1 && previousEvent != Simulation.EventRevision)
                Simulation.Clock.SetSpeed(1);
            Cash = Simulation.Economy.Cash;
            if (Simulation.ContinuousOperations)
            {
                Day = Simulation.CalendarDay;
                if (!ReferenceEquals(Report, Simulation.LastReport))
                {
                    Report = Simulation.LastReport;
                    reports.Clear(); reports.AddRange(Simulation.DayReports);
                }
                RaiseChanged();
            }
            else if (Phase == DayPhase.Service && Simulation.IsServiceComplete) EndShift();
            else RaiseChanged();
            PauseDiagnostics.TickExit();
        }

        public void EndShift()
        {
            if (IsLanReplica) return;
            if (Simulation.ContinuousOperations)
            { ReportCommand(CommandResult.Fail("Continuous operations publish reports without closing the hotel.")); return; }
            if (Phase != DayPhase.Service) return;
            if (Wait) Wait.Stop("Shift complete");
            Report = Simulation.EndShift();
            reports.Add(Report);
            Cash = Simulation.Economy.Cash;
            Phase = DayPhase.Settlement;
            LastMessage = "Day closed. Refunds and costs booked once; portable heaters switched off for the night.";
            RaiseChanged();
            if (ManagementUI.Instance) { ManagementUI.Instance.Close(); ManagementUI.Instance.Open(0); }
        }

        public void ContinueAfterSettlement(int actorId)
        {
            if (ForwardLan(LanCommandKind.ContinueSettlement)) return;
            if (actorId < 0 || actorId > 1 || Phase != DayPhase.Settlement) return;
            Phase = Day >= Settings.TotalDays ? DayPhase.Results : DayPhase.Maintenance;
            LastMessage = Phase == DayPhase.Results ? "Three days survived. Every room sold had a cost." : "Choose how much of today's income to put back into the boiler.";
            ReopenLedger(actorId);
            RaiseChanged();
        }

        public void ChooseMaintenance(int actorId, MaintenanceChoice choice)
        {
            if (ForwardLan(LanCommandKind.Maintenance, amount: (int)choice)) return;
            if (Phase != DayPhase.Maintenance) return;
            var result = Simulation.ApplyMaintenance(actorId, choice);
            if (!result.Success) { ReportCommand(result); return; }
            Cash = Simulation.Economy.Cash;
            Day++;
            Report = null;
            OpenPlanning();
            LastMessage = result.Message;
            ReturnStaffToReception();
            ReopenLedger(actorId);
        }

        public void RestartSession(int actorId)
        {
            if (ForwardLan(LanCommandKind.RestartSession)) return;
            if (actorId < 0 || actorId > 1 || Phase != DayPhase.Results) return;
            NewGame();
            ReturnStaffToReception();
            ReopenLedger(actorId);
        }

        void ReturnStaffToReception()
        {
            var coop = LocalCoopBootstrap.Instance;
            if (!coop) return;
            for (int i = 0; i < coop.Players.Length; i++)
                if (coop.Players[i]) coop.Players[i].ResetToSpawn(i == 0 ? coop.spawn1 : coop.spawn2);
        }

        void ReopenLedger(int actorId)
        {
            if (LocalCoopBootstrap.Instance && LocalCoopBootstrap.Instance.RequestRemoteLedger(actorId)) return;
            if (!ManagementUI.Instance) return;
            ManagementUI.Instance.Close();
            ManagementUI.Instance.Open(actorId);
        }

        public void AdvanceTime(float seconds)
        {
            if (IsLanReplica) return;
            if (!Number.IsFinite(seconds) || seconds <= 0 || (Phase != DayPhase.Service && Phase != DayPhase.Planning)) return;
            seconds = Mathf.Min(seconds, DiagnosticAdvanceHorizon);
            float step = 1f / Settings.TickRate;
            int ticks = 0;
            while (seconds > 0 && ticks++ < MaximumDiagnosticTicks && (Phase == DayPhase.Service || Phase == DayPhase.Planning))
            { float delta = Mathf.Min(seconds, step); Tick(delta); seconds -= delta; }
            if (seconds > 0 && ticks >= MaximumDiagnosticTicks)
                ReportCommand(CommandResult.Fail("Developer advance reached its fixed-tick limit."));
        }

        public void AdvanceToNextEvent()
        {
            if (IsLanReplica) return;
            if (Phase != DayPhase.Service) return;
            if (Wait) Wait.Stop("Developer event advance");
            int revision = Simulation.EventRevision;
            float step = 1f / Settings.TickRate;
            float budget = DiagnosticAdvanceHorizon;
            int ticks = 0;
            while (Phase == DayPhase.Service && Simulation.EventRevision == revision && budget > 0 && ticks++ < MaximumDiagnosticTicks)
            {
                float delta = Mathf.Min(step, Mathf.Min(budget, Simulation.Remaining));
                if (delta <= 0) break;
                Tick(delta); budget -= delta;
            }
            accumulator = 0;
            if (Simulation.EventRevision == revision)
                ReportCommand(CommandResult.Fail("No new hotel event within the bounded developer advance."));
        }
    }
}
