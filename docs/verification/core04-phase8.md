# Core 0.4 phase 8 — Working capital, accounts and booking pressure

Status: gate passed after the calibration and corrections recorded below. Phase 7 checkpoint: `c1113e8`.

Production opening cash is $750, covering the first $450 operating report at 06:00 before the first 10:00 checkout, plus a $200 emergency patch and $100 margin. Daily operating cost, room price grid, refund rules and equipment purchase prices are unchanged. Historical isolated model fixtures retain their original default starting cash; the authored game asset and newly created EconomyConfig use $750.

Read-only current-period totals separate posted checkout charges, credits/refunds and collected income from unpaid nominal active bookings. Maintenance/capital payments are already posted. Reports and their UI show every net component and opening/closing cash without posting anything when read.

Booking estimates use actual current valves, room heat-loss profiles, equipment condition/upgrades and currently switched-on assigned heaters. Accepted intervals and the candidate are swept as half-open intervals; sequential cohorts are not added as simultaneous guests. Materialized checkout changes and committed rooms are respected, with the candidate counted once. The forecast reports busiest typical heating demand, one largest simultaneous shower addition and selected-branch headroom. It is a pure estimate on host or mirror, not a future activity/failure prediction. Price changes expected revenue and guest expectations, not calculated physical demand. The controller-accessible detail page preserves the selected room and draft price.

## Gate evidence

Initial compile passed (`Logs/core04-phase8-compile.log`). Relevant EditMode passed 69/69 in 110.96s (`Logs/core04-phase8-edit.xml`). Physical reception forecast PlayMode passed 1/1 in 6.07s (`Logs/core04-phase8-play.xml`): selected room, fourth overlapping reservation, price-only edit, acceptance/cancellation, current finance and controller return all exercised in the actual session. These results precede the integration corrections below.

Three calendar days from D1 08:00 to D4 08:00 do not yet include the third cohort's 10:00 checkout: comparisons explicitly add a D4 11:00 tail without fourth-day bookings. Report receipts and current-period receipts are counted once. Headless travel/key/linen/repair adapters are labelled and are not an actual SOLO executable playtest; that remains phase 10.

Natural production seed 1947, same room/profile offer prefixes and natural activities, optional promises declined, no upgrades, reactive paid patches only:

| Guests per cohort | Paid stays | Gross | Refunds | Patches | Net after 3 bills | Minimum cash | Peak stress | Failures | Longest occupied quiet interval |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 3 | 9 | 2790 | 0 | 0 | 1440 | 300 | .012 | 0 | 302.2s |
| 4 | 12 | 3330 | 0 | 0 | 1980 | 300 | .143 | 0 | 284.4s |
| 5 | 15 | 4230 | 901 | 1800 | 179 | 100 | 1 | 9 | 51.4s |
| 6 | 18 | 5580 | 2116 | 2400 | -286 | -401 | 1 | 12 | 51.4s |

The 5–6 guest reactive-only policies expose an expensive repeated-patch spiral. This is not evidence that five guests are manageable with useful proactive decisions; that comparison is still required before this gate closes. No balance success is inferred merely from passing structural assertions.

## Integration corrections before phase 9

Cross-checking the complete request found two prior-phase omissions: a real compensation conversation needs a finite Direct hold (including reception/phone), and §52 ends the active emergency patch on the next actual failure. Both corrections compile (`Logs/core04-phase8-corrections-compile.log`) and are being revalidated before advancing. Snapshot validation also rejects a failed boiler carrying an active patch. The original Remote physical cause and posted spending history remain independent of these state transitions.

CompensationDiscussion is a Direct purpose, not a new guest state or clock. Real disclosure/conversation begins the existing 40-hotel-second bounded wait; repaint/reopen does not renew it. Credit/refusal, measured recovery, timeout or checkout ends it. Reception guests retain their physical route and return only after the decision. Phone decisions require that actor's actual handset grant bound to that answered guest/response; room/desk decisions require the physical conversation grant. Client commands carry the displayed intent ID/revision, and paused/inactive/other-actor/stale decisions are rejected. Closing the panel revokes local authority while leaving the model wait finite. Credit is reserved for the actual checkout and does not fix the Remote cause. The explicit blanket agreement remains DropOff and does not acquire a compulsory compensation wait.

Model schema 11 / LAN protocol 12 (`worst-hotel-0.4-discussions12-gzip`) explicitly rejects older snapshots/peers. No new DTO fields were required. Full EditMode after the corrections: **610/611**, 197.81s (`Logs/core04-phase8-full-edit.xml`). All compensation, snapshot, patch lifecycle and prior regression cases passed. The sole failure is the honest balance assertion described below; this gate is still open.

