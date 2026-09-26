# Prototype 0.4 implementation and gates

Authoritative scope: [user request](CORE04_REQUEST.txt). Baseline commit: `98d0813`; development branch: `codex/prototype-0.4`. The existing pause investigation is unresolved, not an established crash fix.

Each phase requires a successful compile, relevant tests and gameplay checks, and a changelog entry before starting the next phase. A failed gate is repaired in its current phase. Full suites run at baseline and final integration. Tests of the historical shift API remain explicitly legacy fixtures; production and new integration tests must use continuous operations.

| Phase | Work | Gate evidence |
|---|---|---|
| 0 | Inspect, compile, all tests, SOLO/LAN/pause/WAIT, checkpoint | Passed: compile, Edit 316/316, full Play 48/49 + focused corrected 1/1, fresh SOLO/LAN; see verification/core04-phase0.md |
| 1 | One calendar, nonmodal daily accounts, no hard simulation end | Passed: compile, full Edit 338/338, actual-session Play 5/5; verification/core04-phase1.md |
| 2 | Dated one-night reservations, live booking decisions, absolute guest schedules, production activation | Passed: compile, full Edit 379/379, continuous Play 10/10 across two runs, legacy Play 3/3; verification/core04-phase2.md |
| 3 | Boundary persistence, bounded history, atomic validated LAN snapshots | Passed: compile/build, full Edit 398/398, four selected Play cases across runs, actual continuous LAN host45/client121; verification/core04-phase3.md |
| 4 | Remote / DropOff / Direct integrated with guest life and finite availability | Passed: compile/scene, full Edit 441/441, seven selected gameplay cases across runs; verification/core04-phase4.md |
| 5 | Measured capacity, causal stress, load feedback | Passed: compile/scene, full Edit 484/485 plus corrected focused 1/1, four physical Play cases; verification/core04-phase5.md |
| 6 | Emergency patch versus proper maintenance and downtime | Passed: compile, relevant Edit 64/64, physical SOLO repair and reception maintenance/WAIT Play 2/2; verification/core04-phase6.md |
| 7 | One persistent boiler upgrade and one electrical upgrade | Pending |
| 8 | Income, expenses, load forecasts and tradeoffs | Pending |
| 9 | Operations board, physical feedback, player instructions | Pending |
| 10 | Three continuous days, safe/ambitious/upgrade comparisons, SOLO then LAN, package | Pending |

## Shared design contract

- `HotelGameClock` remains the only advancing model clock. `HotelCalendar` reads that clock; it does not accumulate a second time value. `OperationsSettings` configures seconds per hotel day, initial hour, report hour and booking hours. Initial design: 720 simulation seconds per calendar day, 08:00 opening, 06:00 reporting, arrivals 14:00–18:00, checkout next morning 10:00. Pacing is provisional until phase 10.
- `HotelSimulation` receives an optional trailing `OperationsSettings operations = null`. Null selects the existing isolated shift API for historical fixtures. Production passes settings and calls `StartOperations()` once on New Game. `ContinuousOperations`, `Calendar`, `CalendarDay`, `NextReportAt` and `DayReports` are query surfaces. A calendar boundary never calls StartShift, EndShift, maintenance, NewGame or a stock reset.
- In continuous mode `Remaining` means time until the next report (always positive after a boundary), and `IsServiceComplete` is always false. `Tick` advances normal model work through boundaries and publishes a report event without pausing or opening a modal. `EndShift`/legacy maintenance are refused in continuous mode.
- Guest revenue is recognized once at checkout. A report records receipts for its business interval and charges operating expense once. Empty receipt intervals do not punish reputation. First report at next-day 06:00 can legitimately precede the first paid checkout at 10:00.
- Reservation intervals are separate from the room's current occupant/current key reservation. Future bookings cannot overwrite an occupied room. The reservation ID is the stable command identity; price and room edits check current state. A stay has absolute arrival/sleep/checkout times and a single-night departure. Checkout releases ownership once; body exit/turnover protection still governs access.
- Guest lifecycle/service state is not cleared at a report. Stock renewal cannot reclaim a blanket still delivered to an active guest. Historical guests and reports need explicit retention limits, with no pruning of active references. Physical guest appearance/queue slots must not depend on a changing roster index.
- LAN remains host-authoritative, same epoch through midnight/report boundaries. Model schema/protocol change together when DTO integration lands. Snapshot validation must cover continuous calendar/reservations/accounting before mutation. Day equality must not reject otherwise valid physical actions crossing midnight; object/action revisions remain authoritative.
- Capacity and economy tuning stay unchanged until their phases. No occupancy-count condition is allowed in the failure model. Capacity additions use existing boiler, electrical, room and maintenance systems.

## Ownership

Root owns phase gates, calendar/model integration, accounts, reservation data, snapshot core, builds and actual player processes. Agency Life owns absolute schedule/presentation integration and later service intents. Agency Interface owns session/UI/WAIT/LAN integration. Agency Situations owns capacity, maintenance, upgrades and their causal tests after phase 4 passes. Agents do not run Unity concurrently.

Baseline audits: [guests](verification/core04-guest-baseline.md), [interface](verification/core04-interface-baseline.md), [capacity](verification/core04-capacity-baseline.md).

