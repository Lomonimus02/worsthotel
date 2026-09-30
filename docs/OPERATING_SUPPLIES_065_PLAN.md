# Prototype 0.6.5 implementation plan

Status: implemented and verified on 2026-09-30. [Result, exact values, observations and limits](OPERATING_SUPPLIES_065.md). The Windows build, 136 focused model checks, two scene checks and the 24-capture Windows presentation diagnostic passed. No balance changes followed the economy observation.

Add paid, delayed operating supplies and a modest ordinary-demand reputation effect to the existing hotel. Preserve the physical turnover loop and all frozen economy values. Do not retune from the observation collected at the end.

## Frozen baseline

Starting cash $1600; overhead $350/day; no Day 1 contract payment, $350 on Day 2 at 22:00, then +$50/day. North Wing costs $4500 with +$240 future obligation. Existing maintenance, insulation, boiler and independent A/B electrical prices and capacities remain unchanged. Retain the requested cash/quota corner display; do not add supply HUD.

## Implementation order

1. Prove ten simultaneous physical stays in the existing scene. Observe per-guest room arrival, physical sleep staging, scheduled outings/returns, actual receipts and physical exits. Separate early departure outcomes from navigation blockers. An explicitly funded, legally purchased equipment fixture may isolate lifecycle coverage, but is not profitability evidence. Stop capacity work once the intended hotel works reasonably.
2. Keep initial linen and bulb stock, remove continuous-calendar free refills. Track deposited dirty sets independently from reusable room bundle identities. Preserve deposited proof for bed-making. Reuse existing physical shelf slots and retain delivered overflow without overwriting carried stock. Return blankets through completed turnover.
3. Add explicit orders: $18 per dirty set; three bulbs for $45. Payment occurs once when ordering, subject only to current available cash and actual stock eligibility. Deliver at the next strictly future 06:00 window. All new prices/timing are configurable in EconomyConfig. No automatic purchases.
4. Add a physical supply ledger in the linen room, using the existing book interaction. Show available clean linen, dirty waiting, laundry in transit, bulbs and next delivery; provide short order actions. Reuse physical stock shelves and add a delivery note, not an NPC or persistent supply HUD.
5. Record laundry and bulb spending separately in current ACCOUNTS, published operating reports and the ownership-loss statement. Contract money remains spendable. Round-trip pending deliveries, stock and expenses through validated snapshots; keep remote orders host-authoritative and reject stale/repeated requests.
6. Multiply existing ordinary price demand by a configurable piecewise linear reputation curve: 0 reputation → 0.80, starting reputation 60 → 1.00, 100 → 1.10. Clamp final probability. Preserve existing rolls, reputation generation and special-booking behavior. Show short reputation/demand labels in the booking book.
7. Compile after integration. Focused checks cover depletion, physical turnover proof, delayed delivery once, separate immediate debits, spending contract cash, blanket recovery, normal-demand baseline, malformed/stale snapshots and commands. Check the supply book and shelf in the actual scene.
8. Collect a short, natural-booking multi-day economy observation with a disclosed staff/order policy. Distinguish model staff automation from physical human play and the separate physical-capacity fixture. Report raw realized money, supplies, occupancy, refunds, maintenance, reputation and demand. Build 0.6.5 separately from the existing 0.6.4 build and stop without another rebalance.

## Main implementation areas

Simulation supply state and housekeeping/service stock; EconomyConfig and EconomySettings; operating report and financial snapshot data; ordinary SalesSettings and demand reads; GameSession and LAN commands; ManagementUI books and the linen-room scene addition. Existing unrelated work must be preserved.

## Evidence at the capacity gate

Initial wing-only diagnostic achieved ten simultaneous checked-in, room-reached physical stays including every room 107–110. All ten produced receipts and physically exited; seven departed early. Four physically staged sleep and five completed an outing/return. The separately labelled equipped follow-up also reached ten simultaneous physical stays, physically staged sleep in all ten rooms, seven outings with completed returns, and ten receipts/exits. Three departures were early; no guest navigation changes were needed. These controlled arrivals and setup grants are not a natural-demand economy sample.
