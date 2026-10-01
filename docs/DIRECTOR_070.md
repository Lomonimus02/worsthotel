# 0.7.0 — Hotel Director and situations

## Current production inventory, inspected before QA

The starting revision was 73ccf78 (0.6.9). The production scene uses continuous operations, a 480-second day, four initially sold rooms out of six operational base rooms, physical staff work and a separate North Wing. Legacy three-shift fixtures still exist; they are not the production calendar.

| Production systems | Current integration / existing verification |
| --- | --- |
| Calendar, pause/resume, SOLO, staff sleep | HotelGameClock, HotelSimulation.Operations, LocalCoopBootstrap, StaffSleep; ContinuousCalendar, PauseDiagnostics, Solo, StaffSleep scenarios |
| LAN, ownership and replicas | LanSession input leases, GameSession snapshots, LanWorldReplicator; LAN protocol/world/service snapshots and input scenarios |
| Automatic bookings, room sales/pricing, assignment | AutomaticBooking, Sales, ContinuousBooking, room-key handoff; automatic sales/booking/forecast tests |
| Contract, operating cost, receipts, ACCOUNTS, game over/restart | Independent financial deadlines, OwnershipContract and SupplyFinance; contract/economy/settlement tests and physical book scenarios |
| Supplies/laundry, reputation and demand | Housekeeping.Supplies, GuestServiceSystem.Supplies, Supplies and Sales; OperatingSupplies and SupplyFinance tests |
| Boiler/circuit upgrades, insulation, North Wing | HotelSimulation.Upgrades/Progression, independent A/B capacity; EquipmentUpgrade and capacity/wing reception scenarios |
| Room board, disorder, linen, turnover, keys and lockout | OperationalWork, Housekeeping, RoomKeySystem, physical cleaning/bed targets; first-day, room-key and operational-chaos tests |
| Guest schedules, privacy, presence, response, early departure | Dated schedules and actual staging, physical doors, GuestResponse and persistent causal incidents; rhythm/privacy/needs/early-checkout tests |
| Phone, service promises, physical blanket receipt | GuestServiceSystem communication/intents and physical shelf pickup; natural-service/physical-blanket tests and GuestLifeCadence scene |
| Luggage, storage, cart, collision/knockdown | Actual owned items, carry/cart constraints, GuestPhysicalReaction; luggage/cart/grab and special-guest scenarios |
| Special guests | Touring musician with amplifier, six-bag overpacker, night owl; SpecialGuestTests and physical special-guest scenario |
| Boiler, preventive work, failure/repair | Measured guest/room load, dated paid maintenance, manual repair; capacity/maintenance/repair/heating regressions |
| Electricity, blackouts and heaters | Actual circuit consumers, retained breaker stress, independent A/B upgrades and powered portable heaters; electricity/boiler-power tests |
| Noise and temperature | Source-specific adjacency propagation and thermal equilibrium, real radiator self-help; Noise, RoomThermal, NeedsSituation tests |

## Architecture

HotelDirector is an optional host-owned layer inside HotelSimulation. The production Session asset enables it. Historical standalone model fixtures remain explicitly independent of the new director. Data-driven definitions specify identity, kind, size, weight, cost, time window, minimum day, repeat permission, duration and cooldown. Small concrete eligibility/action methods connect definitions to existing systems; there is no generic quest language.

Pressure counts waiting/locked-out guests, deduplicated communicated concerns, actual turnover, imminent unready arrivals, accepted undelivered baggage, damaged occupied lamps and infrastructure warnings/failures. An animation in a healthy room adds no pressure. Existing authored situations contribute modest occupancy of the budget. Configurable bands are Quiet/Active/Busy/Overloaded. Selection requires sustained Quiet, an awake operating hotel, affordable eligible candidates and a cooldown/history check. Sleeping/accelerated time does not earn quiet dwell. Night is intentionally quiet. First-day budget is 3, grows by 0.5 per day and 0.4 per operational room above six, capped at 6. No fixed event times or mandatory daily count exist.

