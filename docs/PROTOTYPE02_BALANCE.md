# Prototype 0.2: measured living-hotel balance

The Phase 9 baseline uses the production `PrototypeSession.asset` and every attached living, needs, noise, heater, electricity and housekeeping configuration. The initial run passed **165/165 EditMode tests, zero skips**. The final Phase 9 gate also passed scene compilation, **165 EditMode and 27 PlayMode tests, zero skips**, including the added acceptance assertions. Archived initial evidence is `Logs/p02-phase9-initial-editmode.xml` and `Logs/p02-phase9-initial-balance.txt`; final gate logs use the `Logs/p02-phase9-*` prefix. Subsequent runs regenerate `Logs/living-balance-scenarios.txt` from [LivingBalanceTests.cs](../Assets/_WorstHotel/Tests/EditMode/LivingBalanceTests.cs). The older two-argument `BalanceScenarioTests` measures the Prototype 0.1 kernel and is not evidence for these living schedules.

No coefficient was changed in Phase 9 after this measurement. The existing production defaults already produced a manageable first day, a viable lower-occupancy policy, and natural simultaneous later-day problems. The final regression assertions protect those outcomes without requiring exact complaint or failure timestamps.

## What the experiment measures

Each policy runs three actual service days with seed 1947, 300 hotel seconds per day and 5 model ticks per second. Guests follow normal staggered arrival and activity schedules. Two modeled staff members travel between reception slots and hold check-in for the configured five seconds. A timed adapter supplies arrival, room-reach, departure and cleaner-arrival callbacks using authored route distances, guest speed 1.35 m/s, cleaner speed 1.5 m/s, staff speed 3.5 m/s and a 0.8-second door allowance. Cleaning still requires the model's full 30 seconds per room after arrival.

This is a deterministic balance experiment with modeled travel, not a collision, physical-navigation or two-person usability test. No failure, activity, temperature, noise or demand override is used. There are no emergency repairs, quiet requests, relocations or voluntary credits. Refunds are the actual checkout consequences of recorded exposure. Paid overnight maintenance occurs as listed; rooms are cleaned through normal departure/task callbacks during preparation. The third day enters Results and does not receive a fictional preparation period.

Both policies share day 1: budget guests in 102 and 106, a cold-sensitive guest in 101 and a business guest in 105. This layout intentionally remains an ordinary visible choice; it is not acoustically optimal.

- **Lower occupancy / premium four:** accept the four highest reference offers on days 2–3; select rooms using visible local heating loss, baseline noise and fixture state; buy proper maintenance when affordable, otherwise a patch. This heuristic does not optimize acoustic adjacency. Three business guests on day 2 and four on day 3 bring demanding customers, so lower occupancy is not a promise of no complaints.
- **Risky six:** accept the first six normal offers on days 2–3 into rooms 101–106; buy patches. After a real cold complaint in circuit B, staff choose one portable heater, take the modeled route to deliver it and leave it switched on. Circuit trips persist when this policy does not reset them. This is a compound booking, maintenance and response policy, not an isolated experiment proving the value of any one purchase.

Rates follow the normal price grid. The day-three business reference of $525 therefore becomes an agreed price of $530.

## Three-day results

| Policy / day | Boiler condition, start → end | Peak demand | First natural boiler failure | Maximum active cases | Concurrent independent causes | Gross | Refunds | Satisfaction |
| --- | ---: | ---: | --- | ---: | --- | ---: | ---: | ---: |
| Common day 1 | 85.0 → 62.0 | 5.49 | None | 1 | Noise | $1,110 | $0 | 96.6 |
| Premium four / day 2 | 77.0 → 52.8 | 5.85 | None | 6 | Noise + temperature + room condition | $1,650 | $113 | 69.3 |
| Premium four / day 3 | 95.0 → 68.6 | 5.80 | None | 5 | Noise + temperature + room condition | $2,120 | $133 | 73.6 |
| Risky six / day 2 | 77.0 → 36.7 | 8.04 | 158.6 s | 14 | Noise + temperature + room condition | $1,860 | $840 | 17.9 |
| Risky six / day 3 | 51.7 → 6.7 | 8.26 | 134.4 s | 14 | Noise + temperature + room condition | $2,370 | $1,053 | 10.9 |

An independent cause count excludes the Service need associated with an already-expired complaint. These are real simultaneous temperature, acoustic and room-condition cases, not three labels attached to one expired request. The risky third day first has those three categories concurrently at 106.4 seconds. There can be many individual cases across six guests: the observed maximum of 14 is reported openly, rather than described as only two or three complaints.

| Policy | Total gross | Total mandatory refunds | Maintenance after days 1 / 2 | Final cash | Final reputation | Infrastructure outcome |
| --- | ---: | ---: | --- | ---: | ---: | --- |
| Premium four | $4,880 | $246 | Patch $200 / proper repair $1,500 | $1,934 | 64.4 | No boiler failure or circuit trip |
| Risky six | $5,340 | $1,893 | Patch $200 / patch $200 | $2,047 | 47.3 | Natural boiler failures on days 2–3; B trips and remains tripped into day 3 |

