# Rhythm 0.4.1 — phase 0 baseline

Status: PASS, 27 September 2026. Existing project and runtime checkpoint `d7ce3fc`; new development branch `codex/prototype-0.4.1`. Request copied verbatim to [RHYTHM041_REQUEST.txt](../RHYTHM041_REQUEST.txt); ordered work is tracked in [RHYTHM041_PLAN.md](../RHYTHM041_PLAN.md).

## Preserved state

- Windows0.4 build and complete ZIP remain unchanged. Gameplay DLL SHA256 `16FB043BBBEB33DF0435B223900F9724200AC6EFBE6D765444B86648D47EFF48`; archive `CD3DA33FE4E64EE35DDD7FFA3617DD688E383C23DC64BFF9F21F14937631C8D0`.
- The existing user modification to `tools/Watch-PauseInvestigation.ps1` and unrelated untracked evidence/shortcuts are preserved. Do not stage or revert them as this milestone's work.
- No runtime/scene changes are made during the baseline. Root alone launches Unity/players. Parallel agents inspect existing code and prepare isolated notes; proposed mechanics/tests are not recorded as implemented or passed.

## Fresh evidence

- Compilation during fresh full EditMode passed. **614/614 PASS**,0 failed/skipped,200.18 seconds: [fresh results](rhythm041-baseline-editmode.xml), local log `Logs/rhythm041-baseline-edit.log`. This includes continuous calendar, accounting, services, capacity, paid work, upgrades, validated snapshots and the production-seeded comparison. It does not prove new0.4.1 mechanics.
- Full PlayMode **75/75 PASS**,0 failed/skipped,884.69 seconds: [fresh results](rhythm041-baseline-playmode.xml), local log `Logs/rhythm041-baseline-play.log`. Actual continuous pause/resume without wall-clock catch-up, independent WAIT votes, report/event stop and ordinary physics all passed alongside physical carrying, repairs and guest service routes.
- Fresh continuous Windows SOLO: **PASS**,0 errors,297.31 seconds. [Report](rhythm041-baseline-solo/runtime-verification.txt):72 hotel hours plus checkout tail,14 actual guest room arrivals/departures/receipts,3 reports,conserved cash3130,one natural late boiler failure and no circuit trips. One explicitly timed model staff adapter handles keys/linen/repair; real guest routes are not injected. Full NewGame inventory reset passed.
- Same unchanged binary, subsequent two-process localhost LAN: **host92/client210 PASS**,0 errors. [Host](rhythm041-baseline-lan/20260927-103603-afdab967/host-report.txt), [client](rhythm041-baseline-lan/20260927-103603-afdab967/client-report.txt): real client controller ledger access, booking/edit/cancel/stale rejection, equipment purchases and maintenance downtime, three reporting boundaries,2401 read-only-clock observations. Starting funds8000, forced faults and bounded host clock advances are declared diagnostic setup; this is not natural three-day LAN gameplay or a second-PC test.
- Independently viewed fresh SOLO `continuous-day3-guests.png` and `continuous-final-boiler.png`, and LAN `client-continuous-maintenance-active.png`: readable HUD, physical boiler gauge/labels and maintenance UI. The third-day image directly shows95%/Strained with100%heat, confirming the source finding. The remaining captured images are preserved as candidates, not claimed visually reviewed. Rechecked 0.4 DLL/ZIP SHA256: both unchanged.
- Existing pause/WAIT/calendar physical tests are part of the full PlayMode gate. This is distinct from the old unresolved native Alt+Tab hang.

## Source investigation

- [Boiler/electrical interaction](../BOILER_POWER_INTERACTION.md): no direct boiler-to-circuit consumer, pump, automatic heater activation or shared failure flag found. A physically switched heater can cause an actual electrical cascade; prior user-session cause remains unknown without its state trace.
- [Capacity/thermal baseline](rhythm041-capacity-baseline.md): Strained load below capacity currently retains full heat; the exact existing thermal target explains candidate readings around18/25 without proving the cause of a particular unrecorded session. Full maintenance already permits a functioning boiler; Basic service and readable inspection are missing.
- [Guest/schedule baseline](rhythm041-guest-baseline.md): no natural early checkout, repeated evening activity list after shared morning wake, and a past-sleep-time check blocking morning outings. Preserve real routes and one-night stays.
- [Interface/booking/sleep baseline](rhythm041-interface-baseline.md): concrete reservations can support scheduled automatic sales and legal future reassignment. Sleep needs distinct clock ownership because two existing WAIT/event paths stop on every general event.

## Explicit limits

The request calls the prior long-pause issue fixed; project evidence records it as unresolved and explicitly deferred. Ordinary pause/WAIT regression success cannot retroactively establish its root cause or non-regression. Preserve [the investigation](../PAUSE_CRASH_INVESTIGATION.md). Do not modify computer-use tooling or close user game processes for this work.

No human feel/rhythm, second-PC connectivity, FPS or new feature acceptance is claimed from these baseline checks.
