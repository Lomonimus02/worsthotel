# Prototype 0.2: Phase 10 diagnostic controls

F2 is available in the Editor and development builds. Opening it pauses play. These commands modify the current model only; they do not save changes to configuration assets.

## Circuit demand experiment

Each circuit displays two distinct measurements:

- **Actual consumers:** the sum of real registered guest/device demand, and the amount those consumers receive while the circuit has power. Individual consumer IDs and their requested/delivered loads remain listed.
- **Diagnostic total override:** an optional total used by the breaker warning/trip calculation. This is explicitly labeled as a diagnostic value, not the physical consumer sum. Setting it does not create a fake device, rescale real consumers or supply heat to an unplaced heater.

Enter a finite, nonnegative total and choose **Set override**. Sustained demand above the configured capacity follows the ordinary warning and trip timers. Repeating the same value neither advances time nor creates another state-change event. Unknown circuit IDs, negative values, NaN and infinity are rejected before state changes.

**Clear override / use actual consumers** immediately restores the current real total, including any devices or occupants changed while the override was active. A now-safe total clears the continuous-overload timer and warning. An existing trip remains: use the regular breaker reset, and reduce remaining demand if it would trip again. Resetting a breaker while an overload override remains active restores power only until the same overload trips it again.

An override persists across ticks and days in that diagnostic session until cleared. New Game constructs a new electrical model with no override. Runtime state is not written back to `ElectricityConfig` or another asset.

The public model contract is `HotelSimulation.DebugSetCircuitLoad(circuitId, float? total)`; `null` means clear. `ElectricalCircuit.LoadOverride` identifies the diagnostic mode, `ActualRequestedLoad` / `ActualDeliveredLoad` describe real consumers, and `RequestedLoad` / `DeliveredLoad` are the effective breaker-test totals while overridden. Outside a diagnostic override, both pairs agree.

## Portable heater switch

F2 now offers **Switch ON/OFF (staff 1)** for each registered portable heater through `GameSession.SetHeaterSwitch`, the existing gameplay command. ON requires Service and a valid physical room placement; a held heater or one outside a room cannot be switched on through this control. OFF remains available. The panel does not assign a room, teleport the object or force circuit power.

A switch can remain ON during a real power cut. Demand remains visible while delivered heat is zero; resetting a still-overloaded breaker can therefore trip it again. Carrying the real object clears its placement through the existing physical adapter, giving zero heat and demand until placement is valid again.

## Verification boundary

[ElectricalDebugTests.cs](../Assets/_WorstHotel/Tests/EditMode/ElectricalDebugTests.cs) checks actual-versus-diagnostic accounting, immutable consumer snapshots, warning/trip/reset/retrip behavior, restoration of current demand, atomic invalid-command rejection, no clock advance, no asset mutation and clean state in a fresh model. It also proves that a diagnostic total cannot create heat from invalid heater placement.

The F2 heater button uses the normal public command rather than a separate simulation shortcut. Existing portable-heater PlayMode coverage owns physical carrying, placement and switching behavior. The final Phase 10 gate and visual F2 inspection are recorded by the main verification report; this note does not treat newly written tests as passed before that gate runs.
