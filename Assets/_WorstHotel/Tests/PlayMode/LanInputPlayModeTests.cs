using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace WorstHotel.Tests
{
    public sealed partial class Phase1PlayModeTests
    {
        const long LanFixtureEpoch = 7103;
        long lanFixtureSequence;

        IEnumerator ConfigureHostInputFixture()
        {
            // These tests start at the authenticated transport boundary. Real two-process NGO
            // delivery is a separate player gate; host CC/raycast/joints below are production code.
            bootstrap.ConfigureLan(LanRole.Host, 0);
            bootstrap.ResetInputEpoch(LanFixtureEpoch);
            bootstrap.SetRemoteConnected(true);
            lanFixtureSequence = 0;
            if (ManagementUI.Instance) ManagementUI.Instance.Close();
            yield return RemoteNeutralFrames(3);
            Assert.That(bootstrap.IsPaused, Is.False);
            Assert.That(bootstrap.Players.Single(player => bootstrap.IsLocalActor(player.ActorId)).PlayerCamera.rect,
                Is.EqualTo(new Rect(0, 0, 1, 1)));
            Assert.That(bootstrap.Players.Count(player => player.PlayerCamera.enabled), Is.EqualTo(1));
            Assert.That(bootstrap.GetComponentsInChildren<AudioListener>(true).Count(listener => listener.enabled), Is.EqualTo(1));
        }

        LanInputFrame SubmitLanFrame(System.Action<LanInputFrame> configure = null)
        {
            var frame = new LanInputFrame { epoch = LanFixtureEpoch, sequence = ++lanFixtureSequence, gamepadLabels = true };
            configure?.Invoke(frame);
            Assert.That(bootstrap.SubmitRemoteInput(frame), Is.True, "Fresh authenticated remote input must be accepted.");
            return frame;
        }

        IEnumerator RemoteNeutralFrames(int count)
        {
            for (int frame = 0; frame < count; frame++) { SubmitLanFrame(); yield return null; }
        }

        IEnumerator AimRemoteAt(System.Func<Vector3> target)
        {
            var actor = bootstrap.Players[1];
            float deadline = Time.realtimeSinceStartup + 4;
            while (Time.realtimeSinceStartup < deadline)
            {
                Vector3 delta = target() - actor.PlayerCamera.transform.position;
                float yaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
                float pitch = -Mathf.Atan2(delta.y, new Vector2(delta.x, delta.z).magnitude) * Mathf.Rad2Deg;
                float yawError = Mathf.DeltaAngle(actor.transform.eulerAngles.y, yaw);
                float pitchError = Mathf.DeltaAngle(actor.PlayerCamera.transform.localEulerAngles.x, pitch);
                if (Mathf.Abs(yawError) < .8f && Mathf.Abs(pitchError) < .8f) break;
                SubmitLanFrame(frame => frame.lookDegrees = Vector2.ClampMagnitude(new Vector2(yawError, -pitchError), 45));
                yield return null;
            }
            yield return RemoteNeutralFrames(2);
            Assert.That(Vector3.Angle(actor.PlayerCamera.transform.forward, target() - actor.PlayerCamera.transform.position), Is.LessThan(2));
        }

        [UnityTest]
        public IEnumerator RemotePressBurstPhysicallyTakesRackKeyOnceAndExpiredInputDropsIt()
        {
            yield return ConfigureHostInputFixture();
            var remote = bootstrap.Players[1]; var local = bootstrap.Players[bootstrap.LocalActorId];
            var key = PhysicalKey(101);
            yield return PositionEmptyActorForLinen(1,
                new Vector3(-4.4f, .08f, 4.45f), key.Body.worldCenterOfMass);
            yield return AimRemoteAt(() => key.Body.worldCenterOfMass);
            Assert.That(remote.Interactor.FocusedPickup, Is.SameAs(key.GetComponent<PhysicsPickup>()));
            // Both packets arrive before one host Update: its one-shot grab edge must survive
            // the newer released level without being executed again next frame.
            var pressed = SubmitLanFrame(frame => frame.grabPressed = true);
            SubmitLanFrame();
            yield return null;
            yield return RemoteNeutralFrames(3);
            Assert.That(remote.Interactor.HeldBody, Is.SameAs(key.Body));
            Assert.That(local.Interactor.HeldBody, Is.Null, "A network frame cannot operate the host's local hands.");
            Assert.That(key.State.PlayerId, Is.EqualTo(remote.ActorId));
            Assert.That(key.Body.GetComponent<ConfigurableJoint>(), Is.Not.Null);
            Assert.That(key.Body.isKinematic, Is.False);
            Assert.That(bootstrap.SubmitRemoteInput(pressed), Is.False, "Replaying F must not toggle the held key back onto the floor.");
            Assert.That(bootstrap.SubmitRemoteInput(new LanInputFrame
            { epoch = LanFixtureEpoch - 1, sequence = lanFixtureSequence + 100, grabPressed = true }), Is.False);
            Assert.That(bootstrap.SubmitRemoteInput(new LanInputFrame
            { epoch = LanFixtureEpoch, sequence = lanFixtureSequence + 100, move = new Vector2(float.NaN, 0), grabPressed = true }), Is.False);
            yield return RemoteNeutralFrames(2);
            Assert.That(remote.Interactor.HeldBody, Is.SameAs(key.Body));
            yield return new WaitForSecondsRealtime(LocalCoopBootstrap.RemoteInputLeaseSeconds + .15f);
            Assert.That(bootstrap.RemoteInputLeaseExpired, Is.True);
            Assert.That(remote.Interactor.HeldBody, Is.Null);
            Assert.That(key.Body.GetComponent<ConfigurableJoint>(), Is.Null);
            Assert.That(key.State.Location, Is.EqualTo(RoomKeyLocation.Dropped));
            Assert.That(key.State.PlayerId, Is.Null);
            Assert.That(remote.Input.PrimaryHeld || remote.Input.WaitHeld, Is.False);

            bootstrap.SetPaused(true);
            QueueGrab(padA, true); yield return null;
            var paused = bootstrap.CaptureLocalInput(LanFixtureEpoch, 1);
            Assert.That(paused.uiBlocked, Is.True);
            Assert.That(paused.grabPressed || paused.primaryPressed || paused.primaryHeld || paused.waitHeld, Is.False);
            Assert.That(paused.move, Is.EqualTo(Vector2.zero));
            Assert.That(paused.lookDegrees, Is.EqualTo(Vector2.zero));
            QueueGrab(padA, false); yield return null;
            bootstrap.SetPaused(false); yield return null;
            Assert.That(bootstrap.CaptureLocalInput(LanFixtureEpoch, 2).grabPressed, Is.False,
                "An input edge captured while paused must not replay when the menu closes.");

            // Client view configuration has one full camera and no locally authoritative bodies.
            bootstrap.ConfigureLan(LanRole.Client, 1);
            Assert.That(bootstrap.Players.All(player => !player.BodyCollider.enabled && !player.Interactor.HasWorldAuthority), Is.True);
            var camera = bootstrap.Players.Single(player => bootstrap.IsLocalActor(player.ActorId)).PlayerCamera;
            Assert.That(camera.enabled, Is.True);
            Assert.That(camera.rect, Is.EqualTo(new Rect(0, 0, 1, 1)));
            Assert.That(bootstrap.Players.Count(player => player.PlayerCamera.enabled), Is.EqualTo(1));
            Assert.That(bootstrap.GetComponentsInChildren<AudioListener>(true).Count(listener => listener.enabled), Is.EqualTo(1));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator RemoteBedHoldUsesHostTimeAndDisconnectReleasesPhysicalLinenAndClaim()
        {
            yield return ConfigureHostInputFixture();
            var session = GameSession.Instance;
            var sim = session.Simulation;
            Assert.That(sim.DebugMarkRoomDirty(101).Success, Is.True);
            Assert.That(sim.PickUpLinen(0, "dirty:101").Success, Is.True);
            Assert.That(sim.DepositDirtyLinen(0, "dirty:101").Success, Is.True);
            // Stage an unheld, explicitly dropped clean bundle at the bed. The existing complete
            // shelf-to-bed route owns source/carry evidence; this isolates network hold authority.
            Assert.That(sim.PickUpLinen(0, "clean:0").Success, Is.True);
            Assert.That(sim.DropLinen(0, "clean:0").Success, Is.True);
            yield return RemoteNeutralFrames(2);
            var bundle = Object.FindObjectsByType<LinenBundleItem>(FindObjectsSortMode.None).Single(item => item.itemId == "clean:0");
            var bed = Object.FindObjectsByType<LinenBedInteraction>(FindObjectsSortMode.None).Single(item => item.roomId == 101);
            bundle.Body.position = bed.dirtyBundle.sourceAnchor.position;
            bundle.Body.linearVelocity = bundle.Body.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
            yield return PositionEmptyActorForLinen(1, new Vector3(-4.65f, .08f, 10.10f), bundle.Body.worldCenterOfMass);
            yield return AimRemoteAt(() => bundle.Body.worldCenterOfMass);
            var remote = bootstrap.Players[1];
            Assert.That(remote.Interactor.FocusedPickup, Is.SameAs(bundle.GetComponent<PhysicsPickup>()));
            SubmitLanFrame(frame => frame.grabPressed = true); yield return null;
            yield return RemoteNeutralFrames(2);
            Assert.That(remote.Interactor.HeldBody, Is.SameAs(bundle.Body));
            yield return AimRemoteAt(() => new Vector3(-5.56f, .84f, 10.15f));
            Assert.That(remote.Interactor.Focused, Is.SameAs(bed));
            var task = sim.Housekeeping.Find(101);
            float began = Time.realtimeSinceStartup;
            SubmitLanFrame(frame => { frame.primaryPressed = true; frame.primaryHeld = true; });
            yield return null;
            // Repeated fresh packets preserve the hold; the count of packets is not elapsed work.
            while (Time.realtimeSinceStartup - began < .25f)
            {
                for (int burst = 0; burst < 5; burst++) SubmitLanFrame(frame => frame.primaryHeld = true);
                yield return null;
            }
            Assert.That(task.Step, Is.EqualTo(RoomPreparationStep.MakingBed));
            Assert.That(task.WorkingPlayerId, Is.EqualTo(remote.ActorId));
            Assert.That(task.ProgressSeconds, Is.GreaterThan(0).And.LessThan(.65f));
            Assert.That(bed.OwnerPlayerId, Is.EqualTo(remote.ActorId));
            Assert.That(bundle.State.Location, Is.EqualTo(LinenLocation.HeldByPlayer));
            bootstrap.SetRemoteConnected(false);
            yield return null; yield return null;
            Assert.That(remote.Interactor.HeldBody, Is.Null);
            Assert.That(bundle.Body.GetComponent<ConfigurableJoint>(), Is.Null);
            Assert.That(bundle.State.Location, Is.EqualTo(LinenLocation.Dropped));
            Assert.That(bundle.State.PlayerId, Is.Null);
            Assert.That(task.Step, Is.EqualTo(RoomPreparationStep.NeedsCleanLinen));
            Assert.That(task.WorkingPlayerId, Is.Null);
            Assert.That(task.ProgressSeconds, Is.Zero);
            Assert.That(bed.OwnerPlayerId, Is.EqualTo(-1));
            Assert.That(session.Rooms.Single(room => room.Profile.Id == 101).Cleanliness, Is.EqualTo(Cleanliness.Dirty));
            Assert.That(sim.Housekeeping.Linens.Count(linen => linen.Kind == LinenKind.Clean && linen.Location == LinenLocation.Consumed), Is.Zero);
            Assert.That(bootstrap.SubmitRemoteInput(new LanInputFrame
            { epoch = LanFixtureEpoch, sequence = lanFixtureSequence + 1, primaryHeld = true }), Is.False);
            bootstrap.ResetInputEpoch(LanFixtureEpoch + 1);
            bootstrap.SetRemoteConnected(true);
            Assert.That(bootstrap.SubmitRemoteInput(new LanInputFrame
            { epoch = LanFixtureEpoch, sequence = lanFixtureSequence + 2, primaryHeld = true }), Is.False);
            Assert.That(task.ProgressSeconds, Is.Zero, "A late old-session hold cannot reclaim a bed after reconnect.");
            LogAssert.NoUnexpectedReceived();
        }
    }
}
