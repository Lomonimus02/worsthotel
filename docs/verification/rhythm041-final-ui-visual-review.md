# 0.4.1 — final Windows UI visual review

Reviewed on 2026-09-27: **23/23 actual player PNGs at 1600×900**. No blocking clipping, overlapping controls, missing overlay, or misleading capacity/power state was found in these captured views. This is an image review, not a human gameplay test.

## Exact run and scope

- Source checkpoint: `1186984`; build: `Builds/Windows-0.4.1`.
- Run folder: [20260927T153109Z-8847164f](rhythm041-final-ui/20260927T153109Z-8847164f/).
- Gameplay assembly SHA-256: `86A2A0216EC27B085CFEC30E27A8E5ED3DF0AA14A9E451891AE4973D72E5FEAC`.
- Executable SHA-256: `ABC8179E345B70C9D7CA423C54E02739ED9B9ADCA668C2C745FFB81AADC05CC6`.
- [Binary identity](rhythm041-final-ui/20260927T153109Z-8847164f/binary-and-scope.txt), [runtime verification](rhythm041-final-ui/20260927T153109Z-8847164f/runtime-verification.txt), [capture manifest](rhythm041-final-ui/20260927T153109Z-8847164f/capture-manifest.txt), [archived hashes](rhythm041-final-ui/20260927T153109Z-8847164f/operations-ui-hashes.txt).
- Runtime scenario: `OperationsUIOnly=True`, `OperationsUIVerified=True`, PASS, errors 0, 23 captures, 35.2 seconds. These results come from the archived runtime report; the visual conclusion comes from opening every PNG individually.

This is an explicitly prepared presentation fixture: cloned cash $4000, controlled sales policies and scheduled booking decisions, labelled model guest/check-in adapters, condition 45, tripped circuit B, dirty room 105, and diagnostic clock advances/viewpoints. Controller input operates the menus; the maintenance sequence includes the actual focused boiler plaque hold. The run does not establish natural balance, a three-day lifecycle, a fresh-session reset, or physical guest check-in. F2 scroll positions are diagnostic capture setup; the images show the real runtime UI, not a composed replacement overlay.

## Individual captures

Every filename below is in the run folder above.

| Capture | Visual observation |
|---|---|
| `01-operations.png` | Six room rows, occupancy/readiness, two rooms open to sales, dated IN/OUT, boiler condition and B TRIPPED fit. The next 06:00 bill and maintenance notes are readable. |
| `02-paid-departure.png` | Occupancy changes to 1/6; room 101 is leaving and its settled departure is absent from the upcoming OUT list. Cash and linen readiness remain visible. |
| `02a-room-sales.png` | All six room policies, OPEN/CLOSED state, rates, traits and readiness fit. Automatic booking and existing-contract wording are readable. |
| `02b-rate-draft.png` | Current $120 and draft future rate $130 are distinct; Apply is available. The existing-contract explanation is not cut off. |
| `02c-rate-applied.png` | Current rate becomes $130, Apply is disabled, and the result message confirms preservation of existing contracts. |
| `03-bookings.png` | Two confirmed tomorrow reservations show actual rooms, dates and fixed agreed prices, totalling $250 before credits/refunds. No ordinary approval step is shown. |
| `04-booking-detail.png` | Guest preferences, dates and agreed $120 remain legible alongside the room preview. Selected-room bullet, disabled unavailable room, forecast and reassignment controls are distinguishable. |
| `05-forecast.png` | Current and estimated demand are separate; typical/one-shower estimates and assumptions fit. `TRIPPED · reset required` remains explicit despite positive forecast circuit reserve. |
| `05a-reassigned.png` | Confirmed room becomes 106 while the $120 agreement remains. Duplicate reassignment is disabled; the result text states dates and price were preserved. |
| `06-maintenance.png` | Basic $350 / 45 minutes and Full $1500 / two hours are readable with different outcomes. Selection is explicitly free; instructions direct the player to close the menu and hold the physical inspection plate. |
| `07-maintenance-active.png` | Full Service, HEAT OFF, completion time and remaining duration are visible. The active-job option is disabled and period maintenance spend is $1500. |
| `08-operations-maintenance.png` | All room trends show Cooling during planned downtime. The overview distinguishes maintenance heat shutdown, circuit B power loss and linen backlog; dated movement rows fit. |
| `09-upgrades.png` | Boiler and circuit before/after capacities, prices and one-branch restriction fit. Text explicitly avoids promising repair or restoring power. |
| `10-upgrade-installed.png` | B is marked installed, the other branch purchase is disabled, cash/spend are updated, and the result still requires a breaker reset. |
| `11-current-finances.png` | Opening $4000, checkout $120, maintenance $1500, capital $1200 and cash $1420 are readable. Unpaid $370 is separated from received money and the next $450 bill. |
| `12-daily-report-list.png` | New period starts with cash $970 and zero new-period spending. Historical report 1 remains accessible, with net −$3030 and one settled stay. |
| `13-report-detail.png` | Historical revenue, refunds, operations, maintenance and capital are all present. Arithmetic is consistent: $4000 + $120 − $450 − $1500 − $1200 = $970. Receipt/review and navigation buttons fit. |
| `14-phone-calendar.png` | Phone uses agreed hotel dates/times, not shift-relative wording. Empty wake-call state and answering-versus-promising explanation are readable. This capture does not show a ringing call or an accepted promise. |
| `15-boiler-readout.png` | Physical PRESSURE dial is distinct from rated/effective capacity, demand, reserve, load and stress. Inspection prompt fits three lines; the boiler-isolation sign is visible at the edge of the chosen viewpoint. |
| `16-electrical-readout.png` | A/B room groups, requested/delivered power, signed reserve and consumer totals are readable. B displays TRIPPED / POWER OFF / UPGRADED with capacity 5; A shows delivered 0.85 of capacity 4. |
| `17-debug-boiler.png` | F2 shows current condition, six attributed demand rows, summed demand, rated/effective capacity and independent pressure. Stress, paid Basic/Full service, cash, condition and forced-load controls are visible. |
| `18-debug-thermal.png` | Full room-101 calculation is readable: current 21.96°C → equilibrium 22.00°C; base + central − loss + powered heater, radiator multiplier and time constant. Load context is explicitly not an extra temperature term. Noise/local-fixture controls and clock options are visible. |
| `19-debug-history.png` | All four recorded infrastructure entries fit, newest first: Full Service completion/start and the prepared B trip/band transition. Times, boiler figures, branch figures and bounded count 4/64 are readable. History scope and diagnostic-marker explanation are visible. |

