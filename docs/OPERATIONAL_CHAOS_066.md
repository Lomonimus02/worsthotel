# Prototype 0.6.6 — Operational Chaos & Hotel Entropy

Work accumulates from actual stays in the existing continuous hotel. There is no entropy statistic, random chore scheduler, new special guest type, employee system or daytime time-skip.

## Causes and resolution

| Cause | Persistent state | Staff action |
| --- | --- | --- |
| Served stay ends and guest physically vacates | Existing dirty linen and turnover task | Carry dirty set to hamper; use a clean set on the bed |
| At least 4 hotel hours actually inside the room | One waste bundle | Hold E at the waste basket for 3 seconds |
| At least 0.75 hotel hours of staged showering | One used towel bundle | Hold E on the bundle for 3 seconds |
| At least 0.4 hotel hours of staged loud activity, or unpacking with 3+ actual luggage items | Existing armchair displaced | Hold E on the chair for 2 seconds |
| Unhandled arrival at reception | Impatience, then failed check-in | Prepare/reassign room and hand over its actual key |
| Rare first real outing with room key | Key left on bedside table; returning guest seeks reception | Take STAFF key from rear wall behind reception; use actual room door |
| Accepted staff baggage handling, after guest reaches room | Service dissatisfaction while bag remains outside room | Deliver the actual bags |
| Actual powered room use, outside private sleep/shower state | Existing lamp condition decreases | Replace bulb using existing supply |

Room counters belong to the physical room, including after relocation. Reset elements cannot be cleared while occupied or while a departing body still owns the room. Linen preparation cannot bypass unresolved elements. Empty hands, focused target and an uninterrupted hold are required. The standard bed action clears the use counters only on completion. There are at most three coarse reset elements, in addition to existing linen work.

Only one initial bed (101) is marked dirty when operations start. Existing boiler and lamp starting condition is retained. No reset is attached to midnight, 06:00, sleep, or a new booking.

## Waiting and service

Patience uses the existing guest value, with a 35-second lower bound. Reception impatience stages occur at one and two patience intervals; at three the guest abandons check-in. A bell sounds on escalation, rather than every update. Unserved receipts have zero charge and zero extra fine; the booking cannot re-reserve the room after abandonment. Late check-in also leaves a bounded satisfaction impression.

A deterministic per-stay 1-in-9 opportunity is evaluated on the first real outing, with enough remaining time to resolve it. It is not evaluated periodically or multiplied by occupancy. The key stays physically in the room; it is recovered when the guest actually returns after staff assistance. STAFF does not grant general permission to enter unrelated occupied rooms.

An unresolved lockout can trigger existing early checkout after four patience intervals. Accepted baggage left undelivered can do so after five, with service dissatisfaction starting earlier. Actual delivery stops that exposure. The existing early-checkout policy must be enabled; natural checkout takes precedence. Temperature/noise/condition requests retain their existing causal escalation, communication and receipts. There is no flat timed cash penalty.

Dropped, carried and stored luggage pins its owner's retained identity until resolved. At the storage platform, carrying luggage whose owner has physically left allows Q / File lost property. This is an explicit staff action that clears the physical item; putting it down normally leaves it in storage. It never files current guests' bags.

## Pace and economy

Ordinary arrival schedules now cluster in small groups of three, without a separate rush state. On the first day, arrivals follow their sales decisions earlier (first potential group around 10:06 with shipped settings). Checkout timing remains shared, so occupied rooms require turnover together. Subsequent business still depends on readiness, actual demand and availability.

Daily contract, operating overhead, purchase/renovation costs and laundry prices are unchanged. Neglect loses actual sales or reduces existing stay receipts, making those expenses harder to cover. No work or incident rate is multiplied directly by occupied-room count.

## Verification and limits

Nine focused production-model cases passed: persistent room use, shower disorder, linen gating, overnight backlog, abandoned check-in, staff-key resolution versus early-departure refund, and stored-baggage retention/manual filing. [Raw results](verification/operations066/model-results.xml). Labelled headless travel adapters yielded 2/6/10 linen changes and 2/7/10 additional reset actions for 2/6/10 completed stays respectively; they are not human gameplay.

The scene interaction scenario passed using real gamepad look/grab/use and walking with the key. It checks all three hold actions, rejects a tap, preserves the dirty bed, takes STAFF from its authored location and walks it to the locked-out room's actual door. [Raw results](verification/operations066/physical-results.xml); [room view](screenshots/operations066/used-room.png). Fixture positioning and the rare lockout setup are explicit; this is not a full manual hotel day.

LAN protocol 27 / model schema 23 carry room counters, reset flags, waiting/lockout state, master-key ownership and lost property. Model snapshots round-trip through JSON and read-only mirrors reject mutations. The existing full-scene compressed-packet check measured 451,393 decoded bytes, within its 524,288-byte limit. [Passed packet case](verification/operations066/packet-case.xml), extracted unchanged from an earlier mixed run whose physical-key fixture was subsequently corrected and passed separately. It does not prove two-PC network play.

Human pacing across a complete six-room day and two-PC LAN remain separate acceptance checks. The technical checks do not establish that six rooms are comfortably manageable or ten rooms are correctly balanced.

The Windows build succeeded and reached its main menu through Computer Use on 1 October 2026. That process then suffered a native crash before SOLO was confirmed. Offline local symbols identify `BlockDoublingLinearAllocator::Rewind` via `ManagedTempMemScope::~ManagedTempMemScope` in the Unity player loop; this identifies the faulting stack, not its cause. No managed exception precedes it. The local ignored evidence is `Logs/operational066-native-menu-crash.log` and `Logs/operational066-menu-native-stacks.txt`. A normal manual launch was requested; it has not yet been confirmed. Do not interpret the passing scene checks as a successful standalone full-day playthrough or a fix for the older Alt-Tab issue.

## Local build

`Builds/Windows-0.6.6-operational-chaos/TheWorstHotelEver.exe`, shortcut **Играть 0.6.6**. Unity 6000.3.2f1. Share the entire build folder. State persists within a running session; no cross-launch save was added.
