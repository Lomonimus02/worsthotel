# Prototype 0.6.4 — Economy pressure & expansion viability

Implemented in the existing Unity project on 2026-09-30. The contract is a current-cash ownership payment, not a revenue target. No supplies economy, reputation/demand redesign, new guests or infrastructure were added. No second balance pass was made from the sample.

Follow-up UI request: the latest local build also shows NEXT QUOTA, actual CASH and the next deadline in the top-right of each local camera during play. This explicitly supersedes the original no-permanent-quota-HUD direction below. The compact lettering has no opaque panel and hides while reading a book, using a menu or after ownership loss. No balance or cash-reservation behavior changed. Two existing contract PlayMode scenarios passed with HUD assertions; [result](verification/economy064/playmode-quota-hud.xml).

## Changes

- Day 1 has no payment. Day 2 at 22:00 requires $350; each subsequent successful daily payment increases the obligation by $50. Starting cash $1600, operations $350 at 06:00 from Day 2, wing restoration $4500 and future wing assessment +$240 are unchanged. Buying upgrades never reserves or protects the contract money. Insufficient cash remains terminal, with available/required/shortfall on the notice.
- The existing physical ACCOUNTS book shows the next deadline, current cash, separate cash movements and immediate contract receipt. Current and published guest receipts expose agreed price, actual charge, existing refund, net received and existing guest notes/departure reason. Unserved stays preserve the agreed price without inventing revenue. Longer entries paginate. No permanent quota HUD or new service penalty.
- Reception positions are temporary FIFO leases. A guest releases the berth after physically clearing the queue; room/sleep/away states do not hold it for the entire stay. Returning service visits reacquire a berth. Arrival acknowledgements still require actual walking.
- Opposing reception/corridor traffic uses separate lanes. Bounded local detours use actual body collision checks, retain doorway gates and do not teleport. Adjusted the radiator aisle around existing room furniture and moved the existing lobby luggage platform south by 0.9 m; no scene regeneration or new guest slots.
- A and B electrical upgrades are independent purchases: $800 each, 4 → 6.5 capacity. Duplicate purchases are rejected separately; buying the wing grants neither. Both capacities and physical modules replicate. Persistence means current-session/model-snapshot persistence; save/load across application restarts is still not implemented.
- LAN protocol 25 / model schema 21; old-version clients are incompatible. Published receipts also protect agreed-price metadata against mutation.

## Raw post-change sample

The requested eleven columns are preserved without averaging in [A.csv](verification/economy064/A.csv), [B.csv](verification/economy064/B.csv), [C.csv](verification/economy064/C.csv). Corresponding JSON files retain payment deadlines/outcomes and day-end cash. Calendar-day totals are not 06:00 accounting-report periods. Day 1 starts at the normal 08:00 opening. A blank payment-time balance means no payment attempted; it does not mean zero cash.

Cash after payment is the frozen 22:00 balance. Receipts or repairs after that time remain in the day's totals and may change the next day's opening cash. Therefore the full-day row does not necessarily reconcile to the 22:00 column; it reconciles to `EndCash` in JSON.

- A: rooms 101, 103, 104, 105 at $180. Survived through Day 7; $970 after the final payment and at midnight.
- B: rooms 101–106 at $180. Ownership lost Day 7 at 22:00: cash $280, due $600, shortfall $320. Paid amount is $0, not $600.
- C: rooms 101–106 at $300. Survived through Day 7; $625 after the final payment and $875 at midnight.

These are runtime executions of the actual production `HotelSimulation`, not a spreadsheet approximation. Automatic ordinary sales, authored demand/seed, need/refund/boiler models, clocks and prices remain current. A temporary adapter supplies timed guest travel and sequential work by one employee, using 0.2-second ticks. A/B/C do NOT exercise scene colliders or prove human workload/pacing.

Operator policy: A avoids the cold room 102 and buys no preventive service. B/C inspect at 10:30/12:30 and buy basic service when condition ≤75 or stress ≥0.25, provided cash covers service $350 + operations $350 + patch $200 + the current contract. This is a conservative operator decision, not a gameplay spending restriction. Keys, linen and blankets for cold-sensitive guests are sequential tasks. Natural boiler failures use the existing $200 patch; an unaffordable paid finish is deferred, not subsidized. Optional service promises and special enquiries are not accepted; no upgrades, expansion, manual electrical resets or bulb repairs are made in A/B/C.

