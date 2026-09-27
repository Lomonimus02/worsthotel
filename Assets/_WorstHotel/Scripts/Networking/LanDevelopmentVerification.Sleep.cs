#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem.LowLevel;

namespace WorstHotel
{
    // Opt-in actual two-process sleep verification. No production sleep command
    // is called here: both processes drive their own private controller through the
    // ordinary local/NGO input path and the authored first-hit mattress surface.
    public sealed partial class LanDevelopmentVerification
    {
        IEnumerator RunSleepHost()
        {
            SleepRequire(NetworkManager.Singleton && NetworkManager.Singleton.IsListening,
                "normal NGO host is listening");
            PrepareQuietSleepHotel();
            WriteStage("host-listening");
            yield return SleepUntil(() => lan.PeerConnected, 35, "normal sleep client connects");
            CheckCameraAndAuthority();
            yield return SleepLocalPadReady();
            var original = session.Simulation;
            var originalRooms = session.Rooms;
            long originalEpoch = lan.Epoch;
            yield return Stage("sleep-client-connected", 12);
            PositionEmptySleepStaff();
            WriteStage("sleep-poses-ready");
            yield return SleepAimLocal(0);
            yield return Stage("sleep-client-initial-aimed", 25);

            yield return TapButton(GamepadButton.South);
            yield return SleepUntil(() => SleepPending(0), 5, "host physical bed press latches only staff0");
            long hostFirstRevision = session.Wait.SleepRevision;
            WriteStage("sleep-host-first-pending");
            yield return Stage("sleep-host-first-observed", 12);
            SleepRequire(SleepPending(0) && session.Wait.SleepRevision == hostFirstRevision,
                "releasing the initiating button preserves the host vote and normal clock");
            WriteStage("sleep-host-release-checked");
            yield return SleepUntil(SleepBoth, 12, "client physical bed completes host-first consent");
            yield return Stage("sleep-client-first-both", 12);
            float beforeSleep = original.Elapsed;
            yield return new WaitForSecondsRealtime(1);
            SleepRequire(SleepBoth() && original.Elapsed > beforeSleep + 2 && Time.timeScale == 1,
                "ordinary host update advances hotel time during sleep without accelerating physics");
            WriteStage("sleep-host-first-measured");
            yield return SleepUntil(() => SleepCleared(StaffWakeReason.StaffCancelled), 12,
                "fresh remote Use wakes both employees");
            WriteStage("sleep-host-first-cancelled");
            yield return Stage("sleep-client-first-done", 12);
            facts.Add("HostFirstConsent=True DistinctBeds=True ReleasePreservesConsent=True FreshUseCancels=True");

            WriteStage("sleep-remote-first-ready");
            yield return SleepUntil(() => SleepPending(1), 12, "remote-first bed press alone leaves hotel at normal speed");
            WriteStage("sleep-remote-first-observed");
            yield return Stage("sleep-client-remote-pending-seen", 12);
            yield return TapButton(GamepadButton.South);
            yield return SleepUntil(SleepBoth, 5, "host physical press completes remote-first consent");
            yield return Stage("sleep-client-remote-both", 12);
            yield return TapButton(GamepadButton.South);
            yield return SleepUntil(() => SleepCleared(StaffWakeReason.StaffCancelled), 5,
                "fresh host Use also wakes both employees");
            WriteStage("sleep-host-remote-cancelled");
            yield return Stage("sleep-client-remote-done", 12);
            facts.Add("RemoteFirstConsent=True HostFreshUseCancels=True");

            yield return TapButton(GamepadButton.South);
            yield return SleepUntil(() => SleepPending(0), 5, "new host consent prepares lease scenario");
            WriteStage("sleep-lease-host-pending");
            yield return SleepUntil(SleepBoth, 12, "fresh remote consent starts lease scenario");
            yield return Stage("sleep-client-lease-both", 12);
            WriteStage("sleep-lease-armed");
            yield return SleepUntil(() => coop.RemoteInputLeaseExpired && SleepCleared(StaffWakeReason.InputExpired),
                8, "actual missing remote input expires the host lease and clears both votes");
            SleepRequire(lan.PeerConnected && NetworkManager.Singleton.IsListening,
                "lease expiry occurs without transport disconnection");
            WriteStage("sleep-host-lease-expired");
            yield return Stage("sleep-client-lease-renewed", 8);
            yield return SleepUntil(() => !coop.RemoteInputLeaseExpired, 5, "ordinary neutral input renews the lease");
            SleepRequire(SleepCleared(StaffWakeReason.InputExpired), "renewed input cannot silently resume sleep");
            WriteStage("sleep-host-lease-restored");
            yield return Stage("sleep-client-lease-done", 12);
            facts.Add("LeaseExpiryClearsSleep=True LeaseRenewalDoesNotResume=True");

            yield return TapButton(GamepadButton.South);
            yield return SleepUntil(() => SleepPending(0), 5, "fresh host vote prepares real disconnect");
            WriteStage("sleep-disconnect-host-pending");
            yield return SleepUntil(SleepBoth, 12, "both physical votes exist before disconnect");
            yield return Stage("sleep-client-disconnect-both", 12);
            WriteStage("sleep-disconnect-armed");
            yield return SleepUntil(() => !lan.PeerConnected && SleepCleared(StaffWakeReason.DeviceUnavailable),
                15, "real transport disconnect clears shared sleep");
            SleepRequire(ReferenceEquals(original, session.Simulation) && ReferenceEquals(originalRooms, session.Rooms) &&
                lan.Epoch == originalEpoch, "host hotel and epoch survive the client's disconnect");
            WriteStage("sleep-host-disconnect-verified");
            yield return SleepUntil(() => lan.PeerConnected, 35, "client rejoins through normal connection flow");
            SleepRequire(SleepNone() && session.Simulation.Clock.Speed == 1 &&
                ReferenceEquals(original, session.Simulation) && lan.Epoch == originalEpoch,
                "rejoin preserves the host hotel and requires fresh consent");
            WriteStage("sleep-host-rejoined");
            yield return Stage("sleep-client-rejoin-aimed", 30);
            facts.Add("DisconnectClearsSleep=True RejoinRequiresFreshConsent=True HostHotelPreservedOnRejoin=True");
            yield return RunSleepCriticalHost();

            yield return TapButton(GamepadButton.South);
            yield return SleepUntil(() => SleepPending(0), 5, "physical pending vote exists before NewGame");
            WriteStage("sleep-before-reset-pending");
            yield return Stage("sleep-client-reset-ready", 12);
            session.NewGame();
            SleepRequire(!ReferenceEquals(original, session.Simulation) && lan.Epoch > originalEpoch && SleepNone() &&
                session.Simulation.Clock.Speed == 1, "normal NewGame immediately replaces hotel/epoch and clears sleep");
            PrepareQuietSleepHotel();
            PositionEmptySleepStaff();
            WriteStage("sleep-host-new-epoch-ready");
            yield return SleepAimLocal(0);
            yield return Stage("sleep-client-new-epoch-aimed", 30);
            yield return TapButton(GamepadButton.South);
            yield return SleepUntil(() => SleepPending(0), 5, "host uses a real bed in the fresh epoch");
            long beforeStaleRevision = session.Wait.SleepRevision;
            WriteStage("sleep-old-epoch-test-ready");
            yield return Stage("sleep-client-old-epoch-sent", 12);
            long sentAfterSequence = long.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(output,
                "sleep-old-epoch-sent-sequence.txt")), System.Globalization.CultureInfo.InvariantCulture);
            // Observe later ordinary reliable input, so a lack of activity alone is not
            // mistaken for rejection of the deliberately stale envelope.
            yield return SleepUntil(() => ReadSleepSequence(coop, "lastRemoteSequence") > sentAfterSequence + 3,
                5, "fresh ordinary input continues after the old-epoch envelope");
            float staleWindow = Time.realtimeSinceStartup + .6f;
            while (Time.realtimeSinceStartup < staleWindow)
            {
                SleepRequire(SleepPending(0) && session.Wait.SleepRevision == beforeStaleRevision,
                    "old epoch Use does not alter bed consent, revision, or speed");
                yield return null;
            }
            WriteStage("sleep-host-old-epoch-rejected");
            yield return SleepUntil(SleepBoth, 12, "subsequent genuine current-epoch client Use still works");
            yield return Stage("sleep-client-fresh-epoch-both", 12);
            float morning = session.Wait.SleepUntil;
            var finalModel = session.Simulation;
            long finalEpoch = lan.Epoch;
            WriteStage("sleep-final-morning-armed");
            // No clock advance here: this final short quiet night uses ordinary host
            // Update plus the production sleep multiplier and the real 06:00 clamp.
            yield return SleepUntil(() => SleepCleared(StaffWakeReason.Morning), 90,
                "ordinary sleeping host reaches 06:00 and wakes both employees");
            SleepRequire(ReferenceEquals(finalModel, session.Simulation) && lan.Epoch == finalEpoch &&
                finalModel.Elapsed >= morning && finalModel.Elapsed <= morning + 2 && finalModel.ReportSequence == 1,
                "morning wake retains the hotel and posts its single accounting boundary");
            WriteStage("sleep-host-morning-verified");
            yield return Stage("sleep-client-morning-verified", 12);
            facts.Add("NewGameClearsSleep=True OldEpochInputRejected=True CurrentEpochInputWorks=True MorningWake=True");
            facts.Add("SleepFixturesVerified=True ProductionContinuous=True OwnedLocalPads=True SnapshotSleepAuthority=True");
            facts.Add("ModelBytes=" + lan.LastModelBytes + " WorldBytes=" + lan.LastWorldBytes);
            WriteStage("sleep-host-complete");
            yield return Stage("client-complete", 12);
        }

        IEnumerator RunSleepClient()
        {
            yield return SleepUntil(() => lan.PeerConnected && lan.HasSnapshot, 35, "client receives initial host hotel");
            CheckCameraAndAuthority();
            yield return SleepLocalPadReady();
            SleepRequire(session.IsLanReplica && session.Simulation.IsReadOnlyMirror && session.Simulation.ContinuousOperations,
                "sleep fixture uses the production continuous read-only mirror");
            long originalEpoch = lan.Epoch;
            float before = session.Simulation.Elapsed;
            session.Simulation.Tick(3);
            SleepRequire(session.Simulation.Elapsed == before, "direct client model Tick cannot advance time");
            StartSleepClockObservation();
            WriteStage("sleep-client-connected");
            yield return Stage("sleep-poses-ready", 15);
            yield return SleepUntil(() => Horizontal(coop.Players[1].transform.position, SleepBed(1).standingAnchor.position) < .15f,
                8, "empty staff approach reaches client through actual world snapshots");
            yield return SleepAimLocal(1);
            if (capture) yield return Capture("client-sleep-staff-room");
            WriteStage("sleep-client-initial-aimed");
            yield return Stage("sleep-host-first-pending", 12);
            yield return SleepUntil(() => SleepPending(0), 6, "client sees host-only readiness with normal speed");
            if (capture) yield return Capture("client-sleep-host-ready");
            WriteStage("sleep-host-first-observed");
            yield return Stage("sleep-host-release-checked", 12);
            yield return TapButton(GamepadButton.South);
            yield return SleepUntil(SleepBoth, 8, "host-first consent returns as authoritative sleep view");
            if (capture) yield return Capture("client-sleep-both-ready");
            SleepRequire(SleepBoth() && Time.timeScale == 1, "release preserves both votes and unscaled local physics");
            WriteStage("sleep-client-first-both");
            yield return Stage("sleep-host-first-measured", 12);
            yield return TapButton(GamepadButton.South);
            yield return SleepUntil(() => SleepCleared(StaffWakeReason.StaffCancelled), 8, "fresh client Use clears both replicated votes");
            yield return Stage("sleep-host-first-cancelled", 12);
            if (capture) yield return Capture("client-sleep-wake-cancelled");
            WriteStage("sleep-client-first-done");
            facts.Add("HostFirstConsent=True DistinctBeds=True ReleasePreservesConsent=True FreshUseCancels=True");

            yield return Stage("sleep-remote-first-ready", 12);
            yield return TapButton(GamepadButton.South);
            yield return SleepUntil(() => SleepPending(1), 8, "remote-only readiness returns with normal clock");
            yield return Stage("sleep-remote-first-observed", 12);
            WriteStage("sleep-client-remote-pending-seen");
            yield return SleepUntil(SleepBoth, 8, "host Use completes remote-first sleep");
            WriteStage("sleep-client-remote-both");
            yield return Stage("sleep-host-remote-cancelled", 12);
            yield return SleepUntil(() => SleepCleared(StaffWakeReason.StaffCancelled), 8, "client receives host fresh-Use wake");
            WriteStage("sleep-client-remote-done");
            facts.Add("RemoteFirstConsent=True HostFreshUseCancels=True");

            yield return Stage("sleep-lease-host-pending", 12);
            yield return SleepUntil(() => SleepPending(0), 8, "lease round begins with fresh host-only readiness");
            yield return TapButton(GamepadButton.South);
            yield return SleepUntil(SleepBoth, 8, "lease round has two fresh bed consents");
            WriteStage("sleep-client-lease-both");
            yield return Stage("sleep-lease-armed", 12);
            Queue(default);
            // Only this client's outgoing input Update stops. NGO/transport and registered
            // snapshot handlers remain alive. Finally restores it even on coroutine disposal.
            try
            {
                lan.enabled = false;
                yield return new WaitForSecondsRealtime(1.1f);
            }
            finally { if (lan) lan.enabled = true; }
            WriteStage("sleep-client-lease-renewed");
            yield return Stage("sleep-host-lease-expired", 8);
            yield return Stage("sleep-host-lease-restored", 8);
            yield return SleepUntil(() => SleepCleared(StaffWakeReason.InputExpired), 8,
                "client receives lease wake and neutral renewal does not restore votes");
            SleepRequire(lan.PeerConnected, "lease fixture kept the real transport connection");
            WriteStage("sleep-client-lease-done");
            facts.Add("LeaseExpiryClearsSleep=True LeaseRenewalDoesNotResume=True");

            yield return Stage("sleep-disconnect-host-pending", 12);
            yield return SleepUntil(() => SleepPending(0), 8, "disconnect round requires another host bed press");
            yield return TapButton(GamepadButton.South);
            yield return SleepUntil(SleepBoth, 8, "sleep was active before real transport shutdown");
            WriteStage("sleep-client-disconnect-both");
            yield return Stage("sleep-disconnect-armed", 12);
            Queue(default);
            NetworkManager.Singleton.Shutdown();
            yield return SleepUntil(() => !lan.PeerConnected && lan.MenuOpen, 15, "client observes actual disconnect");
            yield return Stage("sleep-host-disconnect-verified", 15);
            float disconnectedTime = session.Simulation.Elapsed;
            session.Simulation.Tick(3);
            yield return new WaitForSecondsRealtime(.3f);
            SleepRequire(session.IsLanReplica && session.Simulation.IsReadOnlyMirror && session.Simulation.Elapsed == disconnectedTime &&
                coop.Players.All(player => player && !player.HasWorldAuthority && !player.BodyCollider.enabled),
                "disconnected mirror stays frozen without acquiring world authority");
            if (capture) yield return Capture("client-sleep-disconnected");
            clockTracking = false; // Deliberate model replacement, not a clock-authority exception.
            lan.LeaveToMenu();
            yield return SleepUntil(() => lan.Role == LanRole.Offline && lan.MenuOpen &&
                !NetworkManager.Singleton.IsListening && !NetworkManager.Singleton.ShutdownInProgress,
                12, "normal LeaveToMenu completes transport shutdown");
            SleepRequire(lan.Join("127.0.0.1"), "normal Join accepts the same listening host");
            yield return SleepUntil(() => lan.PeerConnected && lan.HasSnapshot && coop.Players[1], 30,
                "normal rejoin returns a fresh read-only replica");
            yield return SleepLocalPadReady();
            yield return Stage("sleep-host-rejoined", 12);
            yield return SleepUntil(SleepNone, 8, "reconnected mirror contains no stale bed consent");
            SleepRequire(lan.Epoch == originalEpoch && session.Simulation.IsReadOnlyMirror && session.Simulation.Clock.Speed == 1,
                "rejoin keeps the host epoch but does not resume sleeping");
            StartSleepClockObservation();
            yield return SleepAimLocal(1);
            if (capture) yield return Capture("client-sleep-rejoined");
            WriteStage("sleep-client-rejoin-aimed");
            facts.Add("DisconnectClearsSleep=True DisconnectedReadOnly=True RejoinRequiresFreshConsent=True HostHotelPreservedOnRejoin=True");
            yield return RunSleepCriticalClient();

            yield return Stage("sleep-before-reset-pending", 12);
            yield return SleepUntil(() => SleepPending(0), 8, "client actually sees pending consent before NewGame");
            clockTracking = false;
            WriteStage("sleep-client-reset-ready");
            yield return Stage("sleep-host-new-epoch-ready", 12);
            yield return SleepUntil(() => lan.Epoch > originalEpoch && lan.HasSnapshot && SleepNone(), 12,
                "new host epoch clears old votes before accepting any new input");
            SleepRequire(session.Simulation.IsReadOnlyMirror && session.Simulation.Clock.Speed == 1,
                "fresh hotel mirror remains read-only at normal speed");
            StartSleepClockObservation();
            yield return SleepUntil(() => Horizontal(coop.Players[1].transform.position, SleepBed(1).standingAnchor.position) < .15f,
                8, "fresh epoch empty staff approach is replicated");
            yield return SleepAimLocal(1);
            if (capture) yield return Capture("client-sleep-fresh-epoch");
            WriteStage("sleep-client-new-epoch-aimed");
            yield return Stage("sleep-old-epoch-test-ready", 12);
            yield return SleepUntil(() => SleepPending(0), 8, "host-only consent is vulnerable to an incorrectly accepted stale Use");
            SendOldEpochSleepInput(originalEpoch);
            WriteText("sleep-old-epoch-sent-sequence.txt", ReadSleepSequence(lan, "inputSequence").ToString(System.Globalization.CultureInfo.InvariantCulture));
            WriteStage("sleep-client-old-epoch-sent");
            yield return Stage("sleep-host-old-epoch-rejected", 12);
            SleepRequire(SleepPending(0), "old epoch input did not create remote bed consent");
            yield return TapButton(GamepadButton.South);
            yield return SleepUntil(SleepBoth, 8, "fresh actual client input still works after rejected old epoch");
            WriteStage("sleep-client-fresh-epoch-both");
            yield return Stage("sleep-final-morning-armed", 12);
            float morning = session.Wait.SleepUntil;
            var finalMirror = session.Simulation;
            long finalEpoch = lan.Epoch;
            yield return Stage("sleep-host-morning-verified", 95);
            yield return SleepUntil(() => SleepCleared(StaffWakeReason.Morning), 8, "06:00 wake reaches read-only client view");
            SleepRequire(ReferenceEquals(finalMirror, session.Simulation) && lan.Epoch == finalEpoch &&
                session.Simulation.Elapsed >= morning && session.Simulation.ReportSequence == 1,
                "morning returns the same client hotel with one accounting report");
            if (capture) yield return Capture("client-sleep-morning");
            WriteStage("sleep-client-morning-verified");
            yield return Stage("sleep-host-complete", 12);
            SleepRequire(stableClockChecks >= 10, "mirror clock was constant between actual host model snapshots");
            facts.Add("NewGameClearsSleep=True OldEpochInputRejected=True CurrentEpochInputWorks=True MorningWake=True");
            facts.Add("SleepFixturesVerified=True ProductionContinuous=True OwnedLocalPads=True SnapshotSleepAuthority=True");
            facts.Add("ReadOnlyMirror=True ReadOnlyTickRejected=True SnapshotOnlyClockChecks=" + stableClockChecks);
            WriteStage("client-complete");
        }

        void PrepareQuietSleepHotel()
        {
            var model = session.Simulation;
            SleepRequire(host && model.ContinuousOperations && model.AutomaticBookingsEnabled &&
                model.Guests.Count == 0 && model.Reservations.Count == 0,
                "quiet sleep fixture starts before the production sales decisions");
            foreach (var policy in model.RoomSalesPolicies.ToArray())
                SleepRequire(model.SetRoomSalesPolicy(0, policy.RoomId, false, policy.Price, policy.Revision).Success,
                    "labelled normal policy closes room " + policy.RoomId + " for this quiet sleep fixture");
            AdvanceContinuousDiagnosticTo(model.Calendar.At(1, 18), "quiet staff-sleep evening setup");
            SleepRequire(model.Guests.Count == 0 && model.Reservations.Count == 0 && !model.Boiler.Failed &&
                model.Electrical.Circuits.All(circuit => !circuit.Tripped), "quiet hotel has no invented guest or hardware emergency");
            facts.Add("SLEEP FIXTURE SETUP: production continuous rules, ordinary room sales policies closed before timed demand, bounded host diagnostic advance to D1 18:00; original production cash/settings. No guest cohort, economy balance, natural workload or real OS focus claim.");
        }

        void PositionEmptySleepStaff()
        {
            SleepRequire(host && SleepNone(), "only host prepares empty approaches while nobody is sleeping");
            for (int actorId = 0; actorId < 2; actorId++)
            {
                var actor = coop.Players[actorId]; var bed = SleepBed(actorId);
                SleepRequire(actor && !actor.Interactor.HeldBody && bed.standingAnchor, "authored bed has a clear empty staff approach");
                var pose = new GameObject("DIAGNOSTIC empty staff sleep approach " + actorId);
                var forward = bed.InteractionPoint - bed.standingAnchor.position; forward.y = 0;
                pose.transform.SetPositionAndRotation(bed.standingAnchor.position + Vector3.up * .08f, Quaternion.LookRotation(forward));
                actor.ResetToSpawn(pose.transform); Destroy(pose);
            }
            facts.Add("SLEEP PHYSICAL SETUP: empty staff0/1 placed once at authored bed0/1 standing anchors for this epoch. All subsequent bed acquisition/consent/cancel uses each process's own virtual pad and production input/first-hit checks. No sleep API or fabricated guest route callback.");
        }

        StaffBedInteraction SleepBed(int id) => FindObjectsByType<StaffBedInteraction>(FindObjectsSortMode.None).Single(bed => bed.bedId == id);
        bool SleepNone() => session.Wait.Mode == HotelAdvanceMode.None && !session.Wait.HasSleepConsent(0) &&
            !session.Wait.HasSleepConsent(1) && session.Wait.SleepBedId(0) == -1 && session.Wait.SleepBedId(1) == -1 &&
            session.Wait.SleepUntil == 0 && session.Simulation.Clock.Speed == 1;
        bool SleepCleared(StaffWakeReason reason) => SleepNone() && session.Wait.WakeReason == reason;
        bool SleepPending(int actor) => session.Wait.Mode == HotelAdvanceMode.None && session.Wait.HasSleepConsent(actor) &&
            session.Wait.SleepBedId(actor) == actor && !session.Wait.HasSleepConsent(1 - actor) &&
            session.Wait.SleepBedId(1 - actor) == -1 && session.Wait.SleepUntil > session.Simulation.Elapsed &&
            session.Wait.WakeReason == StaffWakeReason.None && session.Simulation.Clock.Speed == 1;
        bool SleepBoth() => session.Wait.IsSleeping && session.Wait.HasSleepConsent(0) && session.Wait.HasSleepConsent(1) &&
            session.Wait.SleepBedId(0) == 0 && session.Wait.SleepBedId(1) == 1 &&
            session.Wait.SleepUntil > session.Simulation.Elapsed && session.Wait.WakeReason == StaffWakeReason.None &&
            session.Simulation.Clock.Speed == WaitController.SleepSpeed;

        IEnumerator SleepLocalPadReady()
        {
            SleepRequire(pad != null && pad.added && pad.enabled && pad.canRunInBackground, "private background-capable sleep pad exists");
            yield return SleepUntil(() => coop.Players[coop.LocalActorId] &&
                ReferenceEquals(coop.Players[coop.LocalActorId].Input.Gamepad, pad), 5, "only this process's local staff reads its owned pad");
            Queue(default); yield return new WaitForSecondsRealtime(.3f);
            facts.Add("SLEEP INPUT: this process owns actor" + coop.LocalActorId + " virtual pad only; host remote input reader is never bound or written by the fixture. Existing hidden-player logical focus adapter is diagnostic, not native OS focus manipulation.");
        }

        IEnumerator SleepAimLocal(int bedId)
        {
            var actor = coop.Players[coop.LocalActorId]; var bed = SleepBed(bedId);
            SleepRequire(!session.Wait.HasSleepConsent(coop.LocalActorId), "look acquisition happens before this staff member consents");
            Queue(default); yield return new WaitForSecondsRealtime(.25f);
            if (!host)
            {
                var mattress = bed.transform.Find("Cot mattress").GetComponent<BoxCollider>();
                // This established helper observes authoritative world sequence and prompt,
                // with finite input doses; its actor1 is exactly the role-based local client.
                yield return ServicesAim(() => bed.InteractionPoint, bed.displayName, intendedSurface: mattress);
            }
            else
            {
                float deadline = Time.realtimeSinceStartup + 10;
                while (Time.realtimeSinceStartup < deadline)
                {
                    var delta = bed.InteractionPoint - actor.PlayerCamera.transform.position;
                    float yaw = Mathf.DeltaAngle(actor.transform.eulerAngles.y, Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg);
                    float pitch = Mathf.DeltaAngle(actor.PlayerCamera.transform.localEulerAngles.x,
                        -Mathf.Atan2(delta.y, new Vector2(delta.x, delta.z).magnitude) * Mathf.Rad2Deg);
                    if (Mathf.Abs(yaw) < 1.3f && Mathf.Abs(pitch) < 1.3f) break;
                    Queue(new GamepadState { rightStick = new Vector2(LookAxis(yaw), -LookAxis(pitch)) });
                    yield return null;
                }
                Queue(default); yield return new WaitForSecondsRealtime(.25f);
                var first = Physics.RaycastAll(actor.PlayerCamera.transform.position, actor.PlayerCamera.transform.forward,
                    actor.Interactor.reach, ~0, QueryTriggerInteraction.Ignore)
                    .Where(hit => !hit.collider.transform.IsChildOf(actor.transform)).OrderBy(hit => hit.distance).FirstOrDefault();
                SleepRequire(actor.Interactor.Focused == bed && first.collider && first.collider.GetComponentInParent<StaffBedInteraction>() == bed,
                    "owned host look reaches the real first-hit cot surface; first=" + (first.collider ? first.collider.name : "none"));
            }
        }

        void StartSleepClockObservation()
        {
            lastClientSequence = lan.AppliedModelSequence;
            lastClientClock = session.Simulation.Clock.SimulationTime; clockTracking = true;
        }

        void SendOldEpochSleepInput(long epoch)
        {
            SleepRequire(!host && epoch > 0 && epoch != lan.Epoch, "negative input belongs to the explicitly previous host epoch");
            // Constructed negative envelope on the real normal input channel. It is not
            // described as a recorded packet, and CaptureLocalInput is never called here
            // because that would consume the real reader's pending input edges.
            var frame = new LanInputFrame { epoch = epoch, sequence = long.MaxValue - 1,
                primaryPressed = true, primaryHeld = true, gamepadLabels = true };
            byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(frame));
            SleepRequire(bytes.Length <= LanProtocol.MaxInputBytes, "negative input uses the ordinary bounded wire shape");
            using (var writer = new FastBufferWriter(bytes.Length + 4, Allocator.Temp))
            {
                writer.WriteValueSafe(bytes.Length); writer.WriteBytesSafe(bytes);
                NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage("hotel/input", NetworkManager.ServerClientId,
                    writer, NetworkDelivery.ReliableFragmentedSequenced);
            }
            facts.Add("NEGATIVE WIRE FIXTURE: constructed old-epoch fresh-Use envelope through actual NGO hotel/input; intentionally high sequence tests both stale rejection and continued valid input. Not a byte-for-byte traffic recording.");
        }

        static long ReadSleepSequence(object owner, string field)
        {
            var info = owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            if (info == null) throw new InvalidOperationException("Sleep diagnostic cannot observe " + field);
            return (long)info.GetValue(owner);
        }

        IEnumerator SleepUntil(Func<bool> condition, float seconds, string operation)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                if (System.IO.File.Exists(System.IO.Path.Combine(output, host ? "client-failed.stage" : "host-failed.stage")))
                    throw new InvalidOperationException("Peer failed during " + operation + "; " + SleepDiagnostic());
                yield return null;
            }
            SleepRequire(condition(), operation);
        }

        void SleepRequire(bool valid, string operation) => Require(valid, operation + (valid ? "" : "; " + SleepDiagnostic()));
        string SleepDiagnostic() => !session || !session.Wait || session.Simulation == null ? "sleep session unavailable" :
            "role=" + lan.Role + " peer=" + lan.PeerConnected + " epoch=" + lan.Epoch + " modelSeq=" + lan.AppliedModelSequence +
            " clock=" + session.Simulation.Elapsed.ToString("R") + " speed=" + session.Simulation.Clock.Speed +
            " sleepRevision=" + session.Wait.SleepRevision + " mode=" + session.Wait.Mode + " wake=" + session.Wait.WakeReason +
            " ready=" + session.Wait.HasSleepConsent(0) + "/" + session.Wait.HasSleepConsent(1) +
            " beds=" + session.Wait.SleepBedId(0) + "/" + session.Wait.SleepBedId(1) +
            " until=" + session.Wait.SleepUntil.ToString("R") + " leaseExpired=" + coop.RemoteInputLeaseExpired +
            " paused=" + coop.IsPaused + " menu=" + lan.MenuOpen + " localActor=" + coop.LocalActorId;
    }
}
#endif
