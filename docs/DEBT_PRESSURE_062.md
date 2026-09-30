# Hotel Debt Pressure — 0.6.2

30 September 2026. Implements the requested **PROTOTYPE 0.5.5 — HOTEL DEBT PRESSURE** scope on existing 0.6.1. The numeric version advances because this repository already used 0.5.5 for cart physics. Existing special guests, books, physical systems and map remain; no new guest content or banking simulation.

## Player rules

- Interact with **ACCOUNTS** at reception. **Ownership contract** shows deadline, locked payment, cash, operating-cost reserve, coverage/shortfall, assessed rooms and next payment. **Contract terms** explains the ownership deal.
- At **06:00**, ordinary accounting charges daily operating costs, then the contract checks **cash remaining**, not revenue. Purchases already reduced that cash and are not charged twice.
- Sufficient cash pays the full contract once. The continuous hotel does not reset. The physical book gains a **NEW REPORT** marker; **Daily reports** opens the latest statement.
- Insufficient cash pays nothing and ends ownership at that boundary. No partial payment, rolling debt or later checkout rescue. A paper **OWNERSHIP REVOKED** notice shows due, funds, actual payment and shortfall. Escape/Back cannot continue the lost run.
- Start a fresh hotel or return to menu. Only the host can restart the shared hotel; a client may wait or leave.
- No permanent debt HUD. Income, credits, maintenance, renovation and cash remain in the physical book.

## Prototype tuning

Production assets remain **$1600 starting cash / $350 daily operations**, four initially offered rooms at $180 out of six restored rooms. First close: 06:00 on calendar day 2, before normal 10:00 checkout income. An empty first period leaves $1000; an idle hotel can cover two periods from its reserve, not survive indefinitely.

Contract: **$250 + $25 per subsequent accounting period + $60 per restored room above the original six**. Midnight is not another charge. Current assessment stays locked until close. North Wing still costs $4500 and adds $240 to future obligations as a standing room surcharge, not a compounded daily increase. Opening/closing sales does not change the owned-room assessment.

Example: restore the wing in period 1: current $250 stays; next becomes $515 ($275 + $240), then $540. Boiler/electrical/insulation purchases reduce cash but do not raise the room-based assessment. Renovation previews the next payment and required reserve; risky purchases remain allowed.

Planning review reduced the proposed daily rise from $50 to $25 because the steeper schedule could consume the six-room surplus before the $4500 wing became affordable. This is a starting point, **not final balance or proven player enjoyment**. Human multi-day play must tune it further.

## Implementation

`OwnershipContractSettings` owns the assessment; immutable `ContractPayment` records due, paid, funds-before and rooms. `DayReport.Net` remains ordinary economic net; `DayReport.Cash` includes actual payment. Without diagnostic cash overrides: opening cash + economic net − paid contract = final cash.

Existing operating close owns settlement; no second timer. Large diagnostic ticks stop at failure before future checkouts. Loss freezes simulation/physics/interactions/WAIT; spending is rejected. New-game reset releases camera focus, held interactions and cart state.

Snapshots carry configuration, locked assessment, loss and history. Validation rejects malformed payments atomically, duplicates do not debit, and newer same-epoch packets cannot revive a lost run. LAN **23**, model schema **19**: both computers need this build. Network snapshots are **not new disk-save support**.

## Verification

- Unity 6000.3.2f1 compilation and Windows build: passed.
- Eight focused contract EditMode cases: passed. Exact cash / one dollar short; production reserve and explicit-null legacy mode; growth; expansion lock; revenue followed by actual overspending; stop-before-future-checkout; terminal purchase rejection; mirror round-trip/duplicates/forged state.
- Focused contract + calendar + base economy regression: **42/42 passed**. Corrected one old calendar assertion to use actual circuit A membership rather than obsolete contiguous room numbers.
- One PlayMode scenario: **1/1 passed**. Physical account-book ray/controller interaction, first payment, renovation-book burner purchase, next-period loss, movement/clock freeze, nondismissable notice, restart, in-process terminal LAN frame. Approach poses and clock advances are labelled fixtures, not a human balance run or two-PC session.
- A broad initial substring filter also selected two historical automatic-economy scenarios expecting $750, which failed against the already-existing $1600 production asset. They were not retuned or counted as passing. The whole repository suite is not claimed green.
- PlayMode offscreen images omitted IMGUI text despite renderer-list submission. They are **not** accepted as evidence of readable book/notice text.
- Native SOLO opened and mouse view changed. Subsequent injected keyboard actions produced no visible response; cause is unconfirmed. This is not a successful manual playthrough or a verified fix for the previously deferred focus issue.
- Existing opt-in Windows diegetic presentation pass: **PASS, 0 errors, 26.8 seconds, 960×600**. Added only contract states to that existing pass, not a new framework. Actual player-rendered pages for contract, terms, cash, renovation and paid report plus the final failure notice were visually inspected: text fits, categories are distinct and the expansion preview is visible. The notice now also itemizes operations, refunds, maintenance and purchases. This pass uses explicit viewpoints, synthetic controller input and bounded clock advances; it is not manual play or an Alt+Tab test. Its generic report's unrelated legacy-tour fields are not gameplay acceptance claims.

Selected player screenshots and exact test results are retained in [verification/debt-pressure-062](verification/debt-pressure-062). The failed PlayMode captures are deliberately not included as UI evidence.

Local evidence: `Logs/debt-compile.log`, `Logs/debt-regression-focused.log`, `Logs/editmode-results.xml`, `Logs/debt-playmode.log`, `Logs/playmode-results.xml`, `Logs/debt-build.log`, `Logs/debt-native.log`, `Logs/debt-player-final/`.

## Local launch and remaining acceptance

Run `Builds/Windows-0.6.2-debt/TheWorstHotelEver.exe`, keeping the full directory together. 0.6.1 and the user's original open process remain untouched. The published 0.6.1 GitHub download does not contain this change; no new release is published by this task.

Human multi-day balancing and two-PC LAN remain necessary. Cross-session saves, Steam transport and final-release readiness are not claimed by this milestone.
