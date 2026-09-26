# Prototype 0.3.2 — proposed API and data contract

Implementation contract, 2026-09-26. Originally drafted before implementation; the user subsequently explicitly deferred the hang investigation and authorized the remaining natural-service work. These fields and APIs now guide the integrated implementation. Verification results are recorded separately; this contract does not establish a hang fix.

## One response record; existing outcomes remain authoritative

Keep `HotelIncident` responsible for measured cause, dissatisfaction, escalation, recovery, and episodes. Keep `ServiceCase` responsible for the existing five service agreements and their outcomes. Introduce one small **shared response record** inside the existing `GuestServiceSystem` for noticing, self-help, and communication. Do not create separate notice, phone, desk, and complaint lifecycle managers or invent a sixth service kind for every serious complaint.

Environmental responses can begin from an existing `Observed` incident without creating a service agreement. When a mild request becomes appropriate, the new `ServiceCase` links to that same response. If it becomes serious first, the existing incident uses the same response without needing a soft-service case. Wake/late/luggage responses link to their existing case alone. This prevents a phone call for a blanket and a second simultaneous call for the same temperature episode.

```csharp
public enum GuestResponsePhase {
    Noticed = 0, SelfResponding = 1, Tolerating = 2,
    WaitingToContact = 3, Contacting = 4, Communicated = 5, Cancelled = 6
}
public enum GuestContactChannel {
    None = 0, Phone = 1, Reception = 2, RoomConversation = 3
}
public enum GuestResponseAnchor {
    Radiator = 0, RoomPhone = 1, Reception = 2, AssignedRoom = 3
}
public enum ServiceCommunicationState {
    Uncommunicated = 0, ContactingHotel = 1, Communicated = 2
}
```

`GuestResponse` is a model data class with public getters and internal mutation:

| Field | Type / initial value | Meaning |
| --- | --- | --- |
| `Id`, `GuestId`, `SourceEntityId` | `string` | Stable canonical identity and actual source. |
| `RoomId` | `int` | Current assigned room; moving the guest updates destination, not historical source. |
| `IncidentId` | optional `string` | Existing causal incident, never inferred from a room number alone. |
| `IncidentEpisode` | `int`, 0 only with no incident | Exact episode of that incident. |
| `ServiceCaseId` | optional `string` | Existing/new soft agreement using this response. At least one target must exist. |
| `Phase`, `Channel` | enums above | The single response journey. `Communicated` does not mean the issue is resolved. |
| `CreatedAt`, `PhaseStartedAt` | nonnegative hotel seconds | Notice and phase timing. |
| `DwellSeconds` | `float`, 0 | Current causal eligibility dwell; reset by measured recovery/context loss, never copied from lifetime guest needs. |
| `SelfResponseAttempted`, `SelfResponseApplied` | `bool`, false | One bounded radiator attempt and whether it actually changed the valve. |
| `SelfResponseAt` | `float`, -1 | Actual self-help mutation time, not the intention to walk there. |
| `ContactAttempts` | `int`, 0 | Actual ringing/desk-arrival attempts; at most two. |
| `AttemptStartedAt`, `AttemptDeadline`, `RetryAt` | `float`, -1 | Active attempt and at most one delayed retry; all hotel time. |
| `CommunicatedAt`, `AcknowledgedAt`, `StaffActionAt` | `float`, -1 | Disclosure, acknowledgement, and concrete relevant staff action; these are distinct. |
| `ActionVersion` | `int`, 0 | Increment when assigning a new physical response destination; stale callbacks cannot complete a later retry or a different room. |

Only `GuestServiceSystem` advances this record. `ServiceCommunicationState` is **derived**, not another mutable state machine: `CommunicatedAt >= 0` wins, otherwise `Phase == Contacting` means contacting, otherwise uncommunicated. Ringing is also derived from phone channel, contacting phase, an actual attempt start, and its deadline. A guest travelling to their room phone has not started ringing yet.

