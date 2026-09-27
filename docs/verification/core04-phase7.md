# Core 0.4 phase 7 — Persistent capacity purchases

Status: gate passed. Phase 6 checkpoint: `8750497`.

One $1,800 boiler upgrade multiplies rated capacity by 1.25. One $1,200 electrical upgrade adds 1 unit to either existing branch A or B; buying it uses the hotel's single electrical purchase. Prices and improvements are configurable. Purchases change capacity while preserving actual consumer demand, wear, stress, failure, emergency patch and maintenance deadline. A tripped circuit still requires its physical reset. Paid actions reject invalid actors/identities, duplicate purchases, insufficient funds and unrepresentable capacity before debiting money.

The operations ledger has a controller-accessible Capacity upgrades page with price and current/after capacity. Real boiler/panel readouts mark installed upgrades. GameSession forwards client intentions; the host applies the same guarded transaction. Model schema 10 / LAN protocol 11 persist the boiler flag, chosen electrical branch and capital-spending totals. Circuit capacity derives directly from one parent purchase identity. Reports include capital in net without a second debit.

## Gate evidence

- Compile passed: `Logs/core04-phase7-compile.log`.
- Relevant EditMode **85/85 PASS**, 0.856 s, `Logs/core04-phase7-edit.xml`. Includes 40 new cases (12 equipment, 17 continuous purchase integration, 11 wire) plus capacity/maintenance snapshots, maintenance integration and existing economy regressions. Equal actual demand before/after, safe reset versus renewed overload after adding another heater, paid-state persistence and additional real bookings overcoming the upgraded boiler passed. Wire cases cover derived capacities, expense bounds, invalid branch and extreme valid configurations that cannot represent an increase.
- Actual-session PlayMode **1/1 PASS**, 5.817 s, `Logs/core04-phase7-play.xml`: `ContinuousPhysicalLedgerUpgradesPreserveRealLoadAndNeedActualBreakerReset`. Both purchases through the reception controls preserve the two physical heater consumers and one room load, leave boiler failure/branch trip intact, and raise only the chosen branch capacity. Its actual cover/reset restores power; unchanged load stays below the new capacity without retripping.
- The Play fixture labels initial funds, one headless checked-in guest and physical heater placement; actual switches, natural electrical trip, ledger purchases and physical cover/reset use player input. Boiler failure is an explicit separate fixture to prove an upgrade cannot repair it. Purchase-time electrical stress is observed at the actual installed event, avoiding a frame-timing-dependent assertion about subsequent normal recovery.

No fresh two-process LAN or three-day pacing claim at this intermediate gate. Alt+Tab investigation remains deferred and unresolved.
