using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;

namespace WorstHotel.Tests
{
    public sealed class SupplyFinanceTests
    {
        static HotelSimulation Create(int cash, OwnershipContractSettings contract = null)
        {
            var guests = new[] {
                new GuestProfile(GuestKind.Budget, "Budget", "", 180, .85f, 18, .7f, 28, 90, .55f),
                new GuestProfile(GuestKind.ColdSensitive, "Cold", "", 300, 1.05f, 20, 1.35f, 16, 65, .4f),
                new GuestProfile(GuestKind.Business, "Business", "", 450, 1, 19.5f, 1, 10, 45, .25f) };
            var settings = new SessionSettings(guests, Enumerable.Range(101, 6).Select(id => new RoomProfile(id, "Room " + id)),
                new BoilerSettings(), new EconomySettings(startingCash: cash, dailyOperatingCost: 350));
            var model = new HotelSimulation(settings, settings.Rooms.Select(room => new RoomState(room)).ToArray(),
                new LivingHotelSettings(), operations: new OperationsSettings(contract: contract));
            Assert.That(model.StartOperations().Success, Is.True);
            return model;
        }

        // Isolates the accounting boundary; this does not pretend to create or deliver a stock order.
        static CommandResult Pay(HotelSimulation model, int cost, bool laundry) =>
            (CommandResult)typeof(HotelSimulation).GetMethod("PaySupplyOrder", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(model, new object[] { cost, laundry });

        [Test]
        public void ProductionSupplyCostsAndReputationBaselineMatchFirstPassValues()
        {
            var economy = AssetDatabase.LoadAssetAtPath<EconomyConfig>("Assets/_WorstHotel/ScriptableObjects/Economy.asset").ToData();
            Assert.That(economy.LaundrySetCost, Is.EqualTo(18));
            Assert.That(economy.BulbPackSize, Is.EqualTo(3));
            Assert.That(economy.BulbPackCost, Is.EqualTo(45));
            Assert.That(economy.SupplyDeliveryHour, Is.EqualTo(6));
            Assert.That(economy.DailyOperatingCost, Is.EqualTo(350));
            Assert.That(economy.InitialReputation, Is.EqualTo(60));
            var sales = AssetDatabase.LoadAssetAtPath<SessionConfig>("Assets/_WorstHotel/ScriptableObjects/PrototypeSession.asset").OperationsData().Sales;
            Assert.That(sales.ReputationBaseline, Is.EqualTo(economy.InitialReputation));
            Assert.That(sales.ReputationDemandMultiplier(0), Is.EqualTo(.8f));
            Assert.That(sales.ReputationDemandMultiplier(60), Is.EqualTo(1));
            Assert.That(sales.ReputationDemandMultiplier(100), Is.EqualTo(1.1f));
        }

        [TestCase(0, .8f)]
        [TestCase(30, .9f)]
        [TestCase(60, 1f)]
        [TestCase(80, 1.05f)]
        [TestCase(100, 1.1f)]
        public void ReputationCurveIsConservativeAndAnchoredAtSixty(float reputation, float expected)
        {
            var sales = new SalesSettings();
            Assert.That(sales.ReputationDemandMultiplier(reputation), Is.EqualTo(expected).Within(.000001f));
        }

        [TestCase(120)]
        [TestCase(180)]
        [TestCase(360)]
        [TestCase(650)]
        public void BaselineKeepsExistingPriceProbabilityAndPoorReputationDoesNotTurnItOff(int price)
        {
            var sales = new SalesSettings();
            double previous = Math.Min(1, sales.BaseDemand * Math.Pow(180d / price, sales.PriceElasticity));
            Assert.That(sales.DemandProbability(180, price, 60), Is.EqualTo(previous).Within(1e-12));
            Assert.That(sales.DemandProbability(180, price, 0), Is.GreaterThan(0).And.LessThan(previous));
            Assert.That(sales.DemandProbability(180, price, 100), Is.GreaterThanOrEqualTo(previous).And.LessThanOrEqualTo(1));
            Assert.That(new SalesSettings(baseDemand: 0).DemandProbability(180, price, 100), Is.Zero);
        }

        [Test]
        public void ActualOrdinaryEnquiriesRespectReputationBeforeAnyGuestReceipts()
        {
            var config = AssetDatabase.LoadAssetAtPath<SessionConfig>("Assets/_WorstHotel/ScriptableObjects/PrototypeSession.asset");
            Assert.That(SalesSettings.DecisionsPerDay, Is.EqualTo(12));
            var originalProbability = typeof(SalesSettings).GetMethod("DemandProbability", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(int), typeof(int) }, null);
            Assert.That(originalProbability, Is.Not.Null);
            bool differentReservationCount = false;

            HotelSimulation StartAtReputation(float reputation, int price)
            {
                // Transient copies preserve every production setting except initial reputation.
                var copy = UnityEngine.Object.Instantiate(config);
                var economy = UnityEngine.Object.Instantiate(config.economy);
                try
                {
                    economy.initialReputation = reputation; copy.economy = economy;
                    var settings = copy.ToData();
                    var model = new HotelSimulation(settings, settings.Rooms.Select(room => new RoomState(room)).ToArray(),
                        copy.living.ToData(), copy.needs.ToData(), copy.noise.ToData(), copy.heater.ToData(),
                        copy.electricity.ToData(), copy.housekeeping.ToData(), copy.services.ToData(),
                        copy.infrastructure.ToData(), copy.OperationsData());
                    Assert.That(model.StartOperations().Success, Is.True);
                    foreach (var policy in model.RoomSalesPolicies.Where(row => row.OpenForSale))
                        Assert.That(model.SetRoomSalesPolicy(0, policy.RoomId, true, price, policy.Revision).Success, Is.True);
                    return model;
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(copy);
                    UnityEngine.Object.DestroyImmediate(economy);
                }
            }

            // Same production seed and open-room count; a few legal rates avoid a full-hotel ceiling masking demand.
            foreach (int price in new[] { 180, 300, 450, 650 })
            {
                var low = StartAtReputation(0, price);
                var normal = StartAtReputation(60, price);
                var high = StartAtReputation(100, price);
                var offers = normal.BookingOffers.Where(offer => offer.ArrivalDay == 1).ToArray();
                Assert.That(offers.Length, Is.EqualTo(12));
                foreach (var offer in offers)
                {
                    double previous = (double)originalProbability.Invoke(normal.Operations.Sales,
                        new object[] { offer.Application.ReferencePrice, price });
                    Assert.That(normal.Operations.Sales.DemandProbability(offer.Application.ReferencePrice, price, 60),
                        Is.EqualTo(previous), "Baseline must preserve the existing price-demand method exactly.");
                }
                float until = normal.SalesDecisionAt(1, SalesSettings.DecisionsPerDay - 1) + .25f;
                Assert.That(until, Is.LessThan(offers.Min(offer => offer.ArrivalAt)));
                foreach (var model in new[] { low, normal, high })
                {
                    Assert.That(model.Operations.Sales.Seed, Is.EqualTo(config.bookingSeed));
                    Assert.That(model.BookingOffers.Select(offer => offer.Id), Is.EqualTo(normal.BookingOffers.Select(offer => offer.Id)));
                    while (model.Elapsed < until) model.Tick(Math.Min(1, until - model.Elapsed));
                    Assert.That(model.SalesDecisionCursors.Single(cursor => cursor.ArrivalDay == 1).NextOfferIndex, Is.EqualTo(12));
                    Assert.That(model.Guests, Is.Empty);
                    Assert.That(model.CurrentReceipts, Is.Empty);
                    Assert.That(model.DayReports, Is.Empty);
                    Assert.That(model.Economy.Cash, Is.EqualTo(config.economy.startingCash));
                    Assert.That(model.Reservations.All(row => row.IsAutomatic && row.Offer.ArrivalDay == 1 && row.Price == price), Is.True);
                }
                Assert.That(low.Economy.Reputation, Is.Zero);
                Assert.That(normal.Economy.Reputation, Is.EqualTo(60));
                Assert.That(high.Economy.Reputation, Is.EqualTo(100));
                Assert.That(low.Reservations.Count, Is.LessThanOrEqualTo(normal.Reservations.Count));
                Assert.That(high.Reservations.Count, Is.GreaterThanOrEqualTo(normal.Reservations.Count));
                differentReservationCount |= low.Reservations.Count != normal.Reservations.Count || high.Reservations.Count != normal.Reservations.Count;
                TestContext.Out.WriteLine("First-day ordinary reservations at $" + price + ": rep0=" + low.Reservations.Count +
                    ", rep60=" + normal.Reservations.Count + ", rep100=" + high.Reservations.Count + "; seed=" + config.bookingSeed);
            }
            Assert.That(differentReservationCount, Is.True, "At least one actual booking count must respond to reputation.");
        }

