#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;

namespace WorstHotel
{
    [Serializable]
    public class PauseDiagnosticHeartbeat
    {
        public int schemaVersion = 1, processId, frame;
        public string runId, utc;
        public long sequence;
        public double monotonicSeconds;
        public bool focus, pause, initialized;
    }

    [Serializable]
    public sealed class PauseDiagnosticRecord : PauseDiagnosticHeartbeat
    {
        public string kind, detail, version, unityVersion, mode, role, phase, lastDeviceChange;
        public bool bootstrapFocus, manualPause, devicesDirty, waitingForDevices, menuOpen, managementOpen,
            applicationRunInBackground, peerConnected, hasSnapshot, remoteInputLeaseExpired;
        public float timeScale, deltaTime, unscaledDeltaTime, fixedDeltaTime, accumulator;
        public int day, resumedFrame, lastTickFrame, frameTickStarts, frameTickCompletions, inFlightTicks,
            deviceChanges, gc0, gc1, gc2, worldBytes, worldDecodedBytes;
        public long tickStarts, tickCompletions, diagnosticFixedUpdates, managedBytes,
            unityAllocatedBytes, unityReservedBytes, unityUnusedReservedBytes, modelSequence;
        public float lastTickStep;
        public PauseDiagnosticModelState model;
        public PauseDiagnosticInputState[] devices;
    }

    [Serializable]
    public sealed class PauseDiagnosticInputState
    {
        public int id;
        public string layout, name;
        public bool enabled, added, canRunInBackground;
    }

    [Serializable]
    public sealed class PauseDiagnosticGuestState
    {
        public string id, state, activity, location;
        public int room, activityIndex;
        public bool staged;
        public float nextActivityTime, activityEndsAt, stateChangedAt;
    }

    [Serializable]
    public sealed class PauseDiagnosticModelState
    {
        public bool running, replica;
        public float time, speed, reputation;
        public int cash, eventRevision, guestCount, scheduleCount, scheduleEntryCount, pendingActivityDeadlines,
            serviceCases, activeServiceCases, promises, acceptedPromises, missedPromises,
            incidents, activeIncidents, situationHistoryEntries, requests, unresolvedRequests,
            housekeepingTasks, pendingHousekeepingTasks;
        public string lastEvent;
        public PauseDiagnosticGuestState[] guests;

        // Reads the live model without advancing clocks, taking snapshots into it or raising events.
        public static PauseDiagnosticModelState Capture(HotelSimulation simulation)
        {
            if (simulation == null) return null;
            var result = new PauseDiagnosticModelState
            {
                running = simulation.Running, replica = simulation.IsReadOnlyMirror,
                time = simulation.Elapsed, speed = simulation.Clock.Speed,
                cash = simulation.Economy.Cash, reputation = simulation.Economy.Reputation,
                eventRevision = simulation.EventRevision, lastEvent = simulation.LastEvent
            };
            var guests = new List<PauseDiagnosticGuestState>();
            foreach (var guest in simulation.Guests)
            {
                result.guestCount++;
                var agent = guest.Agent;
                if (agent == null) continue;
                if (Number.IsFinite(agent.NextActivityTime)) result.pendingActivityDeadlines++;
                guests.Add(new PauseDiagnosticGuestState { id = guest.GuestId, room = guest.RoomId,
                    state = agent.State.ToString(), activity = agent.CurrentActivity, location = agent.CurrentLocation.ToString(),
                    activityIndex = agent.ActivityIndex, staged = agent.ActivityStaged,
                    nextActivityTime = FiniteTime(agent.NextActivityTime), activityEndsAt = FiniteTime(agent.ActivityEndsAt),
                    stateChangedAt = agent.StateChangedAt });
            }
            result.guests = guests.ToArray();
            if (simulation.Schedules != null)
                foreach (var schedule in simulation.Schedules.Items)
                { result.scheduleCount++; result.scheduleEntryCount += schedule.Activities.Count; }
            if (simulation.Services != null)
            {
                foreach (var item in simulation.Services.Cases)
                { result.serviceCases++; if (item.Active) result.activeServiceCases++; }
                foreach (var promise in simulation.Services.Promises)
                {
                    result.promises++;
                    if (promise.Status == PromiseStatus.Accepted) result.acceptedPromises++;
                    if (promise.Status == PromiseStatus.Missed) result.missedPromises++;
                }
            }
            foreach (var incident in simulation.Incidents.Items)
            {
                result.incidents++; if (incident.Active) result.activeIncidents++;
                result.situationHistoryEntries += incident.History.Count;
            }
            foreach (var request in simulation.Requests.Items)
            { result.requests++; if (!request.Resolved) result.unresolvedRequests++; }
            if (simulation.Housekeeping != null)
                foreach (var task in simulation.Housekeeping.Tasks)
                { result.housekeepingTasks++; if (task.Step != RoomPreparationStep.Ready) result.pendingHousekeepingTasks++; }
            return result;
        }
        static float FiniteTime(float value) => Number.IsFinite(value) ? value : -1;
    }

    public sealed partial class LocalCoopBootstrap
    {
        internal bool DiagnosticFocus => hasFocus;
        internal bool DiagnosticManualPause => manuallyPaused;
        internal bool DiagnosticDevicesDirty => devicesDirty;
    }
}
#endif
