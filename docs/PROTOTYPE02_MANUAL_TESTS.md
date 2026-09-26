# Prototype 0.2 — manual verification after the gameplay correction

These procedures target physical room keys, player-performed linen turnover, door knocking and meaningful noise responses. The complete gameplay regression is being run separately; consult [VERIFICATION.md](VERIFICATION.md) and current logs for its result. Earlier Phase 10 results, the older Windows tour and its nineteen captures predate this correction. They do not verify the new key, linen or knocker flow. Use the current rebuilt scene or a delivery build explicitly verified after this correction.

The starting hotel has no automatic housekeeper. Players prepare rooms themselves; employee code is retained for possible future progression, which is not implemented here.

For the current local regression harness, use two physical gamepads, then keyboard/mouse plus one gamepad. Local split-screen is a development harness; the intended product direction is one player per computer. F8 is an inspection aid, not evidence of two-player cooperation. F2 in Editor/development builds pauses the hotel: close it before observing movement or recovery. Label every diagnostic setup; a forced condition is not a natural hotel story.

## Controls and physical sources

| Action | Keyboard/mouse | Gamepad |
| --- | --- | --- |
| Give held key, deposit linen, knock or ask | E, one press | A / Cross, one press |
| Make bed | Hold E for 1.5 active-play seconds | Hold A / Cross for 1.5 active-play seconds |
| Pick up / drop actual object | F | RB / R1 |
| Alternate mechanical action | Q | X / Square |
| Consent to WAIT | Hold T | Hold Y / Triangle |

Continuous boiler controls still require continuous input. Giving a key and depositing linen have no progress-bar wait. The optional direct conversation with an in-room noisy guest remains a short interaction; the hallway route is knock, then a second press to ask.

Keys 101–106 come from the numbered rack beside reception. Clean linen comes from six physical utility-shelf slots; dirty linen goes to the adjacent hamper. The portable heater starts in utility storage. None should appear directly in a player's hands.

## Arrival — find, take and give the correct key

1. Start a new game, inspect applications, disclosed traits, room readiness, acoustic links and circuits. Accept a small plan. Confirm each device controls its own player.
2. Watch a guest physically reach reception and read their assigned room. Taking a key must not itself check anyone in.
3. Take a visibly numbered key with F/RB. The same object leaves its matching slot, follows the physical grip and can be dropped and recovered. Another player cannot acquire it simultaneously.
4. Aim at the waiting guest with a wrong key and press once. Check-in fails with a clear room-number explanation; the wrong key remains yours. Take the assigned key and press once at the guest. That actual key transfers and the guest starts walking without a check-in hold.
5. Watch the guest pass through the real door and reach their room. Reservations and waiting/travelling guests must not contribute occupied-room heat/electrical demand. Activities begin after physical arrival.
6. Repeat with a dirty or still-vacating assigned room. Correct key possession must not bypass preparation or the old guest's departure guard.
7. Observe checkout and key return to the original numbered source. NewGame also restores source identities and clears old ownership. No duplicate or permanently lost key appears.

Record identities, hotel-time arrival/handoff, real key-finding time, wrong-key feedback and route obstructions. A long hold is not an acceptable replacement for finding and giving the physical key.

## Scenario A — Hear the source and respond to noise

