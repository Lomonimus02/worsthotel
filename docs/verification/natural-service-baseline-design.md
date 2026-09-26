# Prototype 0.3.2 — natural service baseline and proposed boundaries

Historical baseline audit, 2026-09-26. The user subsequently explicitly deferred the hang investigation and authorized continuing the remaining natural-service work. Implementation now follows the companion contract; the baseline observations below describe the pre-change version. This audit itself does not prove runtime behavior or a hang fix.

The concrete fields, enums, callbacks, identity links, and file ownership are specified in [natural-service-api-contract.md](natural-service-api-contract.md). That companion records the contract used by the implementation and refines the original alternatives discussed below.

Authority: the current 0.3.2 user brief in attachment `52196d6c-637f-4650-931e-974c5efee79f/Pasted text.txt`. Read `README.md`, `docs/GAME_VISION.md`, `docs/ARCHITECTURE.md`, `docs/PROTOTYPE_SCOPE.md`, and `docs/CHANGELOG.md` against the implementation. Parts of vision/scope still describe 0.2 (including a starting housekeeper and no online mode); those historical descriptions do not override the current code or this brief.

## What the current implementation does

| Source observation | Consequence | Proposed correction |
| --- | --- | --- |
| `GuestServiceSystem.TryCreate` immediately increments `ServicesRequested` and calls `Notify`, which emits `SignalEvent`. HUD and service panels select every active case. | A private need becomes a hotel task immediately. | Separate private notice, contact attempt, and information actually communicated to staff. Only the last reveals full case details. |
| Mild cold uses `GuestNeeds.Temperature.ExposureSeconds`, documented and implemented as lifetime exposure during the visit. | Earlier unrelated cold can satisfy a later request's dwell threshold. | Accumulate current-cause dwell in the current response episode, without changing lifetime review history. |
| Wake/late eligibility checks `InAssignedRoom`; this includes a staged shower. | Schedule requests can originate during a shower. | Add an explicit activity/context gate and defer communication until a plausible moment. |
| `DeliverBlanket` changes actual carried-item ownership and comfort, then immediately fulfills an active cold case. | Completion can precede a measured improvement, or occur while the guest remains cold. | Keep physical delivery; close the environmental case only after sustained measured recovery. |
| `IncidentSystem.Living` sets `HasContactedStaff` on its automatic observed-to-complaint transition. | Hiding soft-service cards alone leaves an independent omniscient complaint path. | Separate physiological severity from staff knowledge in the existing incident projection too. |
| Reception phone currently provides a physical grant for outgoing wake calls; complaint ringing is a cosmetic 1.6-second cue. | Incoming requests have no answer/missed-call lifecycle. | Drive incoming ringing and disclosure from authoritative bounded communication state, reusing the physical phone interaction. |

The foundation worth preserving is already substantial: actual source-specific noise, measured temperature and blanket perception, real radiator settings, bounded deterministic eligibility, physical stock identities, promise deadlines in hotel time, mutable checkout scheduling, finite memory, and atomic snapshot validation. No new quest generator, thermal model, inventory, or generic task framework is needed.

## Minimal behavior delta

### One causal episode and one communication history

Use a small explicit response chain: **Notice → SelfResponse or Tolerate → Contacting → Communicated**. A notice may recover and end before contacting anybody. Keep the existing service outcome (`Acknowledged`, `InProgress`, `Fulfilled`, etc.) distinct from communication state: acknowledging a known issue neither changes its cause nor implies successful help.

Prefer extending the existing persistent case/guest-response model with a current phase, channel, current-cause dwell, and bounded contact-attempt history. Do not create a new card or identity for each phase. Preserve guest/source identity across room moves, and stop or reevaluate an episode when the source disappears, the guest leaves the room, or the need genuinely recovers. Keep lifetime `GuestNeeds` exposure and review history intact.

The existing soft-service budget counts created cases. If private notices are stored as cases, change budget accounting to charge **one first contact attempt**, not every notice, every tick, or a retry. Self-resolved notices must not exhaust the day's contact allowance. Maintain the current production limits (three contacts per shift, two per guest), deterministic eligibility, one active low-priority service per guest, and kind/source dedupe. A single retry belongs to the same charged case. Private records also need a hard bound; six guests times five existing service kinds fits the current 32-case snapshot cap. Do not silently make the soft-service budget suppress existing serious environmental escalation.

