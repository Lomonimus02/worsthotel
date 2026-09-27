# Staff sleep — phase 7 implementation contract

Design only, prepared during the phase 5 Unity gate. No staff-sleep runtime implementation or measured acceptance is claimed here. Authoritative scope: [request §§30–35, 58–59, 70–71, phase 7](../RHYTHM041_REQUEST.txt). The previously deferred Alt-Tab investigation remains deferred; this feature must preserve existing pause behavior and run its regression tests.

## Shared clock and two advance modes

Extend the existing `WaitController` with an explicit advance mode (`None`, `Wait`, `Sleep`) and a cohesive `WaitController.Sleep.cs` partial. Do not create a second advancing clock or independent speed controller. Ordinary WAIT keeps its existing event-sensitive semantics. Sleep uses exactly `HotelGameClock.Speed = 8`; Unity `Time.timeScale`, `fixedDeltaTime`, Rigidbody simulation and real guest-route acknowledgements remain unchanged.

Current sites requiring coordinated changes:

- `Core/WaitController.cs`: `ObserveSimulationEvents` currently stops on **every** `EventRevision` change; `IsWaiting` currently means any speed above one. These must distinguish WAIT from sleep without weakening existing WAIT behavior.
- `Core/GameSession.Service.cs`: `Tick` independently resets speed on every event; its fallback must also respect the active mode. `Update` already advances fixed hotel ticks and discards the remaining accelerated accumulator after speed returns to one. Preserve this synchronous interruption.
- `Core/GameSession.cs`: `NewGame` already calls `Wait.Stop`; the shared stop must clear both forms of consent and all sleep state.
- `Simulation/HotelGameClock.cs`: existing allowed speed 8 is sufficient. No reset, new day transition, or direct clock jump is needed.

Derive the next morning from `HotelCalendar.At(day, 6)`, selecting the following day only when today's 06:00 has passed. There is **no hard night lock**: bed use is a player choice, not a phase gate. Clamp the final ordinary tick to the remaining morning interval, handle zero/unrepresentable remainder without submitting an invalid tick, and discard leftover accelerated budget on wake. Midnight, daily reports, sales decisions, service promises, wear, thermal state and guest schedules continue through their existing pipelines. Sleep never calls `EndShift`, reconstructs the hotel, refills inventory itself or fabricates physical arrivals.

## Physical consent and cancellation

Add a modest authored staff room and two stable bed interaction IDs, using existing visual materials and modular construction. One bed works for SOLO; co-op requires two actual actors' consent at distinct beds. A new `StaffBedInteraction : HotelInteractable` uses the existing authoritative first-surface raycast and normal staff interaction. Empty hands, a connected valid actor, no incompatible menu/physical task, and a running continuous hotel are required. Bed use must not be exposed as an unrestricted remotely callable menu command.

Proposed cohesive controller API: `CanUseBed(actorId, bedId)`, `TrySleep(actorId, bedId)`, `CancelSleep(actorId)`, an after-tick observer, and readonly mode/consent/deadline/revision/wake-reason properties. Exact signatures can be settled with the scene owner; these are one shared controller lifecycle. Pending single consent remains at speed one. SOLO never fabricates actor 1 readiness. WAIT consent and bed consent cannot combine or coexist as authority for acceleration.

The initiating bed hold, its ordinary release, and incidental residual look/movement must not immediately cancel sleep. Record activation input freshness and arm cancellation after the initiating Use button is released. A **new** Use/E press or Escape cancels naturally. If movement/look cancellation is retained, require a new neutral-to-deliberate input edge after activation, not the magnitude carried over from aiming at the bed. Waking clears all readiness; held input cannot immediately restart sleep.

Do not make a sleep overlay set `FirstPersonController.IsUIBlocked`: `LanWorldInput.CaptureLocalInput` intentionally strips gameplay input when UI-blocked, which would also suppress remote cancellation. Any sleep presentation must leave the ordinary cancellation input observable. No extra management page or role restriction is required.

## Critical admission and wake semantics

Use typed factual predicates, never the general event string/revision or a sleep-specific random hazard:

| State or transition | Sleep behavior |
| --- | --- |
| Actual boiler failure | Refuse a new sleep admission while unresolved; a genuine new failure during sleep wakes immediately. |
| Already-active **paid Full boiler maintenance**, including a retained `Failed` flag until completion | Permit sleep. This is deliberate downtime with an existing completion deadline. Do not mistake zero heating output or the old failure flag for a new emergency. Completion continues normally and does not itself wake. |
| Actual breaker trip | A new trip wakes. Each existing authored breaker serves a whole three-room branch; sustained-overload **warning alone** does not qualify. Admission with an unresolved actual outage is refused. |
| Guest emergency, if included | Only an active, already communicated `SituationStage.Critical` cause may qualify. Private model distress must not be revealed by a wake notification. Keep the predicate narrow and explicit. |
| Extra blanket, other minor optional request, ordinary guest activity/arrival, new reservation, midnight/report | Do not wake merely because these changed `EventRevision`. Their normal consequences still run. |
| Boiler Busy/Worn/Strained, planned maintenance heat loss, successful service completion | Not a catastrophe and not an automatic wake reason. |

