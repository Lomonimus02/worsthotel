# Prototype 0.4.1 phase 4 — sustained distress and early departure

Status: PASS. Compiled; all 734 Edit cases passed across the full run and corrected single-case rerun; 4/4 physical Play cases passed.

The production need configuration enables early departure. Historical model fixtures opt in explicitly. A checked-in guest must have reached and currently perceive their actual owned room. One real temperature/noise/condition source and episode supplies the evidence; unrelated mild problems cannot be pooled. Severe conditions, individual patience and repeated-incident history determine the exposure trial. The episode must be escalated/critical and either disclosed to staff or exhaust real unanswered contact attempts before warning/grace starts. An ordinary impending checkout wins.

Production starting thresholds: severity strictly above 0.5, three hotel hours of severe exposure adjusted by bounded personal patience, one hotel hour of grace, and at least one hour of contracted stay remaining at commitment. A quarter-hour below severity 0.35 clears accumulated distress/grace eligibility. Being away supplies neither room evidence nor recovery. Relief and already-started bounded discussions can pause the process; opening repeated new discussions cannot defer it indefinitely. Fresh blanket receipt is measured before deciding to leave. These are initial tuning values, subject to final multi-day evidence.

Known warnings use existing guest/situation surfaces. Private distress, stale room/episode data and recovered/terminal warnings are hidden. Developer inspection shows counters. A committed departure uses the same checkout, room-release, key-return and physical exit lifecycle as a normal deadline; the departure token protects the room until the actual body crosses its doorway. Future wake promises are cancelled when no longer applicable, without inventing a missed promise; actual missed/completed promises retain their history. Pending Direct/contact/parcel/move resources are cleared consistently.

Billing uses one existing checkout posting. Compensation is the maximum of quality refund, agreed credit and the existing severe-refund floor, capped at contracted price. The severe score boundary caps the outcome; the factual cause and actual departure time are immutable receipt data. No second refund debit or shortened contractual schedule is introduced. Reports retain their receipt after guest history is pruned.

Model schema 13 / LAN protocol 15 includes the early-departure record and receipt outcome. Full validation precedes replica mutation, including finite counters, valid episode references, timestamps, terminal lifecycle, single receipt outcome and minimum refund. A rejected packet cannot consume its sequence. Client replicas cannot advance the policy.

## Verification so far

- First focused run: 57/59 passed, 1.723s. Both failures were new fixtures expecting OnRack rather than the established Returned key state. Corrected the fixtures; also tested staff reclaim and rejection of duplicate departed-owner key return. Runtime key behavior was retained. [Original result](rhythm041-phase4-focused-attempt1.xml).
- Full Edit run: 733/734 passed, 165.286s. All 60 new cases passed: 20 policy, 7 lifecycle, 9 presentation, 24 atomic snapshot. The sole failure was an old economic assertion that the first morning report cannot contain any room receipts. Two real early departures in the six-room strategy now correctly post before that report. [Original result](rhythm041-phase4-editmode-attempt1.xml). Replaced that expectation with checks of actual early outcome, departure time, preserved contract and once-only report accounting; cautious three/four-room zero-first-report receipts remain required. No production policy was disabled or retuned to satisfy it.
- Corrected economic case: **1/1 passed, 158.791s**, including all original strategy/profit/repair-count and earned-upgrade assertions. [Rerun](rhythm041-phase4-economy.xml). Together with the full run, every one of the 734 Edit cases has passed on the final runtime; only the obsolete test expectation/output changed between runs.

### Production three-day workload comparison

Seed1947, real production thermal/needs/early-departure tuning, D1 08 to D4 08 plus D4 11 checkout tail. Six prescribed booking strategies use explicitly headless physical-route/staff adapters. No automatic-sales, fresh EXE, human movement/feel or second-PC claim. Each row pays1350 operating cost; no capital purchase is included until the separate continuation.

| Strategy | Gross | Refunds | Emergency cost | Net | Boiler failures | Early receipts |
|---|---:|---:|---:|---:|---:|---:|
| 3 sequential | 2790 | 0 | 0 | 1440 | 0 | 0/9 |
| 4 sequential | 3330 | 0 | 0 | 1980 | 0 | 0/12 |
| 5 sequential | 4230 | 638 | 1000 | 1242 | 5 | 3/15 |
| 6 sequential | 5580 | 1725 | 600 | 1905 | 3 | 9/18 |
| 5 warm-room assignment | 4230 | 750 | 800 | 1330 | 4 | 4/15 |
| 5 warm rooms + blankets + vacant-valve preparation | 4230 | 75 | 600 | 2205 | 3 | 0/15 |

The six-room strategy has fewer subsequent boiler failures than the five-room sequential strategy because guests really leave and reduce demand; it still has larger refunds and poorer stay outcomes. More sold contracts do not guarantee more occupied room-hours. The first two early receipts occur at elapsed474.611/488.611, before the first06:00 report at660. Full per-guest departure times are retained in the XML output. Managed five-room operation keeps its original197.2 staff seconds,10 radiator inspections, five received welcome blankets from six attempted visits and no first-night failure. Its three failures occur one onD3 and two during theD4 tail, not one guaranteed failure each night.

The unchanged earned-capital continuation pays1800 from actual cash2955, retains condition39.572/stress.0038, reduces same-load ratio1.1999→.9599, admits the sixth actual arrival, and reachesD5 00:06 without another failure (peak stress.4079). This is causal measured capacity, not a forced failure schedule.

## Physical gate and limits

- **4/4 Play, 106.776s**, first attempt: real warned early-departure route (48.428s), reception compensation, answered-phone compensation, and physically reclaiming/returning a cancelled unreceived blanket. [Result](rhythm041-phase4-playmode.xml).
- The early-departure scene test follows actual exterior→reception→assigned-room and room→doorway→lobby→exterior movement. No arrival/vacancy/exit callbacks are fabricated. Numbered-key model handoff, staff disclosure, short policy thresholds and maintained severe temperature are explicit isolating fixtures; they are not production timing evidence. It verifies warning before commitment, unchanged contractual checkout, one payment, Returned key, dirty room with a protected departing-body token, rejected premature linen pickup, and token clearance only after actual doorway crossing. Time scale and fixed physics step remain unchanged.
- No scene geometry changed in this phase. Later phase 9 fresh executable SOLO/LAN and player-visible readability gates remain outstanding; model snapshots plus in-editor physical input are not a fresh EXE or second-PC network test.
