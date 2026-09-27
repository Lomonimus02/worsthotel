# 0.4.1 phase 6 — natural guest rhythm contract

Design prepared while the phase 5 physical gate runs. This document is a plan, not an implementation or a test result. Phase 5 full EditMode has passed 778/778; its physical gate is still pending at the time of writing. No Assets or runtime files were changed for this document.

The source requirement is [RHYTHM041_REQUEST.txt](../RHYTHM041_REQUEST.txt), sections 23–29 and 83. Keep one continuous hotel clock and one-night contracts. There is no displayed “morning/evening phase”, no time-of-day boiler multiplier, no automatic portable-heater activation, and no new incident generator. The old Alt+Tab investigation remains unresolved and deferred.

## Smallest coherent change

Continuous dated stays gain deterministic, slightly staggered sleep and wake times, a bounded optional afternoon outing, evening activities biased toward being in the hotel, and one guaranteed scheduled shower when the normal morning routine actually begins. Actual walking and anchor arrival still gate every activity's physical output.

Arrival remains 14:00–18:00 and contracted checkout remains the following 10:00. Under the existing one-night model, much of 11:00–15:00 is genuinely vacant following checkout. Do not create multi-night stays or label departed guests “Away” to manufacture the requested calmer midday. Closed radiators and genuinely vacant rooms, alongside fewer staged showers, determine the resulting demand.

The old legacy shift schedule stays unchanged. Raw settings disable the new dated rhythm; the production living configuration explicitly enables it. Fixed-roster fixtures may explicitly isolate a previous schedule where that is necessary for their stated purpose. New rhythm acceptance uses production scheduling.

## Exact settings and pure timing API

Add immutable `GuestRhythmSettings`, exposed by `LivingHotelSettings.Rhythm` through an optional final constructor argument. Proposed constructor and initial defaults:

```csharp
GuestRhythmSettings(
    bool enabled = false,
    float sleepJitterHours = .35f,
    float businessSleepAdvanceHours = .5f,
    float wakeJitterHours = .5f,
    float businessWakeAdvanceHours = 1f,
    float outingReturnHour = 19f,
    float outingReturnJitterHours = .35f,
    float outingTravelAllowanceSeconds = 12f,
    float businessOutingProbability = .25f,
    float budgetOutingProbability = .65f,
    float coldSensitiveOutingProbability = .40f);
```

These are starting tuning values to measure, not promised occupancy, demand or balance results. All values must be finite, probabilities in 0–1, jitter/advances nonnegative and the return hour inside 0–24. Duration and hotel-hour conversions use the configured calendar; the explicit travel allowance is simulation seconds, like existing activity and physical-route timing allowances. It predicts eligibility only and never completes travel.

Add immutable `GuestDailyTiming` with `SleepAt`, `WakeAt`, `OutingReturnAt`, and this pure instance helper:

```csharp
GuestDailyTiming GuestScheduleSystem.DatedTimingFor(
    BookingApplication application,
    int arrivalDay,
    float arrivalAt,
    HotelCalendar calendar);
```

Use stable hashing of the existing living seed, arrival date and guest ID, with independent salts for bedtime, wake and outing return. No shared mutable random generator and no reads that consume randomness. Disabled rhythm reproduces the old configured bedtime and `max(midpoint(sleep, checkout), checkout - 2 hotel hours)` wake exactly; `OutingReturnAt` is then -1.

Enabled bedtime is centered on the configured `SleepHour`, advanced for Business and jittered by the configured range. Enabled wake uses the old normal-wake baseline, advances Business by one hotel hour and applies its independent jitter. In the ordinary 23:00/10:00 configuration this means roughly 22:09–22:51 bedtime and 06:30–07:30 wake for Business; other guests roughly 22:39–23:21 and 07:30–08:30. The final helper clamps custom-calendar results to strictly ordered finite `arrival < sleep < wake < contracted checkout`, without changing arrival or checkout. Where custom intervals cannot support the nominal outing, skip that outing; do not invent another date. Root and model tests will use the same helper to verify the exact clamping boundary, not a separately copied formula.

Root uses this helper for ordinary offer generation, developer dated offers and snapshot validation. Existing `ScheduledBookingOffer.SleepAt/WakeAt` remain the exact authoritative rest times. Materialization passes the helper's outing date to the backwards-compatible signature:

```csharp
GuestAgent AttachStay(GuestStay guest, int arrivalDay,
    float arrivalAt, float sleepAt, float checkoutAt,
    float wakeAt = float.PositiveInfinity,
    float outingReturnAt = -1);
```

The rhythm itinerary is used only when settings enable it and valid finite dated wake/outing timing is supplied. Existing legacy/manual attachment callers preserve their prior behavior by omitting the new argument.

## Schedule and morning progression

Keep the immutable array bounded to 24 entries and preserve all existing activity numeric values. Add only these immutable schedule fields:

```csharp
int GuestSchedule.MorningActivityIndex; // -1 for legacy; otherwise points to Shower
float GuestSchedule.OutingReturnAt;    // -1 only for legacy; every rhythm stay has a finite planned threshold
bool GuestSchedule.HasDailyRhythm => MorningActivityIndex >= 0;
```

The dated itinerary has three small parts:

1. Unpack, short trait-appropriate rest/work, and at most one seeded optional outing. The first entry remains Unpack. A late real check-in skips an obsolete outing when `now + AwayDurationMin + OutingTravelAllowanceSeconds >= OutingReturnAt`; it does not change the contract, teleport the guest or force their later activities to catch up.
2. A varied evening tail built from the existing personality weights, excluding further LeaveHotel and Unpack entries. Business still favors work/phone; Budget favors television/rest; ColdSensitive retains longer showers. Quiet TV is not reclassified as a loud source. If the finite evening tail ends before bed, repeat a quiet/rest tail rather than wrapping to Unpack or crossing into the morning block early.
3. A morning block whose first entry is Shower, followed by quiet rest/work. Normal packing and checkout keep their existing higher priority. Exhausting the morning block repeats its quiet tail; an accepted late checkout extends that ordinary tail and does not start another shower or outing.

The exact number of entries assigned to the evening tail is an implementation detail within the same 24-entry bound. `MorningActivityIndex` is the explicit boundary, so a delayed service conversation cannot accidentally consume morning entries the previous evening.

Add one internal shared transition:

```csharp
void HotelSimulation.BeginGuestMorningRoutine(GuestStay guest, float now);
```

For a rhythm guest this starts the morning block once, advances `ActivityIndex` past the selected Shower and uses existing `SetActivity`. Cursor >= morning boundary proves the routine has already begun; do not add a second mutable “morning done” flag. For legacy guests preserve existing quiet-rest wake behavior. The physical shower still begins only after `SignalGuestActivityReady`; while walking, hot-water demand remains zero.

Invoke the transition on ordinary waking, or on a successful wake-up call that actually wakes the guest. Preserve ordinary checkout/early checkout, current Direct decisions, relocation and existing response-action/return ownership before starting it. A guest who genuinely leaves before their routine can begin correctly has no morning shower. The guarantee is a scheduled first morning action for an eligible remaining stay, not a forced override of lifecycle or service priorities.

The current wake-up request uses checkout minus 30 simulation seconds (09:00 in production), later than the old 08:00 normal wake. For newly generated rhythm requests, use that guest's immutable `Schedule.WakeTime` as the promised due time. Never rewrite an already accepted promise, including after a later checkout extension. Answering after the guest has already woken remains a valid promise outcome within the normal tolerance, but does not restart their shower. Existing private observation/contact and promise acceptance are still required; schedules create no guaranteed service request.

Normalize an obsolete outing cursor consistently before exposing the upcoming activity or testing the existing 12-second contact lead. The actual transition, `NextPlannedActivity`, guest presentation diagnostics and service contact prediction must agree. Sleeping rhythm guests advertise their morning shower rather than the obsolete “Pack / Checkout” caption.

For a natural outing, the real exterior-exit callback sets `AwayReturnTime = max(actual exit time, planned OutingReturnAt)`. If an unexpectedly slow actual route finishes after the intended return, return begins on the next ordinary tick through the real lobby/room route. Diagnostic forced outings keep their existing relative duration. Room/key ownership, privacy, no exposure while away, DropOff receipt eligibility and checkout priority remain unchanged.

## Snapshot and authority requirements

Root owns the wire changes and all snapshot files. Add `MorningActivityIndex` and `OutingReturnAt` to the agent's immutable schedule DTO/capture/restore/identity comparison. No new activity/state enum values are required. Use explicit finite -1 legacy sentinels; do not encode infinity in the new fields.

