# Rhythm 0.4.1 automatic economy: measured comparison and capacity calibration

## Scope and status

The headless tests use production automatic sales at $180, $750 starting cash, natural dated itineraries, real model needs/temperature/early checkout, actual model receipts, and one employee's bounded task adapter. D1 08:00→D4 11:00 covers three sold cohorts, three 06:00 operating reports, and the final checkout tail. Guest route acknowledgements have declared travel delays; this is not collider/EXE or human-feel evidence.

The original seven policies at rated capacity 4.6 exposed a genuine §88.11 gap: even warm five earned less net than cautious four. Those results remain recorded below. An explicit in-memory 4.7 trial then passed in 106.578 seconds. Root selected **4.7 only**, with no 4.8 trial. The final full production EditMode suite passed: **838 regular tests passed, zero failed, one explicit calibration test excluded**, 839 total, in 470.145 seconds. The seven-policy production matrix passed in 212.530 seconds.

Sources: [attempt 3](../../Logs/rhythm041-phase9-economy-attempt3.xml), [attempt 4](../../Logs/rhythm041-phase9-economy-attempt4.xml), [4.7 trial](../../Logs/rhythm041-phase9-capacity47-trial.xml), [final production full suite](../../Logs/rhythm041-phase9-full-editmode-final47.xml), and [test source](../../Assets/_WorstHotel/Tests/EditMode/RhythmAutomaticEconomyTests.cs).

## Historical 4.6 results: negative evidence retained

These used the original Basic affordability rule: retain only the next $450 bill after buying the $350 service. Attempt 3 completed five policies in 168.996 seconds; attempt 4 completed all seven in 217.189 seconds. The latter failed the nonnegative-cash gate for warm-five Basic (minimum −$10); warm-five control also failed the intended net-profit comparison ($390 < $810).

| Policy | Receipts | Gross | Refunds | Paid work | Basic / patches | Early exits | Faults | Fault seconds | Net | Minimum cash |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Cautious four: 101/103/104/105 | 12 | 2160 | 0 | 0 | 0 / 0 | 0 | 0 | 0 | 810 | 300 |
| All six, patch response | 18 | 3240 | 720 | 800 | 0 / 4 | 8 | 4 | 146.0 | 370 | 280 |
| All six, observed Basic | 18 | 3240 | 720 | 1150 | 1 / 4 | 8 | 4 | 146.0 | 20 | 70 |
| Five 101–105, inspect and decline Basic | 15 | 2700 | 450 | 800 | 0 / 4 | 5 | 4 | 181.8 | 100 | 100 |
| Five 101–105, observed Basic | 15 | 2700 | 450 | 800 | 0 / 4 | 5 | 4 | 181.8 | 100 | 100 |
| Warm five: 101/103/104/105/106, inspect and decline | 15 | 2700 | 360 | 600 | 0 / 3 | 4 | 3 | 142.5 | 390 | 100 |
| Warm five, observed Basic | 15 | 2700 | 270 | 1300 | 2 / 3 | 3 | 3 | 109.5 | −220 | −10 |

The cold-five treatment made five real inspection trips but could not afford Basic under its guard, and exactly reproduced its control. It did not receive an invented treatment benefit. Warm-five Basic genuinely reduced measured failed time and early exits, but spending $700 on two services made it financially worse. Six-room Basic did not reduce the four failures or eight early exits. These outcomes demonstrate that preventive work is neither free nor a cure for a persistent capacity deficit.

## Measured 4.7 clone trial

`DiagnosticCapacityTrialUsesActualAutomaticCohorts(4.7f)` cloned `SessionConfig` and its `BoilerConfig`, changed only `safeLoad` from 4.6 to 4.7 in memory, and asserted that the authored boiler remained unchanged. No cash, demand, schedule, temperature, failure or receipt was overridden.

The trial prospectively strengthened the employee's Basic affordability rule: retain the next $450 bill **plus one configured $200 emergency patch** after paying $350. The inspection control keeps the same trips and timing. This is a bounded player-policy change, not a new production purchase restriction or extra income. Both changes are declared; untreated controls isolate the capacity change because they buy no Basic.

| Policy | Receipts | Gross | Refunds | Paid work | Basic / patches | Early exits | Faults | Fault seconds | Net | Minimum cash |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Cautious four | 12 | 2160 | 0 | 0 | 0 / 0 | 0 | 0 | 0 | **810** | 300 |
| Warm five, inspect and decline | 15 | 2700 | 90 | 400 | 0 / 2 | 1 | 2 | 73.0 | **860** | 300 |
| Warm five, observed Basic | 15 | 2700 | 180 | 1100 | 2 / 2 | 2 | 2 | 73.0 | 70 | 280 |
| All six, patch response | 18 | 3240 | 720 | 600 | 0 / 3 | 8 | 3 | 109.5 | 570 | 280 |

Warm-five control earned $50 more net than cautious four at identical rates, while exposing two genuine failures instead of zero and 1148.0 seconds of overload instead of 147.5. This is a narrow measured instance of §88.11, not a claim that every aggressive choice is profitable. Six remained financially worse than four and had eight early exits; blindly opening all rooms is still costly.

Warm-five control's first failure moved from elapsed 679.75 in the historical 4.6 run (D2 about 06:39) to 1454.50 in the 4.7 trial (D3 about 08:29). Its owned guest-hours rose from 236.82 to 261.06 because fewer guests left early. Total overload duration therefore cannot be read as a simple comparable failure timer: the successful trace retains more actual consumers for longer.

