# Architecture — Prototype 0.4 continuous hotel

This document describes the implemented runtime through phase 8 and the phase 9 presentation work. Final phase 10 continuous SOLO/LAN, package and human-play acceptance are not implied by implementation. Phase gates are tracked in [CORE04_PLAN.md](CORE04_PLAN.md); actual evidence belongs in [VERIFICATION.md](VERIFICATION.md).

## Authority and the single clock

`GameSession` composes a pure C# `HotelSimulation` and scene presentation from immutable ScriptableObject settings. Production enables `SessionConfig.continuousOperations`; a new game calls `StartOperations()` once and remains in `DayPhase.Service`. The historical planning/shift/settlement loop remains an explicit legacy configuration for regression fixtures. Ordinary production no longer commits shifts or stops after day 3.

`HotelGameClock` is the only advancing model clock. `HotelCalendar` derives dates and times from it. Production settings are 720 simulation seconds per hotel day, opening at day 1 08:00, reporting at 06:00, arrivals between 14:00 and 18:00, sleep at 23:00 and normal checkout next day at 10:00. Morning wake is an absolute schedule time. One simulation second at 1x is one real second; hotel dates are a scaled view of those seconds.

`GameSession.Update` supplies fixed ticks. Continuous ticking splits work at midnight, the next report and an active maintenance deadline, using steps no larger than one simulation second internally. Extremely large diagnostic deltas are rejected before mutation. Reporting and midnight do not replace the simulation, rooms, clock or LAN epoch and do not call `StartShift`, `EndShift`, `NewGame` or an equipment reset. `IsServiceComplete` stays false in continuous mode.

WAIT accelerates model time after the necessary local/remote consent. Rigidbody physics and actual held controls remain tied to real time. Significant known events stop WAIT; ordinary activities and private discomfort do not create an information shortcut. Pause/device-loss handling releases physical holds through the existing controls. These rules are separate from the unresolved reported Alt+Tab hang.

## Dated bookings and room ownership

`ScheduledBookingOffer` contains the application, arrival day and absolute arrival/sleep/wake/checkout times. Offers roll forward for today and tomorrow. `HotelReservation` stores the stable offer identity, room, agreed price, actor, revision and Reserved/Arrived/Completed/Cancelled status. A future reservation is not yet a `GuestStay`, occupant or physical body.

`AcceptBooking`, `CancelBooking` and `SetBookingPrice` validate actor, current offer, price grid, dates and revision before mutation. `CanReserveInterval` checks half-open stay intervals against reservations, the current occupant's actual checkout and pending room moves. A future dirty room can be reserved, but its readiness remains a physical check-in condition. Price changes do not alter building demand; there is no fixed day-3 reference-price increase in continuous offers.

At arrival the model materializes a stay and calls `GuestScheduleSystem.AttachStay` without clearing existing guests or schedules. The stable reservation ID is also the stay identity. `RoomState.GuestId`, `ReservedGuestId` and `DepartingGuestId` remain distinct: an accepted future booking cannot overwrite a current occupant, and checkout does not prove that the old body has crossed the door. The correct physical key and a clean, available room are required for check-in. Wrong or unready keys retain ownership and produce the normal refusal.

Relocation validates the whole destination interval, cleanliness, departure protection and both keys before committing. The reservation follows the committed room immediately. A proposed room move has a finite Direct intent; a valid key exchange starts the actual route, while cancellation or timeout releases its reservation. Late checkout changes the existing stay's deadline and rechecks future reservations, including preparation clearance. Neither operation creates another stay or charges a second room night.

## Guest life, perception and causal history

`GuestScheduleSystem` generates seeded trait-weighted activities. Unpack, Work, WatchTV, PhoneCall, Shower, QuietRest, LoudRoom, LeaveHotel and Pack coexist with sleep/check-in/out states. `GuestAgent` exposes physical location, activity staging, the next planned activity, response action identity/version and its active Direct intent. Body appearance and reception slots remain stable as the retained roster changes.

`GuestPresentation` owns authored travel through real room doors and reports validated anchor arrivals. Work uses the desk, sleep uses the bed, showers conceal the body, and calls use a phone prop. Temporary outings traverse the lobby and exterior exit; only then does the body become hidden in GuestAway. Return traverses the entrance and room door. Checkout while already outside does not resurrect a body or leave a false departure lock. Audio and activity outputs require actual staging.

