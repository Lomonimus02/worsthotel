#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace WorstHotel
{
    public sealed partial class DevelopmentVerification
    {
        readonly List<string> captures = new List<string>();
        string captureNote = "Developer lifecycle tour / virtual staff / public diagnostic commands / clock8x";
        bool offscreenAvailable = true;

        IEnumerator CaptureDeveloperPanel(string name)
        {
            var panel = FindAnyObjectByType<DeveloperPanel>();
            Require(panel != null, "production F2 panel");
            var scroll = typeof(DeveloperPanel).GetField("scroll", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(scroll != null, "production F2 scroll field");
            scroll.SetValue(panel, Vector2.zero);
            panel.SendMessage("Toggle");
            try
            {
                yield return Capture(name, "Original F2 developer panel / actual IMGUI and cameras");
                // The first capture has completed the original IMGUI layout. Only its scroll
                // position changes here; no control is clicked and no simulation override is set.
                scroll.SetValue(panel, new Vector2(0, 650));
                yield return Capture("developer-controls", "Original F2 heater and electrical controls / actual scrolled IMGUI / no override applied");
            }
            finally
            {
                scroll.SetValue(panel, Vector2.zero);
                panel.SendMessage("Toggle");
            }
        }

        IEnumerator CaptureGuestDetail()
        {
            float previousSpeed = driveSpeed; driveSpeed = 1;
            var ui = ManagementUI.Instance;
            ui.Close(); ui.Open(0);
            // Hidden windows need an actual rendered ledger before its IMGUI action list is
            // used. Keep this original UI image as evidence instead of assuming five yields did it.
            yield return Capture("guest-relations", "Original service ledger before owned virtual-pad navigation");
            var actions = ReadMenuField<List<Action>>(ui, "actions");
            var enabled = ReadMenuField<List<bool>>(ui, "enabledActions");
            facts.Add("Guest menu ready: " + MenuDiagnostic(ui));
            Require(ui.IsOpen && ui.Owner == 0 && !coop.IsPaused && actions.Count >= currentDay.Stays.Length * 2 + 2,
                "original service ledger populated its controls: " + MenuDiagnostic(ui));
            // The second guest is the cold-sensitive booking. Drive the same D-pad/A navigation
            // used by the players; reflection below only verifies the resulting selected id.
            // Each preceding guest contributes two review buttons: name and contextual concern.
            // Compensation is deliberately no longer a shortcut in the overview.
            int target = 2 + (session.Simulation.Boiler.Failed ? 1 : 0);
            Require(target < enabled.Count && enabled[target], "target guest detail control is enabled");
            for (int step = 0; ReadMenuField<int>(ui, "focus") != target && step < actions.Count; step++)
            {
                int before = ReadMenuField<int>(ui, "focus");
                yield return PressMenu(GamepadButton.DpadDown);
                facts.Add("Guest menu down: " + before + " -> " + MenuDiagnostic(ui));
                Require(ReadMenuField<int>(ui, "focus") != before,
                    "owned D-pad advanced real UI focus: " + MenuDiagnostic(ui));
            }
            Require(ReadMenuField<int>(ui, "focus") == target, "real UI focus reached the intended guest control");
            yield return PressMenu(GamepadButton.South);
            facts.Add("Guest menu confirm: " + MenuDiagnostic(ui));
            Require(ReadMenuField<string>(ui, "selectedServiceGuest") == currentDay.Stays[1].GuestId,
                "real controller navigation selected the cold-sensitive guest detail page: " + MenuDiagnostic(ui));
            yield return Capture("guest-detail", "Original guest detail page / selected with real virtual-pad menu input");
            ui.Close(); driveSpeed = previousSpeed;
        }

        IEnumerator PressMenu(GamepadButton button)
        {
            BindSyntheticStaff();
            var pad = verificationPads[0];
            Require(ReferenceEquals(coop.Players[0].Input.Gamepad, pad) && pad.added && pad.enabled,
                "only the created synthetic controller may receive verification menu input");
            // Establish a consumed neutral state before each edge, including the first confirm.
            InputSystem.QueueStateEvent(pad, new GamepadState());
            yield return null; yield return null;
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(button));
            bool read = false;
            for (int frame = 0; frame < 8 && !read; frame++)
            {
                yield return null;
                var input = coop.Players[0].Input;
                read = ReferenceEquals(input.Gamepad, pad) && (button == GamepadButton.DpadDown ?
                    input.Navigate.y < -.5f : button == GamepadButton.East ? input.MenuCancelPressed :
                    button == GamepadButton.South && input.MenuConfirmPressed);
            }
            BindSyntheticStaff();
            Require(ReferenceEquals(coop.Players[0].Input.Gamepad, pad) && pad.added && pad.enabled,
                "synthetic controller remains assigned before verification release");
            InputSystem.QueueStateEvent(pad, new GamepadState());
            yield return null; yield return null; yield return null;
            Require(read, "the production reader consumed " + button + " from synthetic device " + pad.deviceId);
        }

        static T ReadMenuField<T>(ManagementUI ui, string name)
        {
            var field = typeof(ManagementUI).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Require(field != null, "read-only UI diagnostic field: " + name);
            return (T)field.GetValue(ui);
        }

        string MenuDiagnostic(ManagementUI ui) => "owner=" + ui.Owner + " open=" + ui.IsOpen +
            " paused=" + coop.IsPaused + " actions=" + ReadMenuField<List<Action>>(ui, "actions").Count +
            " enabled=" + ReadMenuField<List<bool>>(ui, "enabledActions").Count +
            " focus=" + ReadMenuField<int>(ui, "focus") + " selected=" +
            (ReadMenuField<string>(ui, "selectedServiceGuest") ?? "<none>") +
            " pad=" + coop.Players[0].Input.Gamepad?.deviceId;

        IEnumerator DemonstrateHeaterAndCircuit()
        {
            var simulation = session.Simulation;
            var coldGuest = simulation.Guests.Single(g => g.RoomId == 106);
            var room = session.Rooms.Single(r => r.Profile.Id == 106);
            yield return Until(() => coldGuest.Needs.Temperature.Severity > .03f || simulation.Elapsed >= session.Settings.ServiceSeconds * .55f,
                20, "observe natural room106 temperature before the heater case");
            bool cold = coldGuest.Needs.Temperature.Severity > .03f && room.Temperature < coldGuest.Application.Archetype.Needs.PreferredTemperatureMin;
            facts.Add("Day2 before physical heater setup: room106=" + room.Temperature.ToString("F2") + "C, naturalCold=" + cold +
                ", boilerFailed=" + simulation.Boiler.Failed + ". No temperature or failure override.");
            driveSpeed = 1;
            electricalPanel.cover.RequestOpen();
            yield return Until(() => electricalPanel.cover.IsPassageOpen, 3, "physical electrical cabinet opens for inspection");
            // Clearly labelled diagnostic placement. The ordinary Rigidbody, volume resolver,
            // physical heater and electrical consumer stay enabled; carry is separately PlayMode-tested.
            heater.Body.position = new Vector3(6, .04f, 22.1f);
            heater.Body.rotation = Quaternion.identity;
            heater.Body.linearVelocity = heater.Body.angularVelocity = Vector3.zero;
            yield return Until(() => heater.State != null && heater.State.RoomId == 106 && !heater.IsCarried,
                4, "physical heater volume resolves room106");
            Require(session.SetHeaterSwitch(0, heater.heaterId, true).Success, "public heater switch on");
            heaterStartTemperature = heaterPeakTemperature = room.Temperature; heaterDemonstration = true;
            Position(coop.Players[0], new Vector3(6, .1f, 20.95f), new Vector3(6, .70f, 22.1f));
            Position(coop.Players[1], new Vector3(0, .1f, 24), new Vector3(4.4f, 1.25f, 24.3f));
            yield return Capture("portable-heater", "DIAGNOSTIC heater repositioning / actual room volume, switch, heat and electrical load");
            var circuit = simulation.Electrical.Find("B");
            Require(circuit.RequestedLoad > circuit.Capacity && heater.State.EffectiveHeatOutput > 0,
                "three real B occupants plus the physical heater overload the production circuit");
            driveSpeed = 8;
            yield return Until(() => circuit.Warning, 8, "sustained real electrical load produces a warning");
            Position(coop.Players[0], new Vector3(4.9f, .1f, 32.7f), new Vector3(4.9f, 1.9f, 35.25f));
            Position(coop.Players[1], new Vector3(0, .1f, 24), new Vector3(5.8f, 1.6f, 24.7f));
            yield return Capture("electrical-warning", "Actual overload timer / heater and three occupied B rooms / no forced trip");
            yield return Until(() => circuit.Tripped, 8, "continued overload naturally trips B");
            Require(!room.HasPower && heater.State.EffectiveHeatOutput == 0 && simulation.Electrical.Find("A").HasPower,
                "trip disconnects the heater and room lights while circuitA stays powered");
            facts.Add("Natural circuitB trip: requestedLoad=" + circuit.RequestedLoad.ToString("F2") +
                " capacity=" + circuit.Capacity.ToString("F2") + " trips=" + circuit.TripCount + "; actual heater heat=0, A unaffected.");
            yield return Capture("electrical-tripped", "Actual tripped B / room lights and heater off / independent A powered");
            Require(session.SetHeaterSwitch(Actor(1), heater.heaterId, false).Success, "public heater switch off removes demand");
            Require(session.ResetCircuit(Actor(1), "B").Success, "public reset after removing overload");
            float stableAt = simulation.Elapsed + simulation.Electrical.Settings.TripSeconds + 1;
            yield return Until(() => simulation.Elapsed >= stableAt || session.Phase != DayPhase.Service, 10, "observe stable circuit after removing heater load");
            Require(circuit.HasPower && !circuit.Tripped, "reset circuit stays powered with heater off");
            heaterDemonstration = false;
            if (simulation.Boiler.Failed)
            {
                Position(coop.Players[0], new Vector3(-3.6f, .1f, 33.8f), new Vector3(-1.5f, 1.8f, 36.5f));
                Position(coop.Players[1], new Vector3(2.2f, .1f, 34), new Vector3(1.7f, 1.6f, 36.5f));
                yield return Capture("boiler-natural-failure", "Naturally developed boiler fault / no ForceFailure call");
            }
            driveSpeed = 8;
        }

        static void Position(FirstPersonController player, Vector3 position, Vector3 target)
        {
            if (!player) return; // SOLO has no second staff rig or second viewpoint.
            var marker = new GameObject("Verification viewpoint").transform;
            marker.position = position;
            marker.rotation = Quaternion.Euler(0, Quaternion.LookRotation(target - (position + Vector3.up * 1.61f)).eulerAngles.y, 0);
            player.ResetToSpawn(marker); player.enabled = false;
            player.PlayerCamera.transform.LookAt(target);
            Destroy(marker.gameObject);
        }

        IEnumerator Capture(string name, string note)
        {
            capturing = true; captureNote = "DEVELOPER TOUR: " + note;
            for (int i = 0; i < 20; i++) yield return null;
            Require(!string.IsNullOrWhiteSpace(name) && Path.GetFileName(name) == name, "capture uses a local leaf name");
            Require(!captures.Contains(name + ".png"), "capture name is unique in this run");
            string path = Path.Combine(output, name + ".png");
            // Remove only this exact generated target; an earlier run must never satisfy readiness.
            if (File.Exists(path)) File.Delete(path);
            DateTime requestedUtc = DateTime.UtcNow;
            Texture2D frame = null;
            try
            {
                if (offscreenAvailable)
                {
                    frame = VerificationOffscreenCapture.Capture(coop.Players.Where(p => p && p.PlayerCamera.enabled).ToArray());
                    Require(VerificationOffscreenCapture.LastOverlaySubmitted, "actual IMGUI renderer list submitted");
                    File.WriteAllBytes(path, frame.EncodeToPNG());
                }
                else ScreenCapture.CaptureScreenshot(path);
            }
            catch (Exception exception)
            {
                offscreenAvailable = false;
                facts.Add("Offscreen GPU capture failed: " + exception.Message);
                Debug.LogWarning("VERIFY: fallback backbuffer image requires visual rejection/review. " + exception.Message);
                ScreenCapture.CaptureScreenshot(path);
            }
            finally { if (frame != null) Destroy(frame); }
            float deadline = Time.realtimeSinceStartup + 5;
            while (!CaptureFileReady(path, requestedUtc) && Time.realtimeSinceStartup < deadline) yield return null;
            Require(CaptureFileReady(path, requestedUtc), "fresh complete PNG written for " + name);
            for (int i = 0; i < 25; i++) yield return null;
            Require(CaptureFileReady(path, requestedUtc), "fresh capture remains valid before manifest: " + name);
            captures.Add(name + ".png");
            File.WriteAllLines(Path.Combine(output, "capture-manifest.txt"), captures);
            facts.Add("Capture " + name + ": " + note + ".");
            capturing = false;
            captureNote = "Developer lifecycle tour / virtual staff / public diagnostic commands / clock8x";
        }

        static bool CaptureFileReady(string path, DateTime requestedUtc)
        {
            try
            {
                if (!File.Exists(path) || File.GetLastWriteTimeUtc(path) < requestedUtc) return false;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (stream.Length < 32) return false;
                    byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
                    foreach (byte value in signature) if (stream.ReadByte() != value) return false;
                    // Async ScreenCapture can expose a partial file. Its final IEND chunk is required.
                    stream.Seek(-12, SeekOrigin.End);
                    byte[] end = { 0, 0, 0, 0, 73, 69, 78, 68, 174, 66, 96, 130 };
                    foreach (byte value in end) if (stream.ReadByte() != value) return false;
                }
                return File.GetLastWriteTimeUtc(path) >= requestedUtc;
            }
            catch (IOException) { return false; }
        }

        void OnGUI()
        {
            if (!initialized || diegeticTour) return;
            int previousDepth = GUI.depth;
            GUI.depth = -300;
            var previous = GUI.color;
            GUI.color = new Color(.035f, .04f, .045f, .95f);
            GUI.DrawTexture(new Rect(0, Screen.height - 22, Screen.width, 22), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(10, Screen.height - 22, Screen.width - 20, 22), captureNote);
            GUI.color = previous;
            GUI.depth = previousDepth;
        }
    }
}
#endif
