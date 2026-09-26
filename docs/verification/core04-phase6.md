# Core 0.4 phase 6 — Paid patch and timed proper maintenance

Status: gate passed. Phase 5 checkpoint: `006b3f7`.

The existing authenticated SOLO/co-op physical sequence now commits a $200 emergency patch in continuous operations. It restores working heat at 40 percent condition, leaves 0.2 stress and a persistent 1.25 multiplier on overload stress growth. Invalid actors, relief/catch/pressure, duplicate restart and insufficient cash are rejected before payment. The legacy isolated shift repair remains unchanged.

The operations ledger exposes $1,500 proper maintenance. The single hotel clock supplies its absolute two-hour completion deadline. Heat is zero throughout downtime; physical repair support is cancelled and locked out. The final thermal interval uses zero heat, then one completion restores at least 95 percent condition and clears patch, stress and failure. Midnight/report boundaries preserve the job. Planned downtime itself does not block model-only WAIT; guest problems retain their ordinary interruption rules.

Payments debit immediately and accumulate in the current accounting period. Reports include maintenance in net earnings without debiting it again. Report and current-period totals, patch and deadline survive host snapshots; model schema 9 / LAN protocol 10. Bounds, downtime coherence and finite absolute deadlines validate before any replica mutation. Cost conservation is tested through commands and reports, not imposed on wire cash fields because the existing explicit developer cash override remains supported.

## Gate evidence

- Compile passed: `Logs/core04-phase6-compile.log`. The test invocation also recompiled the final one-line planned-maintenance presentation guard.
- Relevant EditMode **64/64 PASS**, 0.539 s, `Logs/core04-phase6-edit.xml`: 11 equipment maintenance, 15 continuous integration, 11 wire, 12 existing economy, 8 existing repair-sequence and 7 existing SOLO-repair cases. Atomic observers, thermal deadline splitting, invalid payment rejection, report/midnight persistence and malformed packet rejection passed. Same six owned quiet rooms at demand 5.355 gave one-hour stress gain 0.164217 after patch versus 0.073048 after proper maintenance; effective capacities were 3.815836 and 4.166815. These are controlled headless comparison fixtures, not human pacing evidence.
- Actual-session PlayMode **2/2 PASS**, 35.215 s, `Logs/core04-phase6-play.xml`: `ContinuousSoloPhysicalRepairPaysOnceAndLeavesEmergencyPatchState` uses real relief hold/catch, movement and all remaining physical controls; only restart pays and patched gauge state remains. `ContinuousReceptionMaintenanceDebitsOnceKeepsHeatOffAndAllowsRealWaitUntilCompletion` uses reception input, the ledger action and ordinary two-controller WAIT; heat stays off, one completion restores it and stops WAIT without a modal. Explicit initial failure and starting-funds fixtures are labelled. No scene geometry change was required.
- No fresh two-process LAN run in this phase; source-level command routing and replica tests do not substitute for the final actual LAN gate.

The Alt+Tab hang investigation remains deferred and unresolved.
