# Core 0.4 — capacity / maintenance / economy baseline

Phase 0 read-only audit, 2026-09-26. Authoritative request: `docs/CORE04_REQUEST.txt` (the attached 2,385-line request was read completely). This document records existing code and proposed contracts; **none of the proposals below is implemented or runtime-verified**. No Unity or player process was started by this audit. Baseline checkpoint supplied by the root agent: `98d0813`. Existing pause-investigation work was not modified.

## Existing dependency chain

| Responsibility | Current source and behavior |
| --- | --- |
| Composition / production data | `Definitions/SessionConfig.cs` reads `ScriptableObjects/PrototypeSession.asset`, `Boiler.asset`, `Electricity.asset`, `Economy.asset`, `LivingHotel.asset`, room/guest definitions and infrastructure settings into immutable runtime settings. |
| Actual boiler demand | `HotelSimulation.Guests.cs:8` sums guest profile demand × current physical activity multiplier, then `RoomSystem.ExtraBoilerDemand` adds radiator adjustments. `HotelSimulation.Presence.cs:SetActivityOutput` selects quiet/shower/loud multipliers; real staging gates the shower activity. |
| Boiler operation | `BoilerSystem.cs` owns condition, load, pressure, heating output, failure and restart. `BoilerSystem.Solo.cs` owns the existing real-time valve catch. |
| Local heating | `RoomSystem.TickTemperature` uses boiler output, each real valve setting, authored heat loss, powered heater output and exponential thermal response. Radiator mutation goes through `HotelSimulation.Infrastructure.cs:ApplyRadiatorSetting`. |
| Actual power demand | `ElectricalSystem.Tick` constructs real room/TV/heater consumers; `ElectricalCircuit` retains requested demand through a trip while delivered power becomes zero. A=101–103, B=104–106. Physical heater placement, switch and circuit state already govern its output. |
| Physical emergency work | `RepairSequence` and `RepairSequenceController` validate relief pressure, panel/isolation, latches, actor identity and physical aim/hold. SOLO and co-op share the authenticated sequence. Restart currently restores operation without restoring condition or charging money. |
| Paid maintenance | `HotelSimulation.ApplyMaintenance` currently runs once between old shifts. CheapPatch adds 15 condition for $200; ProperRepair restores at least 95 for $1,500. Both are immediate and clear failure. No explicit patch penalty, maintenance downtime or upgrade state exists. |
| Money / receipts | `EconomySystem.CalculateReceipt` already applies bounded refunds/credits. `Settle` charges $450 operating cost and credits unique receipts, idempotently per old day. `TrySpend` already supplies an atomic affordability/debit operation. |
| Forecast | `PlanningSystem.ProjectedLoad` is only the accepted profiles' sum; `IsOverSafeLoad` compares it to immutable nominal 4.2. It has no current condition, valve, activity, circuit or future-time context. |
| Replication | `SubsystemSnapshots`, `HotelModelSnapshot`, `SnapshotValidation` already carry boiler condition/pressure/failure/exposure, circuit trip/exposure and economy. Root owns their continuous-calendar extension. |

Paths above are under `Assets/_WorstHotel/Scripts` unless otherwise stated. `docs/ARCHITECTURE.md` describes the current 0.3.2 architecture; older `BALANCE_PLAN.md` numbers explicitly refer to the 0.2/legacy measured policies and are not evidence for 0.4.

## Production numbers and concrete migration hazards

Production boiler: condition 85; nominal safe load 4.2; wear 4 condition/minute plus 16 × overload/minute; pressure warning 70; failure pressure 85 for 8 consecutive seconds; thermal response 45 seconds. Quiet/shower multipliers are 0.85/1.6. Profile demands are budget 0.85, cold-sensitive 1.05, business 1.0. Room heat losses 101–106 are 0, 1.4, 0.2, 0.4, 0.3, 0.7. Valve settings are 0–3, with +0.25 demand/step above setting 1.

