# Prototype 0.4.1 — Hotel Rhythm & Pressure

Authoritative scope: [user request](RHYTHM041_REQUEST.txt). Existing-project baseline: `d7ce3fc`, branch `codex/prototype-0.4.1`. Preserve the original modular visual direction and 0.4 release. No new hotel expansion, employee simulation, catalogue of requests or progression tree.

## Phase gates

Follow the requested order. Freeze runtime sources while Unity is compiling/testing or an executable gate is running. Root alone launches Unity and players. Each implementation phase must compile, pass relevant model/physical/network checks, preserve evidence of failures and fixes, and update the changelog before the next phase. Full regression suites run at baseline and final integration. A test's existence is not a PASS result.

| Phase | Scope | Status/evidence |
|---|---|---|
| 0 | Inspect current project; compile, full tests, continuous SOLO/LAN, pause/WAIT; safe checkpoint | PASS:614 Edit,75 Play,fresh SOLO and LAN; [baseline](verification/rhythm041-phase0.md) |
| 1 | Investigate actual boiler/electrical causal chain before changing it; document and fix/expose proven cause | PASS:25 Edit,4 relevant physical scenarios; [cause, failed attempts and trace](BOILER_POWER_INTERACTION.md). New prompt pixels remain final readability gate. |
| 2 | Intermediate boiler bands, pre-failure performance, wear/stress and recovery; no new maintenance yet | PASS:638 Edit,3 physical; measured stress retuning preserved all economic gates. [Evidence](verification/rhythm041-phase2.md) |
| 3 | Readable inspection, configurable Basic Service and Full Service with paid real downtime | Pending |
| 4 | Pre-failure guest consequences, sustained severe escalation and eligible early checkout/once-only refunds | Pending |
| 5 | Automatic scheduled normal bookings, per-room sales policy/prices, adjustable actual assignment | Pending |
| 6 | Natural morning/afternoon/evening/night guest activity and demand; no hard phases/load multipliers | Pending |
| 7 | Small modular staff room, physical beds, event-aware model-clock sleep, two-player agreement/critical wake | Pending |
| 8 | Operations/thermal/debug history and physical feedback readability | Pending |
| 9 | Fresh full suites, several continuous days of safe/aggressive/service/sleep/sales play, SOLO then LAN, verified Windows package | Pending |

## Integration contract

- Reuse the existing authoritative hotel clock, calendar, booking intervals, physical room/key/linen ownership, service intents, repairs and LAN command/snapshot infrastructure.
- All new model fields and commands must replicate atomically, reject invalid/stale/duplicate commands and respect a read-only client. Change protocol/schema when data contracts change.
- High demand causes gradual loss of reserve, useful warning and thermal consequences before failure. Condition/stress/recovery remain causal. No failure scheduled by day, sleep choice or workload count.
- Basic service improves a working boiler partially, at a configurable cost and deadline. Full service remains a stronger paid intervention. Neither instantaneously restores a fully running boiler; heat is unavailable during the job. Preserve the physical emergency patch and upgrades.
- Guest early departure is the end of severe, sustained, personality/history-dependent distress. Use the actual existing departure route and turnover pipeline; account once and preserve future booking/room ownership protections.
- Sales policy controls which rooms can receive future ordinary reservations. Offers convert over time from real configured demand, price and interval availability, not on the same click that opens a room. Closing sales cannot silently cancel existing stays. The player can adjust future room assignments legally.
- Sleep advances the hotel model through ordinary time infrastructure without accelerating Rigidbody physics or resetting days. SOLO needs one bed user; LAN needs both actors' explicit agreement. Morning, cancellation/disconnection and actual critical emergencies wake coherently; minor requests do not.
- Thermal detail must come from the actual computed heat/loss/availability terms. Player surfaces summarize cause; developer views expose values and bounded causal history.
- Distinguish headless adapters, real scene input, actual EXE runs, visual inspection and human playtest judgments. New sleep and booking controls need actual physical/controller and two-process LAN checks, not model tests alone.

## Scope and unresolved history

The request describes the earlier long-pause crash as fixed. The actual project history records an unresolved whole-image freeze after Alt+Tab, explicitly deferred by the user. Preserve that evidence and run existing ordinary pause/WAIT regressions; do not relabel the old native hang fixed without a reproduction and proof. See [PAUSE_CRASH_INVESTIGATION.md](PAUSE_CRASH_INVESTIGATION.md).

Complete this bounded milestone and ask the core exit questions from §89. Human experience and second-PC network quality must remain separately qualified. Do not start Prototype 0.5 features as part of this request.

## Ownership during baseline

Root: phase gates, shared data/contracts, builds, actual executable tests and final delivery. Agency Situations: read-only boiler/power/capacity audit. Agency Life: read-only guest/thermal/schedule audit. Agency Interface: read-only booking/UI/sleep/LAN audit. Implementations will receive explicit file ownership per phase after the preceding gate passes.