        [Test]
        public void InvalidSupplyAndDemandTuningIsRejected()
        {
            Assert.Throws<ArgumentException>(() => new EconomySettings(laundrySetCost: -1));
            Assert.Throws<ArgumentException>(() => new EconomySettings(bulbPackSize: 0));
            Assert.Throws<ArgumentException>(() => new EconomySettings(bulbPackCost: -1));
            Assert.Throws<ArgumentException>(() => new EconomySettings(supplyDeliveryHour: 24));
            Assert.Throws<ArgumentException>(() => new EconomySettings(supplyDeliveryHour: float.NaN));
            Assert.Throws<ArgumentException>(() => new SalesSettings(reputationBaseline: 0));
            Assert.Throws<ArgumentException>(() => new SalesSettings(reputationBaseline: 100));
            Assert.Throws<ArgumentException>(() => new SalesSettings(minimumReputationDemand: 0));
            Assert.Throws<ArgumentException>(() => new SalesSettings(maximumReputationDemand: float.NaN));
        }

        [Test]
        public void SeparateSupplyCategoriesPublishOnceAndResetWithoutAnotherDebit()
        {
            var hotel = Create(1050);
            Assert.That(Pay(hotel, 72, true).Success, Is.True);
            Assert.That(Pay(hotel, 45, false).Success, Is.True);
            Assert.That(hotel.Economy.Cash, Is.EqualTo(933));
            Assert.That(hotel.PeriodLaundrySpend, Is.EqualTo(72));
            Assert.That(hotel.PeriodBulbSpend, Is.EqualTo(45));
            hotel.Tick(hotel.NextReportAt - hotel.Elapsed);
            var report = hotel.LastReport;
            Assert.That(report.LaundrySpend, Is.EqualTo(72));
            Assert.That(report.BulbSpend, Is.EqualTo(45));
            Assert.That(report.Net, Is.EqualTo(-350 - 72 - 45));
            Assert.That(report.Cash, Is.EqualTo(583));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(583));
            Assert.That(hotel.Economy.Reputation, Is.EqualTo(60), "An empty report and supplies do not alter reputation.");
            Assert.That(hotel.PeriodLaundrySpend, Is.Zero);
            Assert.That(hotel.PeriodBulbSpend, Is.Zero);
            hotel.Tick(hotel.NextReportAt - hotel.Elapsed);
            Assert.That(hotel.LastReport.LaundrySpend, Is.Zero);
            Assert.That(hotel.LastReport.BulbSpend, Is.Zero);
            Assert.That(hotel.Economy.Cash, Is.EqualTo(233));
            Assert.That(report.LaundrySpend, Is.EqualTo(72), "Published expense history stays frozen.");
        }

