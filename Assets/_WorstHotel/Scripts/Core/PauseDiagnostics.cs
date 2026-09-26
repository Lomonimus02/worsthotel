using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using UnityEngine.Profiling;
using Stopwatch = System.Diagnostics.Stopwatch;
#endif

namespace WorstHotel
{
    /// <summary>Opt-in evidence only. Never changes focus, input, clocks, simulation or pause policy.</summary>
    [DefaultExecutionOrder(32000)]
    public sealed class PauseDiagnostics : MonoBehaviour
    {
        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        public static void FocusCallback(bool requestedFocus, bool entering)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!Active) return;
            instance.Record(entering ? "focus_callback_enter" : "focus_callback_exit", "requestedFocus=" + requestedFocus);
            instance.Heartbeat();
#endif
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        public static void PauseChanged(bool paused)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!Active) return;
            instance.cadence.ObservePause(paused, instance.Now);
            if (paused) instance.recorder.Paused(); else instance.recorder.Resumed();
            instance.Record(paused ? "pause_enter" : "pause_exit");
            instance.Heartbeat();
#endif
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        public static void DeviceChanged(int id, string layout, string change)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!Active) return;
            instance.deviceChanges++;
            instance.lastDeviceChange = id + "/" + layout + "/" + change;
            double now = instance.Now;
            if (now >= instance.nextDeviceWindow) { instance.nextDeviceWindow = now + 1; instance.deviceEventsInWindow = 0; }
            // Bound diagnostic output only. Every real callback remains counted and otherwise untouched.
            if (++instance.deviceEventsInWindow <= 16) instance.Record("input_device_change", instance.lastDeviceChange);
            else if (instance.deviceEventsInWindow == 17) instance.Record("input_device_changes_coalesced", "Further changes counted; at most 16 detailed records per real second.");