`GuestNeedEvaluator` builds factual `GuestPerception` and accumulates need totals. Room temperature, received noise, dirty linen, lost power and a burnt lamp affect guests who are actually in their assigned room. The building itself keeps operating during their absence. In continuous mode Remote incident exposure, stage, grace and recovery are suspended while the guest cannot perceive the room; absence is not evidence of a repair. A reception trip likewise preserves the last room concern until return.

`NoiseSystem` transmits identified sources over direct shared-wall/corridor links; received sound does not retransmit. TV requires electricity, and media/plumbing require staged activity. Decorative ambience cannot invent a complaint. `IncidentSystem` retains three families—Temperature, Noise and RoomCondition—with a stable guest/family/source identity, bounded history and repeat episodes. Sustained exposure and dissatisfaction govern complaint escalation; measured recovery governs resolution. An old episode can remain in history while a new episode of the same incident is still private.

`GuestMemory` records actual complaints, warnings, credits, ignored problems, recovery and service outcomes. Repeated problems change patience. A compensation credit temporarily relieves dissatisfaction and is deducted from the eventual receipt; it does not fix the source or award repeated credits. Meaningful unresolved communicated complaints are recorded before departure; background Observed discomfort is not a staff failure.

## Communication, intents and the guest FSM

`GuestResponse` continues an existing factual incident episode or optional service case through noticing, self-help, tolerance, contact, disclosure and staff response. Eligibility, schedule context, per-stay deduplication, per-day contact budget and at most two contact attempts prevent mandatory task churn. Serious factual complaints do not depend on the optional service allowance. `BeginOperatingDay` updates that allowance's day and service horizon without clearing active cases, promises or supplies; `BudgetDay` records the actual charge date.

One useful self-help adjustment can send a mildly cold guest to the actual radiator. `AdjustRadiator` and `CallReception` are physical staged actions. `SignalGuestResponseAnchorReached` validates the guest, response ID, version, room ownership and live context before mutating the valve or starting the phone ring. Guests do not repair the boiler, replace lamps or reset breakers. Desk visits use GoingToServiceReception, WaitingAtServiceReception and ReturningFromServiceReception, keeping the same room/key and a versioned real return route.

`GuestServiceIntent` ties the interaction relationship to that same concern rather than generating a second task:

| Kind | Runtime source | Scheduling and completion |
| --- | --- | --- |
| Remote | Factual room incident | Ordinary life continues; evaluation resumes when the guest perceives the room again |
| DropOff | Communicated explicit ExtraBlanket agreement | Guest can leave; the real item goes to `room/{id}/blanket-drop`, then awaits actual availability |
| Direct | Late-checkout/wake/luggage decision, room-key exchange or compensation discussion | One current guest-owned wait blocks ordinary sleep/shower/outing until decision, cancellation or deadline |

The default Direct wait is 40 hotel simulation seconds, capped by checkout and applicable service due times. A 12-second contact lead rejects ordinary contact immediately before incompatible schedule transitions. Checkout takes priority over every service hold. Closing/reopening UI does not extend a wait. A physical room conversation holds an available guest; a phone or reception discussion retains its current response action. Completion/timeout clears the hold and preserves the real reception return before room life resumes.

Compensation discussions start only after disclosure or another actual authorized conversation about a known active cause. Repeated opens preserve the same ID/revision/deadline. Credit/refusal commands require the current token plus a matching host-validated body/door or answered-phone grant; generic ledger browsing provides no authority. An accepted blanket delivery does not automatically turn into another Direct hold. A validated room-change proposal can replace a compensation discussion atomically; an invalid destination cannot destroy it. Terminal discussion history tolerates a later private episode of the same incident, while an active private discussion is invalid on the wire.

Normal HUD, operations, guest details and service-board knowledge share the disclosure boundary. Before answering, the phone shows a generic incoming call. `IncomingServicePhoneCue` rings only for the actual staged call; the old incident-publication bell is disabled in natural mode. A known mild cause can appear inside its active physical compensation discussion without becoming a global warning. F2 deliberately exposes private diagnostic details.

## Physical supplies and preparation

`ServiceSupplyItem` binds stable authored blanket/bulb slots and guest suitcase identities to normal carry bodies. GameSession validates actual carrier, focus, range, target identity, generation and current agreement before commanding the model. Interior blanket delivery requires an available staged recipient and physical room access. Exterior delivery requires an explicit accepted agreement and the real held blanket, with expected intent revision and item generation.