        [Test]
        public void SupplyPurchaseMaySpendContractCashAndFailureFreezesItsExpense()
        {
            var hotel = Create(1050, new OwnershipContractSettings(baseDue: 600));
            hotel.Tick(hotel.NextReportAt - hotel.Elapsed);
            Assert.That(hotel.Economy.Cash, Is.EqualTo(700));
            Assert.That(Pay(hotel, 150, true).Success, Is.True);
            Assert.That(hotel.Economy.Cash, Is.EqualTo(550));
            Assert.That(Pay(hotel, 551, false).Success, Is.False);
            Assert.That(Pay(hotel, -1, true).Success, Is.False);
            Assert.That(hotel.PeriodLaundrySpend, Is.EqualTo(150));
            Assert.That(hotel.PeriodBulbSpend, Is.Zero);
            hotel.Tick(hotel.NextContractAt - hotel.Elapsed);
            Assert.That(hotel.OwnershipLost, Is.True);
            Assert.That(hotel.LastContractPayment.PaidAmount, Is.Zero);
            Assert.That(hotel.LastContractPayment.Shortfall, Is.EqualTo(50));
            Assert.That(hotel.OwnershipLossReport.LaundrySpend, Is.EqualTo(150));
            Assert.That(hotel.OwnershipLossReport.Net, Is.EqualTo(-150));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(550));
            Assert.That(Pay(hotel, 18, true).Success, Is.False);
            Assert.That(hotel.Economy.Cash, Is.EqualTo(550));
            var mirror = Create(1050);
            mirror.EnableReadOnlyMirror();
            Assert.That(Pay(mirror, 45, false).Success, Is.False);
            Assert.That(mirror.Economy.Cash, Is.EqualTo(1050));
            Assert.That(mirror.PeriodBulbSpend, Is.Zero);
        }

        [Test]
        public void ReportSupplyTotalsExcludeContractAndRejectExpenseOverflow()
        {
            var receipt = new GuestReceipt("guest", "Guest", 101, 180, 60, 45, "Existing refund.");
            var report = new DayReport(1, new[] { receipt }, 1350, 350, 618, 60, 60, 20, 30,
                new ContractPayment(350, 350, 968, 6), laundrySpend: 72, bulbSpend: 45);
            Assert.That(report.Gross, Is.EqualTo(180));
            Assert.That(report.Compensation, Is.EqualTo(45));
            Assert.That(report.Net, Is.EqualTo(-382));
            Assert.That(report.OpeningCash + report.Net - report.ContractPayment.PaidAmount, Is.EqualTo(report.Cash));
            Assert.Throws<ArgumentException>(() => new DayReport(1, Array.Empty<GuestReceipt>(), 0, 0, 0, 60, 1, laundrySpend: -1));
            Assert.Throws<ArgumentException>(() => new DayReport(1, Array.Empty<GuestReceipt>(), 0, 0, 0, 60, 1, laundrySpend: int.MaxValue, bulbSpend: 1));
        }
    }
}
