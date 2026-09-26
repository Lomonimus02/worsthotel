# Prototype 0.2 — Living Hotel implementation plan

Phase 0 planning document, 2026-09-25. Source: the complete supplied Prototype 0.2 request, preserved in `PROTOTYPE02_REQUEST.txt`, the existing architecture, runtime code and test fixtures. This document proposes the incremental implementation and its acceptance gates; it does not mark any new feature implemented or tested. The root integration task owns the fresh baseline Unity runs. No Unity process was launched for this audit. The 0.1 source baseline is archived at `Logs/prototype01-source-baseline.zip`.

Continue the existing project, scene, modular art, three-day structure and simulation/incident/physical-action separation. Preserve the three guest archetypes, six rooms, existing economy and cooperative boiler procedure. Add only the requested living guests, situations, noise, alternatives, electricity, stable carrying, WAIT and limited turnover. No online code, new facilities, giant AI framework, mandatory luggage deliveries or random fetch quests.

## Required order and common gate

**0 Baseline → 1 Physics stability → 2 Clock and WAIT → 3 Living guests → 4 Needs/situations → 5 Guest conflicts → 6 Multiple solutions → 7 Electricity → 8 Housekeeping → 9 Three-day balance → 10 Polish.**

The suitcase fix must pass before clock/guests or additional carryable tools. Housekeeping must wait until physics, clock/WAIT, guests, noise and electricity are stable. Independent code inspection and design may run in parallel; dependent gameplay integration must respect these gates.

After each phase: compile; run existing and newly relevant tests; launch its actual gameplay path; record the specific result/log; update CHANGELOG, tuning and remaining issues/manual steps. A major compile/runtime regression blocks the next phase. A green unit suite is not proof of stable carrying, real-device ergonomics or enjoyable pacing. Do not drop an old assertion merely to hide a regression.

## Existing baseline and extension points

Historical 0.1 evidence is 77/77 EditMode, 7/7 PlayMode, a successful Windows build and three-day built-player tour with zero logged errors. Fresh Phase 0 rerun also passed compilation, 77/77 EditMode and 7/7 PlayMode, with zero skips; see `Logs/p02-phase0-*.log` and archived XML. The existing carrying bug is not covered by those seven PlayMode tests.

| Current seam | Present behavior | Narrow extension |
| --- | --- | --- |
| `PlayerInteractor` / `PhysicsPickup` | ConfigurableJoint linear drive 700/70/1800, free angular axes, kinematic hand moved in FixedUpdate; saves interpolation/collision mode | Central immutable grab tuning, measured damping/angular stability, complete snapshot/restore of every temporarily modified property; retain real Rigidbody collision and exclusive carrier ownership |
| `GameSession.Service` | 5 Hz accumulator from `Time.deltaTime`; service-only ticks; `AdvanceTime` directly loops ticks | Central hotel clock and one advance path; pause stays distinct from speed; WAIT advances bounded simulation steps without accelerating player/Rigidbody timing |
| `HotelSimulation.StartShift` | Immediately creates all stays, occupies rooms and enables their demand | Preserve committed bookings as reservations; establish actual arrival, reception wait, check-in, occupancy, activity and checkout as separate transitions |
| `GuestPresentation` | Delayed visual spawn and authored waypoint walking; automatically opens door; no authoritative state or collider | Reuse appearance/routes/door cooperation where adequate; project authoritative guest state and report validated travel completion. Physical check-in must no longer be cosmetic |
| `GuestStay` / `RoomState` | Immutable assigned RoomId; cumulative exposure; one occupancy ID; fixed initial noise/cleanliness | Stable guest identity with an atomic relocation command, room occupancy/reservation distinction and retained exposure/history across moves |
| `IncidentSystem` / `RequestSystem` | Sustained conditions, recovery hysteresis, deduplicated guest/reason requests | Evolve into one authoritative situation lifecycle with stages and compatibility projections; avoid two independent systems charging penalties for the same exposure |
| `RoomSystem` / `BoilerSystem` | Constant occupied demand and exponentially smoothed heat | Activity demand plus baseline; local delivered heater heat; preserve deterministic boiler wear/failure and distinct-actor physical repair |
| `PlanningSystem` / `ManagementUI` | Guest/room/rate validation, static room cards and price expectation | Visible traits, actual room graph/circuit, reservation readiness and forecasts; presentation shows problems and available choices, not a prescribed quest solution |
| `PrototypeSceneBuilder` / `EnsureConfiguration` | Fixed authored scene and assets created only if missing | Add explicit markers/room volumes/panel/heaters without replacing the hotel; use a versioned, idempotent config migration that preserves tuned existing fields |
| `DeveloperPanel` / verification tour | Runtime overrides and accelerated authoritative diagnostic route | Add clock/event/guest/situation/noise/circuit/turnover inspection. Keep debug overrides labelled and separate from ordinary causal-play evidence |

