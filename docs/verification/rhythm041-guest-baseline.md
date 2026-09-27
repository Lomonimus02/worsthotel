# 0.4.1 guest baseline — read-only phase 0

Source audit at `d7ce3fc`, 2026-09-27. The complete authoritative brief was read from the user attachment; the project copy is [RHYTHM041_REQUEST.txt](../RHYTHM041_REQUEST.txt). This note proposes later phase 4/6 integration; no runtime, scene or configuration was changed and no Unity/player was launched for this audit. Baseline test/runtime results belong to the root's phase-0 gate.

The prior Alt+Tab hang remains **unresolved and explicitly deferred**. The new brief's statement that it was fixed is not established evidence. Preserve existing pause/clock regressions; do not relabel them as a fix for that reproduction.

## What already connects infrastructure to guests

The existing order in [`HotelSimulation.TickStep`](../../Assets/_WorstHotel/Scripts/Simulation/HotelSimulation.cs) is real guest transitions → attributed electrical/noise sources → room heating demand → boiler → room temperatures → room infrastructure → guest needs → causal incidents → service responses/intents → satisfaction → clock. Continuous settlement then posts actual departures once.

- [`RoomSystem.HeatingDemandForRoom`](../../Assets/_WorstHotel/Scripts/Simulation/RoomSystem.cs) counts an actual room owner, its valve and room loss. An away guest retains space-heating demand. Hot water appears only for a physically staged, present shower; closed radiators cannot subtract shower demand.
- [`GuestNeedEvaluator`](../../Assets/_WorstHotel/Scripts/Simulation/GuestNeedEvaluator.cs) measures present room conditions, including personal blanket comfort. Sleeping in the owned room is exposure; travel/Away is not. Blanket benefit modifies perceived cold, not the room thermometer.
- [`GuestServiceSystem.Responses`](../../Assets/_WorstHotel/Scripts/Simulation/GuestServiceSystem.Responses.cs) already has observation → self-response or tolerance → actual phone/reception contact. Radiator self-help requires a real mild-cold cause, a valve below3 and a not-yet-used attempt. The valve changes only through the versioned physical anchor callback in [`HotelSimulation.GuestResponses`](../../Assets/_WorstHotel/Scripts/Simulation/HotelSimulation.GuestResponses.cs).
- [`IncidentSystem.Living`](../../Assets/_WorstHotel/Scripts/Simulation/IncidentSystem.Living.cs) has source/guest identity, per-episode exposure, dissatisfaction, patience, measured recovery, contact knowledge and repeated-problem memory. Current production exposure thresholds are20/35/65 simulation seconds and dissatisfaction thresholds .30/.65/.90; both are needed. Those are existing values, not proposed0.4.1 tuning.
- [`IncidentSystem.Remote`](../../Assets/_WorstHotel/Scripts/Simulation/IncidentSystem.Remote.cs) freezes active room-problem evaluation while its guest is away. Actual service desk trips likewise retain the last observation. Neither absence nor a changed notification is evidence of repair.
- [`GuestSatisfactionSystem`](../../Assets/_WorstHotel/Scripts/Simulation/GuestSatisfactionSystem.cs) integrates perceived room deficits only during genuine room presence. Existing receipts apply25%/50% refund bands below satisfaction60/35 in [`EconomySystem.CalculateReceipt`](../../Assets/_WorstHotel/Scripts/Simulation/EconomySystem.cs); promised credit and computed refund are combined with `max`, not summed. [`ReviewSystem`](../../Assets/_WorstHotel/Scripts/Simulation/ReviewSystem.cs) already uses exposure, complaints, staff action and service history.

This is a usable escalation path, not a reason to add another complaint generator. The missing terminal result is **natural early checkout**: [`TickLivingGuests`](../../Assets/_WorstHotel/Scripts/Simulation/HotelSimulation.Guests.cs) ends a stay at its checkout time, while the developer checkout command is diagnostic only. No severe-cause decision currently shortens a stay.

