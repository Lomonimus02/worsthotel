# Prototype 0.2 — Phase 3 review notes

This note records the living-guest implementation and a bounded independent source review. The root confirmed the final Phase 3 scene compilation, 101 EditMode tests and 17 PlayMode tests passed without skips. The root task owns the authoritative gate results and CHANGELOG. These automated results do not replace the human playtest.

## Implemented contract

- Runtime `GameSession` always supplies a `LivingHotelSettings` snapshot. The optional two-argument `HotelSimulation` constructor remains a compatibility seam for existing isolated kernel tests, not a runtime switch used to hide regressions.
- A committed booking reserves its room. Scheduled, arriving and waiting guests do not occupy it, generate room heating demand or accumulate exposure to room conditions.
- Arrival times and activity entries derive from the configured seed, day and stable guest identity. Booking enumeration order does not change the schedule. Each guest's actual arrival and room arrival remain distinct events.
- The scene adapter reports reaching reception. A five-second key handoff uses the guest's real raycast collider, one actor owner and held primary input. Release, loss of focus, UI blocking, device cancellation or disappearance of the waiting state ends the attempt through the existing interaction lifecycle.
- Successful check-in changes reservation to occupancy. Room demand and room exposure begin only after the physical route reports that the guest reached the room.
- Reception waiting has its own elapsed time and overdue service penalty. It does not fabricate cold, noise, dirty-room or fixture exposure, or pretend to be an expired room complaint.
- Quiet rest, shower and loud activity have configurable heating multipliers and noise outputs. Activity noise is a source for the later noise/conflict phase; Phase 3 does not claim that authored acoustic propagation is complete.
- Developer force selects an explicit activity; skip consumes the next planned schedule entry. Neither can start a room activity for an unarrived guest.
- A guest with no actual room time contributes zero room revenue. Settlement releases rooms and demand once. Already visible guests can finish their exit after the ledger closes; scheduled guests become left without being spawned.

## Scene and lifetime review

The existing authored waypoint system is retained. Arrival routes use the open strip in front of reception, then independent waiting slots. Room routes approach the doorway along its normal. The moving guest repeatedly requests the real hinged door to open and waits for `IsPassageOpen` at the marked crossing. Within rooms, routes skirt the ends of the bed and use authored rest, shower and radio markers.

Guest travel spends elapsed hotel simulation time. Rigidbody physics and the physical key handoff remain on normal Unity time. After settlement, departing guests use normal presentation time because the hotel clock has stopped. This distinction avoids accelerating the physics world during WAIT.

`GameSession.Changed` only marks presentation dirty. Refresh/removal occurs outside dictionary enumeration, and a completed route is consumed before its adapter command is sent. This prevents synchronous completion callbacks from repeatedly firing or mutating the active guest enumeration. A replacement simulation instance clears old visuals and travel budget. Activity source objects are reset when presentation is disabled.

The reception capsule is active only while waiting. Collision pairs with both staff capsules are ignored when the target is enabled; the target still provides a raycast surface. Walking guests intentionally do not use a dynamic Rigidbody or obstacle-avoidance solver. This is an authored-route prototype, so moved luggage and player-created obstructions still need visual/manual checking.

## Focused finding reported during review

**Door crossing during an interrupted room-entry route:** presentation initially set `InsideRoom` only at final `RoomTarget` completion. If checkout or manual settlement interrupts `GoingToRoom` after the guest is already beyond the threshold, an exit route could still be built with `inRoom=false`. That route moved laterally toward the corridor at the current Z without a marked door crossing and could intersect the frame or a closing door. The presentation owner corrected this by tracking the actual threshold crossing and by making the exit route check the current side of the physical doorway even when its flag is stale. The patch was read back and its focused regression passed in the final EditMode run.

## Automated evidence prepared

`LivingGuestTests.cs` contains nine new EditMode cases:

1. Stable seeded arrivals despite reversed booking enumeration, with distinct staggered times and a changed-seed comparison.
2. Reservation, occupancy, demand and room-exposure separation before and after reception/room completion commands.
3. Invalid actor/guest/state commands and repeated check-in rejection.
4. Waiting patience and persistent satisfaction loss without invented room exposure.
5. Zero revenue for never checked-in bookings.
6. Shower demand, quiet recovery, next-entry skip and activity events.
7. Scheduled activity transition on hotel time.
8. Settlement followed by real exit-completion commands, without spawning future arrivals.
9. Repeated three-day simulation with explicit travel-adapter commands and one receipt per stay.

`LivingGuestPlayModeTests.cs` contains two new integration scenarios using the generated scene and actual Input System events:

1. Both actors consent to WAIT; the actual first seeded arrival interrupts that accelerated batch and requires fresh consent.
2. One guest walks from the entrance to reception, accepts a cancellable physical key handoff, walks through the real doorway, reaches the room marker and produces shower demand.

The four existing WAIT scenarios keep their assertions. Their quiet-interval setup clones the session and living config, delays first arrival to 60 hotel seconds and removes arrival jitter. It explicitly asserts `LivingEnabled`; shared configuration assets are not mutated. The separate arrival integration scenario uses the normal production schedule.

`GuestRouteTests.cs` adds one regression using all six real generated room/door markers. It supplies the stale inside flag at a position beyond each threshold, traces the resulting route across the actual door plane, and requires that crossing to use the marked gate at the centre of the door. This catches the interrupted-entry bug without adding a duplicate long walking scenario.

Phase 2 was reported passed at 91 EditMode / 15 PlayMode, followed by the first Phase 3 run at 100 EditMode / 17 PlayMode. The final scene compilation and 17 PlayMode tests passed under the `p02-phase3-final` gate. Because the new route regression was saved after that EditMode scan, the root ran an additional EditMode gate: `Logs/p02-phase3-route-editmode.xml` records 101 passed, zero skipped. The final verified Phase 3 suite is therefore 101 EditMode / 17 PlayMode.

## Remaining manual and later-phase checks

- Two physical gamepads and keyboard/mouse plus gamepad: actual key-handoff ergonomics, both players approaching a busy reception, and clear ownership feedback.
- Visual routes for all six rooms, all activity markers, interrupted entry/exit, guests queued together and luggage moved into their paths.
- Native rendered inspection of the arrival/check-in/activity indicators in both viewports. Model activity begins before the guest finishes its short within-room route; check that the visual delay remains understandable when water/radio effects activate at the marker.
- Observe departures when players quickly advance through settlement and maintenance. Starting the next booking day replaces the guest list; the current adapter can then remove any prior-day exit visuals that have not finished.
- Human pacing, audio cues/mix and readability remain manual. Noise propagation, escalating situations, multiple solutions, electricity and housekeeping belong to their later gated phases and are not certified by these tests.
