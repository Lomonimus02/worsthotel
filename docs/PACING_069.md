# 0.6.9 — Pacing and occupied hotel gameplay

## Finding

The human 0.6.8 pass confirmed that guests move, shower, watch TV, leave/return and collect requested blankets. It still found too little operational work after approximately 15:00–16:00. Navigation is not being diagnosed or replaced again.

The shipped 0.6.8 calendar used 720 real seconds per 24 hotel hours. The 16:00–23:00 window alone lasted 210 seconds. Ordinary schedules were already varied, bounded and tied to real room anchors. The more concrete service limiter was a **three-contact ceiling per hotel day**, counting optional cases whose contact had started, including completed cases. One guest could use two of those slots. An incident could still be valid while another guest's earlier request had exhausted that day's allowance. Serious factual complaints bypass this optional-service ceiling.

The normal SOLO eligibility was .55 × .8 = .44, sampled once per stay/request kind, not per tick. Cold sensitivity multiplies blanket willingness by 1.6; business schedule requests by 1.35; Patient by .8; Impatient by 1.15. These are willingness gates after a real cause, not spontaneous event probabilities.

## Shipped tuning

| Setting | 0.6.8 | 0.6.9 | Purpose |
| --- | ---: | ---: | --- |
| Hotel day at 1x | 720 seconds | 480 seconds | 20 real seconds per hotel hour; 16:00–23:00 takes 140 seconds |
| Quiet rest / work range | 12–22 seconds; rest capped at 12–18 | 8–14 seconds | Less of the compressed evening spent in low-output activities |
| Optional time outside | 28–48 seconds | 20–34 seconds | Similar duration in hotel hours; travel speed is unchanged |
| Ordinary shower / TV / phone / loud range | 16–28 seconds | unchanged | Keep sustained real loads and time for staff response |
| Dated morning shower | 16–28 seconds before trait multiplier | capped at half a hotel hour: 10 seconds, 13.5 for cold-sensitive guests | Leave room for physical bed/shower travel and the following activity before checkout |
| Hotel-wide optional contact ceiling | 3 per day | 8 per day | Allow valid needs from a larger occupied hotel to coexist |
| Per-guest ceiling | 2 per stay | unchanged | Prevent one guest monopolizing contacts |
| Base eligibility × SOLO factor | .55 × .8 | .65 × .9 | Ordinary willingness becomes .585; real cause still required |
| Observe before self-response | 5 seconds | 3 seconds | Start the existing radiator response sooner |
| Tolerate before contact | 10 seconds | 6 seconds | A sustained noise source can still exist when the guest reaches the phone |
| Minimum causal observation | 7 seconds | unchanged | Transient discomfort still does not immediately call staff |

Values live in `PrototypeSession`, `LivingHotel` and `GuestServices` assets. Unity time scale, fixed step, movement, carrying, key handoff, bed making, phone ring duration (12 seconds), reception wait (35), direct conversation window (40), retry delay (25) and two-attempt maximum are unchanged. The dated special-guest activity hours continue to convert through the calendar. Legacy shift/model constructor defaults are separate from these shipped scene assets.

No hotel-wide contact cooldown exists: each factual response has its own dwell/retry state. The physical telephone rings for one caller at a time; a reception visit, pending blanket, another guest's arrival and infrastructure trouble can coexist. A guest still has at most one active optional service case, one current physical response and one direct conversation. Contact is withheld during sleep, showers, relocation, unstaged travel, the final 12 seconds before checkout/sleep or an imminent outing. Serious concerns retain their own causal response identity.

Complaint eligibility remains factual severity plus sustained exposure/dissatisfaction: 20/35/65-second exposure thresholds and .30/.65/.90 dissatisfaction thresholds; recovery needs 8 seconds and repeat episodes have a 12-second reopening cooldown. No complaint threshold was lowered. Same guest/kind service deduplication, one response per causal episode and exactly-once receipt accounting remain.

## Activity and item audit

- Shower already supplies real hot-water/heating demand (1.6 multiplier versus .85 quiet demand) only after physical staging, with boiler load/pressure/stress consequences.
- TV/loud activity already produces actual noise; .7 shared-wall and .3 corridor transmission feed each adjacent guest's own tolerance and source-specific situation. Quiet TV is not guaranteed to cause a complaint.
- Mild-cold self-help physically reaches the radiator before increasing the actual valve. Each step changes heat output (.12) and demand (.25); it is not a cosmetic gesture.
- Away guests stop experiencing room discomfort. Their retained situation is re-evaluated from room perception on return, without manufacturing relief or staff credit while absent.
- **Blanket** is the only requested stock item delivered to an exterior shelf. Its 0.6.8 physical pickup, thank-you, visible bed blanket and comfort/service memory are preserved. Direct interior blanket delivery now also says thanks.
- **Luggage** remains the owner's real suitcase. Accepted bags physically placed at the correct destination retain DELIVERED status and existing storage-service settlement. An awake, available guest now acknowledges staff-delivered bags near the player on settling or returning; no extra satisfaction payout is added and repeat placement does not repeat the same acknowledgement.
- **Bulb** is installed in the existing broken lamp and restores the actual powered light; its room-condition incident resolves from measured recovery. It is not a new guest drop-off request.
- **Portable heater** is existing equipment: physical placement/power use changes room heat and branch load. No new inventory request or collection flow is introduced.

