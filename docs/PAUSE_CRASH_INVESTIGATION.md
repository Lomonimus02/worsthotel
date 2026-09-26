# Prototype 0.3.2 — pause / Alt+Tab investigation

Status: **deferred at the user's explicit request; no root cause established and no crash fix applied**. Updated 26 September 2026. The user instructed us to leave the hang investigation and continue the other Natural Service requirements. The previous investigation-first implementation gate is therefore superseded; preserved evidence remains available.

## Report and preserved evidence

The user reports SOLO: leave the game using Alt+Tab, remain away **less than five minutes**, then the game fails when switching back. The report concerns focus loss/return, not necessarily the Esc menu. The exact inactive interval is unknown.

Windows confirms an application hang in the released 0.3.1 binary, PID 15404, and recorded the stopped-responding/closed event at 17:11:41 UTC / 20:11:41 Moscow. The surviving WER report is AppHangB1. These event timestamps are not measured pause-entry, focus-return or process-close instants. The gameplay DLL on disk matches the final 0.3.1 package record. Earlier matching hang records also exist for older build folders; they do not by themselves establish a shared cause.

The preserved Player.log ends after startup. There is no managed exception, native faulting stack, surviving dump, focus transition trace or measured queue growth. A D3D12 startup warning is also present in a successful LAN run, so it is not treated as causal evidence. A historical RADAR heuristic is not proof of a pause-related leak.

Exact timestamps, scoped searches, binary hashes and copied evidence: [forensic findings](verification/pause-baseline-evidence/FORENSIC_FINDINGS.md). Raw artifacts remain local and are excluded from the source checkpoint.

## Current behavior, before changes

SOLO focus loss calls the existing pause calculation, cancels held interactions and sets `Time.timeScale` to zero. Focus return automatically resumes unless a manual/device pause remains. GameSession returns before adding its frame delta to the simulation accumulator while paused. Hotel-time guest schedules, services and promises therefore stop through this caller. This source observation does not prove native focus stability or exclude a runtime burst.

Guest navigation uses authored routes rather than NavMesh agents. LAN host and client have different focus semantics: losing focus suppresses local input without freezing the peer, whereas the host menu can pause authoritative gameplay. Transport scheduling uses unscaled time. Audio, physics, WAIT and realtime UI deadline details are in the [source audit](verification/pause-baseline-source-audit.md).

Existing automated EXE drivers force application-focus state and use synthetic background-capable input. Their previous successful runs cannot serve as evidence for real Windows Alt+Tab. Existing short callback tests likewise do not cover the reported native window transition.

## Baseline and reproduction matrix

- Initial compilation: PASS, `Logs/natural-baseline-compile.log`.
- Existing EditMode tests: **276/276 PASS**, no skips, `Logs/natural-baseline-editmode.xml`.
- Full existing PlayMode run: **43/44 PASS**, one failure, no skips, `Logs/natural-baseline-playmode.xml` (512.5763497 test seconds). `TwoAuthoredHeatersOverloadProductionCircuitAndActualResetRetripsUntilOneIsSwitchedOff` failed at the physical `FaceStation` focus assertion (Portable heater; actual focus null). Its unchanged targeted repeat also failed, `Logs/natural-baseline-heaters-repeat.xml`. This is a baseline test failure, not a reproduced Alt+Tab hang or a complete green suite.
- Safe Git source checkpoint: `5f41b12`, working 0.3.1 source/configuration/tools/documentation, excluding generated test scenes, builds, caches and forensic/screenshot artifacts. Further work is on `codex/prototype-0.3.2`. The commit preceded completion of the full PlayMode run; it does not claim its result.

| Mode / trigger | Duration | Context | Result |
| --- | --- | --- | --- |
| SOLO real Alt+Tab out / return | 1 minute | Planning / idle | Pending |
| SOLO real Alt+Tab out / return | 5 minutes | Active shift / guest activity | Pending |
| SOLO real Alt+Tab out / return | 10+ minutes | Active service / infrastructure warning | Pending |
| SOLO real Alt+Tab out / return | 20+ minutes, if practical | Long idle | Pending |
| SOLO Esc pause / resume | Representative long interval | Active shift | Pending; distinct from focus loss |
| LAN host menu pause / resume | 10+ minutes | Active host and connected peer | Pending |
| LAN client inactive / return | Representative long interval | Connected client | Pending; host progression is expected |

Real Windows input automation is currently unavailable: the installed computer-use `node_repl` runtime fails before initialization with `failed to write kernel assets: The system cannot find the path specified. (os error 3)`, including after a kernel reset. No real Alt+Tab reproduction has been claimed. No alternative focus injection is presented as an equivalent native test.

## Evidence required next

Use a separate owned development player, explicit Player.log path, and opt-in diagnostic logging. Preserve pause/focus entry, 30-second samples, exit and the first resumed frames; measure hotel time, accumulator, completed ticks, bounded model collections, memory and network role. Do not alter focus, device policy, timers or simulation behavior in this observer.

