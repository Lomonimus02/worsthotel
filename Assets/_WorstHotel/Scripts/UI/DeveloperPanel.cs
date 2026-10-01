using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Globalization;
using System.Linq;
using UnityEngine.InputSystem;
#endif

namespace WorstHotel
{
    [DefaultExecutionOrder(-475)]
    public sealed partial class DeveloperPanel : MonoBehaviour
    {
        public bool IsVisible
        {
            get
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                return visible;
#else
                return false;
#endif
            }
        }

        /// <summary>Called before LAN configures input and its connection-menu pause.</summary>
        public void CloseForLan()
        {
            NoisePropagationDebug.Visible = false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            pendingSleepDiagnostic = 0;
            if (!visible) return;
            visible = false;
            requestedClockSpeed = 1;
            // The new mode decides its own pause state. Do not restore wasPaused from
            // the old local session or apply a deferred developer clock override.
            if (LocalCoopBootstrap.Instance) LocalCoopBootstrap.Instance.SetPaused(false);
#endif
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        bool visible, wasPaused;
        string cash = "2000", condition = "30", load = "6", temperature = "12", noise = "0.7", advance = "60";
        int roomIndex;
        GuestKind guestKind = GuestKind.ColdSensitive;
        string message = "Developer overrides change this runtime session only.";
        Vector2 scroll;
        GUIStyle body, heading, button, field;
        GameSession Session => GameSession.Instance;

        void Update()
        {
            if (Session && Session.Simulation?.OwnershipLost == true) { CloseForLan(); return; }
            if (RunPendingSleepDiagnostic()) return;
            if (Keyboard.current != null && Keyboard.current.f2Key.wasPressedThisFrame) Toggle();
            if (visible && LocalCoopBootstrap.Instance && !LocalCoopBootstrap.Instance.IsPaused)
                LocalCoopBootstrap.Instance.SetPaused(true);
        }

        void Toggle()
        {
            if (Session && Session.Simulation?.OwnershipLost == true) return;
            if (LanSession.Instance && LanSession.Instance.IsActive) return;
            var coop = LocalCoopBootstrap.Instance;
            if (!coop || !Session) return;
            visible = !visible;
            if (visible) { requestedClockSpeed = 1; wasPaused = coop.IsPaused; coop.SetPaused(true); }
            else
            {
                coop.SetPaused(wasPaused);
                if (Session.Phase == DayPhase.Service || Session.Phase == DayPhase.Planning) Session.Simulation.Clock.SetSpeed(requestedClockSpeed);
            }
        }

        void Apply(Func<CommandResult> operation)
        {
            try
            {
                using (Session.Simulation.BeginDiagnosticInfrastructureChange()) message = operation().Message;
                Session.RefreshDebugState();
            }
            catch (ArgumentException exception) { message = exception.Message; }
        }

        void NumberCommand(string text, Func<float, CommandResult> operation)
        {
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !Number.IsFinite(value))
            { message = "Enter a finite number using a decimal point."; return; }
            Apply(() => operation(value));
        }

        bool Button(string label, bool enabled = true)
        {
            GUI.enabled = enabled;
            bool clicked = GUILayout.Button(label, button, GUILayout.Height(32));
            GUI.enabled = true;
            return clicked;
        }

