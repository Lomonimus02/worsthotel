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
        int accountsTextPage, accountsReceiptPage;
        GuestReceipt accountsReceipt;
        bool accountsReceiptNotes;
        bool bookDetail;
        string bookHeading, bookCopy, bookLastMessage;
        readonly List<(string title, Action action, bool enabled)> bookChoices = new();
        readonly List<(string text, bool completed)> noteLines = new();
        HotelSimulation accountsModel;
        TextMesh accountsTitle;
        int viewedAccountsReport, viewedAccountsPayment;
        bool accountsContractSeen;
        GUIStyle financeBody, financeHeading, financeButton;
        public HotelBook? ReadingBook => readingBook ? readingBook.kind : null;
        public IEnumerable<string> BookOptionTitles => bookChoices.Select(choice => choice.title);
        public string DisplayedBookText => readingBook ? bookCopy : null;
        public string FocusedBookOption => readingBook && focus >= 0 && focus < bookChoices.Count ? bookChoices[focus].title : null;

        public void OpenBook(int actorId, HotelBook kind)
        {
            if (OwnershipRevoked) { ShowOwnershipLost(); return; }
            if (IsOpen) Close();
            Open(actorId);
            if (!IsOpen) return;
            BindBook(kind);
        }

        void BindBook(HotelBook kind)
        {
            readingBook = DiegeticBookInteraction.Find(kind);
            var model = Session.Simulation;
            bookPage = kind != HotelBook.Accounts ? 0 : !model.ContractEnabled ? 1 :
                model.LastContractPayment != null && (accountsModel != model || model.LastContractPayment.Period > viewedAccountsPayment) ? 4 : 0;
            bookListPage = 0; bookDetail = false; bookLastMessage = Session.LastMessage;
            ResetAccountsDetail();
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
        void BookPage(int page) { bookPage = page; bookListPage = 0; bookDetail = false; ResetAccountsDetail(); focus = 0; HotelFeedback.PlayUIClick(); }

        void ResetAccountsDetail()
        {
            accountsTextPage = accountsReceiptPage = 0;
            accountsReceipt = null; accountsReceiptNotes = false;
        }

        void UpdateBookChoices()
        {
            if (!readingBook || guestContext || wakePhone) return;
            bookChoices.Clear(); noteLines.Clear();
            var model = Session.Simulation;
            switch (readingBook.kind)
            {
                case HotelBook.Reservations: ReservationPages(); break;
                case HotelBook.Services: ServiceNotes(); break;
                case HotelBook.Supplies: SupplyPages(); PaginateAccountsText(); break;
                case HotelBook.Accounts:
                    AccountsPages();
                    PaginateAccountsText();
                    UpdateAccountsReadState();
                    break;
                case HotelBook.Renovation:
                    bookHeading = "RENOVATION LEDGER";
                    bookCopy = "Cash   $" + model.Economy.Cash + "\n\nBURNER · more heat capacity\nELECTRICS · more branch capacity\nROOM 102 · seal draughty windows\nNORTH WING · rooms 107–110\nSet rates in RESERVATIONS.";
                    if (model.ContractEnabled)
                        bookCopy += "\n\nCurrent payment stays $" + model.ContractDue +
                            (model.NorthWingRestored ? "\nNext payment   $" + model.NextContractDue :
                            "\nRestoration adds $" + (4L * model.ContractRoomSurcharge) + " per payment\nstarting with the next assessment.\nNext: $" + model.NextContractDue + " → $" + model.NextContractDueWithRooms(model.ContractBaseRooms + 4)) +
                            "\nDue " + GuestLabels.HotelMoment(model, model.NextContractAt) +
                            "\nNext operations   $" + Session.Economy.DailyOperatingCost +
                            "\n" + GuestLabels.HotelMoment(model, model.NextOperatingCostAt) + "\nSpending is your decision.";
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

        void UpdateAccountsMarker()
        {
            var model = Session.Simulation;
            if (accountsModel != model)
            {
                accountsModel = model; viewedAccountsReport = 0; viewedAccountsPayment = 0; accountsContractSeen = false;
                var book = DiegeticBookInteraction.Find(HotelBook.Accounts);
                accountsTitle = book ? book.GetComponentsInChildren<TextMesh>().FirstOrDefault(t => t.name == "Printed book title") : null;
            }
            if (!accountsTitle) return;
            string title = model.LastContractPayment != null && model.LastContractPayment.Period > viewedAccountsPayment ? "ACCOUNTS\nNEW RECEIPT" :
                model.LastReport != null && model.LastReport.DayNumber > viewedAccountsReport ? "ACCOUNTS\nNEW REPORT" :
                model.ContractEnabled && !accountsContractSeen ? "ACCOUNTS\nCONTRACT DUE" : "ACCOUNTS";
            if (accountsTitle.text != title) accountsTitle.text = title;
        }

        void UpdateAccountsReadState()
        {
            if (!bookCamera || !bookCamera.Settled) return;
            var page = readingBook.ScreenPage(LocalCoopBootstrap.Instance.Players[owner].PlayerCamera);
            if (page.width <= 0 || page.height <= 0) return;
            // Reading follows the settled, updated page, even when no IMGUI repaint is requested.
            accountsContractSeen = true;
            var model = Session.Simulation;
            if (bookPage == 4 && model.LastContractPayment != null)
                viewedAccountsPayment = Math.Max(viewedAccountsPayment, model.LastContractPayment.Period);
            if (bookPage == 2 && bookDetail)
            {
                var report = Session.Reports.FirstOrDefault(r => r.DayNumber == operationsReportNumber);
                if (report != null)
                {
                    viewedAccountsReport = Math.Max(viewedAccountsReport, report.DayNumber);
                }
            }
            if (bookPage == 7)
            {
                var payment = Session.Reports.FirstOrDefault(r => r.DayNumber == operationsReportNumber)?.ContractPayment;
                if (payment != null) viewedAccountsPayment = Math.Max(viewedAccountsPayment, payment.Period);
            }
            UpdateAccountsMarker();
        }

        void AccountsPages()
        {
            var model = Session.Simulation;
            if (bookPage == 5 || bookPage == 6)
            {
                AccountsGuestReceipts(bookPage == 5 ? model.CurrentReceipts :
                    Session.Reports.FirstOrDefault(r => r.DayNumber == operationsReportNumber)?.Receipts);
                return;
            }
            if (bookPage == 7)
            {
                var report = Session.Reports.FirstOrDefault(r => r.DayNumber == operationsReportNumber);
                bookHeading = "DAY " + operationsReportNumber + " · CONTRACT";
                bookCopy = report?.ContractPayment == null ? "No contract receipt retained." :
                    ContractReceiptCopy(model, report.ContractPayment) + "\n\nCash at report   $" + report.Cash;
                BookChoice("‹ Report summary", ReturnToAccountsReport);
                return;
            }
            if (model.ContractEnabled) BookChoice("Ownership contract", () => BookPage(0));
            BookChoice("Current cash / charges", () => BookPage(1));
            BookChoice("Current guest receipts", () => BookPage(5));
            BookChoice("Daily reports", () =>
            {
                BookPage(2);
                if (model.LastReport != null) { operationsReportNumber = model.LastReport.DayNumber; bookDetail = true; }
            });
            if (model.ContractEnabled) BookChoice("Contract terms", () => BookPage(3));
            if (model.LastContractPayment != null) BookChoice("Latest contract receipt", () => BookPage(4));
            bookHeading = bookPage == 0 ? "OWNERSHIP CONTRACT" : bookPage == 1 ? "CASH BOOK" : "DAILY REPORTS";
            if (bookPage == 0)
            {
                long shortfall = Math.Max(0L, (long)model.ContractDue - model.Economy.Cash);
                bookCopy = "NEXT CONTRACT PAYMENT\nDue " + GuestLabels.HotelMoment(model, model.NextContractAt) +
                    "\nNext payment   $" + model.ContractDue +
                    (model.CalendarDay < model.Operations.Contract.FirstPaymentDay ? "\nNO PAYMENT TODAY" : "") +
                    "\nCash now   $" + model.Economy.Cash +
                    "\n" + (shortfall == 0 ? "COVERED by current cash" : "SHORT $" + shortfall + " at current cash") +
                    "\n\nAssessed rooms   " + model.ContractAssessedRooms + "\nFollowing payment   $" + model.NextContractDue +
                    "\n\nNext operating charge   $" + Session.Economy.DailyOperatingCost +
                    "\n" + GuestLabels.HotelMoment(model, model.NextOperatingCostAt) +
                    "\n\nFirst due " + GuestLabels.HotelMoment(model, model.FirstContractAt) +
                    "\nAfter the first 10:00 checkout.\nUnpaid bookings are not cash.\nMiss a payment: ownership revoked.";
                return;
            }
            if (bookPage == 3)
            {
                bookHeading = "CONTRACT TERMS";
                bookCopy = "Pay the ownership contract at 22:00.\nFirst: " + GuestLabels.HotelMoment(model, model.FirstContractAt) +
                    "\nAfter the first 10:00 checkout.\n\nOperations charge separately at 06:00.\nReports at 06:00 move no cash." +
                    "\n\nDaily rise   +$" + model.ContractDailyIncrease + "\nEach restored room above " + model.ContractBaseRooms + ": +$" + model.ContractRoomSurcharge +
                    "\nThe current bill stays locked to 22:00.\nRestored rooms enter the next bill." +
                    "\n\nBookings are future income, not cash.\nRoom charges arrive at checkout.\nA short payment ends ownership.\nYour spending choices remain open.";
                return;
            }
            if (bookPage == 4)
            {
                bookHeading = "CONTRACT RECEIPT";
                var receipt = model.LastContractPayment;
                bookCopy = receipt == null ? "No contract payment yet." : ContractReceiptCopy(model, receipt) +
                    "\n\nCash now   $" + model.Economy.Cash + "\nReceipt cash is fixed at payment.\nLater transactions change cash now.";
                return;
            }
            if (bookPage == 1)
            {
                bookCopy = "Cash on hand   $" + model.Economy.Cash + "\nSince " + GuestLabels.HotelMoment(model, model.PeriodStartedAt) +
                    "\n\nRoom charges   $" + model.PeriodGross + "\nCredits / refunds   $" + model.PeriodCompensation +
                    "\nCollected at checkout   $" + model.PeriodCheckoutIncome + "\nMaintenance paid   $" + model.PeriodMaintenanceSpend +
                    "\nRenovation paid   $" + model.PeriodCapitalSpend + "\nOperations paid   $" + model.PeriodOperatingSpend +
                    "\nLaundry service   $" + model.PeriodLaundrySpend + "\nBulb orders   $" + model.PeriodBulbSpend +
                    (model.PeriodContractPayment == null ? "" : "\nContract paid   $" + model.PeriodContractPayment.PaidAmount) +
                    "\n\nNext operations   $" + Session.Economy.DailyOperatingCost +
                    "\n" + GuestLabels.HotelMoment(model, model.NextOperatingCostAt) +
                    (model.ContractEnabled ? "\nContract   $" + model.ContractDue + "\n" + GuestLabels.HotelMoment(model, model.NextContractAt) : "") +
                    "\n\nUnpaid bookings   $" + model.UnpaidBookedRevenue + "\nNot spendable until checkout.";
                return;
            }
            var reports = Session.Reports.Reverse().ToArray();
            bookCopy = "Reports are written at 06:00.\nWriting a report moves no cash.\n\nChoose a day to read its accounts.\nOperating result excludes the contract.\nA receipt appears if paid that period.\nReport cash is the balance at 06:00.";
            if (reports.Length == 0) bookCopy += "\n\nNo completed report yet.";
            foreach (var report in reports.Skip(bookListPage * 3).Take(bookDetail ? 0 : 3))
            {
                var item = report;
                BookChoice("Day " + item.DayNumber + " · result $" + item.Net, () =>
                { operationsReportNumber = item.DayNumber; bookDetail = true; ResetAccountsDetail(); focus = 0; });
            }
            if (!bookDetail && reports.Length > 3) BookChoice("Older pages ›", () => { bookListPage = (bookListPage + 1) % ((reports.Length + 2) / 3); accountsTextPage = 0; });
            if (!bookDetail) return;
            var selected = reports.FirstOrDefault(r => r.DayNumber == operationsReportNumber);
            if (selected == null) return;
            bookHeading = "DAY " + selected.DayNumber + " · ACCOUNTS";
            bookCopy = "Opening cash   $" + selected.OpeningCash + "\nRoom charges   $" + selected.Gross +
                "\nCredits / refunds   $" + selected.Compensation + "\nOperations   $" + selected.OperatingCost +
                "\nMaintenance   $" + selected.MaintenanceSpend + "\nRenovation   $" + selected.CapitalSpend +
                "\nLaundry service   $" + selected.LaundrySpend + "\nBulb orders   $" + selected.BulbSpend +
                "\nOperating result   $" + selected.Net;
            var payment = selected.ContractPayment;
            bookCopy += "\n\nCash at report   $" + selected.Cash + "\nOperating result excludes contract.";
            BookChoice("Guest receipts (" + selected.Receipts.Count + ")", () => BookPage(6));
            if (payment != null) BookChoice("Report contract receipt", () => BookPage(7));
            BookChoice("‹ All daily reports", () => BookPage(2));
        }

        void ReturnToAccountsReport() { BookPage(2); bookDetail = true; }

        void AccountsGuestReceipts(IReadOnlyList<GuestReceipt> receipts)
        {
            bool current = bookPage == 5;
            bookHeading = current ? "CURRENT GUEST RECEIPTS" : "DAY " + operationsReportNumber + " · RECEIPTS";
            if (accountsReceipt != null)
            {
                // Keep the selected frozen receipt readable even if the live period closes while reading.
                var receipt = accountsReceipt;
                bookHeading = accountsReceiptNotes ? "GUEST NOTES" : "GUEST RECEIPT";
                bookCopy = receipt.RoomId + " · " + receipt.Name + "\n" + GuestReceiptStatus(receipt) +
                    (receipt.EarlyCheckout ? "\n" + GuestLabels.HotelMoment(Session.Simulation, receipt.CheckoutAt) : "") + "\n\n" +
                    (accountsReceiptNotes ? GuestReceiptNotes(receipt) : GuestReceiptAmounts(receipt) +
                    "\n\nReason / guest note\n" + GuestReceiptBriefReason(receipt));
                BookChoice(accountsReceiptNotes ? "Receipt amounts" : "Full guest notes", () =>
                { accountsReceiptNotes = !accountsReceiptNotes; accountsTextPage = 0; focus = 0; });
                BookChoice("‹ Guest receipt list", () => { accountsReceipt = null; accountsTextPage = 0; focus = 0; });
            }
            else
            {
                int count = receipts?.Count ?? 0;
                int pages = Math.Max(1, (count + 3) / 4);
                accountsReceiptPage = Mathf.Clamp(accountsReceiptPage, 0, pages - 1);
                bookCopy = (current ? "Checkouts since the last report." : "Checkouts in this published report.") +
                    "\nAmounts are already posted.\nReading moves no money.\n\n" +
                    (count == 0 ? "No guest receipts in this period." : count + " receipts · page " + (accountsReceiptPage + 1) + " / " + pages +
                    "\nChoose a guest for amounts and notes.");
                if (receipts != null) foreach (var receipt in receipts.Skip(accountsReceiptPage * 4).Take(4))
                {
                    var item = receipt;
                    string name = item.Name ?? item.GuestId;
                    if (name.Length > 16) name = name.Substring(0, 15) + "…";
                    BookChoice(item.RoomId + " · " + name + " · $" + item.Net, () =>
                    { accountsReceipt = item; accountsReceiptNotes = false; accountsTextPage = 0; focus = 0; });
                }
                if (accountsReceiptPage > 0) BookChoice("‹ Previous receipts", () => { accountsReceiptPage--; focus = 0; });
                if (accountsReceiptPage + 1 < pages) BookChoice("More guest receipts ›", () => { accountsReceiptPage++; focus = 0; });
            }
            BookChoice(current ? "‹ Current cash / charges" : "‹ Report summary", () =>
            { if (current) BookPage(1); else ReturnToAccountsReport(); });
        }

        static string GuestReceiptStatus(GuestReceipt receipt) => receipt.Price == 0 ? "UNSERVED / NO CHARGE" :
            receipt.EarlyCheckout ? "EARLY CHECKOUT" : "CHECKOUT";

        static string GuestReceiptAmounts(GuestReceipt receipt) =>
            (receipt.AgreedPrice == 0 && receipt.Price == 0 ? "Agreed price   not recorded" : "Agreed price   $" + receipt.AgreedPrice) +
            "\nStay charged   $" + receipt.Price +
            "\nCredits / refunds   $" + receipt.Compensation + "\nNet received   $" + receipt.Net;

        static string GuestReceiptNotes(GuestReceipt receipt)
        {
            string review = receipt.Review ?? "";
            string reason = receipt.DepartureReason;
            return !string.IsNullOrWhiteSpace(reason) && !review.StartsWith(reason, StringComparison.Ordinal) ?
                reason + "\n\n" + review : string.IsNullOrWhiteSpace(review) ? "No guest note recorded." : review;
        }

        static string GuestReceiptBriefReason(GuestReceipt receipt)
        {
            string note = GuestReceiptNotes(receipt);
            int sentence = note.IndexOf(". ", StringComparison.Ordinal);
            if (sentence >= 0) note = note.Substring(0, sentence + 1);
            if (note.Length > 150)
            {
                int end = note.LastIndexOf(' ', 147);
                note = note.Substring(0, end > 0 ? end : 147) + "…";
            }
            return note;
        }

        void PaginateAccountsText()
        {
            // Use the actual book font's wrapped height, not character counts or smaller type.
            if (financeBody == null || string.IsNullOrEmpty(bookCopy)) return;
            var pages = new List<string>();
            string remaining = bookCopy;
            while (remaining.Length > 0)
            {
                int low = 1, high = remaining.Length, fit = 1;
                while (low <= high)
                {
                    int mid = low + (high - low) / 2;
                    if (financeBody.CalcHeight(new GUIContent(remaining.Substring(0, mid)), 420) <= 475)
                    { fit = mid; low = mid + 1; }
                    else high = mid - 1;
                }
                if (fit < remaining.Length)
                {
                    int boundary = remaining.LastIndexOfAny(new[] { ' ', '\n' }, fit - 1, fit);
                    if (boundary > 0) fit = boundary + 1;
                }
                pages.Add(remaining.Substring(0, fit).TrimEnd());
                remaining = remaining.Substring(fit).TrimStart();
            }
            accountsTextPage = Mathf.Clamp(accountsTextPage, 0, pages.Count - 1);
            bookCopy = pages[accountsTextPage];
            if (pages.Count <= 1) return;
            bookCopy += "\n" + (accountsTextPage + 1) + " / " + pages.Count;
            if (accountsTextPage > 0) BookChoice("‹ Previous text page", () => { accountsTextPage--; focus = 0; });
            if (accountsTextPage + 1 < pages.Count) BookChoice("Continue reading ›", () => { accountsTextPage++; focus = 0; });
        }

        static string ContractReceiptCopy(HotelSimulation model, ContractPayment payment) =>
            "CONTRACT " + payment.Period + " · " + payment.AssessedRooms + " rooms\n" + GuestLabels.HotelMoment(model, payment.DueAt) +
            "\nDue $" + payment.Due + " · paid $" + payment.PaidAmount +
            "\nFunds before payment   $" + payment.FundsBeforePayment + "\nCash after payment   $" + payment.CashAfterPayment +
            (payment.OwnershipLost ? "\nShortfall   $" + payment.Shortfall + "\nOWNERSHIP REVOKED" : "\nPayment complete");

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
                if (model.Operations.Sales != null)
                    bookCopy += "Reputation: " + SalesSettings.ReputationLabel(model.Economy.Reputation) +
                        "\nOrdinary demand: " + model.Operations.Sales.DemandLabel(model.Economy.Reputation) + "\n\n";
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
            var lockouts = Session.Simulation.Guests.Where(g => g.LockedOut && !g.ReceiptPosted &&
                g.Agent.State == GuestAgentState.WaitingForCheckIn).ToArray();
            if (lockouts.Length > 0) BookChoice("Guests locked out", () => BookPage(2));
            var notes = services.Cases.Where(GuestLabels.IsKnownToHotel).OrderBy(c => !c.Active).ThenBy(c => c.CreatedAt).ToArray();
            bookListPage = Mathf.Clamp(bookListPage, 0, Math.Max(0, (notes.Length - 1) / 7));
            foreach (var item in notes.Skip(bookListPage * 7).Take(7))
                noteLines.Add((item.RoomId + " · " + GuestLabels.Service(item.Kind, Session.Simulation) + "\n" +
                    (item.Kind == ServiceKind.WakeUpCall || item.Kind == ServiceKind.LateCheckout ? GuestLabels.HotelMoment(Session.Simulation, item.DueTime) + " · " : "") +
                    GuestLabels.ServiceBrief(item, Session.Simulation), item.Status == ServiceStatus.Fulfilled));
            var bags = services.Items.Where(i => i.Kind == ServiceItemKind.Luggage && i.StaffHandling && i.Location != ServiceItemLocation.LostProperty)
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
            if (bookPage == 2)
            {
                noteLines.Clear();
                foreach (var guest in lockouts.Take(7))
                    noteLines.Add(("Room " + guest.RoomId + " · " + guest.Name + "\nKey left inside. Take STAFF key to the room door.", false));
                BookChoice("‹ Guest messages", () => BookPage(0));
            }
            if (notes.Length == 0 && bags.Length == 0 && lockouts.Length == 0) bookCopy += "\n\nNo messages written yet.";
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
            bool finance = readingBook.kind == HotelBook.Accounts || readingBook.kind == HotelBook.Renovation || readingBook.kind == HotelBook.Supplies;
            financeBody ??= new GUIStyle(Body) { fontSize = 22 };
            financeHeading ??= new GUIStyle(Heading) { fontSize = 25 };
            financeButton ??= new GUIStyle(HotelTheme.Button) { fontSize = 21 };
            if (readingBook.kind == HotelBook.Accounts || readingBook.kind == HotelBook.Supplies) UpdateBookChoices();
            // The ink stays registered to the physical page; the real cover, spine and desk frame it.
            Fill(new Rect(18, 14, 468, 646), Paper);
            Fill(new Rect(514, 14, 468, 646), Paper);
            Label(new Rect(40, 25, 420, 60), bookHeading, finance ? financeHeading : Heading, Wine);
            Label(new Rect(40, 101, 420, 510), bookCopy, finance ? financeBody : Body, Ink);
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
                ButtonAt(new Rect(540, 43 + i * (height + 5), 415, height), choice.title, choice.action, choice.enabled, style: finance ? financeButton : null);
            }
            if (Session.LastMessage != bookLastMessage)
                Label(new Rect(540, 594, 415, 62), Session.LastMessage, Small, Wine);
            Label(new Rect(40, 635, 410, 26), "Backspace / B · close     Arrows / Enter · select", Small, Muted);
            GUI.matrix = old;
            return true;
        }
    }
}
