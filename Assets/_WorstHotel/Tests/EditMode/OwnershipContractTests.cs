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
            Assert.That(hotel.Running, Is.True);
            hotel.Tick(hotel.NextReportAt - hotel.Elapsed);
        }

        static void CloseSales(HotelSimulation hotel)
        {
            foreach (var policy in hotel.RoomSalesPolicies.Where(policy => hotel.IsRoomOperational(policy.RoomId)))
                Require(hotel.SetRoomSalesPolicy(0, policy.RoomId, false, policy.Price, policy.Revision));
        }

        static HotelModelSnapshot Wire(HotelSimulation hotel, long sequence) =>
            JsonUtility.FromJson<HotelModelSnapshot>(JsonUtility.ToJson(hotel.CaptureSnapshot(Epoch, sequence)));

        static string State(HotelSimulation hotel) => JsonUtility.ToJson(hotel.CaptureSnapshot(Epoch, 1));

        static GuestStay CheckInGuest(HotelSimulation hotel, int price)
        {
            var offer = hotel.BookingOffers.Where(row => row.ArrivalDay == 1 &&
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
        public void ProductionFirstCloseSeparatesPaymentWhileExplicitNullPreservesLegacyAccounting()
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
            Assert.That(hotel.NextReportAt, Is.LessThan(hotel.Calendar.At(2, 10)));
            CloseSales(hotel); // An intentionally empty first night isolates the production opening reserve.

            CloseNextPeriod(hotel);

            var report = hotel.LastReport;
            var payment = report.ContractPayment;
            Assert.That(payment, Is.Not.Null);
            Assert.That(report.Receipts, Is.Empty);
            Assert.That(report.OpeningCash, Is.EqualTo(1600));
            Assert.That(report.OperatingCost, Is.EqualTo(350));
            Assert.That(report.Net, Is.EqualTo(-350), "Contract payment is not a second operating expense.");
            Assert.That(payment.Due, Is.EqualTo(250));
            Assert.That(payment.PaidAmount, Is.EqualTo(250));
            Assert.That(payment.FundsBeforePayment, Is.EqualTo(1250));
            Assert.That(payment.AssessedRooms, Is.EqualTo(6));
            Assert.That(payment.OwnershipLost, Is.False);
            Assert.That(payment.Shortfall, Is.Zero);
            Assert.That(payment.CashAfterPayment, Is.EqualTo(1000));
            Assert.That(report.Cash, Is.EqualTo(report.OpeningCash + report.Net - payment.PaidAmount));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(report.Cash));
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
            Assert.That(hotel.OwnershipLost, Is.False);
            Assert.That(hotel.Running, Is.True);
        }

        [TestCase(600, true)]
        [TestCase(599, false)]
        public void ExactCashPaysInFullAndOneShortPaysNothing(int openingCash, bool succeeds)
        {
            var hotel = Create(Isolated(new OwnershipContractSettings()), openingCash);
            CloseNextPeriod(hotel);

            var report = hotel.LastReport;
            var payment = report.ContractPayment;
            Assert.That(payment, Is.Not.Null);
            Assert.That(payment.Due, Is.EqualTo(250));
            Assert.That(payment.FundsBeforePayment, Is.EqualTo(openingCash - 350));
            Assert.That(payment.PaidAmount, Is.EqualTo(succeeds ? 250 : 0));
            Assert.That(payment.Shortfall, Is.EqualTo(succeeds ? 0 : 1));
            Assert.That(payment.OwnershipLost, Is.EqualTo(!succeeds));
            Assert.That(payment.CashAfterPayment, Is.EqualTo(succeeds ? 0 : 249));
            Assert.That(report.Net, Is.EqualTo(-350));
            Assert.That(report.Cash, Is.EqualTo(payment.CashAfterPayment));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(report.Cash));
            Assert.That(hotel.Running, Is.EqualTo(succeeds));
            Assert.That(hotel.OwnershipLost, Is.EqualTo(!succeeds));
            Assert.That(hotel.ContractDue, Is.EqualTo(succeeds ? 275 : 250));
            Assert.That(hotel.NextContractDue, Is.EqualTo(succeeds ? 300 : 0));
            Assert.That(hotel.ContractAssessedRooms, Is.EqualTo(6));
            Assert.That(hotel.ReportSequence, Is.EqualTo(1));
        }

        [Test]
        public void OnlySuccessfulAccountingClosesAdvanceTheDailyIncrease()
        {
            var hotel = Create(Isolated(new OwnershipContractSettings()));
            hotel.Tick(hotel.Calendar.At(2, 0));
            Assert.That(hotel.CalendarDay, Is.EqualTo(2));
            Assert.That(hotel.ReportSequence, Is.Zero);
            Assert.That(hotel.ContractDue, Is.EqualTo(250));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(1600));

            CloseNextPeriod(hotel);
            var first = hotel.LastReport;
            Assert.That(hotel.ContractDue, Is.EqualTo(275));
            Assert.That(hotel.NextContractDue, Is.EqualTo(300));
            hotel.Tick(0);
            hotel.Tick(.25f);
            Assert.That(hotel.ReportSequence, Is.EqualTo(1));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(1000));

            CloseNextPeriod(hotel);
            Assert.That(hotel.DayReports.Select(report => report.ContractPayment.Due), Is.EqualTo(new[] { 250, 275 }));
            Assert.That(hotel.DayReports.Select(report => report.ContractPayment.PaidAmount), Is.EqualTo(new[] { 250, 275 }));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(375));
            Assert.That(hotel.ContractDue, Is.EqualTo(300));
            Assert.That(hotel.NextContractDue, Is.EqualTo(325));
            Assert.That(first.ContractPayment.Due, Is.EqualTo(250), "Published payment history remains immutable.");
            Assert.That(first.Cash, Is.EqualTo(1000));
            Assert.That(hotel.Running, Is.True);
        }

        [Test]
        public void SalesDoNotChangeAssessmentAndRestorationAdds240OnlyToTheNextPeriod()
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
            Assert.That(hotel.LastReport.ContractPayment.AssessedRooms, Is.EqualTo(6));
            Assert.That(hotel.LastReport.ContractPayment.PaidAmount, Is.EqualTo(250));
            Assert.That(hotel.LastReport.CapitalSpend, Is.EqualTo(4500));
            Assert.That(hotel.LastReport.Net, Is.EqualTo(-4500 - 350));
            Assert.That(hotel.LastReport.Cash, Is.EqualTo(4900));
            Assert.That(hotel.ContractAssessedRooms, Is.EqualTo(10));
            Assert.That(hotel.ContractDue, Is.EqualTo(515));
            Assert.That(hotel.NextContractDue, Is.EqualTo(540));

            CloseNextPeriod(hotel);
            Assert.That(hotel.LastReport.ContractPayment.AssessedRooms, Is.EqualTo(10));
            Assert.That(hotel.LastReport.ContractPayment.PaidAmount, Is.EqualTo(515));
            Assert.That(hotel.LastReport.CapitalSpend, Is.Zero);
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
            Assert.That(report.ContractPayment.FundsBeforePayment, Is.EqualTo(openingCash + report.Net));
            Assert.That(report.ContractPayment.PaidAmount, Is.Zero);
            Assert.That(report.Cash, Is.EqualTo(openingCash + report.Net), "Purchases and checkout income must not be posted twice at close.");
            Assert.That(hotel.Economy.Cash, Is.EqualTo(report.Cash));
            Assert.That(hotel.OwnershipLost, Is.True);
            Assert.That(hotel.Running, Is.False);
        }

        [Test]
        public void TickStopsAtFailureBeforeFutureCheckoutAndLaterCommandsCannotSpendOrRevive()
        {
            var hotel = Create(Isolated(new OwnershipContractSettings(baseDue: 1300)));
            var guest = CheckInGuest(hotel, 180);
            float boundary = hotel.Calendar.At(2, 6);
            Assert.That(guest.Agent.CheckoutTime, Is.EqualTo(hotel.Calendar.At(2, 10)));

            hotel.Tick(hotel.Calendar.At(3, 12) - hotel.Elapsed);

            Assert.That(hotel.Elapsed, Is.EqualTo(boundary).Within(.001f));
            Assert.That(hotel.Calendar.DisplayTime, Is.EqualTo("06:00"));
            Assert.That(hotel.OwnershipLost, Is.True);
            Assert.That(hotel.Running, Is.False);
            Assert.That(hotel.ReportSequence, Is.EqualTo(1));
            Assert.That(hotel.LastReport.Receipts, Is.Empty);
            Assert.That(guest.ReceiptPosted, Is.False, "The future checkout must not fund a failed earlier close.");
            Assert.That(hotel.LastReport.ContractPayment.PaidAmount, Is.Zero);
            Assert.That(hotel.LastReport.ContractPayment.Shortfall, Is.EqualTo(50));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(1250), "Enough remains to buy either upgrade if the terminal gate is missing.");
            string stopped = State(hotel);
            var report = hotel.LastReport;
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
            Assert.That(State(hotel), Is.EqualTo(stopped), "Rejected actions must leave all captured simulation state unchanged.");
        }

        [Test]
        public void MirrorsApplyPaymentOnceRejectForgedCashAtomicallyAndCannotReviveOwnership()
        {
            var host = Create(Isolated(new OwnershipContractSettings()));
            var mirror = Create(Isolated(new OwnershipContractSettings()));
            mirror.EnableReadOnlyMirror();
            var opening = Wire(host, 1);
            Require(mirror.ApplySnapshot(opening));

            CloseNextPeriod(host);
            var paid = Wire(host, 2);
            Require(mirror.ApplySnapshot(paid));
            Assert.That(State(mirror), Is.EqualTo(State(host)));
            Assert.That(mirror.LastReport.ContractPayment.PaidAmount, Is.EqualTo(250));
            Assert.That(mirror.Economy.Cash, Is.EqualTo(1000));
            string paidState = State(mirror);
            mirror.Tick(mirror.Operations.SecondsPerDay);
            Assert.That(mirror.ApplySnapshot(paid).Success, Is.False);
            Assert.That(State(mirror), Is.EqualTo(paidState));
            Require(mirror.ApplySnapshot(Wire(host, 3)));
            Assert.That(mirror.Economy.Cash, Is.EqualTo(1000), "A newer packet carrying the same payment must not debit it again.");

            CloseNextPeriod(host);
            CloseNextPeriod(host);
            Assert.That(host.OwnershipLost, Is.True);
            var forged = Wire(host, 4);
            forged.Reports.Last().Cash++;
            string before = State(mirror);
            Assert.That(mirror.ApplySnapshot(forged).Success, Is.False,
                "The report closing cash must reconcile with its frozen contract payment.");
            Assert.That(State(mirror), Is.EqualTo(before));
            Assert.That(mirror.AppliedSnapshotSequence, Is.EqualTo(3));
            Require(mirror.ApplySnapshot(Wire(host, 4)));
            Assert.That(State(mirror), Is.EqualTo(State(host)));
            Assert.That(mirror.OwnershipLost, Is.True);
            Assert.That(mirror.Running, Is.False);
            Assert.That(mirror.LastReport.ContractPayment.Due, Is.EqualTo(300));
            Assert.That(mirror.LastReport.ContractPayment.PaidAmount, Is.Zero);
            Assert.That(mirror.LastReport.ContractPayment.FundsBeforePayment, Is.EqualTo(25));
            Assert.That(mirror.Economy.Cash, Is.EqualTo(25));

            string terminal = State(mirror);
            opening.Sequence = 5; // Otherwise valid earlier running state, forged as a newer same-epoch packet.
            Assert.That(mirror.ApplySnapshot(opening).Success, Is.False);
            Assert.That(mirror.ApplySnapshot(paid).Success, Is.False);
            mirror.Tick(mirror.Operations.SecondsPerDay);
            Assert.That(mirror.PurchaseBoilerUpgrade(0).Success, Is.False);
            Assert.That(mirror.StartOperations().Success, Is.False);
            Assert.That(State(mirror), Is.EqualTo(terminal));
            Assert.That(mirror.AppliedSnapshotSequence, Is.EqualTo(4));
            Require(mirror.ApplySnapshot(Wire(host, 5)));
            Assert.That(State(mirror), Is.EqualTo(State(host)));
        }
    }
}