1. Assign a noise-sensitive/business guest to 102 and a guest with the disclosed Noisy trait to 103, or use another explicitly connected pair. The 102↔103 link is a corridor connection; shared walls follow odd/even room chains. Complete physical key handoffs and room entry.
2. For natural play, leave schedules untouched. For a separate diagnostic, trigger the source guest's loud activity in F2 and close it. Do not replace the source with an arbitrary received-noise override.
3. Approach the rooms before relying on the ledger. Listen near the source, then compare source output, received noise, tolerance and the developing situation. Exposure precedes a complaint; source and complaining room can differ.
4. Aim at the separate brass knocker beneath the room-status plaque and press E/A. After the guest answers, press again within the six active-play-second window to ask them to keep it down. Aiming at the ordinary door still opens/closes it.
5. Another player cannot reuse your first knock as their answer. Expiry requires another knock. Pause between presses and resume: pause should not consume the answer window. An occupant change, disable or NewGame invalidates an old answer.
6. Check actual source and neighbouring received noise fall. The situation recovers because conditions improve, without a separate “complete complaint” action. Another continuing source can prevent complete recovery.
7. A Noisy guest's default agreement lasts 25 hotel seconds, capped by checkout. Repetition cannot extend an active promise. A guest without that trait keeps loud music/TV reduced until checkout. Schedules continue; a quiet agreement must not mute a shower or erase its hot-water demand.

**Human listening required:** compare both players' positions and simultaneous rooms. Can players identify where to investigate, hear the reduction, and distinguish radio, shower, phone and boiler? The original synthesized audio currently uses a shared local mix without wall occlusion or guaranteed directional acoustics. Source-state tests do not establish perceived volume, localization or clarity.

## Alternatives — move, compensate or accept the consequence

Replay a noise situation using different responses; none is a mandatory quest path.

- **Move the receiver:** select a suitable clean, free destination in the service book. Physically collect its key and give it to the guest. The proposal must not silently move the guest before key handoff. Watch the route and resulting received noise.
- **Move the source:** use the same destination-key flow for the noisy guest. Their source leaves the old room; the new placement can affect other neighbours. A served origin becomes dirty and requires player turnover.
- **Compensate:** record credit, current dissatisfaction, measured noise and exposure first. Compensation grants a temporary response pause and reduces current dissatisfaction. It does not silence the source, repair temperature or erase lifetime exposure, accumulated quality/service history or review consequences. Defaults are 25 hotel seconds of relief and a 0.35 reduction for currently accepted need reasons. Persistent causes can produce another episode and eventually exhaust renewed patience. Repeated clicks cannot add or refresh the same credit.
- **Ignore deliberately:** acknowledge the decision and continue other work. Continuing discomfort can damage satisfaction, refunds or reviews. This remains a valid management choice.

Existing incident timers pause during physical relocation; walking is not itself a cure. A different later problem must not inherit acceptance from an earlier compensated case. Record destination key, source/receiver changes, travel, history, credit and financial effect.

## Scenario B — Portable heater and manual control

1. Give a cold-sensitive guest the correct key for 104. Use a naturally cold room or label a separate F2 temperature setup. Keep 104's degraded-fixture condition distinct from cold exposure.
2. Carry the real heater from utility storage through the door and place its whole body in the room. Carried or doorway-straddling equipment delivers no heat.
3. Switch it on normally. Compare actual temperature and Circuit B demand over time. Default heat output adds 14°C to the thermal target; it is not an absolute room-temperature setting.
4. Listen for the powered, placed fan. Switch off, carry out, place back and switch on. Registration must not duplicate heat/load, and carried or unpowered equipment must not sound as if it is heating.
5. Watch temperature and switch off when warm. This is a manual heater without a thermostat; leaving it on can overheat a room. Local electric warmth must not make a disconnected central radiator appear to supply boiler heat.

Record temperature, hotel time, placement, switch/power state and requested/delivered load. Use a lightly loaded circuit to isolate warming; Scenario C intentionally exceeds capacity.

## Scenario C — Electrical overload

1. Physically issue keys and admit guests to 104, 105 and 106. Place and switch on the heater in a B room. Quiet occupied demand plus heater is 3×0.85+2=4.55 against capacity 4; loud electric activity may add more. Reservations/travelling guests do not count as occupied consumers.
2. Keep actual demand above capacity. Observe warning after six continuous hotel seconds and trip after eighteen. Do not use diagnostic trip/load buttons for this test.
3. Confirm B lights, radio and heater lose delivered power while requested demand remains visible. A stays powered; shower sound/hot-water demand remains independent. Warning dim must not falsely imply a complete outage.
4. Remove or switch off enough load. Open the cabinet and reset B using its actual breaker. Observe longer than the previous trip delay; safe demand should remain powered.
5. Allow sufficient outage exposure to observe a room-condition situation and later complaint. A breaker event alone must not instantly manufacture a complaint.

