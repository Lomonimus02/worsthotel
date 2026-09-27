# 0.4.1 interface, sales policy and staff-sleep baseline

Read-only source audit, 2026-09-27, checkout `d7ce3fc`. The complete [0.4.1 request](../RHYTHM041_REQUEST.txt) was read. This note proposes integration boundaries; it implements no runtime behavior and is not compile, gameplay or LAN execution evidence. Root owns those phase-0 checks. Preserve the strict sequence: investigation → pre-failure model → maintenance → consequences → booking policy (phase 5) → guest rhythm → staff sleep (phase 7) → readability → multi-day tuning.

The user request §58 describes the old pause crash as fixed. Existing release evidence still defers the native Alt+Tab hang; this audit makes no crash-fix claim. Do not change the user's dirty pause-watch script or pause diagnostics for this milestone's baseline.

## Booking facts in the current source

| Current behavior | Source |
|---|---|
| Eight deterministic candidate identities per arrival date, spanning three existing guest types; generation does not consider price or sales policy | `Scripts/Simulation/GuestSystem.cs:12` |
| Today's and tomorrow's enquiries are generated; accepted reservations materialize at absolute arrival time | `Scripts/Simulation/HotelSimulation.Bookings.cs:153` |
| Each accepted ordinary enquiry requires `AcceptBooking(actor, offerId, roomId, price)`; acceptance immediately reserves a concrete room interval and agreed price | `Scripts/Simulation/HotelSimulation.Bookings.cs:96` |
| Future cancellation and repricing are revision-guarded; there is no pre-arrival reassignment command | `Scripts/Simulation/HotelSimulation.Bookings.cs:113` |
| Availability checks protect overlapping reservations, existing occupants and pending moves; dirty rooms may be sold with a preparation warning | `Scripts/Simulation/HotelSimulation.Bookings.cs:64` |
| `HotelReservation.RoomId` is a required integer, not an optional assignment. Arrival/check-in, physical keys, turnover and snapshot validation rely on it | `Scripts/Simulation/ContinuousBookings.cs:27`, `Scripts/Simulation/OperationsSnapshots.cs:23` |
| The ordinary UI is an enquiry-by-enquiry approval loop. Room choices are enabled only before acceptance; accepted reservations expose price edit/cancel | `Scripts/UI/ManagementUI.Operations.cs:75` |
| Forecasting an accepted reservation in a different candidate room is explicitly refused | `Scripts/Simulation/HotelSimulation.BookingForecast.cs:20` |

All paths above are relative to `Assets/_WorstHotel/`. The room grid currently has no sale-open state or future rate policy. Current room ownership must remain separate from a new sale-open flag: a closed sales channel is not an empty, clean or unoccupied room.

## Minimal booking integration proposed for phase 5

Keep concrete provisional room assignments at automatic reservation creation, then let staff revise the future assignment. §42 permits choosing before arrival; optional/unassigned room identities are not required. This preserves stable reservation/guest IDs, physical room keys, one-night intervals and existing guest arrival routes.

Suggested model contract, names subject to root's phase contract:

- A host-owned `BookingPolicy` exposes per-room `OpenForSale`, future rate and revision, plus a bounded demand cursor/next decision time. All six room IDs must remain explicit.
- `SetRoomSalesPolicy(actor, roomId, open, price, expectedPolicyRevision)` changes only future sales. Closing a room neither cancels an accepted contract nor evicts its occupant; the UI must say that existing reservations remain.
- Scheduled host model ticks evaluate bounded demand against open rooms, interval availability, configured horizon and rate. Do not call booking creation from UI refresh or instantly book on every open-toggle. Existing deterministic offer IDs can supply candidate identity and reproducible demand decisions; persist the decision cursor so snapshots cannot repeat an enquiry.
- `ReassignReservation(actor, reservationId, roomId, expectedReservationRevision)` validates the same future-interval gate, excluding its own reservation. Preserve price and guest identity. Invalid/stale/double-booking attempts change nothing. Start with pre-arrival reassignment; extending it to an already waiting, un-keyed guest would require additional key/reserved-room synchronization and is not necessary for the requested choice.
- A room closed to **new ordinary sales** may still be deliberately assigned to an existing contract if root adopts that policy; make this distinct from automatic room selection. Decide this semantic once in the model, not separately in the UI.
- Separate policy rate from agreed reservation price. Changing a public rate should not silently rewrite existing agreements. Existing expectation and checkout arithmetic can continue using the agreed price.
- Keep `AcceptBooking` for explicitly isolated historical/model fixtures or future special-booking extension points, but do not retain an ordinary approval button in the production flow. An automatic-mode client must not bypass the policy through the obsolete ordinary command.

