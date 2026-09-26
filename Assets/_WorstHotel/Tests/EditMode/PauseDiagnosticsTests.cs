using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace WorstHotel.Tests
{
    /// <summary>Tests the evidence recorder, not a speculative reproduction of the reported native focus crash.</summary>
    public sealed class PauseDiagnosticsTests
    {
        sealed class MemorySink : IPauseDiagnosticSink
        {
            public readonly List<string> Events = new List<string>();
            public string Heartbeat;
            public bool FailEvents, FailHeartbeat, Disposed;
            public void AppendEvent(string json)
            { if (FailEvents) throw new IOException("fixture event disk failure"); Events.Add(json); }
            public void ReplaceHeartbeat(string json)
            { if (FailHeartbeat) throw new IOException("fixture heartbeat disk failure"); Heartbeat = json; }
            public void Dispose() { Disposed = true; }
        }

        static HotelSimulation ModelWithAcceptedPromise()
        {
            var profiles = new[]
            {
                new GuestProfile(GuestKind.Budget, "Budget", "", 180, .85f, 18, .7f, 28, 90, .55f),
                new GuestProfile(GuestKind.ColdSensitive, "Cold", "", 300, 1.05f, 20, 1.35f, 16, 65, .4f),
                new GuestProfile(GuestKind.Business, "Business", "", 450, 1, 19.5f, 1, 10, 45, .25f)
            };
            var settings = new SessionSettings(profiles, Enumerable.Range(101, 6).Select(id => new RoomProfile(id, "Room " + id)),
                new BoilerSettings(), new EconomySettings());
            var simulation = new HotelSimulation(settings, settings.Rooms.Select(room => new RoomState(room)).ToArray(),
                new LivingHotelSettings(firstArrivalSeconds: .2f, arrivalJitterSeconds: 0),
                services: new GuestServiceSettings(eligibility: 0));
            var offer = new BookingApplication("diagnostic-guest", "Diagnostic guest", profiles[2], 450);
            Assert.That(simulation.StartShift(new[] { new BookingAssignment(101, offer.Id, 450, 0) }, new[] { offer }).Success, Is.True);
            simulation.Tick(.4f);
            Assert.That(simulation.SignalGuestReachedReception(offer.Id).Success, Is.True);
            // Explicit model boundary adapters prepare a meaningful read-only capture fixture.
            Assert.That(ModelKeyHandoff.CheckIn(simulation, 0, offer.Id).Success, Is.True);
            Assert.That(simulation.SignalGuestReachedRoom(offer.Id).Success, Is.True);
            Assert.That(simulation.DebugForceService(offer.Id, ServiceKind.WakeUpCall).Success, Is.True);
            Assert.That(simulation.RespondToService(0, simulation.Services.Cases.Single().Id, true).Success, Is.True);
            return simulation;
        }

        [Test]
        public void RepeatedEvidenceCaptureDoesNotMutateModelClockPromisesMemoryOrUnityTimeScale()
        {
            var simulation = ModelWithAcceptedPromise();
            string before = JsonUtility.ToJson(simulation.CaptureSnapshot(1, 1));
            float scale = Time.timeScale;
            var sink = new MemorySink();
            using (var recorder = new PauseDiagnosticRecorder(sink, message => Assert.Fail(message)))
            {
                for (int i = 0; i < 12; i++)
                {
                    var view = PauseDiagnosticModelState.Capture(simulation);
                    Assert.That(view.guestCount, Is.EqualTo(1));
                    Assert.That(view.acceptedPromises, Is.EqualTo(1));
                    Assert.That(view.activeServiceCases, Is.EqualTo(1));
                    Assert.That(view.scheduleEntryCount, Is.EqualTo(24));
                    recorder.WriteEvent(new PauseDiagnosticRecord { kind = "pause_sample", sequence = recorder.NextSequence(), model = view });
                }
            }
            Assert.That(JsonUtility.ToJson(simulation.CaptureSnapshot(1, 1)), Is.EqualTo(before));
            Assert.That(Time.timeScale, Is.EqualTo(scale));
            Assert.That(sink.Events.Count, Is.EqualTo(12));
        }

        [Test]
        public void PausedSamplingIsThirtySecondsWithoutCatchUpBurstAndResumeFramesAreSparse()
        {
            var cadence = new PauseDiagnosticCadence();
            cadence.ObservePause(true, 100);
            Assert.That(cadence.ShouldSamplePause(129.999), Is.False);
            Assert.That(cadence.ShouldSamplePause(130), Is.True);
            Assert.That(cadence.ShouldSamplePause(130), Is.False);
            Assert.That(cadence.ShouldSamplePause(500), Is.True);
            Assert.That(cadence.ShouldSamplePause(500), Is.False, "A delayed observer must not emit a backlog of samples.");
            cadence.ObservePause(false, 501);
            var sampled = new List<int>();
            for (int i = 1; i <= 70; i++)
            {
                if (cadence.ShouldTraceResumedFrame(1000 + i)) sampled.Add(i);
                Assert.That(cadence.ShouldTraceResumedFrame(1000 + i), Is.False, "Duplicate observation of the same Unity frame is not another resumed frame.");
            }
            Assert.That(sampled, Is.EqualTo(new[] { 1, 2, 3, 10, 30, 60 }));
            Assert.That(cadence.ShouldSamplePause(1000), Is.False);
        }

        [Test]
        public void NewPauseCancelsRemainingResumeTraceAndInitialUnpausedStateDoesNotInventResume()
        {
            var cadence = new PauseDiagnosticCadence();
            cadence.ObservePause(false, 0);
            Assert.That(cadence.ShouldTraceResumedFrame(1), Is.False);
            cadence.ObservePause(true, 1); cadence.ObservePause(false, 2);
            Assert.That(cadence.ShouldTraceResumedFrame(2), Is.True);
            cadence.ObservePause(true, 3);
            Assert.That(cadence.ShouldTraceResumedFrame(3), Is.False);
            cadence.ObservePause(false, 4);
            Assert.That(cadence.ShouldTraceResumedFrame(4), Is.True);
            Assert.That(cadence.ResumedFrames, Is.EqualTo(1));
        }

        [Test]
        public void TickCountersDistinguishEntryCompletionFrameAndAnUncompletedTickWithoutWritingPerTick()
        {
            var sink = new MemorySink();
            using (var recorder = new PauseDiagnosticRecorder(sink, message => Assert.Fail(message)))
            {
                Assert.That(recorder.TickEntered(1, .2f), Is.False); recorder.TickExited(1);
                recorder.Resumed();
                for (int i = 0; i < 8; i++)
                {
                    Assert.That(recorder.TickEntered(2, .2f), Is.EqualTo(i < 3));
                    Assert.That(recorder.TickExited(2), Is.EqualTo(i < 3));
                }
                Assert.That(recorder.StartsInFrame(2), Is.EqualTo(8));
                Assert.That(recorder.CompletionsInFrame(2), Is.EqualTo(8));
                Assert.That(recorder.StartsInFrame(3), Is.Zero);
                recorder.TickEntered(3, .2f); // No completion callback: e.g. original gameplay threw outside the recorder.
                Assert.That(recorder.TickStarts, Is.EqualTo(10));
                Assert.That(recorder.TickCompletions, Is.EqualTo(9));
                Assert.That(recorder.InFlightTicks, Is.EqualTo(1));
                Assert.That(recorder.CompletionsInFrame(3), Is.Zero);
                Assert.That(sink.Events, Is.Empty, "Tick hooks count only; the observer chooses sparse writes.");
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OutputFailureDisablesOnlyTheRecorderAndReportsExactlyOnce(bool heartbeatFailure)
        {
            var simulation = ModelWithAcceptedPromise();
            string before = JsonUtility.ToJson(simulation.CaptureSnapshot(1, 1));
            var warnings = new List<string>();
            var sink = new MemorySink { FailHeartbeat = heartbeatFailure, FailEvents = !heartbeatFailure };
            using (var recorder = new PauseDiagnosticRecorder(sink, warnings.Add))
            {
                if (heartbeatFailure) recorder.WriteHeartbeat(new PauseDiagnosticHeartbeat());
                else recorder.WriteEvent(new PauseDiagnosticRecord());
                recorder.WriteEvent(new PauseDiagnosticRecord()); recorder.WriteHeartbeat(new PauseDiagnosticHeartbeat());
                recorder.TickEntered(3, .2f); recorder.TickExited(3);
                Assert.That(recorder.Enabled, Is.False);
                Assert.That(recorder.TickStarts, Is.Zero);
                Assert.That(sink.Disposed, Is.True);
                Assert.That(warnings.Count, Is.EqualTo(1));
                StringAssert.Contains("PAUSE_DIAGNOSTICS_DISABLED", warnings[0]);
                StringAssert.Contains("IOException", warnings[0]);
            }
            Assert.That(JsonUtility.ToJson(simulation.CaptureSnapshot(1, 1)), Is.EqualTo(before));
            Assert.Throws<ArgumentOutOfRangeException>(() => simulation.Tick(-1), "Original gameplay exceptions remain ordinary exceptions outside the recorder.");
            simulation.Tick(.2f);
            Assert.That(simulation.Elapsed, Is.GreaterThan(.4f), "Recorder failure cannot stop otherwise valid gameplay.");
        }

        [Test]
        public void DisabledRecorderDoesNotWriteCountOrWarn()
        {
            using (var recorder = new PauseDiagnosticRecorder(null, message => Assert.Fail(message)))
            {
                recorder.WriteEvent(new PauseDiagnosticRecord()); recorder.WriteHeartbeat(new PauseDiagnosticHeartbeat());
                recorder.Resumed(); recorder.TickEntered(1, .2f); recorder.TickExited(1); recorder.FixedUpdateObserved();
                Assert.That(recorder.Enabled, Is.False);
                Assert.That(recorder.NextSequence(), Is.Zero);
                Assert.That(recorder.TickStarts, Is.Zero);
                Assert.That(recorder.FixedUpdates, Is.Zero);
            }
        }

        [Test]
        public void AtomicHeartbeatReplacementKeepsExactSchemaAndDoesNotRetainTemporaryOrEventQueueFiles()
        {
            string directory = Path.Combine(Path.GetTempPath(), "WorstHotelPauseDiagnostics-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var recorder = new PauseDiagnosticRecorder(new PauseDiagnosticFileSink(directory), message => Assert.Fail(message)))
                {
                    recorder.WriteEvent(new PauseDiagnosticRecord { runId = "test-run", processId = 123, sequence = recorder.NextSequence(), kind = "focus_callback_exit", focus = true });
                    for (int i = 0; i < 3; i++) recorder.WriteHeartbeat(new PauseDiagnosticHeartbeat
                    { runId = "test-run", processId = 123, sequence = recorder.NextSequence(), utc = "2026-09-26T17:00:00.0000000Z", frame = i, monotonicSeconds = i * 2, focus = i == 2, pause = i != 2, initialized = true });
                }
                var heartbeat = JsonUtility.FromJson<PauseDiagnosticHeartbeat>(File.ReadAllText(Path.Combine(directory, "heartbeat.json")));
                Assert.That(heartbeat.schemaVersion, Is.EqualTo(1));
                Assert.That(heartbeat.sequence, Is.EqualTo(4));
                Assert.That(heartbeat.processId, Is.EqualTo(123));
                Assert.That(heartbeat.runId, Is.EqualTo("test-run"));
                Assert.That(heartbeat.focus && !heartbeat.pause && heartbeat.initialized, Is.True);
                Assert.That(heartbeat.monotonicSeconds, Is.EqualTo(4));
                var entry = JsonUtility.FromJson<PauseDiagnosticRecord>(File.ReadAllLines(Path.Combine(directory, "pause-events.jsonl")).Single());
                Assert.That(entry.schemaVersion, Is.EqualTo(1), "The event DTO must retain inherited heartbeat header fields.");
                Assert.That(entry.runId, Is.EqualTo("test-run"));
                Assert.That(entry.focus, Is.True);
                Assert.That(File.Exists(Path.Combine(directory, "heartbeat.tmp")), Is.False);
                Assert.That(Directory.GetFiles(directory).Length, Is.EqualTo(2));
            }
            finally
            {
                // Only this test's named files, then its empty directory; no recursive deletion.
                foreach (var file in new[] { "heartbeat.json", "heartbeat.tmp", "pause-events.jsonl" })
                    if (File.Exists(Path.Combine(directory, file))) File.Delete(Path.Combine(directory, file));
                if (Directory.Exists(directory)) Directory.Delete(directory);
            }
        }
    }
}
