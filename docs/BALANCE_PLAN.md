# Prototype 0.2 balance and calibration

Current living-hotel measurements and all policy assumptions are recorded in [PROTOTYPE02_BALANCE.md](PROTOTYPE02_BALANCE.md). The natural heater/circuit counterfactual is in [PROTOTYPE02_EMERGENCE.md](PROTOTYPE02_EMERGENCE.md). These replace the old Prototype 0.1 constant-occupancy predictions as the balance baseline.

## Production defaults

Immutable ScriptableObjects under `Assets/_WorstHotel/ScriptableObjects` feed plain runtime snapshots. Runtime overrides never edit shared assets.

| System | Retained value / rule |
| --- | --- |
| Session | Three days; 300 hotel seconds per service; 5 simulation ticks/second |
| Arrivals | Seed 1947; first window 8 seconds; spacing 14 seconds with 4-second jitter |
| Check-in | Five-second physical hold after arrival and room readiness |
| Activities | Quiet / shower / loud; normal seeded rest and activity windows; shower increases heating demand |
| Temperature | 45-second response time; heater adds 14°C to local equilibrium while placed and powered |
| Noise | Adjacent wall transmission 0.70; corridor transmission 0.30; no recursive propagation |
| Needs | Sustained discomfort builds dissatisfaction; complaint threshold 0.30 and at least 15 seconds of exposure |
| Power | A = 101–103, B = 104–106; capacity 4; heater demand 2; warning after 6 seconds, trip after 18 seconds of overload |
| Housekeeping | One worker; physical arrival followed by 30 seconds of cleaning; nonpreemptive priority queue |
| WAIT | Two independent one-second votes; production speed 8× (configurable 4×/8×); meaningful events stop acceleration |
| Economy | Opening cash $350; daily operations $450; patch $200 / +15 condition; proper repair $1,500 / at least 95 condition |

Full coefficients and equations remain in their configuration and simulation classes. Noise, cold, power loss and room condition are measured from the actual stay. Service dissatisfaction records expired unresolved cases; it is excluded when counting independent causes. Exposure, refunds and credits must not be charged twice for the same consequence.

## Measured decision space

The common first day has four staggered guests, no forced disaster, peak heating demand 5.49 and a minor noise case. Average satisfaction is 96.6. A lower-occupancy route then selects four premium offers and buys proper maintenance when affordable. It finishes at $1,934 / reputation 64.4 with no infrastructure failure. Six bookings with patches and neglected heater/circuit consequences finish at $2,047 / reputation 47.3. The risky route earns $460 more gross but incurs $1,647 more refunds; saving $1,300 on maintenance leaves its final cash slightly higher.

The six-guest route naturally produces simultaneous temperature, noise and room-condition situations. Its worst neglected state contains 14 individual active cases across guests, which is a readability/playtest risk. A lower occupancy is safer for infrastructure, but demanding premium guests and poor adjacency can still produce complaints.

No coefficient was changed merely to make the comparison look favorable. Phase 9 preserves these measured outcomes with regression assertions. It uses timed navigation boundary callbacks and production configuration; it is not evidence of human handling, collision or enjoyment.

The natural counterfactual demonstrates that a heater improves a cold room before its extra electrical demand trips B and affects another guest. Moving a booking to A prevents that trip. A heater left running can instead overheat its room, so switching it off remains meaningful. There is no scripted cold→heater→trip sequence or automatic thermostat.

## Pacing

The longest measured gap between model events is 43.2 hotel seconds on day 1, and every measured service gap is below one minute. These events include arrivals and activities, not only complaints. Quiet periods remain valid and can be accelerated through WAIT.

Post-shift preparation takes 163.6–239.2 hotel seconds in the measured scenarios, including modeled departure and cleaner travel. Dividing by 8 gives only an ideal lower bound: actual votes, interruptions and player actions add real time. Physical PlayMode checks cover cleaner arrival, departure locks, preparation during planning and two-player WAIT separately. Human testing must establish whether the resulting rhythm feels useful rather than repetitive.

## Emergency repair and maintenance

Emergency restart restores operation, not condition. One actor holds relief in the 35–55 gauge band while another opens the panel, isolates the boiler, seats latches A/B and restarts it. Continued overload can cause another failure. Repair duration and comfort with the controls require a two-person playtest; automated actor authorization does not establish a 20–40-second human repair time.

A deferred failure persists across days. Paid maintenance is chosen between settlement and the next planning phase. Compensation decisions belong to an actual guest stay, and accepting a loss acknowledges a situation without fixing it. No scripted day transition overwrites condition to manufacture a catastrophe.

The retained legacy `BalanceScenarioTests` and `RepairCadenceTests` protect Prototype 0.1 kernel regressions. Their constant-demand numbers are not current living-hotel forecasts. Use `LivingBalanceTests` and `LivingEmergenceTests` for the 0.2 baseline.

## Next human calibration

Record booking/room decisions, reception waits, actual responses, heater switch timing, circuits, failure/restart times, WAIT interruptions, cleanup backlog, cash, refunds and session duration. Ask whether players understood why a problem appeared, could choose among responses, and created a story through their decisions. Tune the smallest relevant existing coefficient only after that evidence; rerun both measured policies and the counterfactual after any change. Do not fill quiet time with chores or mandatory disasters.

