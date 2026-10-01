# Prototype 0.6.7 — First Day & Activity Cadence

## Opening and world clues

The clock is unchanged: 08:00 at opening, 720 real seconds per full hotel day (30 seconds per hotel hour). Room 101 keeps its existing unfinished bed and physical used linen. Four rooms are initially open for sale; the other baseline rooms can still be opened in RESERVATIONS. Infrastructure starts in its ordinary operational state.

The first existing sales enquiry is processed at opening with a deterministic accepting roll through the normal sales pipeline. It still requires an open, available room with nonzero demand. This normally creates the reservation for 101; it does not ready the room, check the guest in, deliver a key, or pay revenue. Later enquiries retain their existing demand rolls and prices. The first three Day 1 arrival appointments are 08:39, 09:03 and 09:27, corresponding to 19.5, 31.5 and 43.5 real seconds after opening; actual approach to reception takes physical travel time. Day 2 and later keep ordinary afternoon arrivals.

A physical ROOM STATUS board behind reception reads the authoritative/mirrored room state: NOT READY, READY, RESERVED, OCCUPIED, CHECKOUT, or CLOSED. Dirty state takes precedence over a reservation; RESERVED means a current arrival-day reservation or a guest awaiting entry. The board adds no HUD objective. A matching plaque marks 101. Used bundles are creased and labelled USED, distinct from CLEAN shelf stock. Signs connect the laundry hamper, clean shelf and bed. The key rack states that key numbers match room numbers.

Nearby waiting guests use existing subtitles to identify their room and ask for its key or acknowledge the unfinished room. Existing bell, impatience and abandonment remain. Speech is bounded to one line per patience stage and local actor; it does not interrupt an existing subtitle or open book. Existing labelled luggage and assistance choices remain.

## Scheduling changes

Dated stays start unpacking immediately on actual room arrival, for at most 6–10 seconds, then choose shower/TV or work according to the existing profile. Ordinary outing position varies across slots 3–5. The existing 28–48 second absence starts when the guest physically exits; the existing evening-return boundary remains an upper bound. Special guest dated return appointments are retained.

The previous final day slot repeated QuietRest until sleep. It now cycles a bounded varied tail of rest, TV, work/phone and shower, without replaying arrival or a second outing. Quiet rest is capped at a varied 12–18 seconds and adjacent quiet entries are avoided. After returning, a dated guest resumes their next activity immediately. Morning keeps its dated shower followed by varied rest/TV or work/phone until existing checkout preparation. Actual wake, sleep and checkout dates remain.

Heating, electricity, noise and room wear continue to come from actual guest activity and presence. No forced failures, new request types, clock acceleration, daytime skip, new staff or load multiplier were added. Economy, quota, laundry costs, supplies, expansion and special guest types retain their settings. This changes how frequently existing activities occur; human workload and evening/midday contrast require the actual play pass below.

## Verification and remaining acceptance

Two focused model checks passed: the shipped opening creates and replicates its reservation without readying 101; an explicitly comfortable single-room schedule fixture proceeds through activities, one outing, sleep, morning shower and checkout turnover. That fixture observed a longest awake quiet block of 16.25 seconds and 31.25 seconds away. It manually acknowledges physical route callbacks and suppresses incidental service pressure; it is not a playthrough or a balance measurement.

The physical scene check reads the board and captures labelled reception/101/laundry viewpoints. The existing full-world packet check measured 458,746 decoded bytes within its 524,288-byte limit. LAN protocol is 28; model schema remains 23 because existing schedule entries already serialize. Two-PC LAN is not verified by these checks.

Raw results: [model checks](verification/cadence067/model.xml), [scene and packet checks](verification/cadence067/scene.xml). Viewpoint captures are in `screenshots/cadence067/`; they are explicit fixtures, not evidence of walking a full day.

The Windows build succeeded on 1 October 2026. The subsequent human pass confirmed the opening board, room preparation, key handoff and luggage were understandable, but reported almost no activity from roughly 16:00 until morning and a blanket left unreceived at its corridor shelf. The cadence acceptance therefore failed despite the passing model checks. [The 0.6.8 correction](GUEST_LIFE_068.md) investigates physical guest routes with accumulated room use and actual delivery receipt.

## Local build

`Builds/Windows-0.6.7-first-day-cadence/TheWorstHotelEver.exe`, shortcut **Играть 0.6.7**. Share the complete build folder. Both LAN players need 0.6.7. State persists within a running session; no cross-launch save was added. This milestone has not been pushed or published to GitHub.