UI can reuse `ManagementUI.Operations` and its controller actions populated in `Update`: a concise sales-policy room grid, upcoming accepted schedule, assignment details and the existing reports. Display rooms for sale, booked occupancy by date, readiness separately, contracted revenue versus approximate additional sales, and available load forecast. Current `UnpaidBookedRevenue` is contracted nominal revenue, not a prediction that every offered room sells.

Forecast needs a pure reassignment preview: the selected reserved identity is included once in the proposed room, excluding its current placement. No temporary mutation of the reservation for rendering. A wider policy forecast should distinguish accepted demand from unsold-room potential and retain actual tripped/power state warnings.

## Clock, WAIT, pause and sleep facts

| Current behavior | Source |
|---|---|
| Hotel clock accepts speeds 1/4/8 and does not alter Unity physics time; mirrors cannot advance it | `Scripts/Simulation/HotelGameClock.cs:16` |
| Host `GameSession.Update` budgets fixed model ticks using `Time.deltaTime * Clock.Speed`; it stops the accelerated batch when speed falls to 1 | `Scripts/Core/GameSession.Service.cs:49` |
| WAIT uses one real consent in SOLO, two in co-op, rejects UI/carry/physical interaction/disconnection and stops on staff activity | `Scripts/Core/WaitController.cs:29` |
| WAIT calls `Stop` on **every** `EventRevision` change, and `IsWaiting` means any speed above 1 | `Scripts/Core/WaitController.cs:11`, `:139` |
| Independently of WAIT, `GameSession.Tick` also resets speed to 1 after any event | `Scripts/Core/GameSession.Service.cs:61` |
| Pause sets `Time.timeScale=0`, cancels physical interactions and halts the hotel. Offline focus loss can pause; LAN local focus suppresses intentions without freezing its peer | `Scripts/Core/LocalCoopBootstrap.cs:254` |
| Remote WAIT input already travels in the authenticated ordinary input envelope. Host frames mirror reason, two votes and progress | `Scripts/Networking/LanWorldInput.cs:14`, `Scripts/Core/GameSession.Lan.cs:28` |
| F2 itself pauses, offers developer clock overrides on close, and is disabled while LAN is active | `Scripts/UI/DeveloperPanel.cs:55`, `Scripts/UI/DeveloperPanel.Clock.cs:9` |

**Sleep cannot simply set speed or reuse WAIT votes.** Routine phone calls, bookings, guest transitions and daily reports increment general event revision and would wake it. Both current event-stop paths must be addressed explicitly, while ordinary WAIT retains its current behavior.

## Minimal sleep integration proposed for phase 7

Use the existing clock/tick pipeline and an explicit acceleration owner (`None`, ordinary `Wait`, `Sleep`, developer override). Sleep owns a host-authoritative state with per-actor bed/consent identity, sleeping/awaiting-consent status, absolute wake target and last wake reason. The target should be the next configured morning, not a `NewGame`, `StartDay`, bulk timestamp assignment or unbounded synchronous skip.

Bed use must go through existing real interaction targeting and host actor authentication. A UI-only checkbox cannot impersonate the absent partner. SOLO needs its one active staff member; LAN needs both connected staff. Departure from bed/cancel, peer loss, epoch/new-game replacement and changed participation must revoke consent and stop sleep. Do not silently reduce an existing two-player agreement to one participant. A stale old bed-use/cancel must not operate on a later sleep session.

During sleep, the host runs normal fixed hotel ticks at a supported clock speed. Physics remains normal; production guest navigation continues using its existing clock budget. Both players get an honest sleeping/partner-ready display and a cancel path, with no extra split-screen dependency. Pause remains separate and suspends sleep progress; decide and test whether explicit pause revokes consent or preserves it while paused, rather than letting old WAIT cleanup choose accidentally.

Use a narrow critical-wake predicate at each model tick: real boiler failure, major actual power loss and a defined existing high-severity guest emergency. Keep ordinary blanket requests, minor services, routine activity and report boundaries out of it. Preserve normal request queues/promise deadlines while sleeping; skipping their wake notification must not fulfill or delete them. Do not reveal a private uncommunicated guest cause merely to justify an alarm. Wake must restore speed 1 and discard the remaining accelerated batch in the same tick, so a failure cannot be simulated many extra hotel minutes before staff regain control.

