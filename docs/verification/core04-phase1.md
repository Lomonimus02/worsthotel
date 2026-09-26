# Prototype 0.4 phase 1 — continuous calendar

Gate passed on 27 September 2026.

- Compile: `Logs/core04-phase1-compile.log`, successful.
- Full EditMode: **338/338 passed**, `Logs/core04-phase1-edit.xml`; includes 22 new calendar/model scenarios.
- Relevant PlayMode: **5/5 passed** in 16.65 s, `Logs/core04-phase1-play.xml`.

The calendar is a view of the existing model clock. Midnight changes the date; the separately configured report boundary closes accounting once, retains bounded report history, and never calls the old shift reset. Empty receipt periods do not alter reputation. Explicit legacy fixture mode remains available. Extreme synchronous diagnostic advances are bounded before work.

Model scenarios cover default/custom calendar dates, exact and fractional report crossings, a single tick through several days, six reporting periods with capped history, no duplicated costs, failed boiler/tripped circuit/heater placement and switch/held linen and key/dirty room persistence, ordinary temperature evolution, read-only mirrors and invalid time/configuration. These are labelled model fixtures, not claims of physical pickup or natural overload balance.

The five real GameSession scenarios use a cloned opt-in calendar configuration and normal rendered Update frames, UI and virtual gamepads. They verify that a report preserves the running model and a failed boiler, leaves the ledger closed or preserves its existing owner, resumes ordinary pause without elapsed-wall-time catch-up, and stops real two-person WAIT at the report event while keeping normal physics timing. A separate diagnostic-advance test checks its finite horizon.

The production configuration remains legacy at this intermediate phase; activation follows dated booking integration in phase 2. Capacity tuning, guest schedule changes and continuous LAN snapshot migration have deliberately not been folded into phase 1. Short ordinary pause coverage does not establish a fix for the previously reported long Alt+Tab hang.
