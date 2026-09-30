# Prototype 0.6.4 implementation plan

Implement the requested economy pressure and viable North Wing in the current project. This is not an economy redesign. Preserve unrelated working-tree changes.

Completed 2026-09-30. See [implementation, measured results and verification limits](ECONOMY_PRESSURE_064.md). No follow-on 0.6.5 work or publication was started.

## Changes

1. Update contract defaults in OwnershipContract, SessionConfig, PrototypeSession and PrototypeGameplayBuilder: first payment Day 2 at 22:00, $350, +$50 per successful payment. Preserve starting cash $1600, operations $350 at 06:00 from Day 2, wing $4500 and future obligation modifier $240. Retain actual-cash settlement, unrestricted spending and terminal ownership loss.
2. Improve the physical ACCOUNTS pages in ManagementUI.Books: next due date and amount, cash movement and paginated frozen guest receipts with agreed amount, existing refund, received amount and existing explanations. Expose current-period receipts read-only; do not calculate new penalties.
3. Decouple GuestPresentation bodies from reception capacity. A reception position is a temporary lease; release after physical clearance, reacquire for service visits, wait and retry if full. Existing guests in rooms or away must not require a slot. Preserve real arrival acknowledgements and room-vacancy ownership.
4. Separate opposing reception/corridor traffic in AuthoredGuestRoute. Keep existing doorway gates and room routes. Recover from local obstruction using collision-checked walking, without acknowledging false arrivals or teleporting bodies.
5. Store electrical upgrades per branch. Update model, snapshots and LAN compatibility, ledger controls and physical indicators. Preserve $800 per branch and authored upgraded capacity 6.5. Reject duplicate purchases; wing restoration must not buy either upgrade.

## Verification and measurements

Compile after integration. Run focused existing contract, electrical and route checks, updating only changed expectations. Use an isolated developer runtime sample of the actual scene to verify at least seven concurrent physical stays including wing rooms, followed by real checkout receipts. If debug funding is needed to restore the wing, label it explicitly and separate it from ordinary progression.

Reuse the existing lightweight one-shot economy sampler for A: four rooms at $180; B: six at $180; C: six at $300, until Day 7 or ownership loss. Record calendar-day cash, gross room receipts, refunds, operations, maintenance, capital purchases, contract paid, payment-time cash, early departures and boiler failures. Distinguish headless model/operator sampling from physical scene validation.

Return raw results and limitations. Do not retune from the sample, add supplies/reputation systems, publish a release or expand the scope.
