# Boiler and electricity interaction — 0.4.1 investigation

Status: **phase-0 read-only findings / phase-1 plan**, 27 September 2026. No relationship, tuning, runtime or tests have been changed by this audit. New milestone baseline/reproduction runs belong to the root verification ledger; the historical observations below are explicitly from 0.4. The authoritative 0.4.1 request is [RHYTHM041_REQUEST.txt](RHYTHM041_REQUEST.txt), §§18–22, 74, 78.

## Observed behavior and scope

The user reports electrical problems occurring around boiler failure. That is a valid playtest observation, but the exact previous session's heater placements, switches, room activities and circuit timers have not been captured here. Its individual cause is **not established** merely from temporal proximity.

The final 0.4 actual three-day SOLO report records **one natural boiler failure and zero circuit trips**, with no driver heater intervention: [runtime report](verification/core04-phase10-solo/20260927T025044Z-465a7488/runtime-verification.txt). Thus a boiler failure does not invariably entail a power outage in an actual player. It does not reproduce the user's particular cascade.

The final 0.4 LAN report deliberately invokes `ForceFailure` and `DebugTripCircuit(B)` as separate diagnostic setup: [host report](verification/core04-phase10-lan/20260927-025558-e1e05778/host-report.txt). Their simultaneity proves persistence, **not a natural causal connection**.

## Actual source relationship

| Question | Verified code behavior |
|---|---|
| Does boiler failure add electricity demand or trip a breaker? | **No.** [ElectricalSystem.Tick](../Assets/_WorstHotel/Scripts/Simulation/ElectricalSystem.cs) builds only `guest:<id>` and `heater:<id>` consumers. It does not receive a boiler reference. [BoilerSystem.ForceFailure](../Assets/_WorstHotel/Scripts/Simulation/BoilerSystem.cs) changes boiler state/output and emits its event; event wiring in [HotelSimulation](../Assets/_WorstHotel/Scripts/Simulation/HotelSimulation.cs) does not trip or load a circuit. |
| Is there an emergency pump or repair electricity load? | **No such registered consumer/path exists.** [RepairSequence](../Assets/_WorstHotel/Scripts/Simulation/RepairSequence.cs), [paid maintenance](../Assets/_WorstHotel/Scripts/Simulation/HotelSimulation.Maintenance.cs) and [boiler maintenance](../Assets/_WorstHotel/Scripts/Simulation/BoilerSystem.Maintenance.cs) do not add electrical consumers. The red breaker in the boiler's mechanical repair sequence is a repair step, not branch A/B. |
| Do cold rooms automatically activate portable heaters? | **No.** [HeaterSystem](../Assets/_WorstHotel/Scripts/Simulation/HeaterSystem.cs) stores explicit placement and switch state. The gameplay switch path is [GameSession.SetHeaterSwitch](../Assets/_WorstHotel/Scripts/Core/GameSession.Alternatives.cs), operated by the physical heater. Guest self-help adjusts its real radiator; it does not switch a heater. |
| Does radiator self-help draw more electricity? | **No.** [RoomSystem.HeatingDemandForRoom](../Assets/_WorstHotel/Scripts/Simulation/RoomSystem.cs) changes attributed central heating demand. The electricity consumer calculation does not read radiator settings. |
| Does an electrical trip stop the central boiler? | **No.** [ElectricalSystem.ApplyPower](../Assets/_WorstHotel/Scripts/Simulation/ElectricalSystem.cs) sets room power and portable-heater power. It does not alter the boiler. A trip removes portable supplemental heat and disables powered room effects, while central heat follows its own availability. |
| Can one staff response produce a real cascade? | **Yes.** Cold room → employee places/switches heater → branch requested load rises → sustained overload → trip → the still-on heater loses delivered power/heat. Cold has an indirect effect through a real staff choice, not an automatic dependency. |

The authoritative tick order is guest/activity update → electrical consumers and overload exposure → attributed boiler demand → boiler stress/output → room thermal integration → perceived needs/incidents/services. Both systems share the hotel clock and real guest presence, but have separate demand, stress/timers and failure state. The circuit does not borrow boiler stress or vice versa.

## Present loads and limits

Production values come from [Electricity.asset](../Assets/_WorstHotel/ScriptableObjects/Electricity.asset), [PortableHeater.asset](../Assets/_WorstHotel/ScriptableObjects/PortableHeater.asset) and their validated settings:

- Branch A serves rooms101–103; branch B rooms104–106. Each has capacity4, with an optional +1 upgrade on one chosen branch.
- A correctly owned guest actually in the assigned room requests .85. Physically staged TV/music adds .25. Travel/away/reception does not add another room consumer.
- Each registered heater explicitly on and assigned to a room requests2. Its demand **persists while power is cut**, while its delivered heat becomes0. Restoring unchanged overloaded demand can trip again.
- Overload means requested load strictly above capacity. Warning occurs after6 hotel seconds, trip after18 accumulated seconds; continuous mode recovers exposure gradually below capacity. At exactly capacity, it does not accumulate overload.

Calculated examples, **not new runtime measurements**: three occupied quiet rooms request2.55; three with simultaneous TV/music request at most3.30, both below4. One heater added to three quiet rooms requests4.55 and can trip after sustained exposure. Two heaters plus one quiet guest request4.85. Therefore ordinary guests alone cannot overload an unmodified production branch; heater use, an explicit diagnostic override or a forced trip is needed under current tuning. The new milestone must not invent a pump to explain this observation.

## Intent, desirability, and bugs

Source comments and existing tests explicitly preserve heater demand through a cut and require load removal before a stable reset. That staff-driven cascade is an intentional, useful choice: local warmth costs electrical headroom. The exact cause of the user's earlier coincidence is still unproven. **No accidental direct boiler/electrical state coupling was found in this audit.** This is scoped source evidence, not proof that all interaction bugs are absent.

Existing physical panel feedback already lists `ROOMS`, `TV / MUSIC`, `HEATER <room>` and `REMOVE LOAD BEFORE RESET`: [ConsumerBreakdown](../Assets/_WorstHotel/Scripts/Environment/ElectricalPanelPresentation.cs). It should be checked from the player's approach, not assumed clear because the text exists. The mechanical repair control is labelled “breaker”; a player could confuse it with the building panel, but that interpretation needs actual inspection rather than being asserted as the reported root cause.

## Chosen direction, pending the phase-0 gate

Preserve separate central boiler and A/B electrical state. Do not introduce an artificial pump, automatic heater switching or paired failure. Phase1 should establish fresh scoped runtime evidence and improve the existing cause message only if observation shows the information missing at the decision point. A minimal candidate is heater interaction feedback stating room/branch and added2 load, plus identifying the boiler repair control as the boiler's isolation switch. These are **proposals, not implemented changes**; do not duplicate a consumer that is already visible.

Required verification before closing phase1:

1. Paired controlled model fixture: identical actual guests/heaters and elapsed electrical time; force only the boiler failure in one copy. Compare consumer identities/requested load, circuit timers/warnings/trips. A zero-time refresh must leave all circuit state unchanged by boiler failure, restart or maintenance. Label controlled setup; do not call it a natural failure reproduction.
2. Cold-room fixture with registered off heaters: allow actual need/response updates; no temperature-only path may toggle heater switches. Real radiator self-help may change boiler demand while the same-room electrical consumer is unchanged.
3. Fresh continuous physical-session trace: begin with actual owned rooms and off authored heaters; controlled boiler failure leaves the branch powered. Staff physically carries/places/switches a heater onto an actually loaded branch; observe its registered load, warning dwell, trip, lost heater output and readable panel cause. Reset without removing demand should retrip; real switch-off plus reset should remain powered. Initial guest/boiler setup may be labelled diagnostic; staff actions must use real input/colliders.
4. Record chronological cause values: hotel time, actual consumer IDs/room/branch/requested and delivered load, overload exposure, boiler load/output/stress/failure, switches and room temperatures. Do not substitute synthetic event text for measured state.

Reuse existing coverage instead of duplicating it: [GuestServiceElectricalPlayModeTests](../Assets/_WorstHotel/Tests/PlayMode/GuestServiceElectricalPlayModeTests.cs) already covers two authored heaters, warning/trip and actual switch/reset; [AlternativeSolutionsPlayModeTests](../Assets/_WorstHotel/Tests/PlayMode/AlternativeSolutionsPlayModeTests.cs) covers physical carry through a door; [ContinuousUpgradePlayModeTests](../Assets/_WorstHotel/Tests/PlayMode/ContinuousUpgradePlayModeTests.cs) uses production continuous demand and real switches/reset with labelled heater placement. [ElectricityTests](../Assets/_WorstHotel/Tests/EditMode/ElectricityTests.cs) covers requested-versus-delivered consumers and secondary guest consequences. The missing assertion is specifically **boiler transition independence plus cold never auto-switching heaters**, not another generic breaker test. Historical [LivingEmergenceTests](../Assets/_WorstHotel/Tests/EditMode/LivingEmergenceTests.cs) is an explicitly pinned legacy shift fixture and must not be relabelled natural0.4.1 evidence.

The former Alt+Tab hang remains [deferred/unresolved](PAUSE_CRASH_INVESTIGATION.md); this investigation does not reopen or claim to fix it.