Record consumers, load, warning/trip/reset times, guest exposure and reviews. A brief recovered overload must not trip. Inspect cabinet labels from normal approaches in both views; a close diagnostic camera crop does not establish comfortable readability.

## Scenario D — Resetting without removing the cause

1. Trip B as above while keeping the three occupied rooms and switched-on heater connected.
2. Physically reset without reducing demand. Verify another warning/trip sequence; reset must not erase overload or silently unregister the heater.
3. Switch the heater off or reduce demand, reset again and verify stable power. The other circuit and independent boiler controls retain their correct state.

Record trip count, load and time to retrip. Explain the cause using visible state before judging whether players understand it.

## Scenario E — Carrying through doors and releasing safely

1. Take a loose lobby suitcase. Move sideways, stop for three seconds, turn sharply and stop again. It should follow with weight and settle without persistent orbiting or rotation.
2. Carry against a wall and through a doorway. Solid contact must not cause teleporting, unbounded acceleration or violent player displacement.
3. Repeat with linen from both room and corridor sides. Open the door through real input while carrying. It should swing away from the approaching player, without sweeping the bundle behind its leaf or into the hinge-side wall. Keep the body in view while crossing: the player alone reaching the corridor is not a successful carry.
4. Release and pick up. Free bodies regain normal damping, collision/interpolation, solver and speed-limit settings. Source-docked items are intentionally frozen until taken; dropped items must remain recoverable.
5. Test exclusive ownership with the other player. Pause, disconnect or open the owning player's ledger while carrying: the grip ends safely and item ownership remains consistent.

Record devices, actual frame rate, settling, wall/door contact, body-versus-hand separation and lost items. Automated checks do not replace subjective weight and ordinary-display testing.

## Scenario F — WAIT does not perform work

1. Put down items, finish continuous interactions, close the ledger and stand still. If an unanswered critical situation blocks WAIT, make an explicit response; do not silently repair it with diagnostics.
2. One player's WAIT hold keeps hotel time normal. The other independently holds for the configured one second; both votes enable the current 8× hotel clock.
3. An important arrival, activity, situation or electrical event stops acceleration with a reason. Still-held buttons cannot restart it; release and renew both votes.
4. Movement, looking, pause, disconnect, carrying or operating a repair control cancels/blocks consent. Reconnected devices cannot retain stale votes.
5. Repeat in Planning with dirty rooms. WAIT may advance appropriate hotel time and physical guest departures. It must not remove linen, deliver a bundle or make a bed. Rooms stay dirty until players work; there are no automatic worker-completion events.
6. Compare props, player motion and audio pitch before/during WAIT. Hotel time accelerates; Rigidbody/player time does not. The 1.5-second bed action must not finish in a fraction of a second because the hotel clock is accelerated.

Record votes/stops, event reasons, real versus hotel time and quiet intervals. Whether WAIT removes tedious downtime remains a human question.

## Scenario G — One owner changes linen while the other runs reception

Target approximately **30–60 real seconds for one understood room turnover**, not a forced minimum or an automated performance claim. Use ordinary input at normal player speed. Time from first taking the dirty bundle to room readiness; separately record the approach from reception. If much longer, identify travel, aiming, door or feedback friction rather than adding abstract cleanup steps.

