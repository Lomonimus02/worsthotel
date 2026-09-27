using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static WorstHotel.HotelTheme;

namespace WorstHotel
{
    public sealed partial class ManagementUI
    {
        enum OperationsPage { Overview, Bookings, Offer, Reports, Report, Maintenance, Upgrades, Forecast }
        bool showingOperations;
        OperationsPage operationsPage;
        int operationsDay, operationsListPage, operationsReportNumber, operationsPrice, operationsReceiptPage;
        string operationsOfferId;
        readonly List<(Rect rect, string title, Action action, bool enabled)> operationsChoices = new();
        public bool IsOperationsOpen => IsOpen && showingOperations && !guestContext && !wakePhone && !showingServiceBoard && !showingHousekeeping && selectedServiceGuest == null;
        public IEnumerable<string> OperationsOptionTitles => operationsChoices.Select(choice => choice.title);
        public string FocusedOperationsOption => IsOperationsOpen && focus >= 0 && focus < operationsChoices.Count ? operationsChoices[focus].title : null;

        void ShowOperations()
        {
            showingOperations = true; operationsPage = OperationsPage.Overview;
            showingServiceBoard = false; showingHousekeeping = false; selectedServiceGuest = null;
            selectedReview = null; operationsOfferId = null; operationsListPage = 0; focus = 0;
            UpdateOperationsPanel();
        }

        void ShowOperationsBookings(int day)
        {
            operationsDay = day; operationsListPage = 0; operationsOfferId = null;
            operationsPage = OperationsPage.Bookings; focus = 0;
        }

        void SelectOperationsOffer(string id)
        {
            var offer = Session.Simulation.BookingOffers.FirstOrDefault(item => item.Id == id);
            if (offer == null) return;
            operationsOfferId = id; operationsPage = OperationsPage.Offer; focus = 0;
            var reservation = Session.Simulation.Reservations.FirstOrDefault(item => item.Id == id);
            int min = Session.Economy.MinPrice, step = Session.Economy.PriceStep;
            operationsPrice = reservation?.Price ?? Mathf.Clamp(min + Mathf.RoundToInt((offer.Application.ReferencePrice - min) / (float)step) * step, min, MaximumGridPrice);
            selectedRoom = reservation?.RoomId ?? Session.Rooms.FirstOrDefault(room =>
                Session.Simulation.CanReserveRoom(room.Profile.Id, offer).Success)?.Profile.Id ?? 101;
        }

        void AddOperationsChoice(float x, float y, float width, float height, string title, Action action, bool enabled = true) =>
            operationsChoices.Add((new Rect(x, y, width, height), title, action, enabled));

