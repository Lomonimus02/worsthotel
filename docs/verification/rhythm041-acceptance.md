# Prototype 0.4.1 — acceptance plan

Prepared 2026-09-27 against [request §88–89](../RHYTHM041_REQUEST.txt) and the ordered [phase plan](../RHYTHM041_PLAN.md). **All 34 criteria are PENDING.** This is a proof plan, not an implementation or execution report. Existing baseline results establish the starting point; they do not pass new 0.4.1 behavior. Update a row only with the actual build/commit, test or run artifact, and limitations.

Proof levels: **model** = deterministic state/accounting checks; **physical** = real scene/controller interaction; **EXE** = fresh built-player execution; **human** = observations from the [Russian playtest procedure](../RHYTHM041_PLAYTEST.ru.md). Model adapters and forced setups must be labelled. Screenshots require actual rendered UI; a controller test alone is not visual proof. LAN acceptance includes host authority, client mirrors and actual two-process commands; a second-PC human test remains distinct.

| §88 | Criterion | Phase | Required proof before acceptance | Status |
|---|---|---|---|---|
| 1 | Meaningful boiler service before failure | 3, 9 | Working boiler, paid Basic/Full action, measurable condition/stress benefit; physical input and EXE observation. | PENDING |
| 2 | Preventive service has real downtime | 3, 9 | Absolute deadline, unavailable heat during work, no duplicate debit or early completion; compare Basic/Full durations in hotel time. | PENDING |
| 3 | Readable intermediate boiler states | 2, 8, 9 | Boundary/recovery model tests plus readable physical and operations views before failure. | PENDING |
| 4 | High load matters before catastrophe | 2, 9 | Sustained demand reduces reserve/performance and accumulates wear/stress while boiler still operates; matched lower-load control. | PENDING |
| 5 | Sustained overload affects room temperature | 2, 9 | Trace actual output, room heat/loss and temperature over time; no separate scripted temperature penalty. | PENDING |
| 6 | Guests react before machine failure | 4, 9 | Physical guest exposure/self-help/contact follows measured room conditions while boiler is not failed. | PENDING |
| 7 | Risk can cause refunds/serious dissatisfaction | 4, 9 | Sustained distress with once-only financial consequences; compare receipts, refunds and guest history. | PENDING |
| 8 | Eligible guests can leave early | 4, 9 | Severe-duration/trait/history guards, mild/transient negative control, real departure and turnover, no double refund. | PENDING |
| 9 | Boiler failure is not mandatory | 2, 9 | No calendar/sleep/guest-count forced failure; continuous safe-run evidence and relevant model checks. | PENDING |
| 10 | Cautious operation avoids catastrophe | 9 | Several continuous days with restrained sales and timely service; record conditions, decisions and all failures without forced fixes. | PENDING |
| 11 | Aggressive occupancy offers more income and danger | 5, 9 | Matched cautious/aggressive runs: realized occupancy, gross income, refunds, costs, net cash and load/stress; human tradeoff assessment. | PENDING |
| 12 | Boiler/electric cascade investigated | 1 | Actual source chain and controlled runtime reproduction recorded in BOILER_POWER_INTERACTION; distinguish correlation from causation. | PENDING |
| 13 | Intentional coupling is understandable | 1, 8, 9 | For each proven coupling, visible cause/consumer/load feedback and human explanation; never invent a new cascade to satisfy this row. | PENDING |
| 14 | Accidental coupling is fixed | 1, 9 | For any demonstrated defect, before/after reproduction plus causal regression; otherwise document evidence that no such defect was found. | PENDING |
| 15 | Ordinary bookings need no individual approval | 5, 9 | New ordinary reservations appear through production demand without Accept-per-guest input; fresh EXE run. | PENDING |
| 16 | Player chooses rooms open for sale | 5, 9 | Real controls alter individual sales eligibility; closing a room preserves existing contracts and occupants. | PENDING |
| 17 | Automatic bookings follow demand/config | 5, 9 | Scheduled demand, rate/horizon/availability rules, no immediate toggle-fill, deterministic checks and natural EXE progression. | PENDING |
| 18 | Assignment remains meaningful | 5, 9 | Legal actual room reassignment changes thermal/load expectations; overlap/ownership/stale-command rejection and physical check-in. | PENDING |
| 19 | Morning differs from evening through guests | 6, 9 | Full-day activity/location and actual demand trace, observed showers/checkouts versus evening room use; no hard-phase multiplier. | PENDING |
| 20 | Midday offers a maintenance/preparation window | 6, 9 | Natural away/turnover period makes real service or preparation practical; human observation across several days. | PENDING |
| 21 | Evening naturally raises building use | 6, 9 | Occupancy and staged activities explain measured evening demand; compare actual periods, not fabricated load curves. | PENDING |
| 22 | Night is naturally quiet | 6, 9 | Guest sleep/activity and lower measured usage during an ordinary night; no requirement for total silence or zero pending requests. | PENDING |
| 23 | Staff room exists | 7, 9 | Authored modular room and reachable physical beds; scene/controller and actual EXE visual inspection. | PENDING |
| 24 | SOLO player can sleep | 7, 9 | Actual bed interaction starts/cancels/completes sleep in a fresh SOLO session. | PENDING |
| 25 | Co-op sleep requires both players | 7, 9 | One vote does not advance sleep; two explicit votes do; cancel/disconnect/stale agreement tests plus real two-process LAN. | PENDING |
| 26 | Sleep advances hotel time | 7, 9 | Calendar, guests, maintenance and promises progress through normal clock logic; physics speed and world identity preserved. | PENDING |
| 27 | Real critical events wake sleepers | 7, 9 | Causally prepared real critical fault interrupts sleep at its boundary, mirrored on LAN; minor-request negative control. | PENDING |
| 28 | Sleep spawns no fake disaster | 7, 9 | Source/model comparison plus quiet-night run; sleep choice alone creates no fault or extra failure probability. | PENDING |
| 29 | Temperature changes are understandable | 8, 9 | Actual shared formula feeds thermal detail/history; human can identify output, radiator, room loss and trend. | PENDING |
| 30 | Quiet time has proactive options | 3, 5, 7, 8, 9 | Player can inspect/service/prepare/adjust sales or assignment, or sleep; human records meaningful choice without a forced task list. | PENDING |
| 31 | SOLO remains functional | 9 | Fresh full regression and several-day actual EXE with booking, service, check-in/out, sleep, finances and clean NewGame. | PENDING |
| 32 | LAN remains functional | 5, 7, 9 | Versioned/validated snapshots and commands, real two-process policy/service/sleep/calendar/accounting checks; separate second-PC observation. | PENDING |
| 33 | Pause remains stable | 0, 7, 9 | Ordinary pause/resume and WAIT/sleep separation across relevant work; preserve the separately unresolved native Alt+Tab investigation. | PENDING |
| 34 | Continuous calendar remains intact | 0, 5, 6, 7, 9 | Midnight/report/due-time boundaries preserve world state and once-only accounting during natural play and sleep; SOLO/LAN. | PENDING |

