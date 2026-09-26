using System;
using UnityEngine;

namespace WorstHotel
{
    public enum LanRole { Offline, Host, Client }

    [Serializable]
    public sealed class LanInputFrame
    {
        public long epoch, sequence;
        public Vector2 move, lookDegrees, navigate;
        public bool primaryPressed, primaryHeld, primaryReleased, secondaryPressed, grabPressed;
        public bool menuConfirmPressed, menuCancelPressed, sprintHeld, waitHeld, gamepadLabels, uiBlocked;
        public bool IsFinite => Finite(move) && Finite(lookDegrees) && Finite(navigate);
        static bool Finite(Vector2 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y);
        internal void ClearEdges()
        {
            lookDegrees = Vector2.zero;
            primaryPressed = primaryReleased = secondaryPressed = grabPressed = menuConfirmPressed = menuCancelPressed = false;
        }
        internal void MergeEdges(LanInputFrame source)
        {
            lookDegrees += source.lookDegrees;
            primaryPressed |= source.primaryPressed; primaryReleased |= source.primaryReleased;
            secondaryPressed |= source.secondaryPressed; grabPressed |= source.grabPressed;
            menuConfirmPressed |= source.menuConfirmPressed; menuCancelPressed |= source.menuCancelPressed;
        }
        internal LanInputFrame Copy() => (LanInputFrame)MemberwiseClone();
    }

    public sealed partial class LocalCoopBootstrap
    {
        public LanRole LanRole { get; private set; }
        public int LocalActorId { get; private set; }
        public bool HasWorldAuthority => LanRole != LanRole.Client;
        public bool RemoteInputLeaseExpired { get; private set; }
        public const float RemoteInputLeaseSeconds = .35f;
        public event Action<int> RemoteLedgerRequested;
        bool remoteConnected;
        bool remoteHostPaused;
        float lastRemoteInputTime = float.NegativeInfinity;
        long inputEpoch, lastRemoteSequence = -1;
        LanInputFrame remoteFrame = new LanInputFrame(), localPending = new LanInputFrame();

        public bool IsLocalActor(int actorId) => actorId >= 0 && actorId < 2 &&
            (IsSolo ? actorId == 0 : LanRole == LanRole.Offline || actorId == LocalActorId);
        public Rect ViewportForActor(int actorId) => LanRole == LanRole.Offline && !IsSolo ?
            new Rect(actorId * .5f, 0, .5f, 1) : new Rect(0, 0, 1, 1);

        bool LocalNetworkInputBlocked
        {
            get
            {
                var lan = LanSession.Instance;
                return IsPaused || !hasFocus || !Players[LocalActorId] || !Players[LocalActorId].DeviceReady ||
                    (lan && (lan.MenuOpen || LanRole == LanRole.Client && !lan.HasSnapshot));
            }
        }

        public void ConfigureLan(LanRole role, int localActorId)
        {
            if (role != LanRole.Offline && localActorId != (role == LanRole.Host ? 0 : 1))
                throw new ArgumentOutOfRangeException(nameof(localActorId), "Two-player LAN assigns host 0 and client 1.");
            foreach (var player in Players) if (player) player.Interactor.CancelInteraction();
            LanRole = role; LocalActorId = role == LanRole.Offline ? 0 : localActorId;
            Mode = role == LanRole.Offline ? SessionMode.LocalDevelopment : SessionMode.Network;
            EnsureSecondPlayer();
            manuallyPaused = remoteHostPaused = false; DebugKeyboardOnly = false;
            ResetInputEpoch(inputEpoch); devicesDirty = true;
            if (Players[0]) { AssignDevices(); ApplyLanPresentation(); RefreshPause(); }
            if (GameSession.Instance) GameSession.Instance.RefreshSoloConfiguration();
        }

        public void ResetInputEpoch(long epoch)
        {
            inputEpoch = epoch; lastRemoteSequence = -1; lastRemoteInputTime = float.NegativeInfinity;
            remoteFrame = new LanInputFrame(); localPending = new LanInputFrame();
            RemoteInputLeaseExpired = false;
            if (Players[1]) { Players[1].Interactor.CancelInteraction(); Players[1].Input.Clear(); }
        }

        public void SetRemoteConnected(bool connected)
        {
            if (remoteConnected == connected) return;
            remoteConnected = connected;
            lastRemoteInputTime = float.NegativeInfinity; remoteFrame = new LanInputFrame();
            if (LanRole == LanRole.Host && Players[1])
            {
                Players[1].Interactor.CancelInteraction(); Players[1].SetUIBlocked(false);
                Players[1].Input.BindRemote(connected);
            }
        }

