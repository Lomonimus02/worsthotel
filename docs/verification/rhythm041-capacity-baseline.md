# 0.4.1 capacity/maintenance baseline and bounded proposal

Status: **read-only phase0 audit; no runtime/configuration/test edits**, 27 September2026. The complete2080-line request was read. Root owns all Unity/player execution and the strict baseline→causality→pre-failure→maintenance phase gates. These proposals do not authorize skipping a phase.

The actual power relationship and planned phase1 reproduction are in [BOILER_POWER_INTERACTION.md](../BOILER_POWER_INTERACTION.md). No direct boiler→electrical coupling was found; portable heater use supplies a real, optional indirect connection. The user's exact earlier incident remains unreproduced.

## Current continuous model, from source

- [Boiler.asset](../../Assets/_WorstHotel/ScriptableObjects/Boiler.asset) sets rated4.6/condition85. [BoilerCapacitySettings](../../Assets/_WorstHotel/Scripts/Simulation/BoilerCapacitySettings.cs) and [BoilerConfig](../../Assets/_WorstHotel/Scripts/Simulation/Definitions/BoilerConfig.cs) provide remaining continuous defaults: capacity floor .85, Strained ratio .85, Critical stress .8, overload stress gain .25/hotel-hour, recovery .25/hotel-hour, poor-condition stress factor .5, running wear3/hotel-day, extra overload wear12/hotel-day, patch penalty1.25. Legacy minute-based values still appear in the asset but are not the continuous kernel.
- [BoilerSystem.Capacity](../../Assets/_WorstHotel/Scripts/Simulation/BoilerSystem.Capacity.cs) computes effective capacity `rated × (.85 + .15 × condition/100)`. Normal output is `min(1, effective/demand)`. Thus **every ratio ≤1 delivers full output**, including the entire current Strained band .85–1. Stress only grows above1; below1 it recovers at a rate proportional to spare capacity. Near-capacity subcritical running changes wear modestly, but creates no output loss or stress buildup. This is the concrete missing middle layer, not missing threshold labels alone.
- Failure requires positive overload and accumulated stress1. No day/hour/fixed occupancy failure trigger exists. A low-condition idle boiler cannot invent a running failure. Failure output is .1, planned maintenance output0. Control of physical relief/catch and the existing repair sequence remains separate from capacity.
- [RoomSystem](../../Assets/_WorstHotel/Scripts/Simulation/RoomSystem.cs) sums attributed space heat plus physically staged shower demand. Owned rooms keep space demand while the guest is away; reservations are not consumers. Radiator settings0/1/2/3 have demand multipliers0/1/1.25/1.5 and heat multipliers0/1/1.12/1.24. Vacant radiator base demand is .08. A room's loss contributes a small `1 + loss × .04` demand factor. Shower increment is guest profile demand ×(1.6−.85), independent of radiator closure. These are actual room/activity values, not time-of-day multipliers.
- Electrical data remains independent: branches4 each; actual in-room guests .85 plus staged TV/music .25; explicit heaters2. Off heaters draw0 and do not turn on because of cold. Guest power loss removes portable heat, not central boiler heat.

## Why room temperatures can look uniform or high

[PrototypeSession.asset](../../Assets/_WorstHotel/ScriptableObjects/PrototypeSession.asset) sets base9, heat gain13 and time constant45. Per-room update approaches:

`target = 9 + 13 × boiler output × radiator heat multiplier − room heat loss + powered heater output`

`temperature += (target − temperature) × (1 − exp(−dt/45))`

The baseline target is **not a guest thermostat setpoint**. All rooms share central output and thermal time constant; room loss/radiator/heater distinguish them. There is currently no allocated room-specific central shortfall and no outside-temperature simulation. The warmest authored room101 has loss0; coldest102 loss1.4; 104 loss.4, so the brief's weak104 is an example, not the current actual weakest room.

Calculated targets, not a fresh runtime trace: full output and level1 yield22−loss; level3 yields25.12−loss. Output.8 at level1 yields19.4−loss; cold room102 tends toward18.0. A powered heater adds14 to the target. Level0/full outage tends toward9−loss without a heater. Guest extra blankets affect perceived comfort, not physical room temperature. These facts can explain candidate readings around25 or18, but cannot identify the precise cause of the user's unrecorded session. A thermal breakdown must show the actual terms and temperature trend at observation time.

## Existing maintenance and evidence

