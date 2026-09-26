# Core 0.4 phase 4 — Service intent and guest availability

Status: **passed**. Full model suite 441/441; all seven selected gameplay cases have passing results across the runs below. This is not a fresh full PlayMode-suite pass.

## Implemented behavior

- Continuous operations use authoritative Remote, DropOff and Direct intents. Legacy shift fixtures retain their old behavior.
- Factual temperature, noise, electricity and lamp incidents continue while the guest follows their schedule. Absence does not count as recovery; evaluation resumes after actual return.
- An explicitly communicated and accepted blanket request has a stable exterior room delivery point. Staff must carry and release a real blanket there. While the guest is away, asleep or showering, the item remains visible and gives no comfort, service credit or receipt. Availability later completes receipt exactly once.
- Cancellation, checkout or relocation makes an unreceived parcel reclaimable at its physical location. Midnight stock renewal cannot take it away. Interior physical delivery records the same real receipt.
- Decisions about late checkout, wake calls and luggage, plus proposed room-key exchange, create a finite guest wait. Closing UI does not renew or cancel it. Decision, correct key exchange, timeout or checkout releases the wait; checkout has priority. Correct initial check-in clears obsolete luggage contact atomically before the guest starts walking to their room.
- Ordinary contact checks sleep, shower, relocation, imminent departure and conflicting Direct work. No additional compulsory request generator was added.
- Continuous cancellation of a room move carries the exact intent ID and revision, so a delayed partner action cannot cancel a newer proposal for the same guest.
- Model schema 7 / LAN protocol 8 include intent state, exact parcel identity/generation and the scheduling owner's intent ID. Validation runs before state mutation; malformed snapshots do not consume their sequence.

## Verification

The first compilation overlapped the additional LAN guard edits and observed a new test against the previous runtime assembly; the frozen source compiled successfully in `Logs/core04-phase4-scene.log`. No pass is claimed from that first log.

- Full EditMode first run: 439/441, `Logs/core04-phase4-edit-first.xml`. Both failures were the newly added immediate check-in fixture querying a scheduled guest before their dated arrival. The corrected fixture explicitly advances to that reservation's arrival, then retains the no-tick handoff-to-snapshot assertion.
- Full EditMode final run: **441/441**, 19.85 s, `Logs/core04-phase4-edit-r2.xml` / `Logs/core04-phase4-edit-r2.log`.
- First selected PlayMode run: **5/7**, 106.10 s, `Logs/core04-phase4-play-first.xml`. Direct UI persistence, stale partner cancellation, actual guest reception return route, ordinary pause and real WAIT passed. Both exterior delivery cases found a real authored obstruction: right-side shelves sat directly behind the existing knocker collider. The shelf assembly moved to the opposite jamb; existing colliders and strict raycast assertions were retained.
- Delivery rerun r2: **0/2**, 25.61 s, `Logs/core04-phase4-delivery-r2.xml`. This exposed a second authoring error: the actual wall collider is twice its visible depth, so the shelf use box was inside the solid wall. The saved prefab/scene bounds confirmed the corridor face at |x|=1.83, not 1.99. The entire shelf was moved to |x|=1.74; tray and parcel now clear the physical wall. Neither walls nor interaction rules were weakened. Diagnostic first-hit/bounds output was added to the physical assertion. Corrected scene r3 and rerun pending.
- Delivery rerun r3: **1/2**, 35.47 s, `Logs/core04-phase4-delivery-r3.xml`. Actual cancellation, reclaim and physical stock return passed. The receipt case passed placement, no early credit, later same-item receipt and once-only memory, then checked its renderers before the normal LateUpdate presentation pass. A bounded visual-settle assertion is used for the final rerun; the visibility assertions remain.
- Final physical receipt rerun: **1/1 PASS**, `Logs/core04-phase4-receipt-r4.xml` / `Logs/core04-phase4-receipt-r4.log`. Pickup/carry/placement, unchanged item generation, deferred receipt, once-only service credit, hidden exterior parcel and visible bed blanket all passed. Latest authored scene is from `Logs/core04-phase4-scene-r3.log`.

Relevant physical tests explicitly stage the guest through labelled model adapters. Staff pickup, carry, exterior placement, reclaim and shelf return use actual controllers and physical bodies. These tests do not claim to exercise guest pathfinding or human pacing. Existing route tests remain separate.

Source-path LAN audit: remote staff input is connection/epoch bound to host actor 1, then uses the common `PlayerInteractor` raycast and `HotelInteractable` dispatch. `GameSession.ServiceTarget`/`HeldServiceItem` check the actual focused target, range, item body and holder before placement. Every Rigidbody and renderer is registered by `LanWorldReplicator`; the fixed blanket body is never renamed, reparented or replaced by delivery/cancellation, so its world ID is stable. The client disables local interaction drivers. This supports the existing host-driven physical path but is not an actual phase-4 two-EXE result. Separate model/world packets can briefly differ in presentation as with the other physical items.

The previous Alt+Tab whole-image freeze investigation remains deferred and unresolved.