Warm-five Basic still did not pay for itself in this horizon. It completed two genuine jobs, at 1550.50→1573.00 and 1604.75→1627.25, restoring condition 39.86→59.86 and 59.84→79.84. Each removed central heat for the configured 22.5 seconds, retained the patch flag, completed once and charged once. Stress was already zero at those purchases; the test correctly observes zero rather than inventing extra relief. Both jobs' exact outcome, full deadline, sampled downtime and cash conservation passed. The stronger reserve prevented the historical negative cash but did not make repeated Basic economically optimal.

## First-night readings and feedback explanation

The trial sampled live equipment and room valves every two hotel hours from D1 18:00 to D2 10:00. First-night band durations cover that 480-second interval:

| Policy | Busy | Strained | Overloaded | Critical | Comfortable |
|---|---:|---:|---:|---:|---:|
| Cautious four | 329.75 | 125.50 | 24.50 | 0 | 0.25 |
| Warm five control | 0 | 288.75 | 191.00 | 0 | 0.25 |
| Warm five Basic | 0 | 288.75 | 191.00 | 0 | 0.25 |
| All six | 0 | 0 | 199.00 | 280.75 | 0.25 |

At midnight, cautious four demanded 3.384 units (ratio .738), had stress 0 and full output. Warm five demanded 4.219 (ratio .921), had stress .193 and output .953; all room valves were still at 1. By 06:00 its stress had gradually reached .240 without a failure. At 08:00 actual staged showers added 1.575 hot-water demand, bringing total demand to 5.794 (ratio 1.268) and output to .710. By ordinary 10:00 checkout the current owners had checked out, demand fell to .571, and output returned to 1.

Six had already raised real valves by 20:00 and demanded 7.277 with one shower. At midnight its non-shower demand remained 6.527, ratio 1.428, stress .864 and output .630. Early departures then removed consumers: by 02:00 only four remained and the ratio fell below 1, while stored stress stayed high. By 08:00 actual morning hot-water use had raised demand again and the boiler was failed. No daily trigger created that sequence.

The rated-capacity increase is about 2.17%; it does **not** guarantee a similarly sized or universally large outcome improvement. Temperature, need thresholds, actual radiator self-help, finite guest interruptions, service timing and early exits form feedback paths. A small change in heat output can shift which of those real actions happens before another. The measured 4.7 readings support that feedback mechanism; the historical 4.6 run did not record an equivalent complete first-night series, so the notes do not claim a fully paired attribution for every changed action.

## Final 4.7 production results and remaining limits

Root selected authored capacity 4.7 and retained all heat-loss, stress, wear, service prices/durations, refund and early-checkout settings. The regular seven-run test uses the same bill-plus-one-patch Basic reserve for every policy. Original negative XML traces remain historical evidence; they are not overwritten or relabelled as passes.

| Final production policy | Receipts | Gross | Refunds | Paid work | Basic / patches | Early exits | Faults | Fault seconds | Net | Minimum cash |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Cautious four: 101/103/104/105 | 12 | 2160 | 0 | 0 | 0 / 0 | 0 | 0 | 0 | **810** | 300 |
| All six, patch response | 18 | 3240 | 720 | 600 | 0 / 3 | 8 | 3 | 109.5 | 570 | 280 |
| All six, observed Basic | 18 | 3240 | 720 | 950 | 1 / 3 | 8 | 3 | 109.5 | 220 | 270 |
| Five 101–105, inspect and decline Basic | 15 | 2700 | 360 | 800 | 0 / 4 | 4 | 4 | 166.8 | 190 | 100 |
| Five 101–105, observed Basic | 15 | 2700 | 360 | 800 | 0 / 4 | 4 | 4 | 166.8 | 190 | 100 |
| Warm five: 101/103/104/105/106, inspect and decline | 15 | 2700 | 90 | 400 | 0 / 2 | 1 | 2 | 73.0 | **860** | 300 |
| Warm five, observed Basic | 15 | 2700 | 180 | 1100 | 2 / 2 | 2 | 2 | 73.0 | 70 | 280 |

The four previously cloned strategies reproduced their measured outcomes exactly. Cold five still could not fund Basic while preserving the declared reserve, and its treatment exactly matched its inspection control. Six-room Basic executed one real 22.5-second job at 830.50→853.00, raised condition 39.61→59.61, retained the patch flag and charged $350 once, but did not avoid a failure or early exit. Its net fell from $570 to $220. Together with warm-five Basic's $70 net, this remains explicit evidence that these observed service policies were not financially optimal over the three-cohort horizon.

The passed production gate includes all real arrivals, model physical exits, unique receipts, matched immutable itineraries, complete service downtime/outcomes and exact cash accounting. Cautious four stayed fault-free without routine paid service; warm-five control earned more net while carrying more measured pressure. Other five/six-room choices remained less profitable. The calibration diagnostic remains an explicit optional test; it was the one deliberately excluded case in this full-suite run, not a hidden failure.

At $180 a fifth room supplies only $540 additional gross over three cohorts. One $350 Basic leaves $190 of that margin for additional refunds and repairs; Basic plus one $200 patch already uses $550. Higher gross alone never satisfies the profitability criterion.

Human pacing, actual finite physical travel, targeted physical regression and actual SOLO/LAN EXEs remain separate gates. This result closes the full EditMode production gate only. The prior Alt-Tab investigation remains deferred and is not claimed fixed here.
