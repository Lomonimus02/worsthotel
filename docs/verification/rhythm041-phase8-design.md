# Readability phase8 — prepared contract

Implemented after phase7's gate; verification is recorded separately in rhythm041-phase8.md. Follow the user request §§46–51,75–76.

## One actual thermal calculation

Current `RoomSystem.TickTemperature` computes float baseline = TemperatureBase + HeatTemperatureGain × clamp(boiler output) × radiator multiplier − room heat loss. With no supplement it uses the existing float interpolation exactly; with powered supplemental heat it uses double target/integration and clamps to finite float range. The production time constant is45. Guest heating demand is a real load context, not an extra temperature term.

Extract a pure readonly `RoomThermalBreakdown` used by the actual temperature tick and by the query/UI. Preserve old arithmetic and zero-supplement behavior. Expose current/equilibrium target, base, central contribution, radiator setting/gain, room loss, powered supplement, actual boiler availability and separate space/shower load. The hotel query uses `Heaters.HeatForRoom` and existing attributed demand, including away owners; it never advances or consumes anything.

Player surfaces show a short cause and warming/cooling/steady direction, current temperature, weak room and radiator/boiler availability. Keep formulas and full components in F2. Existing radiator tint, thermometer, boiler lamp/hum/readout and circuit warning presentation should consume those same factual terms; inspect actual pixels before claiming readability.

## Bounded causal history

Host-local diagnostic history, at most64 entries. Typed infrastructure transitions: boiler operating band, critical stress enter/leave, actual failure/recovery, service start/completion/patch, radiator change, heater placement/switch/powered state and circuit band/warning/trip/reset. Include absolute hotel time/entity and measured causal context. Record only changes after coherent ticks/successful mutations; no query/no-op/failed-command spam, no guest-activity spam, and no `SignalEvent` calls that would alter WAIT or sleep.

History survives reports in the same hotel and clears with a fresh model. F2 already refuses active LAN; do not add a gameplay snapshot field/protocol burden for a host-only debugging derivation. Mirrors do not append. Label diagnostic-origin changes explicitly rather than inferring them from state. Preserve real failures and prior warnings in the bounded order.

## Developer controls

Preserve current cash, condition/load, failure, temperature/noise, radiator, circuit load/trip and bounded time advance tools. Add stress (finite0–1, no implicit repair), ordinary paid Basic/Full begin, completion by ticking to the real deadline, explicit forward calendar hour/morning advance, thermal breakdown/history, sales-open policy and a labelled next-enquiry demand override. A forced enquiry still consumes its real unique due cursor and checks availability/price; never silently refill all rooms on a policy click.

Inspect guest satisfaction/early-departure hooks before adding them. Early eligibility must preserve the normal warning/grace/physical exit and once-only receipt path; no fabricated Committed state or bill. Any direct sleep diagnostic must be clearly labelled, local-only and separate from physical consent acceptance evidence; normal gameplay retains real beds and two LAN consents. Critical wake debug uses a labelled actual infrastructure fault command and the same observer.

## Verification/ownership

Life: shared thermal data/calculation/query/history plus focused arithmetic/purity/history tests and isolated diagnostic-bed test. Root: F2 controls, command origin, diagnostic SOLO bed helper, integration and gate runs. Interface: normal operations/physical labels and causal presentation checks. Situations: guarded debug model commands and tests; independently prepared phase9 automatic-sales/economy strategy matrix, after ordered phase gates permit implementation.

Tests must preserve exact old no-heater arithmetic and double heater clamp; tripped branch removes supplemental heat without stopping central heat. Querying host/mirror must leave time/temperature/load/events/history unchanged. History must record bounded actual causal transitions, retain through a report and ignore failed/no-op/mirror mutations. Existing UI/controller regressions remain required, followed by fresh EXE/pixel gates in phase9.
