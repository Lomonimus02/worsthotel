# Prototype 0.2 — Phase 8 housekeeping

The root confirmed the complete Phase 8 gate: scene compilation and all 163 EditMode / 27 PlayMode tests passed, with zero skips. The preceding Phase 7 gate passed 148 / 24. The root owns all Unity execution; the verification agent did not launch Unity.

## One worker and actual room turnover

Housekeeping follows the completed electricity phase. It adds one visible worker, a single nonpreemptive queue, travel through authored doors, and a default 30 hotel seconds of cleaning after arrival. Players choose the next room's priority in the management book. A current assignment continues; a queued room's latest priority changes the next eligible selection. There is no cleaning minigame, collection of trash chores, staff hiring or employee progression.

A room becomes dirty when a checked-in guest who actually reached it releases its ownership through checkout, relocation or shift closure. A cancelled future booking, an unchecked-in arrival and an unused destination reservation do not dirty a room. Releasing an owned room also retains the departing guest's identity until presentation confirms that the physical body has cleared the doorway. Even an interrupted initial room journey keeps this departure guard, although the unserved room remains clean.

The departure token blocks both housekeeping and another check-in. Presentation checks the doorway with a 0.52-metre clearance margin and reports the matching guest/room pair. Wrong, repeated and stale acknowledgements cannot unlock a different room or remove a new occupant. The callback accepts the previous day's identity after the model roster changes; the old visible guest keeps its exit route across StartShift. A disappearing body is made inactive before its removal can acknowledge vacancy. A captured-simulation check prevents old bodies from changing a fresh session.

Dirty rooms may be reserved during planning, with their availability shown explicitly. Physical check-in requires a clean room with no departing body and no active moving/cleaning assignment. A reservation does not stop the worker from preparing the room. Relocation dirties the actual origin; a destination merely reserved during transit stays clean until it has really been used.

## Time, arrival and availability

The model dispatches only eligible vacant dirty rooms. Moving consumes no cleaning progress. Only the physical worker's matching arrival callback changes the task to Cleaning. Completing one task never spends leftover delta on cleaning the next room before its journey. If an occupied room unexpectedly invalidates an assignment, work is cancelled back to the queue and its partial progress resets; the new occupant is protected.

Preparation runs during Planning as well as normal Service. AdvancePreparation advances only the hotel clock and housekeeping: it does not apply boiler wear, room thermal changes, guest exposure or cash changes. StartShift resets the daily clock while preserving the worker's existing task and cleaning progress. The presentation rebases its movement budget when the clock resets.

Two-player WAIT can accelerate preparation when there is pending work. Consent, release/rearm and physical-input rules still apply. A housekeeping event interrupts WAIT; there is no acceleration without an available worker. Opening the management book allows ordinary preparation to continue, while closing it lets both players provide WAIT consent. Game physics stays at normal time.

Temporarily disabling the physical worker hides it and marks the model worker unavailable. Assignment/progress remain intact, but travel, arrival acknowledgement and cleaning stop. Re-enabling resumes them. The presentation retains a narrow session-change subscription while disabled so NewGame cannot create a secretly available worker behind a hidden presentation.

## Verification prepared for the gate

HousekeepingTests.cs adds fifteen EditMode cases covering:

- Actual checkout dirt, idempotent settlement, physical-departure blocking and no cleaning during travel.
- Future and waiting no-shows leaving unused rooms clean, with no departure tokens.
- Interrupted initial travel protecting a clean room across the next day's guest-list replacement.
- Relocation dirtying the used origin and preserving an unreached destination.
- Wrong/repeated vacancy callbacks leaving occupancy and queue identity intact.
- One nonpreemptive priority queue, unique tasks, physical arrival and no leftover-time cleaning.
- Occupied-room skipping and cancellation after unexpected reoccupation.
- Dirty-room planning/reservation followed by clean-only physical check-in.
- Daily clock reset preserving existing preparation progress.
- All six actually served rooms becoming dirty, then preparing for the next day without further wear, exposure or financial mutation.
- Invalid settings, actors, room callbacks and preparation deltas leaving state unchanged.
- Worker unavailability preventing hidden arrival/work while preserving progress.
- Developer checkout using the ordinary state, ownership, demand and physical-departure path.
- Developer dirt queuing once, retaining reservations and refusing occupied/departing rooms.
- A fresh session starting without previous turnover, and the legacy two-argument simulation preserving its established behavior.

The existing three-day living-model fixture now drives the actual vacancy acknowledgement, worker arrival and preparation APIs between days. It retains living mode and its original financial/lifecycle assertions. It does not directly reset room cleanliness. The separate six-room turnover test deliberately uses six selected offers to isolate preparation capacity; it is not evidence of a natural first-day booking pattern.

The environment agent's two PlayMode scenarios passed: an old guest exits physically after the next StartShift before the worker can enter and clean; and an all-dirty preparation scenario covers priority, real door travel, temporary worker disable, retained progress across StartShift, and check-in prompting. The root's third scenario also passed using the actual preparation worker and virtual pads: one vote cannot start WAIT, two votes accelerate until a housekeeping event, and boiler/cash/temperature/physical time stay unchanged.

## Bounded review and remaining limits

Read-only review covered model eligibility, queue identity, callbacks, planning/service clock integration, worker visibility, guest roster replacement, physical doorway guards and NewGame rebinding. No unresolved critical blocker was found. The disabled-worker/NewGame edge was hardened before freeze. The developer commands use the same turnover path and preserve pending departure tokens rather than bypassing physical vacancy.

Human judgement about queue readability, worker movement, day pacing, real controller ergonomics and real-display performance remains manual. This successful gate permits the three-day balance phase; automated housekeeping tests do not establish that the final day pacing is enjoyable.
