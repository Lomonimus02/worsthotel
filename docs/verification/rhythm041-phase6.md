# Prototype 0.4.1 phase 6 — natural daily rhythm

Status: phase6 PASS. All803 Edit cases passed across the full run plus corrected two-case economic retry; focused110 and physical2 also passed. Fresh executable verification remains phase9.

Production dated one-night stays now have deterministic staggered sleep/wake dates, an optional genuine afternoon outing with an evening return cutoff, evening room activities and a once-only first morning shower. The morning tail stays quiet and finite. Normal checkout, actual routes/anchors, direct service, room moves, packing and early departure retain priority. A wake-up promise created under the new rhythm uses that guest's planned wake time; an already accepted promise retains its exact due time.

The immutable 24-entry schedule carries a morning boundary and planned return cutoff. The cutoff is finite on every rhythm stay even if no outing is selected or feasible; -1 means legacy/disabled rhythm only. Schema15/LAN17 capture, restore and validate these fields and preserve immutable guest identity. Booking creation and snapshot validation share the same pure dated-timing helper. Client previews do not consume randomness or advance guests.

No boiler, room-temperature or electrical demand multiplier depends on the hour. Actual staged showers add hot water; actual evening activities drive their existing consumers. A guest away from their room retains their real room's space heat. With current one-night bookings, quieter midday follows actual checkout and preparation rather than invented multi-night absence.

## Verification and corrections

- First focused attempt stopped at test-assembly compilation. `Logs/rhythm041-phase6-focused-attempt1.log`. The new focused schedule tests referenced internal model members across the test assembly boundary. Runtime code compiled; no test case ran in this attempt. Correcting the tests to observe public captured state and use an explicitly labelled deadline fixture, without widening gameplay APIs.
- Focused retry: **110/110 passed**, 5.1838394 seconds. `Logs/rhythm041-phase6-focused-attempt2.xml`. Includes deterministic/custom-calendar timing, normal/promised wake and immutable late-checkout promise, legacy schedule/service regressions, the full-day demand trace and atomic rhythm/sales snapshots. Before this run, static review corrected the independent cohort fixture's expected returned-key location from OnRack to Returned; no staff reclamation was performed by that adapter.

## Measured production four-guest day

Fixed four-stay supply, production living seed1947/rhythm/needs/services/capacity; only automatic sales is isolated. Finite model travel, key, anchor and staff-reply adapters are explicit. No activity, temperature, load, fault or maintenance override. This is not a physical-navigation or human-pacing result.

| Window | Mean guests in rooms | Mean space heat | Mean hot water | Peak total demand |
|---|---:|---:|---:|---:|
| Afternoon17–19 | 2.403 | 3.6547 | 0 | 3.6547 |
| Evening19:30–22:30 | 3.873 | 3.7780 | .6167 | 5.4064 |
| Night00–06 | 4.000, all asleep | 3.8689 | 0 | 3.8689 |
| Morning06–10 | 3.994 | 4.0188 | .6085 | 5.9314 |
| Midday11–13 | 0 | .5925 | 0 | .5925 |

All four checked in, reached their rooms and departed once at the contracted checkout (elapsed780.029). Two guests made actual model-state outings and returned;434 samples confirmed that away owners still consume their own room's space heat. Every guest had exactly one staged morning shower. Night media activity was zero; evening media guest-time125.6 seconds. Every sample matched attributed boiler load to room consumers, maximum numerical difference2.38418579e-7. Four rooms were dirty after real checkout state/exit acknowledgements; wear and the one operating report persisted. A morning peak higher than the evening peak is a measured consequence of these actual showers, not a prescribed daily curve.

## Multi-day regression and changed comparison contract

The first full run passed **802/803**,219.2742963 seconds (`Logs/rhythm041-phase6-editmode-attempt1.xml`). The single failure was the old requirement that welcome blankets/warm assignment/vacant-valve preparation strictly reduce the emergency-failure count. Those policies use emergency patches only, with no preventive Basic/Full service. Genuine early departures now remove subsequent heating demand: an unmanaged six-room hotel lost11 stays and had3 faults, while managed five retained more guests and had4. This comparison cannot interpret fewer faults alone as better management.

Independent review retained all same-offer/price/schedule, finite staff work, cash/no-credit, net-profit and earned-investment gates. Replaced only the three fault-count/patch-cost ordering assertions with strictly fewer early departures, lower refunds and lower combined refund-plus-patch expense; additionally require cautious three-room operation to have no catastrophe. Production behavior was not retuned. Fault counts remain reported without claiming that this comfort policy substitutes for preventive servicing. A retry must reach the unchanged earned-capital continuation before this gate passes.

Production seed1947, prescribed supplies, D1 08 through D4 11 checkout tail, no fourth cohort,1350 operating cost each:

| Policy | Receipts | Gross | Refund | Patch expense | Net | Minimum cash | Faults | Early exits |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
|3 sequential|9|2790|0|0|1440|300|0|0|
|4 sequential|12|3330|0|200|1780|300|1|0|
|5 sequential|15|4230|750|800|1330|450|4|4|
|6 sequential|18|5580|1965|600|1665|750|3|11|
|5 warm assignment|15|4230|525|1000|1355|100|5|3|
|5 warm/blanket/turnover|15|4230|150|800|1930|100|4|1|

The managed policy made6 finite welcome visits, delivered5 blankets, spent197.2 staff seconds, inspected10 vacant valves and adjusted9. It retained14/15 stays to normal checkout versus11/15 unmanaged. These are prescribed accepted offers at their existing reference prices, **not automatic180-rate demand/balance evidence**. Final phase9 must measure actual sales and preventive-service strategies.

## Physical scene gate

**2/2 passed**,103.207018 seconds, `Logs/rhythm041-phase6-playmode.xml`:

- `DatedMorningWakeWalksFromRealBedToShowerBeforeHotWaterStarts`:35.337313s. Production automatic booking/rhythm, labelled initial model check-in and time advance, real bed staging and normal Update wake, actual shower walk/privacy/water onset and following room activity.
- `GuestUsesRealBedAndEnclosedShowerThenLeavesAndReturnsThroughClosingDoor`:67.836861s. Existing physical bed/shower/outing/return compatibility regression; its diagnostic activity fixture is not evidence of a naturally selected new outing.

Economic retry: **2/2 passed**,206.9083011s, `Logs/rhythm041-phase6-economy-attempt2.xml`. The six policy rows reproduced exactly. Continuing the same managed hotel earned2680 cash, paid1800 for capacity and retained880. Buying capacity did not repair condition38.346 or rewrite stress/load; same-load ratio changed1.0137→.8109. A real sixth owner raised contemporaneous ratio.9983→1.1250; through D5 00:06 peakratio1.2765/stress.4520 caused no additional failure. Purchased ratedcapacity5.750 persisted. No cash injection, guest/load override or rebuilt checkpoint.

Remaining limits: final automatic-demand/service balance, fresh Windows SOLO/LAN and pixel/human checks. This phase does not claim them.
