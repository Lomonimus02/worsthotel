using System;
using System.Linq;

namespace WorstHotel
{
    [Serializable] public sealed class OwnershipContractSettingsSnapshot
    {
        public int BaseDue, DailyIncrease, ExtraRoomCharge, FirstPaymentDay;
        public float PaymentHour;
        public OwnershipContractSettings ToSettings() => new OwnershipContractSettings(BaseDue, DailyIncrease, ExtraRoomCharge, PaymentHour, FirstPaymentDay);
    }
    [Serializable] public sealed class OperationsSnapshot
    {
        public float SecondsPerDay, StartHour, ReportHour, OperatingCostHour, ArrivalStartHour, ArrivalEndHour, SleepHour, CheckoutHour;
        public int ReportHistoryLimit, ReportSequence, PeriodOpeningCash, OffersThroughDay, ServiceDay, PeriodMaintenanceSpend, PeriodCapitalSpend;
        public int OperatingCostSequence, PeriodOperatingSpend, ContractSequence;
        public int PeriodLaundrySpend, PeriodBulbSpend;
        public SupplySnapshot Supplies;
        public float PeriodStartedAt;
        public bool NorthWingRestored, Room102Insulated;
        public OwnershipContractSettingsSnapshot Contract;
        public int ContractBaseRooms, ContractAssessedRooms;
        public bool OwnershipLost;
        public ContractPaymentSnapshot LastContractPayment, PeriodContractPayment;
        public ReportSnapshot OwnershipLossReport;
        public SalesSnapshot Sales;
        public SpecialBookingSnapshot SpecialBookings;
        public ReceiptSnapshot[] PeriodReceipts;
        public ScheduledOfferSnapshot[] Offers;
        public ReservationSnapshot[] Reservations;
        public OperationsSettings ToSettings() => new OperationsSettings(SecondsPerDay, StartHour, ReportHour,
            ArrivalStartHour, ArrivalEndHour, SleepHour, CheckoutHour, ReportHistoryLimit, Sales?.Settings?.ToSettings(), SpecialBookings?.Settings,
            SnapshotData.OptionalContract(Contract)?.ToSettings(), OperatingCostHour);
    }
    [Serializable] public sealed class ScheduledOfferSnapshot
    {
        public BookingSnapshot Application;
        public int ArrivalDay;
        public float ArrivalAt, SleepAt, WakeAt, CheckoutAt;
    }
    [Serializable] public sealed class ReservationSnapshot
    {
        public ScheduledOfferSnapshot Offer;
        public int RoomId, Price, ActorId, Revision;
        public ReservationStatus Status;
        public bool IsAutomatic;
    }

    internal static partial class SnapshotData
    {
        internal static ReceiptSnapshot Capture(GuestReceipt receipt) => new ReceiptSnapshot
        { GuestId=receipt.GuestId,Name=receipt.Name,RoomId=receipt.RoomId,Price=receipt.Price,AgreedPrice=receipt.AgreedPrice,
            Compensation=receipt.Compensation,Satisfaction=receipt.Satisfaction,Review=receipt.Review,
            EarlyCheckout=receipt.EarlyCheckout,CheckoutAt=receipt.CheckoutAt,DepartureReason=receipt.DepartureReason };
        internal static GuestReceipt Receipt(ReceiptSnapshot receipt) => new GuestReceipt(receipt.GuestId,receipt.Name,
            receipt.RoomId,receipt.Price,receipt.Satisfaction,receipt.Compensation,receipt.Review,
            receipt.EarlyCheckout,receipt.CheckoutAt,receipt.DepartureReason,receipt.AgreedPrice);
        internal static ScheduledOfferSnapshot Capture(ScheduledBookingOffer o) => new ScheduledOfferSnapshot
        { Application = Capture(o.Application), ArrivalDay = o.ArrivalDay, ArrivalAt = o.ArrivalAt,
            SleepAt = o.SleepAt, WakeAt = o.WakeAt, CheckoutAt = o.CheckoutAt };
        internal static ScheduledBookingOffer Offer(ScheduledOfferSnapshot o) => new ScheduledBookingOffer(Booking(o.Application),
            o.ArrivalDay, o.ArrivalAt, o.SleepAt, o.WakeAt, o.CheckoutAt);
        internal static ReservationSnapshot Capture(HotelReservation r) => new ReservationSnapshot
        { Offer = Capture(r.Offer), RoomId = r.RoomId, Price = r.Price, ActorId = r.ActorId, Revision = r.Revision, Status = r.Status, IsAutomatic = r.IsAutomatic };
        internal static HotelReservation Reservation(ReservationSnapshot r) => new HotelReservation(Offer(r.Offer), r.RoomId, r.Price, r.ActorId, r.IsAutomatic)
        { Revision = r.Revision, Status = r.Status };
    }

