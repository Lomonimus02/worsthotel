# Prototype 0.2 — Phase 6 alternative responses

The root confirmed the Phase 6 gate: scene compilation and all 136 EditMode / 22 PlayMode tests passed (`Logs/p02-phase6*`). The preceding Phase 5 gate was 124 / 19. No Unity run was started by the verification agent.

## One problem, different consequences

The new response paths retain the same living guest, agreed price, need history and economic evaluation. They do not create a prescribed sequence of repair objectives. The service ledger displays current conditions and offers compensation, acceptance of the loss, and a comparison of possible destination rooms. The portable heater is a physical local workaround; the existing central boiler repair remains another option.

Relocation validates actor, guest, current room ownership, destination existence, occupancy, reservation and cleanliness before changing any ownership. The source becomes free and the destination becomes reserved. The guest enters the real travel state and emits no room heat demand or acoustic source during transfer. Historical exposure, dissatisfaction, quality integral, billable time and schedule identity survive.

An active situation and its request pause during transfer. Merely walking outside the old room cannot count as a sustained recovery. Actual arrival consumes the destination reservation. A warm destination can recover the original complaint after the normal recovery interval; another cold destination continues that same complaint and history. Checkout or settlement during transfer releases the destination reservation and retains earlier actual room time for the receipt.

Compensation reserves one checkout credit and closes the current operational cases as an accepted response. It records an explicit resolution reason while keeping the measured cause and needs. Persistent cold still accumulates exposure and room-quality loss. The accepted ongoing episode is suppressed until real recovery; a different new cause remains eligible to create a new case. Credit metadata is separate from accepting that future problem, and a second credit cannot be promised to the same stay.

`AcceptConsequences` acknowledges attention for current episodes without resolving them. Severity, escalation, dissatisfaction, quality loss and refunds still follow the underlying conditions. The acknowledgement permits intentional continuation of the day rather than forcing a repair. Recovery followed by a new episode resets attention. The root separately integrates explicit acknowledgement of a failed boiler into WAIT; it does not restore heating.

## Portable heater and closing policy

Each portable heater has one stable registered identity, an optional current room, switch state, power availability and immutable heat/electrical settings. Duplicate registration cannot add another consumer with the same ID. Carrying or placing the whole tool outside an authored room removes its local heat and demand. Switching off preserves its physical room while producing no heat or electrical demand. Removing the component unregisters its consumer.

The default heat output is a **+14°C supplement to the local equilibrium-temperature target**, not a heater thermostat fixed at 14°C and not an instantaneous temperature jump. Actual room temperature still changes through the common thermal time constant. A powered heater can relieve a cold room without changing central-boiler demand or neighbouring temperatures. In an already warm room, leaving it on can overheat the room; that is a consequence to inspect during later balance work.

The heater requests 2 electrical units when switched on and placed. Loss of power stops delivered heat while preserving requested load, which is necessary for the Phase 7 breaker to trip again if the underlying overload remains. Circuit capacity, overload timing, trips and room power penalties belong to Phase 7 and are not claimed here.

Independent review identified a lifecycle mismatch: the overnight thermal pass omitted portable heat while the heater could still display ON. The agreed fix is an explicit closing policy. `EndShift` switches every portable heater off, preserving registration and location. The next day starts with that switch still off. The ledger communicates this policy, and the heater regression covers settlement, overnight maintenance and the next shift.

## Prepared verification

`AlternativeSolutionsTests.cs` adds twelve EditMode cases:

- Atomic source release and destination reservation, real arrival occupancy, schedule/history retention and source migration.
- Invalid actor, guest, destination, occupied/reserved/dirty room and repeated-transfer rejection without partial state changes.
- Frozen complaint/need/quality clocks in transit and sustained recovery only after reaching a warm destination.
- Another cold destination continuing the same request and exposure history.
- Checkout during transfer releasing its reservation while earlier room time remains billable.
- Accepted compensation closing operational response while retaining physical cause, quality loss and a single checkout credit.
- A later different noise problem remaining unaccepted despite the earlier credit.
- Intentional non-response retaining escalation and producing a worse receipt than restoring warmth.
- A new episode requiring fresh attention after real recovery.
- Heater identity, room assignment, switch and power behavior, atomic invalid placement and unique consumer demand.
- Real local thermal improvement and complaint recovery with a powered heater versus a switched-off control; power loss, explicit shift-end switch-off, overnight behavior and next-day persistence.
- Invalid heater settings, duplicate room IDs and unknown consumer commands producing no phantom load.

The thermal comparison uses an explicit cold-baseline fixture with a shorter thermal time constant to isolate the model consequence. It is not a claim about final production pacing. Existing legacy tests remain unchanged.

The presentation agent's two complementary PlayMode checks passed: real heater switch/grab/carry through a door/placement/local warmth/off/disable, and physical relocation through the source and destination doors followed by checkout interrupting another transfer. The root's WAIT integration check also passed: two real virtual-pad votes can continue after intentionally accepting boiler failure, without repairing heating, and a later important event stops WAIT. These three additions bring the complete PlayMode suite to 22 passing cases.

## Review and remaining evidence

After the explicit closing policy correction, the bounded source review found no unresolved critical issue in relocation ownership, transfer timers, accepted-response suppression, attention rearming, single credit accounting or heater consumer identity. The root's compile/runtime gate subsequently passed all cases.

Rendered clarity of response choices, carried-object ergonomics, real controller use, audio mix and the human tradeoff between repair, moving, local heat, compensation and accepting a loss remain manual checks. Phase 7 must connect power delivery to heater heat and electric loud-activity sound without erasing requested load. Phase 9 must measure the resulting three-day costs and timing under actual configuration assets.