The sleep UI needs to distinguish `partner not ready`, `sleeping until <hotel time>`, `woken by <known critical event>` and `cancelled`. Existing `WaitController.OnGUI` otherwise labels any sleep speed as WAIT/DEBUG, so its display and stop authority need ownership checks too. General event revision should still advance; add a severity/classification query or dedicated critical revision instead of suppressing legitimate model events.

## LAN, schema and debugging impact

Current baseline is model schema **11**, LAN protocol **12**, compatibility `worst-hotel-0.4-discussions12-gzip`. Use subsequent deliberate bumps when new wire fields land; do not claim those versions implement 0.4.1.

Booking policy and deterministic demand progress belong in the validated model snapshot beside operations/reservations. Validate exact room identities, finite time/rate bounds, revisions, cursor ordering, no overlapping allocations, and accepted-price stability before mutation. New policy/reassignment commands must have explicit revision validation in `LanProtocol.ValidCommand`; do not overload `expectedDirectIntentId`. Preserve epoch/sequence authentication and continuous date-boundary tolerance.

Sleep consent is session/actor state: mirror it in the LAN session frame with strict enum, target-time, two-actor and revision validation before applying the model. Clients render the host decision and never drive the hotel clock independently. Ordinary input release/timeouts already help detect stale devices; a sleep agreement additionally needs an explicit session token so disconnect/reconnect cannot reuse it. Include the new bed target in world interaction discovery/host authorization and test it with real remote use.

F2 additions should remain developer-only and authority-bound. Sales controls should call the same model validation; forcing demand must create a bounded scheduled candidate, not silently fill all rooms. “Start sleep” and “advance to morning” should be distinct labelled diagnostic actions. “Critical wake” should use a real causal condition or an explicitly identified test event, not production random disaster creation. F2's current pause-on-open conflicts with observing sleep: close before normal progression and never infer that sleeping advanced while paused.

Time-setting must remain monotonic/bounded or explicitly reset a disposable diagnostic hotel; rewinding the live clock would invalidate accepted intervals, promise deadlines, sleep targets and report-once accounting. New thermal/maintenance/stress histories should be bounded and distinguish actual causes from developer overrides.

## Migration and gate checks

- Historical manual-booking model fixtures should opt out explicitly. Do not globally turn automatic booking off in shared production fixtures. Existing `ContinuousBookingUIPlayModeTests`, forecast/controller tests, paid-capital LAN driver and 4/5/5 SOLO driver currently call manual acceptance; migrate those to policy-controlled scheduled reservations or clearly retain them as historical command regressions.
- Policy model tests: 4-open cap, closed-room exclusion, interval overlap/late checkout, scheduled arrival of reservations (not instant toggle fill), price effect, persistent decision cursor/no duplicate after a snapshot/date change, stale policy/reassignment rejection, pure room-preview forecast.
- Physical/UI tests: controller room-sale and rate changes, automatically appearing upcoming booking, revised future room followed by its actual correct rack-key/check-in route. Preserve contact privacy and actual item ownership.
- Sleep tests: real SOLO bed use, two real LAN bed consents, one partner insufficient, cancel/disconnect/stale intent, stable sleep to morning on the same model/rooms/epoch, true critical failure wakes on that tick, minor request does not wake or disappear, physics remains 1x, ordinary WAIT keeps its event-stop behavior, pause/resume does not catch up wall time.
- Run existing pause/WAIT/calendar tests at their gates. After production booking activation, final multi-day and LAN evidence must label diagnostic adapters accurately and use fresh captures/binary hashes as the 0.4 release did. Source proposals above are not substitutes for those runs or human rhythm assessment.

## Exact network extension points — independent follow-up audit

Source-only follow-up on 2026-09-27 while the baseline runtime remains frozen. Paths below are relative to `Assets/_WorstHotel/Scripts/`. These are implementation requirements for later phases, not claims that maintenance kinds, sales policy or sleep already exist.

### Current boundary and replay behavior