1. Let a guest who reached their room check out. Confirm DIRTY / NOT READY and the departure guard. Future/no-show reservations must not dirty unused rooms. A correct key or linen interaction cannot bypass an old body still inside.
2. Owner A takes the room's one numbered dirty bundle from the bed and physically carries it through the door to the utility hamper. There are no twenty-piece trash searches or random cleaning quests.
3. Aim at the hamper with that dirty bundle and press once. The same item becomes delivered, cannot remain held or be accepted repeatedly, and a clean set cannot count as dirty disposal.
4. Take a visible clean bundle from the adjacent shelf and carry it back. Bed work before this room's dirty linen was delivered must fail; clean linen cannot appear in empty hands.
5. Start the 1.5-second bed action. Release partway once: cancel without consuming the clean set. Complete it: exactly one set is consumed and the made bed returns. Another owner cannot steal this bed claim; both owners may prepare different beds concurrently.
6. Meanwhile Owner B handles arrivals, assignments and key collection. Let a valuable guest need the dirty room: decide whether to prepare promptly, choose another ready room with its key, or accept delay consequences. There is no compulsory “change room 104” quest.

Then test day boundaries:

- Reserve a dirty room in Planning. A guest may wait, but the correct key cannot admit them early. Opening the ledger or using WAIT does not clean the room.
- Let six real occupied rooms check out on a later day, or label separate F2 dirt as diagnostic setup. Prepare all six using physical stock. No starting worker appears or makes progress for the players.
- The authorized next-day transition refills consumed shelf slots once. Held/dropped clean sets remain the same objects; repeated maintenance/day actions cannot duplicate them. Stock is finite during a shift. NewGame resets stock and room state.
- Start service with unfinished turnover. The clock reset must not mark a bed ready or spend linen. Old guest bodies finish departure before protected rooms can be reused.

Record room and guest identities, each real item action, walk/door time, cancellation, stock, readiness and Owner B's concurrent reception work. Record coordination and total attention, not just completion speed. During preparation-only time, verify the clock alone adds no service charges, guest exposure or boiler wear.

## Unchanged cooperative boiler repair

Replay cold through physical repair: one actor continuously holds relief while the other follows panel, breaker, latches and restart. Compare with correctly keyed relocation, local heater, compensation or an explicit decision to accept consequences.

In separate attempts release relief, look away, open the operator's ledger, pause and disconnect. Only valid current input can support the partner's sequence. Pressure/order failures should explain what happened. Electrical room-power reset cannot substitute for boiler repair. F8 switching is not a two-human pass.

## Scenario H — Three ordinary-input days

Play all three days with production settings and no forced activity, fault, temperature, noise or load. Earlier [balance](PROTOTYPE02_BALANCE.md) and [emergence](PROTOTYPE02_EMERGENCE.md) traces are context; check their correction status before treating them as current evidence. They do not substitute for human play with physical keys and linen travel.

Record seed, offers, rooms, prices, refusals, handoffs, maintenance, choices and WAIT. Compare cautious booking with greater demand. Include linen stock and readiness: a free dirty room is not immediately usable capacity.

Capture hotel and real time for keys, activities, complaints, warnings, repairs, noise conversations, transfers, turnover and settlement. On Day 1 record idle spans and WAIT usefulness. On Day 3 distinguish simultaneous independent causes from forced problems. Log gross, compensation/refunds, net, reputation, wear and factual reviews.

Ask both players whether room assignment mattered, a neighbour pairing caused regret, a response created a later problem, they chose money versus comfort/safety, they deliberately accepted a consequence, and roles shifted naturally between reception and rooms. Do not equate test counts with enjoyment. Sound mix, readable feedback, carrying comfort, real controllers and normal-display performance remain human checks.

## LAN / direct-IP — human and two-computer procedure

Use the same final build on two computers on the same private LAN. Instructions and measured automated scope are in [NETWORKING.md](NETWORKING.md) and [VERIFICATION.md](VERIFICATION.md). The two-EXE localhost harness is reproducible with `tools/VerifyLAN.ps1`; do not treat it as two physical computers or two people.

