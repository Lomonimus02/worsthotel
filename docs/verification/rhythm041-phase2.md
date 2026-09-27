# Rhythm 0.4.1 — pre-failure boiler model

Status: PHASE GATE PASS. Phase1 causal gate and checkpoint: `51b8426`. No new maintenance, booking or sleep mechanic belongs to this phase. Final EXE, visual and milestone balance gates remain pending.

The continuous boiler now has an explicit Busy band below Strained. With ratio `r=demand/effectiveCapacity`, strain `s=clamp((r-.85)/.15,0,1)` and overload `o=max(0,r-1)`, the initial trial is:

- Busy starts at .70; Strained at .85; actual overload remains strictly above1.
- Working heat output is `(1-.10*s)*min(1,1/r)`, or1 for zero demand. Failed and maintenance output overrides remain unchanged.
- Current trial stress/hour is `(.015*s+.25*o)*(1+(1-condition/100)*.5)*(patched?1.25:1) - .25*max(0,(.85-r)/.85)`, clamped into0..1 after elapsed time. The initial .03 strain gain was reduced after the failed multi-day gate below. Lower demand recovers gradually; no calendar reset.
- Condition loss/day is `3*min(1,r)+3*s+12*o`. Wear, stress and recovery use hotel time, not wall time or Unity physics speed.
- Failure still requires **real overload and accumulated stress1**. Merely crossing100%, arriving at a new day or having six guests cannot directly call a failure.

The four new tuning values are configurable and explicitly serialized in the production boiler asset. Existing rated4.6 and condition capacity floor.85 remain. Legacy shift equations remain separate. Detailed calculated consumer/temperature examples, not runtime measurements, are in [the initial audit](rhythm041-capacity-baseline.md).

`Busy=4` preserves existing enum values; explicit semantic ordering replaces numeric ordinal comparisons in physical lamps, sound and HUD. Forecast uses the same load-only classification. Stored critical stress and actual failure remain separate facts. New game version0.4.1 and LAN compatibility13 prevent unlike pre-failure rules from silently joining; the raw model snapshot shape is unchanged at schema11 during this phase.

## Required evidence

### First full run and measured correction

[First EditMode run](rhythm041-phase2-editmode-attempt1.xml): 635/638 passed, 3 failed, 200.8107418s. Compilation passed. All new kernel, snapshot and real-consumer thermal cases passed. Two upgrade tests retained obsolete assumptions: still-Strained demand now accumulates stress more slowly rather than recovering, and a purchase-only callback assertion remained subscribed during a later deliberate load change. Those assertions are being corrected without changing purchase atomicity or recovery coverage.

The substantive balance failure was the existing production-seeded three-day comparison. Cautious4: no failure, net1980. Unmanaged5:9 failures/net104; unmanaged6:12/net−286. Warm assignment alone:9/net329. Managed5 (same stays/prices, finite welcome blankets and timed empty-room radiator preparation):4 failures/net2005, violating the existing maximum3 gate and leaving very little additional return over cautious4. These are headless model policies with declared two-staff route/key/linen/repair adapters, not physical player results.

Only the new strain stress gain changes for trial2: .03→.015 in production asset, config and immutable settings defaults. Heat loss, wear, overload stress, recovery and failure eligibility stay fixed; every economic acceptance assertion stays fixed. The objective is less accumulated preload near capacity while preserving the observed pre-failure room consequences. The trial is not accepted until remeasured.

First-run paired constant-workload thermal evidence at about4 hotel hours (actual four/five/six owned rooms, delayed activity/contact adapters explicitly labelled in the test): heat1/.93708/.80256, room102 20.627/19.869/18.244C, all boilers still working. The natural guest self-help case changed demand only upon its versioned radiator anchor; duplicate application was refused and electrical state remained unchanged. After actual authorized closure of other room valves, the six-room setup recovered heat1/stress0 and room102 reached20.281C without repairing condition. These temperatures are first-trial measurements; trial2 must rerun them.

### Second full run

[Second EditMode run](rhythm041-phase2-editmode.xml): **638/638 PASS, zero failures, 201.8649707s**. Trial2 retains all original acceptance assertions. The same production-seeded three-day comparison produced:

| Occupancy / policy | Gross | Refunds | Emergency spend | Net | Failures |
|---|---:|---:|---:|---:|---:|
| 3 sequential | 2790 | 0 | 0 | 1440 | 0 |
| 4 sequential | 3330 | 0 | 0 | 1980 | 0 |
| 5 sequential | 4230 | 871 | 1800 | 209 | 9 |
| 6 sequential | 5580 | 2116 | 2000 | 114 | 10 |
| 5 warm-room assignment | 4230 | 751 | 1800 | 329 | 9 |
| 5 warm rooms + finite welcome blankets + vacant radiator preparation | 4230 | 75 | 600 | 2205 | 3 |

Operating costs total1350 for every policy. Managed5 first failed only during the second cohort's overnight (calendarD3), preserving a full first night without disaster. Its exact failure counts were D3:1 and D4 checkout tail:2; this is not a guarantee of one failure per night. Cautious4 peak stress.211 and longest occupied quiet interval286.8 model seconds. Managed5 quiet524 seconds, longest297.8; it required197.2 adapter staff seconds for finite blanket visits and10 actual vacant-room inspections. No hidden credit, forced fault or free repair was added. Aggressive failures remain costly: final tuning will also evaluate the upcoming preventive-service choices, automatic sales and schedules rather than treating this intermediate phase as final balance.

The separate earned-capital continuation paid1800 from real cash2955; the same demand ratio fell1.1999→.9599 with wear/stress retained. A real sixth arrival followed; throughD5 00:06 the observed peak stress was.4079 with no additional failure. No future failure is mandated by the test.

The three thermal consequence tests passed again: output and room temperatures were unchanged; five/six-room stress became.04041/.19442 at121.25s. Self-help and recovery remained causal and power-independent. Snapshot JSON/mirror, forecast purity, upgrade atomicity, legacy behavior, cash/report and all other EditMode regressions passed.

The first PlayMode command incorrectly used filenames as class filters (the tests share partial `Phase1PlayModeTests`), selected zero cases and therefore proves nothing; its [empty-filter result](rhythm041-phase2-playmode-empty-filter.xml) is retained. The corrected explicit-method run passed **3/3, 40.7129659s**: actual owned-room demand crosses Busy and Strained with matching plaque/lamp/hum; physical radiator input changes only its attributed consumer; physical heater carry/switch still causes the independent power warning/trip chain. [PlayMode result](rhythm041-phase2-playmode.xml). The first feedback test uses labelled headless room-ownership adapters; the radiator and heater tests use actual scene/controller interactions. No screenshot or human interpretation is inferred from a passed assertion.

All headless route/staging or delayed-activity adapters must be labelled. No temperature/load/failure override may be described as natural guest consequence. No new UI pixel, actual Windows0.4.1 SOLO/LAN, final balance, human feel or native Alt+Tab-fix claim is made here.
