using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using static WorstHotel.HotelTheme;

namespace WorstHotel
{
    public sealed partial class ManagementUI
    {
        DiegeticBookInteraction readingBook;
        BookCameraFocus bookCamera;
        int bookPage, bookListPage;
        bool bookDetail;
        string bookHeading, bookCopy, bookLastMessage;
        readonly List<(string title, Action action, bool enabled)> bookChoices = new();
        readonly List<(string text, bool completed)> noteLines = new();
        public HotelBook? ReadingBook => readingBook ? readingBook.kind : null;
        public IEnumerable<string> BookOptionTitles => bookChoices.Select(choice => choice.title);
        public string FocusedBookOption => readingBook && focus >= 0 && focus < bookChoices.Count ? bookChoices[focus].title : null;

        public void OpenBook(int actorId, HotelBook kind)
        {
            if (IsOpen) Close();
            Open(actorId);
            if (!IsOpen) return;
            BindBook(kind);
        }

        void BindBook(HotelBook kind)
        {
            readingBook = DiegeticBookInteraction.Find(kind);
            bookPage = bookListPage = 0; bookDetail = false; bookLastMessage = Session.LastMessage;
            if (!readingBook) return;
            var player = LocalCoopBootstrap.Instance.Players[owner];
            bookCamera = player.PlayerCamera.GetComponent<BookCameraFocus>() ?? player.PlayerCamera.gameObject.AddComponent<BookCameraFocus>();
            bookCamera.Focus(readingBook.transform);
            UpdateBookChoices();
        }

        void ReleaseBook()
        {
            if (bookCamera) bookCamera.Release();
            bookCamera = null; readingBook = null;
        }

        void BookChoice(string title, Action command, bool enabled = true) => bookChoices.Add((title, command, enabled));
        void BookPage(int page) { bookPage = page; bookListPage = 0; bookDetail = false; focus = 0; HotelFeedback.PlayUIClick(); }

        void UpdateBookChoices()
        {
            if (!readingBook || guestContext || wakePhone) return;
            bookChoices.Clear(); noteLines.Clear();
            var model = Session.Simulation;
            switch (readingBook.kind)
            {
                case HotelBook.Reservations: ReservationPages(); break;
                case HotelBook.Services: ServiceNotes(); break;
                case HotelBook.Accounts:
                    bookHeading = "CASH BOOK";
                    bookCopy = "Cash on hand   $" + model.Economy.Cash + "\n\nSINCE " + GuestLabels.HotelMoment(model, model.PeriodStartedAt) +
                        "\n\nCollected at checkout   $" + model.PeriodCheckoutIncome + "\nRoom charges   $" + model.PeriodGross +
                        "\nCredits / refunds   $" + model.PeriodCompensation + "\n\nMaintenance paid   $" + model.PeriodMaintenanceSpend +
                        "\nRenovation paid   $" + model.PeriodCapitalSpend + "\n\nOperating charge   $" + Session.Economy.DailyOperatingCost +
                        "\nDue " + GuestLabels.HotelMoment(model, model.NextReportAt) + "\n\nUnpaid bookings   $" + model.UnpaidBookedRevenue;
                    var reports = Session.Reports.Reverse().ToArray();
                    foreach (var report in reports.Skip(bookListPage * 5).Take(5))
                    {
                        var item = report;
                        BookChoice("Day " + item.DayNumber + " · net $" + item.Net, () =>
                        { operationsReportNumber = item.DayNumber; bookDetail = true; focus = 0; });
                    }
                    if (bookDetail)
                    {
                        var report = reports.FirstOrDefault(r => r.DayNumber == operationsReportNumber);
                        if (report != null) bookCopy = "DAY " + report.DayNumber + "\nNet income   $" + report.Net + "\nClosing cash   $" + report.Cash +
                            "\n\nSETTLED STAYS\n" + string.Join("\n", report.Receipts.Take(8).Select(r => r.RoomId + " · " + r.Name + "   $" + r.Net));
                        BookChoice("Current accounts", () => bookDetail = false);
                    }
                    if (reports.Length > 5) BookChoice("Older pages ›", () => bookListPage = (bookListPage + 1) % ((reports.Length + 4) / 5));
                    break;
                case HotelBook.Renovation:
                    bookHeading = "RENOVATION LEDGER";
                    bookCopy = "Available cash   $" + model.Economy.Cash + "\n\nNEW BURNER\nMore heating capacity; existing wear still needs servicing.\n\nELECTRICAL WORK\nMore capacity on one branch. A serves the west, B the east and service wing.\n\nROOM 102\nSeal the draughty windows.\n\nNORTH WING\nRestore rooms 107–110 beyond the guest gallery. Set their rates in the reservation book after restoration.";
                    operationsChoices.Clear(); UpdateOperationsUpgrades();
                    foreach (var choice in operationsChoices)
                        BookChoice(choice.title, choice.action, choice.enabled);
                    break;
                case HotelBook.BoilerManual:
                    bookHeading = bookPage == 0 ? "ENGINEER'S MANUAL" : "SERVICE RECORD";
                    var boiler = model.Boiler; var tuning = Session.BoilerSettings.Capacity;
                    if (bookPage == 0)
                    {
                        bookCopy = "EMERGENCY RESTART\n\n1. Hold the red relief valve in the green pressure band. Solo: keep holding until its catch engages.\n2. Open the service panel.\n3. Cut boiler power at the red isolator.\n4. Hold latch A fully, then latch B. In co-op, your partner keeps holding relief.\n5. Press the green restart button.\n\nAn emergency patch costs $" + Session.Economy.CheapPatchCost +
                            ". It restores heat but leaves wear.\n\nIf a breaker trips, switch off portable heaters on that branch before resetting it. A: west / odd rooms. B: east / even rooms and service wing.";
                        BookChoice("Inspection and planned service ›", () => BookPage(1));
                    }
                    else
                    {
                        bookCopy = "INSPECTION\n" + BoilerMaintenanceLabels.State(model) + "\nCondition: " + BoilerMaintenanceLabels.Condition(boiler) +
                            "\nLoad: " + BoilerMaintenanceLabels.Load(boiler, tuning) + "\nStress: " + BoilerMaintenanceLabels.Stress(boiler) +
                            "\n\nBASIC SERVICE\n$" + Session.Economy.BasicMaintenanceCost + " · heating off for " + tuning.BasicMaintenanceHours.ToString("0.##") +
                            " hotel hours. Partial care for a working boiler.\n\nFULL SERVICE\n$" + Session.Economy.ProperRepairCost + " · heating off for " + tuning.MaintenanceHours.ToString("0.##") +
                            " hotel hours. Clears wear, stress and patch penalty.\n\nChoose here, then hold the service plate below the pressure gauge. Payment starts with physical setup.";
                        operationsChoices.Clear(); UpdateOperationsMaintenance();
                        foreach (var choice in operationsChoices) BookChoice(choice.title, choice.action, choice.enabled);
                        BookChoice("‹ Emergency procedure", () => BookPage(0));
                    }
                    break;
            }
            BookChoice("Close book", Close);
            actions.Clear(); enabledActions.Clear();
            foreach (var choice in bookChoices) { actions.Add(choice.action); enabledActions.Add(choice.enabled); }
            focus = Mathf.Clamp(focus, 0, Math.Max(0, actions.Count - 1));
        }

