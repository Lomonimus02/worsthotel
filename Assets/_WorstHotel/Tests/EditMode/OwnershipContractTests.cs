using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace WorstHotel.Tests
{
    /// <summary>Contract boundaries against production data. Cash overrides and headless
    /// key/travel adapters are explicit fixture setup, not evidence of a played balance run.</summary>
    public sealed class OwnershipContractTests
    {
        const long Epoch = 92630;

        static void Require(CommandResult result) => Assert.That(result.Success, Is.True, result.Message);

        static HotelSimulation Create(OperationsSettings operations = null, int? cash = null)
        {
            var config = AssetDatabase.LoadAssetAtPath<SessionConfig>(
                "Assets/_WorstHotel/ScriptableObjects/PrototypeSession.asset");
            Assert.That(config, Is.Not.Null);
            var settings = config.ToData();
            Assert.That(settings.Economy.StartingCash, Is.EqualTo(1600));
            Assert.That(settings.Economy.DailyOperatingCost, Is.EqualTo(350));
            var hotel = new HotelSimulation(settings, settings.Rooms.Select(room => new RoomState(room)).ToArray(),
                config.living.ToData(), config.needs.ToData(), config.noise.ToData(), config.heater.ToData(),
                config.electricity.ToData(), config.housekeeping.ToData(), config.services.ToData(),
                config.infrastructure.ToData(), operations ?? config.OperationsData());
            // Set labelled fixture funds before StartOperations captures the opening balance.
            if (cash.HasValue) Require(hotel.Economy.DebugSetCash(cash.Value));
            Require(hotel.StartOperations());
            return hotel;
        }

        static OperationsSettings Isolated(OwnershipContractSettings contract) =>
            new OperationsSettings(contract: contract); // Explicitly no automatic bookings in boundary fixtures.

        static void CloseNextPeriod(HotelSimulation hotel)
        {
            AdvanceTo(hotel, hotel.NextReportAt);
        }

        static void AdvanceTo(HotelSimulation hotel, float boundary)
        {
            Assert.That(hotel.Running, Is.True);
            Assert.That(boundary, Is.GreaterThan(hotel.Elapsed));
            hotel.Tick(boundary - hotel.Elapsed);
        }

        static void CloseSales(HotelSimulation hotel)
        {
            foreach (var policy in hotel.RoomSalesPolicies.Where(policy => hotel.IsRoomOperational(policy.RoomId)))
                Require(hotel.SetRoomSalesPolicy(0, policy.RoomId, false, policy.Price, policy.Revision));
        }

        static HotelModelSnapshot Wire(HotelSimulation hotel, long sequence) =>
            JsonUtility.FromJson<HotelModelSnapshot>(JsonUtility.ToJson(hotel.CaptureSnapshot(Epoch, sequence)));

        static string State(HotelSimulation hotel) => JsonUtility.ToJson(hotel.CaptureSnapshot(Epoch, 1));

        static GuestStay CheckInGuest(HotelSimulation hotel, int price, int arrivalDay = 1)
        {
            var offer = hotel.BookingOffers.Where(row => row.ArrivalDay == arrivalDay &&
                row.Application.Archetype.Kind == GuestKind.Budget).OrderBy(row => row.ArrivalAt).First();
            Require(hotel.AcceptBooking(0, offer.Id, 101, price));
            hotel.Tick(offer.ArrivalAt + .25f - hotel.Elapsed);
            var guest = hotel.Guests.Single();
            // Explicit headless route and physical key handoff; no simulated scene navigation.
            Require(hotel.SignalGuestReachedReception(guest.GuestId));
            Require(ModelKeyHandoff.CheckIn(hotel, 0, guest.GuestId));
            Require(hotel.SignalGuestReachedRoom(guest.GuestId));
            Require(hotel.RegisterGuestPhysicalStaging(guest.GuestId));
            Require(hotel.SignalGuestActivityReady(guest.GuestId, GuestAgentState.InRoom, GuestActivity.QuietRest));
            return guest;
        }

        [Test]
        public void ProductionChargesAtSixAndFirstContractAtTwentyTwoAfterCheckoutWhileNullDisablesContract()
        {
            var hotel = Create();
            Assert.That(hotel.ContractEnabled, Is.True);
            Assert.That(hotel.Operations.SecondsPerDay, Is.EqualTo(720));
            Assert.That(hotel.AutomaticBookingsEnabled, Is.True);
            Assert.That(hotel.RoomSalesPolicies.Count(policy => policy.OpenForSale), Is.EqualTo(4));
            Assert.That(hotel.ContractBaseRooms, Is.EqualTo(6));
            Assert.That(hotel.ContractAssessedRooms, Is.EqualTo(6));
            Assert.That(hotel.ContractDue, Is.EqualTo(250));
            Assert.That(hotel.NextContractDue, Is.EqualTo(275));
            Assert.That(hotel.NextReportAt, Is.EqualTo(hotel.Calendar.At(2, 6)));
            Assert.That(hotel.Operations.OperatingCostHour, Is.EqualTo(6));
            Assert.That(hotel.Operations.Contract.PaymentHour, Is.EqualTo(22));
            Assert.That(hotel.Operations.Contract.FirstPaymentDay, Is.EqualTo(2));
            Assert.That(hotel.NextOperatingCostAt, Is.EqualTo(hotel.Calendar.At(2, 6)));
            Assert.That(hotel.FirstContractAt, Is.EqualTo(hotel.Calendar.At(2, 22)));
            Assert.That(hotel.NextContractAt, Is.EqualTo(hotel.FirstContractAt));
            Assert.That(hotel.BookingOffers.Where(offer => offer.ArrivalDay == 1)
                .All(offer => offer.CheckoutAt < hotel.FirstContractAt), Is.True,
                "The first deadline follows the first night's normal checkout opportunity.");
            CloseSales(hotel); // An intentionally empty first night isolates the production opening reserve.

            AdvanceTo(hotel, hotel.Calendar.At(2, 6) - .25f);
            Assert.That(hotel.Economy.Cash, Is.EqualTo(1600));
            Assert.That(hotel.OperatingCostSequence, Is.Zero);
            Assert.That(hotel.ContractSequence, Is.Zero);
            CloseNextPeriod(hotel);

            var report = hotel.LastReport;
            Assert.That(report.ContractPayment, Is.Null);
            Assert.That(report.Receipts, Is.Empty);
            Assert.That(report.OpeningCash, Is.EqualTo(1600));
            Assert.That(report.OperatingCost, Is.EqualTo(350));
            Assert.That(report.Net, Is.EqualTo(-350));
            Assert.That(report.Cash, Is.EqualTo(1250));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(1250));
            Assert.That(hotel.OperatingCostSequence, Is.EqualTo(1));
            Assert.That(hotel.PeriodOperatingSpend, Is.Zero, "The report clears already-posted expenses.");
            Assert.That(hotel.ContractSequence, Is.Zero);
            Assert.That(hotel.ContractDue, Is.EqualTo(250));
            Assert.That(hotel.LastContractPayment, Is.Null);

            AdvanceTo(hotel, hotel.NextContractAt - .25f);
            Assert.That(hotel.Economy.Cash, Is.EqualTo(1250));
            Assert.That(hotel.ContractSequence, Is.Zero);
            AdvanceTo(hotel, hotel.NextContractAt);

            var payment = hotel.LastContractPayment;
            Assert.That(payment, Is.Not.Null);
            Assert.That(payment.Period, Is.EqualTo(1));
            Assert.That(payment.DueAt, Is.EqualTo(hotel.Calendar.At(2, 22)));
            Assert.That(payment.Due, Is.EqualTo(250));
            Assert.That(payment.PaidAmount, Is.EqualTo(250));
            Assert.That(payment.FundsBeforePayment, Is.EqualTo(1250));
            Assert.That(payment.AssessedRooms, Is.EqualTo(6));
            Assert.That(payment.OwnershipLost, Is.False);
            Assert.That(payment.Shortfall, Is.Zero);
            Assert.That(payment.CashAfterPayment, Is.EqualTo(1000));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(1000));
            Assert.That(hotel.PeriodContractPayment, Is.SameAs(payment));
            Assert.That(hotel.ContractSequence, Is.EqualTo(1));
            Assert.That(hotel.ReportSequence, Is.EqualTo(1));
            Assert.That(hotel.LastReport, Is.SameAs(report));
            Assert.That(report.Cash, Is.EqualTo(1250), "Evening payment cannot rewrite the morning report.");
            Assert.That(report.ContractPayment, Is.Null);
            Assert.That(hotel.OwnershipLossReport, Is.Null);
            Assert.That(hotel.Running, Is.True);
            AssertLegacyOperatingClose();
        }

        static void AssertLegacyOperatingClose()
        {
            var hotel = Create(Isolated(null));
            Assert.That(hotel.ContractEnabled, Is.False);
            Assert.That(hotel.ContractDue, Is.Zero);
            Assert.That(hotel.NextContractDue, Is.Zero);

            CloseNextPeriod(hotel);

            Assert.That(hotel.LastReport.ContractPayment, Is.Null);
            Assert.That(hotel.LastReport.Net, Is.EqualTo(-350));
            Assert.That(hotel.LastReport.Cash, Is.EqualTo(1250));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(1250));
            Assert.That(hotel.OperatingCostSequence, Is.EqualTo(1));
            Assert.That(hotel.PeriodOperatingSpend, Is.Zero);
            AdvanceTo(hotel, hotel.Calendar.At(2, 22));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(1250));
            Assert.That(hotel.ContractSequence, Is.Zero);
            Assert.That(hotel.LastContractPayment, Is.Null);
            Assert.That(hotel.PeriodContractPayment, Is.Null);
            Assert.That(hotel.OwnershipLost, Is.False);
            Assert.That(hotel.Running, Is.True);
        }

        [TestCase(600, true)]
        [TestCase(599, false)]
        public void ExactCashPaysInFullAndOneShortPaysNothing(int openingCash, bool succeeds)
        {
            // Labelled cash fixture is set before the morning operating charge, not at collection.
            var hotel = Create(Isolated(new OwnershipContractSettings()), openingCash);
            CloseNextPeriod(hotel);

            var report = hotel.LastReport;
            Assert.That(report.ContractPayment, Is.Null);
            Assert.That(report.Cash, Is.EqualTo(openingCash - 350));
            Assert.That(hotel.Running, Is.True, "Only the evening deadline can revoke ownership.");
            AdvanceTo(hotel, hotel.NextContractAt);

            var payment = hotel.LastContractPayment;
            Assert.That(payment, Is.Not.Null);
            Assert.That(payment.Period, Is.EqualTo(1));
            Assert.That(payment.DueAt, Is.EqualTo(hotel.Calendar.At(2, 22)));
            Assert.That(payment.Due, Is.EqualTo(250));
            Assert.That(payment.FundsBeforePayment, Is.EqualTo(openingCash - 350));
            Assert.That(payment.PaidAmount, Is.EqualTo(succeeds ? 250 : 0));
            Assert.That(payment.Shortfall, Is.EqualTo(succeeds ? 0 : 1));
            Assert.That(payment.OwnershipLost, Is.EqualTo(!succeeds));
            Assert.That(payment.CashAfterPayment, Is.EqualTo(succeeds ? 0 : 249));
            Assert.That(report.Net, Is.EqualTo(-350));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(payment.CashAfterPayment));
            Assert.That(hotel.LastReport, Is.SameAs(report));
            Assert.That(report.Cash, Is.EqualTo(openingCash - 350));
            Assert.That(hotel.Running, Is.EqualTo(succeeds));
            Assert.That(hotel.OwnershipLost, Is.EqualTo(!succeeds));
            Assert.That(hotel.ContractDue, Is.EqualTo(succeeds ? 275 : 250));
            Assert.That(hotel.NextContractDue, Is.EqualTo(succeeds ? 300 : 0));
            Assert.That(hotel.ContractAssessedRooms, Is.EqualTo(6));
            Assert.That(hotel.ReportSequence, Is.EqualTo(1));
            Assert.That(hotel.ContractSequence, Is.EqualTo(1), "Failed attempts also consume a contract period.");
            Assert.That(hotel.OperatingCostSequence, Is.EqualTo(1));
            if (succeeds)
                Assert.That(hotel.OwnershipLossReport, Is.Null);
            else
            {
                var failure = hotel.OwnershipLossReport;
                Assert.That(failure, Is.Not.Null);
                Assert.That(failure, Is.Not.SameAs(report));
                Assert.That(failure.ContractPayment, Is.SameAs(payment));
                Assert.That(failure.OpeningCash, Is.EqualTo(249));
                Assert.That(failure.OperatingCost, Is.Zero);
                Assert.That(failure.Net, Is.Zero);
                Assert.That(failure.Cash, Is.EqualTo(249));
                Assert.That(failure.ServiceSeconds, Is.EqualTo(hotel.Calendar.At(2, 22) - hotel.Calendar.At(2, 6)));
                Assert.That(hotel.DayReports.Count, Is.EqualTo(1));
            }
        }

        [Test]
        public void EmptyProductionChronologyGrowsOnlyAtContractAttemptsAndMidnightDoesNotDebit()
        {
            var hotel = Create();
            CloseSales(hotel);
            AdvanceTo(hotel, hotel.Calendar.At(2, 0));
            Assert.That(hotel.CalendarDay, Is.EqualTo(2));
            Assert.That(hotel.ReportSequence, Is.Zero);
            Assert.That(hotel.ContractSequence, Is.Zero);
            Assert.That(hotel.OperatingCostSequence, Is.Zero);
            Assert.That(hotel.ContractDue, Is.EqualTo(250));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(1600));

            CloseNextPeriod(hotel);
            Assert.That(hotel.Economy.Cash, Is.EqualTo(1250));
            Assert.That(hotel.ContractDue, Is.EqualTo(250));
            Assert.That(hotel.ContractSequence, Is.Zero);
            AdvanceTo(hotel, hotel.NextContractAt);
            var first = hotel.LastContractPayment;
            Assert.That(hotel.Economy.Cash, Is.EqualTo(1000));
            Assert.That(hotel.ContractDue, Is.EqualTo(275));
            Assert.That(hotel.NextContractDue, Is.EqualTo(300));
            Assert.That(hotel.NextContractAt, Is.EqualTo(hotel.Calendar.At(3, 22)));
            hotel.Tick(0);
            hotel.Tick(.25f);
            AdvanceTo(hotel, hotel.Calendar.At(3, 0));
            Assert.That(hotel.ReportSequence, Is.EqualTo(1));
            Assert.That(hotel.ContractSequence, Is.EqualTo(1));
            Assert.That(hotel.OperatingCostSequence, Is.EqualTo(1));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(1000));

            CloseNextPeriod(hotel);
            Assert.That(hotel.Economy.Cash, Is.EqualTo(650));
            Assert.That(hotel.ContractDue, Is.EqualTo(275));
            Assert.That(hotel.LastReport.ContractPayment, Is.SameAs(first));
            Assert.That(hotel.LastReport.Cash, Is.EqualTo(650));
            Assert.That(first.CashAfterPayment, Is.EqualTo(1000), "The next morning's expense follows the frozen payment receipt.");
            Assert.That(hotel.LastReport.Cash, Is.EqualTo(hotel.LastReport.OpeningCash + hotel.LastReport.Net - first.PaidAmount));
            Assert.That(hotel.PeriodContractPayment, Is.Null);
            Assert.That(hotel.PeriodOperatingSpend, Is.Zero);
            AdvanceTo(hotel, hotel.NextContractAt);
            Assert.That(hotel.Economy.Cash, Is.EqualTo(375));
            Assert.That(hotel.LastContractPayment.Period, Is.EqualTo(2));
            Assert.That(hotel.LastContractPayment.Due, Is.EqualTo(275));
            Assert.That(hotel.ContractDue, Is.EqualTo(300));
            Assert.That(hotel.NextContractDue, Is.EqualTo(325));
            Assert.That(first.Due, Is.EqualTo(250), "Published payment history remains immutable.");

            CloseNextPeriod(hotel);
            var morning = hotel.LastReport;
            Assert.That(hotel.Economy.Cash, Is.EqualTo(25));
            Assert.That(hotel.ContractSequence, Is.EqualTo(2));
            Assert.That(hotel.ReportSequence, Is.EqualTo(3));
            Assert.That(hotel.Running, Is.True);
            AdvanceTo(hotel, hotel.NextContractAt);
            Assert.That(hotel.Elapsed, Is.EqualTo(hotel.Calendar.At(4, 22)));
            Assert.That(hotel.ContractSequence, Is.EqualTo(3));
            Assert.That(hotel.ReportSequence, Is.EqualTo(3));
            Assert.That(hotel.OperatingCostSequence, Is.EqualTo(3));
            Assert.That(hotel.LastReport, Is.SameAs(morning));
            Assert.That(hotel.DayReports.Select(report => report.ContractPayment?.Due ?? 0), Is.EqualTo(new[] { 0, 250, 275 }));
            Assert.That(hotel.LastContractPayment.Due, Is.EqualTo(300));
            Assert.That(hotel.LastContractPayment.PaidAmount, Is.Zero);
            Assert.That(hotel.LastContractPayment.Shortfall, Is.EqualTo(275));
            Assert.That(hotel.OwnershipLossReport.ContractPayment, Is.SameAs(hotel.LastContractPayment));
            Assert.That(hotel.OwnershipLossReport.Cash, Is.EqualTo(25));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(25));
            Assert.That(hotel.OwnershipLost, Is.True);
            Assert.That(hotel.Running, Is.False);
        }

        [Test]
        public void NoonReportsAreCashNeutralAndDoNotMoveMorningExpensesOrEveningContracts()
        {
            var hotel = Create(new OperationsSettings(reportHour: 12, contract: new OwnershipContractSettings(),
                operatingCostHour: 6));
            Assert.That(hotel.NextReportAt, Is.EqualTo(hotel.Calendar.At(1, 12)));
            CloseNextPeriod(hotel);
            Assert.That(hotel.LastReport.OperatingCost, Is.Zero);
            Assert.That(hotel.LastReport.Net, Is.Zero);
            Assert.That(hotel.Economy.Cash, Is.EqualTo(1600));
            Assert.That(hotel.ContractDue, Is.EqualTo(250));

            AdvanceTo(hotel, hotel.Calendar.At(2, 6));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(1250));
            Assert.That(hotel.PeriodOperatingSpend, Is.EqualTo(350));
            Assert.That(hotel.OperatingCostSequence, Is.EqualTo(1));
            Assert.That(hotel.ReportSequence, Is.EqualTo(1));
            Assert.That(hotel.ContractSequence, Is.Zero);
            Assert.That(hotel.NextOperatingCostAt, Is.EqualTo(hotel.Calendar.At(3, 6)));
            Assert.That(hotel.NextContractAt, Is.EqualTo(hotel.Calendar.At(2, 22)));

            CloseNextPeriod(hotel);
            Assert.That(hotel.Economy.Cash, Is.EqualTo(1250), "Publishing a report cannot post the operating cost again.");
            Assert.That(hotel.LastReport.OperatingCost, Is.EqualTo(350));
            Assert.That(hotel.LastReport.Cash, Is.EqualTo(1250));
            Assert.That(hotel.PeriodOperatingSpend, Is.Zero);
            Assert.That(hotel.LastReport.ContractPayment, Is.Null);
            Assert.That(hotel.ReportSequence, Is.EqualTo(2));
            Assert.That(hotel.ContractDue, Is.EqualTo(250), "Report sequence must not set the contract amount.");
            Assert.That(hotel.NextContractDue, Is.EqualTo(275));

            AdvanceTo(hotel, hotel.Calendar.At(2, 22));
            var payment = hotel.LastContractPayment;
            Assert.That(payment.Period, Is.EqualTo(1));
            Assert.That(payment.DueAt, Is.EqualTo(hotel.Calendar.At(2, 22)));
            Assert.That(payment.PaidAmount, Is.EqualTo(250));
            Assert.That(hotel.ContractSequence, Is.EqualTo(1));
            Assert.That(hotel.ReportSequence, Is.EqualTo(2));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(1000));
            Assert.That(hotel.PeriodContractPayment, Is.SameAs(payment));

            AdvanceTo(hotel, hotel.Calendar.At(3, 6));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(650));
            Assert.That(hotel.PeriodOperatingSpend, Is.EqualTo(350));
            CloseNextPeriod(hotel);
            Assert.That(hotel.Economy.Cash, Is.EqualTo(650));
            Assert.That(hotel.LastReport.ContractPayment, Is.SameAs(payment));
            Assert.That(hotel.LastReport.OperatingCost, Is.EqualTo(350));
            Assert.That(hotel.LastReport.Cash, Is.EqualTo(hotel.LastReport.OpeningCash + hotel.LastReport.Net - payment.PaidAmount));
            Assert.That(payment.CashAfterPayment, Is.EqualTo(1000));
            Assert.That(hotel.PeriodOperatingSpend, Is.Zero);
            Assert.That(hotel.PeriodContractPayment, Is.Null);
            Assert.That(hotel.LastContractPayment, Is.SameAs(payment));
            Assert.That(hotel.ContractDue, Is.EqualTo(275));
            Assert.That(hotel.NextContractAt, Is.EqualTo(hotel.Calendar.At(3, 22)));
        }

        [Test]
        public void CoincidentEveningExpenseFailureAndReportCloseOnceAndRoundTripTheFrozenNotice()
        {
            var operations = new OperationsSettings(reportHour: 22,
                contract: new OwnershipContractSettings(paymentHour: 22, firstPaymentDay: 2), operatingCostHour: 22);
            // Labelled cash fixture: 949 - day-one expense 350 - day-two expense 350 = 249.
            var host = Create(operations, cash: 949);
            var mirror = Create(operations, cash: 949);
            mirror.EnableReadOnlyMirror();
            Require(mirror.ApplySnapshot(Wire(host, 1)));

            CloseNextPeriod(host);
            Assert.That(host.Elapsed, Is.EqualTo(host.Calendar.At(1, 22)));
            Assert.That(host.Economy.Cash, Is.EqualTo(599));
            Assert.That(host.LastReport.ContractPayment, Is.Null);
            Assert.That(host.ContractSequence, Is.Zero);
            Assert.That(host.NextOperatingCostAt, Is.EqualTo(host.NextContractAt));
            Assert.That(host.NextReportAt, Is.EqualTo(host.NextContractAt));

            AdvanceTo(host, host.NextContractAt);

            var failure = host.OwnershipLossReport;
            var report = host.LastReport;
            Assert.That(host.Elapsed, Is.EqualTo(host.Calendar.At(2, 22)));
            Assert.That(host.OperatingCostSequence, Is.EqualTo(2));
            Assert.That(host.ContractSequence, Is.EqualTo(1));
            Assert.That(host.ReportSequence, Is.EqualTo(2));
            Assert.That(host.DayReports.Count, Is.EqualTo(2));
            Assert.That(host.LastContractPayment.FundsBeforePayment, Is.EqualTo(249), "Operating cost must precede contract collection.");
            Assert.That(host.LastContractPayment.PaidAmount, Is.Zero);
            Assert.That(host.LastContractPayment.Shortfall, Is.EqualTo(1));
            Assert.That(report.ContractPayment, Is.SameAs(host.LastContractPayment), "The same-time report follows the failed payment.");
            Assert.That(report.OpeningCash, Is.EqualTo(599));
            Assert.That(report.OperatingCost, Is.EqualTo(350));
            Assert.That(report.Net, Is.EqualTo(-350));
            Assert.That(report.Cash, Is.EqualTo(249));
            Assert.That(failure, Is.Not.Null.And.Not.SameAs(report));
            Assert.That(failure.ContractPayment, Is.SameAs(host.LastContractPayment));
            Assert.That(failure.OpeningCash, Is.EqualTo(599));
            Assert.That(failure.OperatingCost, Is.EqualTo(350));
            Assert.That(failure.Cash, Is.EqualTo(249));
            Assert.That(host.PeriodOperatingSpend, Is.Zero);
            Assert.That(host.PeriodContractPayment, Is.Null);
            Assert.That(host.Economy.Cash, Is.EqualTo(249));
            Assert.That(host.OwnershipLost, Is.True);
            Assert.That(host.Running, Is.False);

            Require(mirror.ApplySnapshot(Wire(host, 2)));
            Assert.That(State(mirror), Is.EqualTo(State(host)));
            Assert.That(mirror.DayReports.Count, Is.EqualTo(2));
            Assert.That(mirror.OwnershipLossReport.Cash, Is.EqualTo(249));
            Assert.That(mirror.PeriodOperatingSpend, Is.Zero);
            Assert.That(mirror.PeriodContractPayment, Is.Null);
            string stopped = State(host);
            host.Tick(0);
            host.Tick(host.Operations.SecondsPerDay);
            Assert.That(host.LastReport, Is.SameAs(report));
            Assert.That(host.OwnershipLossReport, Is.SameAs(failure));
            Assert.That(State(host), Is.EqualTo(stopped));
            Require(mirror.ApplySnapshot(Wire(host, 3)));
            Assert.That(State(mirror), Is.EqualTo(stopped));
        }

        [Test]
        public void RestorationKeepsCurrent250LockedThroughMorningAndAssessesTenRoomsOnlyAfterPayment()
        {
            var hotel = Create(cash: 10000); // Labelled capital fixture, not a claim that the wing was earned.
            var policy = hotel.RoomSalesPolicies.Single(row => row.RoomId == 106);
            Assert.That(policy.OpenForSale, Is.False);
            Require(hotel.SetRoomSalesPolicy(0, 106, true, policy.Price, policy.Revision));
            Assert.That(hotel.ContractDue, Is.EqualTo(250));
            Assert.That(hotel.NextContractDue, Is.EqualTo(275));
            Assert.That(hotel.ContractAssessedRooms, Is.EqualTo(6));

            Require(hotel.RestoreNorthWing(0));
            Assert.That(hotel.OperationalRoomCount, Is.EqualTo(10));
            Assert.That(hotel.RoomSalesPolicies.Where(row => row.RoomId >= 107).All(row => !row.OpenForSale), Is.True);
            Assert.That(hotel.ContractBaseRooms, Is.EqualTo(6));
            Assert.That(hotel.ContractAssessedRooms, Is.EqualTo(6));
            Assert.That(hotel.ContractDue, Is.EqualTo(250));
            Assert.That(hotel.NextContractDue, Is.EqualTo(275 + 240));
            Assert.That(hotel.PeriodCapitalSpend, Is.EqualTo(4500));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(5500));
            CloseSales(hotel);

            CloseNextPeriod(hotel);
            Assert.That(hotel.LastReport.ContractPayment, Is.Null);
            Assert.That(hotel.LastReport.CapitalSpend, Is.EqualTo(4500));
            Assert.That(hotel.LastReport.Net, Is.EqualTo(-4500 - 350));
            Assert.That(hotel.LastReport.Cash, Is.EqualTo(5150));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(5150));
            Assert.That(hotel.PeriodCapitalSpend, Is.Zero);
            Assert.That(hotel.PeriodOperatingSpend, Is.Zero);
            Assert.That(hotel.ContractAssessedRooms, Is.EqualTo(6));
            Assert.That(hotel.ContractDue, Is.EqualTo(250));
            Assert.That(hotel.NextContractDue, Is.EqualTo(515));
            Assert.That(hotel.ContractSequence, Is.Zero);

            AdvanceTo(hotel, hotel.NextContractAt);
            Assert.That(hotel.LastContractPayment.AssessedRooms, Is.EqualTo(6));
            Assert.That(hotel.LastContractPayment.PaidAmount, Is.EqualTo(250));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(4900));
            Assert.That(hotel.ContractAssessedRooms, Is.EqualTo(10));
            Assert.That(hotel.ContractDue, Is.EqualTo(515));
            Assert.That(hotel.NextContractDue, Is.EqualTo(540));

            CloseNextPeriod(hotel);
            Assert.That(hotel.LastReport.ContractPayment.AssessedRooms, Is.EqualTo(6));
            Assert.That(hotel.LastReport.ContractPayment.PaidAmount, Is.EqualTo(250));
            Assert.That(hotel.LastReport.CapitalSpend, Is.Zero);
            Assert.That(hotel.LastReport.OperatingCost, Is.EqualTo(350));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(4550));
            Assert.That(hotel.ContractDue, Is.EqualTo(515));
            AdvanceTo(hotel, hotel.NextContractAt);
            Assert.That(hotel.LastContractPayment.AssessedRooms, Is.EqualTo(10));
            Assert.That(hotel.LastContractPayment.PaidAmount, Is.EqualTo(515));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(4035));
            Assert.That(hotel.ContractDue, Is.EqualTo(540), "The room surcharge is not compounded each day.");
        }

        [Test]
        public void PostedRevenueAboveTheObligationDoesNotProtectCashSpentOnRealPurchases()
        {
            const int openingCash = 1300;
            var hotel = Create(Isolated(new OwnershipContractSettings()), openingCash);
            var guest = CheckInGuest(hotel, 650);
            hotel.Tick(1);
            Assert.That(guest.Elapsed, Is.GreaterThan(0));
            // Labelled early-departure adapter: exercise actual checkout billing without an overnight balance run.
            Require(hotel.DebugCheckoutGuest(guest.GuestId));
            hotel.Tick(.25f);
            Assert.That(guest.ReceiptPosted, Is.True);
            int received = hotel.Economy.Cash - openingCash;
            Assert.That(received, Is.GreaterThan(hotel.ContractDue));
            Require(hotel.PurchaseBoilerUpgrade(0));
            Require(hotel.PurchaseInsulation(0));
            Assert.That(hotel.PeriodCapitalSpend, Is.EqualTo(1000 + 450));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(openingCash + received - 1450));
            Assert.That(hotel.Economy.Cash, Is.LessThan(350 + 250));

            CloseNextPeriod(hotel);

            var report = hotel.LastReport;
            Assert.That(report.Receipts.Count, Is.EqualTo(1));
            Assert.That(report.Gross, Is.EqualTo(650));
            Assert.That(report.Gross - report.Compensation, Is.EqualTo(received));
            Assert.That(report.CapitalSpend, Is.EqualTo(1450));
            Assert.That(report.Net, Is.EqualTo(received - 1450 - 350));
            Assert.That(report.ContractPayment, Is.Null);
            Assert.That(report.Cash, Is.EqualTo(openingCash + report.Net), "Purchases and checkout income must not be posted twice at close.");
            Assert.That(hotel.Economy.Cash, Is.EqualTo(report.Cash));
            Assert.That(hotel.PeriodCapitalSpend, Is.Zero);
            Assert.That(hotel.PeriodOperatingSpend, Is.Zero);
            Assert.That(hotel.OwnershipLost, Is.False);

            AdvanceTo(hotel, hotel.NextContractAt);
            var payment = hotel.LastContractPayment;
            var failure = hotel.OwnershipLossReport;
            Assert.That(payment.FundsBeforePayment, Is.EqualTo(openingCash + report.Net));
            Assert.That(payment.PaidAmount, Is.Zero);
            Assert.That(payment.Shortfall, Is.EqualTo(250 - report.Cash));
            Assert.That(hotel.LastReport, Is.SameAs(report));
            Assert.That(failure.ContractPayment, Is.SameAs(payment));
            Assert.That(failure.Receipts, Is.Empty, "The morning report already recorded the checkout.");
            Assert.That(failure.OpeningCash, Is.EqualTo(report.Cash));
            Assert.That(failure.CapitalSpend, Is.Zero);
            Assert.That(failure.OperatingCost, Is.Zero);
            Assert.That(failure.Net, Is.Zero);
            Assert.That(failure.Cash, Is.EqualTo(report.Cash));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(report.Cash));
            Assert.That(hotel.OwnershipLost, Is.True);
            Assert.That(hotel.Running, Is.False);
        }

        [Test]
        public void TickStopsAtFailureBeforeFutureCheckoutAndLaterCommandsCannotSpendOrRevive()
        {
            var hotel = Create(Isolated(new OwnershipContractSettings(baseDue: 1300)));
            // Book the second night so its genuine checkout is after the first evening deadline.
            var guest = CheckInGuest(hotel, 180, arrivalDay: 2);
            float boundary = hotel.Calendar.At(2, 22);
            Assert.That(guest.Agent.CheckoutTime, Is.EqualTo(hotel.Calendar.At(3, 10)));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(1250));
            var report = hotel.LastReport;

            hotel.Tick(hotel.Calendar.At(3, 12) - hotel.Elapsed);

            Assert.That(hotel.Elapsed, Is.EqualTo(boundary).Within(.001f));
            Assert.That(hotel.Calendar.DisplayTime, Is.EqualTo("22:00"));
            Assert.That(hotel.OwnershipLost, Is.True);
            Assert.That(hotel.Running, Is.False);
            Assert.That(hotel.ReportSequence, Is.EqualTo(1));
            Assert.That(hotel.ContractSequence, Is.EqualTo(1));
            Assert.That(hotel.OperatingCostSequence, Is.EqualTo(1));
            Assert.That(hotel.LastReport, Is.SameAs(report));
            Assert.That(hotel.LastReport.Receipts, Is.Empty);
            Assert.That(hotel.LastReport.ContractPayment, Is.Null);
            Assert.That(guest.ReceiptPosted, Is.False, "The future checkout must not fund a failed earlier deadline.");
            Assert.That(hotel.LastContractPayment.DueAt, Is.EqualTo(boundary));
            Assert.That(hotel.LastContractPayment.PaidAmount, Is.Zero);
            Assert.That(hotel.LastContractPayment.Shortfall, Is.EqualTo(50));
            Assert.That(hotel.OwnershipLossReport.ContractPayment, Is.SameAs(hotel.LastContractPayment));
            Assert.That(hotel.OwnershipLossReport.Receipts, Is.Empty);
            Assert.That(hotel.Economy.Cash, Is.EqualTo(1250), "Enough remains to buy either upgrade if the terminal gate is missing.");
            string stopped = State(hotel);
            var failure = hotel.OwnershipLossReport;
            hotel.Tick(0);
            hotel.Tick(hotel.Operations.SecondsPerDay * 2);
            Assert.That(hotel.PurchaseBoilerUpgrade(0).Success, Is.False);
            Assert.That(hotel.PurchaseElectricalUpgrade(0, "A").Success, Is.False);
            Assert.That(hotel.PurchaseInsulation(0).Success, Is.False);
            Assert.That(hotel.RestoreNorthWing(0).Success, Is.False);
            Assert.That(hotel.DebugCheckoutGuest(guest.GuestId).Success, Is.False);
            Assert.That(hotel.SetRadiatorSetting(0, 101, 0).Success, Is.False);
            Assert.That(hotel.StartOperations().Success, Is.False);
            Assert.That(hotel.LastReport, Is.SameAs(report));
            Assert.That(hotel.OwnershipLossReport, Is.SameAs(failure));
            Assert.That(State(hotel), Is.EqualTo(stopped), "Rejected actions must leave all captured simulation state unchanged.");
        }

        [Test]
        public void MirrorsApplyPaymentOnceRejectForgedSettlementAtomicallyAndCannotReviveOwnership()
        {
            var host = Create(Isolated(new OwnershipContractSettings()));
            var mirror = Create(Isolated(new OwnershipContractSettings()));
            mirror.EnableReadOnlyMirror();
            var opening = Wire(host, 1);
            Require(mirror.ApplySnapshot(opening));

            AdvanceTo(host, host.NextContractAt);
            var paid = Wire(host, 2);
            Require(mirror.ApplySnapshot(paid));
            Assert.That(State(mirror), Is.EqualTo(State(host)));
            Assert.That(mirror.LastReport.ContractPayment, Is.Null);
            Assert.That(mirror.LastReport.Cash, Is.EqualTo(1250));
            Assert.That(mirror.LastContractPayment.PaidAmount, Is.EqualTo(250));
            Assert.That(mirror.LastContractPayment.Period, Is.EqualTo(1));
            Assert.That(mirror.LastContractPayment.DueAt, Is.EqualTo(mirror.Calendar.At(2, 22)));
            Assert.That(mirror.PeriodContractPayment.PaidAmount, Is.EqualTo(250));
            Assert.That(mirror.ContractSequence, Is.EqualTo(1));
            Assert.That(mirror.Economy.Cash, Is.EqualTo(1000));
            string paidState = State(mirror);
            mirror.Tick(mirror.Operations.SecondsPerDay);
            Assert.That(mirror.ApplySnapshot(paid).Success, Is.False);
            Assert.That(State(mirror), Is.EqualTo(paidState));
            Require(mirror.ApplySnapshot(Wire(host, 3)));
            Assert.That(mirror.Economy.Cash, Is.EqualTo(1000), "A newer packet carrying the same payment must not debit it again.");

            CloseNextPeriod(host);
            Require(mirror.ApplySnapshot(Wire(host, 4)));
            Assert.That(State(mirror), Is.EqualTo(State(host)));
            Assert.That(mirror.LastReport.ContractPayment.CashAfterPayment, Is.EqualTo(1000));
            Assert.That(mirror.LastReport.Cash, Is.EqualTo(650), "A valid report includes the expense posted after its payment.");
            Assert.That(mirror.Economy.Cash, Is.EqualTo(650));
            Assert.That(mirror.PeriodContractPayment, Is.Null);
            Assert.That(mirror.PeriodOperatingSpend, Is.Zero);

            AdvanceTo(host, host.Calendar.At(4, 22));
            Assert.That(host.OwnershipLost, Is.True);
            string before = State(mirror);
            foreach (string forgery in new[] { "payment-due", "failure-cash" })
            {
                var forged = Wire(host, 5);
                if (forgery == "payment-due") forged.Reports.Last().ContractPayment.Due++;
                else forged.Operations.OwnershipLossReport.Cash++;
                Assert.That(mirror.ApplySnapshot(forged).Success, Is.False,
                    forgery + " must be rejected before any state changes.");
                Assert.That(State(mirror), Is.EqualTo(before));
                Assert.That(mirror.AppliedSnapshotSequence, Is.EqualTo(4));
            }
            Require(mirror.ApplySnapshot(Wire(host, 5)));
            Assert.That(State(mirror), Is.EqualTo(State(host)));
            Assert.That(mirror.OwnershipLost, Is.True);
            Assert.That(mirror.Running, Is.False);
            Assert.That(mirror.ReportSequence, Is.EqualTo(3));
            Assert.That(mirror.ContractSequence, Is.EqualTo(3));
            Assert.That(mirror.OperatingCostSequence, Is.EqualTo(3));
            Assert.That(mirror.LastReport.ContractPayment.Due, Is.EqualTo(275));
            Assert.That(mirror.LastReport.ContractPayment.PaidAmount, Is.EqualTo(275));
            Assert.That(mirror.LastContractPayment.Period, Is.EqualTo(3));
            Assert.That(mirror.LastContractPayment.DueAt, Is.EqualTo(mirror.Calendar.At(4, 22)));
            Assert.That(mirror.LastContractPayment.Due, Is.EqualTo(300));
            Assert.That(mirror.LastContractPayment.PaidAmount, Is.Zero);
            Assert.That(mirror.LastContractPayment.FundsBeforePayment, Is.EqualTo(25));
            Assert.That(mirror.OwnershipLossReport.ContractPayment.Due, Is.EqualTo(300));
            Assert.That(mirror.OwnershipLossReport.Cash, Is.EqualTo(25));
            Assert.That(mirror.Economy.Cash, Is.EqualTo(25));

            string terminal = State(mirror);
            opening.Sequence = 6; // Otherwise valid earlier running state, forged as a newer same-epoch packet.
            Assert.That(mirror.ApplySnapshot(opening).Success, Is.False);
            Assert.That(mirror.ApplySnapshot(paid).Success, Is.False);
            mirror.Tick(mirror.Operations.SecondsPerDay);
            Assert.That(mirror.PurchaseBoilerUpgrade(0).Success, Is.False);
            Assert.That(mirror.StartOperations().Success, Is.False);
            Assert.That(State(mirror), Is.EqualTo(terminal));
            Assert.That(mirror.AppliedSnapshotSequence, Is.EqualTo(5));
            Require(mirror.ApplySnapshot(Wire(host, 6)));
            Assert.That(State(mirror), Is.EqualTo(State(host)));
        }
    }
}
