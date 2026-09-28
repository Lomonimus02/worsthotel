#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem.LowLevel;

namespace WorstHotel
{
    // Opt-in causal load fixture immediately before
    // the existing NewGame round; the final quiet-night proof therefore remains clean.
    public sealed partial class LanDevelopmentVerification
    {
        IEnumerator RunSleepCriticalHost()
        {
            var model = session.Simulation; var rooms = session.Rooms; long epoch = lan.Epoch;
            SleepRequire(SleepNone() && model.Guests.Count == 0, "critical wake starts from the quiet rejoined hotel");
            int setupDay = model.Calendar.DayAt(model.Elapsed);
            if (model.Elapsed + 60 >= model.Calendar.At(setupDay, model.Operations.SleepHour))
            {
                // Earlier consent/connection rounds have real, variable duration. A late
                // walk-in is rightly forbidden; prepare a separate next evening before any
                // new consent rather than weakening that production booking guard.
                AdvanceContinuousDiagnosticTo(model.Calendar.At(setupDay + 1, 18), "critical-load fixture next-evening setup");
                facts.Add("CRITICAL CLOCK SETUP: prior network rounds reached the late walk-in cutoff; bounded host advance prepared next18:00 before this fixture's guest booking or bed consent. Hotel/epoch retained; no workload or natural-night claim.");
            }
            SleepRequire(model.DebugSpawnGuest(GuestKind.Budget, 104).Success,
                "labelled dated guest fixture supplies a real occupied-room electrical consumer");
            string reservationId = model.Reservations.Single(item => item.RoomId == 104 && item.Active).Id;
            session.RaiseChanged();
            yield return SleepUntil(() => model.Guests.Any(item => item.GuestId == reservationId), 6,
                "ordinary host tick materializes the dated arrival after its real reservation time");
            var guest = model.Guests.Single(item => item.GuestId == reservationId);
            facts.Add("CRITICAL WAKE SETUP: one labelled DebugSpawnGuest Budget104, then actual production reception and room routes. Model-key pickup/handoff is the sole check-in adapter; no fabricated arrival/anchor callbacks, natural booking or physical key-carry claim.");
            yield return SleepUntil(() => guest.Agent.State == GuestAgentState.WaitingForCheckIn, 45,
                "critical fixture guest physically reaches reception");
            SleepRequire(model.Keys.PickUp(0, 104).Success && model.CheckIn(0, guest.GuestId).Success,
                "labelled model-key handoff checks in the actually waiting guest");
            session.RaiseChanged();
            yield return SleepUntil(() => guest.Agent.InAssignedRoom && guest.Agent.ActivityStaged, 45,
                "critical fixture guest physically reaches its own room/activity anchor");

            var heaters = FindObjectsByType<PortableHeater>(FindObjectsSortMode.None).OrderBy(item => item.heaterId).ToArray();
            SleepRequire(heaters.Length == 2 && heaters.All(item => item.Body && item.State != null && !item.State.SwitchedOn),
                "critical fixture uses the two real authored switched-off heater bodies");
            for (int index = 0; index < heaters.Length; index++)
            {
                var body = heaters[index].Body;
                SleepRequire(coop.Players.All(player => player && player.Interactor.HeldBody != body),
                    "diagnostic placement never moves an employee's carried heater");
                body.position = new Vector3(3.85f, .04f, index == 0 ? 18.6f : 16.0f);
                body.rotation = Quaternion.identity;
                body.linearVelocity = body.angularVelocity = Vector3.zero;
            }
            yield return SleepUntil(() => heaters.All(item => item.State.RoomId == 104 &&
                Vector3.Distance(item.transform.TransformPoint(item.placementCollider.center), item.placementCollider.bounds.center) < .05f),
                4, "ordinary world placement registry assigns both authored heater bodies to room104");
            var circuit = model.Electrical.Find("B"); int trips = circuit.TripCount;
            SleepRequire(!circuit.Tripped && !circuit.Warning && circuit.LoadOverride == null &&
                model.Electrical.Consumers.Any(consumer => consumer.Id == "guest:" + guest.GuestId &&
                    consumer.RoomId == 104 && consumer.CircuitId == "B" && consumer.RequestedLoad > 0),
                "real present room owner supplies positive normal branch demand before the heater load");
            facts.Add("CRITICAL LOAD ADAPTER: two existing heater bodies are placed in room104 without a shelf-to-room carry claim. Their ordinary model switches will be enabled only after both real physical bed consents. No load override, direct stress assignment, ForceTrip or timer advance is used.");
            WriteStage("sleep-critical-load-ready");
            yield return TapButton(GamepadButton.South);
            yield return SleepUntil(() => SleepPending(0), 5, "host physically consents before the load experiment");
            WriteStage("sleep-critical-host-pending");
            yield return SleepUntil(SleepBoth, 12, "remote actual bed input completes critical-round consent");
            yield return Stage("sleep-critical-client-both", 12);
            foreach (var heater in heaters)
                SleepRequire(model.Heaters.SetSwitchedOn(heater.heaterId, true).Success,
                    "labelled external load adapter turns on actual registered " + heater.heaterId);
            model.RefreshElectrical(); session.RaiseChanged();
            SleepRequire(circuit.LoadOverride == null && circuit.ActualRequestedLoad > circuit.Capacity &&
                model.Electrical.Consumers.Count(consumer => consumer.Id.StartsWith("heater:") &&
                    consumer.RoomId == 104 && consumer.CircuitId == "B" && consumer.RequestedLoad > 0) == 2,
                "both authored room heaters plus the present guest really exceed branch capacity");
            WriteStage("sleep-critical-load-started");

            bool warnedWhileSleeping = false;
            float deadline = Time.realtimeSinceStartup + 12;
            while (!circuit.Tripped && Time.realtimeSinceStartup < deadline)
            {
                if (circuit.Warning)
                {
                    SleepRequire(SleepBoth() && Time.timeScale == 1,
                        "ordinary electrical warning is not a critical wake event");
                    warnedWhileSleeping = true;
                }
                yield return null;
            }
            SleepRequire(warnedWhileSleeping, "host actually observes the pre-trip warning while still sleeping");
            yield return SleepUntil(() => circuit.Tripped && SleepCleared(StaffWakeReason.CircuitTrip), 3,
                "sustained actual overload trips the branch and wakes staff with the typed critical reason");
            SleepRequire(circuit.TripCount == trips + 1 && circuit.ActualRequestedLoad > circuit.Capacity &&
                circuit.ActualDeliveredLoad == 0 && heaters.All(item => item.State.SwitchedOn && !item.State.Powered) &&
                ReferenceEquals(model, session.Simulation) && ReferenceEquals(rooms, session.Rooms) && lan.Epoch == epoch &&
                session.Simulation.Clock.Speed == 1 && Time.timeScale == 1,
                "real trip preserves requested consumers/hotel identity and restores normal time speed");
            WriteStage("sleep-critical-host-trip");
            yield return Stage("sleep-critical-client-verified", 15);

            // Explicit model-level cleanup, not a physical panel-interaction claim. Shed
            // actual load first, then use the ordinary breaker model command. The already
            // existing physical NewGame-consent round follows and removes this fixture.
            foreach (var heater in heaters)
                SleepRequire(model.Heaters.SetSwitchedOn(heater.heaterId, false).Success,
                    "labelled critical fixture cleanup switches off " + heater.heaterId);
            model.RefreshElectrical();
            SleepRequire(circuit.Tripped && circuit.RequestedLoad <= circuit.Capacity,
                "removing actual heater load does not itself reset the tripped breaker");
            SleepRequire(model.ResetCircuit(0, "B").Success, "ordinary model breaker reset follows load shedding");
            session.RaiseChanged();
            SleepRequire(circuit.HasPower && SleepNone(), "cleanup leaves normal power and no automatic sleep consent");
            facts.Add("CRITICAL CLEANUP: explicitly labelled model switch-off of the same two heaters, then ordinary model ResetCircuit. No physical reset-lever interaction is claimed. The following normal NewGame isolates the final quiet-night proof.");
            WriteStage("sleep-critical-cleaned");
            yield return Stage("sleep-critical-client-cleaned", 12);
            facts.Add("CriticalWarningKeptSleep=True ActualHeaterLoadTrip=True CriticalWakeMirrored=True TripWakeNormalSpeed=True");
        }

