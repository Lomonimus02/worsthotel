# Prototype 0.4 phase 0 — baseline

Baseline source `98d0813` (Natural Service 0.3.2), Unity 6000.3.2f1. No 0.4 runtime changes during these runs. Unrelated pending pause-investigation edits are preserved.

- Compile: passed, `Logs/core04-baseline-compile.log`.
- Full EditMode: **316/316 passed**, `Logs/core04-baseline-edit.xml` (6.40 s).
- Full PlayMode: **48/49 passed**, `Logs/core04-baseline-play.xml` (659.69 s). The failing production-heater scenario captured an interpolated transform still at the shelf while its Rigidbody/collider had already reached the test placement in room 104. The collider assertion was retained. The fixture now waits for physical/interpolated pose agreement and aims at collider bounds; focused rerun **1/1 passed** (`Logs/core04-baseline-heater.xml`). All 49 distinct cases have successful evidence; this is not a fresh full 49/49 rerun. The initial full run is not a 49/49 pass.
- Ordinary pause and WAIT tests passed, including `PauseAndControllerLossClearConsentAndReturnHotelClockToNormal`, physical interaction blocking, electrical warning/trip stopping acceleration, and environment pause/reset feedback. This is not evidence that the previously reported long Alt+Tab hang is fixed.
- Fresh released-player SOLO: [report](core04-baseline-solo/runtime-verification.txt), **PASS, Errors=0, ResetVerified=True**, 152.2 s. All three historical shifts completed, all 4/6/6 guests actually reached rooms and paid. This is the historical loop, not a continuous-days acceptance run.
- Fresh two-process localhost LAN: [run](core04-baseline-lan/20260926-210354-7208db38), **host/client PASS, Errors=0**, physical keys, commands, authoritative snapshots, disconnect. This does not verify a remote computer or firewall.
- Player binary for both runs: `Builds/Windows-0.3.2/TheWorstHotelEver.exe`; `WorstHotel.Runtime.dll` SHA256 `2BB387AA6CD2BBCAAA4C0033CC92C3D84C07B7E02047FEBB22E755354659BC63`.

Read-only audits: [guest lifecycle/services](core04-guest-baseline.md), [session/UI/LAN/WAIT](core04-interface-baseline.md), [capacity/maintenance/economy](core04-capacity-baseline.md).

Gate status: **passed** after the focused physical-heater repair check. The existing Alt+Tab hang remains a separately documented, user-deferred investigation: [status](../PAUSE_CRASH_INVESTIGATION.md). A 0.4 delivery must distinguish ordinary pause regression coverage from this unresolved report.


