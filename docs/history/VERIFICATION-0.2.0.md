# Verification record — Prototype 0.2 Living Hotel

Workstation: Windows, Unity 6000.3.2f1, URP 17.3.0 and Input System 1.17.0. This records observed local results, not minimum hardware or performance promises. The old Prototype 0.1 delivery and its 77/7 counts remain historical entries in CHANGELOG; they are not evidence for this update.

## Sequential gates

Each completed phase compiles before the next phase begins and runs the complete EditMode and PlayMode suites. XML evidence is archived under `Logs/p02-phaseN-*`; Phase 3 uses the final/route suffixes listed in the acceptance audit. Zero skips are required.

| Phase | EditMode passed | PlayMode passed | Scope |
| --- | ---: | ---: | --- |
| 0 baseline | 77 | 7 | Existing project and behavior |
| 1 | 77 | 11 | Damped carry physics and wall contacts |
| 2 | 91 | 15 | Central clock and independent WAIT votes |
| 3 | 101 | 17 | Arrival, key hold, physical routes and activities |
| 4 | 113 | 18 | World-state needs and situations |
| 5 | 124 | 19 | Acoustic adjacency and physical quiet request |
| 6 | 136 | 22 | Relocation, compensation, ignore and heater |
| 7 | 148 | 24 | Power consumers, circuits and real breaker controls |
| 8 | 163 | 27 | Departure locks, cleaner movement and preparation |
| 9 | 165 | 27 | Production living balance and natural counterfactual |
| 10 | 175 | 28 | Audio, feedback, debug controls and final polish |

The tests use actual Input System virtual-device events, ordinary interaction raycasts, doors and dynamic bodies where those behaviors are under test. They do not establish physical-gamepad compatibility or two-human usability. Model tests use explicit boundary callbacks and must not be described as physical navigation tests.

## Causal and balance evidence

`LivingBalanceTests` loads the production assets and runs both measured three-day policies with normal seeded activity schedules. It includes timed staff/check-in/travel boundaries and normal housekeeping durations. No forced failures, activities, room temperatures, noise or electrical load are used. It protects a manageable first day, a solvent four-guest route, concurrent independent later-day causes and sub-minute gaps between model events. Full methods, exact results and limitations: [PROTOTYPE02_BALANCE.md](PROTOTYPE02_BALANCE.md).

`LivingEmergenceTests` compares the same five day-two guests with a heater, without it, and with one booking distributed onto the other circuit. A genuine cold complaint leads staff to choose heat; temperature dissatisfaction improves before overload cuts power and creates a secondary guest's room-condition case. The comparator also measures overheating if the powered heater is left on. Evidence: `Logs/p02-phase9-editmode.xml`, `Logs/living-emergence.txt`, [PROTOTYPE02_EMERGENCE.md](PROTOTYPE02_EMERGENCE.md).

The legacy constant-demand balance/cadence tests remain regression checks. Their old timings do not describe current living schedules. Model events do not prove meaningful human decisions every 20–30 real seconds; that is a playtest question.

## Windows player — verified 25 September 2026

`Builds/Windows/TheWorstHotelEver.exe` was rebuilt successfully from the final source (`Logs/p02-windows-delivery.log`). The actual player reports ApplicationVersion **0.2.0** and Unity **6000.3.2f1**. `tools/VerifyPlayer.ps1` returned exit 0 with **Outcome=PASS Errors=0 ResetVerified=True**. Evidence: [runtime report](screenshots/runtime/runtime-verification.txt), [current capture manifest](screenshots/runtime/capture-manifest.txt), `Logs/player-verification.log`.

The opt-in `-verifyHotel` driver ran production session updates with actual guest and housekeeper routes, **4/6/6 checked-in, physically arrived and paid stays**, one physical relocation, **11 physical cleaning arrivals and completions**, three reports and a full new-session reset. Minimum time spent in rooms was 191.2 / 165.8 / 171.0 hotel seconds across the three days. It uses synthetic staff devices and a labelled developer 8x clock; this is not the two-vote WAIT implementation. Key commands wait at least five hotel seconds after physical reception arrival; the real held-raycast interaction is covered separately in PlayMode. The driver positions cameras and places the heater explicitly for diagnostics; heater carrying is covered separately in PlayMode.