    public sealed partial class HotelSimulation
    {
        OperationsSnapshot CaptureOperations() => !ContinuousOperations ? null : new OperationsSnapshot
        {
            SecondsPerDay = Operations.SecondsPerDay, StartHour = Operations.StartHour, ReportHour = Operations.ReportHour,
            OperatingCostHour = Operations.OperatingCostHour,
            ArrivalStartHour = Operations.ArrivalStartHour, ArrivalEndHour = Operations.ArrivalEndHour,
            SleepHour = Operations.SleepHour, CheckoutHour = Operations.CheckoutHour, ReportHistoryLimit = Operations.ReportHistoryLimit,
            ReportSequence = ReportSequence, PeriodOpeningCash = periodOpeningCash, PeriodStartedAt = periodStartedAt,
            PeriodMaintenanceSpend = PeriodMaintenanceSpend,
            PeriodCapitalSpend = PeriodCapitalSpend,
            PeriodLaundrySpend = PeriodLaundrySpend, PeriodBulbSpend = PeriodBulbSpend, Supplies = CaptureSupplies(),
            OperatingCostSequence = OperatingCostSequence, PeriodOperatingSpend = PeriodOperatingSpend, ContractSequence = ContractSequence,
            NorthWingRestored = NorthWingRestored, Room102Insulated = Room102Insulated,
            Contract = !ContractEnabled ? null : new OwnershipContractSettingsSnapshot
            { BaseDue = Operations.Contract.BaseDue, DailyIncrease = Operations.Contract.DailyIncrease, ExtraRoomCharge = Operations.Contract.ExtraRoomCharge,
                PaymentHour = Operations.Contract.PaymentHour, FirstPaymentDay = Operations.Contract.FirstPaymentDay },
            ContractBaseRooms = ContractBaseRooms, ContractAssessedRooms = ContractAssessedRooms, OwnershipLost = OwnershipLost,
            LastContractPayment = SnapshotData.Capture(LastContractPayment), PeriodContractPayment = SnapshotData.Capture(PeriodContractPayment),
            OwnershipLossReport = SnapshotData.Capture(OwnershipLossReport),
            Sales = CaptureSales(), SpecialBookings = CaptureSpecialBookings(),
            OffersThroughDay = offersThroughDay, ServiceDay = operatingServiceDay,
            PeriodReceipts = periodReceipts.Select(SnapshotData.Capture).ToArray(),
            Offers = bookingOffers.Select(SnapshotData.Capture).ToArray(), Reservations = reservations.Select(SnapshotData.Capture).ToArray()
        };
        void RestoreOperations(OperationsSnapshot data, ContractPayment lastPayment, ContractPayment periodPayment, DayReport lossReport)
        {
            if (data == null) return;
            ReportSequence = data.ReportSequence; periodOpeningCash = data.PeriodOpeningCash; periodStartedAt = data.PeriodStartedAt;
            PeriodMaintenanceSpend = data.PeriodMaintenanceSpend;
            PeriodCapitalSpend = data.PeriodCapitalSpend;
            PeriodLaundrySpend = data.PeriodLaundrySpend; PeriodBulbSpend = data.PeriodBulbSpend;
            RestoreSupplies(data.Supplies);
            OperatingCostSequence = data.OperatingCostSequence; PeriodOperatingSpend = data.PeriodOperatingSpend;
            ContractSequence = data.ContractSequence; LastContractPayment = lastPayment; PeriodContractPayment = periodPayment;
            OwnershipLossReport = lossReport;
            ContractBaseRooms = data.ContractBaseRooms; ContractAssessedRooms = data.ContractAssessedRooms; OwnershipLost = data.OwnershipLost;
            Economy.OwnershipRevoked = OwnershipLost;
            NorthWingRestored = data.NorthWingRestored; Room102Insulated = data.Room102Insulated; ApplyProgressionToRooms();
            offersThroughDay = data.OffersThroughDay; operatingServiceDay = data.ServiceDay;
            periodReceipts.Clear(); periodReceipts.AddRange(data.PeriodReceipts.Select(SnapshotData.Receipt));
            bookingOffers.Clear(); bookingOffers.AddRange(data.Offers.Select(SnapshotData.Offer));
            reservations.Clear(); reservations.AddRange(data.Reservations.Select(SnapshotData.Reservation));
            RestoreSales(data.Sales); RestoreSpecialBookings(data.SpecialBookings);
            Economy.RestoreOperatingSequence(ReportSequence);
        }
    }

