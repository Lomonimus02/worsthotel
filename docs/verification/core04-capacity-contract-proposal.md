# Core 0.4 phases 5–8: proposed implementation contract

Read-only design, 2026-09-27. **Not implemented or runtime verified. Runtime work waits for the phase 4 gate.** Source: exact request sections 12–39, 50–59, 65–71, 75–81 and phases 93–96 in `docs/CORE04_REQUEST.txt`; current boiler, physical repair sequence, room demand, circuits and continuous accounting were inspected. This refines `core04-capacity-baseline.md`; the calibration below replaces that document's illustrative stress rate.

## Boundaries and public surface

Extend `BoilerSystem`, `ElectricalSystem`, `RoomSystem`, `EconomySystem` and small `HotelSimulation` partials. No parallel infrastructure manager, upgrade tree, debt system or generic work-order layer. Preserve the historical shift kernel as an explicitly legacy fixture; production continuous operations use the new calculation. Existing room loss, real valves, real showers, heater consumers and physical repair controls remain the causes.

| Owner | Proposed API / state | Purpose |
| --- | --- | --- |
| Shared read-only enum | `CapacityBand { Comfortable, Strained, Overloaded, Critical }` | A common label; no shared mutable capacity subsystem. |
| `BoilerSystem` | `RatedCapacity`, `EffectiveCapacity`, `LoadRatio`, `Reserve`, `Stress01`, `CapacityBand`; existing `Load`, `HeatingOutput`, `Condition`, `Pressure`, `Failed` | Demand stays an actual source sum. Reserve can be negative; stress is bounded 0–1. |
| `BoilerSystem` | `EmergencyPatchActive`, `MaintenanceEndsAt` (0 means inactive), `MaintenanceInProgress`, `CapacityUpgradePurchased` | Persistent equipment state. `MaintenanceRemaining(now)` is derived from the single hotel clock. |
| `RoomSystem` | `HeatingDemandForRoom(RoomState room, GuestStay occupant, LivingHotelSettings living)` returning a small immutable `RoomHeatingDemand(RoomId, GuestId, SpaceHeating, HotWater)`; aggregate `HeatingDemands` on simulation | Nonnegative room-attributed inputs; distinguish actual hot water from valve-controlled heating. An empty room can still have a real open radiator. |
| `ElectricalCircuit` | existing `Capacity`, `ActualRequestedLoad`, `DeliveredLoad`, `OverloadSeconds`, `Tripped`; add derived `LoadRatio`, `Reserve`, `Stress01`, `CapacityBand` | Keep the real consumer registry and warning-before-trip behavior. No invented appliances. |
| `ElectricalSystem` | `UpgradedCircuitId` (empty, A or B); internal capacity install | One purchased electrical upgrade total, applied to the player's chosen existing circuit. This is not one upgrade per circuit. |
| `HotelSimulation` | `EmergencyPatchBoiler(int actorId)`, `BeginBoilerMaintenance(int actorId)`, `PurchaseBoilerUpgrade(int actorId)`, `PurchaseElectricalUpgrade(int actorId, string circuitId)` | Authority, actor, state, affordability and duplicate checks before any debit. GameSession/physical wrappers enforce context/range. |
| `HotelSimulation` | `ForecastBookingLoad(string offerId, int roomId)` → read-only `BookingLoadForecast` | Current measured band, typical proposed heating demand, an illustrative one-shower peak, effective capacity and selected branch headroom. Invalid/stale offers return an explicit unavailable result. No simulation mutation. |

API names are a concrete proposal for the next gate, not a claim that these members already exist. Put new immutable boiler operating fields in a small `BoilerCapacitySettings` value owned by `BoilerSettings`, with a trailing optional constructor parameter for old call-site compatibility. `BoilerConfig` authors them. Reuse `EconomySettings` for money; `RoomInfrastructureSettings` for radiator/room-loss coefficients; `ElectricitySettings` for circuit additions/recovery. Configure the continuous calculation with the authoritative `Operations.SecondsPerDay` once; do not introduce another clock or hard-code 720 into subsystem rates.

