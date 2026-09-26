# Prototype 0.2 — Phase 7 electricity

The root confirmed the Phase 7 gate: scene compilation and all 148 EditMode / 24 PlayMode tests passed. The preceding Phase 6 gate was 136 / 22. The root owns scene compilation and all Unity execution; the verification agent did not launch Unity.

## Two circuits and inspectable demand

Circuit A supplies rooms 101–103; B supplies 104–106. The agreed default capacity is 4 units per circuit. A guest actually in their assigned occupied room requests 0.85 units, and an ongoing loud electric activity requests another 0.25. A placed, switched-on portable heater requests 2 units. Reservations, initial room travel and relocation do not count as occupied guest consumers.

The defaults leave deliberate headroom for light booking: two quiet occupied rooms plus a heater request 3.70; one loud activity raises this to 3.95, still below capacity. Three occupied rooms plus a heater request 4.55 and overload that circuit. Three ordinary loud rooms without a heater request only 3.30. Exact capacity is allowed.

Every guest/device consumer has a stable unique ID. The consumer inspection snapshot exposes room, circuit, requested load and delivered load. A registered carried or switched-off heater can remain visible in the inspection list with zero demand. Received power and requested demand are distinct: an open breaker delivers nothing while the plugged-in consumers still request their original load. This preserves the cause of a repeated trip.

Overload must be continuous. Default warning begins at 6 hotel seconds and trip at 18. Returning below capacity clears the warning and continuous timer. A trip opens the circuit until a staff member physically resets it; merely reducing load does not move the breaker. Resetting while the original demand remains gives the same warning/trip sequence again. Resetting after reducing demand restores stable power.

## Delivered consequences

The electrical pass derives demand from current activities and placed tools, advances the circuit, then updates room and heater power before downstream noise, thermal and guest-need evaluation. The clock and physics timestep are not multiplied separately by electricity.

Power loss stops a portable heater's delivered heat. The physical activity and its requested demand survive. The circuit also silences electric loud-room audio/noise, retaining the guest's activity and requested loud load so they can resume when power returns. Shower sound and its boiler demand remain separate causes.

Lack of power contributes the configured 0.65 severity to the existing room-condition need. Dirt, fixture damage and power loss combine there with a normalized cap; electricity does not add a second independent quality penalty. Exposure history also records actual seconds without electrical power so the final review can describe that experience factually. Need accumulation still requires the guest to be in the room.

Tripped breakers persist through checkout, settlement, boiler maintenance and the next day. Shift closure still switches portable heaters off. A new session creates fresh powered circuits. The panel, room plaques, guest detail and relocation comparisons expose actual circuit/power state; planning presents load risk rather than pretending every room has independent power.

## Prepared verification

`ElectricityTests.cs` adds twelve EditMode scenarios:

- Authored circuit membership and safe ordinary occupancy/loud activity without heaters.
- No reservation/travel consumer, followed by unique guest demand moving between circuits and ending at checkout.
- Unique requested consumers retaining demand while a trip stops delivered power and heater heat.
- Safe exact capacity and the intended two-room/heater/one-loud-activity headroom.
- Warning after real sustained overload, no repeated warning event every tick, clean timer recovery and a later independent trip episode.
- Invalid resets, physical reset without cause removal retripping, and stable operation after reducing demand.
- Power loss silencing electric loud activity while shower sound survives and requested activity load is preserved.
- A single room-condition/quality contribution, no instant request, and normalized combined dirt/power severity.
- Heater carrying/placement migrating demand without ghost consumers; zero-time refresh does not advance overload time.
- Tripped state persisting across days while a newly constructed session starts powered.
- An integrated causal thermal/secondary-guest/receipt comparison.
- Invalid tuning and circuit commands leaving healthy power unchanged.

The integrated diagnostic uses the same three guest identities and schedules. Both runs place a heater in a cold room 101. The overloaded run places the third guest in 103, while the distributed run uses 104. Central heating and activity timing are explicitly controlled to isolate this causal difference, with the ordinary 45-second thermal time constant retained. No forced trip or mid-run temperature override is used. The test requires measurable warming before the actual overload trip, a later power-loss complaint from the otherwise comfortable guest in 102, factual power-exposure review text, and worse financial results than distributing the bookings. It logs actual timing, temperatures, refunds and secondary-guest satisfaction. This diagnostic is not the later Phase 9 natural-day balance story.

The existing Phase 6 heater thermal fixture now requests a real diagnostic circuit trip and staff reset rather than directly setting `Heater.Powered`. All its cooling, persistent requested-load, closing-policy and next-day assertions are retained. This is an explicit migration to the new power authority, not a disabled electrical/living mode.

The root's PlayMode test passed using two actual virtual-pad WAIT votes: electrical warning stops acceleration before trip; fresh consent can continue until trip stops it again while physical time remains unchanged. It uses a declared diagnostic consumer to isolate the event observer. The environment agent's physical test also passed: three actual guests on B and a placed heater produce warning/trip, lights/radio/heater lose power, opening the cabinet and resetting the real breaker causes a repeat trip, then switching the actual heater off and resetting restores stable power while A is unaffected.

The controlled thermal comparison passed with an overload trip after 18.20007 hotel seconds and the secondary guest's complaint after 41.20005 seconds. Room 101 started at 12.724°C and warmed to 16.11179°C before the trip; it ended at 10.43585°C without power versus 21.6093°C in the distributed-booking run. Guest 102's score was 72.99986 versus 100; total refunds were $45 versus $0. These measured diagnostic results isolate electricity's causal consequences; they are not the Phase 9 natural-schedule balance story.

## Review limits

The final bounded read-only review found no unresolved critical issue in consumer identity, requested/delivered separation, circuit transitions, zero-time refresh, room/heat/noise power delivery, lifecycle hooks or the single quality penalty. Power flags and consumer delivery snapshots are applied before circuit-change callbacks. Legacy two-argument simulation fixtures do not instantiate the living electrical system. All prepared test APIs were checked against the saved source.

The automated gate passed. Real-display readability, audio mix, physical controller ergonomics and human judgement about the consequences of a heater remain manual checks. This phase supplies the second interacting infrastructure system; its successful gate permits the subsequent housekeeping phase.
