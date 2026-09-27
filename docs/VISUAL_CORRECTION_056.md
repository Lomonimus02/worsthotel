# Visual correction 0.5.6 — implementation ready, gameplay review pending

This continues the existing 0.5.1 visual work and 0.5.2 service-wing layout. It does not add gameplay systems. The luggage-cart physics and room-access corrections from 0.5.4/0.5.5 are retained.

## Findings and changes

- The current North Gallery already has continuous walls, recessed framed doors, timber panelling, sage insets, a blue-green patterned carpet, rooflights, brass details and a window/seat focal point. Its passage geometry is retained.
- Actual scene renders exposed wall lamps overlapping the pictures in 107/109 and decorative books/cups intersecting bedside lamp bases. All four gallery wall lamps now sit beside their pictures. Desk-end props leave both the service lamp and the guest notebook clear.
- Rooms 107–110 now have different framed subjects (landscape, sailing boat, trees, village), in addition to their existing sage/ochre/blue/burgundy schemes. The existing shower screens gain timber-framed panels without changing their footprint, guest entrance or activity anchors.
- TV screens used emissive window glazing and were omitted from circuit bindings. TVs now use the terminal material; both TV and reception-terminal luminous surfaces follow the existing room/public A/B bindings. Unpowered breaker lenses retain a red surface but no emission. Bedside lamps also respect whether the room is operational.
- The lobby reflection probe previously captured once on startup. It now refreshes after an actual A/B power-state change, so powered reflections do not persist during an outage. It does not refresh continuously.

## Lighting audit

The hotel uses realtime practicals and exterior spots, with no mixed lights or baked scene lightmaps contributing to these views. Forward+ avoids the old per-object light-selection limit. Soft practical shadows and non-shadow-casting translucent fixture proxies remain in place. Bloom is 0.035 with a 1.5 threshold and Neutral tonemapping. The lobby has one low-intensity (0.08), 128-pixel realtime reflection probe.

The flat ambient fill (0.22, 0.24, 0.29), weak directional bounce and visible window/rooflight spill are unchanged. No emergency lamps or generator were added. A remains west/odd rooms; B east/even rooms and the east service wing. Exterior glazing sources are excluded from both circuits.

## Visual review actually performed

Opened and rendered the authored Unity scene with the existing capture utility. Inspected the gallery, all four new-room interiors before/after the prop corrections, powered reception/main corridor, A-off/B-off/both-off main-corridor previews, and the gallery with both circuits off. The temporary capture poses and lighting-preview edits were discarded; no automated tests or new test infrastructure were added or run.

In these rendered views the switched-off corridor side is visibly darker. With both sides off, cool glazing spill keeps doors, wall profiles, the carpet and corridor direction visible. The gallery end window remains a clear focal point. Room pictures and desk props no longer overlap their lamps.

The lighting previews disabled the actual authored circuit light/surface bindings in the editor. They do **not** exercise breaker gameplay, the simulation, reflection transitions, room entry or cart movement. Static scene renders are not accepted as a first-person walkthrough.

Retained images: `docs/screenshots/visual056-lobby-on.png`, `visual056-hall-on.png`, `visual056-hall-outage-1.png` (A), `visual056-hall-outage-2.png` (B), `visual056-hall-outage-3.png` (both), `visual056-wing-before.png` (powered gallery), `visual056-wing-off.png`, and `visual056-107-after.png` through `visual056-110-after.png`.

## Remaining acceptance

Scene regeneration, C# compilation and the Windows 0.5.6 build succeeded (`Logs/visual056-scene.log`, `Logs/visual056-build.log`). The player is `Builds/Windows-0.5.6/TheWorstHotelEver.exe`; its complete portable archive is `Builds/TheWorstHotelEver-0.5.6-Windows.zip`. The matching-build LAN string was updated; the wire format is unchanged.

Actual Windows gameplay inspection is blocked by the Windows Firewall permission dialog covering the launched 0.5.5 game. The user was asked to dismiss it; Computer Use must not act on security permission prompts.

After that, use the 0.5.6 player for one deliberate walkthrough: reception/corridor powered, A off, B off, both off; restore North Wing from reception, inspect the reveal, walk its full length and enter 107–110, push the luggage cart through, and view the wing during an actual power loss. Check reflection/terminal/lamp transitions in that same session. No gameplay acceptance, cart-traversal success or full completion of this visual pass is claimed yet.
