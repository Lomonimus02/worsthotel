using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static WorstHotel.HotelTheme;

namespace WorstHotel
{
    public sealed partial class ManagementUI : MonoBehaviour
    {
        public static ManagementUI Instance { get; private set; }
        public bool IsOpen => owner >= 0;
        public int Owner => owner;
        int owner = -1, focus, openedFrame;
        string selectedBooking, selectedServiceGuest;
        GuestReceipt selectedReview;
        int selectedRoom = 101, proposedPrice = 180;
        float nextNavigation;
        Action pending;
        readonly List<Action> actions = new List<Action>();
        readonly List<bool> enabledActions = new List<bool>();
        GameSession Session => GameSession.Instance;
        bool openInitially = true;
        void Awake() => Instance = this;

        public void Open(int actorId)
        {
            if (IsOpen || actorId < 0 || actorId > 1 || Session == null || Session.Plan == null) return;
            if (LocalCoopBootstrap.Instance && !LocalCoopBootstrap.Instance.IsLocalActor(actorId)) return;
            owner = actorId; focus = 0; openedFrame = Time.frameCount;
            selectedReview = null;
            selectedServiceGuest = null;
            choosingMoveRoom = false;
            showingHousekeeping = false;
            guestContext = false;
            showingServiceBoard = false; wakePhone = false; selectedServiceCase = null; callingPromise = null;
            showingOperations = Session.Simulation.ContinuousOperations;
            operationsPage = OperationsPage.Overview; operationsOfferId = null; operationsListPage = 0;
            var coop = LocalCoopBootstrap.Instance;
            if (coop) foreach (var p in coop.Players) if (p && coop.IsLocalActor(p.ActorId)) p.SetUIBlocked(Session.Phase != DayPhase.Service || p.ActorId == owner);
            selectedBooking = Session.Plan.Applications.FirstOrDefault()?.Id;
            SelectBooking(selectedBooking);
            actions.Clear(); enabledActions.Clear();
        }

        public void Close()
        {
            if (guestContext && Session && owner >= 0 && selectedServiceGuest != null)
                Session.CloseGuestConversation(owner, selectedServiceGuest);
            if (wakePhone && Session && owner >= 0) Session.CloseWakePhone(owner);
            owner = -1; pending = null; selectedServiceGuest = null; choosingMoveRoom = false; showingHousekeeping = false; guestContext = false;
            showingServiceBoard = false; wakePhone = false; selectedServiceCase = null; callingPromise = null;
            showingOperations = false;
            var coop = LocalCoopBootstrap.Instance;
            if (coop) foreach (var p in coop.Players) if (p && coop.IsLocalActor(p.ActorId)) p.SetUIBlocked(false);
        }

        void Update()
        {
            var coop = LocalCoopBootstrap.Instance;
            if (!coop || Session == null || Session.Plan == null) return;
            if (openInitially && !coop.IsPaused && coop.Players[0] != null)
            { openInitially = false; Open(coop.LocalActorId); }
            if (!IsOpen || coop.IsPaused || Time.frameCount <= openedFrame + 1) return;
            if (owner >= coop.Players.Length || !coop.Players[owner] || !coop.IsLocalActor(owner)) { Close(); return; }
            UpdateGuestContext();
            UpdateServicePanel();
            UpdateOperationsPanel();
            if (!IsOpen) return;
            var input = coop.Players[owner].Input;
            if (pending != null) { var execute = pending; pending = null; HotelFeedback.PlayUIClick(); execute(); return; }
            if (input.MenuCancelPressed && IsOperationsOpen) { OperationsBack(); return; }
            if (input.MenuCancelPressed) { if (guestContext || wakePhone) Close(); else if (showingServiceBoard && selectedServiceCase != null) { selectedServiceCase = null; serviceHasResponse = false; focus = 0; } else if (showingServiceBoard) Close(); else if (selectedReview != null) selectedReview = null; else if (showingHousekeeping) { showingHousekeeping = false; focus = 0; } else if (choosingMoveRoom) { choosingMoveRoom = false; focus = 0; } else if (selectedServiceGuest != null) { selectedServiceGuest = null; focus = 0; } else Close(); return; }
            float navigation = Mathf.Abs(input.Navigate.y) > .5f ? -input.Navigate.y : input.Navigate.x;
            if (Mathf.Abs(navigation) < .5f) nextNavigation = 0;
            else if (Time.unscaledTime >= nextNavigation && actions.Count > 0)
            {
                int direction = navigation > 0 ? 1 : -1;
                for (int i = 0; i < actions.Count; i++)
                { focus = (focus + direction + actions.Count) % actions.Count; if (enabledActions[focus]) break; }
                nextNavigation = Time.unscaledTime + .20f;
            }
            if (input.MenuConfirmPressed && focus >= 0 && focus < actions.Count && enabledActions[focus]) pending = actions[focus];
        }