        void UpdateOperationsPanel()
        {
            if (!IsOperationsOpen || !Session.Simulation.ContinuousOperations) return;
            var simulation = Session.Simulation;
            operationsChoices.Clear();
            if (operationsPage == OperationsPage.Overview)
            {
                AddOperationsChoice(42, 196, 342, 43, "Today's bookings", () => ShowOperationsBookings(Session.Day));
                AddOperationsChoice(405, 196, 342, 43, "Tomorrow's bookings", () => ShowOperationsBookings(Session.Day + 1));
                AddOperationsChoice(42, 633, 342, 43, "Guests / conversations", () => { showingOperations = false; focus = 0; });
                AddOperationsChoice(405, 633, 342, 43, "Service board / promises", () => { showingOperations = false; ShowServices(); }, simulation.Services != null);
                AddOperationsChoice(42, 689, 342, 43, "Room preparation", () => { showingOperations = false; OpenHousekeeping(); });
                AddOperationsChoice(405, 689, 342, 43, "Daily reports", () => { operationsPage = OperationsPage.Reports; operationsListPage = 0; focus = 0; });
                AddOperationsChoice(42, 745, 342, 42, "Boiler maintenance", () => { operationsPage = OperationsPage.Maintenance; focus = 0; });
                AddOperationsChoice(405, 745, 342, 42, "Capacity upgrades", () => { operationsPage = OperationsPage.Upgrades; focus = 0; });
            }
            else if (operationsPage == OperationsPage.Bookings)
            {
                AddOperationsChoice(42, 196, 342, 40, "TODAY · Day " + Session.Day, () => ShowOperationsBookings(Session.Day));
                AddOperationsChoice(405, 196, 342, 40, "TOMORROW · Day " + (Session.Day + 1), () => ShowOperationsBookings(Session.Day + 1));
                var offers = simulation.BookingOffers.Where(item => item.ArrivalDay == operationsDay).OrderBy(item => item.ArrivalAt).ToArray();
                operationsListPage = Mathf.Clamp(operationsListPage, 0, Math.Max(0, (offers.Length - 1) / 6));
                int row = 0;
                foreach (var offer in offers.Skip(operationsListPage * 6).Take(6))
                {
                    string id = offer.Id;
                    var reservation = simulation.Reservations.FirstOrDefault(item => item.Id == id);
                    string state = reservation != null ? reservation.Status + " · room " + reservation.RoomId + " · $" + reservation.Price : "$" + offer.Application.ReferencePrice + " reference rate";
                    AddOperationsChoice(42, 249 + row++ * 65, 705, 57, offer.Application.GuestName + " · " + state +
                        "\nArrive " + GuestLabels.HotelMoment(simulation, offer.ArrivalAt), () => SelectOperationsOffer(id));
                }
                if (offers.Length > 6)
                    AddOperationsChoice(42, 647, 705, 35, "More bookings ›", () => { operationsListPage = (operationsListPage + 1) % ((offers.Length + 5) / 6); focus = 0; });
            }
            else if (operationsPage == OperationsPage.Offer)
            {
                var offer = simulation.BookingOffers.FirstOrDefault(item => item.Id == operationsOfferId);
                if (offer == null) { operationsPage = OperationsPage.Bookings; UpdateOperationsPanel(); return; }
                var reservation = simulation.Reservations.FirstOrDefault(item => item.Id == offer.Id);
                bool editable = reservation != null && reservation.Status == ReservationStatus.Reserved && offer.ArrivalAt > simulation.Elapsed;
                bool available = reservation == null && offer.ArrivalAt > simulation.Elapsed;
                for (int i = 0; i < Session.Rooms.Length; i++)
                {
                    var room = Session.Rooms[i]; int roomId = room.Profile.Id;
                    bool canReserve = available && simulation.CanReserveRoom(roomId, offer).Success;
                    AddOperationsChoice(42 + i % 2 * 363, 414 + i / 2 * 46, 342, 40,
                        (roomId == selectedRoom ? "● " : "") + "Room " + roomId + " · " + room.Profile.Label,
                        () => selectedRoom = roomId, canReserve);
                }
                AddOperationsChoice(42, 556, 90, 39, "− $" + Session.Economy.PriceStep,
                    () => operationsPrice = Mathf.Max(Session.Economy.MinPrice, operationsPrice - Session.Economy.PriceStep), (available || editable) && operationsPrice > Session.Economy.MinPrice);
                AddOperationsChoice(657, 556, 90, 39, "+ $" + Session.Economy.PriceStep,
                    () => operationsPrice = Mathf.Min(MaximumGridPrice, operationsPrice + Session.Economy.PriceStep), (available || editable) && operationsPrice < MaximumGridPrice);
                AddOperationsChoice(42, 603, 705, 34, BookingForecastChoiceTitle(),
                    () => { operationsPage = OperationsPage.Forecast; focus = 0; });
                if (reservation == null)
                    AddOperationsChoice(42, 640, 705, 45, "Accept booking · room " + selectedRoom + " · $" + operationsPrice,
                        () => Session.AcceptBooking(owner, offer.Id, selectedRoom, operationsPrice), available && simulation.CanReserveRoom(selectedRoom, offer).Success);
                else
                {
                    int revision = reservation.Revision;
                    AddOperationsChoice(42, 640, 342, 45, "Update agreed price", () => Session.SetBookingPrice(owner, reservation.Id, operationsPrice, revision), editable && operationsPrice != reservation.Price);
                    AddOperationsChoice(405, 640, 342, 45, "Cancel reservation", () => Session.CancelBooking(owner, reservation.Id, revision), editable);
                }
            }
            else if (operationsPage == OperationsPage.Reports)
            {
                var available = Session.Reports.Reverse().ToArray();
                operationsListPage = Mathf.Clamp(operationsListPage, 0, Math.Max(0, (available.Length - 1) / 5));
                int row = 0;
                foreach (var report in available.Skip(operationsListPage * 5).Take(5))
                {
                    int number = report.DayNumber;
                    AddOperationsChoice(42, 394 + row++ * 54, 705, 50, "Operating report " + number + " · net $" + report.Net +
                        "\n" + report.Receipts.Count + " stays settled · cash $" + report.Cash,
                        () => { operationsReportNumber = number; operationsReceiptPage = 0; operationsPage = OperationsPage.Report; selectedReview = null; focus = 0; });
                }
                if (available.Length > 5)
                    AddOperationsChoice(42, 665, 705, 28, "Older reports ›", () => { operationsListPage = (operationsListPage + 1) % ((available.Length + 4) / 5); focus = 0; });
            }
            else if (operationsPage == OperationsPage.Report)
            {
                var report = Session.Reports.FirstOrDefault(item => item.DayNumber == operationsReportNumber);
                if (report != null && selectedReview == null)
                {
                    int row = 0;
                    foreach (var receipt in report.Receipts.Skip(operationsReceiptPage * 6).Take(6))
                    {
                        var review = receipt;
                        AddOperationsChoice(42, 292 + row++ * 62, 705, 54, receipt.RoomId + " · " + receipt.Name + " · satisfaction " + receipt.Satisfaction.ToString("F0") +
                            "/100\n" + (receipt.EarlyCheckout ? "EARLY CHECKOUT · " : "") + "Paid $" + receipt.Net + " · read review ›",
                            () => { selectedReview = review; focus = 0; });
                    }
                    if (report.Receipts.Count > 6)
                        AddOperationsChoice(42, 667, 705, 27, "More guest receipts ›", () =>
                        { operationsReceiptPage = (operationsReceiptPage + 1) % ((report.Receipts.Count + 5) / 6); focus = 0; });
                }
            }
            if (operationsPage == OperationsPage.Maintenance) UpdateOperationsMaintenance();
            if (operationsPage == OperationsPage.Upgrades) UpdateOperationsUpgrades();
            if (operationsPage != OperationsPage.Overview)
                AddOperationsChoice(42, 746, 705, 42, operationsPage == OperationsPage.Forecast ? "Back to booking" : operationsPage == OperationsPage.Offer ? "Back to bookings" :
                    selectedReview != null ? "Back to report" : operationsPage == OperationsPage.Report ? "Back to reports" : "Back to operations", OperationsBack);
            AddOperationsChoice(42, 799, 705, 42, "Close / keep working", Close);
            actions.Clear(); enabledActions.Clear();
            foreach (var choice in operationsChoices) { actions.Add(choice.action); enabledActions.Add(choice.enabled); }
            focus = Mathf.Clamp(focus, 0, Math.Max(0, actions.Count - 1));
            if (actions.Count > 0 && !enabledActions[focus])
            { int first = enabledActions.FindIndex(value => value); if (first >= 0) focus = first; }
        }