There is already a small authored navigation solution in `GuestPresentation`; the manifest contains no AI Navigation package. The integration decision is to preserve and extend these authored routes with reception/room/activity/exit waypoints; no new navigation dependency is planned. Keep movement's state synchronization explicit and do not rebuild all navigation by default.

Agreed integration choices: the acoustic graph uses shared-wall chains 101–103–105 and 102–104–106 plus weaker corridor links, including 102↔103, disclosed in planning. Housekeeping advances in Planning and Service through an explicit preparation path. Plans may reserve a dirty room with visible pending readiness, but physical check-in requires Clean. These choices resolve the topology/day-boundary risks; they still require the phase tests below.

## Phase 0 — Baseline

Scope: inspect current checkout, generated scene, configuration, input, build scripts and archived evidence. Run a fresh compile, all existing tests and the relevant built-player path. Reproduce and record suitcase behavior before changing it: lateral shake, fast yaw, stop, wall/door contact, release, partner grab and pause/disconnect cancellation.

Exit evidence:

- Fresh compile/test/build results recorded separately from historical 0.1 counts; any pre-existing failure identified rather than attributed to 0.2.
- Baseline three-day accounting, physical two-actor boiler sequence and independent controls still verified.
- A short suitcase trace records mass, tuning, target/body position, linear/angular velocity and decay after input stops. Existing screenshots/art and immutable config assets are retained.
- This plan and the bounded known-risk list below are reviewed; no 0.2 gameplay is claimed by this phase.

## Phase 1 — Physics stability

Scope: diagnose the existing ConfigurableJoint before replacing it. Check off-center grab torque, free angular motion, mass-dependent damping, target movement, force limits, collision retraction, interpolation, max angular velocity and damping. Make tuning configurable in one definition. Keep force/joint motion in physics timing; do not parent a dynamic body to the camera or set its Transform position each frame.

Exit evidence:

- Repeatable suitcase PlayMode/debug scenario: lateral/yaw input followed by a stationary target produces visibly decaying position and rotation envelopes, bounded velocity and quick settling; no persistent orbit or increasing energy once the target stops injecting it.
- Record thresholds before the run. Initial acceptance target: error/velocity envelope falls below 20% of its post-shake peak within three seconds and remains bounded for five more seconds; adjust these proposed limits only with explicit measurements and a documented reason, not after a failing assertion without explanation.
- Door/wall contact remains stable, the object cannot be pulled through an occluder, and release does not launch it uncontrollably.
- Release, disable, disconnect, UI block, NewGame and joint break restore every temporarily changed body/collision property, including nondefault starting values; another actor can subsequently grab it.
- Existing repair and input tests still pass. No portable heater work begins until this gate passes; physical feel remains a short manual check.

## Phase 2 — Simulation clock and cooperative WAIT

Scope: add a central clock with explicit SimulationTime/SimulationSpeed and a testable bounded stepping contract. Route normal service advance and debug advance through the same simulation path. Existing global pause can still freeze gameplay; WAIT must never set global `Time.timeScale` above normal or multiply physics/player movement. Establish an event stream for existing infrastructure warnings, requests and shift end, then extend it with arrivals/activities/checkout in later phases.

Exit evidence:

- One actor's consent cannot start WAIT. Two distinct ready actors must consent; cancel/movement, device loss, pause, phase transition and NewGame clear stale consent.
- Reject WAIT during critical repair, held important physics objects/controls or an unresolved major situation; revalidate while waiting, not just on entry. Shared-terminal ownership must not prevent the second actor giving independent consent.
- Stop at the first meaningful event within one configured simulation tick, including an event created midway through a multi-step rendered frame. Process coincident events deterministically and report why waiting stopped.
- Equivalent unaccelerated and accelerated fixed-step sequences produce the same state for the same seed/commands. Bound work per rendered frame so WAIT cannot block the UI for seconds or skip past warnings to repay a large time debt.
- Suitcase/repair regression still passes and physics fixed-step settings remain unchanged. A quiet hotel has a usable WAIT path, including eventual shift end when no earlier event exists.

