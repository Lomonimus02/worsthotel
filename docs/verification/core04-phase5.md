# Core 0.4 phase 5 — Capacity, actual demand and causal stress

Status: gate passed. Phase 4 checkpoint: `fba48ce`.

Continuous operations calculate nonnegative room-attributed radiator demand and separately staged shower hot-water demand. Empty radiators consume a small configurable amount. Going away does not close a radiator; closing one valve cannot subtract another room's demand or cancel an operating shower. Legacy shift behavior remains isolated for historical fixtures.

The boiler's rated capacity, condition-dependent effective capacity, utilization, reserve and stress are separate. A small overload first accumulates stress; safe load recovers it. Running wear uses hotel-day rates and utilization; failed equipment does not accumulate operating wear. Low condition alone does not trigger an idle failure. Electrical circuits preserve their actual consumers and warning/trip/reset behavior, with gradual recovery of overload exposure in continuous mode.

This phase does not implement the later paid emergency patch, maintenance downtime, purchases or economy retune. Ordinary low-level restart still clears its causal exposure; phase 6 will bind the physical repair to the paid persistent patch outcome.

Root wire integration: model schema 8 / LAN protocol 9. Boiler stress is explicitly serialized and range-checked before mutation. Derived capacity/bands use the matching operating mode and authored settings; no room-demand history or additional clock is persisted.

## Verification

- Compile and scene generation passed: `Logs/core04-phase5-scene.log`.
- First full EditMode: **484/485**, 16.73 s, `Logs/core04-phase5-edit-first.xml`. All 44 new cases passed: 19 equipment, 16 attributed-room, 2 production-asset counterfactuals and 7 wire cases. The only failure was an existing booking-offer test expecting zero heat demand in an empty hotel. It now explicitly expects six open vacant radiators (0.48), with no guest identities or hot water; the booking assertions remain. Corrected focused rerun **1/1 PASS**, 0.083 s, `Logs/core04-phase5-booking-r2.xml`: all 485 distinct cases passed across runs, not a fresh full 485/485 run.
- Selected PlayMode: **4/4 PASS**, 53.75 s, `Logs/core04-phase5-play-first.xml` / `Logs/core04-phase5-play.log`. Actual controller valve changes only its own attributed demand, real guest bed-to-shower movement starts hot-water demand only at the physical anchor, physical displays follow demand and tripped requested-versus-delivered power, legacy two-heater overload/retrip and the full SOLO physical repair sequence still work. Initial guest booking/key staging and the display-only breaker trip are explicitly labelled model adapters; the shower arrival and staff controls are real scene actions.

Measured production-asset quiet workloads at the same condition 84.99938 and effective capacity 4.105496:

| Actual mixed guests | Demand | Utilization |
| --- | ---: | ---: |
| 3 | 2.766260 | 67.3794% |
| 4 | 3.419040 | 83.2796% |
| 5 | 4.241290 | 103.3076% |
| 6 | 5.032850 | 122.5881% |

Closing radiators 102 and 105 for the same six occupants reduces their actual aggregate demand to 3.187160 (77.6316%) without removing guests or changing condition. Six budget profiles consume 4.421700; six cold-sensitive profiles consume 5.462100 in identical rooms/settings. These are measured steady quiet snapshots, not claims of human pacing or three real played days. Those scenarios remain phase 10.

The existing Alt+Tab hang remains deferred and unresolved.