Evaluate critical state after **each normal model tick**, before continuing the accelerated batch. The paid-Full-maintenance exemption must check the real active service kind/lifecycle, not merely an acknowledged failure or heat output. A completed maintenance job restores its normal state before subsequent critical evaluation. The baseline is recorded at actual sleep start; do not reissue an old wake every frame. A broad 'accepted consequences allow any current fault' extension is not required by this contract.

## LAN authority, atomic frames and freshness

Physical remote bed use travels through the existing input frame, authenticated sender-to-actor mapping and host raycast; the client does not start its own clock. Existing input epoch and monotonically increasing sequence protect physical action edges from replay. Do not add a start-sleep command that bypasses distance, focus or bed identity.

Root owns frame integration. A minimal host view contains advance mode, sleep revision, absolute sleep-until time, two actor consent/bed-ID rows and a typed wake reason. Existing WAIT fields remain backward-compatible within the newly versioned protocol. Keep data bounded and validate the outer sleep view **before** `GameSession.ApplyLanFrame` commits `model.ApplySnapshot`:

- finite time, defined enum values, nonnegative revision, exact two-row shape;
- only valid authored beds, no duplicated occupied bed, actor/consent consistency;
- active sleep requires continuous Service, speed 8 and both required LAN consents;
- its deadline matches the next morning boundary within clock precision;
- pending consent requires speed one and cannot coexist with active WAIT consent;
- do not introduce a global ban on existing diagnostic/legacy clock speeds outside sleep.

Only after complete validation and successful model application may the client apply the readonly sleep presentation. Clients never call authority update or `SetSpeed`. Invalid packets must leave both model and controller view unchanged, and a corrected packet with the same sequence must remain admissible. Existing epoch/sequence validation remains authoritative.

Concrete current integration points:

- `Core/GameSession.Lan.cs`: capture/validate/apply bounded controller view; fresh-epoch reset.
- `Networking/LanProtocol.cs`: versioned view fields and strict outer shape.
- `Networking/LanSession.cs`: disconnect/reconnect/new-game cleanup; no remote identity in payload.
- `Networking/LanWorldInput.cs`: `RemoteInputLeaseExpired` becomes true after **0.35 real seconds** without fresh remote input. `DeviceReady` can remain true, so device checks alone cannot preserve safe latched sleep consent. Revoke sleep on lease expiration as well as actual disconnect.
- `Core/LocalCoopBootstrap.cs` and `LanWorldInput.cs`: existing menu/focus/device suppression must revoke consent before further acceleration. If needed expose a narrow readonly input-freshness property; do not change the current pause policy.

Reconnect, NewGame and SOLO/local/LAN mode changes clear all readiness and advance its revision. Reconnecting restores the running hotel, never old bed votes. Both actors must provide fresh physical consent. Client focus/menu suppression sends a neutral UI-blocked frame; host detects that as loss of current consent. True PAUSE stops hotel time as before; sleep is never represented by pause or `Time.timeScale = 0`.

## Ownership and decisive verification

Suggested implementation split: controller/model timing owner handles `WaitController` plus sleep partial and `WaitConfig`; world owner handles staff room and bed interactable; root owns `GameSession.Service`/reset integration and LAN frame changes. Keep modifications to input/focus code narrow. No extra guest/equipment clock is introduced.

New tests should prove:

1. Actual SOLO bed use reaches 06:00 with the same hotel, guests, equipment, promises and accounting history; no day reset and no forced failure.
2. One real co-op consent cannot accelerate; two distinct bed consents can. Ordinary release/residual aim does not wake; a new cancellation edge does. Re-arming requires fresh use.
3. Rigidbody/player physics runs at ordinary speed while the model advances at 8×.
4. A real high-risk workload crosses the existing failure threshold during sleep and wakes on that tick. The fixture labels initial stress/load preparation and never calls `ForceFailure` from the sleep path.
5. Minor service events, booking/report boundaries and ordinary activity do not wake; accepted Full maintenance with a retained failure flag is admitted and completes through its real deadline.
6. Model-only mirror inspection cannot advance time or mutate consent. Malformed/stale sleep frames reject atomically; corrected same-sequence packets apply.
7. Two executable LAN peers exercise host-first and client-first bed consent, remote cancel, input-lease loss, disconnect/rejoin and new epoch; readiness never resurrects automatically.

Regression filters include `WaitPlayModeTests`, `ElectricityWaitPlayModeTests`, `AlternativesWaitPlayModeTests`, `SoloUsesOneActualRigAndOneWaitConsentAndKeepsModeOnReset`, existing LAN input lease tests and existing pause tests. Human sleep pacing and final executable SOLO/LAN evidence remain future gates. No claim is made that the separately deferred Alt-Tab issue was fixed.
