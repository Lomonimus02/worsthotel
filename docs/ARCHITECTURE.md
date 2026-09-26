# Architecture — Prototype 0.3.2 Natural Service

## Authority and composition

`GameSession` composes a pure C# `HotelSimulation`, planning, the three-day phase loop and scene presentation. ScriptableObject assets provide immutable settings; runtime state belongs to the simulation. SOLO creates one staff object, camera and input assignment. LAN uses one host and one client: NGO messages carry client input and commands; only the host runs hotel decisions and rigidbody physics. The replica validates and restores model snapshots without ticking, emitting gameplay callbacks or granting local authority. World frames carry guest poses, doors, props and staff views. The development-only split-screen mode retains two local staff rigs.

`SimulationClock` drives fixed hotel ticks. WAIT accelerates hotel time after the required local/remote votes; physical input and rigidbody time remain real time. Significant staff contacts, infrastructure warnings and player responses stop WAIT. Ordinary room activities and temporary outings do not emit notification events.

## Life and perception

`GuestScheduleSystem` produces seeded, trait-weighted plans. `GuestAgent` separates location, activity and physical staging. Unpack, Work, WatchTV, PhoneCall, Shower, QuietRest, LoudRoom, LeaveHotel and Pack use simple activity rules; sleep and check-in/out retain lifecycle states. Business guests favor desk work, calls and earlier sleep; budget travelers favor outings and TV; noisy traits independently bias loud activity; cold-sensitive showers last longer.

`GuestPresentation` follows authored routes through real room doors and acknowledges arrival at the appropriate anchor. Bed transitions are horizontal, showers conceal the body, desk work uses a desk, and phone calls show a phone. Audio/output begins only after staging. Temporary outings traverse the lobby and exterior exit; the room and key remain assigned while the body is hidden outside. Return traverses the same entrance and room door. Away guests checking out do not resurrect a body or retain a departure lock.

`GuestNeedEvaluator` writes location-based `GuestPerception` and lifetime need totals. Only actual presence in the assigned room samples room temperature, external sources, dirty linen and loss of power. Lobby/travel/away states do not inherit the room's comfort. Service waiting remains a receipt/quality concern, not a fourth guest situation family.

## Causal situations and memory

`INoiseSource`, implemented by factual `RoomNoiseSource` measurements, identifies guest, room and category. TV needs electricity; phone and shower retain their own activity output. `NoiseSystem` applies one direct shared-wall/corridor transmission; received sound never retransmits. Decorative ambient levels and explicit developer noise overrides cannot invent complaint sources.

`IncidentSystem` owns three situation families: Noise, Temperature and RoomCondition. The key is guest + family + source entity. One case persists through Observed, Complaint, Escalated, Critical and Resolved; a later episode reuses the identity. A source may disappear without erasing its bounded causal history. Complaint requires sustained exposure AND dissatisfaction. Measured recovery uses hysteresis; relocation itself is not a completion command. Unsupported abstract repair percentages do not produce condition complaints.

`RequestSystem` projects staff-contact cases for existing UI/receipt logic. Compensation reserves one credit, eases dissatisfaction and grants finite grace; it never resolves or silences the cause. Source warnings change actual TV/phone output. Normal guests generally comply for the rest of the stay; noisy guests can resume, with shorter agreements after repeated warnings. Moving either guest uses destination reservation, the corresponding physical key and a real route.

`GuestMemory` stores complaint/category counts, reserved compensation, successful recovery, ignored problems and source warnings. Repeated categories reduce configured patience. Unresolved complaints are recorded before departure/settlement; background Observed discomfort is excluded. Reviews describe recorded results without another duplicate score penalty.

## Interaction and presentation

Occupied doors first knock; an answered second interaction opens compact `ManagementUI.Context` choices. Body interaction opens the same context, except key handoff retains one-press priority. `GameSession.Conversations` grants short-lived conversation authority from a real host-side raycast, matching actor, guest, room, proximity and current model. A client opening UI cannot create a grant. Quiet and entry commands are revalidated by the host; leaving or closing the conversation revokes the grant.

Normal HUD cards, guest details, context actions and the NOW/UPCOMING reception board share a communication boundary: an undisclosed response cannot reveal the guest's exact concern. A generic ringing/waiting cue is available before conversation. After disclosure, the surfaces describe the experienced problem, not a prescribed delivery. F2 deliberately exposes private phases, measured causes, perception, memory and bounded history. Context actions use the established keyboard/gamepad UI and input blocking.

## Guest services

`GuestServiceSystem` owns persistent service cases, wake-up promises and physical supply identities. Cause checks use guest presence, actual discomfort/source measurements, room readiness or the existing departure schedule. Stable per-stay eligibility, configurable trait/Solo multipliers, one active case per guest, per-kind deduplication and stay/day budgets prevent random request churn. Serious incidents retain their independent system.

With `NaturalCommunicationEnabled`, `GuestResponse` links the same factual source and incident episode to its optional soft case. It records noticing, self-response, tolerance, contact attempts, disclosure, acknowledgement and actual staff action separately. `Noticed → SelfResponding/Tolerating → WaitingToContact → Contacting → Communicated` is not a mandatory task sequence: recovery, checkout or lost context can cancel it. Observed discomfort remains private. A repeat call reuses the response; it does not create a second case or fulfillment reward. Low-service allowance is charged when a real contact attempt starts, with serious causal complaints handled independently of that allowance.