The initial deck has four minor, four medium and two headline opportunities:

| Size | Premise | Required reality / natural continuation |
| --- | --- | --- |
| Minor | Forgotten key | A checked-in guest actually leaving the room with their own key; leaves it inside, returns through ordinary lockout/reception/master-key logic |
| Minor | Luggage help | Existing readiness-related storage request and actual unhandled baggage; staff may accept or ignore the existing service |
| Minor | Extra blanket | Actual mild cold episode, no duplicate case/receipt, existing guest contact and physical blanket flow |
| Minor | Schedule promise | Existing wake-up or late-checkout eligibility at a real schedule boundary; normal promise and deadline |
| Medium | Musician rehearsal | Awake touring musician, delivered instrument case and powered amplifier; real rehearsal route, sound and circuit consumer |
| Medium | Noisy evening | Available noisy guest and powered room; real loud activity, normal neighbours and quiet requests |
| Medium | Visitor | Available ordinary guest and no other active visitor; physical entrance/reception/room/exit, allow or dismiss, ignore remains possible |
| Medium | Worn lamp | Already worn, used and powered lamp during room use; real broken lamp requiring the existing bulb repair |
| Headline | Social evening | High occupancy and available musician with actual equipment; longer rehearsal, no forced complaint/trip/departure |
| Headline | Special arrival | A real vacant operational room and no unanswered special offer; premium optional reservation, favouring overpacker near arrivals and night owl later |

Visitor effects start only after physical room arrival while the host is present: a real circuit consumer, extra heat demand, conversation noise through the existing adjacency graph and additional measured room use/disorder. No flat satisfaction penalty or new fee economy. Visitors leave at their deadline, host departure/sleep or staff request. New visitor bodies use the existing original character kit and authored routes. Staff interaction travels through ordinary host-authoritative physical input; LAN carries their actual poses and model state.

Director history, budget and visitor state are copied and validated with the model snapshot. Clients do not select or advance situations. Version 0.7.0 uses LAN 31 / model schema 25. User-facing discovery remains speech, sound, people, equipment and the reservations book; internal pressure is visible only in F2 diagnostics.

Quiet dwell is 28 active hotel seconds, consideration interval 4 seconds, recovery at least 40 seconds plus size. The global window is 07:00–23:00 with narrower per-card windows. Brief Active pressure may spend previously earned quiet credit on a minor premise or optional special enquiry; Active itself never earns credit. Busy/Overloaded and sleep clear it. The configurable first-day minor limit is one, reserving the remaining two budget points for a medium premise if eligible. Natural guest requests remain independent of this limit.

## QA status

The focused final model run passed **32/32**: 13 director/deck regressions and 19 existing LAN model/agency/service snapshot regressions. It covers eligibility, quiet dwell, busy suppression, budget/cooldown/history, first-day budget reservation, physical-arrival-gated visitor effects, real amplifier prerequisites, optional booking acceptance, deep replication and malformed-snapshot rejection.

The broad archived EditMode run produced **702 passed, 222 failed, 1 skipped (925 total)**. It is not a green game-wide suite. Large groups assume an initially clean room 101, the former 720-second day, old opening cash or the old circuit mapping. This pass also found an actual optional-snapshot compatibility issue: Unity deserializes missing operation data into an empty object. The explicit mode bit now canonicalizes that absence; the relevant LAN suites passed on rerun. Remaining archive failures have not all been proved pre-existing and are not hidden or disabled.

The production scene observation ran from **Day 1 08:00 to Day 3 09:00** at the ordinary 480-second day length. Guests physically reached reception and rooms. Staff model adapters performed routine room work, key handoffs and service responses every eight hotel seconds; sleep used the real bed interaction. There were five room preparations, four first-morning checkout receipts ($720), paid laundry, ordinary operating charges and a paid contract, ending with $1180 and no ownership loss. Director choices included a forgotten key, a blanket request and a visitor; a Busy interval suppressed new choices. This is scene integration evidence, not a human difficulty or boredom verdict. The observation motivated the configurable one-minor limit on Day 1 to leave budget for a medium premise.

