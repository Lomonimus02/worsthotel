# Prototype 0.3.2 — pause / Alt+Tab investigation

Status: **investigation incomplete; no root cause established and no crash fix applied**. Updated 26 September 2026. This note is written before any proposed final fix.

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

The local shortcut **Запустить диагностику Alt-Tab.lnk** invokes the checked launcher with `-Interactive`. The [Russian instructions](PAUSE_DIAGNOSTICS.ru.md) ask for the reported real SOLO Alt+Tab reproduction and a one-minute wait before closing a hung game. No completed human reproduction, causal stack analysis or crash fix is claimed at this point.
