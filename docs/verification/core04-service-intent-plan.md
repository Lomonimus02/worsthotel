# Phase 4 proposal: service intent inside guest life

Status: design only, prepared while phase 3 is being verified. No phase 4 runtime change is authorized by this document. The authoritative requirements are [CORE04_REQUEST.txt](../CORE04_REQUEST.txt), sections 42–49, 65, 72–74 and 82. The unresolved Alt-Tab hang remains outside this work; this plan is not evidence of a fix or a long-pause test.

## Required outcome and existing gaps

Every active concern or agreement declares whether staff can work remotely, leave an item, or need a present guest. The guest schedule reads that same relationship. Disclosure remains governed by the existing physical phone / reception / room conversation; an intent cannot expose a private concern to hotel UI.

| Existing path | Phase 4 problem | Minimal change |
|---|---|---|
| `GuestServiceSystem.Communication.Communicate` always calls `ClearGuestResponseAction(guest, true)` | A heard direct decision immediately releases the guest into leisure, sleep or departure. | Disclosure selects the intent policy. Direct retains a finite wait; Remote and DropOff release the contact action normally. |
| `HotelSimulation.Guests.TickLivingGuests` knows physical response actions but not a heard request / pending room-key exchange | A separate service case cannot stop an incompatible scheduled activity. | Query the guest's one active Direct wait before Pack / sleep / next leisure activity. Checkout remains higher priority. |
| `IncidentSystem.TickLiving` feeds zero severity to unseen causes outside the room | Leaving the hotel can look like repairing the room and close the incident before return. | For an open Remote / awaiting-receipt DropOff concern, suspend room-cause recovery and escalation while outside. Fresh room perception after actual return decides recovery. |
| `DeliverBlanket` and bed interaction require `InAssignedRoom`, and immediately grant comfort | Staff must catch the guest; a delivery cannot wait for its recipient. | Add a real exterior room drop point. Deliver the actual carried blanket there, preserve its recipient and item generation, then grant comfort once when the guest receives it. |
| Environmental `ServiceCase.DueTime` currently doubles as a short response deadline | A heard case may expire during a legitimate outing. | Separate the direct decision window from physical completion/observation. Remote remains open until measured recovery or stay end; accepted DropOff remains eligible until receipt or stay end. |

Source hotspots: [communication](../../Assets/_WorstHotel/Scripts/Simulation/GuestServiceSystem.Communication.cs), [response lifecycle](../../Assets/_WorstHotel/Scripts/Simulation/GuestServiceSystem.Responses.cs), [guest scheduler integration](../../Assets/_WorstHotel/Scripts/Simulation/HotelSimulation.Guests.cs), [incident evaluation](../../Assets/_WorstHotel/Scripts/Simulation/IncidentSystem.Living.cs), [physical blanket](../../Assets/_WorstHotel/Scripts/Environment/RoomBlanketDeliveryInteraction.cs), [access/privacy](../../Assets/_WorstHotel/Scripts/Simulation/HotelSimulation.Presence.cs).

## One small model, attached to existing identities

Add `ServiceIntentType { Remote = 0, DropOff = 1, Direct = 2 }`. Keep existing `ServiceKind`, `ServiceStatus`, incident episode, response identity and promise outcome authoritative. An intent describes interaction requirements, not a second complaint or a second reward system.

Recommended `GuestServiceIntent` record, owned by `GuestServiceSystem`:

- Stable `Id`, `GuestId`, `RoomId`; existing `ResponseId` and optional `ServiceCaseId` / `IncidentId`; a small purpose enum for command-originated room move / compensation discussion.
- `Type`, lifecycle state `Pending / WaitingForReceipt / Completed / Cancelled / TimedOut`, and monotonic `Revision`.
- Finite simulation times: `CreatedAt`, optional `WaitingUntil`, `DeliveredAt`, `ReceivedAt`, `FinishedAt`. Use the existing `-1` optional-time convention; never serialize infinity.
- For delivery only: exact `DeliveryItemId`, `DeliveryItemGeneration`, authored delivery-point identity.
- For Direct only: waiting place `AssignedRoom / Reception`. One active Direct intent per guest. Remote concerns may coexist; they cannot create competing route ownership.

The `GuestAgent` exposes an active Direct-intent ID/query and delivery/remote queries for UI/debug. Avoid copying mutable intent state into both agent and manager. A command-originated room-key exchange may own a Direct intent without inventing a service complaint or increasing service budget/memory counts. The existing physical `ResponseActionId` remains solely the identity of a route/anchor action.

Policy mapping:

| Concern/action | Relationship |
|---|---|
| Cold room, power/lamp fault, actual received noise | Remote: fix the same causal source while the guest continues life. |
| Accepted/requested extra blanket with an identified recipient | DropOff: delivery at that guest's room point; receipt and comfort later if absent. |
| Late-checkout decision; proposed room move awaiting key exchange; compensation discussion | Direct until the decision/handoff, cancellation or deadline. |
| Wake-up / luggage agreement decision | Direct decision window; after agreement, release the wait. The future wake promise or actual suitcase storage keeps its existing completion rules. |

This does not add guaranteed chores or random room-change requests. The existing `RequestGuestMove` command supplies the room-change waiting scenario. If autonomous guest-generated room-change requests are required beyond that existing interaction, they need a separately agreed causal generation rule; do not silently describe a staff-proposed move as an NPC-generated request.

## Direct waiting and conflicts

Suggested helpers (names can follow current partial-file conventions):

```text
Services.BeginDirectIntent(guestId, purpose, responseId?, serviceCaseId?)
Services.GetDirectIntent(guestId)
Services.ShouldHoldGuestSchedule(guestId, now)
Services.CompleteDirectIntent(intentId, expectedRevision, outcome)
Services.CancelGuestIntents(guestId, cause)
HotelSimulation.HoldGuestForService(guest, intent)
HotelSimulation.ReleaseGuestServiceWait(guest, intent)
```

`Communicate` publishes the concern exactly once and activates its appropriate intent. A room/phone Direct wait clears the completed phone action and stages an ordinary quiet waiting pose in the assigned room. A reception Direct wait stays physically at reception. No shower, sleep or hotel outing starts during the finite wait. Existing `IsRoomState` / room perception remain accurate; a receptionist wait never becomes room exposure.

The schedule priority is: checkout / invalid ownership, required physical return or relocation, current physical response action, active Direct wait, then normal packing / sleep / leisure. No new infinite wait is introduced. Keep enum numbering stable; existing room/reception states plus a readable intent label can express waiting without adding three near-duplicate locomotion states.

Use configured `DirectWaitSeconds` and a small `OrdinaryContactLeadSeconds`, measured on `HotelGameClock`. Set the deadline once; opening or reopening UI cannot renew it. A proposed initial tuning is 40 and 12 simulation seconds respectively, subject to actual play pacing. The deadline cannot exceed checkout. An accepted future wake-up promise does not hold the guest overnight.

- Finishing a Direct decision releases the wait; a guest at reception uses the existing actual return route and versioned callback.
- Ignoring the decision times out once, records the existing applicable decline/ignored outcome once, cancels a pending room reservation when relevant, and resumes the schedule. It does not create a fake room fault or keep adding penalties every tick.
- Closing the menu is not an automatic acceptance or rejection. Explicit cancellation and timeout are the release paths.
- Room move cancellation releases only the pending destination/key-exchange reservation; the current room/key remains owned. Successful exchange updates reservation room immediately, then the actual move route owns the guest.
- Conflicting Direct operations reject or require explicit completion/cancellation of the first. Two staff cannot extend the deadline or commit a stale decision; existing host actor validation plus intent revision governs commands.
- Checkout, guest departure, invalidated cause and New Game cancel the wait safely. A guest already returning from reception still reaches the assigned room unless checkout replaces that route. Old route/action versions cannot revive an ended intent.

Ordinary generation and contact must check the next disruptive schedule boundary: sleeping, showering, packing/checkout, physical transfer, current response, or a planned outing within the configured lead time. Defer evaluation until a sensible window; do not interrupt just to manufacture a request. Debug forcing remains explicitly labelled and must respect ownership and physical completion invariants.

## Remote: fix now, assess on return

Attach Remote intent to the existing factual response/case, even when a serious complaint has no optional service case. Keep `IsKnownToHotel` / `HasContactedStaff` independent of type and waiting status.

While the guest is outside the assigned room:

- Do not accumulate room exposure, new room dissatisfaction or incident escalation from that room.
- Do not treat an empty perception snapshot as measured recovery. Preserve the causal incident and its last-observed information; label it as awaiting reassessment where useful.
- Allow real boiler/electrical/noise-source work to change the world normally. Record actual staff action using the existing causal hooks; no completion button substitutes for a repair.

After the existing real exterior → lobby → room return and a fresh `NeedEvaluator` sample, `IncidentSystem` and `UpdateCase` use their ordinary measured recovery duration. Improved state resolves the original case/episode; unchanged state continues it. Walking out alone earns no success/memory credit. Remote intent does not forbid ordinary guest outings.

Implement the suspension as a narrow query supplied to `IncidentSystem`, rather than letting it reach into a UI manager. Only active relevant intents suspend their own linked causes. Relocation uses its existing transfer policy; the new logic must not preserve a source that no longer reaches the new room forever.