An exterior parcel becomes AwaitingReceipt. It gives no comfort, service reward or bed blanket visual while the guest is away, asleep or showering. Receipt by the available guest records the item, delivery and receipt times, then improves personal perceived comfort once. That fulfills the item agreement; the independent Remote temperature incident still needs measured recovery. Cancellation/checkout/relocation before receipt releases the same body as Dropped where it was placed, allowing physical reclaim and return to stock. It never teleports back to the shelf.

Accepted luggage storage uses the guest's actual suitcase and explicit placement at the reception storage zone. A departed guest's held/dropped suitcase may be deposited as lost property without a new service reward; otherwise stranded luggage could pin the finite presentation pool forever. Wake-up promises are completed from the physical reception phone near their absolute due time. A missed or cancelled promise has one terminal outcome. Room changes synchronize relevant service destinations.

`HousekeepingSystem` owns finite dirty/clean linen and room-generation identity. Players remove dirty linen, deposit it, take a clean set and perform the configured 1.5-second real held bed action. No automatic worker performs turnover. Room occupation and actual departing-body locks gate preparation. At the next calendar date only eligible used stock slots refill; held, dropped and active delivered supplies remain intact. Neither reporting nor WAIT prepares a room.

## Room demand, equipment and spending

`RoomSystem.HeatingDemandForRoom` produces one `RoomHeatingDemand` row per physical room, exposed through `HotelSimulation.HeatingDemands`. It validates actual current ownership; future bookings and old departure bodies do not duplicate occupants. Space heating uses guest quiet demand or the vacant baseline 0.08, multiplied by that room's valve setting and heat-loss factor 0.04. Valve 0 removes only its own space-heating demand. A staged shower adds a separate nonnegative hot-water demand; it cannot be cancelled by another room's closed valve. An away owner still heats the assigned room.

Production boiler rated capacity is 4.6 units. Effective capacity is rated capacity multiplied by `0.85 + 0.15 * condition/100`; load ratio and signed reserve compare actual summed room demand against that capacity. `BoilerSystem.Capacity` accumulates wear per hotel day and overload stress per hotel hour. Poor condition and an active patch increase overload consequences. Spare capacity recovers stress gradually. Actual overload with fully accumulated stress causes failure; calendar date and guest count are not failure switches. Brief load reduction does not erase the history of sustained overload.

Physical repair ordering remains valve support, panel, power isolator, latches A/B and restart. Co-op requires another actor to hold the valve; SOLO retains the bounded valve catch. `EmergencyPatchBoiler` validates restart conditions and $200 payment before committing condition 40%, stress 0.2 and the 1.25x overload-stress penalty. The patch survives dates and reports, ending on proper maintenance or a subsequent actual failure. A later failure still leaves the boiler failed.

`BeginBoilerMaintenance` charges $1500 once and sets an absolute deadline two hotel hours later. Central output is zero during work; guests and thermal loss continue. Completion restores at least condition 95%, clears stress/patch/failure and preserves upgrades. Tick splitting ensures only the interval after the exact completion boundary receives restored output. Maintenance can start during operations; there is no required between-day maintenance phase.

One boiler upgrade costs $1800 and multiplies rated capacity by 1.25. One electrical upgrade costs $1200 and adds 1 unit to the selected A or B circuit; the hotel cannot buy both branches. Purchases preserve wear, stress, failure, patch state, maintenance downtime, consumers and tripped status. They are capacity changes, not repair commands.

`HeaterSystem` supplies room heat only for a released, switched-on, powered heater wholly inside a room. `ElectricalSystem` sums actual room/media/heater consumers for A (101–103) and B (104–106), separates requested from delivered load and preserves gradual overload recovery. A tripped line keeps displaying requested load. The physical reset remains quick; unchanged overload can retrip it. Dates/reports do not switch off heaters or reset circuit faults. The burnt bedside lamp is a concrete condition fault repaired by consuming a real replacement bulb; a power outage does not create a burnt bulb.

## Accounts and booking forecasts