        void NumericRow(string label, ref string value, string action, Func<float, CommandResult> command)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, body, GUILayout.Width(195));
            value = GUILayout.TextField(value, field, GUILayout.Width(105));
            if (Button(action)) NumberCommand(value, command);
            GUILayout.EndHorizontal();
        }

        void OnGUI()
        {
            if (!visible || !Session || Session.Simulation == null || Session.Simulation.OwnershipLost) return;
            if (body == null)
            {
                body = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true };
                heading = new GUIStyle(body) { fontSize = 23, fontStyle = FontStyle.Bold };
                button = new GUIStyle(GUI.skin.button) { fontSize = 15 };
                field = new GUIStyle(GUI.skin.textField) { fontSize = 17 };
            }
            GUI.depth = -200;
            var rect = new Rect(Mathf.Max(12, (Screen.width - 680) / 2f), 20, Mathf.Min(680, Screen.width - 24), Screen.height - 40);
            var oldColor = GUI.color;
            GUI.color = new Color(.085f, .095f, .11f, 1);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = oldColor;
            GUILayout.BeginArea(new Rect(rect.x + 18, rect.y + 14, rect.width - 36, rect.height - 28));
            GUILayout.Label("DEVELOPER PANEL   /   F2", heading);
            GUILayout.Label("Hotel paused while editing. Commands affect runtime state, never config assets.", body);
            scroll = GUILayout.BeginScrollView(scroll);
            var simulation = Session.Simulation;
            var boiler = simulation.Boiler;
            GUILayout.Label("Day " + Session.Day + "  /  " + Session.Phase + "  /  " + simulation.Elapsed.ToString("F1") + " seconds\nCash $" + simulation.Economy.Cash + "  •  Reputation " + simulation.Economy.Reputation.ToString("F1") +
                "\nCondition " + boiler.Condition.ToString("F1") + "  •  Load " + boiler.Load.ToString("F2") + "  •  Pressure " + boiler.Pressure.ToString("F1") + (boiler.LoadOverride.HasValue ? "  [LOAD OVERRIDE]" : ""), body);
            DrawHeatingCapacityDebug();
            DrawBoilerRhythmDebug();
            NumericRow("Cash", ref cash, "Set cash", value => simulation.DebugSetCash(value));
            NumericRow("Boiler condition", ref condition, "Set condition", value => { boiler.SetCondition(value); return CommandResult.Ok("Condition updated; an active fault still needs repair."); });
            NumericRow("Forced demand", ref load, "Override load", value => { boiler.OverrideLoad(value); return CommandResult.Ok("Load override set; reset it to restore guest demand."); });
            GUILayout.BeginHorizontal();
            if (Button("Reset demand override")) Apply(() => { boiler.OverrideLoad(null); return CommandResult.Ok("Load now follows actual guests."); });
            if (Button("Force boiler failure", Session.Phase == DayPhase.Service && !boiler.MaintenanceInProgress)) Apply(() => { boiler.ForceFailure(); return CommandResult.Ok("Failure forced for repair testing."); });
            GUILayout.EndHorizontal();
            GUILayout.Space(9);
            GUILayout.BeginHorizontal();
            if (Button("‹ Room")) roomIndex = (roomIndex + Session.Rooms.Length - 1) % Session.Rooms.Length;
            var room = Session.Rooms[roomIndex];
            GUILayout.Label(room.Profile.Id + "  /  " + room.Temperature.ToString("F1") + "°C  /  " + (room.Occupied ? "Occupied" : "Free"), body, GUILayout.Width(260));
            if (Button("Room ›")) roomIndex = (roomIndex + 1) % Session.Rooms.Length;
            GUILayout.EndHorizontal();
            NumericRow("Room temperature", ref temperature, "Set temperature", value => simulation.SetRoomTemperature(room.Profile.Id, value));
            DrawThermalDebug(room);
            NumericRow("Room noise (0–1)", ref noise, "Set noise", value => simulation.SetRoomNoise(room.Profile.Id, value));
            DrawRoomNoiseDebug(room);
            DrawRoomServicesDebug(room);
            GUILayout.BeginHorizontal();
            foreach (GuestKind kind in Enum.GetValues(typeof(GuestKind)))
                if (Button((guestKind == kind ? "● " : "") + kind)) guestKind = kind;
            GUILayout.EndHorizontal();
            if (Button("Spawn guest in selected vacant room", Session.Phase == DayPhase.Service && !room.Occupied && !room.Reserved))
                Apply(() => simulation.DebugSpawnGuest(guestKind, room.Profile.Id));
            NumericRow("Advance seconds", ref advance, "Advance simulation", value => { if (value <= 0 || (Session.Phase != DayPhase.Service && Session.Phase != DayPhase.Planning)) return CommandResult.Fail("Use preparation or service and enter positive seconds."); Session.AdvanceTime(value); return CommandResult.Ok("Simulation advanced in fixed ticks; physical travel still needs rendered frames."); });
            DrawClockDebug();
            var director = Session.Simulation.Director;
            if (director != null) GUILayout.Label("DIRECTOR (internal) · " + director.Pressure.Band + " " + director.Pressure.Score.ToString("F1") +
                " · quiet " + director.QuietElapsed.ToString("F0") + "s · budget " + director.SpentToday + "/" + director.Budget +
                "\n" + director.Pressure.Reason + " · premises " + director.History.Count, body);
            DrawRhythmClockDebug();
            DrawSalesRhythmDebug();
            DrawGuestDebug();
            DrawServiceDebug();
            DrawHeaterDebug();
            DrawElectricityDebug();
            DrawHousekeepingDebug();
            DrawInfrastructureHistory();
            GUILayout.BeginHorizontal();
            if (Button("Start shift", Session.Phase == DayPhase.Planning))
                Apply(() =>
                {
                    if (Session.Plan.Assignments.Count == 0)
                        for (int i = 0; i < 3; i++)
                        {
                            var offer = Session.Plan.Applications[i];
                            int rate = Session.Economy.MinPrice + Mathf.RoundToInt((offer.ReferencePrice - Session.Economy.MinPrice) / (float)Session.Economy.PriceStep) * Session.Economy.PriceStep;
                            Session.Assign(0, offer.Id, Session.Rooms[i].Profile.Id, Mathf.Clamp(rate, Session.Economy.MinPrice, Session.Economy.MaxPrice));
                        }
                    Session.CommitPlan(0); return Session.Phase == DayPhase.Service ? CommandResult.Ok("Shift started with the current plan.") : CommandResult.Fail(Session.LastMessage);
                });
            if (Button("End shift / settle", !simulation.ContinuousOperations && Session.Phase == DayPhase.Service))
                Apply(() => { Session.EndShift(); return CommandResult.Ok("Early checkout settled once."); });
            GUILayout.EndHorizontal();
            GUILayout.Label("Requests — developer resolution does not repair the room:", body);
            foreach (var request in simulation.Requests.Items.Where(r => !r.Resolved).ToArray())
                if (Button("Resolve " + request.RoomId + " / " + request.Reason + " / " + request.GuestName)) Apply(() => simulation.Requests.ResolveRequest(request.Id));
            GUILayout.Label(message, body);
            GUILayout.EndScrollView();
            if (Button("Close developer panel")) Toggle();
            GUILayout.EndArea();
            GUI.depth = 0;
        }

        void OnDisable()
        {
            pendingSleepDiagnostic = 0;
            if (visible && LocalCoopBootstrap.Instance) LocalCoopBootstrap.Instance.SetPaused(wasPaused);
            visible = false;
        }
#endif
    }
}
