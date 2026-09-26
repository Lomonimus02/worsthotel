using System;
using UnityEngine;

namespace WorstHotel
{
    public enum SessionMode { LocalDevelopment, Solo, Network }

    public sealed partial class LocalCoopBootstrap
    {
        public SessionMode Mode { get; private set; } = SessionMode.LocalDevelopment;
        public bool IsSolo => Mode == SessionMode.Solo;
        public int RequiredStaffCount => IsSolo ? 1 : 2;

        public void ConfigureSolo()
        {
            foreach (var player in Players) if (player) player.Interactor.CancelInteraction();
            LanRole = LanRole.Offline; LocalActorId = 0; Mode = SessionMode.Solo;
            manuallyPaused = remoteHostPaused = DebugKeyboardOnly = false;
            remoteConnected = false;
            ResetInputEpoch(inputEpoch);
            if (Players[1])
            {
                var retired = Players[1]; Players[1] = null;
                retired.gameObject.SetActive(false);
                Destroy(retired.gameObject);
            }
            devicesDirty = true;
            if (Players[0]) { AssignDevices(); ApplyLanPresentation(); RefreshPause(); }
            if (GameSession.Instance) GameSession.Instance.RefreshSoloConfiguration();
        }

        void EnsureSecondPlayer()
        {
            if (Players[0] && !Players[1]) Players[1] = CreatePlayer(1, spawn2, new Vector3(1.4f, .1f, 2));
        }

        void SelectInitialMode()
        {
            // The explicit verification flag works even when the networking menu installer is absent.
            // Normal menu boot has only one rig; a second is created only by HOST/JOIN or the dev option.
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-hotelSolo") >= 0 || LanSession.Instance)
                ConfigureSolo();
        }
    }
}
