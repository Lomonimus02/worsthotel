# Preserved baseline acceptance plan and 0.2 extensions

The numbered procedures below preserve Prototype 0.1's baseline checks. For the current living hotel, use `PROTOTYPE02_MANUAL_TESTS.md` and the per-phase notes as the extension: occupancy begins after physical room arrival, load changes with activities, room turnover persists, and WAIT changes wall-clock duration. Constant-occupancy balance figures from the old model are regression evidence only. Current pass/fail evidence belongs in VERIFICATION and CHANGELOG; this file is a procedure, not a completion report.

This is a test specification, not a report of passed tests. Features described below remain unverified until evidence is recorded. Consult CHANGELOG.md for actual implementation status. Pass each phase's compile and smoke gate before integrating the next phase.

## Evidence and common setup

- Record Unity version, commit or source snapshot, generated scene date, configuration values, display resolution, input devices and tester names. Keep batch logs, EditMode XML and player logs with the tested build.
- Use the generated `PrototypeHotel` scene and a fresh session unless a scenario specifies carry-over. Disable debug overrides for normal acceptance runs. Clearly label footage or traces that use them.
- Record each scenario as **Not run / Pass / Fail / Blocked**, with actual observations, evidence path and remaining issue. Missing controllers or an unavailable Editor means Blocked, never Pass.
- A compile pass requires a completed Unity import/compile without C# errors; scene generation and a successful executable build are separate checks. A build exit code alone does not prove input, repair, visual quality or fun.
- At every phase, enter Play, walk from reception to utility room and back in both viewports, exercise all previously implemented paths, then stop and restart. Record Console exceptions and broken references. A later gate includes earlier functionality.
- Evidence for numerical scenarios should include starting state, commands, elapsed simulation time and final state. Compare deterministic tests with tolerances appropriate to the tick; do not judge a 5 Hz simulation from frame rate alone.

## Phase 1 — bootstrap, input, interaction and blockout

| ID | Procedure | Acceptance and evidence |
| --- | --- | --- |
| P1-01 | Open with the selected Unity LTS version on a clean import, generate the scene from **Tools → Worst Hotel → Build Prototype Scene**, save and reload it. Repeat generation. | No compile errors, missing scripts or pink materials. Each generation gives one lobby, six room spaces, one utility room, two spawns and one set of controls, without duplicate roots. Save Editor log and scene overview. |
| P1-02 | Connect two physical gamepads before starting. Identify A and B, then move/look/interact on A while B is idle; repeat with B; finally operate both simultaneously. | Each device affects only its assigned player and viewport. Both can start a new session and see a correctly framed first-person view. Record continuous footage with both hands/controllers visible or two testers' observations. |
| P1-03 | Start a fresh session with keyboard/mouse and one physical gamepad. Repeat isolated and simultaneous move/look/interact checks. | Keyboard/mouse drives one actor, gamepad the other. No shared mouse look, double interaction or loss of gamepad input when mouse capture changes. Record device assignment and footage. A keyboard-only debug fallback does not satisfy P1-02 or P1-03. |
| P1-04 | Walk through all doorways in opposite directions at once; stand together at reception and both boiler stations. Walk against walls and closed doors. | Both fit in relevant work areas, collision prevents crossing solid walls, neither becomes trapped, and door/room numbers remain readable in each viewport. Record any reproducible collision defect with location. |
| P1-05 | Grab a physics prop, walk and look while holding it, release it against floor/furniture, and have the other player pick it up. Attempt simultaneous pickup. | A prop has at most one holder, responds to Unity Physics after release, does not teleport the other player or keep a stale owner. Interaction prompts follow the correct viewport and target. Capture before/after ownership or visible behavior. |
| P1-06 | Use primary and secondary interaction on installed controls. Open and close the reception interface from each actor, then return to movement. | The advertised actions work, UI focus cannot strand either controller, and an actor's menu does not silently steal the other's movement. Record the actual control mapping in README. |

## Phase 2 — guests, assignment and price