The first B collection stopped on an adapter assumption when a 22:00 payment made an in-progress repair unaffordable. Only the adapter's handling of rejected spending was corrected; B was rerun from a fresh normal state. The archived incomplete B is excluded from the delivered tables. A and C were not rerolled. These are one sample per strategy, not an isolated experiment varying only room count/price and not a final balance conclusion.

## Expanded physical sample

[Raw result](verification/economy064/expanded-result.json), [per-frame state transitions](verification/economy064/expanded-guest-transitions.csv), [calendar-day rows](verification/economy064/expanded-physical-days.csv).

Isolated diagnostic of the real SOLO scene, physics and GuestPresentation. One labelled +$4500 debug grant paid the ordinary $4500 restoration; cash after purchase was the original $1600. Eight current ordinary offers were explicitly committed at $180 into 107, 108, 109, 110, 101, 103, 104, 105 with closely spaced fixture arrivals. Original sleep/wake/checkout times were retained. This setup does not measure natural expanded booking demand or the ability to earn the restoration cost.

Observed peak: **8 simultaneous physical checked-in guests who reached their rooms**, including all four wing rooms. Every member of that cohort completed the stay, received a real receipt and physically exited; all receipt/left flags are recorded in the result. Existing keys, prescribed luggage, heating/electricity/noise/service systems were reused. Guest callbacks were physical; no collider disabling, body teleport, satisfaction override or injected receipts. Clock ×8 and a timed MODEL employee supplied service actions; this is not a physical human staff walkthrough.

The cohort generated $1440 gross, $540 refunds, $900 collected. Rooms 107–110 generated $720 gross, $180 refunds, $540 collected. Six early checkouts, zero boiler failures. Operations $350 and maintenance $350 were separate outflows; collected room money is not profit. End: Day 2 12:03, cash $1800, before the first contract deadline. Day 2 in the diagnostic CSV is partial; Day 1's $6100 opening explicitly includes the grant.

This proves more than six simultaneous physical stays and paying wing rooms in the sampled scenario. It does not prove a full ten-guest cohort, absence of every congestion case, long-run expansion sustainability or two-PC LAN operation.

## Evidence and delivery

71 focused EditMode cases passed for contracts, guest routes, equipment/continuous upgrades and snapshot validation, including agreed-price immutability. Raw result: [editmode-064-final.xml](verification/economy064/editmode-064-final.xml).

Two additional existing unserved-stay boundary cases passed: a guest never checked in and a guest given a key without physically reaching the room both retain agreed price without generating cash. [Result](verification/economy064/editmode-064-receipt-boundary.xml). Total focused EditMode cases: **73 passed**.

**3/3 PlayMode scenarios passed**: physical contract/guest/report book navigation, real capital spending and terminal notice/restart/in-process mirror; current-cash coverage; independent A+B physical-book purchases preserving real consumer demand and requiring the actual breaker reset. The older electrical fixture was adapted from the removed reception terminal and pre-service-wing cabinet orientation to the current book and cabinet. [Result](verification/economy064/playmode-064-final.xml).

Windows development build succeeded at `Builds/Windows-0.6.4-economy-pressure/TheWorstHotelEver.exe`. Existing opt-in hidden-player presentation pass: **0.6.4, PASS, Errors=0, 21 captures at 960×600**. Visually inspected contract ($350, Day 2 22:00), cash, unserved receipt ($180 agreed/$0 received), retained paid receipt ($900 at payment versus $100 at report), and terminal notice ($400 due/$100 cash/$300 short). [Presentation record](verification/economy064/presentation-064.txt), [saved images](screenshots/economy064). This is a staged UI inspection, not a natural balance run; its `ResetVerified=False` is expected because it stops on the notice. Restart is covered by the separate PlayMode scenario.

Editor screenshots omitted IMGUI text and were not accepted as visual evidence. The built-player captures above include the actual rendered page text. Temporary sampling adapters and their metadata were removed from Assets after archival; no new permanent sampling suite is delivered. Raw runtime artifacts and archived adapters are retained outside the project under `C:/Users/Lomonimus/.codex/analysis/worst-hotel-064-20260930`.

No GitHub publication, commit or new milestone work is included. Human multi-day play, a ten-person physical cohort and LAN on two computers remain unverified. The pre-existing Alt+Tab investigation remains separate.