1. **Time scaling must be settled before tuning.** `BoilerSystem.Tick:74` applies base wear even at zero demand and while failed. Current 85 condition reaches zero after about 21.25 ticking minutes without overload. Merely extending the old 300-second session would change the game drastically. Lamp wear likewise uses absolute ticking seconds. Phase 1 must preserve the baseline time units until Phase 5 explicitly converts wear/stress to the chosen calendar duration.
2. **Current boiler recovery is not persistent stress.** Pressure relaxes gradually, but `FailureExposure` resets instantly below threshold. `BeginService` resets pressure/exposure for an unfailed boiler. Low condition alone raises target pressure even at zero demand; condition zero has a target above failure pressure without guests. There is no literal forced Day 3 failure, but continuous idle wear can create the same unwanted practical result.
3. **Radiator demand is a signed global correction.** `RoomSystem.ExtraBoilerDemand:20` sums `(setting−1)*0.25` for every room. Closing an empty room's valve can therefore subtract from another guest's shower demand; all normal-setting empty radiators consume zero in that formula while still warming rooms. Phase 5 should produce nonnegative room-attributed demand components and keep hot water distinct from valve-controlled space heating. Do not add invisible balancing consumers. The existing heat-loss/temperature model remains useful.
4. **Calendar reporting must not call old lifecycle resets.** `StartShift` clears guest lists/incidents/requests, resets the clock and invokes `Boiler.BeginService`. `EndShift` switches all heaters off, releases every room, zeroes boiler demand and stops ticking. `ApplyMaintenance` sets load zero, advances empty-room temperature by a synthetic 180 seconds and refills supplies. These are operational actions, not legitimate report-boundary side effects.
5. **Paid patch is not yet the requested emergency patch.** It can take a healthy condition to 100, has no persistent marker and shares the full repair method. The physical restart and paid maintenance should become two clearly distinct outcomes while retaining the working mechanical interaction.
6. **Daily income needs stay-level idempotency.** Existing settlement rejects duplicate guests within one report, not across different reports. Continuous reports must include only newly completed/unbilled stays, while costs are posted exactly once per reporting boundary. This is a root calendar/economy integration concern, not a new debt system.

## Minimal proposed model contract, after phase gate

Extend the current systems rather than creating a parallel infrastructure manager:

- A small shared read-only capacity view: `RatedCapacity`, `EffectiveCapacity`, `CurrentDemand`, `LoadRatio`, `Reserve`, `Stress01`, `CapacityBand { Comfortable, Strained, Overloaded, Critical }`. Boiler and each existing electrical circuit supply it; actual source breakdown remains inspectable.
- `BoilerSystem`: persistent normalized stress, `EmergencyPatchActive`, maintenance remaining hotel time and a single `CapacityUpgradePurchased` flag. Existing `Condition`, pressure, failure and output remain authoritative. Pressure continues to drive the physical relief interaction; it should be a readable consequence of utilization/stress, not an independent low-condition idle catastrophe.
- One configured boiler capacity increment and one electrical capacity increment. `ElectricalCircuit.Capacity` becomes derived from immutable base capacity plus the purchased installation upgrade. No upgrade tree or random electrical faults are necessary.
- Simulation-level commands validate actor/authority, current operation, affordability and repeat purchase before calling existing systems: begin proper boiler maintenance, purchase boiler upgrade, purchase electrical upgrade. Exact public names belong to the phase implementation contract. They reuse `Economy.TrySpend`; replay/repeated clicks cannot charge twice.
- Successful emergency physical restart becomes the emergency-patch outcome: fast operational recovery, a poor-condition target in the requested 35–50 band and an explicit retained penalty. Proper maintenance takes configured hotel time, disables actual boiler heat during the work, restores substantial condition and removes the penalty only on completion. Calendar rollover does none of these. Preserve real-time SOLO hold/catch timing and current authentication; do not apply calendar acceleration to rigidbody or control-hold physics.
- Prefer one authoritative normalized stress accumulator over an additional hidden failure timer. Positive overload accumulates stress by overload magnitude and condition/patch multiplier; genuine spare capacity removes stress gradually. Failure requires accumulated stress, not a single threshold-crossing tick. Idle/offline wear is zero or explicitly very small; an offline failed boiler must not accumulate operating wear indefinitely.
- Forecast query accepts prospective booking/room/time data and returns a demand range / capacity band with explanation of present condition and circuit headroom. It must not predict the exact random-free schedule/failure time or secretly run future guest activities.