## Phase 3 — Living guests

Scope: plain runtime guest state, small FSM, visible modular traits, seeded arrival/activity schedules and physical short check-in. Reuse the three archetypes and visual characters. Required activity effects: shower increases boiler demand, quiet rest reduces activity demand/noise, loud activity produces noise and optionally electric demand. There is no electricity implementation in this phase.

Exit evidence:

- Same session/day/guest IDs and seed reproduce the same schedule and event order; use a stable explicit hash/PRNG, not process-dependent string hashes or shared `UnityEngine.Random`. Adding one guest does not perturb every other guest's stream.
- Guests arrive at different configured times, enter the lobby, queue at reception, accept a quick 5–15-second check-in, reach their assigned rooms and later leave. Waiting decreases service patience; pre-arrival time is not cold-room exposure or occupied demand.
- A guest cannot be checked into two rooms, checked in twice, or silently enter a dirty/unavailable room when readiness is added. Check-in must be an actual validated player action, not just an animation.
- Scheduled activities start only in valid guest/location states, have explicit duration and produce real simulation demand. Presentation and WAIT cannot disagree about an unarrived guest showering in a room.
- Debug tools select a guest, show schedule/current state, force an activity and skip to its next activity. Tests distinguish seeded schedules from intentional debug overrides.
- Preserve short optional luggage interactions without requiring luggage delivery for occupancy, payment or schedule advancement.

## Phase 4 — Needs and situations

Scope: temperature, noise, room condition and service/patience only. Configurable preferred/tolerated ranges and severity-weighted exposure drive Observed → Complaint → Escalated → Critical. Evolve the existing incident/request path; do not add a parallel quest generator or a second satisfaction accumulator charging the same problem twice.

Exit evidence:

- Equal conditions affect the three archetypes/traits differently; slight bad conditions accumulate slowly and severe ones faster. A single short threshold crossing does not spam a complaint.
- Tests cover stage timings, stable deduplication, hysteresis/recovery, response delay, checkout termination and correct recurrence after a later new exposure.
- Satisfaction/reviews retain actual historical exposure after resolution and relocation; a warm final frame cannot erase an uncomfortable stay.
- Compensation acceptance can close or de-escalate the management case under explicit policy while the cold physical condition and its factual history remain distinct. Existing credit accounting must still charge at most the larger required refund/credit, not both.
- UI shows room, measured problem and worsening patience, without assigning a mandatory repair sequence. Existing boiler repair prompt can still explain how its physical mechanism works when approached.

## Phase 5 — Guest conflicts and room noise

Scope: explicit room adjacency graph, activity noise sources, attenuation and source attribution. Preserve the existing 0–1 simulation noise scale internally unless an explicit migration is necessary; player-facing 0–100 examples can be converted at the UI boundary. Do not add expensive acoustics.

Exit evidence:

- Configured neighboring rooms receive attenuated noise; separated rooms receive none or clearly weaker noise. Graph validation rejects duplicate/missing/self edges and invalid attenuation.
- User scenario A is demonstrable: noise-sensitive guest in 102 and noisy guest in 103, loud activity, sustained exposure then complaint. The actual scene alternates odd/even room sides; implement shared-wall chains 101–103–105 / 102–104–106 and weaker corridor links including 102↔103, with the same relationship visible in planning. Do not infer physical adjacency from consecutive room numbers or silently renumber existing anchors.
- With identical seed/guest/activity timing, separated placement avoids the noise conflict. Noise origin and receiving room values are visible in debug output.
- A short request-quiet response temporarily changes the actual source activity/output and expires according to configuration; it cannot permanently erase a noisy trait or conceal the complaint while exposure remains unchanged.
- Stopping the source ends propagation and permits normal situation recovery. Later relocation tests must also prove that moving one guest changes the relevant receiving/source room.

## Phase 6 — Multiple responses and portable heater

Scope: atomic relocation to a suitable free room, compensation, ignore, and one stable physical portable-heater type. These are alternative player decisions about the same cold situation. Existing boiler repair remains available. Implement the heater's local heat and explicit power-consumer data now; real circuit consequences are the Phase 7 dependency and cannot be declared proven yet.

Exit evidence:

- Relocation validates actor, guest, destination availability/readiness and phase before changing anything; changes old/new occupancy, routing and situation evaluation atomically. Retain agreed price, identity, credit and exposure; do not manufacture a second paying stay.
- Heating recovery, moving to a warmer room, compensation and ignoring a cold situation each have a valid observable outcome. Compensation is a management response; ignore still allows checkout with consequences.
- A carried heater uses the Phase 1 mechanism. Its registered room is determined consistently from authored room volumes, not a nearest-object search every Update. Crossing a boundary, carrying it out, releasing it, disabling it and NewGame cannot leave duplicate or stale heat contributions.
- An enabled, correctly placed heater measurably changes the target room's thermal evolution; a disabled/outside-room heater does not. Define whether held heaters are inactive and show that state clearly.
- No UI says that delivering a heater is the only solution. The Phase 6 report explicitly marks electrical load/trip behavior as not complete until Phase 7.

## Phase 7 — Electricity and physical panel

Scope: Circuit A 101–103, Circuit B 104–106, baseline/activity/heater consumers, warnings, delayed trips and two physical reset controls. Reuse the hotel's thick geometry/materials; panel has real box/door/breakers/labels/indicators. Avoid an optional utility circuit unless a concrete requested behavior needs it.

Exit evidence:

- Load totals are from unique consumer IDs; remove/move/disable cannot double-count. Track requested demand separately from delivered power so a tripped circuit does not appear safe merely because all devices went dark.
- Overload warning and trip require configured durations; a transient spike recovers without tripping. Reset with the same excessive requested load produces another delayed trip. Reduce load then reset and power stays restored.
- Heater heat requires delivered power: after trip, both its power delivery and heat stop. Room baseline/activity plus one heater can independently cause Circuit B to exceed capacity; no scripted chain controller forces the result.
- Rooms on the affected circuit show the power state in actual lights/devices and in room-condition/guest needs. Other circuit rooms remain powered. Situation generation follows the resulting world state, not the breaker button event.
- Run user scenarios B/C/D through actual carry/place/reset interactions and record temperature, requested/delivered load, warning, trip, guest response and recovery. Existing two-person boiler controls remain mechanically and semantically separate from the new simple breakers.

## Phase 8 — Housekeeping and room turnover

Scope: checkout marks the vacated room Dirty, one housekeeper moves through a priority queue and cleans one room at a time. Player priority is the main decision; one optional short help action may accelerate the current work. No trash/towel/stain checklist or employee economy.

Exit evidence:

- Checkout, dirty, queued, cleaning and ready transitions are idempotent; dirty is not cleared just by opening the next plan, moving a guest or reloading presentation.
- Dirty rooms cannot accept physical check-in. Planning may reserve a dirty room with explicit pending readiness; UI must disclose the risk and reception must wait for Clean. Both occupied and cleaning rooms are protected from invalid moves/double booking.
- Solve the day-boundary deadlock by advancing housekeeping in Planning and Service through an explicit preparation path. Keep normal guest service/exposure clocks distinct where needed; do not silently clean every room overnight or require check-in to an unavailable room to start the clock. Test the all-rooms-dirty transition after day one.
- Highest eligible priority is selected deterministically; one NPC cleans only one room, reacts correctly if a task is cancelled/occupied, and reaches the right room before progress. Helpful player input cannot stack unlimited duplicate work or complete several rooms at once.
- Actual day-two arrivals can be constrained by cleaning capacity; priority resolves the bottleneck. WAIT can pass a truly quiet preparation interval and stops at completion/arrival or another meaningful event without bypassing consent/physics guards.
- All previous phase gates pass before this phase starts; housekeeping remains a capacity choice rather than the dominant minute-to-minute player task.

## Phase 9 — Three-day balance and pacing

Scope: tune existing/new config, not new subsystems. Day 1: staggered few guests, safe boiler, activity consequences, forgiving noise and useful WAIT. Day 2: room placement, heaters and power matter. Day 3: valuable guests plus retained wear may produce two or three simultaneous independent situations; careful management can still avoid catastrophe.

Exit evidence:

- Record repeatable cautious/risky seeds, all booking/price/room decisions, arrivals/check-in wait, activities, warning/situation timestamps, responses, turnover delay, refund/reputation/cash and maintenance choices for three complete days.
- Natural chain demonstrated from independent systems: demand → cold → optional heater → circuit overload → another guest's power complaint. No forced failure or hand-set temperature is counted as natural-chain evidence; debug scenarios remain separately labelled diagnostics.
- Paired planning counterfactual with identical schedule seed shows that changing room assignment prevents the intended noise conflict. Paired response scenarios show heater versus boiler/move/compensation/ignore tradeoffs.
- Measure real-time quiet spans as well as simulation time. Aim for a meaningful observation/change/decision roughly every 20–30 seconds of active play without periodic forced trouble; any genuinely quiet stretch has usable two-person WAIT and never requires staring at the timer for two minutes.
- Three-day settlement, immutable reports, one-time expenses, maintenance and fresh-session reset still work. Re-measure the old extreme repeat-failure risk instead of carrying forward 0.1 numerical promises after activity loads change.