[HotelSimulation.BeginBoilerMaintenance](../../Assets/_WorstHotel/Scripts/Simulation/HotelSimulation.Maintenance.cs) **already permits a functioning boiler**. The existing full job costs1500, takes two hotel hours, disables heat, preserves its deadline across reports/midnight, then restores at least95/zero stress and removes the patch. This is a real preventive option today, although there is no smaller basic service or physical boiler-side inspection/setup flow. Do not replace the working job with a second lifecycle.

The $200 failed-boiler emergency patch uses the authenticated mechanical sequence and leaves condition40/stress.2/penalty1.25. It ends at the next actual failure or full maintenance, not at midnight. [Maintenance integration](../../Assets/_WorstHotel/Tests/EditMode/ContinuousMaintenanceTests.cs) and [physical maintenance](../../Assets/_WorstHotel/Tests/PlayMode/ContinuousMaintenancePlayModeTests.cs) already prove downtime, payment once, healthy maintenance, calendar persistence and real WAIT.

Historical0.4 evidence: [614/614 Edit](core04-phase10-editmode.xml), [75/75 Play](core04-phase10-playmode.xml), [final actual SOLO](core04-phase10-solo/20260927T025044Z-465a7488/runtime-verification.txt). The SOLO had14 real room arrivals/departures/receipts, one natural boiler failure in the D4 tail, zero electrical trips and zero recorded optional service requests. It used one declared timed staff adapter and8× clock; it is **not** a0.4.1 baseline or human rhythm assessment. [Final economic comparisons](core04-phase8.md) show cautious3/4 no failures and managed5 greater profit, but the new pre-failure changes must remeasure this balance rather than assume those outcomes persist.

## Proposed phase2 delta — verify before maintenance additions

Keep the continuous kernel, causal room rows, condition capacity curve, cumulative stress and gradual thermal response. Leave legacy fixtures explicitly legacy. No occupancy/day/hour multiplier and no paired failures.

1. Add a readable Busy band between Comfortable and Strained; use separately exposed `Failed`/maintenance state for actual outages. Avoid renumbering serialized existing enum values. Proposed configurable Busy threshold .70, existing Strained .85, overload >1 and Critical stress .8. Audit enum ordering assumptions and wire validation when implementing.
2. Introduce a modest continuous output reduction **within** the Strained range: candidate `strain = clamp((ratio−.85)/.15,0,1)`, `output = (1−.10×strain) × min(1,1/ratio)`, with the zero-demand case explicitly1. The proposed setting is `MaximumStrainedHeatLoss=.10`. This is a trial, not approved tuning. At ratio .94 output≈.94; at1 output.90; at1.12 output≈.804. Consequently room102 at level1 can lose comfort before a machine fault while a better room remains warmer. Weakness comes from actual room loss, not a hardcoded room ID.
3. Give sustained Strained demand a small nonfailure stress contribution and higher running wear, using hotel hours/days. Proposed new settings are `StrainedStressGainPerHotelHour=.03` and `StrainedWearPerHotelDay=3`. The precise candidate rates and their fixed-state implications are below. Keep **failure gated by real overload plus stress1**, so Busy/Strained alone does not become a mandatory overnight catastrophe. Exact rates require paired tests and production measurements before acceptance.
4. Expose a derived thermal-breakdown value from the same arithmetic used for integration: room current/target temperature, central contribution, loss, valve, supplemental heater, output/availability and existing attributed demand. Do not create a second formula in UI. Physical temperature must remain distinct from blanket-perceived comfort.
5. Add a bounded, change-based causal history of measured band/valve/shower/heater/stress/failure transitions. Record meaningful transitions, not every tick; retain source IDs/room and finite capacity. This supports phase1 proof and later tuning without generating gameplay chores.

Minimal phase2 tests: safe four-room workload remains stable; sustained below-failure strain measurably lowers weak-room temperature before any failure; actual cold-sensitive perception/self-response begins while boiler remains working; removing real load recovers output/stress; short >100% spike survives; prolonged real overload can fail; equal load with poorer condition worsens outcome; time subdivision does not alter the qualitative outcome; no electrical consumers appear. Phase4 will add severe guest outcomes, but phase2 must already prove real thermal/need consequences. A UI label-only pass is insufficient.

### Exact first-trial settings and arithmetic — NOT runtime measurements

