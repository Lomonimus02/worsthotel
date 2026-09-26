#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Security;
using System.Text;
using UnityEngine;

namespace WorstHotel
{
    public interface IPauseDiagnosticSink : IDisposable
    {
        void AppendEvent(string json);
        void ReplaceHeartbeat(string json);
    }

    /// <summary>Owns diagnostic output only. It receives values, never gameplay commands or callbacks.</summary>
    public sealed class PauseDiagnosticRecorder : IDisposable
    {
        readonly IPauseDiagnosticSink sink;
        readonly Action<string> warning;
        bool disposed;
        long sequence;
        int tickFrame = -1, frameStarts, frameCompletions, traceTicksRemaining;
        bool traceCurrentTick;
        public bool Enabled { get; private set; }
        public string Failure { get; private set; }
        public long TickStarts { get; private set; }
        public long TickCompletions { get; private set; }
        public int InFlightTicks { get; private set; }
        public int LastTickFrame => tickFrame;
        public float LastTickStep { get; private set; }
        public long FixedUpdates { get; private set; }

        public PauseDiagnosticRecorder(IPauseDiagnosticSink sink, Action<string> warning)
        { this.sink = sink; this.warning = warning; Enabled = sink != null; }

        public long NextSequence() => Enabled ? ++sequence : sequence;
        public int StartsInFrame(int frame) => frame == tickFrame ? frameStarts : 0;
        public int CompletionsInFrame(int frame) => frame == tickFrame ? frameCompletions : 0;
        public void Resumed() { if (Enabled) traceTicksRemaining = 3; }
        public void Paused() { traceTicksRemaining = 0; traceCurrentTick = false; }
        public void FixedUpdateObserved() { if (Enabled) FixedUpdates++; }

        public bool TickEntered(int frame, float step)
        {
            if (!Enabled) return false;
            if (tickFrame != frame) { tickFrame = frame; frameStarts = frameCompletions = 0; }
            TickStarts++; frameStarts++; InFlightTicks++; LastTickStep = step;
            traceCurrentTick = traceTicksRemaining > 0;
            if (traceCurrentTick) traceTicksRemaining--;
            return traceCurrentTick;
        }

        public bool TickExited(int frame)
        {
            if (!Enabled) return false;
            if (tickFrame != frame) { tickFrame = frame; frameStarts = frameCompletions = 0; }
            TickCompletions++; frameCompletions++;
            if (InFlightTicks > 0) InFlightTicks--;
            bool trace = traceCurrentTick; traceCurrentTick = false;
            return trace;
        }

        public void WriteEvent(PauseDiagnosticRecord record)
        {
            if (!Enabled) return;
            // The catch surrounds diagnostic serialization/output only, never a simulation operation.
            try { sink.AppendEvent(JsonUtility.ToJson(record)); }
            catch (Exception exception) when (IsOutputFailure(exception)) { Disable(exception); }
        }

        public void WriteHeartbeat(PauseDiagnosticHeartbeat heartbeat)
        {
            if (!Enabled) return;
            try { sink.ReplaceHeartbeat(JsonUtility.ToJson(heartbeat)); }
            catch (Exception exception) when (IsOutputFailure(exception)) { Disable(exception); }
        }

        static bool IsOutputFailure(Exception exception) => exception is IOException ||
            exception is UnauthorizedAccessException || exception is SecurityException ||
            exception is ArgumentException || exception is InvalidOperationException || exception is NotSupportedException;

        public void Disable(Exception exception)
        {
            if (!Enabled) return;
            Enabled = false;
            Failure = exception.GetType().Name + ": " + exception.Message;
            DisposeSink();
            warning?.Invoke("PAUSE_DIAGNOSTICS_DISABLED: " + Failure + ". Gameplay is unchanged; external heartbeat will stop.");
        }

        void DisposeSink()
        {
            if (disposed) return;
            disposed = true;
            try { sink?.Dispose(); }
            catch (Exception exception) when (IsOutputFailure(exception)) { /* Already disabling diagnostic output. */ }
        }
        public void Dispose() { Enabled = false; DisposeSink(); }
    }

    /// <summary>Sparse sampling policy; elapsed values are supplied so tests need no real waiting.</summary>
    public sealed class PauseDiagnosticCadence
    {
        bool initialized, paused;
        double nextSample;
        int lastFrame = -1, resumedFrames;
        bool tracingResume;
        public int ResumedFrames => resumedFrames;

        public void ObservePause(bool value, double now)
        {
            if (initialized && paused == value) return;
            bool wasPaused = initialized && paused;
            initialized = true; paused = value;
            nextSample = now + 30;
            tracingResume = wasPaused && !value;
            resumedFrames = 0; lastFrame = -1;
        }

        public bool ShouldSamplePause(double now)
        {
            if (!initialized || !paused || now < nextSample) return false;
            // A delayed diagnostic frame produces one sample, not a wall-time catch-up loop.
            nextSample = now + 30;
            return true;
        }

        public bool ShouldTraceResumedFrame(int frame)
        {
            if (!tracingResume || paused || frame == lastFrame) return false;
            lastFrame = frame; resumedFrames++;
            if (resumedFrames >= 60) tracingResume = false;
            return resumedFrames == 1 || resumedFrames == 2 || resumedFrames == 3 ||
                resumedFrames == 10 || resumedFrames == 30 || resumedFrames == 60;
        }
    }

    /// <summary>Single writer; no retained event queue or background thread.</summary>
    public sealed class PauseDiagnosticFileSink : IPauseDiagnosticSink
    {
        readonly StreamWriter events;
        readonly string heartbeat, temporary;
        static readonly Encoding Utf8 = new UTF8Encoding(false);
        public PauseDiagnosticFileSink(string directory)
        {
            Directory.CreateDirectory(directory);
            heartbeat = Path.Combine(directory, "heartbeat.json");
            temporary = Path.Combine(directory, "heartbeat.tmp");
            events = new StreamWriter(new FileStream(Path.Combine(directory, "pause-events.jsonl"),
                FileMode.CreateNew, FileAccess.Write, FileShare.Read), Utf8);
        }
        public void AppendEvent(string json) { events.WriteLine(json); events.Flush(); }
        public void ReplaceHeartbeat(string json)
        {
            File.WriteAllText(temporary, json, Utf8);
            if (File.Exists(heartbeat)) File.Replace(temporary, heartbeat, null);
            else File.Move(temporary, heartbeat);
        }
        public void Dispose() => events.Dispose();
    }
}
#endif
