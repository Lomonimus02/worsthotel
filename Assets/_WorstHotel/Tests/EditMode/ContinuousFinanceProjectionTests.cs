using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace WorstHotel.Tests
{
    public sealed class ContinuousFinanceProjectionTests
    {
        static void Require(CommandResult result) => Assert.That(result.Success, Is.True, result.Message);
        static HotelSimulation Create()
        {
            var config = AssetDatabase.LoadAssetAtPath<SessionConfig>("Assets/_WorstHotel/ScriptableObjects/PrototypeSession.asset");
            Assert.That(config, Is.Not.Null);
            var settings = config.ToData();
            var model = new HotelSimulation(settings, settings.Rooms.Select(room => new RoomState(room)).ToArray(),
                config.living.ToData(), config.needs.ToData(), config.noise.ToData(), config.heater.ToData(),
                config.electricity.ToData(), config.housekeeping.ToData(), config.services.ToData(), config.infrastructure.ToData(), ManualBookingFixture.Operations(config));
            Require(model.StartOperations());
            return model;
        }
        static void Advance(HotelSimulation model, float target)
        { while (model.Elapsed < target) model.Tick(Math.Min(1, target - model.Elapsed)); }
        static string State(HotelSimulation model) => JsonUtility.ToJson(model.CaptureSnapshot(808, 1));

        [Test]
        public void ProductionWorkingCapitalCoversFirstOperatingBillAndOneEmergencyPatchBeforeAnyCheckout()
        {
            var model = Create();
            Assert.That(model.Economy.Cash, Is.EqualTo(750));
            Assert.That(model.PeriodOpeningCash, Is.EqualTo(750));
            Advance(model, model.NextReportAt + .25f);
            Assert.That(model.LastReport.Receipts, Is.Empty);
            Assert.That(model.LastReport.OperatingCost, Is.EqualTo(450));
            Assert.That(model.LastReport.Net, Is.EqualTo(-450));
            Assert.That(model.Economy.Cash, Is.EqualTo(300));
            model.Boiler.ForceFailure(); // Explicit fault fixture tests the opening cash reserve, not failure pacing.
            Require(model.Boiler.SetRelief(1, true));
            while (!model.Boiler.InRepairBand) model.Tick(.5f);
            Require(model.EmergencyPatchBoiler(0));
            Assert.That(model.Economy.Cash, Is.EqualTo(100));
            Assert.That(model.PeriodMaintenanceSpend, Is.EqualTo(200));
            Assert.That(model.PeriodCheckoutIncome, Is.Zero);
            Assert.That(model.PeriodOpeningCash + model.PeriodCheckoutIncome - model.PeriodMaintenanceSpend - model.PeriodCapitalSpend,
                Is.EqualTo(model.Economy.Cash));
        }

        [Test]
        public void NominalBookingRevenueChangesWithPriceAndCancellationWithoutPretendingItWasCollected()
        {
            var model = Create();
            var offers = model.BookingOffers.Where(offer => offer.ArrivalDay == 1).Take(2).ToArray();
            Require(model.AcceptBooking(0, offers[0].Id, 101, 180));
            Require(model.AcceptBooking(0, offers[1].Id, 103, 300));
            Assert.That(model.UnpaidBookedRevenue, Is.EqualTo(480));
            Assert.That(model.PeriodGross, Is.Zero);
            Assert.That(model.PeriodCompensation, Is.Zero);
            Assert.That(model.PeriodCheckoutIncome, Is.Zero);
            Assert.That(model.Economy.Cash, Is.EqualTo(750));
            var first = model.Reservations.Single(reservation => reservation.Id == offers[0].Id);
            Require(model.SetBookingPrice(1, first.Id, 250, first.Revision));
            Assert.That(model.UnpaidBookedRevenue, Is.EqualTo(550));
            var second = model.Reservations.Single(reservation => reservation.Id == offers[1].Id);
            Require(model.CancelBooking(0, second.Id, second.Revision));
            Assert.That(model.UnpaidBookedRevenue, Is.EqualTo(250));
            Assert.That(model.Economy.Cash, Is.EqualTo(750));
            string before = State(model);
            for (int i = 0; i < 10; i++)
                Assert.That(model.PeriodOpeningCash + model.PeriodCheckoutIncome - model.PeriodMaintenanceSpend - model.PeriodCapitalSpend,
                    Is.EqualTo(model.Economy.Cash));
            Assert.That(State(model), Is.EqualTo(before), "Finance inspection has no command or accounting side effects.");
        }

        [Test]
        public void ActualCheckoutMovesNominalCommitmentIntoCollectedIncomeAndMirrorProjectsIdenticalFinances()
        {
            var model = Create();
            var offer = model.BookingOffers.First(value => value.ArrivalDay == 1);
            Require(model.AcceptBooking(0, offer.Id, 101, 180));
            Advance(model, offer.ArrivalAt + .25f);
            var guest = model.Guests.Single();
            // Explicit headless travel/key adapters. Billing uses the actual dated stay and checkout.
            Require(model.SignalGuestReachedReception(guest.GuestId));
            Require(ModelKeyHandoff.CheckIn(model, 0, guest.GuestId));
            Require(model.SignalGuestReachedRoom(guest.GuestId));
            Advance(model, guest.Agent.CheckoutTime + 1);
            Assert.That(guest.ReceiptPosted, Is.True);
            Assert.That(model.UnpaidBookedRevenue, Is.Zero);
            Assert.That(model.PeriodGross, Is.EqualTo(180));
            Assert.That(model.PeriodCheckoutIncome, Is.EqualTo(model.PeriodGross - model.PeriodCompensation));
            Assert.That(model.PeriodOpeningCash + model.PeriodCheckoutIncome - model.PeriodMaintenanceSpend - model.PeriodCapitalSpend,
                Is.EqualTo(model.Economy.Cash));
            var mirror = Create(); mirror.EnableReadOnlyMirror();
            Require(mirror.ApplySnapshot(JsonUtility.FromJson<HotelModelSnapshot>(State(model))));
            Assert.That(mirror.PeriodGross, Is.EqualTo(model.PeriodGross));
            Assert.That(mirror.PeriodCompensation, Is.EqualTo(model.PeriodCompensation));
            Assert.That(mirror.PeriodCheckoutIncome, Is.EqualTo(model.PeriodCheckoutIncome));
            Assert.That(mirror.UnpaidBookedRevenue, Is.EqualTo(model.UnpaidBookedRevenue));
            Assert.That(mirror.PeriodOpeningCash, Is.EqualTo(model.PeriodOpeningCash));
            Assert.That(mirror.PeriodStartedAt, Is.EqualTo(model.PeriodStartedAt));
        }
    }
}