Keep production rated capacity4.6, initial condition85, capacity floor.85, Strained threshold.85, Critical stress.8, overload gain.25/hour, recovery.25/hour, poor-condition factor.5, running wear3/day, overload wear12/day and patch multiplier1.25. Add only `BusyLoadRatio=.70`, `MaximumStrainedHeatLoss=.10`, `StrainedStressGainPerHotelHour=.03`, `StrainedWearPerHotelDay=3`. Do not retune the room loss, guest preferences, shower duration, electrical timers or thermal time constant to manufacture this result. These are an implementation trial proposal, **not an accepted balance**.

For working equipment, let `r = demand/effectiveCapacity`, `s = clamp((r−.85)/.15,0,1)`, `o = max(0,r−1)`, `p = (1 + (1−condition/100)×.5) × (patched ? 1.25 : 1)`. Use:

```text
output           = (1 − .10×s) × min(1, 1/r)       [r=0: output1]
stress/hour      = (.03×s + .25×o)×p − .25×max(0, (.85−r)/.85)
condition/day    = −(3×min(1,r) + 3×s + 12×o)
failure          = r>1 AND accumulated stress>=1
```

Stress remains clamped to0..1. Recovery reaches zero continuously at the Strained threshold; condition loss has no duplicate poor-condition multiplier. This extends the existing kernel rather than adding a second pressure-based failure mechanism. Current pressure can still approach `40 + 70×o + 30×stress`; its needle need not invent a pressure fault merely because the thermal band became Busy. Critical stored stress can remain visible during gradual recovery even after live demand falls. Maintenance and failure retain their separate output overrides.

The following arithmetic holds condition fixed at85 (`effectiveCapacity=4.4965`), unpatched/unupgraded, all radiators at1, no powered portable heater. The illustrative owned-room prefix101→106 uses the real production profiles **Budget, Cold-sensitive, Business, Budget, Cold-sensitive, Business**; remaining rooms still consume vacant heat. It is a selected consumer configuration, **not a measured seeded guest schedule or a predicted complaint count**. All thermal values are eventual targets, not instantaneous readings; stress rates are before clamping and before condition changes.

| Actual consumer configuration | Demand | Ratio / proposed load band¹ | Output | Room101 / room102 target °C | Stress per hotel hour | Wear points per hotel day² |
| --- | ---: | --- | ---: | --- | ---: | ---: |
| Vacant, six valves1 | .4896 | .109 / Comfortable | 1.000 | 22.00 / 20.60 | −.2180 | .327 |
| Three quiet owned rooms | 2.7663 | .615 / Comfortable | 1.000 | 22.00 / 20.60 | −.0691 | 1.846 |
| Four quiet owned rooms | 3.4190 | .760 / Busy | 1.000 | 22.00 / 20.60 | −.0264 | 2.281 |
| Four + one actual Cold-sensitive shower | 4.2065 | .936 / Strained | .943 | 21.26 / 19.86 | +.0184 | 4.517 |
| Five quiet owned rooms | 4.2413 | .943 / Strained | .938 | 21.19 / 19.79 | +.0200 | 4.695 |
| Five + one actual Cold-sensitive shower | 5.0288 | 1.118 / Overloaded | .805 | 19.46 / 18.06 | +.0641 | 7.421 |
| Six quiet owned rooms | 5.0329 | 1.119 / Overloaded | .804 | 19.45 / 18.05 | +.0643 | 7.431 |
| Six + one actual Cold-sensitive shower | 5.8204 | 1.294 / Overloaded | .695 | 18.04 / 16.64 | +.1114 | 9.533 |

¹ Assumes stored stress below.8 and working equipment. ² A rate if held for a full hotel day, **not each brief activity's condition cost**. Source demand is `.85×profileHeatingDemand×valveDemand×(1+.04×loss)`, plus `.75×profileHeatingDemand` only while a staged shower actually runs. Profile demand values are .85/1.05/1; shower increments are therefore .6375/.7875/.75. No occupancy-count multiplier is used by the proposed formula.

Why this is a reasonable first trial, and where it can fail:

