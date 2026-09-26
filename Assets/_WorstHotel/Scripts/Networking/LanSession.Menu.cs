using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using UnityEngine;
using UnityEngine.InputSystem;
using static WorstHotel.HotelTheme;

namespace WorstHotel
{
    public sealed partial class LanSession
    {
        string joinAddress = "127.0.0.1", hostAddresses;
        GUIStyle addressField;
        int menuFocus, menuButtonIndex, menuButtonCount, requestedMenuButton = -1;
        float nextMenuNavigation;

        bool MenuButton(Rect rect, string caption, bool prominent = false)
        {
            int index = menuButtonIndex++;
            Fill(rect, prominent ? Teal : LightPaper);
            Border(rect, Brass, index == menuFocus ? 4 : 2);
            Label(rect, caption, HotelTheme.Button, prominent ? LightPaper : Ink);
            bool activated = GUI.Button(rect, GUIContent.none, GUIStyle.none);
            if (Event.current.type == EventType.Repaint && requestedMenuButton == index)
            { requestedMenuButton = -1; activated = true; }
            return activated;
        }

        void ReadMenuInput()
        {
            if (!coop) return;
            if ((IsActive || IsSolo && soloStarted) && coop.Players[coop.LocalActorId] && coop.Players[coop.LocalActorId].Input.PausePressed)
            { MenuOpen = !MenuOpen; coop.SetPaused(MenuOpen); }
            if (!IsActive && !MenuOpen && Keyboard.current != null && Keyboard.current.f10Key.wasPressedThisFrame)
            { ManagementUI.Instance?.Close(); MenuOpen = true; coop.SetPaused(true); }
            if (MenuOpen)
            {
                Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
                var input = coop.Players[coop.LocalActorId]?.Input;
                if (input == null) return;
                float navigation = Mathf.Abs(input.Navigate.y) > .5f ? -input.Navigate.y : input.Navigate.x;
                if (Mathf.Abs(navigation) < .5f) nextMenuNavigation = 0;
                else if (menuButtonCount > 0 && Time.unscaledTime >= nextMenuNavigation)
                {
                    menuFocus = (menuFocus + (navigation > 0 ? 1 : -1) + menuButtonCount) % menuButtonCount;
                    nextMenuNavigation = Time.unscaledTime + .2f;
                }
                if (input.MenuConfirmPressed && menuButtonCount > 0) requestedMenuButton = menuFocus;
            }
            else requestedMenuButton = -1;
        }

        static string FindHostAddresses()
        {
            try
            {
                var addresses = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(item => item.OperationalStatus == OperationalStatus.Up)
                    .SelectMany(item => item.GetIPProperties().UnicastAddresses)
                    .Select(item => item.Address).Where(ip => ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                    .Select(ip => ip.ToString()).Distinct().Take(4).ToArray();
                return addresses.Length == 0 ? "No LAN address found. Local test: 127.0.0.1" : string.Join("  /  ", addresses);
            }
            catch (NetworkInformationException) { return "Use this computer's local IPv4 address."; }
        }

        void OnGUI()
        {
            if (!coop) return;
            bool connecting = IsClientReplica && !HasSnapshot;
            if (!MenuOpen && !connecting)
            {
                if (IsActive || IsSolo)
                {
                    Ensure();
                    GUI.depth = -80;
                    var oldMatrix = GUI.matrix;
                    GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(Screen.width / 1600f, Screen.height / 900f, 1));
                    Fill(new Rect(820, 16, 760, 35), new Color(.055f, .075f, .065f, .94f));
                    Label(new Rect(834, 22, 732, 25), Status + "   ·   Esc / Start: " + (IsSolo ? "session menu" : "connection menu"), Small, LightPaper);
                    GUI.matrix = oldMatrix;
                    GUI.depth = 0;
                }
                return;
            }
            Ensure();
            if (addressField == null)
            {
                addressField = new GUIStyle(GUI.skin.textField) { fontSize = 24, alignment = TextAnchor.MiddleLeft };
                foreach (var state in new[] { addressField.normal, addressField.focused, addressField.hover, addressField.active })
                { state.textColor = Ink; state.background = Texture2D.whiteTexture; }
            }
            GUI.depth = -400;
            menuButtonIndex = 0;
            var previous = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(Screen.width / 1600f, Screen.height / 900f, 1));
            Fill(new Rect(0, 0, 1600, 900), new Color(.05f, .08f, .07f, .97f));
            Fill(new Rect(280, 115, 1040, 660), Paper); Border(new Rect(292, 127, 1016, 636), Brass, 2);
            Label(new Rect(325, 160, 950, 60), "THE WORST HOTEL EVER", Title);
            Label(new Rect(327, 221, 940, 60), "ONE OLD HOTEL · SOLO OR TWO OWNERS\nSOLO uses one player. HOST / JOIN uses one computer per owner.", Body, Muted);
            if (IsActive)
            {
                Label(new Rect(327, 312, 930, 95), Status, Heading, Wine);
                if (Role == LanRole.Host)
                {
                    if (hostAddresses == null) hostAddresses = FindHostAddresses();
                    Label(new Rect(327, 410, 930, 75), "JOIN ADDRESS\n" + hostAddresses + "   ·   UDP " + Port, Body);
                }
                else Label(new Rect(327, 410, 930, 75), HasSnapshot ? "Your owner and camera are controlled by this computer. The host runs the hotel." : MenuOpen ?
                    "The hotel stays with the host. Check the connection, then return to the main menu and join again." :
                    "Waiting for the host. Check the address and allow the game on the host's private network if Windows asks.", Body);
                if (!connecting && MenuButton(new Rect(327, 523, 930, 58), "Return to hotel", true))
                { MenuOpen = false; coop.SetPaused(false); }
                if (MenuButton(new Rect(327, 599, 930, 58), Role == LanRole.Host ? "End hosting / main menu" : "Disconnect / main menu")) LeaveToMenu();
            }
            else
            {
                if (MenuButton(new Rect(327, 302, 450, 64), "SOLO", true)) StartSolo();
                if (MenuButton(new Rect(797, 302, 460, 64), "HOST GAME", true)) StartHost();
                Label(new Rect(327, 403, 330, 30), "HOST IP ADDRESS", Small, Muted);
                joinAddress = GUI.TextField(new Rect(327, 438, 590, 58), joinAddress, 45, addressField);
                if (MenuButton(new Rect(938, 438, 319, 58), "JOIN GAME", true)) Join(joinAddress.Trim());
                Label(new Rect(327, 518, 930, 52), Status, Small, Muted);
                if (soloStarted && IsSolo && GameSession.Instance && MenuButton(new Rect(327, 574, 930, 50), "Return to solo hotel"))
                { MenuOpen = false; coop.SetPaused(false); }
                if ((Debug.isDebugBuild || Application.isEditor) && MenuButton(new Rect(327, 637, 930, 42), "Development only / local split-screen")) StartLocalMode();
                Label(new Rect(327, 693, 930, 40), "Menu: mouse or arrows / D-pad + Enter / A. Esc / Start returns to the hotel.\nSOLO uses one device. Development split-screen requires two; F10 returns here.", Small, Muted);
            }
            menuButtonCount = menuButtonIndex;
            if (menuFocus >= menuButtonCount) menuFocus = 0;
            GUI.matrix = previous; GUI.depth = 0;
        }
    }
}