        void OperationsBack()
        {
            if (selectedReview != null) selectedReview = null;
            else if (operationsPage == OperationsPage.Forecast) operationsPage = OperationsPage.Offer;
            else if (operationsPage == OperationsPage.Offer) operationsPage = OperationsPage.Bookings;
            else if (operationsPage == OperationsPage.Report) operationsPage = OperationsPage.Reports;
            else if (operationsPage == OperationsPage.Overview) Close();
            else operationsPage = OperationsPage.Overview;
            focus = 0;
        }

        void DrawOperations()
        {
            var simulation = Session.Simulation;
            Fill(new Rect(15, 50, 770, 820), Paper); Border(new Rect(23, 58, 754, 804), Brass);
            Label(new Rect(42, 78, 705, 48), operationsPage == OperationsPage.Overview ? "HOTEL OPERATIONS" :
                operationsPage == OperationsPage.Bookings ? "DATED BOOKINGS" : operationsPage == OperationsPage.Offer ? "ONE-NIGHT BOOKING" :
                operationsPage == OperationsPage.Forecast ? "BOOKING FORECAST" : operationsPage == OperationsPage.Maintenance ? "BOILER MAINTENANCE" : operationsPage == OperationsPage.Upgrades ? "CAPACITY UPGRADES" : "OPERATING REPORTS", Title);
            Label(new Rect(42, 135, 705, 49), GuestLabels.HotelMoment(simulation, simulation.Elapsed) + " · Cash $" + Session.Cash.ToString("F0") +
                "\nThe hotel keeps running while you read and decide.", Small, Muted);
            if (operationsPage == OperationsPage.Overview) DrawOperationsOverview();
            else if (operationsPage == OperationsPage.Offer) DrawOperationsOffer();
            else if (operationsPage == OperationsPage.Bookings && !simulation.BookingOffers.Any(item => item.ArrivalDay == operationsDay))
                Label(new Rect(42, 274, 705, 70), "No applications for Day " + operationsDay + ".", Body, Muted);
            else if (operationsPage == OperationsPage.Reports) DrawCurrentOperationsFinance();
            else if (operationsPage == OperationsPage.Report) DrawOperatingReport();
            else if (operationsPage == OperationsPage.Maintenance) DrawOperationsMaintenance();
            else if (operationsPage == OperationsPage.Upgrades) DrawOperationsUpgrades();
            else if (operationsPage == OperationsPage.Forecast) DrawBookingForecast();
            if (operationsPage != OperationsPage.Overview)
                Label(new Rect(42, 699, 705, 41), string.IsNullOrEmpty(Session.LastMessage) ? "" : "Last update: " + Session.LastMessage, Small, Wine);
            foreach (var choice in operationsChoices) ButtonAt(choice.rect, choice.title, choice.action, choice.enabled);
        }