## Phase 5: causal capacity, room demand and stress

`EffectiveCapacity = RatedCapacity * (ConditionCapacityFloor + (1 - ConditionCapacityFloor) * Condition / 100)`; initial floor 0.85. Rated capacity starts at the existing 4.2. Poor condition reduces capacity moderately and increases stress rate; it does not directly halve all heat output. A patch adds a visible stress-rate penalty rather than several hidden capacity/output penalties.

Replace the current signed global radiator correction. Proposed per-room arithmetic, all configurable:

- Space heating = occupied profile demand × quiet multiplier × valve multiplier × `(1 + max(0, HeatLoss) * HeatLossDemandFactor)`. Valve 0 gives zero; normal valve 1 gives one; higher settings add the authored demand step **within that room**.
- A vacant open radiator has a small explicit space demand (trial 0.08 at setting 1), using the same valve/loss factors. That demand belongs to the physical radiator; it is not a fake guest. An assigned guest going away removes hot-water demand, but their radiator keeps operating until its valve changes. This is deliberately different from the old presence-based global sum.
- Hot water = profile demand × the positive shower increment above quiet demand, only during an actually staged shower in the assigned room. Closing a radiator cannot cancel shower demand. Television/phone activities do not secretly create hot-water consumers.
- Existing `TickTemperature` remains the room thermal response, with the same authored heat loss and actual powered portable heat. The aggregate output factor is `min(1, EffectiveCapacity / demand)` when running; failed output remains configured, maintenance output is zero. Do not compound this with the old large condition-output loss in continuous mode.

Trial settings: strain begins at ratio 0.85; overload above 1; critical at stress 0.8 or failure. Stress gain per hotel hour = `max(0, ratio - 1) * 0.25 * (1 + (1 - condition/100) * 0.5)`, multiplied by 1.25 while patched. Below capacity, recover `max(0, 1 - ratio) * 0.25` per hotel hour. Stress reaching 1 under current overload causes failure. Safe idle operation does not manufacture a failure from low condition alone. Trial running wear: 3 condition points per hotel day, scaled by utilization capped at 1, plus 12 × overload per day. Failed/maintenance equipment has no operating wear.

Operating pressure follows actual overload and stress, e.g. current base 40 + existing overload factor 70 × overload + configured 30 × stress. It is warning/physical feedback, not a second independent condition-driven failure lottery. `FailureExposure` can remain diagnostic actual overload dwell, but must not reset the authoritative stress when load momentarily improves. Split time integration at maintenance completion; test bounded tick subdivision error.

Circuit tripping may retain its existing inspectable overload-duration model: actual excess load → warning → trip; returning below capacity gradually reduces exposure instead of pretending previous stress never existed. Retain explicit reset and real requested demand during a trip. No mandatory magnitude-sensitive replacement is needed to satisfy this phase.

Illustrative arithmetic with normal valves, all assigned rooms open, profile sequence Budget/Cold/Business repeated in rooms 101 onward, vacant demand 0.08 and loss coefficient 0.04:

| Guests | Typical demand | One cold-sensitive shower | Typical ratio at condition 85 (capacity 4.1055) |
| --- | ---: | ---: | ---: |
| 3 | 2.766 | 3.554 | 0.674 |
| 4 | 3.419 | 4.207 | 0.833 |
| 5 | 4.241 | 5.029 | 1.033 |
| 6 | 5.033 | 5.820 | 1.226 |

These are calculation checks, not acceptance results. At the trial stress rate, constant six-guest typical demand reaches full stress after roughly 16.5 hotel hours from zero; real staggered arrival, shower timing, condition, chosen valves and vacant intervals change that result. Five guests can strain without automatically failing. Six easy guests and six demanding guests must produce different outcomes. Neither calendar day nor occupancy count is a failure input.

