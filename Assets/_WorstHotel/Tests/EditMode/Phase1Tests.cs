using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

namespace WorstHotel.Tests
{
    /// <summary>Structural tests inspect the generated scene; they never regenerate/overwrite it.</summary>
    public sealed class Phase1SceneTests
    {
        private const string ScenePath = "Assets/_WorstHotel/Scenes/PrototypeHotel.unity";
        private Scene scene;
        private Scene previousActive;
        private bool openedByTest;

        [OneTimeSetUp]
        public void OpenGeneratedScene()
        {
            Assert.That(File.Exists(ScenePath), Is.True,
                "Generate PrototypeHotel through Tools > Worst Hotel > Build Prototype Scene before running structural tests.");
            previousActive = SceneManager.GetActiveScene();
            scene = SceneManager.GetSceneByPath(ScenePath);
            openedByTest = !scene.IsValid() || !scene.isLoaded;
            if (openedByTest) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            Assert.That(scene.isLoaded, Is.True);
            Physics.SyncTransforms();
        }

        [OneTimeTearDown]
        public void RestoreSceneSetup()
        {
            if (openedByTest && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
        }

        private T[] SceneComponents<T>() where T : Component => scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();

        [Test]
        public void AllAuthoredGameplayAnchorsExistExactlyOnce()
        {
            var names = SceneComponents<Transform>().Select(t => t.name).ToArray();
            var required = new List<string>
            {
                "Environment", "Lighting", "Gameplay", "SpawnPoints", "Player1Spawn", "Player2Spawn",
                "GuestSpawn", "ReceptionTerminal", "Boiler", "ValveAnchor", "PanelAnchor", "BreakerAnchor",
                "LatchAAnchor", "LatchBAnchor", "RestartAnchor", "GaugeAnchor", "SteamAnchor"
            };
            for (int room = 101; room <= 106; room++)
            {
                required.Add("Room" + room);
                required.Add("RoomTarget" + room);
            }
            foreach (string anchor in required)
                Assert.That(names.Count(name => name == anchor), Is.EqualTo(1),
                    anchor + " must exist exactly once for subsequent gameplay wiring.");
            Assert.That(EditorBuildSettings.scenes.Any(s => s.enabled && s.path == ScenePath), Is.True,
                "The generated scene must be included in the player build and PlayMode smoke path.");
        }

        [Test]
        public void GeneratedSceneHasNoMissingScripts()
        {
            var broken = SceneComponents<Transform>()
                .Where(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) != 0)
                .Select(t => t.name).ToArray();
            Assert.That(broken, Is.Empty, "Missing scripts on: " + string.Join(", ", broken));
        }

        [Test]
        public void AtLeastSixDoorsHaveConnectedPhysicalSlabs()
        {
            var doors = SceneComponents<DoorInteractable>();
            Assert.That(doors.Length, Is.GreaterThanOrEqualTo(6), "Each guest room requires a usable physical door.");
            foreach (var door in doors)
            {
                Assert.That(door.doorPivot, Is.Not.Null, door.name + " has no connected pivot.");
                Assert.That(door.doorPivot.gameObject.scene, Is.EqualTo(scene), door.name + " references another scene.");
                Assert.That(door.doorPivot.GetComponentsInChildren<Collider>(true)
                    .Any(c => c.enabled && !c.isTrigger), Is.True, door.name + " has no solid door collider.");
                Assert.That(Mathf.Abs(door.openAngle), Is.GreaterThanOrEqualTo(80f), door.name + " cannot clear its doorway.");
                Assert.That(door.degreesPerSecond, Is.GreaterThan(0), door.name + " cannot move.");
            }
        }

        [Test]
        public void TwoDistinctSpawnsAreConnectedAndHaveClearSupportedCapsules()
        {
            var bootstraps = SceneComponents<LocalCoopBootstrap>();
            Assert.That(bootstraps, Has.Length.EqualTo(1), "Exactly one device/session bootstrap must own both actors.");
            var bootstrap = bootstraps[0];
            Assert.That(bootstrap.spawn1, Is.Not.Null);
            Assert.That(bootstrap.spawn2, Is.Not.Null);
            Assert.That(bootstrap.spawn1, Is.Not.SameAs(bootstrap.spawn2));
            Assert.That(Vector3.Distance(bootstrap.spawn1.position, bootstrap.spawn2.position), Is.GreaterThan(0.7f),
                "Staff capsules would overlap at spawn.");
            foreach (var spawn in new[] { bootstrap.spawn1, bootstrap.spawn2 })
            {
                Assert.That(spawn.gameObject.scene, Is.EqualTo(scene), "Spawn references must belong to the generated scene.");
                // The runtime controller is 1.78 m tall, radius 0.30 m; inset slightly for its skin width.
                Vector3 bottom = spawn.position + Vector3.up * 0.335f;
                Vector3 top = spawn.position + Vector3.up * 1.445f;
                var obstacles = Physics.OverlapCapsule(bottom, top, 0.29f, ~0, QueryTriggerInteraction.Ignore)
                    .Where(c => c.gameObject.scene == scene && c.enabled).Select(c => c.name).ToArray();
                Assert.That(obstacles, Is.Empty, spawn.name + " intersects: " + string.Join(", ", obstacles));
                bool hasFloor = Physics.RaycastAll(spawn.position + Vector3.up * 0.30f,
                        Vector3.down, 1.0f, ~0, QueryTriggerInteraction.Ignore)
                    .Any(hit => hit.collider.gameObject.scene == scene && hit.normal.y > 0.5f);
                Assert.That(hasFloor, Is.True, spawn.name + " has no supporting floor collider within 0.7 m below its feet.");
            }
        }