## Actual thermal explanation, without guessing runtime causes

[`RoomSystem.TickTemperature`](../../Assets/_WorstHotel/Scripts/Simulation/RoomSystem.cs) currently uses:

```
central = 13 * clamp(boilerOutput, 0, 1) * valveHeatMultiplier
roomTarget = 9 + central - room.HeatLoss + actualPoweredHeaterHeat
blend = 1 - exp(-dt / 45)
nextRoomTemperature = current + (roomTarget - current) * blend
```

The constants above come from the current [`PrototypeSession.asset`](../../Assets/_WorstHotel/ScriptableObjects/PrototypeSession.asset), not a new formula. Valve1 multiplier is1, valve2 is1.12, valve3 is1.24; valve0 removes the central contribution. All rooms share boiler output and the45-second response constant. Their authored heat losses are101=0,102=1.4,103=.2,104=.4,105=.3,106=.7. Room104 in the brief is an example; current102 is the weakest actual room.

Consequently, with no portable heater, room104's target is21.6°C at full output/valve1,24.72°C at full output/valve3, and18.35°C at75% output/valve1. This provides a code-based explanation for similar temperatures across rooms and approximately25°C or18°C outcomes. It does **not** prove which state produced a particular user's observation.

Current [`BoilerSystem.Capacity`](../../Assets/_WorstHotel/Scripts/Simulation/BoilerSystem.Capacity.cs) delivers `min(1, effectiveCapacity / demand)`: the Strained band below100% still delivers full heat. Stored stress grows only beyond capacity. Thus pre-failure overload already can cool rooms, but a below-capacity Strained label currently introduces no thermal loss itself. Phase2 should establish the shared gradual output model before phase4 assesses guest consequences; guest code should consume its real output rather than add a second overload penalty.

Developer capacity UI shows attributed space/hot-water demand, but does not yet expose the complete target/contribution/loss/temperature-trend breakdown. A later pure `RoomThermalBreakdown` should be computed by the **same helper** used by TickTemperature: current, equilibrium target, central contribution, supplemental contribution, authored loss, valve, boiler availability/output, actual room demand and instantaneous °C-per-simulation-second trend. Preferred guest temperature is separate from physical equilibrium target. It must not duplicate the formula in UI or use the blanket's perceived temperature as the thermometer.

## Minimal phase4 guest-consequence integration proposal

1. Add a small configurable continuous-operations early-departure policy, disabled for legacy shift fixtures. Consume existing factual incident severity, current episode, exposure/dissatisfaction, need-profile patience and complaint/service memory. Do not derive eligibility from occupancy count, calendar day, boiler failure or `CapacityBand` alone.
2. Require a serious persistent actual condition and substantial accumulated **present-room** exposure. Prior communicated complaint, or exhausted genuine contact attempts with no answer, supplies the escalation history. Merely creating a private incident or one minor cold tick must never qualify. Trait/profile tolerance should affect a deterministic duration, not introduce a per-tick random departure roll.
3. Keep a bounded warning/decision interval, scoped to the causal episode. Recovery stops/cancels eligibility; Away or desk travel freezes it. Record cause/episode before ending the stay so the refund/review can explain the departure after incident cleanup. A small record on GuestStay is enough: causal incident/episode, severe exposure/decision time and final early-departure reason/time. Final field names remain a phase4 contract, not implemented API.
4. Commit the result through a common natural checkout helper extracted from the existing time-based branch. Preserve `ReleaseRoom`, service cancellation, pending-move release, guest key return, dirty linen, departing-room token, existing CheckingOut→Leaving→Left route and normal `PostCompletedStays`. Do not teleport/remove a body or settle before the genuine checkout transition.
5. Normal checkout remains highest priority. Early departure must not leave an active Direct hold, an uncleared physical service return, or a relocation half-committed. Prefer committing only once the guest is in a valid owned-room state and no live Direct/relocation/action owns them; existing waits are bounded. A previously escalated sleeping guest may use the normal physical checkout route, with no extra sleep-only penalty.
6. Calculate financial consequences once in `CalculateReceipt`, retaining the original contracted price. A configurable early-departure minimum refund can use `max(existingQualityRefund, promisedCredit, earlyDepartureRefund)` capped at price. Do not both prorate revenue and subtract a second refund for the same loss. Record a factual negative review; do not invent another compensation dialogue or mandatory request family.
7. Carry the new record through model snapshots with finite/range/causal validation and host-only mutation. Mirrors can display it but cannot advance departure decisions. Retention must preserve the old guest's record/receipt without pinning unrelated active state indefinitely.

