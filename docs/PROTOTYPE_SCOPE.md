# Prototype 0.2 — Living Hotel scope

Continue the existing one-floor hotel, art direction, physics and local two-player split-screen. Six guest rooms, three archetypes and three days. Two gamepads or keyboard/mouse plus one gamepad; Unity URP, Physics and Input System. No replacement project.

Guests arrive at different times, receive a physical room-key handoff, walk to their rooms and use quiet rest, shower and loud music activities. Disclosed traits change reactions to four needs: temperature, noise, room condition and patience. Room allocation, price and maintenance decisions affect the consequences.

Preserved boiler load/wear/pressure and two-person mechanical repair now interact with real guest activities, room noise, one portable heater, two electrical circuits and one housekeeper. Responses include physical repair, temporary quiet, relocation, a credit and accepting loss. No system prescribes a fetch chore as the only response.

Both staff can consent to event-aware WAIT; hotel time advances while physics remains normal. Room turnover has a priority queue and actual worker travel. Preparation also runs before service so all-dirty rooms do not lock the next day. Dirty bookings are allowed with visible preparation status; physical check-in requires readiness.

## Required implementation order

0. Preserve and verify the existing baseline.
1. Stable physics carrying.
2. Hotel clock and two-player WAIT.
3. Living guests and schedules.
4. Needs and state-driven situations.
5. Noise propagation and guest conflicts.
6. Multiple responses and physical portable heater.
7. Electricity and real breaker panel.
8. One housekeeper and room turnover.
9. Three-day balance.
10. Audio, feedback, UI polish and final verification.

Each phase requires compilation, regression tests, relevant gameplay validation and an accurate changelog before the next phase. See CHANGELOG for actual gates, PROTOTYPE02_REQUEST for the complete brief, and VERIFICATION for delivered evidence and limitations.

## Excluded

Restaurant, pool, spa, bar, weddings, conferences, VIP floor, second floor, elevator, employee hiring/wages/skills, crafting, inventory progression, food/hunger simulation, dozens of tools, city exterior, procedural hotel gameplay, online multiplayer, story campaign and complex dialogue. The worker is one queue-capacity constraint, not a staff-management expansion.

## Acceptance boundary

The simulation must show consequences of prior decisions: a noise conflict avoided by a different room assignment, optional portable warmth with electrical demand, repeat tripping under unchanged overload and a natural multi-system risk on later days. Careful play may avoid catastrophe. Three days, settlement, maintenance and a fresh-session reset must remain usable. Automated state/physical paths support this assessment; two-person discussion, pleasant carrying, audio clarity, real-display performance and enjoyable pacing still require human playtests.