| Layer / exact entry point | Existing invariant and consequence for extensions |
|---|---|
| `Networking/LanProtocol.cs`: `LanCommand`, `ValidCommand` | Protocol 12, exact positive epoch, increasing positive command sequence, known enum, bounded strings/amount/room. Continuous commands may cross midnight (`command.day <= host day`) only in Service; legacy commands require equal day. Old date alone therefore cannot invalidate a continuous command: the target's revision/state must do that. Reservation revision is allowed only on cancel/reprice; Direct identity/revision only on move cancellation/credit/refusal. Add explicit whitelists for new fields; unrelated commands must retain their sentinel values. |
| `Core/GameSession.Lan.cs`: `ForwardLan`, `ExecuteLanCommand`; `Networking/LanSession.cs`: `SubmitCommand`, `ReceiveCommand` | Sender connection chooses actor 1; payload contains no actor ID. Host requires the connected peer, valid envelope and, during Service, `Players[1].IsUIBlocked`. It consumes `lastCommandSequence` **before** business dispatch: a rejected business decision still cannot replay that same envelope. `AcceptedRemoteCommands` counts dispatched envelopes, not successful purchases. UI-block state is supplied through input and is not a physical-presence grant. |
| `Networking/LanSession.cs`: `Connected`, `Disconnected`, `NotifyHostNewGame` | NewGame advances epoch and resets command/model/world sequences and remote UI-open revisions. Reconnection resets remote command sequence/input state; disconnect clears existing guest/phone grants and stops WAIT. New sleep agreements must be explicitly revoked on these paths; neither epoch alone nor a reconnect's new sequence protects an old agreement within the same hotel. |
| `Networking/LanSession.cs`: `ReceiveHotel`; `Core/GameSession.Lan.cs`: `ApplyLanFrame` | Only server-originated newer frames are accepted. Header, model/frame epoch+sequence, phase/day, WAIT arrays and presentation fields are checked first. A fresh epoch builds a provisional mirror; an existing epoch retains hotel/room identity across midnight. `ApplySnapshot` must succeed before session fields, WAIT view or local menu changes. A rejected frame does not consume its applied sequence. |
| `Simulation/HotelSimulation.Snapshots.cs`: `ApplySnapshot`; `SnapshotValidation.Model`, `OperationsModel`, `Services` | Read-only mirrors only; schema 11 and stale snapshot checks. Validate nested state and construct incoming records before restoring anything. After success restore clock/cash/rooms/guests/subsystems, then publish applied metadata. Preserve this ordering: never restore one new subsystem and discover another is invalid afterwards. `HasOperations`/`HasServices` are explicit presence flags because Unity JSON can materialize empty nested objects. |
| `Networking/LanWorldInput.cs`: `SubmitRemoteInput`, `ReadLanInput`; `Interaction/PlayerInteractor.cs`: `UpdateFocus`, `Update` | Input has epoch/sequence checks, finite/clamped vectors and a 0.35-real-second lease; host actor raycast selects the first physical surface and checks availability before interaction. Client components cannot execute the world action. Bed authorization belongs here through a real `HotelInteractable`, not in an asserted UI payload. |
| `Networking/LanWorldReplicator.cs`: `Configure`, `CacheScene`, `Capture`, `Apply` | World pose/prompt presentation is a separate sequence/channel from the hotel frame. All client `HotelInteractable` authority drivers are disabled; existing authored labels are cached, while moving parts/renderers need explicit registration where appropriate. A bed's visible pose or prompt is not authoritative consent. Put sleep control state in the validated hotel frame, not only in this presentation stream. |

### Maintenance kind: keep payment and the job one transaction

Current route is `GameSession.Maintenance.BeginBoilerMaintenance` → LAN `BeginBoilerMaintenance` → `HotelSimulation.Maintenance.BeginBoilerMaintenance`. The session checks continuous Service, actor/device readiness and pause; the model checks mirror/running/actor/accounting bounds, pure boiler availability, then cash, payment total and job creation. The old LAN `Maintenance` command instead chooses **legacy between-shift** maintenance and must not be mistaken for continuous Basic/Full service.

Extend the continuous route with an explicit valid kind; choose cost, duration and benefit on the host from configuration. Never trust a client-supplied charge, improvement or finish time. Validate all job/actor/preparation requirements before debit; a busy/invalid/stale request leaves money, period spend, condition and deadline unchanged. Sequence guards already reject identical replay, but a different delayed envelope can outlive a job: bind any multi-step physical preparation/confirmation to its job or boiler revision so it cannot start an unintended later service. Preserve deliberate repeat service when newly authorized.

Wire additions belong in `HotelModelSnapshot.BoilerSnapshot`, `SubsystemSnapshots.BoilerSystem.CaptureSnapshot/RestoreSnapshot`, `SnapshotValidation.Model` and the configuration-aware checks inside `ApplySnapshot`. Today `MaintenanceEndsAt > 0` requires a running hotel, a future deadline, zero heat and no relief actor, and its maximum duration uses the existing Full maintenance hours. A kind-aware implementation must validate kind/presence, start/end relation, configured duration/benefit bounds and inactive sentinels together. The retained cash, `PeriodMaintenanceSpend` and report fields must still round-trip once; restoration must not charge or complete a job through gameplay callbacks.

### Sales policy and reassignment: model identity and revision, not UI selection

