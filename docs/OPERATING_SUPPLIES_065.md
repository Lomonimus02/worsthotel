# Prototype 0.6.5 operating economy and supplies

Implemented paid, delayed laundry and bulb orders, reusable blankets recovered through bed turnover, a physical linen-room supply book, separate accounting categories and a modest reputation effect on ordinary demand. No contract rebalance was performed after observing the results.

## Exact first pass settings

- Laundry: $18 for each deposited dirty set. The explicit order sends all currently waiting sets and pays immediately.
- Bulbs: one pack of 3 for $45. Pending packs reserve space in the six existing physical bulb slots; carried or dropped stock is never overwritten.
- Delivery: the next strictly future 06:00 on the hotel calendar. An order submitted at 06:00 goes to the following morning. No automatic ordering or delivery NPC.
- Initial physical stock remains 10 clean linen sets, 3 bulbs and 3 blankets. There is no free continuous-day linen or bulb refill. Legacy shift fixtures retain their historical behavior.
- Returned linen fills consumed physical shelf slots. If shelves are full, the paid remainder is kept in delivered reserve and appears as slots are consumed; nothing paid for disappears.
- Ordinary demand multiplier: reputation 0 → 0.80, 60 → 1.00, 100 → 1.10. Linear on either side of 60; final price-based probability is clamped to 0–1. The original 0.9 reference-price probability remains 0.9 at starting reputation. Existing reputation generation and special-booking decisions are unchanged.

Supply costs and delivery time are in `EconomyConfig` / `ScriptableObjects/Economy.asset`; demand tuning is in `SessionConfig` / `PrototypeSession.asset`. These are prototype values, not final balance recommendations.

Starting cash $1600, overhead $350, Day 1 payment holiday, Day 2 22:00 payment $350, +$50/day, wing $4500 / +$240 future obligation, existing maintenance and independent A/B upgrades are unchanged. Supply purchases may spend tonight's contract money. No fines, loans, banking, employees, new guest types, per-unit utility costs or additional inventories were added.

## Physical interaction and accounting

Find **SUPPLIES & LAUNDRY** on the folding table in the existing laundry room off the staff corridor. The book shows shelf linen, delivered reserve, dirty sets waiting, sets at laundry, bulbs, pending bulbs, next delivery and actual cash. Choose **Send … sets** or **Order 3 bulbs**. Service terms explain prices and the delivery window. The physical notice between shelves records pending and received deliveries.

Dirty linen still must be carried from a physically vacated room to the hamper. Sending it out does not invalidate proof that the room was stripped: the player can make that bed with another clean set while laundry is away. Dirty inventory outlives reuse of the room's physical bundle identity. A completed turnover recovers used blankets; neither midnight nor pruning guest history gives away a new blanket.

**ACCOUNTS** records **Laundry service** and **Bulb orders** separately in current spending, published reports and the final ownership-loss notice. Reports and deliveries never charge a second time. **RESERVATIONS → Room sales / rates** shows short reputation/demand labels. No permanent supply HUD was added; the previously requested top-right cash/quota display remains.

LAN protocol 26 / model schema 22 include stock, delivery, deposit generations, separate expenses and reputation-demand settings. Only the host executes orders. Commands carry an observed supply revision and require the supply book; stale or repeated orders do not buy twice. Replicas can preview order availability but cannot mutate stock or charge independently. Snapshot persistence here means in-session host replication, not saving a game across application restarts.

## Ten physical stays

The first, wing-only diagnostic reached 10 simultaneous checked-in guests with physical bodies at their assigned rooms, including all four restored rooms. All 10 posted receipts and physically exited; 7 left early, so only 4 physically staged sleep.

The separately labelled equipped run reached the same 10 simultaneous stays and physical sleep staging in all 10 rooms. Seven guests completed an exterior outing and a physical return; routines were not forced to give everyone an outing. All 10 posted receipts and physically exited. Three departures were early. No additional guest navigation change was necessary.

Equipment fixture funding was explicit: $7550 paid for wing $4500, burner $1000, A/B $800 each and insulation $450 through normal purchase commands, leaving the normal $1600 baseline. The fixed cohort used ten real ordinary offers with closely spaced diagnostic arrival times. Guest navigation, sleep anchors and exits were real scene callbacks; staff work used the disclosed timed model adapter. This is not proof that ordinary players can afford the expansion immediately or operate it profitably.