        void SelectBooking(string id)
        {
            selectedBooking = id;
            var booking = Session.Plan.Applications.FirstOrDefault(x => x.Id == id);
            if (booking != null)
            {
                int step = Session.Economy.PriceStep;
                int minimum = Session.Economy.MinPrice;
                proposedPrice = Mathf.Clamp(minimum + Mathf.FloorToInt((booking.ReferencePrice - minimum + step / 2f) / step) * step, minimum, MaximumGridPrice);
            }
        }

        int MaximumGridPrice => Session.Economy.MinPrice + ((Session.Economy.MaxPrice - Session.Economy.MinPrice) / Session.Economy.PriceStep) * Session.Economy.PriceStep;

        bool ButtonAt(Rect rect, string text, Action action, bool enabled = true, bool selected = false, bool important = false)
        {
            int index = actions.Count; actions.Add(action); enabledActions.Add(enabled);
            Color background = important ? Wine : selected ? Teal : LightPaper;
            if (!enabled) background = new Color(.80f, .77f, .67f);
            Fill(rect, background);
            Border(rect, index == focus && enabled ? Brass : new Color(.62f, .54f, .38f), index == focus ? 3 : 1);
            Label(rect, text, HotelTheme.Button, (important || selected) && enabled ? LightPaper : Ink);
            bool clicked = GUI.Button(rect, GUIContent.none, GUIStyle.none);
            var staff = LocalCoopBootstrap.Instance;
            bool mouseOwner = staff && owner >= 0 && owner < staff.Players.Length && staff.Players[owner] && staff.Players[owner].Input.IsMouseLook;
            if (clicked && enabled && mouseOwner) { focus = index; pending = action; }
            return clicked && enabled && mouseOwner;
        }

        void OnGUI()
        {
            if (!IsOpen || Session == null || Session.Plan == null) return;
            Ensure();
            GUI.depth = -30;
            var matrix = GUI.matrix;
            actions.Clear(); enabledActions.Clear();
            if (Session.Phase == DayPhase.Service || showingServiceBoard || wakePhone)
            {
                bool singleView = LocalCoopBootstrap.Instance && (LocalCoopBootstrap.Instance.IsSolo || LocalCoopBootstrap.Instance.LanRole != LanRole.Offline);
                GUI.matrix = Matrix4x4.TRS(new Vector3(singleView ? Screen.width * .25f : owner * Screen.width / 2f, 0, 0), Quaternion.identity, new Vector3(Screen.width / 1600f, Screen.height / 900f, 1));
                if (guestContext) DrawGuestContext(); else if (showingServiceBoard || wakePhone) DrawServicePanel();
                else if (IsOperationsOpen) DrawOperations(); else DrawServiceLedger();
            }
            else
            {
                GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(Screen.width / 1600f, Screen.height / 900f, 1));
                if (selectedReview != null) DrawReview();
                else if (Session.Phase == DayPhase.Planning) { if (showingHousekeeping) DrawHousekeeping(); else DrawPlanning(); }
                else if (Session.Phase == DayPhase.Maintenance) DrawMaintenance();
                else if (Session.Phase == DayPhase.Results) DrawResults();
                else DrawSettlement();
            }
            GUI.matrix = matrix; GUI.depth = 0;
        }