The existing activity weights, independently seeded choices, no-adjacent-rest rule, varied evening tail, individual sleep/wake offsets and optional outings remain. Shorter quiet intervals change the time spent in active use without a shared evening trigger. No director, request quota, new archetype, new request type, daytime skip or tutorial UI was added. The room board, dirty opening bed, ordinary booking system and all economy prices/contract dates remain.

## Evidence and limits

A small paired model observation enabled the two unsold base rooms (105/106) through normal sales policy, prepared 101, gave keys, answered natural contacts and delivered agreed blankets. It used **instantaneous headless travel**, not real NPC/player routes, and did not maintain the boiler. It is a gate diagnostic, not human pacing acceptance.

| Headless observation | Previous values | New values |
| --- | ---: | ---: |
| Guests | 6 | 6 |
| All distinct contacts, including serious episodes | 14 | 12 |
| Contacts starting 16:00–23:00 | 7 | 3 |
| Peak simultaneous known optional services | 1 | 2 |
| Ticks with budget saturated and a response waiting without a case | 750 | 0 |
| Optional contact slots actually charged | 3 | 6 |

The waiting-tick count is a diagnostic intersection, not proof that every waiting response passed every other gate. The comparison deliberately does not claim that more calls alone are better. Unattended boiler failure and resulting cold account for serious contacts in this sample; those are existing operational consequences, not injected requests. Real shower demand and neighbour noise were observed; no duplicate service kind per stay was created.

The physical six-room observation completed the daytime and evening and entered actual staff sleep at 23:04. It used explicit staff adapters for linen, keys, one naturally requested blanket and two full-price Basic services triggered by actual stress. Guest routes, activity staging, loads and pickup acknowledgements were production. Staff sleep used the real bed and input after a labelled staff approach placement. Remaining contacts were observed, not answered by this adapter, so this is not a successful-service or human playthrough score.

During 16:00–23:00, sampled boiler demand ranged from 3.84 to 6.50, crossing Busy/Overloaded; aggregate received room noise ranged from 0 to 3.19. The longest gap between meaningful sampled load/noise changes was 20.4 seconds (samples approximately 10 seconds apart). Two new distinct temperature contacts began in that evening window. Across the day there were five distinct additional temperature contacts, alongside the earlier naturally requested blanket physically received in 102 at elapsed 82.8 seconds. Basic service was purchased at elapsed 117.4 and 180.4 seconds, for the unchanged $350 each. Radiator changes came from actual guest self-help. These contacts and maintenance costs were not injected to meet a quota.

The combined run then **failed at the next morning**: several guests could not physically reach their sleep position. A focused collider check identified the solid waste-basket interaction at the standing bed approach. This is a separate concrete night-transition blocker, not a new explanation for the already-observed evening pacing. Ten baskets have been moved from the inner bedside path to the free outer foot corners; bed anchors, routes, furniture and cleaning interactions remain. The scene change is ten transform positions and the matching scene-builder placement. The old fixture also needed its existing dirty opening bed prepared and unopened North Wing sales left alone.

The targeted bed-clearance and physical sleep/wake/shower checks both passed after this correction. All ten bed approaches are clear. The existing morning fixture observes actual sleep, waking, walking to the shower before hot-water demand starts, and a staged activity after the shower. The compressed wake-to-checkout window also required capping the dated morning shower at half a hotel hour, before the cold-sensitive trait multiplier; daytime showers remain sustained. The full evening run is not being repeated merely for counts. Normal human acceptance remains pending: this evidence does not establish how busy the evening feels or whether a new player has enough travel time.

Raw evidence is retained under `docs/verification/pacing069`: paired model observations, the physical evening run including its original night failure, and the two successful focused checks after the correction. These have different scopes; the original combined run is not presented as a passing end-to-end session.

For the human pass, start SOLO, prepare 101, check in ordinary arrivals, remain active through 16:00–23:00, sleep in the staff room and observe morning work. Four rooms start for sale as before; opening the existing 105/106 in Room sales gives the six-room comparison without unlocking a wing or changing prices. Judge whether there is a 45–60-second evening stretch with no observable, operational or actionable change, and whether the compressed clock still leaves enough time for physical work.

## Build

Windows build succeeded: `Builds/Windows-0.6.9-pacing/TheWorstHotelEver.exe`, shortcut **Играть 0.6.9**. LAN 30 / model schema 24; both players need the same version. The build includes the Russian quick guide. Normal player acceptance remains pending; the user has been asked to judge the evening and next morning. No GitHub publication is part of this correction.
