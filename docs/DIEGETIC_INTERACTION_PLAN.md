# Embodied interaction — implementation map

Default future rule: information belongs to its physical source; add a HUD only when the world cannot reasonably communicate it.

| Existing element | Decision | Destination / behavior |
| --- | --- | --- |
| Shift HUD, actor/session badge, idle controls, idle waiting strip | REMOVE from normal play | Exact statistics remain in the opt-in developer overlay; waiting feedback appears only during waiting. |
| Crosshair and interaction box / verbose prompts | KEEP / CONVERT | Tiny dot and short physical verbs, only at a reachable target. Optional enhanced prompts. |
| Reception terminal and automatic management popup | CONVERT | Physical reservation ledger, focused on arrivals, checkouts, availability and sales. |
| Cash and revenue / expense screens | CONVERT | Accounting book beside reception. |
| Service board and task/status text | CONVERT | Reception notepad containing only communicated requests and explicit promises; completed entries crossed out. |
| Phone popup and wake-call status label | CONVERT | Ringing phone, immediate answer, lifted handset, compact conversation subtitles and replies. |
| Door / guest popups, overhead guest and employee information | CONVERT / REMOVE | Actual knock and direct conversation; brief subtitles and response choices. Preserve room-entry permissions. |
| Boiler floating statistics and procedure boards / inspection menu | CONVERT | Existing pressure gauge, warning lamp and sound; local load indication and physical maintenance manual. Preserve physical repair controls. |
| Electrical HUD / explanatory boards | REMOVE / CONVERT | Physical breakers, A west / B east labels and local load indicators. Preserve lighting and blackout behavior. |
| Upgrade menu / expansion purchase | CONVERT | Physical renovation ledger; price based on actual economy, unaffordable at spawn. |
| Time display | CONVERT | Working lobby, staff-room and boiler-room clocks. |
| Room status plaques and appliance state text | CONVERT | Plausible room numbers / local labels; actual lamp, valve, linen and heater state. |
| Luggage / cart explanatory signs | REMOVE / CONVERT | Recognizable cart and small physical luggage tags. Preserve cart physics and key tags. |
| Pause, session setup, debug and accessibility | KEEP | Menus only when explicitly opened; subtitles and prompts configurable. |

Implement in this order: clean HUD and prompts; phone / conversations; physical books and memory; machine presentation and clocks; signs and economy. Keep simulation and authority checks intact. Validate with a compile, scene build, focused checks and one representative play session, without a new test framework.