Potential ownership split once phase4 opens: guest policy/data + GuestStay/GuestAgent + narrow `HotelSimulation.Guests` transition integration; root settlement/snapshot contract; existing services keep their ownership. This proposal does not authorize changes before the phase gate.

### Existing service invariants to preserve

- Direct compensation/room-move/service decision remains finite, token/revision checked and does not renew on closing/reopening UI. A scheduled checkout already cancels it; early checkout must use that same cleanup.
- Remote room incidents remain factual and suspend while absent. Never age serious-room exposure during an outing simply to force early checkout.
- DropOff is a real parcel with item generation, guest/room identity and separate delivery/receipt. An away/asleep guest gets no comfort/reward until present, awake and staged. [`EndGuestIntents`](../../Assets/_WorstHotel/Scripts/Simulation/GuestServiceSystem.Intents.cs) cancels pending delivery and makes the same item reclaimable; early checkout must not silently consume it or reset it onto the shelf.
- A paid/departed guest's physical suitcase remains the same item and can still be deposited as lost property. Reuse the existing retention/deposit policy.

## Phase4 follow-up: exact evidence, time units and departure boundary

This section is an additional source inspection while the baseline suite runs. All field names in the first two tables exist now; the policy and new fields below are **proposals**, not implemented behavior or calibrated results.

### Reusable evidence already present

| Existing evidence | Meaning and limitation for an early-departure decision |
| --- | --- |
| `HotelIncident.Id`, `GuestId`, `RoomId`, `Reason`, `Cause.SourceEntityId`, `EpisodeCount` | An attributable source and affected guest. A stable incident ID is reused after recovery; the episode number is essential to avoid applying an old warning to a new occurrence. |
| `Severity`, `ExposureSeconds`, `Dissatisfaction`, `Stage`, `StageAge` | Severity/dissatisfaction are normalized; incident exposure is bad-condition simulation seconds **in this episode**. Exposure starts above the recovery threshold, so a long mild complaint can reach Critical. Critical alone is therefore insufficient proof of severe conditions. |
| `Active`, internal `RecoverySeconds`, `ReopenCooldown`, `PausedForTransfer` | Measured recovery closes a cause. Absence or a relocation transition must not be interpreted as either continuing exposure or a repaired room. |
| `HasContactedStaff`, `ComplaintRecorded`, `AttentionAcknowledged`, `ResponseAccepted`, `ResponseReliefRemainingSeconds` | Actual communication and bounded compensation relief already exist. Acknowledging a conversation does not repair the room. Knowledge and relief are reset appropriately for a new episode. |
| `GuestResponse.IncidentEpisode`, `ContactAttempts`, `Phase`, `AttemptStartedAt`, `AttemptDeadline`, `CommunicatedAt`, `AcknowledgedAt`, `StaffActionAt` | Attempts count only when the guest reaches the real phone/reception anchor. Beginning a walk does not count. Two actual missed attempts are distinguishable from a private concern that never found a contact window. Cancelled alone does not prove neglect: recovery, changed context or explicit debug cancellation can also cancel a response. |
| `GuestStay.Perception.InAssignedRoom`, `Agent.InAssignedRoom`, perceived temperature and current causes | Present-room evidence; sleep in that room still counts, hotel outings/contact travel do not. Delivered-and-received blanket comfort changes perceived cold without changing the thermometer. |
| `Needs.*.ExposureSeconds`, `Needs.*.Dissatisfaction` | Need exposure is lifetime bad-condition exposure, unlike the per-episode incident counter. Do not reuse it as a fresh episode's departure countdown. |
| `NeedProfile` comfort/tolerance ranges and `PatienceSeconds`; `GuestTraits` | Existing personal sensitivity, Patient/Impatient and cold/noise preferences. These can set bounded deterministic tolerance without a new personality catalogue or random per-tick leaving chance. |
| `Memory.ComplaintsInCategory`, `ProblemsIgnored`, `ProblemsResolvedSuccessfully`, `PromisesBroken`, `CompensationReceived` | Bounded service history. The incident already captures a repeated-category patience multiplier. Do not apply the same repeated-complaint reduction twice, or infer a specific current broken promise from an unrelated historic counter. |
| `Satisfaction.Evaluate(guest)`, `QualityIntegral`, `Elapsed`, exposure histories | Satisfaction is a price-weighted **stay average**, not current room severity. It can stay high after an otherwise good stay or fall because the price was high. Use it for the receipt/review and observable consequence, not as the sole trigger or a hard requirement that conceals a new severe problem. |

