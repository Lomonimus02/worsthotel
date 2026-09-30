# Hotel Debt Pressure — implementation plan

30 September 2026. Requested milestone label: PROTOTYPE 0.5.5. Implement on the existing 0.6.1 source (10aeb18), retaining its guests, physical books, services and expansion. Do not downgrade or add new guest content. Candidate build: 0.6.2 debt pressure.

## Existing economy

The production assets, not historical documentation, start with $1600 and $350 daily operating costs. Four of six restored rooms initially sell at $180. Checkout posts real income once, usually at 10:00. Accounts close at 06:00; the first close therefore precedes ordinary checkout income. Boiler/electric upgrades cost $1000/$800; basic service $350, full service $1500, insulation $450, wing restoration $4500.

## Implementation order

1. Preserve existing receipt and purchase accounting. Record contract payment separately from operating profit. Add a small immutable contract configuration and payment result, not a banking system.
2. Initial prototype amount $250, predictable +$25 per successful accounting period, +$60 per restored room above the original physical room count. Design review reduced growth from the first proposal of $50: at fixed $180 rates that proposal could make the $4500 wing unreachable even at perfect six-room occupancy. Freeze the current assessment; an expansion affects the next period only. Closing rooms for sale never reduces physical ownership responsibility. Disabled contract remains available only to legacy/model fixtures.
3. Extend the existing physical FINANCE book: current cash, income, maintenance/capital outflows, upcoming operating expense, obligation, deadline, cash shortfall/surplus and next assessment. State clearly that unpaid bookings are not cash and the opening reserve funds the first morning.
4. At the existing report boundary post operating costs once, then attempt a separate contract debit against remaining cash. Report economic net separately; final cash is opening cash plus economic net minus actual contract payment. Passing keeps the continuous world intact.
5. Insufficient funds means terminal ownership loss, no partial payment or rolling debt. Stop simulation/time advancement, disallow purchases and physical actions, show a thematic repossession notice with exact funds/shortfall. Offer an explicit new hotel or return to menu, not continuation. Host remains authoritative; mirror snapshots show the same result without charging again.
6. Keep implementation contained: Simulation/HotelSimulation.Operations.cs, EconomySystem.Operations.cs, GuestStay.cs, new contract types; SessionConfig/production asset; existing snapshot validation and LAN compatibility; GameSession service/terminal lifecycle; ManagementUI physical book and terminal paperwork.
7. Preview the next contract increase in renovation/purchase context, preserving existing bookings, demand, room sales and infrastructure behavior. No new random penalties, lenders, infrastructure or reputation systems.

## Verification

Use the existing NUnit setup for a small focused set: exact-funds pass; healthy revenue but spending-induced failure; repeated tick/idempotency and stopping at the failure boundary; multiple successful increasing days; expansion locks today's amount and raises tomorrow's; snapshot round-trip/invalid payment rejection/read-only replica. Compile after core integration and final UI. Use existing Windows build tooling into a new output directory, preserving 0.6.1.

Perform a short native interaction pass: locate/open physical accounts, inspect due and spending, observe a payment and a terminal notice/restart. Any debug clock acceleration or fixture funds must be labelled, never described as full human pacing or balance acceptance. Do not add an automated test framework or long soak pipeline. Keep existing user's open player untouched unless permission to close is given.

## Completion record

All seven implementation steps are implemented in 0.6.2 on top of 0.6.1. Reused the existing ACCOUNTS book (not a new FINANCE object), existing accounting clock and existing diegetic presentation pass. Compile/build, 42 focused regression cases, one PlayMode transaction/failure/restart scenario and the 26.8-second built-player presentation pass succeeded. Source scene was not regenerated. Exact rules, accepted screenshots and caveats are in [DEBT_PRESSURE_062.md](DEBT_PRESSURE_062.md).

Native SOLO launch and mouse view were observed, but keyboard injection did not visibly respond, so the manual short-run acceptance remains incomplete. Do not equate the fixture passes with human pacing, focus recovery or two-PC LAN acceptance. The old open 0.6.1 player was preserved.
