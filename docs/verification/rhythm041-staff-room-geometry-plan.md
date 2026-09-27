# 0.4.1 phase 7 — staff-room geometry plan

2026-09-27. **Read-only source audit and proposed authoring; not implemented or physically tested.** Prepared while phase 5 sources are frozen. The agreed compact niche is inside the existing utility room; there is no annex or new hotel wing. Scope follows request sections 30–35 and phase 7. Sleep state, consent, clock advancement and wake policy remain separate runtime work.

## Exact layout

All coordinates are world metres, with floor height `y=0`. The existing utility floor spans `x=-7..7`, `z=29..40`; the actual west wall inner face is `x=-6.68`. Use the agreed niche `x=-6.68..-3.80`, `z=32.80..36.00`.

| Element | Placement and bounds |
|---|---|
| South/north partitions | Centre `z=32.80` / `36.00`, thickness `0.12`, from existing west wall to east partition. Clear inside `z=32.86..35.94`. |
| East partition | Centre `x=-3.80`, thickness `0.12` → `x=-3.86..-3.74`. Entry clear between `z=33.70..35.10`, width `1.40`; lintel underside at least `y=2.25`. No swinging leaf. |
| South bed | Centre `(-5.40,33.32)`, total footprint `2.00 × 0.82`, long axis X, head west → `x=-6.40..-4.40`, `z=32.91..33.73`. |
| North bed | Centre `(-5.40,35.48)`, same total footprint → `x=-6.40..-4.40`, `z=35.07..35.89`. |
| Aisle | `z=33.73..35.07`, width `1.34`; keep entirely free of furniture and collision. |
| Table and mug | Centre `(-4.11,33.23)`, footprint `0.40 × 0.55`, tabletop height `0.68` → `x=-4.31..-3.91`, `z=32.955..33.505`. Mug decorative, not a new pickup. |
| Lockers | Centre `(-4.11,35.49)`, footprint `0.40 × 0.66`, height `1.60` → `x=-4.31..-3.91`, `z=35.16..35.82`. |
| Staff standing anchors | `(-5.60,0,34.10)` and `(-4.65,0,34.68)`, 1.11 m apart. These are clear approach/wake positions, not a requirement to teleport players during use. |
| Entry approach | `(-2.50,0,34.40)`, facing west into the niche. |

`FirstPersonController.Initialize` uses height `1.78`, radius `0.30`, skin width `0.035`, step offset `0.28`. Both anchors fit without overlap. Conservatively adding the skin width to the radius leaves the tighter south-bed clearance `0.035` and north-bed clearance `0.055`; do not add blanket collision, bed rails or protruding trim into that aisle. Two inflated capsules still have about `0.44` m between them. Bed ends leave `0.28` m to the existing west wall; their front/back clearance to the new partitions is `0.05` m.

## Builder and visual choices

- Add one new editor partial, proposed `Scripts/Editor/StaffRoomBuilder.cs`, with `AddStaffRoom(GameObject gameplay)` called once from `PrototypeGameplayBuilder.BuildGameplay` after `AddElectricalPanel`. Keep the staff assembly under Gameplay, alongside existing interactive fixtures. No changes to guest-room volumes, room bindings or hotel capacity.
- Reuse `HotelKitAssets.Group/Box/Cylinder/Sphere/Text` and existing `Cream plaster`, `New plaster patch`, `Mahogany`, `Walnut panels`, `Cream linen`, `Teal upholstery`, `Aged brass`, `Warm lamp` materials. A small repair notice, scuffed wood accents and two slightly different folded teal blankets establish the used back-of-house look without new asset production.
- **Do not use `WallRun` for the 0.12 m partitions.** `PrototypeSceneBuilder.BuildKit` gives its thick-plaster object scale Z `0.32` but collider size Z `2`: its actual wall thickness is `0.64`. Author thin partitions with explicit Box sizes and their ordinary unit colliders. Existing utility walls remain unchanged.
- Author simple compact cots from the existing primitive kit. The guest-bed prefab is not a 2 × 0.82 m cot: its base, duvet and headboard all differ in size, with the headboard `2.24` m wide. Every new cot part, including headboard and covers, must remain inside the stated total footprint. Keep headboards low enough that the room remains visually modest.
- Existing floor and ceiling suffice. No raised floor or threshold. Optional rug is decorative, without a collider, slightly above existing grout. The notice and entrance sign use the existing non-colliding Sign/Text style. A modest warm fixture can use the existing utility-light convention; it must not implicitly introduce a new electrical consumer or room-lamp binding.