        public bool SubmitRemoteInput(LanInputFrame frame)
        {
            if (LanRole != LanRole.Host || !remoteConnected || frame == null || !frame.IsFinite ||
                frame.epoch != inputEpoch || frame.sequence <= lastRemoteSequence) return false;
            // Sender-to-actor mapping belongs to the transport. This API always controls actor 1.
            var next = frame.Copy(); next.move = Vector2.ClampMagnitude(next.move, 1);
            next.navigate = Vector2.ClampMagnitude(next.navigate, 1);
            next.lookDegrees = Vector2.ClampMagnitude(next.lookDegrees, 90);
            next.MergeEdges(remoteFrame);
            next.lookDegrees = Vector2.ClampMagnitude(next.lookDegrees, 180);
            remoteFrame = next; lastRemoteSequence = frame.sequence;
            lastRemoteInputTime = Time.unscaledTime; RemoteInputLeaseExpired = false;
            return true;
        }

        public LanInputFrame CaptureLocalInput(long epoch, long sequence)
        {
            if (LanRole != LanRole.Offline && LocalNetworkInputBlocked)
            {
                // Menus keep sending a lease renewal with no gameplay intent. Do not replay a key
                // press, carry toggle or accumulated mouse movement after the menu closes.
                localPending = new LanInputFrame();
                return new LanInputFrame { epoch = epoch, sequence = sequence, uiBlocked = true,
                    gamepadLabels = Players[LocalActorId] && Players[LocalActorId].Input.Gamepad != null };
            }
            var result = localPending.Copy(); result.epoch = epoch; result.sequence = sequence;
            if (Players[LocalActorId]) result.uiBlocked |= Players[LocalActorId].IsUIBlocked || !hasFocus || !Players[LocalActorId].DeviceReady;
            localPending.ClearEdges();
            if (result.uiBlocked) return new LanInputFrame { epoch = epoch, sequence = sequence,
                uiBlocked = true, gamepadLabels = result.gamepadLabels };
            return result;
        }

        public void SetRemoteHostPaused(bool paused)
        {
            remoteHostPaused = paused;
            if (LanRole == LanRole.Client) RefreshPause();
        }

        public bool RequestRemoteLedger(int actorId)
        {
            if (LanRole != LanRole.Host || actorId != 1) return false;
            if (Players[1]) Players[1].SetUIBlocked(true);
            RemoteLedgerRequested?.Invoke(actorId);
            return true;
        }

        void ApplyLanPresentation()
        {
            foreach (var player in Players) if (player)
                player.ConfigureAuthority(HasWorldAuthority, IsLocalActor(player.ActorId), ViewportForActor(player.ActorId));
        }

        void ReadLanInput()
        {
            if (LanRole == LanRole.Offline) return;
            var local = Players[LocalActorId];
            if (local)
            {
                if (!hasFocus || !local.DeviceReady) local.Input.Clear();
                var input = local.Input;
                var next = new LanInputFrame
                {
                    move = input.Move, navigate = input.Navigate,
                    lookDegrees = input.Look * (input.IsMouseLook ? local.mouseSensitivity : local.controllerLookSpeed * Time.unscaledDeltaTime),
                    primaryPressed = input.PrimaryPressed, primaryHeld = input.PrimaryHeld, primaryReleased = input.PrimaryReleased,
                    secondaryPressed = input.SecondaryPressed, grabPressed = input.GrabPressed,
                    menuConfirmPressed = input.MenuConfirmPressed, menuCancelPressed = input.MenuCancelPressed,
                    sprintHeld = input.SprintHeld, waitHeld = input.WaitHeld, gamepadLabels = input.Gamepad != null,
                    uiBlocked = local.IsUIBlocked || !hasFocus || !local.DeviceReady
                };
                if (LocalNetworkInputBlocked) localPending = new LanInputFrame { uiBlocked = true, gamepadLabels = next.gamepadLabels };
                else
                {
                    next.MergeEdges(localPending);
                    next.lookDegrees = Vector2.ClampMagnitude(next.lookDegrees, 180);
                    localPending = next;
                }
            }
            if (LanRole != LanRole.Host || !Players[1]) return;
            if (!remoteConnected || Time.unscaledTime - lastRemoteInputTime > RemoteInputLeaseSeconds)
            {
                if (!RemoteInputLeaseExpired) Players[1].Interactor.CancelInteraction();
                RemoteInputLeaseExpired = remoteConnected;
                Players[1].Input.Clear(); remoteFrame = new LanInputFrame();
                return;
            }
            Players[1].Input.ApplyRemote(remoteFrame);
            Players[1].SetUIBlocked(remoteFrame.uiBlocked);
            remoteFrame.ClearEdges();
        }
    }
}