| ID | Procedure | Acceptance and evidence |
| --- | --- | --- |
| P2-01 | Open planning and inspect every room and all three guest archetypes. | Cash, boiler condition, room condition/temperature/noise, booking traits, price and projected load are visible. Any displayed trait has a documented gameplay effect; fixed values are labelled or consistently represented. Screenshot one complete planning view. |
| P2-02 | Accept one booking into 101, reassign it to 102, reject it, then refill. Attempt a duplicate guest and an occupied room. Have both players issue conflicting assignments. | One guest occupies at most one room and one room has at most one booking. Reassignment clears old ownership. Invalid or conflicting commands are rejected visibly and do not change cash/load twice. Save resulting allocation. |
| P2-03 | Compare a cold-sensitive guest in the warmest room and the room with highest heat loss; compare a business guest in quiet and noisy/degraded rooms. | Planning explains the tradeoff and projected load reflects accepted guests. Preserve these plans for later satisfaction checks; a planning preview alone does not establish consequences. |
| P2-04 | Set a low and then high price for the same guest/room, try an invalid price, then start the shift. | Valid prices update expected income and expectation warnings; invalid input is constrained or rejected. Committed guest, room and price cannot drift after check-in. Capture both previews and the committed record. |

## Phase 3 — day lifecycle, satisfaction and settlement

| ID | Procedure | Acceptance and evidence |
| --- | --- | --- |
| P3-01 | Start with three selected bookings and wait for check-in. Let the configured shift end naturally. | Only accepted guests appear and occupy assigned rooms. Planning → Service → Settlement happens once; real-time service uses the configured duration. Save phase events and guest-room mapping. |
| P3-02 | Replay an identical room-condition trace and guest at two prices. Keep all other values equal. | Higher price increases expectations or price dissatisfaction. The same defect causes a greater dissatisfaction penalty at the higher price. Record the two scores and gross income; add an EditMode test for this comparison. |
| P3-03 | Complete a known plan with agreed prices and known compensation. Independently add its ledger. Reopen settlement and try ending the shift again. | Closing cash equals opening cash + gross room payments − compensation − daily operating costs. Each charge/revenue posts once; reopening UI does not settle twice. Show all ledger lines and arithmetic. |
| P3-04 | Run one guest with a short known mismatch and one with the same mismatch for much longer. | Longer exposure lowers satisfaction. Fast restoration gives a better result than late restoration for otherwise identical guests. This must arise from elapsed state exposure, not a canned outcome. |

## Phase 4 — causal boiler and complaints

| ID | Procedure | Acceptance and evidence |
| --- | --- | --- |
| P4-01 | From identical healthy starts, run four guests and then six comparable guests for the same duration. | Load equals accepted guest demand. Four fit safe capacity; six overload it. Overloaded wear/heat behavior differs according to config. Save a time series of occupancy, demand, safe load, condition, heating output and pressure. |
| P4-02 | Replay the six-guest plan with healthy and worn boiler condition. | Worn condition worsens output/risk and can reach overpressure failure earlier. No mandatory day-number disaster or random quest is needed. Capture failure threshold crossing and its cause. |
| P4-03 | With one cold-sensitive occupied room, use debug temperature only to create a sustained cold condition; then restore warmth. Repeat with no occupied room and with a temperature above tolerance. | A cold incident produces one deduplicated complaint for its guest after the configured exposure. Empty/comfortable rooms create no guest complaint. Stable recovery resolves the cause. Capture guest ID, reason, urgency, patience, creation time and resolution. |
| P4-04 | Perform the same cold scenario through load/condition alone, with debug overrides off. | Evidence shows **accepted demand → overload/wear → reduced heat → lower room temperature → dissatisfaction → complaint** in that order. A debug-spawned request cannot pass this scenario. |
| P4-05 | Leave a real complaint untouched until its patience expires and the day settles. Repeat while offering compensation. | Ignoring it is a valid choice with worse financial/reputation results. Compensation costs the displayed amount and changes guest response without falsely heating the room or marking its physical cause repaired. No repeated compensation exploit. |
| P4-06 | Hold comfortable state for a full shift, then replay a failing state with the same tick inputs at different rendering rates. | No unrelated timed fetch quest appears. Pure simulation outcomes match for the same tick sequence; complaints trace to incidents rather than camera or frame Update behavior. |

## Phase 5 — physical cooperative repair

Use the actual implemented labels/order printed on the machine. The required intended order is relief valve by A; then panel, breaker, two latches and restart by B. These are physical controls, not one repair progress bar.