        [Test]
        public void AvailableCarryablePropsHaveFiniteBodiesWhileFutureDirtyLinenStaysHidden()
        {
            var props = SceneComponents<PhysicsPickup>();
            Assert.That(props, Is.Not.Empty, "Phase 1 requires a prop that can be grabbed and released.");
            foreach (var prop in props)
            {
                var body = prop.GetComponent<Rigidbody>();
                Assert.That(body, Is.Not.Null, prop.name);
                Assert.That(body.mass, Is.GreaterThan(0).And.LessThanOrEqualTo(prop.maxGrabMass), prop.name);
                var linen = prop.GetComponent<LinenBundleItem>();
                if (linen && linen.sourceRoomId != 0)
                {
                    // Six persistent room slots become physical only after a used bed becomes dirty.
                    Assert.That(linen.itemId, Is.EqualTo("dirty:" + linen.sourceRoomId));
                    Assert.That(linen.sourceAnchor, Is.Not.Null);
                    Assert.That(SceneComponents<LinenBedInteraction>().Count(bed => bed.roomId == linen.sourceRoomId && bed.dirtyBundle == linen), Is.EqualTo(1));
                    Assert.That(body.isKinematic, Is.True, prop.name);
                    Assert.That(prop.GetComponentsInChildren<Collider>(true).Any(c => c.enabled), Is.False, prop.name);
                    Assert.That(prop.GetComponentsInChildren<Renderer>(true).Any(renderer => renderer.enabled), Is.False, prop.name);
                    continue;
                }
                Assert.That(body.isKinematic, Is.False, prop.name);
                Assert.That(prop.GetComponentsInChildren<Collider>(true)
                    .Any(c => c.enabled && !c.isTrigger), Is.True, prop.name + " lacks physical collision.");
            }
        }
    }