The risky policy earns $460 more gross and ends $113 richer, despite $1,647 more refunds, because it also spends $1,300 less on maintenance. Its final reputation is 17.1 points lower and its infrastructure is badly degraded. Those are visible tradeoffs. The comparison does not show that greed is always financially worse, nor that proper repair is always the optimal final-session purchase.

Proper repair is reachable without a debug cash grant: the premium route has $1,897 after day-two settlement, spends $1,500 and starts the final day with $397. The first day cannot afford it; both policies buy a patch and carry $810 forward.

## Pacing and turnover

| Policy / day | Reception waiting, mean / max | Longest interval without a model event | Post-shift preparation to physical vacancy and clean rooms |
| --- | ---: | ---: | ---: |
| Common day 1 | 5.5 / 5.6 s | 43.2 s | 174.0 s |
| Premium four / day 2 | 5.9 / 6.2 s | 18.2 s | 163.6 s |
| Premium four / day 3 | 5.5 / 5.6 s | 23.4 s | Results; no preparation clock |
| Risky six / day 2 | 5.5 / 5.6 s | 19.4 s | 239.2 s |
| Risky six / day 3 | 5.5 / 5.6 s | 34.2 s | Results; no preparation clock |

The 43.2-second longest event gap is a quiet sleep-to-checkout period, not a guaranteed new fault. No measured service interval approaches several minutes without a model event. A long span without an active complaint can still contain arrivals, activities and useful observations; it must not be confused with empty waiting.

Preparation times include genuine cleaner travel and sequential work. At speed 8, 174 seconds divided by 8 is 21.75 seconds and 239.2 divided by 8 is 29.9 seconds. These are **mathematical lower bounds**, not measured real play durations: both players must consent, events interrupt acceleration, and movement or active work can prevent it. Similarly, 43.2 hotel seconds would take at least 5.4 real seconds at uninterrupted speed 8. The test does not automate or claim to verify player consent.

Keeping cleanup at 30 seconds is justified for this baseline because it creates visible preparation work without consuming boiler condition or guest exposure, and players can accelerate quiet portions. Whether repeated voting or waiting for one cleaner feels tedious remains a human playtest question.

## Independent natural heater counterfactual

[LivingEmergenceTests.cs](../Assets/_WorstHotel/Tests/EditMode/LivingEmergenceTests.cs) runs a separate equal-price five-guest scenario after an ordinary four-guest first day and deliberately deferred maintenance. It tests the chain with and without a heater, and with one booking moved across circuits while retaining the same corridor acoustic transmission to the secondary guest.

In the loaded-B run, a natural cold complaint in room 104 appears at 139.6 seconds. After a 12-second response/transport budget, staff switch on the heater at 151.8 seconds. Room temperature rises from 19.34°C to 23.81°C while power is available; temperature dissatisfaction falls from 0.383 to 0.056. Circuit B then warns at 158.0 seconds and trips at 170.0 seconds. The business guest in 106 experiences a new room-condition complaint at 193.0 seconds and 114.6 seconds of actual power-loss exposure.

| Same-seed response / booking choice | B trip | Secondary guest power exposure | Secondary satisfaction | Total gross | Total refunds |
| --- | --- | ---: | ---: | ---: | ---: |
| Three B occupants + heater104 | 170.0 s | 114.6 s | 26.7 | $1,410 | $525 |
| Move one booking to A + heater104 | None | 0 s | 42.0 | $1,410 | $503 |
| Same loaded bookings, no heater | None | 0 s | 42.9 | $1,410 | $413 |

The heater genuinely improves the initial cold condition before creating a new electrical problem. Changing the circuit allocation or declining the additional load prevents that trip; no scripted secondary complaint is needed. Leaving the heater on in the powered counterfactual can also overheat the room: the measured peak is 30.42°C. The existing switch is therefore an ongoing player decision, not a guaranteed positive upgrade or hidden thermostat. This limitation belongs in playtesting, not in an assertion that all workaround use improves final score.

## Numerical retention and remaining validation

The retained service length, 14-second arrival spacing, five-second check-in, normal seeded shower/loud/rest durations, thermal time constant of 45 seconds, noise transmission and buildup, 4-unit circuit capacity, heater demand of 2 and 30-second cleaning period already establish the required causal links. Increasing demand or accelerating dissatisfaction would worsen the heavily neglected six-guest scenario; reducing them solely to make the premium policy complaint-free would erase meaningful room and guest choices.

Phase 9 therefore adds production-asset regression coverage and measurements rather than changing a coefficient without evidence. The final test protects a non-catastrophic introductory day, a solvent four-guest route without infrastructure failure, genuinely concurrent later-day causes, lower gross revenue for lower occupancy and event gaps below one minute. It does not pin exact failure times or require a particular final reputation.

Human validation still needs to answer whether 14 ignored cases across six guests are readable, whether real responses can recover without repetitive repair work, whether the heater's strong output is clear enough to manage, and whether preparation with interrupted WAIT remains enjoyable. Model evidence, physical PlayMode evidence and the opt-in built-player tour are separate from an actual two-player playtest.