| ID | Procedure | Acceptance and evidence |
| --- | --- | --- |
| P5-01 | Trigger the one supported overpressure failure using debug for setup only. A holds the relief valve while B opens the panel, uses the breaker, releases both latches and performs restart. | Gauge responds to A's action; restart is permitted only in the safe pressure band with A still supporting and the full B sequence valid. Heating returns through simulation, not direct request deletion. Two-person sequence takes about 20–60 seconds after players know it. Record uninterrupted split-screen video and timings. |
| P5-02 | At the restart step, A releases the valve early. B attempts restart after pressure exits the safe band. | Pressure visibly rises and restart fails with a readable reason. Holding the valve again allows a valid recovery. No permanent deadlock; the exact reset behavior is communicated. |
| P5-03 | With pressure safe, have B deliberately use a latch/restart in the wrong order. Then perform the correct order. | Wrong order cannot repair the boiler; a visible/audible failure and clear recoverable sequence state appear. Correct retry succeeds. |
| P5-04 | Let one actor try to operate both stations, including holding the valve then walking away, switching target or opening a UI. Attempt input from an unassigned actor. | Valve support requires ongoing valid physical interaction. One actor cannot satisfy both roles by a stale hold flag, rapid target switching or a fake actor ID. Unit-test command validation where practical, but use physical play for final acceptance. |
| P5-05 | During a valid repair, swap A/B for a second run. Walk to every station with the other actor in place and attempt interactions from beyond range. | Both input schemes can perform either role. Controls remain reachable and readable; out-of-range input cannot activate them. End/start a new day or session mid-hold to check clean release and ownership. |

## Phase 6 — maintenance, reviews and three days

| ID | Procedure | Acceptance and evidence |
| --- | --- | --- |
| P6-01 | From the same settlement, branch into cheap patch and proper repair. Inspect next planning and replay identical bookings. | Each choice charges exactly its shown cost, changes persistent condition by its shown effect and changes the next day's forecast/outcome. An unaffordable repair cannot create an undisclosed loan. Save both cash/condition traces. |
| P6-02 | Complete a cautious three-day run at low occupancy. Use ordinary controls and no state cheats. | Day 1 permits 3–4 guests, later days offer more/tempting applicants, and caution can avoid forced catastrophe. Day 3 leads to final results, not an endless day 4. Record full session duration and all settlements. |
| P6-03 | Complete a greedy three-day run, accepting six when available and choosing cheap maintenance where possible. | Higher gross income is visible alongside higher strain, response demands and potential losses. At least one failure is caused by chosen load/condition, and choices carry across days. Save reviews, reputation and final cash; do not require greed to always outperform caution. |
| P6-04 | Compare reviews for a comfortable cheap room, an expensive troubled room, and a compensated complaint. | Reviews reflect observed price/quality, duration/response and compensation. Reputation updates once from actual outcomes. Reviews never claim an event that did not occur. |

## Phase 7 — art, readability and audio

- Capture each player's ordinary view at reception, a room door, inside a room and both repair stations. At normal play distance, identify the valve, breaker, gauge and room number without relying only on HUD text.
- Inspect real wall thickness, recessed doors, separate slabs, skirting, crown trim, pilasters, chunky furniture and machinery. Geometry must carry depth instead of flat photo textures.
- Check cream plaster, burgundy/patterned carpet, dark wood, aged brass, warm lamps and visible repairs. The setting is an operating grand hotel, not the airplane reference or a horror ruin. Reference characters, logos and copied assets must not appear.
- Inspect failure from both stations: moving gauge/red zone, restrained steam/leak/flicker and escalating mechanical sound explain the state before a giant HUD does. Check audio listener configuration, audible warnings, clipping and split-screen balance. Placeholder sound quality is acceptable; contradictory or silent critical state is not.
- Confirm distinguishable staff placeholders, readable guest variants, idle/walk feedback and practical interaction pose. Verify URP materials and the lighting configuration actually used; record performance on the test machine rather than inventing a universal frame-rate claim.

## Phase 8 — debug, regression and playtest decision

- Verify each debug command independently: cash, condition, forced load, forced failure, room temperature, guest spawn, time advance, request resolution, start/end shift. Show active overrides and reset them on new session. A forced request resolution must not silently erase its still-active underlying condition.
- Run required EditMode coverage: satisfaction, price expectations, boiler load, degradation, causal incident generation, compensation and day-end revenue. Include duplicate-settlement protection, invalid assignment and repair command order/actor checks when those contracts exist. Use boundaries and representative traces instead of tests that merely repeat formulas line by line.
- Build and launch a Windows player outside the Editor, rerun P1-02/P1-03 and a full three-day path. Confirm generated assets/configs ship without Editor code or a paid/external runtime dependency.
- Two testers complete one uncoached 15–25 minute session after learning controls. Note time spent planning, servicing and repairing; difficulty finding controls; every complaint's understood cause; and whether accepting additional guests felt voluntary. Record the desired greed/regret exchange only if it occurs naturally; scripted dialogue is not evidence of fun.
- Release assessment lists passed IDs, blocked IDs, defects and machine/device limitations. No remaining compile errors or broken core path; unresolved controller/human-play gates are explicit. Keep new-feature ideas in BACKLOG.md.