Expose these readers:

```csharp
GuestServiceSystem.Responses : IReadOnlyList<GuestResponse>
GuestServiceSystem.FindResponse(string responseId) : GuestResponse
GuestServiceSystem.IncomingCall : GuestResponse // null or exactly one ringing call
ServiceCase.Response : GuestResponse           // bound reference, serialized by ID
ServiceCase.IsKnownToHotel : bool              // Response.CommunicatedAt >= 0
ServiceCase.CommunicationState : ServiceCommunicationState
HotelIncident.Response : GuestResponse         // current episode only, optional in legacy mode
```

Add only case outcome metadata still missing from the existing class: `BudgetCharged` (false), `ResolutionAt` (-1), and `ResolutionReason` (optional string). Severity is read from the linked incident for environmental cases; schedule services have no invented physical severity. Existing case ID, kind, source, due time, status, recovery time, and active predicate remain intact.

### Identity and finite retention

- An environmental response ID is `incident.Id + "/contact/" + incident.EpisodeCount`. The incident ID already contains reason, guest, and actual source. Temperature's source is the existing `room/<id>/temperature`, not an inferred boiler identity.
- A schedule/readiness-only response ID is `case.Id + "/contact"`.
- Linking an environmental service later sets `ServiceCaseId`; it does not rename the response or restart its timers/attempts.
- A reopened incident gets a new response for its new episode. Its old response is retained only while a retained case references it. Otherwise remove it. All current incident references and retained case references must resolve exactly.
- Keep at most one nonterminal physical/contact action per guest and one global ringing phone call. Other eligible responses wait in the finite existing collection; no separately growing queue is required.
- Bound responses by the existing validated incident cap plus case cap: at most 256 + 32 = 288. This is a defensive serialization bound, not a request target. Production still has only six guests and the existing small service budgets.

## Incident contact bridge and once-only memory

When natural communication is enabled, automatic `Observed → Complaint` still changes the **internal distress stage**, but does not set `HasContactedStaff`, create a public request, or record a spoken complaint. The response system selects a real channel for that existing source/episode.

Add `HotelIncident.ComplaintRecorded` (false, reset with `HasContactedStaff` on each new episode) and an internal method:

```csharp
IncidentSystem.MarkCommunicated(GuestStay guest, string incidentId,
                               int episode, float now) : CommandResult
```

It validates guest, active episode, and the matching response before setting `HasContactedStaff`. A helper publishes the existing `OnIncidentStarted` and `Memory.RecordComplaint` **once**, only when both stage is at least `Complaint` and communication has occurred. Call that helper from both communication and threshold crossing: a mild communicated issue can later become a complaint without another call.

`RequestSystem.OnIncidentStarted`, its public active count/projection, expired-request checks, and normal UI must require current-episode communication in this mode. An old request object sharing the incident ID must not leak the new episode's private state while `HasContactedStaff` has reset. Filter it until republished; canonical history remains on the incident. Preserve legacy behavior when natural communication is disabled.

`RecordIgnored` must no longer infer staff contact solely from `Stage >= Complaint`. Private noticed problems still affect ordinary condition exposure/reviews, but do not manufacture an ignored spoken request. `ProblemsResolvedSuccessfully` requires actual measured recovery, a guest present to experience it, and `StaffActionAt >= 0`; acknowledgement or a naturally ending source alone is insufficient.

A linked soft case becoming serious can move to its existing `Escalated` outcome, but preserves the shared response and communication. Emit one meaningful public transition from the incident route; do not emit a second soft-case toast for the same transition. Hidden recovery/cancellation emits no player event. `Changed` may refresh diagnostic views without calling `SignalEvent`.

## Physical action contract

Preserve all current numeric enum values. Proposed additions:

```csharp
// Append to GuestActivity:
AdjustRadiator = 9, CallReception = 10

// Append to GuestAgentState:
GoingToServiceReception = 13,
WaitingAtServiceReception = 14,
ReturningFromServiceReception = 15

// GuestAgent additions:
public string ResponseActionId { get; internal set; } // null outside a response action
public int ResponseActionVersion { get; internal set; }
```

The appended room activities retain ordinary room presence and use actual anchors. `CallReception` is distinct from the existing personal `PhoneCall` activity and must not create a new loud-noise source. The three desk-trip states are not `GuestAway`; they preserve room/key/booking ownership. `CurrentLocation` is travelling/lobby/travelling respectively. No new arrival, occupancy release, check-in, or linen event is generated.

The shared physical callback is:

```csharp
HotelSimulation.SignalGuestResponseAnchorReached(
    string guestId, string responseId, int actionVersion,
    GuestResponseAnchor anchor) : CommandResult
```

This is an authoritative scene-to-model acknowledgement, like existing arrival callbacks. Validate running host model, guest identity, selected response/action version, assigned room ownership, and expected activity/state/anchor. Outbound self-help/contact also rechecks the current cause before mutation. Returning to the owned room must still complete if the response was cancelled or its source recovered while the guest was at reception; otherwise cancellation would strand the guest outside. Repeated or stale callbacks fail without events or side effects. No `dt` passage automatically invokes it in a physical scene. Pure model tests may explicitly invoke this adapter and must label it as such.

| Anchor | Model action after validated physical arrival |
| --- | --- |
| `Radiator` | Recheck mild cold and own valve; mark attempted; increase the real setting by one within 0–3 only if appropriate; refresh existing heating load. Record whether it changed, enter tolerance, resume the existing schedule without consuming a scheduled activity entry. |
| `RoomPhone` | Recheck plausible context/cause; start one ring attempt only if the global phone is free; set hotel-time deadline and charge the contact budget once. |
| `Reception` | Change `GoingToServiceReception` to `WaitingAtServiceReception`, start bounded waiting, issue only a generic visitor cue. Actual details remain hidden until interaction. A guest already waiting for initial check-in may use that real reception arrival without walking out and back. |
| `AssignedRoom` | Complete the service return only for that trip; clear response action and resume quiet rest/existing schedule. Never re-run check-in or release the room. |

Self-help uses a shared internal radiator mutation factored from `SetRadiatorSetting`; it never calls the public staff API with invented actor 0. A staff valve change can stamp `StaffActionAt` on matching active temperature responses; a guest's own adjustment cannot. Apply the same stamp only from concrete relevant outcomes such as actual blanket delivery, accepted source quieting, successful infrastructure restoration, or an actual completed room move. Compensation and acknowledgement never stamp it.

Critical route edge case: **a service desk trip must not resolve its linked room incident merely because the guest is outside the room for eight seconds**. Pause that episode's exposure accumulation and recovery while the guest makes this trip; retain its last observed cause for the conversation. Resume measurement on physical room return. This does not invent exposure outside the room or credit staff. Checkout still ends the stay normally. Do not reuse `PausedForTransfer`, whose meaning is an actual room relocation.

A response action suspends the scheduled activity without advancing `ActivityIndex`. Existing checkout priority still wins. On completion, resume a short quiet stage and the existing schedule; do not rebuild the schedule or extend checkout except through the existing late-checkout agreement. Room relocation invalidates `ActionVersion` and changes the destination so a callback for the old room cannot turn the new room's valve.

## Staff commands and trust boundary

Keep existing respond/acknowledge/delivery/wake APIs, adding a known-case guard to response decisions. Proposed new simulation wrappers:

```csharp
HotelSimulation.AnswerIncomingServiceCall(int actorId, string responseId)
HotelSimulation.TalkToServiceGuest(int actorId, string guestId, string responseId)
HotelSimulation.DiscussRoomConcern(int actorId, string guestId, string responseId)
```