Production starts with $750. `PostCompletedStays` posts each checkout receipt once, guarded by `GuestStay.ReceiptPosted`; unserved arrivals receive no fabricated room charge. `CloseOperatingDay` publishes the closed interval and subtracts the $450 operating expense once, without reposting receipt revenue. Maintenance and capital spending are recorded at purchase and included in report arithmetic without another debit. Closed reports satisfy opening cash plus net equals closing cash. Current-period collected income and all-date unpaid bookings are shown separately.

The first 06:00 report precedes the first normal 10:00 checkout. After three calendar days, the last cohort's checkout receipts can still belong to the open fourth-day period. Cohort results must therefore use stable guest IDs and the union of published and current-period receipts, not equate report number with arrival day.

`ForecastBookingLoad` is a pure derived query, safe on a read-only mirror. It clips current/future stay intervals to the candidate's interval, sweeps half-open boundaries and finds the busiest overlapping segment. A selected accepted candidate is included once. Actual materialized checkout and committed room override original offer assumptions; stale/cancelled enquiries return an explicit unavailable reason.

Forecast and live demand share `RoomSystem` arithmetic for room heat loss, valves, occupied and vacant baseline. The preview reports typical demand, the largest single-shower peak, present effective boiler capacity, maximum overlapping guests and selected-branch baseline plus currently assigned on-heaters. It does not predict future activities, repairs, switches or simultaneous multiple showers. Prices are read from the current UI/booking state and do not enter demand calculations. A currently tripped branch still needs a physical reset even when estimated future reserve is positive.

## Persistence, snapshots and presentation

Persistence means continuity within the running hotel, not a disk save system. Midnight/report processing retains active stays, guest schedules, ownership, dirty rooms, services, physical items, thermal state, failures, stress, patches, maintenance and upgrades. NewGame explicitly replaces the model and rebinds presentation. The current network epoch changes for a new session, not for a report.

Retention distinguishes live/recent/protected history owners from source-only identities referenced by another guest's incident or perception. Active room/key/response/parcel references are pinned. Terminal unreferenced records are pruned coherently; fixed supply slots are not deleted. Capacity is bounded (including 128 reservations, 256 cases, 512 responses and 1024 intents); admission refuses overflow rather than deleting protected live work.

SOLO creates one staff rig, one camera and one listener. LAN uses NGO for client input and actor-stamped commands; the host runs hotel decisions and physics. `HotelModelSnapshot` schema 11 and LAN protocol 12 (`worst-hotel-0.4-discussions12-gzip`) require matching versions. Structural and causal references, time ordering, intent ownership, receipt accounting and retained sources are validated before snapshot mutation. Mirrored state is deep-copied and cannot tick, spend, create intentions or issue physical guest-arrival callbacks.

World frames replicate bodies, doors, supplies, valve knobs, lamps and guest poses through the bounded gzip envelope. Decoded length and checksum are checked before ordinary frame validation. Host-side interaction grants include actor, model/epoch scope, target identity, proximity and revisions; UI on a client cannot invent a grant. Stale cancellation cannot cancel a replacement room move or compensation discussion.

The reception operations book exposes dated bookings, forecasts, current rooms, arrivals/checkouts, services, preparation, maintenance, upgrades and nonmodal reports. Capacity/condition/stress and branch status appear as measured operational information. Host-authorized context menus use the same keyboard/gamepad controls as existing physical interactions. `PlayerInteractor`, carry joints and release-on-disconnect behavior remain the physical action boundary.

## Verification boundaries

`tools/Unity.ps1` is the build/test entry point. Model tests exercise calendar boundaries, accounting idempotency, retention, interval overlap, causal recurrence, service intents, capacity, maintenance, upgrades and pure forecasts. Physical PlayMode cases exercise real guest staging, controller input, delivery/reclaim, panel reset, repair and UI authority. Tests using headless key/anchor or staff-travel adapters label those adapters explicitly.

Historical `-verifyHotel` and older LAN diagnostic modes use an explicit legacy configuration. Their three-shift results do not prove continuous production behavior. The phase10 continuous three-calendar-day EXE tour and final LAN/package checks remain pending at the time of this document update. Diagnostic clock acceleration and model linen/key adapters are not human pacing, real carrying or performance evidence. Read exact commands, artifacts, binary provenance and limitations in the verification records.

The reported Alt+Tab hang remains unresolved and deferred at the user's explicit request. Opt-in observation tools preserve evidence and do not establish a pause, graphics or network fix. See [PAUSE_CRASH_INVESTIGATION.md](PAUSE_CRASH_INVESTIGATION.md).
