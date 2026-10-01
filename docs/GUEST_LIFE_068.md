# 0.6.8 — Guest life and physical delivery receipts

Subsequent human feedback for the [0.6.9 milestone](PACING_069.md) confirmed living guests and successful item delivery, but still found too little operational work after check-in. The original automated evidence below proves the route/receipt correction; it does not establish that the evening pacing was sufficient.

## Human finding and reproduced cause

The 0.6.7 human pass confirmed that the opening board, room preparation, keys and luggage were understandable. The hotel then felt inactive from roughly 16:00 until morning, apart from a wake-up request. A promised extra blanket remained on the designated corridor shelf without a receipt or thanks.

The existing activity generator was already producing shower/TV/work/outings. Physical presentation had to acknowledge each activity anchor before the model started its duration and output. A guest stalled on that route therefore retained `ActivityStaged=false` and infinite activity deadlines, blocking subsequent activities and even the ordinary sleep transition. Blanket receipt also required a staged, awake guest.

The physical scene reproduced the failure after real showers. Used towels activate an interaction collider in the route from the inner aisle to the rest/TV position. Its bounds in 104 were centered at `(6.30, 0.18, 15.58)` with extents `(0.43, 0.18, 0.29)`. Both static-route sweeps and dynamic-person sweeps treated the low cloth as a solid obstacle. A pre-shower scene sweep passed because this collider was still disabled. The sustained scene run then recorded room 104 trying to reach RestAnchor for more than 60 seconds (two hotel hours), against the used towel target. This links the defect to real accumulated room use, rather than insufficient schedule probabilities.

Both guest walking sweeps now step over the soft towel bundle, while its real interaction collider remains available to player cleaning. Furniture, room walls, doors, carts, people and the waste basket retain their normal collision behavior. Towels remain visible turnover work; this does not clean them or bypass physical room arrival.

A second scene run isolated a separate contact error: room 101 remained on the unpack approach for over 60 seconds beside its stationary suitcase. Collision handling used the relative speed, including the guest's own walking speed, as incoming object speed. Walking into a resting suitcase could therefore repeatedly trigger knockback. Impacts now use the other rigidbody's velocity toward the guest; genuine moving luggage/cart impacts and the separate player-contact response remain. This also explains delays before any shower had occurred.

## Existing activity audit

| Activity | Existing trigger/cadence | Actual effects / gating |
| --- | --- | --- |
| Unpack | Immediately after reaching the assigned room; 6–10 seconds after staging | Physical unpack anchor, room use; no new task |
| Shower | Initial shower/TV choice, profile-weighted later entries, morning shower; normally 16–28 seconds | Physical shower anchor; real hot-water demand (configured multiplier 1.6), noise, used towels |
| TV / loud room | Existing profile/trait choices and varied late-day tail | Physical rest/TV anchor; power-dependent sound and actual neighbour noise |
| Work / phone | Business and ordinary profile choices; 12–22 or 16–28 seconds | Real desk/phone routes, existing power/noise behavior |
| Quiet rest | Bounded 12–18 seconds, without adjacent quiet entries | Baseline room use; previously never began after blocked towel route |
| Outing / return | One optional outing at slots 3–5; business .25, budget .65, cold-sensitive .40 | Real doorway/corridor/exterior travel; 28–48 seconds outside for ordinary stays, then physical return; special dated returns retained |
| Radiator self-help | Existing sustained mild cold, unused self-response, available valve step | Real valve approach changes the actual radiator setting and heating demand |
| Service contact | Existing needs, eligibility, request budget, tolerance, contact windows and retry limits | Phone or reception route and player response; now starts only after the current room activity has physically staged |
| Sleep / wake / checkout | Existing dated preferences and checkout calendar | Real bed and morning routes, departure and persistent turnover |

