using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WorstHotel
{
    /// <summary>Each frame is read from one explicitly assigned device set, never global action maps.</summary>
    public sealed class LocalPlayerInput
    {
        public Gamepad Gamepad { get; private set; }
        public Keyboard Keyboard { get; private set; }
        public Mouse Mouse { get; private set; }
        public bool DeviceReady { get; private set; }
        public string DeviceLabel { get; private set; } = "Waiting for controller";
        public bool IsMouseLook => Mouse != null;
        public bool LookIsDegrees { get; private set; }
        public bool IsRemote { get; private set; }
        bool remoteGamepad;
        public Vector2 Move { get; private set; }
        public Vector2 Look { get; private set; }
        public Vector2 Navigate { get; private set; }
        public bool PrimaryPressed { get; private set; }
        public bool PrimaryHeld { get; private set; }
        public bool PrimaryReleased { get; private set; }
        public bool SecondaryPressed { get; private set; }
        public bool GrabPressed { get; private set; }
        public bool PausePressed { get; private set; }
        public bool MenuConfirmPressed { get; private set; }
        public bool MenuCancelPressed { get; private set; }
        public bool SprintHeld { get; private set; }
        public bool WaitHeld { get; private set; }
        public string PrimaryLabel => Gamepad != null || remoteGamepad ? "A / Cross" : "E";
        public string SecondaryLabel => Gamepad != null || remoteGamepad ? "X / Square" : "Q";
        public string GrabLabel => Gamepad != null || remoteGamepad ? "RB / R1" : "F";
        public string WaitLabel => Gamepad != null || remoteGamepad ? "Y / Triangle" : "T";

        internal void Bind(Gamepad pad, Keyboard keyboard, Mouse mouse, bool debugInactive = false)
        {
            IsRemote = LookIsDegrees = remoteGamepad = false;
            Gamepad = pad;
            Keyboard = keyboard;
            Mouse = mouse;
            DeviceReady = debugInactive || pad != null || (keyboard != null && mouse != null);
            DeviceLabel = debugInactive ? "DEBUG: inactive actor" : pad != null ? pad.displayName :
                keyboard != null && mouse != null ? "Keyboard + mouse" : "Connect a gamepad";
            Clear();
        }

        internal void Clear()
        {
            Move = Look = Navigate = Vector2.zero;
            PrimaryPressed = PrimaryHeld = PrimaryReleased = SecondaryPressed = GrabPressed = false;
            PausePressed = MenuConfirmPressed = MenuCancelPressed = SprintHeld = WaitHeld = false;
        }

        internal void Read()
        {
            if (IsRemote) return;
            Clear();
            if (!DeviceReady) return;
            if (Gamepad != null)
            {
                Move = Gamepad.leftStick.ReadValue();
                Look = Gamepad.rightStick.ReadValue();
                Navigate = Gamepad.dpad.ReadValue();
                PrimaryPressed = Gamepad.buttonSouth.wasPressedThisFrame;
                PrimaryHeld = Gamepad.buttonSouth.isPressed;
                PrimaryReleased = Gamepad.buttonSouth.wasReleasedThisFrame;
                SecondaryPressed = Gamepad.buttonWest.wasPressedThisFrame;
                GrabPressed = Gamepad.rightShoulder.wasPressedThisFrame;
                PausePressed = Gamepad.startButton.wasPressedThisFrame;
                MenuConfirmPressed = Gamepad.buttonSouth.wasPressedThisFrame;
                MenuCancelPressed = Gamepad.buttonEast.wasPressedThisFrame;
                SprintHeld = Gamepad.leftStickButton.isPressed;
                WaitHeld = Gamepad.buttonNorth.isPressed;
            }
            else if (Keyboard != null && Mouse != null)
            {
                Move = new Vector2((Keyboard.dKey.isPressed ? 1 : 0) - (Keyboard.aKey.isPressed ? 1 : 0),
                    (Keyboard.wKey.isPressed ? 1 : 0) - (Keyboard.sKey.isPressed ? 1 : 0));
                Move = Vector2.ClampMagnitude(Move, 1);
                Navigate = new Vector2((Keyboard.rightArrowKey.isPressed ? 1 : 0) - (Keyboard.leftArrowKey.isPressed ? 1 : 0),
                    (Keyboard.upArrowKey.isPressed ? 1 : 0) - (Keyboard.downArrowKey.isPressed ? 1 : 0));
                Look = Mouse.delta.ReadValue();
                PrimaryPressed = Keyboard.eKey.wasPressedThisFrame;
                PrimaryHeld = Keyboard.eKey.isPressed;
                PrimaryReleased = Keyboard.eKey.wasReleasedThisFrame;
                SecondaryPressed = Keyboard.qKey.wasPressedThisFrame;
                GrabPressed = Keyboard.fKey.wasPressedThisFrame;
                PausePressed = Keyboard.escapeKey.wasPressedThisFrame;
                MenuConfirmPressed = Keyboard.enterKey.wasPressedThisFrame;
                MenuCancelPressed = Keyboard.backspaceKey.wasPressedThisFrame;
                SprintHeld = Keyboard.leftShiftKey.isPressed;
                WaitHeld = Keyboard.tKey.isPressed;
            }
        }

        internal void BindRemote(bool connected)
        {
            Gamepad = null; Keyboard = null; Mouse = null; IsRemote = LookIsDegrees = true;
            DeviceReady = connected; DeviceLabel = connected ? "LAN colleague" : "Waiting for LAN colleague";
            remoteGamepad = false; Clear();
        }

        internal void ApplyRemote(LanInputFrame frame)
        {
            Clear(); DeviceReady = true; IsRemote = LookIsDegrees = true;
            remoteGamepad = frame.gamepadLabels;
            Move = frame.move; Look = frame.lookDegrees; Navigate = frame.navigate;
            PrimaryPressed = frame.primaryPressed; PrimaryHeld = frame.primaryHeld; PrimaryReleased = frame.primaryReleased;
            SecondaryPressed = frame.secondaryPressed; GrabPressed = frame.grabPressed;
            MenuConfirmPressed = frame.menuConfirmPressed; MenuCancelPressed = frame.menuCancelPressed;
            SprintHeld = frame.sprintHeld; WaitHeld = frame.waitHeld;
        }
    }

    [DefaultExecutionOrder(-500)]
    public sealed partial class LocalCoopBootstrap : MonoBehaviour
    {
        public static LocalCoopBootstrap Instance { get; private set; }
        public Transform spawn1;
        public Transform spawn2;
        public FirstPersonController[] Players { get; private set; } = new FirstPersonController[2];
        public bool IsPaused { get; private set; }
        public bool DebugKeyboardOnly { get; private set; }
        public int DebugActorId { get; private set; }
        public bool WaitingForDevices => Players[0] != null && (IsSolo ? !Players[0].DeviceReady : LanRole != LanRole.Offline ?
            Players[LocalActorId] == null || !Players[LocalActorId].DeviceReady :
            Players[1] == null || !Players[0].DeviceReady || !Players[1].DeviceReady);

        private bool manuallyPaused;
        private bool hasFocus = true;
        private bool devicesDirty = true;
        private bool gamepadMode;
        private DeveloperPanel developerPanel;
        private readonly int[] assignedPadIds = { -1, -1 };
        private GUIStyle heading;
        private GUIStyle text;

        private void Awake()
        {
            Application.targetFrameRate = 60;
            Instance = this;
            InputSystem.onDeviceChange += OnDeviceChange;
        }

        private void Start()
        {
            SelectInitialMode();
            developerPanel = GetComponent<DeveloperPanel>();
            Players[0] = CreatePlayer(0, spawn1, new Vector3(-1.4f, 0.1f, 2));
            if (!IsSolo) EnsureSecondPlayer();
            AssignDevices();
            ApplyLanPresentation();
            RefreshPause();
        }

        private FirstPersonController CreatePlayer(int actorId, Transform spawn, Vector3 fallback)
        {
            var rig = new GameObject("Staff " + (actorId + 1));
            rig.transform.SetParent(transform, false);
            rig.transform.SetPositionAndRotation(spawn ? spawn.position : fallback, spawn ? spawn.rotation : Quaternion.identity);
            var controller = rig.AddComponent<FirstPersonController>();
            controller.Initialize(actorId);
            return controller;
        }

        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (device is Gamepad || device is Keyboard || device is Mouse) devicesDirty = true;
        }

        private void AssignDevices()
        {
            devicesDirty = false;
            foreach (var player in Players) if (player && IsLocalActor(player.ActorId)) player.Interactor.CancelInteraction();
            var pads = new List<Gamepad>();
            foreach (var pad in Gamepad.all) if (pad.added && pad.enabled) pads.Add(pad);
            pads.Sort((a, b) => a.deviceId.CompareTo(b.deviceId));
            if (IsSolo)
            {
                // One explicitly assigned device set; extra connected controllers own no actor.
                var pad = pads.Find(candidate => candidate.deviceId == assignedPadIds[0]);
                if (pad == null && pads.Count > 0) pad = pads[0];
                assignedPadIds[0] = pad != null ? pad.deviceId : -1;
                Players[0].Input.Bind(pad, pad == null ? Keyboard.current : null, pad == null ? Mouse.current : null);
                return;
            }
            if (LanRole != LanRole.Offline)
            {
                localPending = new LanInputFrame();
                bool keyboardReady = pads.Count == 0 && Keyboard.current != null && Mouse.current != null;
                for (int i = 0; i < Players.Length; i++)
                    if (i == LocalActorId) Players[i].Input.Bind(keyboardReady ? null : pads.Count > 0 ? pads[0] : null,
                        keyboardReady ? Keyboard.current : null, keyboardReady ? Mouse.current : null);
                    else Players[i].Input.BindRemote(LanRole == LanRole.Host && remoteConnected);
                return;
            }
            if (DebugKeyboardOnly)
            {
                for (int i = 0; i < Players.Length; i++)
                    Players[i].Input.Bind(null, i == DebugActorId ? Keyboard.current : null,
                        i == DebugActorId ? Mouse.current : null, i != DebugActorId);
            }
            else
            {
                // Once two-pad mode is active, a disconnect leaves that actor unassigned and pauses.
                // The remaining pad keeps its actor instead of silently changing to the other staff member.
                if (!gamepadMode && pads.Count >= 2) gamepadMode = true;
                if (gamepadMode && assignedPadIds[0] < 0 && assignedPadIds[1] < 0)
                    assignedPadIds[0] = pads[0].deviceId;
                Gamepad second = pads.Find(p => p.deviceId == assignedPadIds[1]);
                Gamepad first = gamepadMode ? pads.Find(p => p.deviceId == assignedPadIds[0]) : null;
                if (second == null) second = pads.Find(p => p != first);
                if (gamepadMode && first == null) first = pads.Find(p => p != second);
                if (first != null) assignedPadIds[0] = first.deviceId;
                if (second != null) assignedPadIds[1] = second.deviceId;
                Players[0].Input.Bind(first, gamepadMode ? null : Keyboard.current, gamepadMode ? null : Mouse.current);
                Players[1].Input.Bind(second, null, null);
            }
        }

        private void Update()
        {
            if (Players[0] == null) return;
            var keyboard = Keyboard.current;
            if (!IsSolo && LanRole == LanRole.Offline && (Debug.isDebugBuild || Application.isEditor) && keyboard != null)
            {
                if (keyboard.f8Key.wasPressedThisFrame)
                {
                    DebugKeyboardOnly = !DebugKeyboardOnly;
                    manuallyPaused = false;
                    devicesDirty = true;
                }
                if (DebugKeyboardOnly && keyboard.f9Key.wasPressedThisFrame)
                {
                    DebugActorId = 1 - DebugActorId;
                    devicesDirty = true;
                }
            }
            if (devicesDirty) AssignDevices();
            foreach (var player in Players) if (player) player.Input.Read();
            ReadLanInput();
            if (LanRole == LanRole.Offline && !(IsSolo && LanSession.Instance) &&
                (Players[0].Input.PausePressed || Players[1] && Players[1].Input.PausePressed)) manuallyPaused = !manuallyPaused;
            RefreshPause();
            bool mouseNeeded = IsPaused || (Players[0].IsUIBlocked && Players[0].Input.IsMouseLook) ||
                (Players[1] && Players[1].IsUIBlocked && Players[1].Input.IsMouseLook);
            Cursor.lockState = mouseNeeded ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = mouseNeeded;
        }

        private void RefreshPause()
        {
            // LAN focus/device loss suppresses this computer's intentions; it cannot freeze its peer.
            var lan = LanSession.Instance;
            bool next = LanRole == LanRole.Offline ? manuallyPaused || WaitingForDevices || !hasFocus :
                manuallyPaused || (lan && lan.MenuOpen) ||
                (LanRole == LanRole.Client && (remoteHostPaused || lan && !lan.HasSnapshot));
            if (next != IsPaused)
            {
                IsPaused = next;
                if (next) foreach (var player in Players) if (player) player.Interactor.CancelInteraction();
            }
            Time.timeScale = IsPaused ? 0 : 1;
        }

        public void SetPaused(bool paused)
        {
            manuallyPaused = paused;
            RefreshPause();
        }

        private void OnApplicationFocus(bool focus)
        {
            hasFocus = focus;
            if (!focus && LanRole != LanRole.Offline)
                foreach (var player in Players)
                    if (player && IsLocalActor(player.ActorId)) player.Interactor.CancelInteraction();
            if (Players[0] != null) RefreshPause();
        }

        private void OnDestroy()
        {
            InputSystem.onDeviceChange -= OnDeviceChange;
            Time.timeScale = 1;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (Instance == this) Instance = null;
        }

        private void OnGUI()
        {
            if (Players[0] == null) return;
            if (LanRole != LanRole.Offline) return;
            if (LanSession.Instance && LanSession.Instance.MenuOpen) return;
            if (heading == null)
            {
                heading = new GUIStyle(GUI.skin.label) { fontSize = 27, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true };
                heading.normal.textColor = new Color(1, 0.84f, 0.51f);
                text = new GUIStyle(GUI.skin.label) { fontSize = 17, alignment = TextAnchor.MiddleCenter, wordWrap = true };
                text.normal.textColor = new Color(1, 0.94f, 0.8f);
            }
            if (DebugKeyboardOnly)
            {
                GUI.color = new Color(0.18f, 0.06f, 0.02f, 0.96f);
                GUI.DrawTexture(new Rect(Screen.width / 2f - 255, 0, 510, 32), Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(new Rect(Screen.width / 2f - 250, 1, 500, 30), "DEBUG ONLY  •  Actor " + (DebugActorId + 1) + "  •  F9 switch / F8 exit", text);
            }
            if (!IsPaused) return;
            // F2 already explains and owns this pause. Keep device/focus loss visible,
            // but do not cover the developer controls with the ordinary pause card.
            if (developerPanel && developerPanel.IsVisible && !WaitingForDevices && hasFocus) return;
            GUI.depth = -100;
            GUI.color = new Color(0.055f, 0.035f, 0.04f, 0.94f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            float w = Mathf.Min(700, Screen.width - 40), x = (Screen.width - w) / 2, y = Screen.height / 2f - 170;
            GUI.Label(new Rect(x, y, w, 60), WaitingForDevices ? IsSolo ? "SOLO · CONNECT YOUR CONTROLS" : "TWO STAFF. ONE VERY OLD HOTEL." : "SHIFT PAUSED", heading);
            GUI.Label(new Rect(x, y + 65, w, 70), WaitingForDevices ?
                (IsSolo ? "Use one gamepad or keyboard + mouse. The hotel pauses if your assigned controls are unavailable." :
                "Connect two gamepads, or use keyboard + mouse with one gamepad.\nThe whole hotel pauses if either staff member loses their controller.") :
                "Esc / controller Start resumes the shift.", text);
            GUI.Label(new Rect(x, y + 150, w, 60), "STAFF 1  —  " + Players[0].Input.DeviceLabel +
                (Players[1] ? "\nSTAFF 2  —  " + Players[1].Input.DeviceLabel : ""), text);
            GUI.Label(new Rect(x, y + 218, w, 65), "Keyboard: WASD / mouse · E use · Q alternate · F carry · T wait\nGamepad: sticks · A / Cross use · X / Square alternate · RB / R1 carry · Y / Triangle wait", text);
            if (!IsSolo && (Debug.isDebugBuild || Application.isEditor))
                GUI.Label(new Rect(x, y + 290, w, 50), "Developer inspection only: F8 keyboard-only mode; F9 changes actor.\nThis mode does not replace the two-person repair playtest.", text);
            GUI.depth = 0;
        }
    }
}