Sources: [incident state](../../Assets/_WorstHotel/Scripts/Simulation/IncidentSystem.cs), [episode progression](../../Assets/_WorstHotel/Scripts/Simulation/IncidentSystem.Living.cs), [needs](../../Assets/_WorstHotel/Scripts/Simulation/GuestNeedEvaluator.cs), [memory/perception](../../Assets/_WorstHotel/Scripts/Simulation/GuestAgencyData.cs), [response state](../../Assets/_WorstHotel/Scripts/Simulation/GuestResponse.cs), [actual contact attempts](../../Assets/_WorstHotel/Scripts/Simulation/GuestServiceSystem.Communication.cs), [satisfaction](../../Assets/_WorstHotel/Scripts/Simulation/GuestSatisfactionSystem.cs).

### Existing thresholds are simulation seconds, not clock hours

With the production calendar `SecondsPerDay=720`, one hotel hour is30 simulation seconds; one simulation second represents two hotel minutes. Acceleration changes the real time needed to consume those seconds, not the thresholds. Pause consumes none. Do not use `Time.time`, realtime waits or multiply these counters by acceleration a second time.

| Current production value | Hotel-calendar equivalent at720 seconds/day |
| --- | --- |
| Complaint exposure20s; escalation35s; critical65s | 40min; 1h10; 2h10 |
| Complaint/escalated/critical dissatisfaction .30/.65/.90 | Dimensionless; **both** the exposure and dissatisfaction tests must pass |
| Recovery8s; reopen cooldown12s | 16min; 24min |
| Temperature dissatisfaction gain `.02 × severity` per second; recovery `.04` per second | At severity1,0→.9 takes45s; severity.5 takes90s before any relief. Neither is an instant event. |
| Compensation relief25s before repetition scaling | 50min; cause/exposure remain, stage escalation temporarily waits |
| Physical phone ring12s; retry25s; reception wait35s; max2 attempts | 24min; 50min; 1h10. Travel time is additional and requires actual arrival. |
| Direct wait40s; contact lead12s | 1h20; 24min. Existing UI-close/reopen does not renew the same intent. |
| Profile patience: Budget90s, ColdSensitive65s, Business45s | 3h; 2h10; 1h30. **Current living stage thresholds do not use these directly**: `Reached` multiplies both shared thresholds by `EffectivePatienceMultiplier`, whose current source is repeat history. |

`NeedProfile.DefaultFor` currently gives Budget preferred20–23/tolerable18–26°C, ColdSensitive22–25/20–28°C, Business21.5–24.5/19.5–27.5°C. Temperature severity is only up to.25 inside the tolerance margin; it rises toward1 beyond that margin over the configured4°C severe delta. Thus a room at18°C means Budget severity.25 but ColdSensitive severity.625. The same thermometer must not automatically eject both guests. A1°C dip below preferred minimum with the default2°C tolerance span is only.125.