## Preserved routes and surfaces

| Existing fixture | Current authored geometry and constraint |
|---|---|
| Repair workbench | Centre `(-5.30,32.00)`, top footprint `2.25 × 1.02`; back edge `z=32.51`. New south partition begins `32.74`, leaving a 0.23 m dead gap. Preserve workbench access from its existing south side; do not treat the gap as a walkway. |
| Linen / hamper | Linen centre `(-3.00,30.85)` and hamper `(-4.45,30.65)` stay south of the niche. No movement or stock-layout change. |
| Blanket shelf | Centre `(-1.15,31.00)`, width `1.62`, depth `0.62`. Approach from the central utility spine, not diagonally through this shelf. Suggested route: corridor → `(0.50,33.00)` → `(-2.50,34.40)` → staff entry. |
| Boiler relief valve | Actual control is `ValveAnchor=(-3.10,1.55,36.52)`, not a room radiator. Its use box spans `x=-3.60..-2.60`, `z=36.40..36.64`; east partition ends at `x=-3.74`, north partition at `z=36.06`. Preserve the staff approach along `x=-3.10`; a radius-plus-skin capsule there clears the east partition by `0.305` m. |
| Boiler body and service controls | Foundation begins `z=36.70`, `x=-2.675..1.475`. Readout, inspection plate, service panel, isolation, latches and restart remain in their existing positions and reachable from the central utility space. |
| Heaters / bulb stock | Heaters `(3.80,31.40)` and `(2.60,31.40)`, bulb shelf `(6.20,32.20)` are east of the staff route. Keep both heater pickup paths unchanged. |
| Room electrical panel | Centre `(4.90,35.30)`; cover hinge `(3.80,1.75,34.81)`, open angle 105°. Open cover reaches approximately `(3.23,32.69)`, well east of the niche and central approach. Preserve cover travel and both breaker faces. |

Sources: `PrototypeSceneBuilder.BuildShell/BuildUtility/BuildKit`, `LinenStorageBuilder.AddLinenStorage`, `ServiceLayerBuilder.AddServiceLayer`, `PortableHeaterBuilder`, `ElectricalPanelBuilder.AddElectricalPanel`, `HotelKitAssets`, and `FirstPersonController.Initialize`.

## Interaction and verification requirements for implementation

The bed interaction component should sit on each cot root so the first struck bed collider resolves to that bed. `PlayerInteractor.UpdateFocus` deliberately selects the nearest physical surface, with reach `3.1` m and triggers ignored. Avoid a large invisible use box spanning the aisle or passing through a partition. Decorative pillows/blanket folds should either be non-colliding or children of the same interaction root. Derive any public interaction point from its transform/local target rather than `Collider.bounds` on a disabled LAN replica.

After authoring, root should rebuild the scene and verify actual controller traversal from the corridor through the entry to both bed targets, both actors present without body overlap, and an unobstructed return to the relief valve. Verify that an outside-wall ray does not activate either bed. Keep actual first-hit collider assertions. Check workbench/linen/blanket pickup, heater pickup, boiler inspection/repair control reach, and open-panel breaker reach after the new geometry exists. Capture the actual built player from the entry and aisle to check scale, sign readability, warmth and clutter; this source-derived clearance audit is not a visual or physical PASS.

No Assets, scene, runtime, process or Unity changes were made for this plan.