Rows 13–14 are conditional on the phase-1 finding. A supported “no coupling/defect found” conclusion must state the exercised scope; it is not permission to claim an unobserved user incident fixed. Row 11 compares the opportunity for higher revenue against greater exposure: do not guarantee greater net profit after refunds and failures or force catastrophe in every aggressive run.

## Existing foundation, not new-feature proof

The [phase-0 evidence](rhythm041-phase0.md), [capacity audit](rhythm041-capacity-baseline.md), [guest audit](rhythm041-guest-baseline.md) and [interface audit](rhythm041-interface-baseline.md) document the starting point. These existing files can supply regression fixtures; extensions and execution remain pending:

- Capacity/thermal attribution: [RoomCapacityCounterfactualTests](../../Assets/_WorstHotel/Tests/EditMode/RoomCapacityCounterfactualTests.cs), [physical capacity feedback](../../Assets/_WorstHotel/Tests/PlayMode/ContinuousCapacityFeedbackPlayModeTests.cs).
- Paid service and financial accounting: [ContinuousMaintenanceTests](../../Assets/_WorstHotel/Tests/EditMode/ContinuousMaintenanceTests.cs), [physical maintenance](../../Assets/_WorstHotel/Tests/PlayMode/ContinuousMaintenancePlayModeTests.cs), [finance projections](../../Assets/_WorstHotel/Tests/EditMode/ContinuousFinanceProjectionTests.cs). Existing Full Service is not proof of new Basic Service.
- Booking/assignment and schedules: [booking UI](../../Assets/_WorstHotel/Tests/PlayMode/ContinuousBookingUIPlayModeTests.cs), [guest schedules](../../Assets/_WorstHotel/Tests/EditMode/ContinuousGuestScheduleTests.cs). Current manual booking coverage needs adaptation; it cannot prove automatic reservations.
- Clock/authority: [calendar session tests](../../Assets/_WorstHotel/Tests/PlayMode/ContinuousCalendarPlayModeTests.cs), [WAIT tests](../../Assets/_WorstHotel/Tests/PlayMode/WaitPlayModeTests.cs), [atomic client session tests](../../Assets/_WorstHotel/Tests/PlayMode/ContinuousSessionAtomicPlayModeTests.cs). Existing WAIT consent is not proof of physical sleep.

Final human assessment uses all 12 questions in request §89. Record yes/partly/no/not observed with a concrete moment; automated success cannot answer whether the rhythm is interesting. The [old Alt+Tab investigation](../PAUSE_CRASH_INVESTIGATION.md) remains deferred unless new evidence actually resolves it.