1. Host on computer A. Enter A's displayed local IPv4 on B, then Join. Each screen must show one full-screen camera, one local input device and one audio listener. Repeat with keyboard/mouse and with a real gamepad.
2. Both owners open reception independently. Change a price or room, commit, and verify that both show the same bookings, money, clock and guests. Leave the client's ledger open while the host moves and performs a chore.
3. Have the client fetch the real assigned key, drop/regrab it and give it with one press. Host observes the same object and ownership. Repeat with the owners swapped; a second actor cannot acquire an owned item.
4. Turn over a used room with both owners splitting delivery/reception work. Check dirty pickup, hamper, finite shelf, short bed action and cancellation. Try simultaneous bed work: one owner per bed, no duplicate clean room or linen consumption.
5. Carry the heater from utility, place/switch it, trip and reset its actual circuit. Both computers must agree on temperature, power and light. Use the normal cooperative boiler procedure, including interruption of a held valve/latch.
6. Knock and request quiet, then compare both ledgers. Try physical-key relocation, credit and accepted consequences. Run WAIT with independent votes; moving, interacting or disconnecting must stop accelerated time.
7. Disconnect B while holding a key or continuous control. A must release the hold/item without a stale owner. Rejoin through the menu: B receives A's current hotel and can act again. Losing connection must never make B an independent authority.
8. Complete three days, maintenance and results. Start a new session from the host, verify the client receives the new roster/state and rejects old ownership. Exit hosting; the client must show a disconnected read-only state.

Record actual latency, errors, audio direction/volume, controls, legibility, physical collisions and human route durations. No Steam services, matchmaking, prediction or employee progression are required in this pass.

## Guest presence and SOLO — 0.2.2

Use [GUEST_PRESENCE_REQUEST.txt](GUEST_PRESENCE_REQUEST.txt) and the updated [Russian guide](HOW_TO_PLAY.ru.md). These are human checks; automated staging fixtures and a model-assisted three-day tour do not mark this procedure complete.

1. Launch with only a keyboard/mouse, then separately with one real gamepad. Select SOLO from the menu. Check full-screen view, no second body, no missing-controller pause and normal reception/key/linen work. Open and close the session menu, then restart after results.
2. Follow guests from reception through both left and right room doors. Doors should open for passage and close afterward; the guest must clear the moving leaf. Observe a crowded queue, an activity interrupted by checkout, and a relocation from either side of the corridor.
3. Observe natural sleep and a debug-requested sleep. The guest approaches the bed, settles horizontally without hovering or clipping through the mattress, then visibly gets up before walking. Check all room variants and accessories such as backpacks.
4. Observe a shower from the corridor and, separately, after deliberate emergency entry. Water/steam/sound should communicate presence without revealing a standing guest through the curtain. Shower demand starts at the anchor, and sound/water stop when the guest leaves. Compare quiet and loud-room sounds through a closed door.
5. Knock at a resting, loud, sleeping, showering and temporarily empty occupied room. Verify distinct permission, quiet request, refusal and no-answer responses. Interrupt the emergency hold before completion; complete it deliberately afterward. Exit from inside without becoming trapped.
6. During a SOLO boiler failure, secure pressure in the green band, release the temporary catch, walk to the panel and complete all controls. Measure whether the default window feels stressful but reasonable. Test expiry and pause, then verify that HOST/JOIN still needs the other owner holding relief.
7. Use SOLO WAIT with one consent. Arrival or another event must stop it; holding the same button through the event must not restart it. Complete three ordinary-input days with physical key handoffs and linen delivery, without diagnostic shortcuts.
8. Compare host and client views while a guest sleeps, showers, closes a door and leaves/returns. Confirm matching body visibility, curtain, door angle and room state. Disconnect and return to the SOLO menu; the old remote staff object must be gone.

Record build version, room/guest, activity, actual input, image/audio evidence, expected/observed result and any intervention. Normal-display performance, subjective staging, audio localization and pacing remain separate from automatic pass counts.