    /// <summary>
    /// Isolated virtual-device checks exercise the runtime input reader. They do not certify physical
    /// gamepad drivers, the bootstrap pairing UI, controller ergonomics or simultaneous human repair.
    /// </summary>
    public sealed class Phase1InputTests
    {
        private readonly List<InputDevice> devices = new List<InputDevice>();
        private InputSettings originalSettings;
        private InputSettings testSettings;
        private static readonly MethodInfo BindMethod = typeof(LocalPlayerInput).GetMethod("Bind", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo ReadMethod = typeof(LocalPlayerInput).GetMethod("Read", BindingFlags.Instance | BindingFlags.NonPublic);

        [SetUp]
        public void UsePlayerUpdateSemanticsForInputEdges()
        {
            // Native EditMode updates use separate Editor buffers and do not advance the
            // per-device player frame stamp used by wasPressedThisFrame in Input System 1.17.
            // Exercise the runtime reader under Dynamic/player semantics without entering Play.
            // Only this disposable settings copy changes; project assets remain untouched.
            originalSettings = InputSystem.settings;
            testSettings = UnityEngine.Object.Instantiate(originalSettings);
            testSettings.hideFlags = HideFlags.HideAndDontSave;
            testSettings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
            testSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            testSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            testSettings.SetInternalFeatureFlag("RUN_PLAYER_UPDATES_IN_EDIT_MODE", true);
            InputSystem.settings = testSettings;
        }

        private T Add<T>() where T : InputDevice, new()
        {
            var device = InputSystem.AddDevice<T>();
            devices.Add(device);
            return device;
        }

        [TearDown]
        public void RemoveOnlyTestDevices()
        {
            try
            {
                foreach (var device in devices) if (device.added) InputSystem.RemoveDevice(device);
                devices.Clear();
            }
            finally
            {
                // Clear the manager's transient flag before restoring: ApplySettings does
                // not read feature flags when the original settings have no flag collection.
                if (testSettings != null) testSettings.SetInternalFeatureFlag("RUN_PLAYER_UPDATES_IN_EDIT_MODE", false);
                if (originalSettings != null) InputSystem.settings = originalSettings;
                if (testSettings != null) UnityEngine.Object.DestroyImmediate(testSettings);
                originalSettings = testSettings = null;
            }
        }

        private static void Bind(LocalPlayerInput input, Gamepad pad = null, Keyboard keyboard = null, Mouse mouse = null)
        {
            Assert.That(BindMethod, Is.Not.Null, "The internal input binding contract changed; update this test deliberately.");
            BindMethod.Invoke(input, new object[] { pad, keyboard, mouse, false });
        }

        private static void Read(params LocalPlayerInput[] inputs)
        {
            Assert.That(ReadMethod, Is.Not.Null);
            InputSystem.Update();
            Assert.That(InputState.currentUpdateType, Is.EqualTo(InputUpdateType.Dynamic),
                "Input edge tests must run a player Dynamic update, not an Editor state-buffer update.");
            foreach (var input in inputs) ReadMethod.Invoke(input, null);
        }

        [Test]
        public void TwoAssignedPadsDoNotReadEachOthersMovementOrUseButton()
        {
            var padA = Add<Gamepad>();
            var padB = Add<Gamepad>();
            var a = new LocalPlayerInput();
            var b = new LocalPlayerInput();
            Bind(a, padA);
            Bind(b, padB);
            InputSystem.QueueStateEvent(padA, new GamepadState { leftStick = new Vector2(0.8f, 0) }.WithButton(GamepadButton.South));
            InputSystem.QueueStateEvent(padB, new GamepadState());
            Read(a, b);
            Assert.That(a.Move.x, Is.GreaterThan(0.5f));
            Assert.That(a.PrimaryHeld, Is.True);
            Assert.That(b.Move, Is.EqualTo(Vector2.zero));
            Assert.That(b.PrimaryHeld, Is.False);

            InputSystem.QueueStateEvent(padA, new GamepadState());
            InputSystem.QueueStateEvent(padB, new GamepadState { leftStick = new Vector2(0, -0.8f) }.WithButton(GamepadButton.South));
            Read(a, b);
            Assert.That(a.Move, Is.EqualTo(Vector2.zero));
            Assert.That(a.PrimaryHeld, Is.False);
            Assert.That(b.Move.y, Is.LessThan(-0.5f));
            Assert.That(b.PrimaryHeld, Is.True);
        }

        [Test]
        public void KeyboardMouseAndPadCanActIndependentlyInOneInputUpdate()
        {
            var keyboard = Add<Keyboard>();
            var mouse = Add<Mouse>();
            var pad = Add<Gamepad>();
            var keyboardActor = new LocalPlayerInput();
            var padActor = new LocalPlayerInput();
            Bind(keyboardActor, null, keyboard, mouse);
            Bind(padActor, pad);
            // Input System initializes per-button edge tracking on its first read. Establish
            // a real neutral frame before testing a later press, as runtime Update does.
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.QueueStateEvent(mouse, new MouseState());
            InputSystem.QueueStateEvent(pad, new GamepadState());
            Read(keyboardActor, padActor);
            Assert.That(keyboardActor.PrimaryHeld, Is.False);
            Assert.That(padActor.SecondaryPressed, Is.False);

            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.E));
            InputSystem.QueueStateEvent(mouse, new MouseState { delta = new Vector2(8, 4) });
            InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = new Vector2(-0.8f, 0) }.WithButton(GamepadButton.West));
            Read(keyboardActor, padActor);
            Assert.That(keyboardActor.DeviceReady, Is.True);
            Assert.That(keyboardActor.Move, Is.EqualTo(Vector2.up));
            Assert.That(keyboardActor.PrimaryHeld, Is.True);
            Assert.That(keyboardActor.Look.sqrMagnitude, Is.GreaterThan(0));
            Assert.That(keyboardActor.SecondaryPressed, Is.False);
            Assert.That(padActor.DeviceReady, Is.True);
            Assert.That(padActor.Move.x, Is.LessThan(-0.5f));
            Assert.That(padActor.Move.y, Is.EqualTo(0).Within(0.0001f));
            Assert.That(padActor.Look, Is.EqualTo(Vector2.zero));
            Assert.That(padActor.PrimaryHeld, Is.False);
            Assert.That(padActor.SecondaryPressed, Is.True);
        }

        [Test]
        public void RebindingToNoDeviceClearsHeldInteractionAndMovementImmediately()
        {
            var pad = Add<Gamepad>();
            var input = new LocalPlayerInput();
            Bind(input, pad);
            InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = Vector2.up }.WithButton(GamepadButton.South));
            Read(input);
            Assert.That(input.PrimaryHeld, Is.True);
            Bind(input);
            Assert.That(input.DeviceReady, Is.False);
            Assert.That(input.PrimaryHeld, Is.False);
            Assert.That(input.Move, Is.EqualTo(Vector2.zero));
            Assert.That(input.Look, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void KeyboardWithoutMouseDoesNotClaimAReadyStaffSlot()
        {
            var keyboard = Add<Keyboard>();
            var input = new LocalPlayerInput();
            Bind(input, null, keyboard);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.E));
            Read(input);
            Assert.That(input.DeviceReady, Is.False);
            Assert.That(input.Move, Is.EqualTo(Vector2.zero));
            Assert.That(input.PrimaryHeld, Is.False);
        }
    }
}
