#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Text;
using UnityEngine;

namespace WorstHotel
{
    public sealed partial class DeveloperPanel
    {
        string stress = "0.5", hotelHour = "6", satisfaction = "50";
        bool showHistory;
        int pendingSleepDiagnostic;
        // Repaint positions let the opt-in player capture scroll the real layout precisely.
        float thermalDebugTop, historyDebugTop;

        void DrawBoilerRhythmDebug()
        {
            var model = Session.Simulation;
            NumericRow("Boiler stress (0–1)", ref stress, "Set stress", model.DebugSetBoilerStress);
            GUILayout.BeginHorizontal();
            if (Button("Begin paid Basic service")) Apply(() => model.BeginBoilerMaintenance(0, BoilerServiceKind.Basic));
            if (Button("Begin paid Full service")) Apply(() => model.BeginBoilerMaintenance(0, BoilerServiceKind.Full));
            GUILayout.EndHorizontal();
            if (Button("Tick to maintenance completion", model.Boiler.MaintenanceInProgress))
                Apply(() => AdvanceDiagnosticTo(model.Boiler.MaintenanceEndsAt));
            GUILayout.Label("Service uses normal cash, duration and heat shutdown. Advancing time also runs the rest of the hotel.", body);
        }

        void DrawThermalDebug(RoomState room)
        {
            if (!Session.Simulation.TryGetRoomThermalBreakdown(room.Profile.Id, out var heat, out var demand)) return;
            GUILayout.Label("ACTUAL THERMAL STEP · room " + heat.RoomId, heading);
            if (Event.current.type == EventType.Repaint) thermalDebugTop = GUILayoutUtility.GetLastRect().y;
            GUILayout.Label("Now " + heat.CurrentTemperature.ToString("F2") + "°C → equilibrium " + heat.TargetTemperature.ToString("F2") + "°C" +
                "\nBase " + heat.TemperatureBase.ToString("F2") + " + central " + heat.HeatingContribution.ToString("F2") +
                " − room loss " + heat.HeatLoss.ToString("F2") + " + powered heater " + heat.SupplementalHeat.ToString("F2") +
                "\nBoiler output " + heat.BoilerOutput.ToString("P1") + " · radiator " + heat.RadiatorSetting + "/3 ×" + heat.RadiatorMultiplier.ToString("F2") +
                " · response " + heat.TimeConstant.ToString("F1") + "s" +
                "\nBoiler failed " + heat.BoilerFailed + " · maintenance " + heat.BoilerMaintenance +
                "\nLoad context: space " + demand.SpaceHeating.ToString("F3") + " + actual shower " + demand.HotWater.ToString("F3") +
                ". These are boiler demand, not extra temperature terms.", body);
        }

        CommandResult AdvanceDiagnosticTo(float target)
        {
            var model = Session.Simulation;
            if (!model.ContinuousOperations || !model.Running || !Number.IsFinite(target) || target <= model.Elapsed)
                return CommandResult.Fail("Choose a future time in a running continuous hotel.");
            for (int attempt = 0; attempt < 3 && model.Elapsed < target; attempt++)
            {
                float before = model.Elapsed;
                Session.AdvanceTime(target - before);
                if (model.Elapsed <= before) break;
            }
            if (model.Elapsed < target) return CommandResult.Fail("The bounded advance stopped before the requested time. Current hotel time: " + model.Calendar.DisplayTime);
            return CommandResult.Ok("Hotel advanced through ordinary ticks to Day " + model.Calendar.Day + " " + model.Calendar.DisplayTime +
                ". Close F2 for physical guest travel.");
        }

        void DrawRhythmClockDebug()
        {
            var model = Session.Simulation;
            if (!model.ContinuousOperations) return;
            NumericRow("Forward to hour (0–24)", ref hotelHour, "Set next occurrence", value =>
            {
                if (value < 0 || value >= 24) return CommandResult.Fail("Choose an hour from 0 inclusive to 24 exclusive.");
                float at = model.Calendar.At(model.Calendar.Day, value);
                if (at <= model.Elapsed) at = model.Calendar.At(checked(model.Calendar.Day + 1), value);
                return AdvanceDiagnosticTo(at);
            });
            if (Button("Advance to next 06:00")) Apply(() => AdvanceDiagnosticTo(WaitController.NextMorningAt(model.Calendar, model.Elapsed)));
            GUILayout.Label("SOLO sleep fixture: first look at a staff bed. These buttons close F2 and resume the hotel. Critical-wake fixture creates a real boiler fault.", body);
            GUILayout.BeginHorizontal();
            if (Button("Start sleep (SOLO diagnostic)")) QueueSleepDiagnostic(false);
            if (Button("Sleep + critical boiler wake", !model.Boiler.MaintenanceInProgress)) QueueSleepDiagnostic(true);
            GUILayout.EndHorizontal();
        }

        void QueueSleepDiagnostic(bool critical)
        {
            pendingSleepDiagnostic = critical ? 2 : 1;
            requestedClockSpeed = 1; wasPaused = false;
            Toggle();
        }

