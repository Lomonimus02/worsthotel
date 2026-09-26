using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace WorstHotel.Tests
{
    /// <summary>Virtual-device gameplay smoke; physical controllers and human cooperation remain manual gates.</summary>
    public sealed partial class Phase1PlayModeTests
    {
        private Gamepad padA;
        private Gamepad padB;
        private Scene loadedScene;
        private LocalCoopBootstrap bootstrap;
        private SessionConfig legacySceneConfig;

        [UnitySetUp]
        public IEnumerator LoadFreshHotelWithTwoVirtualPads()
        {
            if (Gamepad.all.Count != 0)
                Assert.Ignore("Run this isolated smoke test without existing gamepads. It does not disable or inject events into physical devices.");
            Assert.That(Application.CanStreamedLevelBeLoaded("PrototypeHotel"), Is.True,
                "Generate PrototypeHotel and include it in Build Settings before this PlayMode test.");
            padA = InputSystem.AddDevice<Gamepad>("Phase1SmokeStaffA");
            padB = InputSystem.AddDevice<Gamepad>("Phase1SmokeStaffB");
            var loading = SceneManager.LoadSceneAsync("PrototypeHotel", LoadSceneMode.Single);
            Assert.That(loading, Is.Not.Null);
            while (!loading.isDone) yield return null;
            loadedScene = SceneManager.GetSceneByName("PrototypeHotel");
            yield return null;
            yield return null;
            bootstrap = LocalCoopBootstrap.Instance;
            Assert.That(bootstrap, Is.Not.Null);
            // Historical scene tests exercise the original shift contract explicitly.
            // Continuous acceptance tests keep the real production asset and opt out of this adapter.
            if (!TestContext.CurrentContext.Test.Properties["Category"].Contains("ContinuousOperations"))
            {
                legacySceneConfig = Object.Instantiate(GameSession.Instance.config);
                legacySceneConfig.continuousOperations = false;
                GameSession.Instance.config = legacySceneConfig;
                GameSession.Instance.NewGame();
            }
            Assert.That(bootstrap.Players, Has.Length.EqualTo(2));
            Assert.That(bootstrap.Players[0], Is.Not.Null);
            Assert.That(bootstrap.Players[1], Is.Not.Null);
            Assert.That(bootstrap.Players[0].Input.Gamepad, Is.SameAs(padA));
            Assert.That(bootstrap.Players[1].Input.Gamepad, Is.SameAs(padB));
            Assert.That(bootstrap.WaitingForDevices, Is.False);
            Assert.That(bootstrap.IsPaused, Is.False, "Device-ready hotel must run; inspect application focus if a batch host pauses it.");
            // Later phases initially show the shared planning ledger. Close it through the
            // public UI command before testing free movement; do not bypass actor blocking.
            if (ManagementUI.Instance != null) ManagementUI.Instance.Close();
            Assert.That(bootstrap.Players[0].IsUIBlocked || bootstrap.Players[1].IsUIBlocked, Is.False);
            yield return new WaitForSecondsRealtime(0.15f);
        }

        [UnityTearDown]
        public IEnumerator UnloadHotelAndRemoveOnlyVirtualPads()
        {
            if (loadedScene.IsValid() && loadedScene.isLoaded)
            {
                if (SceneManager.sceneCount == 1) SceneManager.CreateScene("Phase1 smoke cleanup");
                var unloading = SceneManager.UnloadSceneAsync(loadedScene);
                if (unloading != null) while (!unloading.isDone) yield return null;
            }
            if (padA != null && padA.added) InputSystem.RemoveDevice(padA);
            if (padB != null && padB.added) InputSystem.RemoveDevice(padB);
            padA = padB = null;
            if (legacySceneConfig) Object.Destroy(legacySceneConfig);
            legacySceneConfig = null;
            Time.timeScale = 1;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        [UnityTest]
        public IEnumerator TwoStaffHaveSeparateCamerasAndOnlyOneAudioListener()
        {
            var a = bootstrap.Players[0];
            var b = bootstrap.Players[1];
            Assert.That(a.ActorId, Is.Not.EqualTo(b.ActorId));
            Assert.That(a.PlayerCamera, Is.Not.Null);
            Assert.That(b.PlayerCamera, Is.Not.Null);
            Assert.That(a.PlayerCamera, Is.Not.SameAs(b.PlayerCamera));
            Assert.That(a.PlayerCamera.rect, Is.EqualTo(new Rect(0, 0, 0.5f, 1)));
            Assert.That(b.PlayerCamera.rect, Is.EqualTo(new Rect(0.5f, 0, 0.5f, 1)));
            Assert.That(a.PlayerCamera.enabled && b.PlayerCamera.enabled, Is.True);
            Assert.That(a.BodyCollider.enabled && b.BodyCollider.enabled, Is.True);
            Assert.That(bootstrap.GetComponentsInChildren<AudioListener>().Length, Is.EqualTo(1));
            // Allow an actual rendered player-loop frame; no Camera.Render or GPU readback is required.
            yield return null;
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator EachVirtualPadMovesOnlyItsOwnGroundedActor()
        {
            var a = bootstrap.Players[0];
            var b = bootstrap.Players[1];
            // Lobby spawns face loose luggage; this test isolates flat-ground input ownership.
            // The authored corridor at x +/-0.65, z 7 has a clear, continuous floor.
            var corridorStart = new GameObject("Flat corridor movement test origin");
            corridorStart.transform.SetPositionAndRotation(new Vector3(-0.65f, 0.08f, 7), Quaternion.identity);
            a.ResetToSpawn(corridorStart.transform);
            corridorStart.transform.position = new Vector3(0.65f, 0.08f, 7);
            b.ResetToSpawn(corridorStart.transform);
            UnityEngine.Object.Destroy(corridorStart);
            yield return WaitForGroundContact(a);
            yield return WaitForGroundContact(b);
            var aStart = a.transform.position;
            var bStart = b.transform.position;
            InputSystem.QueueStateEvent(padA, new GamepadState { leftStick = Vector2.up });
            InputSystem.QueueStateEvent(padB, new GamepadState());
            yield return new WaitForSecondsRealtime(0.35f);
            InputSystem.QueueStateEvent(padA, new GamepadState());
            yield return null;
            Assert.That(HorizontalDistance(aStart, a.transform.position), Is.GreaterThan(0.20f), "Staff A did not walk.");
            Assert.That(HorizontalDistance(bStart, b.transform.position), Is.LessThan(0.06f), "Staff A's pad also moved Staff B.");
            yield return WaitForGroundContact(a);

            aStart = a.transform.position;
            bStart = b.transform.position;
            InputSystem.QueueStateEvent(padB, new GamepadState { leftStick = Vector2.up });
            yield return new WaitForSecondsRealtime(0.35f);
            InputSystem.QueueStateEvent(padB, new GamepadState());
            yield return null;
            Assert.That(HorizontalDistance(bStart, b.transform.position), Is.GreaterThan(0.20f), "Staff B did not walk.");
            Assert.That(HorizontalDistance(aStart, a.transform.position), Is.LessThan(0.06f), "Staff B's pad also moved Staff A.");
            yield return WaitForGroundContact(b);
            LogAssert.NoUnexpectedReceived();
        }

        private static IEnumerator WaitForGroundContact(FirstPersonController player)
        {
            // isGrounded describes the last CharacterController.Move, not a persistent floor
            // overlap. Startup/render timing and the final stop frame can precede contact.
            // Require two consecutive actual contacts within a bounded settling window, and
            // verify the real floor height; a hovering or falling actor must still fail.
            float deadline = Time.realtimeSinceStartup + 2;
            int contacts = 0, frames = 0;
            float initialY = player.transform.position.y;
            while (contacts < 2 && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                frames++;
                contacts = player.BodyCollider.isGrounded ? contacts + 1 : 0;
            }
            string detail = "Staff " + player.ActorId + " after " + frames + " frames: position=" +
                player.transform.position.ToString("F4") + ", initialY=" + initialY.ToString("F4") +
                ", velocity=" + player.BodyCollider.velocity.ToString("F4") + ", collisionFlags=" +
                player.BodyCollider.collisionFlags + ", deltaTime=" + Time.deltaTime.ToString("F5") +
                ", minMoveDistance=" + player.BodyCollider.minMoveDistance.ToString("F5");
            Assert.That(contacts, Is.GreaterThanOrEqualTo(2), "Controller did not settle on the floor. " + detail);
            Assert.That(player.BodyCollider.isGrounded, Is.True, detail);
            var feet = player.transform.position + Vector3.up * 0.12f;
            var surfaces = Physics.RaycastAll(feet, Vector3.down, 0.25f, ~0, QueryTriggerInteraction.Ignore);
            bool floorAtFeet = false;
            foreach (var hit in surfaces)
                if (!hit.collider.transform.IsChildOf(player.transform) && hit.normal.y > 0.9f &&
                    Mathf.Abs(player.transform.position.y - hit.point.y) <= 0.10f) floorAtFeet = true;
            Assert.That(floorAtFeet, Is.True, "Ground contact must be backed by an actual supporting floor collider. " + detail);
        }

        private static float HorizontalDistance(Vector3 from, Vector3 to) =>
            new Vector2(to.x - from.x, to.z - from.z).magnitude;
    }
}
