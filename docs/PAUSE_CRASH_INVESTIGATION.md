# Prototype 0.3.2 — pause / Alt+Tab investigation

Status: **investigation incomplete; no root cause established and no crash fix applied**. Updated 26 September 2026. This note is written before any proposed final fix.

## Report and preserved evidence

The user reports SOLO: leave the game using Alt+Tab, remain away **less than five minutes**, then the game fails when switching back. The report concerns focus loss/return, not necessarily the Esc menu. The exact inactive interval is unknown.

Windows confirms an application hang in the released 0.3.1 binary, PID 15404, subsequently closed at 17:11:41 UTC / 20:11:41 Moscow. The surviving WER report is AppHangB1. Its timestamp is not a measured pause-entry or focus-return timestamp. The gameplay DLL on disk matches the final 0.3.1 package record. Earlier matching hang records also exist for older build folders; they do not by themselves establish a shared cause.

The preserved Player.log ends after startup. There is no managed exception, native faulting stack, surviving dump, focus transition trace or measured queue growth. A D3D12 startup warning is also present in a successful LAN run, so it is not treated as causal evidence. A historical RADAR heuristic is not proof of a pause-related leak.

Exact timestamps, scoped searches, binary hashes and copied evidence: [forensic findings](verification/pause-baseline-evidence/FORENSIC_FINDINGS.md). Raw artifacts remain local and are excluded from the source checkpoint.

## Current behavior, before changes

SOLO focus loss calls the existing pause calculation, cancels held interactions and sets `Time.timeScale` to zero. Focus return automatically resumes unless a manual/device pause remains. GameSession returns before adding its frame delta to the simulation accumulator while paused. Hotel-time guest schedules, services and promises therefore stop through this caller. This source observation does not prove native focus stability or exclude a runtime burst.

Guest navigation uses authored routes rather than NavMesh agents. LAN host and client have different focus semantics: losing focus suppresses local input without freezing the peer, whereas the host menu can pause authoritative gameplay. Transport scheduling uses unscaled time. Audio, physics, WAIT and realtime UI deadline details are in the [source audit](verification/pause-baseline-source-audit.md).

Existing automated EXE drivers force application-focus state and use synthetic background-capable input. Their previous successful runs cannot serve as evidence for real Windows Alt+Tab. Existing short callback tests likewise do not cover the reported native window transition.

## Baseline and reproduction matrix

- Initial compilation: PASS, `Logs/natural-baseline-compile.log`.
- Existing EditMode tests: **276/276 PASS**, no skips, `Logs/natural-baseline-editmode.xml`.
- Full existing PlayMode run: pending; no result claimed yet.
- Safe Git source checkpoint: pending completion of the baseline gate.

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

An external sampler should retain process identity, resource trends and heartbeat age. If the main loop stalls, capture thread/process dumps while the exact owned process is still alive. A missing heartbeat is a suspicion requiring investigation, not proof that a particular subsystem failed. Never terminate the separately running user-owned old player (PID 32284).

The failure still needs classification as while inactive, during focus return, or after resumed frames. Once a faulting subsystem is supported by evidence, isolate only that subsystem, record the minimal reproduction and explain the smallest fix here before applying it.

## Root cause, chosen fix and acceptance

**Unknown.** No clock/network/physics/graphics rewrite, arbitrary catch-up cap, memory optimization or exception suppression is justified by the evidence collected so far. No root-cause regression or successful post-fix long-pause test is claimed.

Natural-service implementation follows the crash investigation/fix in the requested development order. The [natural-service audit](verification/natural-service-baseline-design.md) records planned integration boundaries only; it is not an implemented feature or an acceptance result.