        void ReservationPages()
        {
            var model = Session.Simulation;
            bookHeading = "RESERVATIONS · DAY " + Session.Day;
            BookChoice("Today", () => BookPage(0));
            BookChoice("Tomorrow", () => BookPage(1));
            BookChoice("Checkouts / available rooms", () => BookPage(2));
            BookChoice("Room sales / rates", () => BookPage(3));
            BookChoice("Special enquiries (" + model.SpecialEnquiries.Count(e => e.Status == SpecialOfferStatus.Pending) + ")", () => BookPage(4));
            if (bookPage == 4)
            {
                bookChoices.Clear();
                var pending = model.SpecialEnquiries.FirstOrDefault(e => e.Status == SpecialOfferStatus.Pending);
                bookCopy = "SPECIAL CORRESPONDENCE\n\n";
                if (pending == null)
                    bookCopy += "No unanswered enquiries. Unusual travellers write occasionally once the hotel has been operating for a while. Ordinary reservations continue automatically.";
                else
                {
                    bookCopy += pending.Offer.Application.GuestName + "\nOne night · $" + pending.Definition.Payment +
                        "\nArrival " + GuestLabels.HotelMoment(model, pending.Offer.ArrivalAt) + "\nCheckout " +
                        GuestLabels.HotelMoment(model, pending.Offer.CheckoutAt) + "\n\n" + pending.Definition.Note + "\n\nChoose a room to accept this stay.";
                    var available = Session.Rooms.Where(r => model.CanReserveRoom(r.Profile.Id, pending.Offer).Success).ToArray();
                    bookListPage = Mathf.Clamp(bookListPage, 0, Math.Max(0, (available.Length - 1) / 6));
                    foreach (var room in available.Skip(bookListPage * 6).Take(6))
                    {
                        int roomId = room.Profile.Id; int revision = pending.Revision; string id = pending.Offer.Id;
                        BookChoice("Accept · room " + roomId, () => Session.DecideSpecialBooking(owner, id, roomId, true, revision));
                    }
                    if (bookChoices.Count == 0) bookCopy += "\nNo room is free for these dates.";
                    if (available.Length > 6) BookChoice("More rooms ›", () => bookListPage = (bookListPage + 1) % ((available.Length + 5) / 6));
                    BookChoice("Decline politely", () => Session.DecideSpecialBooking(owner, pending.Offer.Id, 0, false, pending.Revision));
                }
                BookChoice("‹ Reservations", () => BookPage(0));
            }
            else if (bookPage <= 1)
            {
                operationsDay = Session.Day + bookPage;
                var entries = OperationsBookingOffers().OrderBy(o => o.ArrivalAt).ToArray();
                bookCopy = (bookPage == 0 ? "TODAY" : "TOMORROW") + "\n\n";
                if (entries.Length == 0) bookCopy += "No reservations entered yet.\n\nOpen rooms are offered automatically at the rates on the sales page.";
                foreach (var entry in entries.Skip(bookListPage * 5).Take(5))
                {
                    var reservation = model.Reservations.FirstOrDefault(r => r.Id == entry.Id);
                    bookCopy += GuestLabels.HotelMoment(model, entry.ArrivalAt) + "\n" + entry.Application.GuestName +
                        (reservation == null ? "" : " · " + reservation.RoomId + " · $" + reservation.Price) + "\n\n";
                }
                if (entries.Length > 5) BookChoice("More arrivals ›", () => bookListPage = (bookListPage + 1) % ((entries.Length + 4) / 5));
                // Revision-checked reservation editing stays in this book.
                if (bookDetail)
                {
                    var reservation = model.Reservations.FirstOrDefault(r => r.Id == operationsOfferId);
                    if (reservation != null)
                    {
                        bookCopy = "BOOKING · " + reservation.RoomId + "\n" + entries.FirstOrDefault(e => e.Id == reservation.Id)?.Application.GuestName +
                            "\nAgreed price $" + reservation.Price + "\n\nChoose another room before arrival. Existing price stays unchanged.";
                        bookChoices.Clear();
                        foreach (var room in Session.Rooms.Where(r => CanPreviewBookingRoom(reservation, r.Profile.Id)))
                        {
                            int id = room.Profile.Id; int revision = reservation.Revision;
                            BookChoice("Move booking to " + id, () => ApplyBookingRoom(reservation.Id, id, revision));
                        }
                        int rev = reservation.Revision;
                        BookChoice("Cancel this reservation", () => Session.CancelBooking(owner, reservation.Id, rev), reservation.Status == ReservationStatus.Reserved);
                        BookChoice("‹ Arrivals", () => bookDetail = false);
                    }
                }
                else foreach (var entry in entries.Skip(bookListPage * 5).Take(5))
                {
                    string id = entry.Id;
                    var reservation = model.Reservations.FirstOrDefault(r => r.Id == id);
                    if (reservation?.Status == ReservationStatus.Reserved && entry.ArrivalAt > model.Elapsed)
                        BookChoice("Amend · " + entry.Application.GuestName, () => { operationsOfferId = id; bookDetail = true; focus = 0; });
                }
            }
            else if (bookPage == 2)
            {
                bookCopy = "CHECKOUTS\n\n" + string.Join("\n", model.Reservations.Where(r => r.Status == ReservationStatus.Arrived)
                    .Select(r => r.RoomId + " · " + GuestLabels.HotelMoment(model, model.Guests.FirstOrDefault(g => g.GuestId == r.Id)?.Agent?.CheckoutTime ?? r.Offer.CheckoutAt))) +
                    "\n\nAVAILABLE NOW\n" + string.Join("  ", Session.Rooms.Where(r => r.Operational && !r.Occupied && !r.Reserved && r.DepartingGuestId == null && r.Cleanliness == Cleanliness.Clean).Select(r => r.Profile.Id.ToString())) +
                    "\n\nPreparation needed\n" + string.Join("  ", Session.Rooms.Where(r => r.Operational && r.Cleanliness != Cleanliness.Clean).Select(r => r.Profile.Id.ToString()));
            }
            else
            {
                bookChoices.Clear();
                bookCopy = "ROOM SALES\n\nOpen rooms are offered at the written rate. Higher prices attract fewer bookings.\n\nChanges affect future sales; existing reservations keep their agreed price.\n\n";
                if (bookDetail)
                {
                    bookCopy += "ROOM " + salesRoomId + "\nDraft: " + (salesOpen ? "OPEN" : "CLOSED") + "\nNightly rate $" + salesPrice;
                    operationsChoices.Clear(); UpdateRoomSalesDraft();
                    foreach (var choice in operationsChoices) BookChoice(choice.title, choice.action, choice.enabled);
                    BookChoice("‹ Room list", () => bookDetail = false);
                }
                else
                {
                    foreach (var policy in model.RoomSalesPolicies.OrderBy(p => p.RoomId))
                    {
                        int id = policy.RoomId;
                        BookChoice(id + " · " + (!model.IsRoomOperational(id) ? "WING CLOSED" : policy.OpenForSale ? "OPEN" : "CLOSED") + " · $" + policy.Price,
                            () => { SelectRoomSales(id); bookDetail = true; }, model.IsRoomOperational(id));
                    }
                    BookChoice("‹ Reservations", () => BookPage(0));
                }
            }
        }