Raw evidence: [baseline](verification/supplies065/physical-ten-baseline.json), [equipped lifecycle](verification/supplies065/physical-ten-equipped.json).

## Actual four day observation

Both observations ran the production model through the end of calendar Day 4, six starting rooms at $180 with natural automatic bookings. No cash grants, forced demand, changed prices or quota adjustment. Guest travel callbacks and one employee's actions used the timed model adapter, not physical player input. Physical capacity was measured separately above.

With daily manual supply rounds, laundry cost $90 on Day 2, $126 on Day 3 and $108 on Day 4: $324 total. End-of-day cash was $1690, $990, $614, $226. The Day 4 contract was paid with $246 remaining at 22:00; later repair spending and receipts left $226 at midnight. The run survived all four days and paid the three scheduled obligations; Day 1 had no payment.

Without supply orders, shelf linen reached zero during Day 3. Day 4 had zero new check-ins and $360 of uncharged agreed receipts; four earlier stays paid that day. Final cash was $570, but normal room preparation had stopped. This does not imply that skipping supplies is sustainable: the observation stops after four days and does not forecast later survival.

Both runs retained all six room sales; actual check-ins, receipts, refunds, operating and maintenance costs, supply charges, quota outcomes, breakdowns and ending stocks are in the raw files. Bulb purchases were zero in these samples because the adapter did not replace lamps; paid bulb ordering/delivery is covered by the focused checks, not inferred from this economy observation.

- [Daily orders raw CSV](verification/supplies065/daily-orders.csv), [full observations](verification/supplies065/daily-orders.json), [events](verification/supplies065/daily-orders-events.log).
- [No orders raw CSV](verification/supplies065/no-orders.csv), [full observations](verification/supplies065/no-orders.json), [events](verification/supplies065/no-orders-events.log).
- [Observed configuration](verification/supplies065/config-observed.json).

Order policy: once daily after 15:00 when the employee is free, send all dirty sets then waiting, and buy one bulb pack only when available bulbs ≤1 with none in transit. Later dirty deposits wait until the next round. Failed affordability checks are skipped. Basic boiler service is considered after inspections at 10:30 and 12:30 when condition ≤75 or stress ≥0.25, with the operator retaining funds for operations, emergency repair and contract; this is adapter policy, not a game purchasing restriction. Natural failures use existing repairs. Special offers were not accepted. Raw seconds without clean stock are approximate to the model tick; no table values were retrospectively normalized.

## Verification and limits

Compilation and additive scene update succeeded. Comparing serialized scene objects found no existing object removed; only new ledger/notice children were added alongside the earlier platform-position fix. 136 focused EditMode cases passed after correcting three obsolete tests that still expected eight enquiries and six maximum linen slots instead of the pre-existing twelve enquiries and ten slots. This changed test expectations, not demand or initial supplies.

Temporary capacity/economy drivers were removed from the Unity project after execution; archived sources and original telemetry remain under `C:/Users/Lomonimus/.codex/analysis/worst-hotel-065-20260930`.

Two focused PlayMode scenarios passed: actual ray/input access to the supply book, immediate one-time bulb debit, delayed physical shelf appearance, and the existing contract/terminal-notice regression. The first no-graphics scene attempt could not create reflection render textures; the rerun enabled rendering. The new test also needed the existing continuous/automatic-sales fixture categories. Neither issue required a gameplay change.

The separate Windows build succeeded at `Builds/Windows-0.6.5-operating-supplies/TheWorstHotelEver.exe`; the older build was preserved. The opt-in rendered Windows presentation run exited with code 0. Actual 960×600 captures were inspected for the supply book before/after its $45 purchase, next-morning delivery text, service terms, cash/report expense rows, reputation/demand labels, the preserved top-right cash/quota HUD and the terminal ownership notice. Text and controls were readable without overlap in those views. This was a controlled diagnostic, not a human playthrough. [Selected captures](screenshots/supplies065/) and the [raw presentation report](verification/supplies065/presentation-result.txt) are retained with the test results.

No newly discovered physical-capacity blocker remains in the measured ten-guest scenario. The supply-starvation observation is the intended consequence of not ordering laundry, not an artificial penalty. No values were adjusted after the four-day sample.

Human play quality, remote two-PC LAN and cross-launch saves are not claimed. The economy sample is one observation per policy, not a final balance study.
