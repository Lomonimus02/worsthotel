# Phase8 — measured thermal feedback and causal history

Implementation after phase7 commit24f2024. Compile,34 focused model cases and all5 selected physical/UI scenarios PASS across the documented corrected attempts. No fresh0.4.1 EXE result is claimed in this document yet.

## Contract

`RoomThermalBreakdown` is the single calculation used by temperature ticks and diagnostic/presentation queries. Original float arithmetic remains exact for unassisted rooms, with the existing double integration/clamp retained for supplemental heaters. Queries return the actual powered heater contribution and attributed space/shower demand without advancing state. Demand is load context, not an extra temperature term.

Normal Operations room rows show actual temperature and warming/cooling/steady direction. Radiator prompts explain current central heat, active supplemental heat and relative insulation weakness. Radiator tint follows its actual central contribution rather than portable heater output. Door plaques retain temperature when a room circuit trips, identifying the circuit power loss separately.

Infrastructure history retains64 typed actual transitions, absolute hotel time, entity, before/after values and measured boiler/circuit context. Tick observations occur after a coherent completed tick; direct successful commands observe their changes immediately. Queries, no-ops, failed commands and mirrors do not append. No new gameplay event is generated. History persists through reports and resets with a fresh model; it is host-local diagnostic evidence, not a new replicated gameplay state.

F2 exposes the measured equation/history, existing controls, finite boiler stress, paid Basic/Full service, completion by ordinary ticks, forward-only calendar hour/morning, per-room sales toggles and one next-due enquiry demand override. The override still checks a real open available room and consumes its real cursor once. Satisfaction changes accumulated quality history only, preserving price/service penalties and rejecting impossible targets. Early-checkout priming requires current severe disclosed distress, then preserves normal warning, grace, physical departure and receipt rules.

The labelled diagnostic sleep control is offline SOLO only, requires a nearby actual bed under the physical ray, never teleports, and uses ordinary waking rules. It is separate from normal E/controller consent acceptance evidence. Its critical-wake option creates an explicitly labelled actual boiler fault; it is unavailable during paid maintenance. F2 commands label resulting infrastructure entries as diagnostic. Ordinary LAN gameplay still uses actual physical input and host-authoritative consent.

## Validation

Compile passed. Focused attempt1 passed33/34: one powered-heater exact-bit reference differed by one float unit. Extraction had fed the separately rounded displayed central contribution back into the baseline. Restored the complete original baseline expression and the original explicit double target local in both helper and reference. Kept exact bit assertions; did not substitute a tolerance or tune gameplay settings.

Focused attempt2 passed **34/34**,0.4832707s: RoomThermalBreakdownTests9, InfrastructureHistoryTests4, RhythmDebugTests19 and ThermalPresentationTests2. Evidence: `Logs/rhythm041-phase8-focused-attempt1.xml`, `Logs/rhythm041-phase8-focused-attempt2.xml` and matching Unity logs. Covers original float/double arithmetic, heater-power independence, actual staged/away demand, query purity, bounded causal ordering/report persistence, guarded debug commands, ordinary early warning/grace/departure and real thermal trend projection.

Physical attempt1 passed4/5,27.780307s. The old phase2 radiator test still prohibited every collider on the capacity display; phase3 had deliberately made that same display the actual paid service interaction. Updated that stale assumption to require exactly its service hitbox and no intersection with any enabled emergency repair control. No runtime geometry or collision was weakened. The corrected case passed1/1,2.5618233s on attempt2.

The five verified scenarios cover demand-driven boiler hum/recovery, actual shower hot-water attribution and tripped requested load, real controller Operations pages without repeat charges, actual radiator input/local attributed load/capacity display, and diagnostic SOLO-bed distance/mode/mirror guards plus actual-fault wake. XMLs: `Logs/rhythm041-phase8-playmode-attempt1.xml` and `Logs/rhythm041-phase8-playmode-attempt2.xml`.

Prepared and compiled the opt-in two-process physical sleep driver and wrapper, but did not execute it in this phase. Final source suites, actual same-binary SOLO/LAN execution, rendered pixels and package delivery belong to phase9. The separately deferred native Alt+Tab hang is not claimed fixed.