`Core/GameSession.Bookings.cs`, `GameSession.Lan.ExecuteLanCommand` and `LanSession.SubmitCommand` are the command bridge. `Simulation/HotelSimulation.Bookings.cs` supplies `BookingCommandGate`, `EditableBooking`, `ValidBookingPrice`, `CanReserveInterval`; the latter already accounts for real checkout extensions and pending move destinations. Add a dedicated expected policy revision for a room-policy edit, and extend the reservation-revision whitelist for reassignment. Capture the displayed revision when building the UI action. Host checks must reject stale edits rather than silently reread a fresh revision. Do not reuse compensation's Direct token.

Policy rows and the scheduled demand cursor belong with `Simulation/OperationsSnapshots.cs`: `OperationsSnapshot`, `CaptureOperations`, `RestoreOperations`, `SnapshotValidation.OperationsModel`. Before any restore validate the exact unique room set, legal price grid, policy versions, finite bounded decision times/counters and consistent demand progress. Preserve the current bounds on offers/reservations and no duplicate candidate decisions after snapshot/reconnect/midnight. A closed-sales room **may retain existing reservations**; rejecting that combination would violate the requested policy semantics.

Before reassignment mutation, validate future editable status + expected reservation revision, real destination room and legal interval excluding this same reservation. Keep agreed price, guest/candidate identity and times unchanged; increment revision once. Snapshot validation must still tie arrived guests to their reservation room/price/schedule, protect active non-overlapping stays and receipt status. No partial room/key/guest update on rejection. Automatic booking generation runs only on the host model tick; a mirror refresh must neither generate demand nor expose the old manual-accept command as a policy bypass.

### Sleep: replicated decision, local presentation

`LanHotelFrame` plus `GameSession.CaptureLanFrame/ApplyLanFrame` is the smallest session-state mirror boundary. Add a bounded sleep record: state, agreement identity/revision, the required participants, each actor's confirmed bed/consent, absolute wake target, and a bounded public wake reason. Validate its enum/identity, exact two-actor shape, known authored bed IDs/unique bed ownership, consent/state consistency, finite future target while sleeping, and compatibility with the incoming clock/phase/acceleration owner **before** calling `targetSimulation.ApplySnapshot`. Validate local scene references while staging the frame; committing sleep presentation after model restore must not introduce a new failure path.

Physical bed input can use the existing authenticated input stream. If a bed opens confirmation UI, the host first issues a bed-bound grant; confirm/cancel commands then carry that agreement identity/revision and validate active actor, connection, bed ownership/proximity and current state. An arbitrary `uiBlocked=true` is insufficient. A cancel remains possible while sleeping/input is otherwise blocked; it must target the current agreement, not a later one. Do not broaden the generic Service command gate merely to let an ungranted bed command through. Disconnect/input-lease/device/focus loss need an explicit sleep policy that revokes unsafe consent and cannot silently turn two-player agreement into SOLO.

Local-only UI: selected button, focus/navigation, pending “sent” feedback, fade/camera presentation and button labels. Replicated: partner readiness, actual sleep start/end, target time, wake/cancel reason and revision. Local UI close is not itself the authoritative vote result; send cancellation and reconcile to the host. The client never changes `Clock.Speed`, advances the model, or derives sleep completion from its own wall clock. Keep guest privacy when choosing public emergency text.

`WaitController.Update/Stop/ObserveSimulationEvents` and `GameSession.Service.Update/Tick` currently own acceleration resets, including pause and any event. Give sleep explicit ownership and same-tick critical wake without weakening ordinary WAIT. `LanSession.Disconnected`, NewGame/epoch reset and pause handling must clear/suspend the agreement according to the selected contract. World pose packets may arrive separately; they must not recreate a cancelled agreement or reopen an old sleep UI.

Future gate cases: real Unity-JSON round trips (including null-to-empty strings); unknown enum/nonfinite deadline/duplicate policy room/invalid sleep participant; malformed combined frame leaves clock, cash, model identities **and local UI** unchanged and allows the next valid packet with the same sequence; two stale concurrent edits; duplicate payment; reconnect with old consent; real remote bed interaction; ordinary WAIT and midnight still work. Existing `Tests/EditMode/LanProtocolTests.cs`, `MaintenanceSnapshotTests.cs` and `Tests/PlayMode/ContinuousSessionAtomicPlayModeTests.cs` provide test seams, not evidence that these new fields are implemented. Bump model/LAN compatibility deliberately when contracts land.

Only this audit document was changed by this follow-up. No Assets, process state, build outputs or user watch-script changes were made.
