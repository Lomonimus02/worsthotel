# LAN staff sleep diagnostic scope

Prepared 2026-09-27 after phase 7 checkpoint `24f2024`; integrated into the matching runtime files after the parent released the source freeze. The `.cs.txt` and patch files preserve the proposed scope. Do not apply them again to already integrated sources.

Execution is **pending**. This is neither a test result nor a human/two-machine LAN playtest. Only the root agent builds or launches players.

Run mode: `tools/VerifyLAN.ps1 -SleepFixtures -Capture` with an explicitly selected current development build. The wrapper creates two owned hidden EXE processes, a fresh run directory, a 480-second outer timeout, exact binary hashes and fresh reports/captures. The internal watchdog is 420 seconds. Other fixture modes are mutually exclusive.

Both processes own a separate virtual gamepad bound only to their local actor. The host alone positions empty staff once per epoch at the authored bed standing anchors. Ordinary production room-sales policies close all rooms for a quiet test hotel; a labelled bounded diagnostic clock advance prepares D1 18:00. Production cash, sleep rules and physical state are otherwise retained. No `TrySleep`, `CancelSleep`, input-reader mutation, guest-route callback or diagnostic RPC supplies the consent.

Sequence:

1. Host uses the first actual cot. The client observes pending readiness before adding its physical second-cot vote. Releasing the initiating button keeps consent; fresh client Use cancels both. Real hotel time advances through ordinary Update at the sleep multiplier while `Time.timeScale == 1`.
2. Reverse the order. Client-only readiness must be observed before the host's completing press; fresh host Use then cancels.
3. With both votes active, disable only the client's `LanSession` Update for 1.1 seconds. NGO remains connected and snapshot handlers stay registered. Host input-lease expiry clears both votes; renewed neutral input does not restore consent.
4. Shut down the client transport normally, verify the host retains its hotel/epoch, and verify the disconnected mirror stays frozen/read-only. Use ordinary `LeaveToMenu` and `Join` to reconnect; fresh consent is required.
5. Reset the host through normal `NewGame` while a physical vote is pending. The client observes the new epoch with no stale votes. Prepare the labelled fresh quiet evening/empty approaches again.
6. Send an explicitly **constructed negative** old-epoch Use envelope through the genuine bounded `hotel/input` NGO channel. It is not called a captured packet replay. Subsequent ordinary input sequence delivery brackets the check; pending vote/revision/speed stay unchanged. A fresh actual client press must still complete sleep, proving rejection did not poison the current sequence.
7. Let the final sleep reach 06:00 through ordinary host Update, with no diagnostic clock advance during sleep. Both processes must retain the same hotel/epoch and observe normal speed/no votes plus one accounting report.

Client snapshot-only clock checks run across every uninterrupted connection/epoch. They are explicitly rebaselined for intentional `LeaveToMenu` and host `NewGame`, not silently skipped for an unexpected reset.

Eight requested GPU candidates use the existing actual-player native-IMGUI capture path:

- `client-sleep-staff-room.png`
- `client-sleep-host-ready.png`
- `client-sleep-both-ready.png`
- `client-sleep-wake-cancelled.png`
- `client-sleep-disconnected.png`
- `client-sleep-rejoined.png`
- `client-sleep-fresh-epoch.png`
- `client-sleep-morning.png`

The reports and wrapper require each individual evidence flag. PNG existence is not visual acceptance: root must inspect the fresh pixels. The fixture's inherited hidden-player logical-focus adapter is not real OS focus manipulation and provides no evidence about the deferred Alt+Tab hang. Quiet setup provides no workload, balance, natural guest-cohort, graphics-performance or second-computer claim.

Static preparation checks: wrapper PowerShell syntax parsed successfully without executing it; every literal stage wait has a producer; owned diffs pass whitespace checks. Unity compilation and actual execution remain pending.

## Integrated critical-wake extension

`LanDevelopmentVerification.SleepCritical.cs.txt`, its `.meta.txt` and `critical-hooks.patch.txt` were prepared as a separate phase9 draft while phase8 Assets were frozen. They were integrated after phase8 checkpoint `ea7f80e` and explicit source release; do not apply the patch again. The original eight-capture scope above is historical; the combined driver now requires nine captures. Compilation and execution of this extension remain pending.

The extra round runs after normal rejoin and before the existing pending-consent/NewGame round. One explicitly spawned Budget guest follows the actual reception and room104 routes; only the key handoff is a labelled model adapter. Both existing heater bodies are placed in104 and are switched on through a labelled model-level external load adapter only after both employees physically consent at their cots. Two heaters alone equal the4u circuit capacity; the actual present guest supplies the additional positive consumer. No direct trip, load override, synthetic stress/timer, guest arrival callback or physical heater-carry claim is used.

The diagnostic booking first waits for ordinary host ticks to materialize its scheduled `Elapsed+1` arrival by the real reservation ID. If earlier network rounds have already reached the late walk-in cutoff, a separately labelled bounded next-evening advance occurs before booking or consent, retaining the hotel/epoch. It never relaxes the production arrival guard or advances an active sleep round.

Both peers observe the short warning interval continuously without screenshot or blocking acknowledgement: warning must retain Sleep/8×, and the ordinary sustained-overload trip must produce typed `CircuitTrip`, cleared readiness and1×. A ninth real GPU candidate, `client-sleep-critical-wake.png`, is captured only after the wake. Then explicit model-level switch-off/load shedding and ordinary model breaker reset clean the causal fixture; this is not claimed as physical reset-lever input. The subsequent already-tested NewGame prepares a separate empty hotel for the quiet-night proof.

Required new evidence flags on both reports: `CriticalWarningKeptSleep=True`, `ActualHeaterLoadTrip=True`, `CriticalWakeMirrored=True`, `TripWakeNormalSpeed=True`. Draft stage wait/producer pairing is checked statically; compilation, execution and image inspection remain pending.