The probabilities, schedule entries, clock, quota, prices and request budget were not increased in this correction. Quiet activity audio now uses the square root of its existing normalized source level for playback volume, so active shower/TV sources remain audible nearby. This changes audio feedback, not noise exposure, dissatisfaction or infrastructure load.

## Blanket receipt

The player still accepts the existing blanket request and leaves the actual stock item at that room's corridor shelf. Sleeping, showering, away or busy guests wait until available. A physically staged guest then walks through their own door to the shelf using the existing route controller, reaches for the parcel, and acknowledges the actual item/revision. Only then does the existing service system grant comfort, fulfilled-service memory and satisfaction, remove the parcel from the shelf and show the blanket on the bed. A nearby player receives a short thank-you subtitle. The guest returns through the door and resumes the same schedule.

An existing delivery intent records a `Collecting` flag during this physical approach. Stale/duplicate receipts and mirror mutations are refused. Cancelled or reclaimed parcels cannot grant comfort. No independent AI manager, new request type, forced complaint, scripted evening event or dialogue system was introduced. LAN protocol 29 / model schema 24 carry the collection state; the host still owns actual movement and completion.

## Verification status

Before-fix evidence: a production-scene observation with explicit staff adapters for starting linen and key handoff reproduced the blocked guest. It did not inject guest travel callbacks or activity choices. The separate empty-room sweep passed, explaining why checks without accumulated towels were insufficient.

After-fix focused validation passed: one EditMode receipt/snapshot case and three PlayMode cases (423.9 seconds total). The sustained production-scene observation ran at the unchanged clock from 08:00 to 22:00 with four naturally arriving guests. Its explicit staff adapters prepared the opening bed, gave model keys only on actual reception arrival, and answered/delivered one naturally generated blanket request. No guest arrivals, activity choices, needs, travel completion or pickup acknowledgement were injected.

| Room | Physically staged activity starts | Observed activities |
| --- | ---: | --- |
| 101 | 13 | Unpack, shower, TV, loud room, phone, quiet rest |
| 102 | 13 | Unpack, shower, TV, work, phone, quiet rest |
| 103 | 14 | Unpack, shower, TV, work, quiet rest |
| 104 | 10 | Unpack, shower, TV, loud room, phone, quiet rest |

Rooms 101, 102 and 104 physically left and returned. The longest hotel-wide gap between observed staged activity/away transitions during 16:00–22:00 was 17.4 real seconds (about 35 hotel minutes), including the interval boundaries. Real hot-water demand and received neighbour noise were nonzero. Room 102 physically collected its natural delivery at elapsed 95.0 seconds (about 11:10), at the shelf rather than inside the room, and received a +2 blanket comfort bonus and one fulfilled-service memory. Its return and later normal activities are in the same trace.

The focused model test checked receipt delay, collection snapshot round-trip, read-only mirror rejection, stale revision rejection and exactly-once completion. The towel sweep used enabled, visible used-towel colliders in all ten rooms; the existing complete-world LAN packet-size case passed. These are deliberately scoped checks; they do not establish two-PC LAN acceptance or audible/visual quality for a human player.

Raw [scene results](verification/life068/life068-after.xml), [receipt results](verification/life068/receipt068-model.xml), [after trace](verification/life068/guest-life-cadence.txt), [towel failure](verification/life068/guest-life-before.txt) and [stationary-bag failure](verification/life068/guest-life-contact-before.txt) are retained. A normal player pass of 0.6.8 is still pending. Automated staff adapters and scene observations are not a full human playthrough.

## Local build

Built successfully: `Builds/Windows-0.6.8-guest-life/TheWorstHotelEver.exe`, shortcut **Играть 0.6.8**. [Build result](verification/life068/build-result.txt). The complete folder is required for sharing. This correction is local until explicitly uploaded.

The standalone EXE reached its main menu under native Computer Use. A Windows Firewall prompt covered the menu; the assistant did not interact with that security prompt and asked the user to dismiss it manually. Normal SOLO player acceptance remains pending.
