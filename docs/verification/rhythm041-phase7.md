# Prototype0.4.1 phase7 — staff room and sleep

Status: phase7 scene/compile and all15 selected physical/network/pause regressions PASS. Fresh executable and pixel gates remain phase9.

The compact staff niche occupies the west utility side, with two distinct cots, worn lockers, a small table/mug and warm lighting. Thin explicit partitions preserve the entry aisle and existing boiler/linen routes. Either employee can use either bed through the actual first-hit interaction surface; no bed use or wake teleports a player.

`WaitController` now distinguishes None/Wait/Sleep. Sleep latches one SOLO consent or both independent co-op consents, uses only the existing hotel clock at8×, and clamps the final normal tick to the nearest future06:00. A pending first vote expires at that same morning. Existing WAIT still stops at ordinary events. Sleep preserves the running calendar, actual activities, sales, wear, service deadlines and accounting; Rigidbody physics and Unity timescale remain ordinary.

The activation hold/release cannot cancel or immediately restart sleep. Fresh subsequent Use, deliberate rearmed movement/look, pause, menu/device/focus loss, expired remote input lease or session reset clears agreement. Real boiler failure, tripped branch and already communicated critical guest issues wake; warnings/minor activities/reservations/reports do not. An already paid active Full service can cover an existing failed boiler; its completion continues normally.

LAN18 carries a bounded controller view outside the unchanged model15 snapshot: mode, monotonic view revision, absolute deadline and two fixed actor/bed consent slots. The host alone accepts actual physical input. Outer validation precedes model application, rejects stale/malformed/mixed votes and computes morning from the **incoming** model date. Reconnect and new sessions clear readiness; no sleep command bypasses physical focus. An explicit reset clears even an old replica's presentation immediately.

## Measured gates and limitations

- Scene generation/compilation passed on the first attempt: `Logs/rhythm041-phase7-scene-attempt1.log`. Preserved the regenerated scene and five changed prefab artifacts consistently. Restored the author's unrelated vSync0→1 generation side effect to the existing0 setting.
- **15/15 passed on first attempt**,199.9419543 seconds, `Logs/rhythm041-phase7-playmode-attempt1.xml`. Six new actual-bed/controller scenarios, authored-room movement/access case, atomic frame case with25 malformed inputs and corrected same-sequence acceptance, plus seven existing WAIT/SOLO/pause/real-fault acceptance regressions.
- Fresh Windows SOLO and two actual localhost LAN processes, GPU pixels and final suite are still required. A host-input fixture is not two executable peers; virtual devices are not human controls or second-PC networking.

| Scenario | Seconds | Proven boundary |
|---|---:|---|
|BothStaffWalkIntoActualBedNicheAndBackWithoutBlockingUtilityControls|67.040254|Both real capsules walk from labelled outer approaches through entry/aisle and out; first bed use/partner approach/cancel; partition blocks bed use; relief, inspection, repair panel and linen remain accessible.|
|TwoActualBedsRequireDistinctConsentAndFreshCancelWhileOnlyHotelTimeAccelerates|8.418912|One consent1×, two distinct beds8×; actual held/release/fresh cancel; ordinary Rigidbody gravity; pending first vote expires at06.|
|SoloBedIgnoresRealFutureBookingAndReachesMorningWithoutResettingTheHotel|55.792133|Actual SOLO bed, new timed future booking does not wake, ordinary ticks reach06; same hotel/rooms/equipment/reservation, wear and one paid report retained.|
|RealLoadedBranchWarningDoesNotWakeButItsCausalTripImmediatelyEndsSleep|6.862633|Labelled room/heater setup, actual heater switches and consumer demand; warning does not wake; existing sustained-load timer trips branch and stops sleep that tick; central boiler unaffected.|
|PaidFullMaintenanceOfExistingFailureAllowsSleepAndCompletesOnceWithoutWaking|12.007385|Labelled funding/pre-existing failure and paid model service setup; actual bed admits planned Full shutdown; ordinary completion restores heat and charges once without wake.|
|PauseControllerLossAndNewGameClearBedConsentAndCannotReuseHeldInput|11.226406|Actual pause/device loss/session reset clear agreement and require fresh input.|
|RemoteBedConsentExpiresWithInputLeaseAndReconnectRequiresTwoFreshPhysicalVotes|4.043238|Authenticated host-input fixture in both vote orders;0.35s lease expiry/stale replay/disconnect/reconnect clear readiness. This is not two processes.|
|StaffSleepFramesValidateAtomicallyAcrossMorningAndRequireFreshRevision|1.137649|Explicit wire fixture;25 malformed/stale/mixed states leave model/view/UI unchanged; corrected same sequence applies; incoming06 boundary/fresh epoch/read-only client/immediate replica→NewGame cleanup.|

Read-only review additionally tightened impossible WAIT fields and found the connection-menu timing edge: menu input runs after the wait controller, so host LateUpdate now revokes active agreement before publishing a paused packet. These changes were included in the passing run. No clock/sleep difficulty setting was retuned.

No existing Alt-Tab hang is claimed fixed. No false sleep-only disaster, extra room sale capacity, second hotel clock or employee simulator is added.