Assets: [needs](../../Assets/_WorstHotel/ScriptableObjects/GuestNeeds.asset), [services](../../Assets/_WorstHotel/ScriptableObjects/GuestServices.asset), [Budget](../../Assets/_WorstHotel/ScriptableObjects/BudgetTraveler.asset), [ColdSensitive](../../Assets/_WorstHotel/ScriptableObjects/ColdSensitiveGuest.asset), [Business](../../Assets/_WorstHotel/ScriptableObjects/BusinessGuest.asset). Missing later serialized fields use the declaration defaults in [NeedConfig](../../Assets/_WorstHotel/Scripts/Simulation/Definitions/NeedConfig.cs) and [GuestServiceSettings](../../Assets/_WorstHotel/Scripts/Simulation/GuestServiceSettings.cs); do not confuse those with a second tuning asset.

### Recommended small policy and warning lifecycle

Use one optional record per `GuestStay` and a pure eligibility evaluator over the existing factual incident. Limit the first policy to supported room causes (Temperature, Noise, RoomCondition), never a fabricated “overload complaint”. Only an unpaid, checked-in guest who has actually reached the owned room can accumulate it. Select one dominant cause deterministically; never sum unrelated weak sources to manufacture a severe threshold.

1. **Severe accumulation:** require fresh present-room perception and current source severity above a configurable severe threshold (starting candidate `.50`, with a lower recovery boundary such as `.35` to prevent chatter). Accumulate a separate severe-present-seconds counter tied to source/room/episode; existing `ExposureSeconds` includes mild time and cannot substitute. Brief improvement pauses accumulation; sustained measured improvement clears it. Outings, service trips and relocation freeze this counter and recovery verification.
2. **History and personality:** require an escalated/critical episode plus real disclosure, or maximum actual contact attempts that have genuinely finished unanswered. Use the existing repeated-episode multiplier once, then a bounded ratio based on `NeedProfile.PatienceSeconds` for the new duration. Temperature/noise traits already affect severity; avoid stacking an arbitrary extra “cold guest penalty” over that. Patient guests should require longer exposure, not be immune; one bad price choice must not qualify an otherwise comfortable guest.
3. **Warning:** when the severe-duration requirement is met, record one warning for that episode and a nonrenewing grace budget. A conservative initial tuning candidate is3 hotel hours of severe exposure at reference patience65s, scaled within a bounded range by personal/repeat patience, plus1 hotel hour of grace. Convert hours once using `SecondsPerDay/24`. These are starting candidates for later model/runtime comparison, not accepted final balance. Existing stage/history gates still apply, so this does not schedule everyone to leave exactly four hours after check-in.
4. **Communication stays physical:** an already-known concern can expose “I cannot keep staying like this” in its existing guest/phone context. Failed unanswered contacts retain their real call/desk history; do not reveal the private cause in the overview merely because a departure candidate exists. Generic impending-departure feedback may be shown without inventing room details. No new mandatory service kind, card or automatic forced conversation is necessary.
5. **Grace and recovery:** count grace only while fresh severe exposure continues; do not let an absolute deadline expire invisibly during Away. Pause it during existing compensation relief and in-flight physical contact/return or move. A received blanket, quieter source, effective heat or completed relocation is useful only through the resulting perception. Sustained severity below the recovery boundary clears the warning, even if a milder incident legitimately remains open. Acknowledgement or a reserved credit alone cannot clear the cause. Reopening a menu or repeating a staff action must not reset the grace budget.
6. **Commit:** at the end of grace, recheck the same guest, owner room, source/episode, fresh severe condition and sufficient remaining contracted stay. Scheduled checkout wins if it is due; a near-checkout case should finish normally rather than create a meaningless seconds-early outcome. Allow an already active finite Direct decision/physical return to finish, but do not admit an endless sequence of new Direct discussions after departure is pending. Do not leave during a partially committed key exchange or while still abroad. Commit once from a supported in-room state; a sleeping guest with previously qualified history can use the ordinary checkout exit sequence.