The real heater and three B occupants produced a warning and trip with requested demand 4.80 against capacity 4.00. Delivered heater heat became zero while A remained powered; heater switch-off followed by reset retained stable power. Room 106 was naturally cold before placement; during the powered interval its measured temperature rose from 20.96 to 24.40°C. This single observation is not a thermal counterfactual; the separate Phase 9 comparisons provide that evidence. The boiler failed naturally on day 2 and remained failed after deferred maintenance into day 3.

This diagnostic policy intentionally tolerates poor placement and unresolved complaints. Its gross/refunds/net were $1110/$225/$435, $1860/$720/$690 and $2000/$955/$595; final cash $1870 and reputation 46.73. These are delivery-run observations, not replacements for the controlled balance-policy comparisons. Runtime 181.9 seconds on GTX 1650 SUPER at 1600×900 is hidden-window diagnostic duration, not an FPS benchmark.

After the Phase 10 gate, the full PlayMode suite was repeated: **28/28 passed**, zero failed/skipped (`Logs/p02-final-ui-playmode.xml`). The final build additionally includes a display-only background under the HUD overflow line; its actual rendered result was inspected. F2 now suppresses the ordinary pause card while its own panel is visible, while retaining device/focus-loss notifications. The diagnostic pad layout supports background input without altering physical devices or the global Input System policy. Its menu navigation renders the original ledger, reads real controls, then verifies D-pad focus and confirmation through the owned synthetic gamepad.

## GPU capture method

`VerificationOffscreenCapture` submits actual player cameras to URP RenderTextures, composes their real split-screen viewports and submits native low-level IMGUI overlay rendering before GPU readback. This is the application's live world and GUI, not a separately drawn mockup. The opt-in helper restores camera settings and is restricted to Editor/development builds.

Only fresh nonblank images in the current `capture-manifest.txt` qualify for inspection. Captures must then be reviewed for overlap, labels, missing materials and useful framing. File existence or a nonblack-pixel test alone is insufficient. An offscreen image establishes the captured appearance at that resolution, not ordinary visible-window performance or comfortable legibility at a real viewing distance.

All **19 current-run images** were opened and visually inspected at 1600×900: planning for each day, both F2 views, split-screen, service ledger and guest detail, both preparation pages, heater, overload/tripped panel, three settlements, maintenance, results and fresh session. No blocking text overlap or missing-material rendering was found in these views. The F2 pause cover and low-contrast HUD overflow text found in earlier attempts were fixed and recaptured. The close diagnostic breaker framing places upper cabinet labels beneath the HUD; the switch positions and lower load/state readouts remain visible, and ordinary viewing-distance usability remains a manual check.

Representative current captures: [planning](screenshots/runtime/planning.png), [guest conditions and choices](screenshots/runtime/guest-detail.png), [physical heater](screenshots/runtime/portable-heater.png), [tripped circuit](screenshots/runtime/electrical-tripped.png), [three-day results](screenshots/runtime/results.png). Earlier 0.1 captures are archived under `Logs/prototype01-runtime-before02/`; unreferenced old PNGs were removed from the current evidence folder by archiving them separately. They were not used to accept 0.2.

## Reproduce

Close another Editor instance using the project, then run `tools/PhaseGate.ps1 -Phase local-check -RegenerateScene`. Run `tools/Unity.ps1 -Task Build`, then `tools/VerifyPlayer.ps1`. Test logs/XML remain under `Logs/`; the built-player report and current-run image manifest are under `docs/screenshots/runtime/`. The verifier starts a hidden window, has a bounded timeout and stops only the player it launched.

## Human verification boundary

Use [HOW_TO_PLAY.ru.md](HOW_TO_PLAY.ru.md) and [PROTOTYPE02_MANUAL_TESTS.md](PROTOTYPE02_MANUAL_TESTS.md) for an ordinary-input session. Still assess two physical gamepads and keyboard+gamepad, audible mix and sound localization, carrying comfort, split-screen viewing distance, natural communication, repeated WAIT voting, preparation pace, recovery from several complaints and normal rendered performance. Automated tests and diagnostic captures do not answer whether the cooperative decisions are enjoyable.