### Real and limited self-help

A mildly cold guest can attempt a radiator adjustment only in their own room, at its actual radiator interaction location. Reuse `RoomState.RadiatorSetting`, `RoomSystem` heat multipliers, and `ExtraBoilerDemand`/`RefreshGuestLoad`. Do not impersonate staff actor 0; factor the existing validated mutation so a guest action can use it after its own room/presence/staging checks. The resulting setting and heat-demand change must match a player's physical adjustment.

Make the attempt bounded per causal episode and wait for thermal response before considering contact. If the radiator is already high or the room does not warm, tolerate for a configured period and then use a real channel. Avoid repeated valve oscillation or fighting a later staff adjustment. Guests never repair the boiler, breaker, or lamp.

Minor noise uses sustained actual received noise from a retained real source, with a tolerance phase. Do not replace this with ambient noise profiles or an arbitrary timer. A brief quiet interval should be handled by a defined recovery/reset rule, not lifetime accumulated exposure. The existing lamp has no supported alternate-light interaction; do not fake the optional lamp self-help example unless such a real light is separately implemented.

### Plausible request contexts

| Existing kind | Context to preserve/refine |
| --- | --- |
| Mild cold / internal `ExtraBlanket` | Sustained current discomfort while physically in the room, a real self-help attempt where possible, then tolerance and contact if the condition persists. Player wording describes cold rather than requiring a blanket. |
| `AskNeighborsQuiet` | Sustained actual source exposure while present. Retain the source internally; guest wording need not identify an exact room they cannot know. |
| `WakeUpCall` | Check-in or a quiet pre-sleep moment aligned with the guest's existing sleep/checkout schedule. No creation/contact during shower, transit, sleep, or an unrelated disruptive activity. Accepted wake promise semantics and hotel-time grace remain unchanged. |
| `LateCheckout` | Check-in, a quiet evening moment, or a plausible pre-checkout opportunity. Preserve the actual schedule extension and resulting room-turnover pressure; do not replace it with a reward-only resolution. |
| `LuggageStorage` | Existing arrival state already has a useful cause: the guest is waiting at reception while their room is occupied, dirty, departing, or being prepared. Communicate at that desk encounter. Do not generate luggage requests for a ready room. A checkout-luggage extension would require an actual modeled reason and is unnecessary for this correction. |

Context is a gate over existing seeded activities and real conditions, not a scripted sequence guaranteeing requests. Some guests and entire calm intervals should produce no contact. Do not tune tests to force a daily minimum.

## Channels and player knowledge

Incoming phone calls need a configured ring deadline and at most one delayed retry, all in hotel elapsed time. A generic ringing/missed-call cue may be public; the issue, source, proposed response, and full case card remain private until staff answer at the physical phone. Reuse the current host-authoritative physical phone grant and actor/distance checks, retaining outgoing wake-call support.

A guest contacting the desk must actually traverse an authored room-to-reception route, wait there, and interact before disclosing details. Reuse existing route/door primitives. Do not reuse `GuestAway`: it represents an exterior visit and would give the wrong destination, occupancy meaning, and return behavior. Desk visits retain room ownership and must not release or dirty the room.

An actual staff knock/room conversation can disclose an existing noticed issue early. Player proactive actions remain possible from visible conditions before any contact. Only real conversation changes hotel knowledge; a debug command, diagnostic panel, replicated snapshot, or proximity by itself must not silently communicate the case.

Apply one shared `known to hotel` predicate consistently to HUD, case board, contextual details, response commands, normal guest labels, notifications, and wait interruption. Debug tools may inspect every phase. `Notice`, self-help, and hidden recovery should not issue full notifications. Ringing or a waiting visitor may interrupt WAIT with a generic contact cue. Answering publishes details once; a known case's later resolution may notify once.