    internal static partial class SnapshotValidation
    {
        internal static void OperationsModel(HotelModelSnapshot model, OperationsSettings expected, EconomySettings economy, int[] roomIds,
            GuestScheduleSystem schedules)
        {
            var data = model.HasOperations ? model.Operations : null;
            Require(!model.HasOperations || data != null, "Missing continuous operations state.");
            Require((data != null) == (expected != null), "Continuous operations mode differs from this hotel.");
            if (data == null) return;
            var config = data.ToSettings();
            Require(data.PeriodMaintenanceSpend >= 0 && data.PeriodCapitalSpend >= 0 && data.PeriodOperatingSpend >= 0 &&
                data.PeriodLaundrySpend >= 0 && data.PeriodBulbSpend >= 0 &&
                (long)data.PeriodMaintenanceSpend + data.PeriodCapitalSpend + data.PeriodOperatingSpend + data.PeriodLaundrySpend + data.PeriodBulbSpend <= int.MaxValue,
                "Invalid period equipment spending total.");
            Require(config.SecondsPerDay == expected.SecondsPerDay && config.StartHour == expected.StartHour && config.ReportHour == expected.ReportHour &&
                config.OperatingCostHour == expected.OperatingCostHour &&
                config.ArrivalStartHour == expected.ArrivalStartHour && config.ArrivalEndHour == expected.ArrivalEndHour &&
                config.SleepHour == expected.SleepHour && config.CheckoutHour == expected.CheckoutHour && config.ReportHistoryLimit == expected.ReportHistoryLimit,
                "Continuous calendar settings differ from this hotel.");
            Require(model.Time / (double)config.SecondsPerDay < 999999, "Calendar time exceeds the supported wire range.");
            var clock = new HotelGameClock(); clock.Advance(model.Time);
            var calendar = new HotelCalendar(clock, config);
            Require(model.Day == (model.Running || data.OwnershipLost ? calendar.Day : 0), "Calendar date does not match the host clock.");
            Require(data.ReportSequence >= 0 && data.ReportSequence <= model.Day && data.ServiceDay >= 0 && data.ServiceDay <= model.Day &&
                data.OffersThroughDay >= 0 && data.OffersThroughDay <= model.Day + 1, "Invalid operating calendar counters.");
            Range(data.PeriodStartedAt, 0, model.Time);
            float expectedStart = ReportPeriodStart(calendar, data.ReportSequence + 1);
            Require(data.PeriodStartedAt == expectedStart && data.ReportSequence == FinancialOccurrencesAt(calendar.FirstReportAt, config.SecondsPerDay, model.Time),
                "Accounting interval does not match the calendar.");
            Require(model.Reports.Length == Math.Min(data.ReportSequence, config.ReportHistoryLimit) && model.LastReportDay == data.ReportSequence,
                "Operating report sequence differs from retained history.");
            float firstOperatingAt = calendar.FirstOccurrenceAt(config.OperatingCostHour);
            Require(data.OperatingCostSequence == FinancialOccurrencesAt(firstOperatingAt, config.SecondsPerDay, model.Time),
                "Operating charge sequence differs from the clock.");
            ValidateOperatingSpend(data.PeriodOperatingSpend, expectedStart, model.Time, calendar, economy);
            for (int index = 0; index < model.Reports.Length; index++)
            {
                var report = model.Reports[index];
                Require(report.Day == data.ReportSequence - model.Reports.Length + index + 1, "Reports must retain consecutive closed periods.");
                float start = ReportPeriodStart(calendar, report.Day), end = ReportPeriodEnd(calendar, report.Day);
                Require(Math.Abs(report.ServiceSeconds - (end - start)) < .01f, "Report duration differs from its accounting interval.");
                ValidateOperatingSpend(report.OperatingCost, start, end, calendar, economy);
            }
            var receipts = Array(data.PeriodReceipts, 128); Unique(receipts.Select(item => item.GuestId));
            foreach (var receipt in receipts) { ValidateOperatingReceipt(receipt, roomIds); DepartureReceipt(receipt, model.Time); }
            Require(receipts.Sum(item => (long)item.Price) <= int.MaxValue && receipts.Sum(item => (long)item.Compensation) <= int.MaxValue,
                "Operating receipt totals overflow.");
            Require(!data.NorthWingRestored || roomIds.Contains(110), "Opened wing has no room registry.");
            OwnershipContract(model, expected.Contract, roomIds, calendar, economy);
            var offers = Array(data.Offers, 24); Unique(offers.Select(item => item.Application?.Id));
            foreach (var offer in offers) ValidateScheduledOffer(offer, calendar, schedules);
            var reservations = Array(data.Reservations, 128); Unique(reservations.Select(item => item.Offer?.Application?.Id));
            foreach (var reservation in reservations)
            {
                ValidateScheduledOffer(reservation.Offer, calendar, schedules); EnumValue(reservation.Status);
                Require(roomIds.Contains(reservation.RoomId) && reservation.ActorId >= (reservation.IsAutomatic ? -1 : 0) && reservation.ActorId <= 1 && reservation.Revision >= 1 &&
                    reservation.Price >= economy.MinPrice && reservation.Price <= economy.MaxPrice &&
                    (reservation.Price - economy.MinPrice) % economy.PriceStep == 0, "Invalid dated reservation.");
                Require((reservation.Status != ReservationStatus.Reserved || reservation.Revision <= int.MaxValue - 2) &&
                    (reservation.Status != ReservationStatus.Arrived || reservation.Revision <= int.MaxValue - 1),
                    "Reservation has no revisions left for its remaining lifecycle.");
                var guest = model.Guests.FirstOrDefault(item => item.Application.Id == reservation.Offer.Application.Id);
                if (reservation.Status == ReservationStatus.Arrived || reservation.Status == ReservationStatus.Completed)
                    Require(guest != null && guest.Price == reservation.Price &&
                        (reservation.Status == ReservationStatus.Completed) == guest.ReceiptPosted, "Reservation and stay billing disagree.");
                if (reservation.Status == ReservationStatus.Reserved || reservation.Status == ReservationStatus.Cancelled)
                    Require(guest == null, "Unarrived reservation already has a live guest.");
                if (reservation.Status == ReservationStatus.Reserved)
                    Require(reservation.Offer.ArrivalAt > model.Time, "Future reservation is past its arrival.");
                if (guest != null && guest.Agent != null)
                {
                    var agent = guest.Agent;
                    Require(guest.RoomId == reservation.RoomId && agent.ArrivalTime == reservation.Offer.ArrivalAt &&
                        agent.SleepTime == reservation.Offer.SleepAt && agent.HasWakeTime && agent.WakeTime == reservation.Offer.WakeAt &&
                        agent.CheckoutTime >= reservation.Offer.CheckoutAt && calendar.DayAt(agent.CheckoutTime) == reservation.Offer.ArrivalDay + 1,
                        "Dated reservation and guest schedule differ.");
                    if (schedules != null)
                    {
                        var timing = schedules.DatedTimingFor(SnapshotData.Booking(reservation.Offer.Application),
                            reservation.Offer.ArrivalDay, reservation.Offer.ArrivalAt, calendar);
                        Require(agent.OutingReturnAt == timing.OutingReturnAt &&
                            (agent.MorningActivityIndex >= 1) == (timing.OutingReturnAt >= 0),
                            "Dated guest rhythm differs from its immutable timing.");
                    }
                    bool terminal = agent.State == GuestAgentState.CheckingOut || agent.State == GuestAgentState.Leaving || agent.State == GuestAgentState.Left;
                    Require(guest.ReceiptPosted == terminal, "Stay receipt and departure state differ.");
                }
            }
            foreach (var guest in model.Guests)
                Require(reservations.Any(item => item.Offer.Application.Id == guest.Application.Id), "Guest has no dated reservation.");
            var active = reservations.Where(item => item.Status == ReservationStatus.Reserved || item.Status == ReservationStatus.Arrived).ToArray();
            for (int i = 0; i < active.Length; i++)
            for (int j = i + 1; j < active.Length; j++)
            {
                if (active[i].RoomId != active[j].RoomId) continue;
                float endI = model.Guests.FirstOrDefault(item => item.Application.Id == active[i].Offer.Application.Id)?.Agent?.CheckoutTime ?? active[i].Offer.CheckoutAt;
                float endJ = model.Guests.FirstOrDefault(item => item.Application.Id == active[j].Offer.Application.Id)?.Agent?.CheckoutTime ?? active[j].Offer.CheckoutAt;
                Require(active[i].Offer.ArrivalAt >= endJ || active[j].Offer.ArrivalAt >= endI, "Dated reservations overlap.");
            }
            foreach (var receipt in receipts)
                Require(model.Guests.Any(guest => guest.Application.Id == receipt.GuestId && guest.ReceiptPosted), "Unposted checkout receipt.");
            SpecialBookings(model, expected.SpecialBookings, calendar, schedules);
        }