- Ordinary three/four-room demand keeps full heat and recovers stress. A four-room Cold-sensitive shower lasting the current16–28 model seconds (.53–.93 hotel-hour at720 seconds/day) adds only about.010–.017 stress from zero; the underlying quiet load then recovers it. The room temperature only moves30–46% toward that temporary target during the shower. Four quiet occupied rooms for an illustrative16–20 hours, vacant for the remaining8–4 hours, lose approximately1.63–1.96 condition points/day at fixed85. This supports **no daily-service obligation for cautious ordinary use**, not a guarantee for four guests with every valve high or several simultaneous showers.
- Five quiet rooms have a visible pre-failure consequence: room102's eventual target drops from20.60 to19.79, crossing its Cold-sensitive guest's20°C tolerance boundary while the boiler still works. With the actual45-second time constant, after60 model seconds from20.60 it reaches about20.00, not an instant19.79. The same five-room fixed state adds about.32 stress over16 hotel hours, with no overload-gated failure. Guest perception, dwell, presence, self-help and actual staff actions must decide what happens next; the temperature target alone does not create a public complaint.
- A real valve adjustment is feedback, not free warmth. Current self-help raises **one level once per response**, after the physical anchor callback; it does not instantly set all valves3. Raising room102 from1→2 adds.23562 demand. With four rooms this still gives ratio.813/full output. Raising both Cold-sensitive rooms102/105 from1→2 at five-room occupancy adds.46142 total, ratio1.046/output.861: their actual targets become20.13/21.23, while an unchanged room101 falls to20.19. This is the important coupled-room tradeoff to measure, and can make the previous managed-five policy harder. Neither these boosts nor eventual valve3 states may be forced into the natural tuning run.
- Six quiet rooms held at the initial condition add approximately1.03 stress over16 hours; real condition wear and showers increase risk, while later arrival, checkout, staff load reduction and upgrades can reduce it. Thus six is genuinely aggressive without a day3 failure script. This is a **fixed-state risk calculation**, not a claim that every six-guest night must fail at a fixed time. Existing natural three-night economic comparisons must be rerun before accepting the trial; especially reject a new daily emergency-patch treadmill for managed five.
- Actual checkout lowers demand; merely going outside does not. Current ordinary checkout is10:00 and arrival window14:00–18:00, so there can be at least four hotel hours of mostly vacant demand **if departures really release ownership on time**. At the illustrative fully vacant level1 state the formula can recover.872 stress over four hours; even all vacant valves3 give demand.7344 and recover about.808. Late stays or retained owners reduce this opportunity. No reset at midday, sleep, report or midnight is permitted. Vacant-load recovery repairs stress only, not lost condition, and valves remain where people left them.

Condition and upgrades retain useful causal roles without another penalty layer:

| Same actual quiet workload | Effective capacity | Ratio | Output | Stress/hour | Interpretation |
| --- | ---: | ---: | ---: | ---: | --- |
| Five, condition40 with active patch | 4.1860 | 1.013 | .888 | +.0541 | Small overload; patch starts with stored stress.2 and costs more future stress |
| Five, condition95 after Full | 4.5655 | .929 | .947 | +.0162 | Better reserve and longevity, still near the limit |
| Six, condition85 with existing25% capacity upgrade | 5.6206 | .895 | .970 | +.0098 | Same consumers become Strained rather than Overloaded |

No Full/Basic implementation is part of phase2. The table only uses current Full/patch/upgrade states as counterfactual inputs. Phase3's Basic candidate should be compared against these real reduced-output/stress consequences, rather than made obligatory by accelerating quiet wear. Final daily costs, guest income and human workload remain unmeasured for this proposal.

### Required migration checks before a Busy implementation