This uses severity, duration, patience, personality and actual history while making a mild dip incapable of satisfying the conjunction. Satisfaction/refunds are consequences from the same facts. The warning is not another clock or another incident generator.

### Reuse the actual checkout pipeline; preserve the contract

**Do not implement early departure by overwriting `Schedule.CheckoutTime`.** [OperationsSnapshots](../../Assets/_WorstHotel/Scripts/Simulation/OperationsSnapshots.cs) requires that time to remain at or after the contracted `Offer.CheckoutAt`, and [SnapshotValidation](../../Assets/_WorstHotel/Scripts/Simulation/SnapshotValidation.cs) requires the existing wake time to precede checkout. Changing checkout to this evening would invalidate legitimate snapshots and corrupt future-booking/late-checkout reasoning. Retain contracted schedule/price; record a separate actual early-departure outcome/time.

Extract a common terminal-transition helper from the timed branch in [HotelSimulation.Guests](../../Assets/_WorstHotel/Scripts/Simulation/HotelSimulation.Guests.cs). Both the normal deadline and the new policy call it. Evaluate new evidence after need/incident/service/satisfaction updates, then commit at one defined end-of-step boundary before `PostCompletedStays`. Recheck same-step receipt of a blanket or relief before committing. This also preserves the final interval's real satisfaction exposure. On commit, refresh presence/noise/electrical/heat demand before publishing the resulting world snapshot; do not leave the departing guest as an active source for another frame.

The existing sequence to retain is:

- Capture the factual departure explanation **before** `ReleaseRoom`: `Incidents.EndGuestStay` otherwise resolves the record, zeroes severity and replaces its explanation with “Guest checked out”.
- `ReleaseRoom` ends service intents/responses, releases pending relocation, clears instantaneous needs, records only meaningful ignored complaints, releases room ownership, clears own reservation token and returns that guest's keys. `ReleaseOwnedRoom` marks a genuinely used room dirty and creates `DepartingGuestId` before freeing ownership.
- `CheckingOut` lasts the configured2 simulation seconds, then `Leaving`. [GuestPresentation](../../Assets/_WorstHotel/Scripts/Guests/GuestPresentation.cs) routes the actual body from its current position through the room door/lobby/exterior. The physical vacancy callback clears only the matching departure token; `SignalGuestLeft` finishes the route. Never synthesize either callback to meet a timer.
- [PostCompletedStays](../../Assets/_WorstHotel/Scripts/Simulation/HotelSimulation.Bookings.cs) already posts one receipt at **CheckingOut/Leaving/Left**, guarded by `ReceiptPosted`, and marks the reservation Completed. It does not wait for exterior exit. Keep that billing rule; physical vacancy independently protects cleaning and check-in until the body leaves.
- [EconomySystem.Operations](../../Assets/_WorstHotel/Scripts/Simulation/EconomySystem.Operations.cs) adds checkout net once. The06:00 report copies paid receipts and charges the period operating expense; it never posts their revenue again. A departure at a report boundary belongs to the period determined by the existing tick ordering, with no second payment.

Financially, preserve the original room price as Gross and put the early-departure concession into the single Compensation deduction. The minimal initial policy can take `max(existing quality refund, already promised credit, configured early-departure minimum)`, bounded to Price, and add a factual early-exit sentence to the review. Reusing the current severe refund rate as the initial minimum avoids introducing a large separate fine; later tuning can assess whether it sufficiently prices the risky extra booking. Do not additionally prorate Gross, debit credit at dialogue time or apply an unexplained second score/cash penalty.

