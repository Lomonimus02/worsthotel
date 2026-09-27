# Visual correction 0.5.1

## Lighting audit and correction

The generated hotel has realtime lights, no scene lightmaps or mixed-light contribution. Flat ambient is explicitly supplied by `HotelAmbientLighting`; it remains unchanged. The lobby uses a weak, once-captured realtime reflection probe. A is the west/odd-room side; B is east/even, including their public practicals.

The previous setup combined hard point-light shadows from opaque lamp/glass proxies, narrow downward corridor spots, saturated warm light, ACES and Forward rendering's eight-light limit on shared floor/ceiling meshes. The first rendered comparison after correction shows readable material colour and removes the large ceiling silhouettes around the lobby pendants.

Changes: Forward+ light selection; soft practical shadows; translucent fixture proxies no longer cast opaque silhouettes; less saturated practicals, reduced lamp emission/bloom and Neutral tonemapping. Room, wall and furniture shadows remain. Visible rooflights and exterior windows provide weak cool spill independent of electricity. These lights are explicitly excluded from circuit bindings. Both practical illumination and luminous surfaces still follow their existing A/B bindings. Tripped breaker indicators no longer cast a bright light into the room.

The first rendered scene exposed shadow-atlas overflow after adding window sources. Exterior sources now use a single inward-facing spotlight shadow map instead of six point-light maps. The final runtime review must confirm the resulting appearance.

## North Gallery

Restoration removes the existing physical barrier. A framed threshold, continuous corridor walls, sage insets, travel prints, brass details, overhead trim and a blue-green carpet connect the four existing rooms. A curtained window, seat and plant provide the corridor's end composition. Room 107–110 coverlets, upholstery, curtains, pictures and bedside objects vary. Small bed offsets are applied to the corresponding guest anchors too.

Wall decoration is non-colliding; the central aisle is preserved. The end seat/plant sit beyond the last doors. Luggage delivery zones, wardrobe locations, cart, guests, sales, electricity and progression rules are retained. No gameplay systems or automated tests/harnesses were added.

## Review record

- Scene generation and C# compilation succeeded.
- Inspected actual editor-rendered reception, main corridor and utility views. Reception/corridor materials read clearly; large pendant silhouettes are gone; utility has cooler illumination.
- Final Windows gameplay review of A off, B off, both off, restored wing, four rooms and cart traversal: pending assisted manual walkthrough.

This record deliberately does not treat geometry dimensions or disabled lights as proof of gameplay/blackout acceptance.