All return `CommandResult` and delegate to one internal `GuestServiceSystem.Communicate(actor, responseId, channel)`. Validate actor 0–1, active session, matching current response/cause, physical guest state appropriate to the channel, and no prior disclosure. Phone requires the actual ringing response; reception requires a physically waiting guest; room conversation requires room presence and permission to converse. Communication sets its timestamp, increments `ServicesRequested` at most once for a linked case, and invokes the incident bridge. It does not accept the agreement automatically.

`GameSession` owns the real focus/distance/phone-or-door/guest interaction grant before calling these simulation wrappers. LAN commands carry response ID (and guest ID where needed); host repeats those checks against authoritative bodies. Client callbacks cannot move guests, turn their valve, or declare an arrival. Existing outgoing wake-call grants remain separate from answering an incoming call.

Proposed diagnostic additions operate on the same records:

```csharp
DebugBeginGuestSelfResponse(string guestId)
DebugBeginGuestContact(string guestId, GuestContactChannel channel)
DebugCancelGuestContact(string responseId)
```

They select an existing real noticed cause/context and initiate the real action, not fake source-free needs, automatic communication, or physical arrival. Existing `DebugForceService` creates an uncommunicated scenario intent in natural mode. Tests wanting a communicated setup explicitly use the model callback/communication adapter, clearly labelled; production debug must not bypass the player's knowledge rules by default.

## Configuration and budget rules

Append opt-in `NaturalCommunicationEnabled = false` to `GuestServiceSettings`/config so existing kernel fixtures remain meaningful; enable the production asset only after integration tests. Reuse `ObservationSeconds` for minimum current-cause dwell. Proposed extra validated tuning: `SelfResponseObserveSeconds`, `ToleranceSeconds`, `PhoneRingSeconds`, `ContactRetryDelaySeconds`, `ReceptionWaitSeconds`, and `MaxContactAttempts` constrained to 1–2. Default seconds require a pacing measurement before final choice; do not disguise guessed tuning as verified balance.

Environmental notice/self-help records do not consume a service-case slot or contact budget. If an eligible mild issue reaches a contact attempt, create/link its single case and set `BudgetCharged` once. Wake/late/luggage intents may exist privately, but only charged contacts count against the existing shift/guest allowance. Check available allowance immediately before starting contact, not just on notice creation. Retries never spend a second slot. Serious existing incidents may communicate even after the soft allowance is used; this does not create a bonus soft-service task.

After an answered phone/desk conversation the guest can resume life while the shared response remains `Communicated`; it does not keep the actor reserved until a wake promise's future deadline. Before communication, one physical response action per guest wins deterministically; no more than one incoming phone call rings. A second response waits without ticking a fake ringing deadline. A missed attempt schedules at most one retry if the original cause/context and remaining stay still allow it.

## Snapshot extension

Serialize each response exactly once in `ServiceLayerSnapshot.Responses : GuestResponseSnapshot[]`; `ServiceCaseSnapshot.ResponseId` and `HotelIncidentSnapshot.ResponseId` are links. Snapshot all scalar fields in the response table above. Add case `BudgetCharged`, `ResolutionAt`, `ResolutionReason`; incident `ComplaintRecorded`; agent `ResponseActionId` and `ResponseActionVersion`. This avoids duplicating timers in nested incident/case DTOs.

Validation precedes all mutation: canonical response ID, valid guest/current destination, linked real source and episode, at least one valid target, bidirectional case/current-incident links, unique response IDs, cap 288, finite nonnegative timing plus exact -1 sentinels, coherent phase/channel/attempt fields, at most two attempts, one ringing call, and one current physical action per guest. Historical responses may reference an older incident episode only while a retained case links them; they cannot be the incident's current response or an active guest action. `CommunicatedAt >= 0` must agree with known case state and current incident `HasContactedStaff`. Promise/stock validation remains unchanged.

Restore DTOs first, then relink existing guest/incident/case identities and response references without events. Mirrors never advance or acknowledge response state. Root owns model/protocol version migration; no speculative version edit is part of this note.

