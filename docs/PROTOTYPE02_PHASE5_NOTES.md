# Prototype 0.2 — Phase 5 room noise and temporary quiet

The root confirmed the Phase 5 gate: scene compilation and all 124 EditMode / 19 PlayMode tests passed. The preceding Phase 4 gate was 113 / 18. No Unity run was started by the verification agent; the root owns execution and the authoritative CHANGELOG.

## Authored acoustic model

The immutable room graph names direct connections. Shared walls join 101–103–105 and 102–104–106. Corridor links join 101–102, 103–104, 105–106, 101–104, 102–103, 103–106 and 104–105. The default transmissions are 0.70 across a shared wall and 0.30 along a corridor link. Therefore 102 and 103 can affect one another without claiming they share a wall. Room 105 has no direct acoustic link to 102; room 106 does have one to 103.

Only guests actually in their assigned occupied room emit an activity source. Source output comes from their real quiet-rest, shower or loud-room activity. A receiver sums its direct neighbours' sources with the corresponding attenuation. Received sound never becomes another source, so this model does not accidentally flood every room through repeated graph propagation. Own activity is excluded from the guest's received disturbance.

`SourceNoise` and `ReceivedNoise` retain their measured sums for inspection. The guest-facing total is the room's authored background plus received noise, clamped to 0–1. The existing 0.60 background of room 103 remains an independent pipe-side condition; it is not attributed to the guest's own activity. The explicit developer override replaces the total reading while preserving visible source/received measurements and can be cleared to restore ordinary computation.

This is an authored direct acoustic model. It does not simulate sound through changing door geometry or claim physically accurate wave propagation. The topology is exposed to planning and inspection so its gameplay consequences are explainable.

## Causal requests and temporary intervention

The new noise buildup multiplier defaults to 3. It changes how quickly noise dissatisfaction accumulates; it does not multiply actual exposure time, normalized severity, temperature dissatisfaction or the quality penalty weight. Existing need thresholds and staged situations still decide when an observed disturbance becomes a complaint.

`RequestQuiet` requires a valid staff actor and a guest physically in their room performing a loud activity. The successful request reduces the actual source to 0.15 of its normal output for 25 hotel seconds. It does not skip or reschedule the activity. Another request during this agreement is rejected without extending the deadline. At expiry the source returns to its current activity output. An expiry event is relevant when the guest is still performing a loud activity in the room.

The physical action extends the existing guest capsule. A 1.2-second conversation has one actor owner; releasing use or losing the interaction resets partial progress. The same capsule retains the reception key handoff behavior and displays a suitable in-room prompt. The model remains authoritative: the interaction submits the command after the real hold completes.

`LivingGuestAudio` supplies original synthesized shower/radio loops at authored activity anchors. It reads the current activity and authoritative source level, fades by distance to either local player, and pauses with the cooperative session. Auditory clarity, loudness and the two-player mix remain manual checks.

## Prepared verification

`NoiseTests.cs` adds eleven EditMode cases:

- Authored wall/corridor topology and rejection of duplicate, self and out-of-graph links.
- Actual source attenuation, no retransmission through a receiving room, and no complaint about one's own source in a zero-background fixture.
- Different business/budget need reactions to identical received sound.
- A complete ordinary seeded schedule using snapshots of the actual Session, LivingHotel, GuestNeeds and RoomNoise assets. Business stays in 102 while the same budget guest stays in 103 or 105. No forced activity, noise override or automatic quiet response is used. The test requires an actual scheduled loud activity and a business noise request only in the adjacent case, with greater measured noise exposure there.
- Atomic bounded override validation and returning to measured sound.
- Multiple actual sources retaining their raw sum while total noise stays normalized.
- Temporary quiet reduction, invalid-command rejection, no deadline stacking, expiry and event emission while the underlying loud activity continues.
- No sound source during reservation/reception/travel and source removal at scheduled checkout.
- Immediate settlement cleanup of instantaneous need severity and acoustic sources while retaining exposure, dissatisfaction and service history.
- The noise buildup multiplier affecting only dissatisfaction buildup for that need.
- Invalid/non-finite/amplifying configuration rejection.

The prior Phase 4 fixture now uses `Simulation.SetRoomNoise` for its explicit test measurement instead of writing a total that the new authoritative propagation pass would immediately replace. All earlier assertions are retained. Standalone need-evaluator unit tests can still supply a direct measured room value because they do not run propagation.

`NoiseGuestPlayModeTests.cs`, owned by the presentation agent, adds one real-input scenario: actual in-room guest raycast, partial hold, exclusive ownership, release cancellation, full conversation, source and neighbour reduction, unchanged activity deadline, temporary-quiet prompt and no repeated-use refresh. Its explicit public travel-adapter setup isolates this interaction; Phase 3 already covers the complete arrival/key/door route. It retains living mode, the generated scene and virtual gamepad input.

## Bounded review and remaining evidence

Read-only review found no unresolved critical issue in direct propagation, quiet-command guards, event timing, source checkout cleanup or physical hold cancellation. All source changes validate before applying measured room totals. Source contributions are ordered by guest ID and accumulated independently of room enumeration.

Two smaller Phase 4 findings are now patched in source: request-closure events use neutral wording that also describes checkout/developer resolution, and `ClearInstantaneous` clears current needs when a guest leaves an eligible physical state while retaining historical counters. Root check-in/release call that helper immediately; the new lifecycle regression covers settlement without relying on another service tick. These patches passed the Phase 5 automated gate alongside the new noise behavior.

The asset-based normal-schedule comparison passed with seed 1947. Business guest 102 accumulated 127.7992 seconds of noise exposure with the budget guest in 103 and 0 seconds with that same guest in 105. A business noise request occurred only in the adjacent case. These are measured exposed seconds for this deterministic run, not the wall-clock duration of a human play session or a universal balance claim.

Relocation is a Phase 6 command and therefore is not claimed as implemented or tested here. Its integration must move the source to the new occupied room without retaining a source or current need severity in the old room, and must preserve the same guest's schedule and history. Ordinary scheduled checkout and end-of-shift cleanup are covered in this phase.

Native rendered inspection, real controllers, two-player communication, audio mix and human pacing remain manual. The normal-schedule comparison is a deterministic model acceptance scenario, not evidence that every guest/day/seed is balanced or that the hotel is enjoyable to play.