## Phase 10 — Polish and final delivery

Scope: guest movement/door/arrival/activity sound, shower/noise source audio, heater/electrical hum, warning/trip/reset, phone cues, VFX, diegetic labels and clear problem UI. Preserve current hotel art and prototype content scale.

Exit evidence:

- Compile, all preserved/additional EditMode and focused PlayMode tests, regenerated scene, Windows build, complete built-player three-day/reset tour, and actual GPU/native GUI image review. Reuse the existing validated offscreen capture path; black hidden-window backbuffers and their timings are not visual/performance proof.
- Inspect planning traits/adjacency/circuits, guest reception/activity states, situation presentation, physical panel/heater, housekeeping priority and results in both viewports. No HUD overlap or required information hidden by the partner's ledger.
- Human two-pad and keyboard-plus-pad playtest checks carrying stability, WAIT consent, real-display performance, audio mix, pacing, repeated repair and meaningful alternative responses. Record whether players independently tell the intended unscripted story; do not mark fun as passed from a simulation trace.
- Deliver a versioned 0.2 verification record and remaining issues. Keep the 0.1 records as historical evidence; do not relabel them as validation of changed mechanics.

## Integration contracts to settle before each dependent phase

- **Time ownership:** hotel clock advances schedules, exposure, infrastructure timers and cleaning. Unity physics time advances bodies/real control holds. Presentation animation may interpolate, but cannot create authoritative activities independently. WAIT pauses at meaningful state events; NewGame resets clock, consent, queues and subscriptions.
- **Tick order:** consume validated actor commands and due schedule transitions → current guest/location/activity state → room activity demands/noise and unique consumers → electrical requested load/trip/delivered power → boiler demand/wear/output → local room heat/power/noise → needs/situations → integrated satisfaction → terminal events/clock. Keep event ordering explicit to avoid a powerless heater heating for another accelerated batch.
- **Guest ownership:** a booking is not yet an occupied room. One authoritative guest identity owns check-in, current room, activity, credit and exposure; visuals never overwrite it. A relocation is one transaction. Early checkout must not charge twice at day settlement or discard its receipt.
- **Situation ownership:** expose a single source of truth with physical condition, response/acceptance and historical exposure separately represented. Reuse current request views/events where practical; do not independently escalate both a legacy incident and a new situation.
- **State configuration:** add validated immutable snapshots with sensible additive defaults, explicit schema migration and idempotent asset setup. Existing `EnsureConfiguration` returns old assets unchanged, so new required references cannot rely on recreating files. Do not overwrite designer tuning or store runtime queue/consumer/guest state in ScriptableObjects.
- **Scene ownership:** register room zones, routes, spawn/reception/exit/activity targets and infrastructure controls once. Preserve known anchors and six-door clearance tests. No per-guest whole-scene searches each Update; avoid full hotel list allocation in every new needs tick.

## Existing test compatibility risks