        void ServiceNotes()
        {
            bookHeading = "RECEPTION NOTES";
            bookCopy = "Requests heard by reception.\nCompleted promises are crossed out.";
            var services = Session.Simulation.Services;
            if (services == null) return;
            var notes = services.Cases.Where(GuestLabels.IsKnownToHotel).OrderBy(c => !c.Active).ThenBy(c => c.CreatedAt).ToArray();
            bookListPage = Mathf.Clamp(bookListPage, 0, Math.Max(0, (notes.Length - 1) / 7));
            foreach (var item in notes.Skip(bookListPage * 7).Take(7))
                noteLines.Add((item.RoomId + " · " + GuestLabels.Service(item.Kind, Session.Simulation) + "\n" +
                    (item.Kind == ServiceKind.WakeUpCall || item.Kind == ServiceKind.LateCheckout ? GuestLabels.HotelMoment(Session.Simulation, item.DueTime) + " · " : "") +
                    GuestLabels.ServiceBrief(item, Session.Simulation), item.Status == ServiceStatus.Fulfilled));
            var bags = services.Items.Where(i => i.Kind == ServiceItemKind.Luggage && i.StaffHandling)
                .GroupBy(i => i.GuestId).Where(g => g.Any(i => i.Location != ServiceItemLocation.Delivered)).ToArray();
            if (bags.Length > 0) BookChoice("Luggage promises", () => BookPage(bookPage == 1 ? 0 : 1));
            if (bookPage == 1)
            {
                noteLines.Clear();
                foreach (var batch in bags.Take(7))
                {
                    var guest = Session.Simulation.Guests.FirstOrDefault(g => g.GuestId == batch.Key);
                    noteLines.Add(((guest?.Name ?? batch.Key) + " · room " + guest?.RoomId + "\n" +
                        batch.Count(i => i.Location == ServiceItemLocation.Delivered) + "/" + batch.Count() + " bags delivered · " +
                        batch.Count(i => i.Location == ServiceItemLocation.Stored) + " stored", false));
                }
                BookChoice("‹ Guest messages", () => BookPage(0));
            }
            if (notes.Length == 0 && bags.Length == 0) bookCopy += "\n\nNo messages written yet.";
            if (notes.Length > 7) BookChoice("Turn page ›", () => bookListPage = (bookListPage + 1) % ((notes.Length + 6) / 7));
        }