## Phase 6: preserve the physical repair, make its outcome honest

The current physical sequence directly calls `boiler.Restart(actor)`. Add an optional `Func<int, CommandResult> restartCommand` to `RepairSequence`, defaulting to the existing method for legacy tests. Production binds `simulation.EmergencyPatchBoiler`. Factor a nonmutating `Boiler.CanRestart(actor)` so pressure, failed state, relief ownership and SOLO catch are validated **before** `Economy.TrySpend`. Successful low-level restart applies patch state before emitting `OnFailureResolved`; a rejected/replayed press cannot charge again.

Trial emergency patch: existing $200 cost; configured restored condition 40, persistent patch flag, stress lowered to configured 0.2, working heat returns. The configured poor condition is an explicit emergency outcome, even when severe overload caused failure before wear became low. It cannot be applied repeatedly to a running boiler. Do not reuse the old paid `+15` between-shift maintenance meaning. Patch persists through midnight/report until proper maintenance or another actual failure. Lower load after patch is useful; repeated unchanged overload remains dangerous.

Proper maintenance: existing $1,500 cost and condition target 95; trial downtime 2 hotel hours. Debit once at start; set an absolute completion deadline; disable actual heat and cancel relief/SOLO support. While busy, reject emergency repair and duplicate maintenance. Complete only when the hotel clock reaches the deadline: restore condition to at least target, clear patch/stress/failure and emit actual recovery once. No new-day reset or synthetic overnight thermal jump. Cancellation/refund mechanics are unnecessary for this scope.

**Minigame compatibility:** preserve green band 35–55, relief target 43, relief rate 4, failed-pressure rise, real-time latch holds, real-time SOLO catch lifetime and actor/physical-authentication rules. Failed pressure still enters the existing band through real relief. Do not multiply the physical hold/catch timers by hotel hours. A maintenance status needs to override the normal repair prompt while offline; availability guards must also exist in the model.

## Phase 7: two persistent purchases

Trial boiler upgrade +25% rated capacity (4.2→5.25), cost $1,800. Trial electrical upgrade +1 capacity to the chosen branch (4→5), cost $1,200. Values live in settings. At condition 85, upgraded boiler capacity is 5.1319: the same six-guest typical demand falls from ratio 1.226 to 0.981, while a shower peak can still overload it. One circuit upgrade lets three ordinary occupied rooms + one heater (4.55) fit; two heaters (6.55) still exceed it.

Purchases change capacity only. They do not silently restore condition, erase stress, fix a failed boiler or reset a tripped breaker. Recovery follows lower actual utilization; players still perform the relevant repair/reset. Repeat/invalid/mirror commands leave money and equipment unchanged. No installation timer is required beyond proper maintenance's separate downtime.

## Phase 8: money and approximate forecasting

Keep the existing $450 recurring operating cost; no new fee category is needed. Reference-price gross/net before refunds and maintenance: 3 mixed guests $930/$480, 4 (2 budget/1 cold/1 business) $1,110/$660, 5 (2/2/1) $1,410/$960, 6 (2 each) $1,860/$1,410. Three budget guests yield only $90 after operating cost. Existing exposure/refunds make ambitious operation risky without inventing penalty events.

Opening cash $350 is below the first $450 report charge, which occurs at 06:00 before the first 10:00 checkout payment. Test a working-capital increase to $750 (one operating bill + one emergency patch + $100 margin) rather than adding debt mechanics or raising fees. Keep actual safe/ambitious three-day receipts as the tuning evidence.

Add report aggregates `MaintenanceSpend` and `CapitalSpend`, with corresponding current-period counters. Purchases debit immediately; reports display those costs in `Net` but never debit them again. Legacy reports default these fields to zero. Successful transactions increment only the relevant checked counter; failed ones affect neither cash nor report. No unbounded purchase ledger is necessary.