Actual-session PlayMode passed **6/6**, 77.23s (`Logs/core04-phase8-corrections-play.xml`): two new actual reception/phone discussions, prior exterior carry/deferred receipt, cancelled parcel reclaim/return, closing the service board preserving a Direct wait, and reception booking forecast. Both conversation fixtures use labelled initial booking/key/room/clock/cold setup, then real guest contact routes, physical interaction and controller choices; they verify captured discussion identities, close/reopen without deadline renewal, stale/other-actor/paused rejection and credit releasing only the conversation. These are gameplay/session and serialized-envelope checks, not a fresh two-process LAN run; that remains phase 10.

Warm-room assignment alone did not control boiler demand: five guests produced 10 failures, $2,000 patch spending, $826 refunds and $54 net. Warm assignment plus five received welcome blankets (six finite attempts, 197.2 shared staff-model seconds, actual stock/access commands) reduced refunds to $451 and improved net to $429, but still produced 10 failures. Those fixture trips are headless model adapters, not physical carry proof. The failed assertion requires fewer recurrent faults, so improved guest comfort alone is not accepted as the intended manageable five-guest operation. Next comparison adds an explicitly performed valve inspection during actual vacant-room linen preparation; ordinary runtime has no automatic valve reset.

No full-game pacing or Alt+Tab stability claim is made here.

## Calibration iterations (not final acceptance)

The vacancy-only manual radiator inspection reduced managed-five failures from ten to six at rated capacity 4.2 (`Logs/core04-phase8-balance-r3.xml`, 15/16 passed; all fourteen compensation snapshot cases passed). No runtime automatic reset was added. At rated 4.5, full EditMode passed 612/614 (`Logs/core04-phase8-full-edit-r4.xml`, 177.26s): managed five had four failures, $800 patch costs, $75 refunds and $2,005 net versus cautious four's $1,980, still exceeding the maximum three emergencies. Three/four guests had no failures; unmanaged five/six had six/twelve. The other failure was a historical shift/defer fixture unintentionally consuming the new continuous capacity; it is now explicitly pinned to its previous 4.2 through cloned configuration.

Next calibration is rated 4.6 (+2.22% from 4.5), with stress, wear, prices, stock and failure thresholds unchanged. At initial condition 85 its effective capacity is 4.4965: typical mixed five stays strained at approximately 94% utilization; six exceeds 111%. These are calculated expectations awaiting the final run, not measured acceptance.

## Final production economic comparison

Rated 4.6 passed all 20 selected cases in 179.92s (`Logs/core04-phase8-final-edit.xml`), including the strict natural comparison, immutable production settings, consumer counterfactuals and historical heater regression. All scenarios use the same seed and offer prefixes above; no forced faults, injected cash, automatic valve resets or schedule overrides were introduced.

| Policy | Paid stays | Gross | Refunds | Patch spending | Net after three bills | Minimum cash | Natural failures | Longest occupied quiet interval |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 3 sequential rooms | 9 | 2790 | 0 | 0 | 1440 | 300 | 0 | 302.2s |
| 4 sequential rooms | 12 | 3330 | 0 | 0 | 1980 | 300 | 0 | 286.8s |
| 5 sequential rooms | 15 | 4230 | 413 | 1200 | 1267 | 300 | 6 | 67.4s |
| 6 sequential rooms | 18 | 5580 | 1929 | 2200 | 101 | -14 | 11 | 51.4s |
| 5, warm-room assignment | 15 | 4230 | 150 | 1000 | 1730 | 300 | 5 | 67.4s |
| 5, warm rooms + welcome blankets + vacancy valve inspection | 15 | 4230 | 75 | 400 | 2405 | 300 | 2 | 284.4s |

Managed five delivered five welcome blankets in six finite visits (197.2 staff-model seconds), inspected ten vacated radiators and adjusted five. It completed sixteen naturally staged showers, twelve contacts and three reports. The two actual failures occurred at elapsed 1357.2 and 2122.0, during the second and third occupied overnights; they are not scheduled day events. Four rooms reached peak stress .019 with no failure. Gross revenue rises with additional business, while unprepared six briefly runs out of working cash and retains only $101 net. This demonstrates an economic choice under the declared staff adapters, not a human difficulty rating.

The physical conversation grant now covers the configured Direct wait plus five real seconds (minimum 30), preventing the old 30-second authority expiry from rejecting a still-visible 40-model-second decision. Legacy conversation lifetime remains 30. The physical regression explicitly ages the existing grant and model to 31 seconds before controller compensation; no production clock override was added.

Final actual-session PlayMode passed 5/5 (`Logs/core04-phase8-final-play.xml`): real radiator demand, staged shower and tripped-panel requested load, ledger upgrades and physical breaker reset, booking forecast/controller navigation, and actual desk compensation after the old 30-second expiry. Compilation succeeded in the final Edit and Play runs. Together with the earlier complete 612/614 suite and the final corrected 20/20, both outstanding failures are resolved; a fresh full suite remains required at phase 10. Phase 9 may now proceed. This does not claim the final 0.4 executable or LAN release is built.