## Limits and minor presentation observations

- The physical electrical plate uses smaller text than the management screens; it is readable at this captured approach and resolution. Other distances, resolutions, display scaling and split-screen layouts were not reviewed here.
- `18-debug-thermal.png` and `19-debug-history.png` cut neighbouring content at the scroll viewport edges. Their target thermal/history sections are complete; the fixed Close button remains visible. The partial sleep explanation at the bottom of the thermal capture is not evidence of a complete sleep-debug layout review.
- The report writes negative currency as `Net $-3030`. The sign and accounting are unambiguous, although `-$3030` would be more conventional. This does not block use or invalidate the arithmetic.
- Gold outlines represent controller focus; selected room uses a bullet and explicit reservation text. A focused navigation button is not evidence that the corresponding page is selected.
- The history capture explains `[F2]` markers but contains no marked F2 mutation. It does not alone prove the marker rendering or every history-event variant. The prepared direct-model circuit trip is part of this labelled fixture, not evidence of an organically overloaded circuit.
- No source changes were made during this review. Runtime, LAN, complete lifecycle and human acceptance remain separate checks; earlier runs on another gameplay DLL are not silently promoted to this binary.

## Verified image identities

All 23 PNG SHA-256 values were recalculated from disk and match the archived manifest. All hashes are distinct. Distinctness establishes different files; the individual image review above establishes visible UI content.

```text
75c2040b1ae21a884ccc4509827378702f75a57ed0e39266a59e6bba689184a9 01-operations.png
43aa8101205eea23bdcd10b4c29e64e215df6e7959afddfa951a29a264f7a336 02-paid-departure.png
791eea444caa23d6398a1e3d2a104f17565bcdae7340b4b20ff86865ea04ccf7 02a-room-sales.png
4023203007dcadb29e539653e028ba5e0fa06d8ca18af84013f6c9984da7f2f8 02b-rate-draft.png
f23ce86acb7b1f2bf69e850187796e0bb260579d65a9fdc93fe1c316a6a76faf 02c-rate-applied.png
b73da8acde13e4fde24bbd01bf159cf4b02c29a4aa0f527914c5ba8e40089c6e 03-bookings.png
4b99742f9bc691f609b63b907f5635b2eb449f2fc0614f4f0f4291006238a3d0 04-booking-detail.png
de61af1c2a8f1daad92451ef7b5e5c5abd93dc7befa53c2819c60cbec8793b32 05-forecast.png
fffc47a9dba8b232ed47cb4c5f469c2e3d9876f947acbe3979273f9e3668c0b0 05a-reassigned.png
91b3d5077cc5b8aef6aef739eec0993231833bc2c4196a02b33e74826ef7e07c 06-maintenance.png
f1b2934fe7aa8cd44b67fe29efa1f255baf0e06cf9304a20d7081640a927a2a6 07-maintenance-active.png
af5cc0360763ba8a51e242f26dfbff85c6594cee99af98dbc23619538886857c 08-operations-maintenance.png
acbbfcb48d0fab5f36b9ee341a12e767aef66d191a08be9a695e3e80a7023748 09-upgrades.png
52bc1da1dbb5af1c09bd5e4d1186cc20b9e90c739ad55e99c710ceba1ad8e364 10-upgrade-installed.png
b8417c90a99296a8313fa68b8e6b169c26b88f6567ebd723ae46e5a63f881061 11-current-finances.png
fa2ca90151e9f3d753cb23be60eef487fd4f5fbc1dd364781d1657df2fc898fc 12-daily-report-list.png
ba347f1546f10382d46c5014fbb325f0a82791ef6eb74adce87e01b5ad5a6a9e 13-report-detail.png
e88a0e5eaa01ed0a376bef241fe487232ed205607e1fc1367fce5222cc572ea9 14-phone-calendar.png
87b1dbab6106ba0a752a2fa385387e48bf711ba2c2026d0c5596fe2d617c5bb3 15-boiler-readout.png
b2182ba2428b782dcb51aed8ee643594667a257edf3e62012bbbe690148202de 16-electrical-readout.png
c7f0f49f3c8532342c50b433b576d14382cc17fe53c56c7443272ec8f23dfe1c 17-debug-boiler.png
14cd00de4779438428a9537a1f37693999f3a3e5e59401186bb1b19ec64e1e66 18-debug-thermal.png
d8713a0306f6a6ac9f99128be7b2821f18549702370542a45821f6e039b180bc 19-debug-history.png
```
