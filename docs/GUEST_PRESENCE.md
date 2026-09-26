# Guest presence and SOLO — 0.2.2

The authoritative request is [GUEST_PRESENCE_REQUEST.txt](GUEST_PRESENCE_REQUEST.txt). This pass continues the existing hotel, physical keys, player linen turnover, noise counterplay and direct-IP LAN. It adds a small set of staged guest activities and a one-person testing mode; it does not add employee AI, relationships, dialogue trees or another needs simulation.

## Guests and their rooms

Each authored room has a BedAnchor, floor BedApproach, ShowerAnchor, RestAnchor, DeskAnchor and inside/outside doorway anchors. Routes stay in the hotel's authored clear lanes. A horizontal bed pose follows a short approach/settling transition; waking restores the upright body before walking. Showering uses the enclosed corner and privacy curtain, with the guest concealed, running water, steam and local sound. The upright interaction capsule and the guest's visible key are hidden with the concealed body.

Scene guests register physical activity staging with the existing hotel model. Reaching an activity anchor and completing its brief transition starts the actual activity timer and output. A guest walking to the shower therefore does not already consume shower hot water. Pure model test adapters remain explicitly separate from physical navigation. Guest audio and electrical activity use the staged source. LAN transfers body poses and visibility as well as shower feedback; only the host acknowledges arrivals.

The room records its assigned guest, occupancy, privacy and actual door state. Guests open the physical door for passage and close it after crossing. Closed occupied doors use a knock followed by a contextual response: ask for quiet when loud activity is audible, otherwise ask permission to enter. Sleeping and showering guests refuse ordinary entry. Staff already inside can open the door to leave. Deliberate emergency access uses the secondary action, followed by a three-second primary hold. An interrupted hold does not complete access.

Room ownership survives a temporary departure. Checkout and relocation retain the existing room/key lifecycle and dirty-linen work. Navigation uses bounded local detours and nearby clear recovery points, preserving the room side and doorway checks. Recovery does not send a fictitious arrival callback.

## SOLO

The main menu offers SOLO, HOST and JOIN BY IP. SOLO creates one staff object, one full-screen camera and one input assignment. There is no inactive second employee or AI teammate. The old split-screen fixture is an explicit development option. Returning from LAN restores the one-person menu.

The same hotel simulation, bookings, economy, heating, electricity, complaints, keys and finite linen stock run in SOLO. WAIT needs the actual solo player's consent; it never fabricates a vote for actor1. NewGame retains the selected mode.

During a SOLO boiler failure, hold the real relief valve until pressure is in the safe band and the mechanical catch charges for **2 real seconds**. The catch then holds pressure for **18 active real seconds** while the player releases the valve, walks to the panel and performs the existing panel → breaker → latch A → latch B → restart sequence. Expiry, pause, device loss and session replacement clear the catch. Multiplayer keeps the simultaneous two-person valve requirement. Both timings are in `SoloAssist.asset`; no economy or patience multipliers are scattered through gameplay.

## Diagnostics and proof boundaries

F2 shows the selected guest's state, activity, destination, assigned room, privacy, route status and next scheduled activity. Force Sleep, Shower, Rest, LeaveRoom and ReturnRoom request ordinary movement/transition paths. The panel remains a development tool in local modes and is disabled during LAN.

`tools/VerifyPlayer.ps1 -Solo -PresenceFixtures -OutputPath docs/screenshots/presence-solo` creates one owned synthetic controller. A disposable preliminary hotel exercises forced guest staging and records actual GPU images; it is then reset before the separate three-day diagnostic tour. The tour uses labelled MODEL key and linen commands, real guest routes and the production update loop. It does not claim a human physical linen route or a three-day human playtest. Physical controls are checked separately by PlayMode.

The Windows 0.2.2 build and its one-person three-day diagnostic tour passed, including real guest staging and a fresh-session reset. Nine representative GPU images were opened and accepted, including sleep, enclosed shower, locked room and the extended guest debug panel. Exact gate/build/runtime evidence and the remaining human/physical-machine limits are recorded in [VERIFICATION.md](VERIFICATION.md). The prior 0.2.1 evidence is preserved in [history/VERIFICATION-0.2.1.md](history/VERIFICATION-0.2.1.md).