**New edge exposed by early checkout:** [GuestServiceSystem.EndGuestStay](../../Assets/_WorstHotel/Scripts/Simulation/GuestServiceSystem.cs) currently calls `MissPromise` for every still-Accepted wake-up promise. That is harmless for a normal checkout after its wake-call window, but wrong for an evening early departure before tomorrow's due time. Introduce a narrow stay-end reason/time branch: a promise whose opportunity has not expired becomes the existing `PromiseStatus.Cancelled` with no broken-promise penalty; truly expired missed calls retain their recorded consequence. The existing miss boundary is strictly `now > DueTime + WakeMissSeconds`, not merely reaching DueTime. This must not manufacture a second negative review about a call staff never had a chance to make.

### Snapshot and race contract to settle before implementation

Suggested small record: causal incident ID + episode + room ID; severe-present-seconds; below-severe recovery-verification seconds; warning time (`−1` when absent); remaining grace seconds; committed time (`−1` when absent); stable reason enum and a bounded captured cause description/measurement for the final review. The policy's partial-improvement counter is separate from the incident's full-recovery counter. A status `None/Warning/Committed` can make invariants explicit; None may still hold a not-yet-qualified candidate and its accumulated severe seconds. Keep one current record, not an unbounded warning history. Exact names belong to the phase4 implementation contract.

- All times/durations are finite, nonnegative except documented `−1` sentinels, and bounded by model time/configuration. Active warning must match an unpaid current guest and same active causal episode; threshold-crossing alone must not mutate a replica.
- Terminal evidence must remain valid after incident cleanup/reopening/pruning. Do not demand current `HasContactedStaff`, current source severity or current episode equality for an old committed outcome; retain immutable factual outcome text/reason. Avoid pinning all historical responses solely for the receipt. Reports already store self-contained receipts and allow old bodies to be pruned.
- Committed early departure must have terminal agent state, completed reservation and `ReceiptPosted` together in an externally captured frame. No fresh command or event callback should expose a partially released room with an unpaid terminal guest. Reject malformed wire input before applying any subsystem.
- A matching episode can warn only once; reopening it after genuine recovery gets a fresh budget. Source/room changes reset the candidate rather than carrying one room's severe exposure into a new comfortable room. Episode identity matters even though the incident ID stays unchanged.
- Pending DropOff cleanup must keep the same parcel at the same place: `EndGuestIntents` already changes AwaitingReceipt→Dropped and increments generation. A late delivery/receipt packet for the former room/intent/generation must fail, without comfort or fulfillment reward. Already received blankets remain received history.
- Preserve action versions, conversation grants, key owner tokens and session epoch protections. Stale radiator/phone/return/room-arrival callbacks cannot revive a departed guest; a retained departing body cannot acknowledge vacancy in a freshly reset game.
- Direct expiry/decision, relocation completion, natural checkout, warning maturity, pending parcel receipt and06:00 reporting in the same step each need focused ordering tests. In particular, an existing bounded Direct wait must not become an unlimited postponement through newly reopened discussions.

Extra focused tests beyond the phase4 list below: warning freezes while Away and resumes only on return; warning clears after sustained *partial* improvement below severe eligibility; genuine repeated episode is not immediately eligible on old severe exposure; future wake promise cancels without blame; normal checkout wins an exact-time tie; a host→mirror roundtrip immediately after early commit and after the next report preserves one receipt/dirty-room token/terminal intent history. All physical tests must still prove door→lobby→exterior movement separately from billing.

## Current rhythm and narrow phase6 proposal

[`HotelSimulation.Bookings`](../../Assets/_WorstHotel/Scripts/Simulation/HotelSimulation.Bookings.cs) creates one-night arrivals14–18, sleep23 and checkout next10. Current wake calculation gives next08. Dated `AttachStay` preserves the supplied absolute anchors; unlike the old shift path, it does not apply business early-bed adjustment/jitter itself.

[`GuestScheduleSystem`](../../Assets/_WorstHotel/Scripts/Simulation/GuestScheduleSystem.cs) builds24 seeded personality-weighted activities. Unpack is first; subsequent work/phone/TV/shower/rest/outing choices are not calendar-contextual. Morning wake resumes the same array with QuietRest first. All guests share the same production bedtime/wake anchors. The `SetActivity(LeaveHotel)` guard compares return time with SleepTime even after morning wake, so ordinary new morning outings are currently replaced with rest.