Serious incidents require the same boundary: reaching a dissatisfaction threshold is evidence of distress, not proof of a completed conversation. Keep severity/escalation calculations independent, and set `HasContactedStaff` through a real communication path. Existing services-disabled legacy fixtures can retain their established direct projection if the integration remains opt-in, but the production path must not bypass the boundary.

## Resolution, memory, and physical items

Environmental resolution follows measured conditions. Delivering a real blanket changes perception and leaves the case open until recovery has held long enough. An acknowledgement or promise alone earns no successful-help credit. A source stopping naturally can end the problem without claiming the staff fixed it. Temporary absence must not count as successful staff recovery.

Keep the existing concrete completion rules where the action itself fulfills the request: accepted late checkout changes the actual schedule; luggage storage consumes the correct held suitcase at the physical zone; a wake promise completes only on the permitted real call in its time window. Preserve grace expiry, once-only promise outcomes, and review accounting at natural checkout and settlement.

Preserve stock counts, stable item IDs, ownership, delivered blanket visuals, room relocation remapping, day-boundary refill, and prepared held/dropped supplies. Communication must not spawn items or fulfill physical requests. Record service-request memory once actual details are communicated; use bounded attempt state for the contact budget. Hidden self-resolved notices should not inflate complaint, requested-service, successful-help, or ignored-problem counters.

## Snapshot and authority boundary

`ServiceSnapshots.cs` currently captures service cases, promises, items, recovery time, and refill generation, validates them before restore, and reconstructs mirror state without events. Extend that schema rather than adding client-side service logic. Exact field names remain an implementation decision; the minimal required data is:

- Response phase, communication state/channel, severity, and current-episode exposure.
- A bounded self-help attempt/outcome and relevant phase timestamp.
- First contact/last attempt, ring or desk deadline, retry count/deadline, communicated timestamp, and acknowledged timestamp (sentinels validated explicitly).
- Once-only budget/contact accounting, plus any authoritative short desk-trip purpose needed for physical presentation.

Keep identity links to the existing guest, room, incident source, and promise. Validate enum ranges, finite nonnegative timers, sentinel rules, timing order, maximum attempts, unique active guest/call ownership, valid referenced entities, and compatible terminal states. Preserve atomic rejection and guest identity on restore. Mirrors can display replicated private debug data but must neither advance phases nor mutate stock, promises, radiator settings, or player knowledge. Coordinate model/protocol version changes with root; do not change release version constants speculatively.

## Focused verification planned after the investigation gate

- Model: cold does not instantly contact; same-episode dwell resets correctly; real radiator changes room setting and demand; a brief condition ends privately; persistent failure produces one bounded contact path.
- Context: wake/late cannot originate during a shower; ready-room arrivals do not request luggage; calm seeded runs keep budgets and can produce zero requests.
- Resolution: blanket delivery with residual cold stays unresolved; measured recovery closes once; acknowledgement/natural absence does not manufacture staff-success memory; promise and checkout behavior is preserved.
- Information: before/after communication predicates agree across normal UI and commands; hidden serious distress cannot bypass them; retries do not duplicate cards or memory.
- Snapshot: JSON roundtrip in notice, self-help, ringing, desk travel, communicated and resolved states; malformed timers/ownership/unknown sources rejected atomically; mirror mutation blocked.
- Physical PlayMode and actual EXE: radiator approach, phone answer, desk round trip, item delivery, and subsequent ordinary guest navigation use real bodies/routes. Label any calendar/setup adapters explicitly; they are not evidence of physical interaction.
- Actual SOLO and LAN multi-day observation after implementation: sparse contacts, varied plausible channels, preserved stock/turnover, and no notification bursts or invented task quota.

## Pause investigation boundary

Source inspection shows that `LocalCoopBootstrap` sets the SOLO pause state on lost focus; `GameSession` returns before advancing hotel time while paused; guest presentation also checks pause; promises use hotel time. LAN transport uses real/unscaled scheduling and has different focus semantics. These are facts about control flow, **not evidence of the reported crash's cause**.

This note proposes no pause, clock, transport, queue, catch-up, or physics change. It does not claim a reproduction, memory measurement, root cause, fix, or successful ten-minute resume. Those claims require root's captured baseline/reproduction evidence and subsequent targeted verification before the natural-service implementation proceeds.
