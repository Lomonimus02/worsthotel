# Pause / Alt+Tab baseline evidence — 2026-09-26

**Confirmed failure class: Windows application hang. Root cause remains unproven.** The user describes SOLO becoming unresponsive when returning to the game after Alt+Tab; the duration is unknown. The preserved artifacts contain no pause/focus timestamps or faulting-thread stack, so they cannot establish whether the failure began while inactive, on return, or later.

Evidence was copied at 17:17–17:20 UTC into [20260926T171733Z](20260926T171733Z/copied-file-manifest.json). Original files were only read. No Unity/player process was started, stopped, paused, focused or controlled. The separately running user-owned PID 32284 (`Builds/Windows333/Windows`) was queried for identity only.

## Latest confirmed occurrence

- [Application Hang event 12554](20260926T171733Z/event-12554.xml), ID 1002: `2026-09-26T17:11:41.1532525Z` (20:11:41 local, UTC+03). Windows reports the application stopped responding and was closed; `HangType=Unknown`.
- Process **15404**, start `2026-09-26T16:51:51.7481226Z`; executable `D:\UnityCache\WorstHotelEver2\Builds\Windows-0.3.1\TheWorstHotelEver.exe`.
- [WER event 12553](20260926T171733Z/event-12553.xml), ID 1001: `2026-09-26T17:11:40.9236563Z`, `AppHangB1`, signature `29b2`, bucket `1496579685325429514`.
- The surviving [Report.wer](20260926T171733Z/wer/AppHang_TheWorstHotelEve_7b5ec26996d2511bea531441fa18452d4877dfde_974e04e5_6b2cb178-fb0d-4ff2-8d9e-7d21934da880/Report.wer) has `EventTime=2026-09-26T17:10:04.8821094Z`. This is the WER timestamp, **not a measured pause entry, focus return or proven first blocked instruction**. Process lifetime must not be called pause duration.
- [Builds junction metadata](20260926T171733Z/builds-junction.json) confirms that the workspace `Builds` directory resolves to `D:\UnityCache\WorstHotelEver2\Builds`; the C: paths in Player.log/modules and D: executable path refer to this layout.
- The current gameplay DLL SHA-256 is `CFF84699B2F858C7E52C4C0D91949425A9CD3DF3822B0B2F75B36B802E41B966`, matching the final 0.3.1 [release-package record](20260926T171733Z/services-release-package.txt). Its last-write timestamp precedes process start. EXE, gameplay DLL and matching PDB were preserved with [binary hashes](20260926T171733Z/binary-copy-manifest.json). This identifies the on-disk release artifacts; no process-memory image survives to hash the loaded DLL independently.
- WER's `6000.3.2.30623` application version and the executable's `6000.3.2f1` product version describe Unity, **not prototype milestone 0.3.2**. The Unity launcher EXE hash is identical in the old Windows333 and current release folders and alone cannot identify gameplay revision.

## Player logs and available stacks

The preserved [Player.log](20260926T171733Z/player/Player.log) is 3454 bytes, last written `2026-09-26T16:51:55Z`, with a Mono path under `Builds/Windows-0.3.1`. It reports Unity `6000.3.2f1 (a9779f353c9b)`, Direct3D 12, NVIDIA GeForce GTX 1650 SUPER, driver `32.0.16.1062`, PhysX and Windows.Gaming.Input initialization. Its final line is `UnloadTime: 0.636100 ms` during startup.

There is **no managed exception, native crash stack, focus/resume trace or shutdown sequence** in this log. No source stack frames can be reported. The startup line `d3d12: failed to query info queue interface (0x80004002)` also appears in the successful [0.3.1 LAN client log](../lan/20260926-164251-96b35795/client-player.log); its presence alone does not identify this hang's cause. Loaded graphics/input/NVIDIA overlay modules in Report.wer are an inventory, not a faulting stack.

The preserved [Player-prev.log](20260926T171733Z/player/Player-prev.log) is 3991 bytes and points to `Builds/Windows333/Windows`. It ends with normal subsystem shutdown and Unity's allocation summary for PID 28240 at `2026-09-26T12:07:36Z`. It is not proof that the still-running protected PID 32284 ended, nor proof of a gameplay memory leak.

## Earlier matching Windows records

The [raw event export](20260926T171733Z/windows-application-events.json) covers Application IDs 1000, 1001, 1002 and 1026 over the previous 14 days, retaining only messages naming this game. Eleven records matched. Four Application Hang / AppHangB1 pairs were found:

| ID 1002 UTC | PID | Process started UTC | Build path suffix |
| --- | ---: | --- | --- |
| 2026-09-24 21:38:54.2168037 | 25588 | 2026-09-24 21:15:00.3234803 | `Builds/Windows` |
| 2026-09-25 10:09:46.0153955 | 37772 | 2026-09-25 09:28:49.5461646 | `Builds/Windows` |
| 2026-09-26 14:08:56.0029094 | 29240 | 2026-09-26 12:25:12.3667935 | `Builds/Windows` |
| 2026-09-26 17:11:41.1532525 | 15404 | 2026-09-26 16:51:51.7481226 | `Builds/Windows-0.3.1` |

Additional records: `AppHangTransient` at September 26 14:41:17 and 14:44:15 UTC; `RADAR_PRE_LEAK_64` at September 24 21:15:21 UTC. The latter is a historical Windows heuristic report, without a retained allocation trace, causal producer or pause correlation. It does not establish increasing memory during this user's pause. No game-matching ID 1000 Application Error or ID 1026 .NET Runtime exception was found in this search window.

Only two game WER archive folders still contained files; both contained `Report.wer` only. All explicitly named temporary attachments from the latest WER record were already absent, including the ETL/XML/text/CSV paths; see [attachment-presence inventory](20260926T171733Z/wer-latest-attached-file-presence.json). No game dump was found in the scoped local CrashDumps directory. Default Unity editor logs and scoped Unity/game crash directories were absent. These are bounded search results, not a claim that no dump exists anywhere on the computer; [searched paths](20260926T171733Z/searched-locations.json) are recorded.

## Required next evidence

The next reproduction needs an owned process with timestamps for real focus loss/return and pause state, plus a hang dump/thread stacks captured while it is unresponsive. Measure simulation/queue/memory state rather than infer growth. The current evidence supports investigating the Alt+Tab hang in the current release, but does not justify a clock, networking, graphics, physics or input fix yet. Reproduction, root cause and fix verification remain open.

All captured files have [SHA-256 inventory](20260926T171733Z/capture-sha256.json); original log timestamps and copy sizes are in the manifests.