An external sampler should retain process identity, resource trends and heartbeat age. A small main-loop heartbeat is separate from the 30-second rich snapshots and should update every two seconds, so a 20-second stale threshold cannot confuse ordinary snapshot spacing with a stall. If the main loop stalls, capture thread/process dumps while the exact owned process is still alive. A missing heartbeat is a suspicion requiring investigation, not proof that a particular subsystem failed. Never terminate the separately running user-owned old player (PID 32284).

The failure still needs classification as while inactive, during focus return, or after resumed frames. Once a faulting subsystem is supported by evidence, isolate only that subsystem, record the minimal reproduction and explain the smallest fix here before applying it.

## Root cause, chosen fix and acceptance

**Unknown.** No clock/network/physics/graphics rewrite, arbitrary catch-up cap, memory optimization or exception suppression is justified by the evidence collected so far. No root-cause regression or successful post-fix long-pause test is claimed.

Natural-service implementation follows the crash investigation/fix in the requested development order. The [natural-service audit](verification/natural-service-baseline-design.md) records planned integration boundaries only; it is not an implemented feature or an acceptance result.

## Instrumentation implementation and validation

After the baseline, an opt-in `-pauseDiagnostics <directory>` observer was added to development/editor builds. It brackets the original focus callback, records actual pause edges after `Time.timeScale` assignment, counts tick entry/completion without changing the simulation loop, writes a two-second atomic heartbeat, and emits 30-second paused samples plus the first 1/2/3/10/30/60 resumed frames. The first three resumed ticks also have entry/exit records. Device callback detail is bounded while its total count continues. No synthetic input or forced focus is installed.

The external launcher/observer validates the exact new PID, creation FILETIME, resolved executable path and build hashes. It samples CPU/memory and allows at most two thread-oriented dumps after a stale valid heartbeat. It cannot terminate the game. Separate `ToolSmoke` capture is explicitly labelled healthy-process validation and is not crash evidence. Heartbeat reads allow delete sharing so they do not obstruct atomic replacement.

Recorder failures emit one warning and disable diagnostic output only. Original gameplay operations remain outside its exception boundaries. Diagnostic I/O is synchronous and adds observation overhead: a stall inside this instrumentation must be distinguished from the original uninstrumented failure.

Compilation and the extended **284/284 EditMode suite passed**, including eight evidence-recorder tests, no skips (`Logs/natural-diagnostics-editmode.xml`). These verify read-only model/promise capture, sparse sampling, exact unfinished-tick accounting, atomic heartbeat schema and output-failure isolation. They are **not regression tests for an established Alt+Tab root cause**. Built-player/dump smoke is recorded below; real focus reproduction remains pending.

The later targeted PlayMode run passed **2/2** (`Logs/natural-diagnostics-playmode.xml`): the existing SOLO/WAIT/reset/short focus-callback scenario and the heater scenario. Only failure-message observation was added to the heater fixture; its interaction behavior was not changed. The earlier failures therefore remain an intermittent baseline issue with no established cause or fix. This targeted pass is not represented as a fresh full 44/44 pass, and invoking a managed focus callback is not native Alt+Tab reproduction.

The diagnostic Windows build succeeded (`Logs/natural-pause-diagnostic-build.log`), preserving application version 0.3.1 and the existing graphics/input settings. Its folder is `Builds/Windows-0.3.2-pause-diagnostic`; it is not the completed 0.3.2 release. Gameplay DLL SHA-256: `AE02A36C80BC5EFB20B2F2E209179853C37DC0F23F47339C542456FA3DE8BEA4`.

An owned-process smoke, [20260926T175224Z-1ab00351](verification/pause/20260926T175224Z-1ab00351/tool-smoke-validation.json), successfully initialized ordinary SOLO, wrote valid heartbeat/model records, and completed the observer's 60-second limit without a suspected stall or disabled recorder. An explicitly healthy-process dump was then captured: 2,831,607 bytes, valid MDMP header, 97 threads and 106 modules. This validates evidence collection, not native focus stability. Root subsequently stopped only that exact owned PID 33260 after validating its creation time/path; cleanup is recorded separately and is not a crash.

That smoke exposed a diagnostic-tool timing error: PowerShell selected the integer overload of `Math.Max(0, ...)`, causing extra samples during the final fraction of a second. The observer delay was corrected to explicit Double arithmetic and checked in both PowerShell 7 and Windows PowerShell 5.1. This correction affects only the external observer, not game pause or simulation time.

The corrected launcher/observer also passed an actual Windows PowerShell 5.1 smoke, matching the user's shortcut shell: [20260926T175714Z-69085e56](verification/pause/20260926T175714Z-69085e56/observer-ended.json), owned PID 39300. It recorded 15 samples over 30.020 seconds with a live initialized heartbeat, no observer error or suspected stall, then successfully captured an explicitly healthy 2,846,643-byte dump. Root stopped only this exact owned process afterward. These short startup checks are not the requested 1/5/10-minute pause matrix.

