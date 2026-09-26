# Prototype 0.2 — Phase 4 needs and situations

This note records the independent review and verification for Phase 4. The root confirmed the preceding Phase 3 gate at 101 EditMode / 17 PlayMode. `Logs/p02-phase4-editmode.xml` records 113 passed, zero failed and zero skipped. The root also confirmed successful scene compilation and all 18 Phase 4 PlayMode cases, with zero skips. The root task owns Unity execution and the authoritative CHANGELOG.

## Causal model

Runtime living mode supplies immutable need settings and guest preference snapshots. Each guest owns four need dimensions: temperature, noise, room condition and service. Every dimension exposes current normalized severity, lifetime seconds exposed beyond the recovery band, and accumulated dissatisfaction. Stronger severity increases dissatisfaction faster; it does not invent additional elapsed seconds.

Temperature distinguishes preferred and tolerable ranges on both the cold and hot side. Noise respects each guest's preferred level and tolerance. Dirt and damaged fixtures contribute to one room-condition dimension. Service records either overdue reception waiting or an expired room response, according to the guest's current physical state.

Room measurements affect guests only while they are actually in their assigned room. Reception waiting affects service without inventing room exposure. Scheduled arrivals and travel states do not accumulate room-quality time. This retains the Phase 3 reservation/check-in/travel boundary.

Living situations project measured need state through `Observed`, `Complaint`, `Escalated`, `Critical` and `Resolved`. Complaint and later stages require both the configured episode exposure and dissatisfaction thresholds. Observed discomfort is visible without creating a request immediately. The default thresholds are 15/35/65 exposed seconds together with 0.30/0.65/0.90 dissatisfaction.

Recovery requires severity inside the configured recovery band for eight continuous seconds. It does not require accumulated dissatisfaction to disappear first. Guest exposure history is retained; a later episode uses a fresh exposure baseline and a 12-second reopening cooldown. The same guest/reason situation and request identity are reused rather than generating duplicate errands.

Each situation transition raises an important hotel event, so WAIT stops at the relevant fixed simulation tick. Critical unresolved situations block fresh WAIT consent. The physics clock remains unchanged.

## Service and scoring audit

The orchestration uses `HasExpiredRoomRequest`, which excludes Service. This closes the self-reference risk where an overdue reception request could otherwise perpetuate its own service severity after check-in.

The separate Service situation is a reception problem. Missing a room complaint's response adds service dissatisfaction while the original room request remains the cause; it does not recursively create another complaint about that complaint.

`GuestNeedEvaluator` computes need snapshots and the service integral. `AccumulateLiving` is the single writer of room quality integral and room stay time. `EvaluateLiving` uses that room integral and one service fraction; it does not subtract the earlier legacy check-in-delay or expired-complaint fractions again. The existing two-argument kernel fixtures keep their legacy evaluation path.

Requests are projections of the source situation. They are created at Complaint, keep the source room and stage, and resolve when the source recovers or the guest checks out. The developer resolution path resolves the source as well, avoiding a request UI state that contradicts a still-active source.

No unresolved critical implementation finding was identified in the bounded source review after the expired-Service exclusion was corrected. This does not certify balance, rendered communication, or later-phase systems.

Two smaller findings from the cross-review were carried into Phase 5: a resolved event should not say a physical condition recovered when its actual cause was checkout or a developer override; and need snapshots outside an eligible physical state should clear current severity while keeping exposure/dissatisfaction history. Their Phase 5 patches and lifecycle regression are recorded with that phase's verification, rather than retroactively claimed as part of the Phase 4 run.

## Prepared tests

`NeedsSituationTests.cs` adds twelve EditMode cases covering:

- A comfortable room with four quiet need dimensions and no manufactured situations.
- Different temperature/noise reactions to identical measurements for the existing guest profiles.
- Independent causes and actual exposure time for all four dimensions.
- Observed discomfort without an instant complaint.
- Severe cold producing a complaint earlier than mild cold.
- One request escalating through all stages with meaningful events.
- Sustained recovery, retained history, reopening cooldown and fresh episode exposure.
- Dirty and broken conditions grouped into one room-condition situation; fixing only one cause is insufficient.
- A genuinely expired reception service request, followed by check-in and recovery without recursion or double service penalty.
- One room-quality accumulation per second, including the complete hotel tick.
- Dissatisfaction recovery and immutable earlier snapshots without erased exposure history.
- Invalid preference ranges, threshold ordering and non-finite tuning rejection.

`NeedsSituationPlayModeTests.cs` adds one integration scenario. It explicitly sets up a physically checked-in model state through the public travel adapter commands, then changes the actual room temperature. The real update loop moves the situation from Observed to Complaint; both virtual pads consent to WAIT and the complaint stops acceleration. Restoring a comfortable room temperature resolves the same request after sustained recovery. This test does not claim to repeat the physical check-in/door-route evidence already supplied by Phase 3.

The new PlayMode fixture clones config assets and isolates the need transition from unrelated scheduled activities. It retains living mode and the real Input System, GameSession, WAIT observer and generated scene. Existing tests were preserved.

## Remaining checks and later phases

Native rendered inspection must confirm that observed discomfort, complaint, escalation, critical state, measured cause and recovery are readable in both viewports. Physical-device ergonomics, audio cues and human understanding remain manual checks.

The timings above are implementation defaults rather than a claim that the hotel is well paced. Cross-room noise propagation, temporary quiet requests, relocation, portable heat, electrical load and housekeeping still belong to their subsequent gated phases. Phase 4 supplies the causal observation and consequence layer for those systems; it does not certify them in advance.