- [CapacityBand](../../Assets/_WorstHotel/Scripts/Simulation/CapacityBand.cs) is currently `Comfortable=0, Strained=1, Overloaded=2, Critical=3`. Appending `Busy=4` preserves old values **but is unsafe with existing numeric comparisons**. [BoilerReadout](../../Assets/_WorstHotel/Scripts/Infrastructure/BoilerReadout.cs), [HotelFeedback](../../Assets/_WorstHotel/Scripts/Environment/HotelFeedback.cs), [ElectricalPanelPresentation](../../Assets/_WorstHotel/Scripts/Environment/ElectricalPanelPresentation.cs) and [ShiftHUD.Electricity](../../Assets/_WorstHotel/Scripts/UI/ShiftHUD.Electricity.cs) use `>= Strained` or `>= Overloaded`. Replace these with explicit semantic predicates/rank before Busy can be returned; otherwise Busy creates a severe alarm. Do not silently renumber the shared enum.
- [CapacityLabels](../../Assets/_WorstHotel/Scripts/UI/CapacityLabels.cs) maps every nonexisting branch to CRITICAL; [ManagementUI.Overview](../../Assets/_WorstHotel/Scripts/UI/ManagementUI.Overview.cs) has a similar final high-stress fallback. Both require explicit Busy handling. Keep failure/maintenance presentation separate. The electrical classifier need not start returning Busy or change trip behavior just because the enum is shared.
- [BookingForecast](../../Assets/_WorstHotel/Scripts/Simulation/HotelSimulation.BookingForecast.cs) repeats the three load-threshold expression. Use the same load classification as the boiler for prospective workload, while avoiding falsely projecting current stored critical stress into future typical demand. Forecast and live consumer arithmetic already share room helpers; preserve that.
- Add the four immutable settings as trailing optional constructor parameters, validate finite ranges (`0 < Busy < Strained < 1`, loss within0..1, nonnegative gains/wear), and explicitly author the production values in the config/asset at the authorized implementation stage. Legacy kernel stays unchanged. Existing continuous tests that assert `output=1/ratio`, exact wear or economic policy outcomes need deliberate new-formula assertions; do not hide regressions by converting those tests to legacy.
- Current [BoilerSnapshot](../../Assets/_WorstHotel/Scripts/Simulation/HotelModelSnapshot.cs) carries raw output/condition/stress, **not CapacityBand**; [SnapshotValidation](../../Assets/_WorstHotel/Scripts/Simulation/SnapshotValidation.cs) range-checks output rather than reproducing its curve. No enum wire field currently needs renumbering. Host and mirror must nevertheless use the same settings/classifier; test derived bands and restored snapshots, and coordinate any bounded history DTO separately with root. Maintenance completion, paid patch, load changes and condition changes must all recalculate the same output helper.

## Proposed phase3 delta — reuse one maintenance job

Extend the existing deadline/job with a small `BoilerMaintenanceKind` (`None`, `Basic`, `Full`) rather than creating parallel clocks or an elaborate minigame. Root must include kind in snapshot validation; deadline/availability and exact completion remain authoritative. Preserve existing full-maintenance wrapper as a compatibility path to Full if needed. Physical boiler inspection should be read-only; a short authenticated local setup starts the chosen paid job once, and the player may leave to do other work.

Candidate configurable Basic service:350 cost, .75 hotel-hour downtime, +20 condition capped at80 without ever reducing an already higher condition, accumulated stress reduced by .25 down to0. Restrict it to functioning equipment; it is not an emergency restart. It leaves a still-active emergency-patch limitation visible, preserving a reason to buy Full. Full initially retains1500/two hours/at least95/zero stress/patch removed; it is strictly longer and more restorative. All numbers are proposed initial trials, not requirements hardcoded from the brief's48→68 example.

Settings/data proposal: new Basic fields in the existing capacity/maintenance settings and economy asset; `MaintenanceKind` plus existing `MaintenanceEndsAt`; a single guarded `BeginBoilerMaintenance(actor, kind)` and shared atomic completion. Prevent double payment, overlapping jobs, start in mirror/invalid actor/state, and representability failures before cash mutation. Exact end-of-step thermal integration stays heat-off until completion. Full upgrades/condition/patch history must persist through boundaries; no fee on completion or report. Avoid a new global manager.

Phase3 tests should compare Basic vs Full from the same functioning poor-condition/stressed state, verify finite real downtime/thermal cooling and exact once-only payment/restoration, insufficient money/duplicate rejection, snapshot atomicity, midnight/report survival and actual SOLO/co-op local setup. A midday-versus-evening impact claim waits for phase6 **real schedule/demand** comparison; merely checking the hour is prohibited.

## Outstanding baseline/decision gates

- Root must run phase0 compile/tests/SOLO/LAN/pause/WAIT/calendar and make the safe checkpoint before implementation. Historical green logs are supporting context, not that new baseline.
- Phase1 must capture the specific independence/actual-heater cascade values described in the interaction document. No old forced-fault LAN result may be called its natural reproduction.
- Proposed output/stress/service prices require measured tuning, especially because automatic booking and guest rhythm arrive in later ordered phases. Do not claim eventual natural gameplay balance from a controlled subsystem fixture.
- The brief's statement that the prior long-pause crash was fixed conflicts with preserved evidence. [Alt+Tab investigation](../PAUSE_CRASH_INVESTIGATION.md) remains explicitly deferred with no root cause/fix. Preserve current normal pause/WAIT guarantees and report this limit honestly.