        void DrawPlanning()
        {
            Fill(new Rect(0, 0, 1600, 900), new Color(.08f, .09f, .07f, .89f));
            Fill(new Rect(38, 30, 1524, 840), Paper);
            Border(new Rect(48, 40, 1504, 820), Brass, 2);
            Fill(new Rect(60, 52, 1480, 6), Wine);
            Label(new Rect(80, 69, 1100, 48), "THE WORST HOTEL EVER", Title);
            Label(new Rect(82, 120, 1060, 30), "RECEPTION LEDGER  /  DAY " + Session.Day + " OF 3  /  Planning", Small, Muted);
            Label(new Rect(1000, 119, 245, 28), "BOILER  " + Session.Simulation.Boiler.Condition.ToString("F0") + "% condition", Small, Session.Simulation.Boiler.Condition < 50 ? Red : Teal);
            ButtonAt(new Rect(1270, 82, 230, 44), "Walk the hotel  ›", Close);
            Label(new Rect(82, 165, 345, 38), "01   APPLICATIONS", Heading);
            Label(new Rect(450, 165, 655, 38), "02   ROOMS & ASSIGNMENTS", Heading);
            Label(new Rect(1135, 165, 355, 38), "03   THE OFFER", Heading);
            Fill(new Rect(429, 165, 1, 515), Brass); Fill(new Rect(1108, 165, 1, 515), Brass);

            int i = 0;
            foreach (var application in Session.Plan.Applications)
            {
                var captured = application;
                bool accepted = Session.Plan.Assignments.Any(x => x.BookingId == application.Id);
                string text = application.GuestName + "   " + (accepted ? "[BOOKED]" : "$" + application.ReferencePrice) + "\n" + application.Archetype.Label;
                ButtonAt(new Rect(82, 212 + i * 55, 328, 49), text, () => SelectBooking(captured.Id), !Session.PlanCommitted, selectedBooking == application.Id);
                i++;
            }
            ButtonAt(new Rect(82, 664, 328, 44), "Room preparation / linen", OpenHousekeeping);

            for (int r = 0; r < Session.Rooms.Length; r++)
            {
                var room = Session.Rooms[r];
                var assignment = Session.Plan.Assignments.FirstOrDefault(x => x.RoomId == room.Profile.Id);
                var booking = assignment != null ? Session.Plan.Applications.First(x => x.Id == assignment.BookingId) : null;
                float x = 450 + (r % 2) * 329, y = 212 + (r / 2) * 153;
                bool selected = selectedRoom == room.Profile.Id;
                Fill(new Rect(x, y, 313, 142), selected ? new Color(.85f, .87f, .74f) : LightPaper);
                Border(new Rect(x, y, 313, 142), selected ? Teal : Brass, selected ? 3 : 1);
                Label(new Rect(x + 12, y + 7, 95, 30), room.Profile.Id + "/" + room.CircuitId, Heading);
                Label(new Rect(x + 106, y + 9, 195, 23), booking != null ? booking.GuestName : room.Cleanliness == Cleanliness.Clean && room.DepartingGuestId == null ? "READY" : "NEEDS PREPARATION", Small, booking != null ? Wine : Muted);
                Label(new Rect(x + 12, y + 42, 288, 45), room.Profile.Label + "\n" + room.Temperature.ToString("F1") + "°C  /  " + room.RepairState + "  /  " + room.Cleanliness, Small);
                Label(new Rect(x + 12, y + 78, 288, 19), PlanningNoiseLabel(room), Small, Muted);
                int id = room.Profile.Id;
                ButtonAt(new Rect(x + 12, y + 98, 289, 32), assignment != null ? "Selected rate: $" + assignment.Price : "Select room", () => { selectedRoom = id; if (assignment != null) { SelectBooking(assignment.BookingId); proposedPrice = assignment.Price; } }, !Session.PlanCommitted, selected);
            }

            DrawPlanningNeighbours();
            DrawPlanningElectricity();
            var chosen = Session.Plan.Applications.FirstOrDefault(x => x.Id == selectedBooking);
            if (chosen != null)
            {
                var guest = chosen.Archetype;
                Label(new Rect(1135, 210, 352, 35), chosen.GuestName, Heading);
                Label(new Rect(1135, 250, 352, 39), GuestLabels.Traits(guest.Traits), Small);
                Label(new Rect(1135, 290, 352, 35), GuestLabels.Tendencies(guest), Small, Muted);
                Label(new Rect(1135, 325, 352, 96), "Reference price   $" + chosen.ReferencePrice + "\nPrefers   " + guest.Needs.PreferredTemperatureMin.ToString("F0") + "–" + guest.Needs.PreferredTemperatureMax.ToString("F0") + "°C\nNoise tolerance   " + (guest.Needs.NoiseTolerance <= .3f ? "Low" : guest.Needs.NoiseTolerance >= .5f ? "High" : "Average") + "\nPatience   " + (guest.Needs.PatienceSeconds >= 75 ? "Patient" : guest.Needs.PatienceSeconds < 50 ? "Short" : "Average"), Small);
                Label(new Rect(1135, 427, 352, 26), "OFFER FOR ROOM " + selectedRoom, Small, Muted);
                ButtonAt(new Rect(1135, 462, 58, 47), "−", () => proposedPrice = Mathf.Max(Session.Economy.MinPrice, proposedPrice - Session.Economy.PriceStep), !Session.PlanCommitted);
                Label(new Rect(1198, 463, 221, 45), "$" + proposedPrice, Title);
                ButtonAt(new Rect(1430, 462, 58, 47), "+", () => proposedPrice = Mathf.Min(MaximumGridPrice, proposedPrice + Session.Economy.PriceStep), !Session.PlanCommitted);
                Label(new Rect(1135, 520, 352, 48), "Higher prices raise expectations. A poor room sold dearly costs satisfaction.", Small, Muted);
                ButtonAt(new Rect(1135, 579, 352, 45), "Assign guest to room " + selectedRoom, () => Session.Assign(owner, selectedBooking, selectedRoom, proposedPrice), !Session.PlanCommitted, false, true);
                bool roomAssigned = Session.Plan.Assignments.Any(a => a.RoomId == selectedRoom);
                bool selectedOccupant = Session.Plan.Assignments.Any(a => a.RoomId == selectedRoom && a.BookingId == selectedBooking);
                ButtonAt(new Rect(1135, 632, 170, 36), "Update price", () => Session.SetPrice(owner, selectedRoom, proposedPrice), selectedOccupant && !Session.PlanCommitted);
                ButtonAt(new Rect(1317, 632, 170, 36), "Remove booking", () => Session.Remove(owner, selectedRoom), roomAssigned && !Session.PlanCommitted);
            }
            Fill(new Rect(82, 727, 1418, 2), Brass);
            Label(new Rect(82, 743, 260, 30), "CASH   $" + Session.Cash.ToString("F0"), Heading);
            Label(new Rect(365, 743, 355, 30), "PROJECTED GROSS   $" + Session.Plan.ProjectedGross, Heading);
            float demand = Session.Plan.ProjectedLoad;
            float safe = Session.BoilerSettings.SafeLoad;
            Label(new Rect(745, 739, 470, 33), "BASE HEATING   " + demand.ToString("F2") + " / " + safe.ToString("F2") + " safe", Heading, demand > safe ? Red : Teal);
            Meter(new Rect(747, 780, 434, 7), demand / (safe * 1.5f), demand > safe ? Red : Teal);
            Label(new Rect(747, 798, 440, 27), demand > safe ? "OVER CAPACITY — heat loss and wear expected" : "Showers raise demand; condition still matters", Small, demand > safe ? Red : Muted);
            ButtonAt(new Rect(1240, 745, 260, 56), "START SHIFT  ›", () => Session.CommitPlan(owner), Session.Plan.Assignments.Count > 0 && !Session.PlanCommitted, false, true);
            Label(new Rect(82, 789, 625, 25), "Operating cost $" + Session.Economy.DailyOperatingCost + "   /   Before refunds $" + (Session.Plan.ProjectedGross - Session.Economy.DailyOperatingCost), Small, Muted);
            Label(new Rect(82, 812, 625, 22), Session.LastMessage, Small, Wine);
            Label(new Rect(82, 833, 1370, 22), "Staff " + (owner + 1) + " operates the shared ledger  •  Mouse or D-pad / arrows  •  Enter / A confirms  •  Backspace / B closes", Small, Muted);
        }

        void OnDestroy() { Close(); if (Instance == this) Instance = null; }
    }
}