        IEnumerator RunSleepCriticalClient()
        {
            var mirror = session.Simulation; long epoch = lan.Epoch;
            yield return Stage("sleep-critical-load-ready", 120);
            yield return SleepUntil(() => mirror.Electrical.Consumers.Any(consumer => consumer.Id.StartsWith("guest:") &&
                consumer.RoomId == 104 && consumer.CircuitId == "B" && consumer.RequestedLoad > 0) &&
                mirror.Heaters.Items.Count(item => item.RoomId == 104 && !item.SwitchedOn) == 2,
                8, "actual guest consumer and two registered heater placements reach the read-only mirror");
            yield return Stage("sleep-critical-host-pending", 12);
            yield return SleepUntil(() => SleepPending(0), 8, "client sees host-only vote in the critical round");
            yield return TapButton(GamepadButton.South);
            yield return SleepUntil(SleepBoth, 8, "client physical second cot consent starts shared sleep");
            WriteStage("sleep-critical-client-both");
            yield return Stage("sleep-critical-load-started", 12);
            var circuit = mirror.Electrical.Find("B");
            bool warnedWhileSleeping = false;
            float deadline = Time.realtimeSinceStartup + 15;
            // No screenshot or peer acknowledgement blocks the short warning interval.
            // Observe real incoming snapshots continuously until the authoritative trip.
            while (!circuit.Tripped && Time.realtimeSinceStartup < deadline)
            {
                if (circuit.Warning)
                {
                    SleepRequire(SleepBoth() && Time.timeScale == 1,
                        "replicated noncritical warning keeps the two existing sleep consents");
                    warnedWhileSleeping = true;
                }
                yield return null;
            }
            SleepRequire(warnedWhileSleeping, "client actually received the warning before the trip snapshot");
            yield return Stage("sleep-critical-host-trip", 8);
            yield return SleepUntil(() => circuit.Tripped && SleepCleared(StaffWakeReason.CircuitTrip), 8,
                "typed CircuitTrip wake and cleared votes arrive in the authoritative snapshot");
            SleepRequire(ReferenceEquals(mirror, session.Simulation) && lan.Epoch == epoch && mirror.IsReadOnlyMirror &&
                circuit.LoadOverride == null && circuit.ActualRequestedLoad > circuit.Capacity && circuit.ActualDeliveredLoad == 0 &&
                mirror.Heaters.Items.Count(item => item.RoomId == 104 && item.SwitchedOn && !item.Powered) == 2 &&
                mirror.Clock.Speed == 1 && Time.timeScale == 1,
                "client observes the actual load/trip, same read-only hotel and normal speed after critical wake");
            if (capture) yield return Capture("client-sleep-critical-wake");
            WriteStage("sleep-critical-client-verified");
            yield return Stage("sleep-critical-cleaned", 12);
            yield return SleepUntil(() => circuit.HasPower && mirror.Heaters.Items.All(item => !item.SwitchedOn) && SleepNone(),
                8, "ordinary load shedding/reset is mirrored without restarting sleep");
            WriteStage("sleep-critical-client-cleaned");
            facts.Add("CriticalWarningKeptSleep=True ActualHeaterLoadTrip=True CriticalWakeMirrored=True TripWakeNormalSpeed=True");
        }
    }
}
#endif