Booking forecast uses the proposed stay interval and overlapping reservations, including future reservations whose guests do not yet exist. Calculate a typical simultaneous occupancy estimate at normal/current authored valves and an illustrative one-shower peak, using current condition/patch/upgrades. Label it approximate: do not run future schedules or predict exact failure times. Cancellation/moving/price edits must not leave a cached stale forecast; price affects revenue, not physical heating demand. Prefer calculating on demand from the small current booking set.

## Wire and player-facing data

Root-owned snapshot additions: boiler `Stress01`, `EmergencyPatchActive`, `MaintenanceEndsAt`, `CapacityUpgradePurchased`; electrical `UpgradedCircuitId`; current-period/report `MaintenanceSpend`, `CapitalSpend`. Existing condition, pressure, demand, exposure and trip fields remain. Derived capacity/reserve/bands recompute from authoritative config plus state; if serialized for diagnostics, validate consistency. New fields require finite/range/state validation before atomic apply; active maintenance has a finite future deadline, and an empty electrical upgrade ID is distinct from an unknown circuit. Preserve state across midnight, reports, mirrors and retained-history pruning. New commands use existing host authority/replay protection.

Normal UI: readable heating/branch bands, condition, patch marker, maintenance remaining time, purchase cost and permanent capacity benefit; current cash and approximate booking pressure. Expanded debug: rated/effective capacity, each demand component, exact stress/rates and operation deadline. Physical boiler gauge/sound and panel warnings follow real state. No automatic objective arrows or small-fault frequency increase.

## Counterfactual gate tests

1. Same actual consumers, condition and valves: +5% capacity excess raises stress without immediate failure; reduce demand and observe recovery; severe sustained overload fails later than a one-tick spike. Idle low condition and cautious 3–4-guest operation cannot cause a scripted/day-number failure.
2. Closing an empty radiator removes only its nonnegative contribution; closing an occupied room valve cannot cancel another room or its own real shower. An unstaged shower, absent/departed guest or duplicate room identity cannot invent hot-water demand. Radiator heat/demand remain aligned while a guest is away.
3. Compare identical three-day seeds/bookings at 3–4 versus 5–6 actual stays: measure reserve, temperature, stress and receipts; quiet intervals remain. Failure is a measured consequence, not a mandatory assertion for every six-guest run.
4. Keep actual SOLO/co-op physical repair authentication tests. Invalid pressure/actor/catch, insufficient cash and repeated restart do not debit. Success produces working but patched poor equipment. Same load after proper repair has better capacity/stress behavior for a meaningful interval; maintenance actually cools rooms during downtime.
5. Cross midnight and a report while failed, stressed, patched and separately during maintenance. Preserve flags/load/timing; one completion and one debit. Mirror round-trip and malformed-field rejection must be atomic.
6. Same registered circuit consumers: trip → reset without reducing load → re-trip; switch off a real heater → reset → stable. Purchase the one branch upgrade and measure reduced utilization with unchanged consumer IDs; the other branch remains unchanged; two heaters can still overload.
7. Same boiler demand before/after upgrade: increased rated/effective capacity, decreased utilization/future stress, unchanged condition/current failure. Growing bookings afterwards can create new pressure without any hidden degradation of the purchased upgrade.
8. Cash conservation: opening cash + checkout net − operating costs − maintenance − capital = current cash, including purchases before/after a report and duplicate/rejected commands. Under-booking survives but earns less; successful ambitious operation can fund maintenance/upgrades.
9. Forecast is pure, interval-aware, monotonic for the same condition/room assumptions and larger profile demand; never creates incidents, consumers or commitments. Actual shower peaks can differ from the typical estimate.

Root owns Unity/build/EXE gates. Arithmetic and source inspection are not substitutes for the final three continuous days, SOLO/LAN physical repair, purchase replication and same-load upgrade comparisons.
