# 0.6.3 — independent scheduled ownership payment

User-approved change to 0.6.2, 30 September 2026.

## Implementation plan

1. Keep the existing hotel clock. Schedule operating costs at 06:00 with their own sequence, and contract collection at 22:00 with its own sequence, initially on calendar day 2. Keep report publication at 06:00 but make report creation cash-neutral. Moving the report hour must not move either debit.
2. Keep $250 / +$25 / +$60-per-restored-room tuning. Lock room assessment until the contract deadline, not the report boundary. Compare actual cash only at contract time; do not reserve or subtract another operating cost then.
3. Record payment period and exact due time. A daily report can contain no payment or one earlier payment; its closing cash may differ from the balance immediately after that payment. Keep the most recent payment independently so evening success is visible immediately.
4. On an unpaid deadline freeze a separate financial notice for the incomplete reporting period and terminate the run. Do not invent another completed morning report. Process crossed event boundaries chronologically; if configured boundaries coincide, charge operating cost, resolve contract, then publish any due report exactly once.
5. Update snapshot validation and same-epoch continuity for independent counters, current posted operating costs, last/current-period payments and frozen loss report. Increment wire compatibility once. Preserve replica read-only behavior, host-owned restart and WAIT cancellation.
6. Update physical ACCOUNTS/RENOVATION and failure text: current cash vs locked due, separate next operating-charge time, first deadline after normal first checkout, last payment receipt. No permanent HUD.
7. Adapt the existing small contract test set and one scene scenario. Check separate 06:00/22:00 boundaries, no debit on report-only close, exact cash and one-dollar short, growth/expansion, midnight/no double debit, oversized tick stopping on failure, snapshot rejection/round-trip and terminal controls. Build a separate Windows-0.6.3-contract-time folder and inspect existing opt-in player presentation captures. Preserve older builds and the user's original running game.

Main files: Simulation/OwnershipContract.cs, HotelSimulation.Contract.cs, HotelSimulation.Operations.cs, EconomySystem.Operations.cs, HotelCalendar.cs, GuestStay.cs, OperationsSnapshots.cs, SnapshotValidation.cs, HotelSimulation.Snapshots.cs; SessionConfig and production asset; GameSession terminal/report references; physical-book UI; existing tests and presentation fixture.

## Verification results

- Unity 6000.3.2f1 compilation passed. Focused EditMode regression: **44/44 passed** (10 contract cases plus calendar/basic economy cases).
- Covered independent 06:00 expense / 22:00 contract chronology, first deadline after checkout, exact cash / one-dollar short, midnight/no duplicate debit, a cash-neutral noon report, growth and expansion locking, real checkout/purchases, oversized ticks stopping at failure, immutable snapshots and same-time expense/payment/report ordering.
- The first regression run caught Unity JSON's default-object representation of absent nested financial records. Strict all-default normalization fixed this without discarding partially malformed records; the rerun passed.
- PlayMode: **2/2 passed**. Real scene books show current cash coverage independently of the next operating bill; the main scenario exercises morning report, evening receipt/unread marker, actual burner purchase, later operating expense, terminal notice, movement/purchase lockout, fresh restart and a JSON-round-tripped terminal LAN frame within the existing message size limit. A read-marker update originally depended on IMGUI repaint; it now follows a settled valid book page in the normal UI update. The rerun passed without weakening its assertions.
- Windows development build succeeded at `Builds/Windows-0.6.3-contract-time/TheWorstHotelEver.exe`.
- Existing opt-in Windows presentation fixture completed: **PASS, Errors=0, version 0.6.3**, 19 captures at 960×600. Visually inspected contract, terms, cash, renovation, evening receipt, morning report and ownership-revoked notice. Text fits; the 22:00 receipt retains its $1000 closing cash while the next report correctly shows -$350 after the burner purchase and operating charge. The notice uses the failed day-3 22:00 payment ($275 due, -$350 cash, $625 shortfall), not the earlier successful receipt.
- Final test XML, player result/manifest and seven inspected finance images are saved in [verification/contract-time-063](verification/contract-time-063). Local diagnostic logs remain in `Logs/contract063-*`.

Native focus/keyboard issues and two-PC LAN from the previous milestone are not declared fixed by this change. Fixtures use explicit bounded clock advances and labelled guest/input setup; they are not evidence of human multi-day balance or natural pacing. Prior versions and the user's already-running 0.6.1 player are preserved.