- Legacy schedules require both sentinel values. Rhythm schedules require `1 <= MorningActivityIndex < Schedule.Length <= 24` and Shower at the morning index.
- Outing time must be finite, after arrival and before the schedule's sleep. No more than one LeaveHotel entry occurs before the morning boundary; no morning LeaveHotel/Unpack or service-response activity can be accepted from a packet.
- Validate offer sleep/wake against `DatedTimingFor`, then require materialized schedule dates to match its existing offer exactly. Validate the derived outing date with the same helper, not a second clock formula.
- Capture/restore the morning cursor atomically with activity, staging and current route. A mid-shower snapshot cannot make a mirror restart the morning routine. Existing response version, intent revision, item generation and physical ownership validation remain in force.
- Mirrors display and forecast host state. They do not choose a new activity, advance a date or apply a due wake transition.
- Protected history, already accepted promises, original contracted checkout and receipt-once behavior remain intact.

## Focused acceptance tests

Agency Life model tests:

- Same ID/date/settings yield identical timing and itinerary independent of attachment order; different production guest IDs have a measurable wake spread. Custom calendars preserve strict date ordering and legacy disabled values remain exact.
- Real model wake selects the morning Shower once; its numerical heat/noise output remains absent until the labelled headless physical-anchor callback. A fulfilled earlier wake call enters the same block once; calling after natural waking does not restart it. Promise due time remains stable after a permitted late checkout.
- A late check-in skips the expired outing. No new morning outing starts; a slow actual departure can return late through the existing states without becoming a false room arrival. The finite itinerary never wraps to Unpack or an earlier shower.
- Existing Direct/contact/relocation ownership postpones activity without renewing itself or resetting the cursor. Normal and eligible early checkout still win. Away guests retain actual radiator demand but contribute no staged shower, room noise or room exposure.
- Snapshot roundtrip preserves the exact immutable plan and current morning progress. Malformed index, date or injected morning outing is rejected before any mirror mutation; client ticking cannot make the next decision.

Agency Situations independent cohort tests:

- Use real production dated stays, calendar and attributed consumer calculation with explicitly labelled headless route/key/anchor adapters. Observe morning staged showers, evening room presence/activities, sleeping night and genuinely vacant turnover interval.
- Count actual activities/presence and sum the existing demand rows; never assert a hard-coded time multiplier or force temperature/failure/load. Compare morning shower hot water to the same guests asleep and to the post-checkout interval. Do not claim a lower night space-heating baseline when unchanged real radiator settings still request it.
- Re-run the existing multi-day economic counterfactual as a measured regression; do not relax it merely because schedules changed. Production sales counts remain outcomes, not a fixed injected roster.

Agency Interface physical tests:

- One actual living guest walks from real sleep staging to the morning shower. Observe hot-water demand only at the genuine shower anchor, then normal continuation. Clearly label any initial model key/check-in adapter separately from the route assertion.
- One optional natural outing follows room door → lobby → exterior and genuine return before its evening activities; no diagnostic ForceLeave/ForceActivity or fabricated arrival callback can establish this claim.
- Existing service/privacy/controller regressions retain their ordinary purpose. Pure captions may be checked without adding another broad gameplay matrix.

Root runs Unity, full/selected gates and later actual executables. These planned tests are not yet run. Long-pause behavior, human SOLO pacing and two-machine networking need their own evidence; this phase does not claim them.

## Ownership after the phase 5 gate

- **Agency Life:** `GuestScheduleSystem.cs` and new rhythm partial/data/settings; `LivingHotelSettings.cs`, `Simulation/Definitions/LivingHotelConfig.cs`, production `LivingHotel.asset` and its narrow authoring defaults; `GuestAgent.cs`; `HotelSimulation.Guests.cs` / `.Presence.cs`; narrow `GuestServiceSystem.Commands.cs` / `.Intents.cs` and wake request due-time hook; new focused model tests.
- **Root:** `HotelSimulation.Bookings.cs` timing/materialization call sites, every snapshot DTO/validation/capture/restore file, versions, shared integration and all Unity/build/EXE runs.
- **Agency Situations:** independent cohort-rhythm/demand comparison tests and explicitly scoped fixture isolation where required.
- **Agency Interface:** actual guest-route PlayMode tests and the narrow DeveloperPanel/GuestPresentation upcoming-activity captions; coordinate shared presentation files before editing.

No runtime implementation begins until root opens phase 6 after the phase 5 gate.