## Suggested file ownership after root opens the implementation gate

| Owner | Files / responsibility |
| --- | --- |
| Model agent (`agency_situations`) | New `Simulation/GuestResponse.cs`, `GuestServiceSystem.Responses.cs`, `GuestServiceSystem.Communication.cs`, model helpers in a new `HotelSimulation.GuestResponses.cs`; existing `GuestServiceData/Settings`, service commands/items/update paths; `IncidentSystem` and `RequestSystem` bridge; focused model tests. No Unity or player launch. |
| Physical life agent (`agency_life`) | `GuestAgent.cs` appended state/activity fields; `GuestPresentation` response routing/staging, `AuthoredGuestRoute`, actual radiator anchor and minimal gesture/room-phone staging; physical phone cue; authoring scene markers through existing builders; route and physical PlayMode tests. Coordinate the exact new model callback before edits. |
| UI agent (`agency_interface`) | HUD/board/context/labels/debug, one shared visibility predicate, problem wording, incoming answer UI plus outgoing wake compatibility, reception guest interaction display; controller UI tests. No mutation through UI-only state. |
| Root | `HotelSimulation.cs` composition/tick hooks and existing `.Guests/.Presence/.Infrastructure` shared hook points; `GameSession` physical grants and wrappers; LAN commands/snapshots/validation/protocol; runtime drivers; production config integration; all compile/test/EXE runs. |

Avoid concurrent edits to the same partial. Model helpers can be added in new partials; root inserts the few lifecycle calls in its owned files. Life agent owns `GuestAgent` enum changes; model agent consumes that agreed contract. Root remains owner of all snapshot files even when an agent supplies new snapshot tests.

## Verification changes and what each test proves

- New `NaturalGuestResponseTests` (EditMode): same-source dwell, self-help only after explicit anchor acknowledgement, actual valve/demand change, recovery without contact, bounded retries, shared service/incident identity, no automatic `HasContactedStaff`, once-only memory, wrong/stale version rejection, service-trip absence does not resolve the source, and known-case command guards. These prove model behavior, not scene travel.
- Existing `GuestServiceTests`: stop assuming `DebugForceService` means public communication; add explicit labelled setup for promise/stock tests. Replace immediate blanket-fulfillment assertion with sustained perceived recovery, preserving actual item/thermometer/power assertions. Preserve all checkout/stock/grace tests.
- `GuestServicePacingTests`: count actual charged/communicated contacts, not private records. Keep deterministic upper budgets, varied justified causes and guests without requests; remove the old universal `1..3` minimum where self-help now legitimately yields zero. Log actual seeded outcomes rather than force schedules to obtain a quota.
- Existing pure incident/needs/compensation tests retain communication-disabled compatibility where that is their subject. Add enabled-mode tests for the production integration instead of weakening their causal/dissatisfaction assertions.
- `LanServiceSnapshotTests` plus new response snapshot cases: each phase roundtrips; invalid links/episodes/times/multiple ringing calls/stale action ownership fail atomically; references stay attached to the same mirror guest; read-only commands remain blocked. Accepted promises, carried/delivered items, room moves and day refill remain covered.
- `GuestRouteTests` covers authored path geometry. New physical PlayMode tests must demonstrate radiator arrival, room-phone anchor before ringing, a complete real reception trip and return with door/queue/luggage colliders enabled, and unchanged ownership/checkout. No fabricated arrival callback is permitted inside those proofs.
- UI PlayMode tests exercise actual controller answer/desk/room conversation, hidden versus known cards, and preserved outgoing wake cancellation/completion. Existing UI fixtures may use labelled model guest setup, but the tested new phone/desk disclosure itself must be physical.
- Root's actual EXE/LAN scenarios need a communicated setup adapter only for unrelated existing stock fixtures; new acceptance scenarios must observe real self-help/contact routes and no source-free debug forcing. Pause/crash tests remain separate evidence, not inferred from these service tests.