New snapshot state would include boiler stress, patch flag, maintenance operation/timing, upgrade flags, derived-capacity validation and any accepted-operation identity needed for idempotency. Recompute deterministic derived values from settings + state; validate finite/nonnegative times and stress range; mirrors remain read-only. Root should serialize current/future receipts separately from bounded historical report presentation.

## Starting calibration proposal — arithmetic, not a passing playtest

Keep nominal boiler capacity near the existing 4.2 initially. A restrained condition-capacity curve such as `capacity = rated * (0.85 + 0.15 * condition/100)` gives 4.106 at condition 85 and 3.822 at condition 40. Thus condition 50 does not imply half capacity. Avoid stacking several large hidden condition penalties; make the patch penalty visible and modest.

For an illustrative mixed profile average of 0.967, normal valves and all guests physically present, current demand arithmetic gives:

| Guests | Quiet demand | One shower demand | Quiet / proposed condition-85 capacity | One shower / proposed capacity |
| --- | ---: | ---: | ---: | ---: |
| 3 | 2.465 | 3.190 | 60% | 78% |
| 4 | 3.287 | 4.012 | 80% | 98% |
| 5 | 4.108 | 4.833 | 100% | 118% |
| 6 | 4.930 | 5.655 | 120% | 138% |

These are source-demand examples, not occupancy-trigger rules or exact bookable guest compositions. Actual profiles, simultaneous showers, actual time away, radiator positions and poor-room heat loss must change the result. Once room-attributed radiator demand is corrected, regenerate this table from the production calculation before tuning. Four guests may briefly overload; six guests must be capable of recovering when demand falls. No day number is an input.

For Phase 5's first trial, stress gain proportional to positive overload (about 0.6 normalized stress per overload-unit per hotel hour), a moderate poor-condition multiplier and recovery proportional to genuine spare capacity give useful starting behavior: a 20% overload on a healthy boiler takes several hotel hours, while a short 5% peak cannot instantly fail. These rates must be converted through the authoritative calendar unit and measured against actual day/sleep/shower duration; they are not a recommendation to retain the old per-minute wear rate. Begin with only a few condition points of base operating wear per full hotel day, and stronger measured overload wear, then use three continuous days for calibration.

Power already supplies a natural secondary constraint. At current capacity 4, two quiet rooms + one heater demand 3.70; adding one TV gives 3.95; three quiet rooms + one heater demand 4.55; three TV rooms + one heater give 5.30. One upgrade to approximately 5 capacity gives the crowded branch real headroom for one ordinary heater while leaving two heaters (6.55 with three quiet rooms) risky. Preserve warning-before-trip, real consumer accounting and reset-without-removal retripping. Improving magnitude-sensitive stress/recovery should not erase that causal counterfactual.

A boiler upgrade near +25% (4.2→5.25 nominal) makes six quiet mixed guests approximately 96% utilization at condition 85 instead of 120%, while simultaneous showers can still exceed capacity. This is a measurable expansion rather than universal immunity.

## Economy and acceptance boundaries

Existing prices/costs already provide a useful first baseline. At reference prices and zero refunds, after the current $450 daily cost: three budget stays earn $90; three mixed stays $480; four (2 budget/1 cold/1 business) $660; five (2/2/1) $960; six (2 each) $1,410. Initial cash is $350. Therefore avoid adding extra fixed penalties before testing. Proper maintenance's current $1,500 cost is substantial; indicative upgrade prices around $1,800 boiler / $1,200 electrical allow ambitious operation to fund investment while remaining several careful-day profits. Prices remain proposed and should be judged together with actual refunds, repair costs and new stay durations.

Retain existing meaningful tests for source consumers, repeat breaker trip, repair actor/pressure validation, receipt compensation and purchase atomicity. Replace legacy assertions that specifically require 300-second wear, exactly three days or mandatory between-day maintenance only in the relevant phase. Add focused regressions for: report boundary preserving load/failure/patch/stress/ongoing maintenance; slight-overload dwell; stress recovery; zero-demand nonfailure; proper-maintenance completion clearing patch; both upgrades reducing stress under identical actual consumers; repeated purchase/rejected payment; forecast monotonicity without creating consumers. Final acceptance needs three continuous days in both cautious and ambitious policies, plus real SOLO/LAN interaction and same-load before/after upgrade measurements. Source arithmetic alone cannot establish pacing, enjoyment or successful integration.
