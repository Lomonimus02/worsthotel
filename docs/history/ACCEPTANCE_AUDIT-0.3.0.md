# Acceptance audit — Prototype 0.3 Guest Agency

Scope: [the 24 requested criteria](../GUEST_AGENCY_REQUEST.txt). This matrix records implementation and the type of evidence required. Final run results and remaining limits are maintained in [VERIFICATION.md](../VERIFICATION.md). Earlier Guest Presence / SOLO acceptance is archived in [history/ACCEPTANCE_AUDIT-0.2.2.md](../history/ACCEPTANCE_AUDIT-0.2.2.md).

| # | Criterion | Implementation / evidence |
| --- | --- | --- |
| 1 | Noise requires an identifiable cause | Real staged `INoiseSource` measurements, causal keys; source-free/ambient override regression. |
| 2 | Sources discoverable in the world | TV, muffled phone and plumbing loops, TV/phone/shower visuals; actual-player A checks playing source at closed door. Human audio readability remains manual. |
| 3 | Persistent cause avoids duplicate complaints | Guest/family/source key, one request projection; deduplication regression and A. |
| 4 | Existing situation escalates | Persistent stage/exposure/history; sustained-source test. |
| 5 | Player reduces guest noise | Real knock/body context and explicit quiet choice; input-driven PlayMode and player A/LAN agency. |
| 6 | Noise reduction changes simulation | Source multiplier, measured received noise and recovery; model + actual player tests. |
| 7 | Compensation preserves cause | Active source/case, finite grace, continued exposure; regression and D. |
| 8 | Relocation changes exposure | Prepared destination, physical key, real route, graph recomputation; both source/affected model tests and C. |
| 9 | Lightweight guest memory | Complaint/category counts, compensation, recovery, ignored cases and source warnings; snapshot roundtrip. |
| 10 | Repetition changes reactions | Configured patience reduction and shorter noisy-source agreements; B and memory tests. |
| 11 | Expanded activity repertoire | Unpack, Work, PhoneCall, WatchTV, rest, shower, loud activity, outings, Pack plus sleep/lifecycle; seeded schedule and staging tests. |
| 12 | Archetype tendencies | Weighted schedule choices, independent Noisy trait, earlier business sleep, longer cold-sensitive shower; schedule tests and booking UI. |
| 13 | Leave and return | Real room/lobby/exterior routes, hidden Away body and return; route/PlayMode tests and F. |
| 14 | No assigned-room exposure while away | Location-based perception; noise/temperature/relocation tests and F. |
| 15 | Most activities have no notifications | Activity/outing transitions are ambient; E and natural three-day tour. |
| 16 | Notifications are meaningful | Complaint+ HUD, real reception contact, infrastructure warnings, no Observed/recovery-only repeat notifications. |
| 17 | Temperature has several real solutions | Central heating/repair, local powered heater, relocation; credit/ignore preserve cause. Infrastructure tests and F. |
| 18 | Condition refers to supported state | Dirty linen and power loss only, source-specific causes; condition tests. |
| 19 | No unexplained condition score complaints | Abstract RepairState removed from living complaint/quality accumulation; regression. |
| 20 | SOLO preserved | One actor/camera/listener, existing solo repair/WAIT/input tests; actual SOLO tour. |
| 21 | LAN preserved | Version3 host authority, validated snapshots and physical conversation grants; baseline two-EXE and agency two-EXE checks. |
| 22 | Keys/linen/boiler/electricity preserved | Full existing physical interaction PlayMode suite and three-day lifecycle; model adapters clearly labelled in long tour. |
| 23 | No new large hotel subsystem | Existing rooms, heating, power and linen extended; no restaurant/employee/HVAC/city simulation. |
| 24 | Three-day run possible | Actual Windows SOLO tour with natural schedules, 3 reports and reset; final outcome linked in verification. |

The qualitative question—whether a human investigates the cause and weighs alternatives—is covered by [manual scenarios A–F](../PROTOTYPE03_MANUAL_TESTS.md). Automated checks establish causal behavior, route/input execution and state synchronization; they do not claim a human playtest, a two-computer LAN session, final audio mix or normal-display performance measurement.
