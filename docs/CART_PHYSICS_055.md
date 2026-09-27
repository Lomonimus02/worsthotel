# Luggage cart physics — 0.5.5

The user supplied a Development Console screenshot of `rigidbody.torque ... { NaN, NaN, NaN }` for the brass luggage cart and explicitly requested gameplay tests. The same error and `LuggageCart.FixedUpdate` call were present in Player.log.

## Diagnosis and change

The original scene/input regression test could drive the cart but measured only 0.016 degrees of turning for a 30-degree look request. Inspection found the automatic principal inertia frame rotated approximately 37 degrees around X, with its X/Z moments locked. That was incompatible with the intended upright cart steering. Unity documents that frozen rotational axes use zero inertia components: [Rigidbody.inertiaTensor](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Rigidbody-inertiaTensor.html).

The cart now uses an explicit chassis-aligned inertia frame and finite chassis moments. Steering applies bounded physical torque in Force mode, avoiding acceleration-mode conversion through locked inertia axes. Steering effort/damping were tuned against actual loaded and unloaded movement, rather than overriding the cart's transform or making it kinematic. Linear damping compensation keeps backward travel aligned with the employee's walking speed.

The regression also exposed different distances for maintaining and releasing the handle. Both now use the same 2.8 m reach once guiding; initial acquisition remains 2.1 m. The existing physical cargo joints, collision, carrying, ownership and host-authoritative behavior remain in use. Both LAN peers need 0.5.5; no wire fields changed.

## Verified scenarios

Two targeted PlayMode tests passed on the actual authored hotel scene with virtual Input System gamepads and real raycasts, CharacterControllers, rigidbodies, colliders and joints:

- Acquire the actual handle, move forward (2.921 m measured), pull backward continuously for four seconds without losing the handle, turn (23.95 degrees measured for a 30-degree request), remain upright and release with ordinary Use input.
- Let two accepted physical suitcases fall onto the platform and secure automatically, drive 3.853 m, steer under load (11.57 degrees for a 20-degree request), retain both cargo joints/relative positions, release the handle, walk around its collider and physically grab one suitcase while the other remains attached.

Both finish with no unexpected Unity log messages, including no invalid-torque/NaN errors. [Baseline failure](verification/cart055/before-results.xml) and [final passing results](verification/cart055/after-results.xml) are retained. Intermediate failed runs led to the inertia, steering and release fixes. One attempt without graphics failed during renderer setup and is not gameplay evidence; the final passing run used the normal graphics-enabled PlayMode runner.

These tests place the cart initially on the real corridor floor. The cargo scenario uses labelled guest/model and initial dropped-bag placement fixtures; attachment, transport, steering and removal then run through production physics and input. They do not claim a manual route from the lobby parking spot, a human keyboard/mouse walkthrough, all hotel door clearances or a two-computer LAN playtest.

Windows package: `Builds/Windows-0.5.5/TheWorstHotelEver.exe`; launcher: `Играть 0.5.5.lnk`.