The supported self-help is one useful increase of the guest's own radiator during an actual mild-cold episode. `AdjustRadiator` and `CallReception` are staged physical activities, appended as enum values 9 and 10. `GuestPresentation` navigates around furniture to the actual valve or room telephone and reports `SignalGuestResponseAnchorReached` with guest, response ID and action version. The model validates identity, room ownership, state and live cause before applying the shared radiator mutation or starting the call. A stale route cannot apply another response. The valve changes local heat and boiler demand; it is not a staff-action credit. Guests do not repair infrastructure.

Desk visits use states 13–15 (`GoingToServiceReception`, `WaitingAtServiceReception`, `ReturningFromServiceReception`). They traverse the actual room door and lobby, retain room/key ownership, and return after conversation, cancellation or bounded waiting. They do not use `GuestAway`; linked incident recovery does not advance merely because the guest left the room to report it. Existing checkout/departure handling supersedes an unfinished contact. Schedule progression is suspended while the response action owns the guest; the original next planned activity and checkout remain available afterwards.

`IncomingServicePhoneCue` reads the authoritative ringing response, plays a bounded local telephone cue and pauses with the hotel. Walking toward the phone is not ringing. The old incident-publication bell is suppressed in natural mode. A host-validated physical phone grant permits answering; reception and room-door conversations likewise require the established actual interaction scope. Answer/disclosure, acknowledgement, agreement and outcome are separate commands. Configurable ring duration, retry delay and at most two attempts bound missed calls. The room-call gesture is not a personal-call noise source.

`ServiceSupplyItem` binds authored blanket, bulb and guest suitcase bodies to model ownership. Normal pickup/carry/drop and target interactions require the actual carrier, reachable focused target and valid stock. Delivered blankets change personal perceived cold comfort without changing room heat or power. In natural mode delivery records the staff action but does not immediately fulfill the cold case: measured severity must remain below the existing recovery threshold for the recovery interval. Passive recovery can close a case without a staff reward. Luggage needs a waiting guest's agreement and actual placement near reception. Day refill preserves held/dropped prepared supplies and cannot repeat within the same day.

Late checkout changes the existing single-night departure schedule, bounded by the current service end. Room/key ownership and the ordinary physical departure/linen lock remain in force. Wake-up acceptance stores a due time in hotel seconds. Only a scoped grant from the physical reception phone permits completion; closing/disconnecting revokes it. A late grace window precedes one missed outcome. Guest departure and shift settlement resolve outstanding promises before modest bounded service satisfaction and review memory are recorded. Moving a guest synchronizes case/promise destinations and their delivered blanket.

Radiator settings 0–3 affect local central heating and total boiler demand. A degraded bedside bulb is a concrete room fault: its presentation is dark, and repair consumes a carried replacement bulb. A healthy lamp on a tripped circuit requires restored power instead. `ServiceSnapshots` and `GuestResponseSnapshots` validate and mirror services, response/cause identities, action versions, communication times, promises, supplies, comfort, room settings and memory. Scene replication includes delivered blankets, lamp surfaces/lights, valve knobs and guest phone poses. LAN protocol 6 uses the existing bounded gzip world envelope; decoded model schema is version 5. Exact decompressed length, checksum and ordinary snapshot validation precede atomic application. Client mirrors never generate responses, start autonomous routes or complete services themselves.

## Infrastructure and physical work

`BoilerSystem` retains condition, load, pressure, failure and repair ordering. SOLO has a configured temporary valve catch; co-op requires a second actor's real held support. Restart restores operation without removing wear. Paid maintenance acts between days.

`HeaterSystem` delivers heat only when the released device is wholly inside a room, switched on and powered. `ElectricalSystem` separates requested and delivered load on A (101–103) and B (104–106). Tripping removes lights/TV/heater output; unchanged demand causes another trip after reset. Heater switches turn off at settlement; position and circuit state persist.

`HousekeepingSystem` records dirty/clean linen identity and finite stock. Owners physically remove dirty linen, deposit it, take clean linen and perform a short bed action. No automatic employee is present. Occupancy, reservation and departing-body identity remain distinct; turnover cannot begin until the body actually vacates.

`PlayerInteractor` validates nearest focus, reach and actor identity. `PhysicsGrabDriver` uses a damped joint and bounded hand target; dropping, disconnection and reset restore body settings and clear ownership. NewGame replaces model state and rebinds presentation, clearing old guest bodies, conversations, history, tools and effects.

## Verification

`tools/Unity.ps1` regenerates the scene, runs EditMode/PlayMode tests and builds Windows. Causal model tests cover source identity, deduplication, recovery, compensation, relocation, away perception, repeat patience and memory. Snapshot tests exercise JSON null normalization, deep copies, atomic rejection and read-only replicas.

The opt-in development player uses owned synthetic devices only. Agency fixtures explicitly label forced activity/temperature and model key adapters, while real presentation supplies every guest route callback. A separately reset three-day tour uses natural schedules and labelled model key/linen work. Two EXE LAN runs use actual transport and remote input; stage files coordinate assertions without applying gameplay state. Current results and limits belong in `VERIFICATION.md`, not inferred from the existence of test code.

Natural-service tests cover private/communicated boundaries, bounded retries, real radiator/room-phone approaches and desk round trips, cancellation, measured blanket recovery and replica validation. Completed 0.3.2 model, physical interaction, SOLO and two-process localhost LAN results are recorded in [VERIFICATION.md](VERIFICATION.md), including repaired test failures and exact binary provenance. Existing version results remain historical evidence, not automatic proof for these changes.

The reported Alt+Tab hang investigation is deferred at the user's explicit request. The opt-in observer and forensic tools preserve evidence and do not constitute a pause/clock/network fix. No root cause or successful long-pause stability result is claimed; see [PAUSE_CRASH_INVESTIGATION.md](PAUSE_CRASH_INVESTIGATION.md).