The post-tuning first-day observation selected **ForgottenKey at 112.8 hotel seconds** and **Visitor at 169.8 seconds**, spending 1 + 2 points. A waiting guest between those choices deferred the medium opportunity. These timestamps are observed output from this state, not authored trigger times.

The focused physical visitor scenario passed: actual entrance/reception/room/exit travel, normal aimed allow/dismiss input, real room-side position and replicated physical pose. Existing physical supply-book ordering/delivery and the three-special-guest scene also passed. The latter includes actual musical equipment, cart handling and a North Wing guest route, with explicitly funded/calendar-staged fixtures.

Actual Windows startup was observed through Computer Use with no startup exception in the new player log. The user then took control of that window and asked that it remain untouched. The five-minute manual pause/Alt-Tab check was consequently not performed; an old freeze is not claimed fixed by this milestone.

The compact rerun passed cart loading/transport/retrieval, pause/resume without wall-clock catch-up, physical phone/door conversation, contract/book/payment/purchase/game-over/restart, the enquiry letter, production first-day pacing and sleep into the morning. Remote physical key ownership, input replay/lease expiry and release also passed after correcting the fixture's approach from inside the reception counter to its staff aisle.

QA found an actual level regression: the previously moved baskets occupied the same outer foot corner as the armchairs. All ten authored baskets and the scene builder now use the clear inner foot position. This fixes the real overlap rather than weakening the aimed physical-work assertion. Electrical fixtures now account for odd/even room circuits, shared hall bindings, unopened wing lights and the heater's actual ON/OFF switch label; the real no-power state is checked separately.

The physical-fix rerun passed room waste/towel/chair work and the staff-key lockout. The final electrical rerun passed actual cover/breaker interaction, overload warning and trip, re-trip with the unchanged heater load, heater shutoff and a stable reset. The selected ten compact scenarios therefore passed on their latest targeted runs, not as one fabricated all-green archive run. Earlier failures remain in the recorded XML.

The Windows development build succeeded in `Builds/Windows-0.7.0-r2`; the `Играть 0.7.0` shortcut points there. The earlier `Windows-0.7.0` player was kept intact because the user was playing it. The r2 build adds the basket clearance and concise visitor action labels.

The complete Windows package is distributed as the [v0.7.0 prerelease](https://github.com/Lomonimus02/worsthotel/releases/tag/v0.7.0). [Package verification](verification/director070/release-package.txt) checked all 281 ZIP entries against their source SHA256 hashes; this is archive verification, not another gameplay test.

The final **two-process Windows LAN run passed on both host and client with zero errors** (`20261001-224943-fcf0d4b1`). Normal NGO transport carried a real remote key pickup/release with exclusive ownership, a remote visitor decision, the visitor's actual route to the room, visible guest/visitor replicas and agreeing cash/contract/history/budget. The read-only client could neither tick nor author director decisions. This focused fixture openly uses one manually booked guest, a visitor-only deck, three-second quiet dwell and explicit empty-staff approach poses. It does not manufacture the visitor's route callbacks or use diagnostic RPCs.

Evidence is retained in [verification/director070](verification/director070), including the broad archive failure list, targeted XML, multi-day traces, physical screenshots and both LAN reports. The regular Windows build log and intermediate runs remain in ignored `Logs`.

Remaining limits: the full archived regression suite is not green and still needs fixture migration plus investigation of unclassified failures; LAN was checked on localhost, not two computers; long manual pause/Alt-Tab was deferred while the user played; human acceptance of pacing and difficulty is still pending. No test was disabled to obtain the focused passes. This milestone does not claim an old freeze is fixed or that every archived failure is harmless.