The current day already has arrival/checkout/turnover rhythm and a quiet sleeping night, but no deliberate morning shower window or evening presence bias. Space heat remains on while a guest sleeps or is away; that is a real valve/ownership rule, not a bug to hide with a global night multiplier.

Minimal later change:

- Preserve the existing finite activity types, seeded personality and actual routes. Add deterministic per-stay sleep/wake offsets within configurable windows; keep arrival/checkout ordering valid and absolute. Business may sleep/wake earlier, without creating multi-day stays.
- At ordinary activity selection, use the stay's real awake/morning/evening context to weight existing activities. Make one bounded morning shower opportunity likely for appropriate traits and stagger it; avoid synchronizing all showers at06:00. Work/phone/TV/evening showers supply actual sources. No `if evening: boilerLoad *= ...`.
- Before an excursion, compare available time with the upcoming **relevant** commitment (sleep before bed, checkout after waking), including conservative actual-route allowance. Never create Away by time alone; exterior exit and assigned-room return callbacks remain mandatory.
- Do not interrupt Direct, physical contact/return, shower staging, relocation or packing to satisfy a rhythm window. Missing a window is permissible; a scheduled activity is not a compulsory task.
- Keep the one-night design: after10:00 checkout and before14–18 arrivals, most rooms are truly vacant and need turnover. This already supplies a useful midday maintenance window. The brief's “many guests away” must not be implemented by adding multi-day stays or retaining paid occupants just to fill noon.
- Let occupied radiator settings persist. If later tuning requires a guest to lower their valve before an outing/sleep, that is a separate **actual physical self-action** with an explicit setting/route, not an invisible activity multiplier. It is not necessary for the first narrow schedule pass.

## Tests required at the corresponding gates

Phase4 model/integration tests:

- Actual six-room attributed demand causes weaker-room cooling and a guest reaction **while boiler.Failed remains false**; no temperature/failure forcing in the acceptance counterfactual. Compare four-room/healthy and managed-recovery cases over the same interval.
- Mild/brief exposure, an isolated1°C dip, a private minor case, a healthy room, compensated-but-recovered cause and Away time do not produce early departure.
- Severe unresolved episode + tolerance/contact history can lead to one early checkout; measured recovery and timely blanket/relocation prevent it without removing the physical cause artificially.
- Physical early departure crosses door/lobby/exterior, keeps dirty-room protection until actual vacancy, returns keys and produces one receipt. Repeated ticks/capture/restore/next report cannot pay twice.
- Pending DropOff/Direct/room move and real service-return cases clean up consistently; an undelivered/unreceived blanket remains reclaimable with no reward. Mirror departure is read-only and malformed records reject atomically.
- Refund/review arithmetic remains within the original price and does not double-charge compensation. Different real patience/trait/history configurations produce different tolerance, not automatic exodus of every cold guest.

Phase6 model/physical tests:

- A full one-night schedule has ordered varied absolute sleep/wake anchors and real morning shower opportunities, followed by packing/checkout. Repeated seed is deterministic, arrival identity remains stable through midnight.
- A24-hour trace measures actual guests present/away/sleeping, staged showers/TV, attributed heat and electric sources; morning versus evening differences arise from these events. No test asserts a hardcoded load curve or a mandatory failure.
- Morning excursions, if selected and time permits, complete exterior and return routes before checkout; late excursions are declined safely. Direct/service/relocation priority remains unchanged.
- Actual PlayMode guest paths verify bed→shower→packing and one contact/return during a schedule boundary, without fake production callbacks. Label headless route adapters in pure-model tests separately.
- Run existing pause, WAIT, continuous-report, privacy, snapshot, retention and SOLO/LAN regression coverage. Player sleep is a later phase7 clock feature, not guest schedule mutation or pause reuse.

No0.4.1 test result, runtime-cause reproduction or completed implementation is claimed by this note.