        void DrawOperationsOffer()
        {
            var simulation = Session.Simulation;
            var offer = simulation.BookingOffers.FirstOrDefault(item => item.Id == operationsOfferId);
            if (offer == null) return;
            var reservation = simulation.Reservations.FirstOrDefault(item => item.Id == offer.Id);
            Label(new Rect(42, 196, 705, 38), offer.Application.GuestName + " · " + offer.Application.Archetype.Label, Heading);
            Label(new Rect(42, 240, 705, 59), "ARRIVE " + GuestLabels.HotelMoment(simulation, offer.ArrivalAt) +
                "\nCHECKOUT " + GuestLabels.HotelMoment(simulation, offer.CheckoutAt), Body);
            Label(new Rect(42, 306, 705, 62), GuestLabels.Traits(offer.Application.Archetype.Traits) + " · " + GuestLabels.Tendencies(offer.Application.Archetype) +
                "\nReference rate $" + offer.Application.ReferencePrice + " · prefers " + offer.Application.Archetype.Needs.PreferredTemperatureMin.ToString("F0") +
                "–" + offer.Application.Archetype.Needs.PreferredTemperatureMax.ToString("F0") + "°C", Small, Muted);
            var room = Session.Rooms.First(item => item.Profile.Id == selectedRoom);
            Label(new Rect(42, 373, 705, 39), reservation != null ? reservation.Status + " · room " + reservation.RoomId + " · agreed $" + reservation.Price :
                "Room " + selectedRoom + " now: " + PreparationStatus(room, simulation.Housekeeping?.Find(selectedRoom)) + " · future dates checked separately", Small, reservation != null ? Teal : Muted);
            Label(new Rect(152, 554, 485, 42), "OFFER  $" + operationsPrice, Heading);
        }

        void DrawOperatingReport()
        {
            var report = Session.Reports.FirstOrDefault(item => item.DayNumber == operationsReportNumber);
            if (report == null) { Label(new Rect(42, 236, 705, 72), "This report is no longer retained.", Body); return; }
            if (selectedReview != null)
            {
                Label(new Rect(42, 200, 705, 68), selectedReview.RoomId + " · " + selectedReview.Name + "\nSatisfaction " + selectedReview.Satisfaction.ToString("F0") + "/100 · paid $" + selectedReview.Net, Heading);
                string earlyCheckout = GuestLabels.EarlyCheckoutReceiptSummary(selectedReview, Session.Simulation);
                if (earlyCheckout != null)
                    Label(new Rect(42, 278, 705, 101), earlyCheckout + "\nAgreed $" + selectedReview.Price + " − credits/refunds $" +
                        selectedReview.Compensation + " = paid $" + selectedReview.Net, Small, Wine);
                Label(new Rect(42, earlyCheckout != null ? 392 : 306, 705, earlyCheckout != null ? 284 : 370), "“" + selectedReview.Review + "”", Body);
                return;
            }
            Label(new Rect(42, 195, 705, 86), "PERIOD " + report.DayNumber + " · Revenue $" + report.Gross + " − credits/refunds $" + report.Compensation +
                "\nOperations $" + report.OperatingCost + " · maintenance $" + report.MaintenanceSpend + " · capital $" + report.CapitalSpend +
                "\nNet $" + report.Net + " · opening cash $" + report.OpeningCash + " → closing $" + report.Cash +
                "\nThese costs are already posted. Existing stays and physical work continue.", Small, Muted);
            if (report.Receipts.Count == 0) Label(new Rect(42, 317, 705, 80), "No stays settled during this period.\nGuests still staying will pay when their own stay ends.", Body, Muted);
        }
    }
}