        bool RunPendingSleepDiagnostic()
        {
            if (pendingSleepDiagnostic == 0) return false;
            int command = pendingSleepDiagnostic; pendingSleepDiagnostic = 0;
            if (!Session || Session.Simulation == null) return true;
            if (command == 2 && Session.Simulation.Boiler.MaintenanceInProgress)
            { message = "Finish maintenance before forcing a diagnostic boiler wake."; Toggle(); return true; }
            var wait = Session.GetComponent<WaitController>();
            var result = wait ? wait.DebugStartSleep() : CommandResult.Fail("No hotel wait controller.");
            message = result.Message;
            if (result.Success && command == 2)
            {
                using (Session.Simulation.BeginDiagnosticInfrastructureChange()) Session.Simulation.Boiler.ForceFailure();
                wait.ObserveSimulationEvents(); Session.RefreshDebugState();
                message = "Diagnostic boiler fault: " + wait.Reason;
            }
            if (!result.Success) Toggle();
            return true;
        }

        void DrawGuestRhythmDebug(GuestStay guest)
        {
            NumericRow("Satisfaction history 0–100", ref satisfaction, "Set quality history", value => Session.Simulation.DebugSetGuestSatisfaction(guest.GuestId, value));
            GUILayout.Label("Adjusts accumulated quality only. Existing price/service penalties still limit the result; causes and complaints remain real.", body);
            if (Button("Prime severe early-checkout eligibility")) Apply(() => Session.Simulation.DebugPrimeEarlyCheckoutEligibility(guest.GuestId));
        }

        void DrawSalesRhythmDebug()
        {
            var model = Session.Simulation;
            if (!model.AutomaticBookingsEnabled) return;
            GUILayout.Label("ORDINARY ROOM SALES", heading);
            foreach (var policy in model.RoomSalesPolicies)
                if (Button("Room " + policy.RoomId + " · $" + policy.Price + " · " + (policy.OpenForSale ? "OPEN → close sales" : "CLOSED → open sales")))
                    Apply(() => model.SetRoomSalesPolicy(0, policy.RoomId, !policy.OpenForSale, policy.Price, policy.Revision));
            GUILayout.Label("Existing reservations remain. Next enquiry " + model.NextSalesDecisionAt.ToString("F1") + "s · demand override " + model.DebugNextBookingDemandPending, body);
            GUILayout.BeginHorizontal();
            if (Button("Force next due demand")) Apply(model.DebugForceNextBookingDemand);
            if (Button("Clear demand override")) Apply(model.DebugClearNextBookingDemand);
            GUILayout.EndHorizontal();
        }

        void DrawInfrastructureHistory()
        {
            var model = Session.Simulation;
            if (Button((showHistory ? "Hide" : "Show") + " infrastructure history · " + model.InfrastructureHistory.Count + "/" + HotelSimulation.InfrastructureHistoryLimit)) showHistory = !showHistory;
            if (Event.current.type == EventType.Repaint) historyDebugTop = GUILayoutUtility.GetLastRect().y;
            if (!showHistory) return;
            GUILayout.Label("Newest first. [F2] marks diagnostic changes. History survives reports and resets with a new hotel.", body);
            var text = new StringBuilder();
            for (int i = model.InfrastructureHistory.Count - 1; i >= 0; i--)
            {
                var entry = model.InfrastructureHistory[i];
                text.Append('#').Append(entry.Sequence).Append(" · ");
                if (model.ContinuousOperations)
                {
                    double hours = model.Operations.StartHour + (double)entry.At * 24 / model.Operations.SecondsPerDay;
                    int minutes = (int)Math.Floor(hours % 24 * 60 + .00001);
                    text.Append('D').Append(model.Calendar.DayAt(entry.At)).Append(' ').Append((minutes / 60).ToString("00"))
                        .Append(':').Append((minutes % 60).ToString("00"));
                }
                else text.Append(entry.At.ToString("F1")).Append('s');
                text.Append(entry.Diagnostic ? " [F2] " : " ").Append(entry.EntityId).Append(' ').Append(entry.Kind).Append(": ")
                    .Append(HistoryValue(entry.Kind, entry.PreviousValue)).Append(" → ").Append(HistoryValue(entry.Kind, entry.Value))
                    .Append("\n  boiler ").Append(entry.BoilerLoad.ToString("F2")).Append('/').Append(entry.BoilerCapacity.ToString("F2"))
                    .Append(" · stress ").Append(entry.BoilerStress.ToString("P0")).Append(" · heat ").Append(entry.BoilerOutput.ToString("P0"));
                if (entry.CircuitCapacity > 0) text.Append(" · branch ").Append(entry.CircuitLoad.ToString("F2")).Append('/').Append(entry.CircuitCapacity.ToString("F2"))
                    .Append(" stress ").Append(entry.CircuitStress.ToString("P0"));
                text.Append('\n');
            }
            GUILayout.Label(text.ToString(), body);
        }

        static string HistoryValue(InfrastructureChangeKind kind, float value)
        {
            if (kind == InfrastructureChangeKind.BoilerBand || kind == InfrastructureChangeKind.CircuitBand) return ((CapacityBand)(int)value).ToString();
            if (kind == InfrastructureChangeKind.BoilerService) return ((BoilerServiceKind)(int)value).ToString();
            if (kind == InfrastructureChangeKind.RadiatorSetting || kind == InfrastructureChangeKind.HeaterRoom) return value.ToString("F0");
            return value == 0 ? "no" : "yes";
        }
    }
}
#endif