        static void ValidateScheduledOffer(ScheduledOfferSnapshot offer, HotelCalendar calendar, GuestScheduleSystem schedules)
        {
            Require(offer != null, "Missing dated offer."); Booking(offer.Application);
            var value = SnapshotData.Offer(offer);
            float expectedSleep = calendar.At(value.ArrivalDay, calendar.Settings.SleepHour);
            float expectedWake = Math.Max(expectedSleep + (value.CheckoutAt - expectedSleep) * .5f,
                value.CheckoutAt - calendar.Settings.SecondsPerDay / 12);
            if (schedules != null)
            {
                var timing = schedules.DatedTimingFor(value.Application, value.ArrivalDay, value.ArrivalAt, calendar);
                expectedSleep = timing.SleepAt; expectedWake = timing.WakeAt;
            }
            Require(value.ArrivalAt / (double)calendar.Settings.SecondsPerDay < 1000000 &&
                value.CheckoutAt / (double)calendar.Settings.SecondsPerDay < 1000001, "Offer time exceeds the supported calendar range.");
            Require(value.ArrivalDay <= 1000000 && calendar.DayAt(value.ArrivalAt) == value.ArrivalDay &&
                calendar.DayAt(value.CheckoutAt) == value.ArrivalDay + 1 &&
                Math.Abs(value.SleepAt - expectedSleep) < .01f &&
                Math.Abs(value.WakeAt - expectedWake) < .01f &&
                Math.Abs(value.CheckoutAt - calendar.At(value.ArrivalDay + 1, calendar.Settings.CheckoutHour)) < .01f,
                "Dated offer is not a single-night stay.");
        }
        static void ValidateOperatingReceipt(ReceiptSnapshot receipt, int[] rooms)
        {
            Text(receipt.GuestId); Text(receipt.Name); Text(receipt.Review, 4096, true);
            Require(rooms.Contains(receipt.RoomId) && receipt.Price >= 0 && receipt.Compensation >= 0 && receipt.Compensation <= receipt.Price,
                "Invalid operating receipt."); Range(receipt.Satisfaction, 0, 100);
            ReceiptAgreedPrice(receipt);
        }
    }
}