#endif
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        public static void TickEnter(float step)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Active && instance.recorder.TickEntered(Time.frameCount, step)) instance.Record("resume_tick_enter");
#endif
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        public static void TickExit()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Active && instance.recorder.TickExited(Time.frameCount)) instance.Record("resume_tick_exit");
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        static PauseDiagnostics instance;
        static bool Active => instance && instance.recorder != null && instance.recorder.Enabled;
        PauseDiagnosticRecorder recorder;
        readonly PauseDiagnosticCadence cadence = new PauseDiagnosticCadence();
        Stopwatch stopwatch;
        string runId, lastDeviceChange = "none";
        int processId, deviceChanges, deviceEventsInWindow;
        double nextHeartbeat, nextDeviceWindow;
        bool quitting;
        HotelSimulation observedModel;
        double Now => stopwatch.Elapsed.TotalSeconds;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            var arguments = Environment.GetCommandLineArgs();
            int index = Array.FindIndex(arguments, arg => string.Equals(arg, "-pauseDiagnostics", StringComparison.OrdinalIgnoreCase));
            if (index < 0) return;
            if (index + 1 >= arguments.Length || string.IsNullOrWhiteSpace(arguments[index + 1]) || arguments[index + 1].StartsWith("-"))
            { Debug.LogWarning("PAUSE_DIAGNOSTICS_DISABLED: -pauseDiagnostics requires an output directory. Gameplay is unchanged."); return; }
            if (instance) return;
            var observer = new GameObject("Opt-in pause and native focus evidence").AddComponent<PauseDiagnostics>();
            DontDestroyOnLoad(observer.gameObject);
            observer.Initialize(arguments[index + 1]);
        }

        void Initialize(string output)
        {
            instance = this;
            stopwatch = Stopwatch.StartNew();
            try
            {
                string directory = Path.GetFullPath(output);
                runId = new DirectoryInfo(directory).Name;
                using (var process = System.Diagnostics.Process.GetCurrentProcess()) processId = process.Id;
                recorder = new PauseDiagnosticRecorder(new PauseDiagnosticFileSink(directory), message => Debug.LogWarning(message));
                cadence.ObservePause(LocalCoopBootstrap.Instance && LocalCoopBootstrap.Instance.IsPaused, Now);
                Record("observer_start", "Read-only diagnostics; no input synthesis or focus injection. Non-finite model deadlines are encoded as -1. Counts are model collections, not engine queue sizes.");
                Heartbeat();
            }
            catch (Exception exception) when (DiagnosticFailure(exception))
            {
                if (recorder != null) recorder.Disable(exception);
                else Debug.LogWarning("PAUSE_DIAGNOSTICS_DISABLED: " + exception.GetType().Name + ": " + exception.Message + ". Gameplay is unchanged.");
                enabled = false;
            }
        }

        void LateUpdate()
        {
            if (!Active) return;
            var session = GameSession.Instance;
            var coop = LocalCoopBootstrap.Instance;
            if (session && !ReferenceEquals(observedModel, session.Simulation))
            { observedModel = session.Simulation; Record("model_bound"); }
            bool paused = coop && coop.IsPaused;
            cadence.ObservePause(paused, Now);
            if (Now >= nextHeartbeat) Heartbeat();
            if (cadence.ShouldSamplePause(Now)) Record("pause_sample");
            if (cadence.ShouldTraceResumedFrame(Time.frameCount)) Record("resumed_frame");
        }

        void FixedUpdate() { if (Active) recorder.FixedUpdateObserved(); }
        void OnApplicationPause(bool value)
        { if (Active) { Record("application_pause_callback", "value=" + value); Heartbeat(); } }
        void OnApplicationQuit()
        { quitting = true; if (Active) { Record("application_quit"); Heartbeat(); } recorder?.Dispose(); }
        void OnDestroy()
        {
            if (instance != this) return;
            if (!quitting && Active) Record("observer_destroyed");
            recorder?.Dispose(); instance = null;
        }

        void FillHeader(PauseDiagnosticHeartbeat record)
        {
            var coop = LocalCoopBootstrap.Instance;
            var session = GameSession.Instance;
            record.processId = processId; record.runId = runId; record.sequence = recorder.NextSequence();
            record.utc = DateTime.UtcNow.ToString("O"); record.monotonicSeconds = Now; record.frame = Time.frameCount;
            record.focus = Application.isFocused; record.pause = coop && coop.IsPaused;
            record.initialized = coop && session && session.Simulation != null && coop.Players[0];
        }

        void Heartbeat()
        {
            if (!Active) return;
            try
            {
                var heartbeat = new PauseDiagnosticHeartbeat(); FillHeader(heartbeat);
                recorder.WriteHeartbeat(heartbeat); nextHeartbeat = Now + 2;
            }
            catch (Exception exception) when (DiagnosticFailure(exception)) { recorder.Disable(exception); }
        }

        void Record(string kind, string detail = null)
        {
            if (!Active) return;
            // Only observation/serialization is inside this boundary. Original game code executes outside it.
            try
            {
                var record = Capture(); FillHeader(record); record.kind = kind; record.detail = detail ?? "";
                recorder.WriteEvent(record);
            }
            catch (Exception exception) when (DiagnosticFailure(exception)) { recorder.Disable(exception); }
        }

        static bool DiagnosticFailure(Exception exception) => exception is IOException ||
            exception is UnauthorizedAccessException || exception is System.Security.SecurityException ||
            exception is ArgumentException || exception is InvalidOperationException ||
            exception is NotSupportedException || exception is NullReferenceException || exception is UnityException;

        PauseDiagnosticRecord Capture()
        {
            var coop = LocalCoopBootstrap.Instance;
            var session = GameSession.Instance;
            var lan = LanSession.Instance;
            var record = new PauseDiagnosticRecord
            {
                version = Application.version, unityVersion = Application.unityVersion,
                mode = coop ? coop.Mode.ToString() : "unbound", role = lan ? lan.Role.ToString() : "Offline",
                phase = session ? session.Phase.ToString() : "unbound", day = session ? session.Day : 0,
                bootstrapFocus = coop && coop.DiagnosticFocus, manualPause = coop && coop.DiagnosticManualPause,
                devicesDirty = coop && coop.DiagnosticDevicesDirty, waitingForDevices = coop && coop.WaitingForDevices,
                menuOpen = lan && lan.MenuOpen, managementOpen = ManagementUI.Instance && ManagementUI.Instance.IsOpen,
                applicationRunInBackground = Application.runInBackground,
                timeScale = Time.timeScale, deltaTime = Time.deltaTime, unscaledDeltaTime = Time.unscaledDeltaTime,
                fixedDeltaTime = Time.fixedDeltaTime, accumulator = session ? session.DiagnosticAccumulator : 0,
                tickStarts = recorder.TickStarts, tickCompletions = recorder.TickCompletions, inFlightTicks = recorder.InFlightTicks,
                frameTickStarts = recorder.StartsInFrame(Time.frameCount), frameTickCompletions = recorder.CompletionsInFrame(Time.frameCount),
                lastTickFrame = recorder.LastTickFrame, lastTickStep = recorder.LastTickStep,
                diagnosticFixedUpdates = recorder.FixedUpdates, resumedFrame = cadence.ResumedFrames,
                managedBytes = GC.GetTotalMemory(false), gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2),
                unityAllocatedBytes = Profiler.GetTotalAllocatedMemoryLong(), unityReservedBytes = Profiler.GetTotalReservedMemoryLong(),
                unityUnusedReservedBytes = Profiler.GetTotalUnusedReservedMemoryLong(),
                deviceChanges = deviceChanges, lastDeviceChange = lastDeviceChange,
                peerConnected = lan && lan.PeerConnected, hasSnapshot = lan && lan.HasSnapshot,
                modelSequence = lan ? lan.AppliedModelSequence : -1, worldBytes = lan ? lan.LastWorldBytes : 0,
                worldDecodedBytes = lan ? lan.LastWorldDecodedBytes : 0,
                remoteInputLeaseExpired = coop && coop.RemoteInputLeaseExpired,
                model = PauseDiagnosticModelState.Capture(session ? session.Simulation : null)
            };
            var devices = new List<PauseDiagnosticInputState>();
            foreach (var device in InputSystem.devices)
                devices.Add(new PauseDiagnosticInputState { id = device.deviceId, layout = device.layout,
                    name = device.name, enabled = device.enabled, added = device.added, canRunInBackground = device.canRunInBackground });
            record.devices = devices.ToArray();
            return record;
        }
#endif
    }
}