The local shortcut **Запустить диагностику Alt-Tab.lnk** invokes the checked launcher with `-Interactive`. The [Russian instructions](PAUSE_DIAGNOSTICS.ru.md) ask for the reported real SOLO Alt+Tab reproduction and a one-minute wait before closing a hung game.

## User-reported frozen picture in the diagnostic build

The user reported another freeze and confirmed both that the **whole picture froze** and that this window was opened through the diagnostic shortcut. Run `20260926T182352Z-754c0b8b` is the interactive SOLO diagnostic process PID 28292, created 2026-09-26T18:23:52.3580188Z. These confirmations are symptom evidence; they do not yet identify a faulting subsystem.

The main-loop heartbeat continued advancing during the reported symptom (frame 17,303 at 18:28:46.9016111Z; 25,862 at 18:31:10.1119401Z; 40,606 at 18:35:16.2519677Z), with `focus=true`, `pause=false` and `initialized=true`. Its trace contains only the successfully completed startup focus callback, not a later native focus-loss/return pair. Rich event recording deliberately becomes sparse after startup, so the last recorded Planning state cannot be interpreted as current gameplay state. A live heartbeat does not disprove a frozen displayed picture.

Two manual symptom-time thread-oriented dumps were saved at 18:30:30–31Z and 18:31:12Z, 2,959,728 and 2,951,952 bytes respectively. Their `UserReportedHang` reason distinguishes them from automatic heartbeat-stall captures and healthy dump-tool smokes. Exact hashes/identity are in `user-reported-dump-1-result.json` and `user-reported-dump-2-result.json` in that run. Neither capture terminated the player. The separately running old player PID 32284 was not controlled.

Preserved logs and interpretation: [user-report forensic note](verification/pause/20260926T182352Z-754c0b8b/FORENSIC_USER_REPORT.md). The source audit confirms that `-hotelSolo` alone does not install a verification driver or synthetic focus; those require separate flags absent from this launch. No graphics, input or simulation fix is justified yet.

The offline `tools/Read-NativeDump.cpp` reader was compiled with the already installed MSVC/Windows SDK and opened both dumps using the Windows DbgEng library and local matching Unity PDB. No live-process attachment was used. Both reports resolve the main thread in `UnityPlayer!HighResolutionTimer::Wait` → `ThreadHelper::SleepInSeconds` → `TimeManager::EndSyncFrame/Sync` → `ExecuteTimeUpdate` → `MainMessageLoop`. The D3D12 submission and graphics worker threads are waiting for stream-buffer input in these samples. CPU totals advance between captures. This is consistent with continuing frame pacing and does not establish a native deadlock, Present stall, or the cause of the frozen picture. Two sampled stacks do not exclude intermittent rendering problems. Debugger extension DLLs for `!analyze -hang` are absent, so no automated hang verdict is claimed. The complete `.native-stacks.txt` reports remain beside the dumps.

The scoped Windows-event/WER audit found no new Application hang/crash or Display/nvlddmkm/DxgKrnl/Dwm-Core records during 18:09:43.7358301–18:39:43.7358301Z. Exact filters, preserved results and limitations: [Windows continuation](verification/pause/20260926T182352Z-754c0b8b/FORENSIC_WINDOWS_CONTINUATION.md). Absence of these events does not disprove the reported visual freeze.

Computer Use was retried after the user asked for direct desktop inspection. Kernel reset succeeded, but initialization again failed before executing the import with `failed to write kernel assets: The system cannot find the path specified. (os error 3)`. The registered Node REPL/Node executables and module directory exist. The exact missing output path is not established, and no screenshot or native input has been obtained through this tool. This separate tool failure must not be described as the game's root cause.

## Computer Use recovery

At the next attempt on 26 September 2026, the installed Computer Use runtime had already changed: skill version `26.924.22138`, `cua_node` runtime `b63ee7ee40c23b77`. Fresh `node_repl` processes PID 33648 and 35972 were created at 21:45:57 Moscow / 18:45:57 UTC; the previously observed Node REPL processes were no longer present. The first initialization in this attempt **succeeded**. Native window enumeration, activation and screenshots also succeeded. The diagnostic game window was initially minimized; after activation its reception scene was captured. A harmless Ctrl+L in the already-open project Explorer window visibly selected the address bar, verifying keyboard input without changing files. Escape was sent afterward. Computer Use is operational in this attempt.

An Escape key sent to the diagnostic game did not visibly open its menu in the immediate screenshot. This is not evidence that keyboard control is broken globally: the Explorer check succeeded, and the game's native focus-loss/pause callbacks were recorded at 18:48:05Z during the checks. The window list also contained a separate `process:dwm.exe` window with the game's title; its relationship to the older player is not established and it was not controlled. The game's freeze investigation and full native Alt+Tab matrix remain incomplete.

No manual Codex configuration edits, temporary-directory recreation or whole-application reset were performed as a repair in this investigation. The runtime changed before this attempt; we did not establish why the previous runtime could not write its kernel assets or attribute recovery to a confirmed fix. This recovery concerns the separate inspection tool, not the game's reported frozen picture. The preceding unavailable-tool statements describe earlier attempts.