## DropOff: a physical parcel, then a receipt

Author one small, labelled blanket delivery spot outside each occupied room, beside its existing door approach and clear of guest/staff passage. It must not make the room public or bypass knocking. Retain the current inside-bed delivery target for permitted entry and ordinary proactive delivery.

Add `RoomServiceDropOffInteraction` (room and stable anchor ID) and a host `GameSession` bridge that checks actor reach, exact carried `ServiceSupplyItem`, item generation, room owner and intended recipient. A menu acceptance cannot create stock or place a parcel.

Delivery sequence:

1. Guest's communicated DropOff request identifies the spot; the guest can continue normal life.
2. Staff actually takes a finite shelf blanket, carries it and uses the real spot. Model binds that same item/generation to the intent and marks delivery time. The item's actual body appears at the authored spot.
3. While absent, guest comfort/memory/fulfillment remains unchanged. The case says delivered, awaiting receipt; this is not still a request to bring another blanket.
4. On actual return/availability, the guest receives it once. The existing physical room-arrival acknowledgement plus fresh perception is the minimum gate; add a short door/parcel pickup gesture or anchor callback if the authored route requires it. A guest already inside but asleep must first become available; do not grant receipt through a sleeping/away flag alone.
5. Apply `BlanketComfortBonus`, `BlanketsDelivered` and applicable success memory once, then show the inside-bed blanket and end the exterior parcel. Repeated callbacks and repeated snapshots cannot duplicate stock, comfort or credit.

Minimal storage can retain the existing `Delivered` item location with an intent link distinguishing pending receipt from consumed blanket. If a separate location enum is clearer, append it without changing existing numeric values. Persist item generation/recipient/anchor before changing presentation.

If the guest checks out without receiving the parcel, cancel the intent without claiming fulfillment. Leave the actual unreceived blanket as a normal dropped/reclaimable item at that point; do not erase it or refill its slot at midnight. Staff can return that exact supply to the shelf, clearing the former recipient link. This reuses the phase 3 held/dropped retention pins and finite stock, avoiding a new inventory system.

Moving to another room invalidates the old delivery point. Before receipt, either require staff to reclaim/re-deliver the actual parcel or a real guest pickup en route; do not teleport it to the new room. After receipt, the existing delivered-blanket ownership follows the guest normally.

## DTO, authority and retention

- Add bounded intent snapshots to `ServiceLayerSnapshot`; agent carries only the active Direct ID. Model protocol and LAN protocol advance together when implemented.
- Validate intent owner/room, linked response/case/incident, type/purpose combination, ordered finite timestamps, revision and single active Direct owner before any replica mutation.
- A pending receipt must match exactly one blanket item and generation at its authored room anchor. Received status must agree with comfort/memory and item ownership. No referenced guest, response, case or item can be pruned.
- Root retention includes active intents and pending deliveries as protected owners; terminal intents follow the corresponding retained case/incident history. Refuse admission if bounds are full rather than evict an active wait or parcel.
- Client replicas display host type/wait/deadline/delivery status and submit ordinary commands. They do not run guest wait expiry, receipt, recovery or scheduling locally.
- Reuse existing command revisions for room/guest context and add the intent revision where a decision can outlive that context. Day equality never authorizes or rejects these operations by itself.

## Compatibility and gates

Activate intent behavior for `ContinuousOperations`; legacy shift fixtures keep their existing behavior and snapshot compatibility contract. Constructors default new tuning harmlessly. Do not weaken existing physical key handoff, shower/sleep privacy, explicit room permission, response action versioning, finite phone attempt/retry, compensation-cause recovery or host ownership checks.

Required model checks: Direct blocks sleep/shower/outing until completion/deadline; timeout releases room-key proposal once; midnight does not renew waits; cancellation/checkout interrupts safely; Remote outing alone does not resolve a complaint; actual repair plus actual return resolves through measured recovery; DropOff gives no early comfort and acknowledges once; stale room/generation/intent versions reject atomically; mirrors cannot advance any decision; pruning preserves active wait/parcel.

Required physical checks: a real reception Direct wait and return; an in-room pending key exchange across a would-be sleep/outing boundary; actual carried blanket to exterior point while guest is away, unchanged privacy, guest's real return and receipt; checkout with an unreceived parcel that can be reclaimed. Use labelled model fixture setup only for prerequisites, never fake route arrival in these navigation assertions.

Required actual-player checks after compile/Edit/Play pass: one SOLO Direct / DropOff / Remote chain with readable world/HUD feedback, then remote-player acceptance/delivery/receipt state on the same LAN session across a date boundary. Keep these new results separate from legacy 0.3.x diagnostics and from the deferred hang investigation.
