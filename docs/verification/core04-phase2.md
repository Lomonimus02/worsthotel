# Prototype 0.4 phase 2 — dated bookings

Gate passed on 27 September 2026.

Production now opts into the continuous calendar. Current source adds today/tomorrow enquiries, single-night reservation intervals, dated physical arrivals, next-morning wake and checkout, and once-only checkout receipts. Future reservations do not overwrite current occupancy/key ownership. Late checkout and relocation recheck upcoming intervals before accepting a conflicting change.

The operations journal supports controller-driven room/price selection, accepting, editing/cancelling future bookings, and viewing nonmodal reports. Guest/service/phone/housekeeping clocks display calendar moments. LAN protocol 7 / model schema 6 carries explicit continuous mode, calendar settings, booking revisions and accounting state. Legacy diagnostics select their old fixture mode explicitly; the normal game uses continuous operations.

Completed checks so far:

- Initial compile found three invalid multi-variable `var` declarations in new schedule tests. Fixed without changing gameplay.
- `Logs/core04-phase2-compile-r2.log`: compile passed.
- `Logs/core04-phase2-edit.xml`: **365/365 passed**, but this run did not include 14 new service snapshot tests: their handwritten metadata GUID had 33 characters and Unity ignored that asset. Root detected the missing test count, replaced the GUID with a generated 32-character value, and queued a new compile/full EditMode run. This 365 count is not a complete phase-2 gate.
- `Logs/core04-phase2-play.xml`: **6/6 passed**, covering the calendar, pause, WAIT and continuous mirror. Four further physical/controller/legacy-mirror scenarios are queued explicitly (their names do not all begin with `Continuous`).

Final gate evidence:

- `Logs/core04-phase2-compile-r3.log`: compile passed after the metadata correction.
- `Logs/core04-phase2-edit-r2.xml`: **379/379 passed**, including all 14 continuous service snapshot cases.
- `Logs/core04-phase2-play-bookings.xml`: remaining **4/4 passed** (43.28 s). Real controller input operated the physical reception terminal, accepted/edited/cancelled a future booking, read a report without repaying it, and retained explicit legacy host compatibility. Physical guest routes reused the six reception/luggage slots for seventh/eighth arrivals without changing an existing guest/body/suitcase.
- `Logs/core04-phase2-play-legacy.xml`: relevant legacy **3/3 passed** (52.36 s): unique physical keys through drop/regrab/reset, SOLO boiler repair controls, and two physical heaters causing overload/retrip until load removal.

All ten new continuous PlayMode cases have passed across the two focused runs, plus the three legacy cases. This is not a fresh full PlayMode suite or a two-process continuous LAN run. JSON/host-mirror/controller coverage is separate from the actual LAN player gate planned with persistence integration.

Known next-phase dependency: guest/service/incident history requires coherent bounded retirement for endless operation. The model currently preserves matching guest/reservation identities rather than discarding an active or referenced record to fit a calendar page. Capacity and maintenance tuning are still intentionally unchanged.