| Existing test/contract | Likely 0.2 pressure | Required protection |
| --- | --- | --- |
| `EconomyTests.CleanFourGuestDayPaysOnceAndChargesOneOperatingCost` expects all rooms occupied immediately after StartShift | Staggered arrival/check-in deliberately separates reservation from occupancy | Preserve this explicit legacy/unit scenario using documented neutral/additive config or a distinct historical fixture; add a real 0.2 delayed-check-in path. Do not silently auto-check-in production guests to keep the assertion green |
| `ProgressionTests` exact cash/condition values and direct StartShift → ticks → EndShift loops | Activity loads, dirty-room availability and arrival timing change numerical outcomes | Keep fixed old component fixtures and their accounting invariants. Add 0.2 progression tests with actual schedule/check-in/turnover commands; any fixture migration is explicit and retains the old verified scenario |
| `BalanceScenarioTests` loads actual PrototypeSession.asset and assumes passive/no-check-in guests | Real 0.2 asset defaults will invalidate old output and possibly its route driver | Preserve baseline traces separately; evolve/add a valid 0.2 authoritative scenario driver before treating its output as evidence. Never redirect the production-default check to old assets without documenting it |
| `IncidentTests.CompensationMarksResponseWithoutPretendingTheColdRoomIsFixed` | Accepted compensation may close an operational situation | Preserve the physical-cause assertion and credit behavior; represent accepted response separately from thermal repair and historical dissatisfaction |
| `SatisfactionTests` noise range and time subdivision; factual `ReviewTests` | New comfort/tolerance exposure, changing room, power deficiency, early checkout | Keep common units and exposure integration. Price effect, no invented facts, tick subdivision and zero-duration bounds remain required; avoid charging both the old quality deficit and a new identical need penalty |
| `PlanningTests` immutable snapshots, actor/rate validation, fixed accepted demand forecast | Traits/arrival schedules/circuits add forecast uncertainty | Preserve valid price grid and unique reservations. Show baseline/peak scheduled forecast clearly; do not present an arbitrary exact number as constant actual occupancy load |
| Input tests reflect `LocalPlayerInput.Bind/Read` and explicitly use Dynamic updates | New WAIT input, UI ownership and pause | Keep device exclusivity and press-edge fixture semantics; add independent consent tests, no global Gamepad.current shortcuts |
| `Phase1Tests` named anchors, six doors, physical floor and carryable-body checks | New panel, NPC routes/heaters and room zones may obstruct the corridor | Keep authored anchors and spawn/door clearance; add objects outside verified paths and include new geometry in actual scene validation |
| Seven PlayMode tests use explicit ForceFailure and fast station setup | Clock/schedules/WAIT/guest presence may unintentionally pause, auto-open UI or steal input during repair | Keep deterministic setup and ordinary raycast input; preserve every repair/cancel/rebind assertion. New tests should not mutate actual connected pads or pretend teleport setup measures human repair duration |
| `RepairSequenceTests`/`RepairCadenceTests` preserve wear and require distinct actors | Electricity/WAIT might bypass held controls or change hot-water demand | Keep the six-control repair invariant and no ghost hold. Do not accelerate ongoing latch/valve physics or infer consent from debug actor switching; retune cadence only with measured old/new traces |
| NewGame/OnDisable cleanup and single scene static registries | Schedules, consumers, situations, room moves and housekeeper add persistent references | Test reset after active WAIT/repair/heater/cleaning and scene unload; no old simulation events or consumer registrations may affect the replacement session |

Preserve all existing tests and their meaningful invariants. The user permits necessary incremental refactoring, not broad replacement. Prefer explicit additive settings and neutral subsystem fixtures over a parallel legacy architecture. If an intentional new rule contradicts a fixture's old scenario, record the conflict and preserve that scenario as a regression while adding the new acceptance scenario; do not delete, skip or weaken it to conceal a failure. No fixture or source migration is performed by this Phase 0 document.

## Trace to the 30 user criteria

| Criteria | Primary phase/evidence |
| --- | --- |
| 1–3: active guests, staggered arrivals, activities | Phase 3 authoritative state, physical reception/room route, seeded schedule tests |
| 4–5: activities affect heat demand and noise | Phase 3 output tests, Phase 5 propagation integration |
| 6–9: adjacency, different needs, causal complaints, planning consequences | Phases 4–5 same-seed adjacency counterfactual and exposure/stage tests |
| 10–12: alternatives, actual local heater heat and electric demand | Phases 6–7 relocation/response tests and powered heater measurements |
| 13–16: overload/re-trip, compensation and ignoring | Phase 7 independent circuit tests and Phase 4/6 accounting/consequence paths |
| 17–20: WAIT exists, physics independent, event stop, two consents | Phase 2 clock/input/guard tests, extended through later event types |
| 21–22: oscillation decays/no sustained energy gain | Phase 1 measured stationary-target and wall-contact scenarios; repeated after heater addition |
| 23: emergent noise conflicts | Phase 5 neighboring-versus-separated assignment scenario |
| 24–27: three days, no long dead periods, causal simultaneous situations | Phase 9 full normal causal traces plus timed human run; no forced catastrophe |
| 28–29: turnover works without becoming dominant | Phase 8 one-housekeeper priority test and Phase 9 human work/time distribution |
| 30: art preserved | Phase 10 comparison of real world/native GUI captures with established hotel art |

Human evidence must answer the supplied playtest questions: discussion of guest placement; a prior decision visibly mattering later; more than one valid response; intentional ignoring; heater solving cold while causing power trouble; the hotel changing without player action; WAIT used because it is quiet rather than empty design; stable pleasant carrying; and an unscripted story emerging from independent systems.