        bool DrawPhysicalBook()
        {
            if (!readingBook || guestContext || wakePhone) return false;
            if (!bookCamera || !bookCamera.Settled) return true;
            var camera = LocalCoopBootstrap.Instance.Players[owner].PlayerCamera;
            var page = readingBook.ScreenPage(camera);
            if (page.width <= 0 || page.height <= 0) return true;
            var old = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(new Vector3(page.x, page.y, 0), Quaternion.identity, new Vector3(page.width / 1000, page.height / 680, 1));
            Ensure();
            // The ink stays registered to the physical page; the real cover, spine and desk frame it.
            Fill(new Rect(18, 14, 468, 646), Paper);
            Fill(new Rect(514, 14, 468, 646), Paper);
            Label(new Rect(40, 25, 420, 60), bookHeading, Heading, Wine);
            Label(new Rect(40, 101, 420, 492), bookCopy, Body, Ink);
            if (readingBook.kind == HotelBook.Services)
            {
                float y = 164;
                foreach (var note in noteLines)
                {
                    Label(new Rect(40, y, 420, 52), note.text, Small, note.completed ? Muted : Ink);
                    if (note.completed) Fill(new Rect(42, y + 16, 408, 1), Muted);
                    y += 59;
                }
            }
            actions.Clear(); enabledActions.Clear();
            float height = bookChoices.Count > 10 ? 39 : 46;
            for (int i = 0; i < bookChoices.Count; i++)
            {
                var choice = bookChoices[i];
                ButtonAt(new Rect(540, 43 + i * (height + 5), 415, height), choice.title, choice.action, choice.enabled);
            }
            if (Session.LastMessage != bookLastMessage)
                Label(new Rect(540, 594, 415, 62), Session.LastMessage, Small, Wine);
            Label(new Rect(40, 635, 410, 26), "Backspace / B · close     Arrows / Enter · select", Small, Muted);
            GUI.matrix = old;
            return true;
        }
    }
}
