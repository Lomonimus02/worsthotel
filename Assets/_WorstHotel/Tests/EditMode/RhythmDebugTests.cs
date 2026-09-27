using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace WorstHotel.Tests
{
    public sealed class RhythmDebugTests
    {
        static SessionConfig Config => AssetDatabase.LoadAssetAtPath<SessionConfig>("Assets/_WorstHotel/ScriptableObjects/PrototypeSession.asset");
        static void Require(CommandResult value) => Assert.That(value.Success, Is.True, value.Message);
        static string State(HotelSimulation model) => JsonUtility.ToJson(model.CaptureSnapshot(841, 1));

        static HotelSimulation Create(SalesSettings sales = null)
        {
            var config = Config; var settings = config.ToData(); var calendar = config.OperationsData();
            var living = UnityEngine.Object.Instantiate(config.living);
            var services = UnityEngine.Object.Instantiate(config.services);
            try
            {
                // Decision-only fixture: quiet activities and explicitly answered real contact.
                // Severity/complaint/grace/economy/thermal production settings stay unchanged.
                living.firstActivityDelay = living.quietDurationMin = living.quietDurationMax = 10000;
                living.awayDurationMin = living.awayDurationMax = 10000;
                services.selfResponseObserveSeconds = 10000; services.eligibility = 0;
                var operations = new OperationsSettings(calendar.SecondsPerDay, calendar.StartHour, calendar.ReportHour,
                    calendar.ArrivalStartHour, calendar.ArrivalEndHour, calendar.SleepHour, calendar.CheckoutHour,
                    calendar.ReportHistoryLimit, sales ?? new SalesSettings());
                var hotel = new HotelSimulation(settings, settings.Rooms.Select(room => new RoomState(room)).ToArray(),
                    living.ToData(), config.needs.ToData(), config.noise.ToData(), config.heater.ToData(),
                    config.electricity.ToData(), config.housekeeping.ToData(), services.ToData(),
                    config.infrastructure.ToData(), operations);
                Require(hotel.StartOperations()); return hotel;
            }
            finally { UnityEngine.Object.DestroyImmediate(living); UnityEngine.Object.DestroyImmediate(services); }
        }

        static GuestStay AddGuest(HotelSimulation hotel, int? price = null)
        {
            Require(hotel.DebugSpawnGuest(GuestKind.ColdSensitive, 102));
            if (price.HasValue) Require(hotel.SetBookingPrice(0, hotel.Reservations.Single().Id, price.Value));
            hotel.Tick(1.25f);
            var guest = hotel.Guests.Single();
            Require(hotel.SignalGuestReachedReception(guest.GuestId));
            Require(ModelKeyHandoff.CheckIn(hotel, 0, guest.GuestId));
            Require(hotel.SignalGuestReachedRoom(guest.GuestId));
            hotel.Tick(.25f); return guest;
        }

        static void Advance(HotelSimulation model, float target)
        { while (model.Elapsed < target) model.Tick(Math.Min(.25f, target - model.Elapsed)); }

        static void HoldCold(HotelSimulation model, float seconds)
        {
            float until = model.Elapsed + seconds;
            while (model.Elapsed < until)
            {
                Require(model.SetRoomTemperature(102, 18)); // Labelled current severe physical cause.
                model.Tick(Math.Min(.25f, until - model.Elapsed));
            }
        }

        static void Disclose(HotelSimulation model, GuestStay guest)
        {
            Require(model.DebugBeginGuestContact(guest.GuestId, GuestContactChannel.Phone));
            var response = model.Services.FindResponse(guest.Agent.ResponseActionId);
            Require(model.SignalGuestResponseAnchorReached(guest.GuestId, response.Id,
                guest.Agent.ResponseActionVersion, GuestResponseAnchor.RoomPhone));
            Require(model.AnswerIncomingServiceCall(0, response.Id));
            Require(model.AcceptConsequences(0, guest.GuestId));
            Assert.That(model.Incidents.Items.Any(item => item.GuestId == guest.GuestId && item.Active && item.HasContactedStaff), Is.True);
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(-.001f)]
        [TestCase(1.001f)]
        public void InvalidDebugStressRejectsBeforeAnyModelMutation(float value)
        {
            var model = Create(); string before = State(model);
            Assert.That(model.DebugSetBoilerStress(value).Success, Is.False);
            Assert.That(State(model), Is.EqualTo(before));
        }

        [Test]
        public void DebugStressDoesNotAdvancePressureRepairOrForceFailureAndNextRealTickStillGoverns()
        {
            var model = Create(); var boiler = model.Boiler;
            int failures = 0, recoveries = 0;
            boiler.OnFailureStarted += () => failures++; boiler.OnFailureResolved += () => recoveries++;
            float now = model.Elapsed, condition = boiler.Condition, pressure = boiler.Pressure, output = boiler.HeatingOutput;
            Require(model.DebugSetBoilerStress(1));
            Assert.That(boiler.Stress01, Is.EqualTo(1));
            Assert.That(boiler.Failed, Is.False); Assert.That(failures, Is.Zero);
            Assert.That(model.Elapsed, Is.EqualTo(now)); Assert.That(boiler.Condition, Is.EqualTo(condition));
            Assert.That(boiler.Pressure, Is.EqualTo(pressure)); Assert.That(boiler.HeatingOutput, Is.EqualTo(output));
            model.Tick(.25f);
            Assert.That(boiler.Stress01, Is.LessThan(1));
            Assert.That(boiler.Failed, Is.False, "Actual under-capacity demand recovers seeded stress through the normal tick.");
            // Labelled diagnostic demand; only the later ordinary capacity tick may fail.
            boiler.OverrideLoad(boiler.EffectiveCapacity * 2);
            Require(model.DebugSetBoilerStress(1));
            Assert.That(boiler.Failed, Is.False); Assert.That(failures, Is.Zero);
            model.Tick(.25f); Assert.That(boiler.Failed, Is.True); Assert.That(failures, Is.EqualTo(1));
            condition = boiler.Condition; pressure = boiler.Pressure;
            Require(model.DebugSetBoilerStress(0));
            Assert.That(boiler.Failed, Is.True, "Zeroing a diagnostic value cannot repair the existing outage.");
            Assert.That(recoveries, Is.Zero); Assert.That(boiler.Condition, Is.EqualTo(condition));
            Assert.That(boiler.Pressure, Is.EqualTo(pressure));
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(-.001f)]
        [TestCase(100.001f)]
        public void InvalidSatisfactionTargetRejectsAtomically(float target)
        {
            var model = Create(); var guest = AddGuest(model); string before = State(model);
            Assert.That(model.DebugSetGuestSatisfaction(guest.GuestId, target).Success, Is.False);
            Assert.That(State(model), Is.EqualTo(before));
        }

        [Test]
        public void SatisfactionHistoryChangesOnlyQualityIntegralAndOrdinaryScoringContinues()
        {
            var model = Create(); var guest = AddGuest(model);
            var expected = model.CaptureSnapshot(841, 1);
            Require(model.DebugSetGuestSatisfaction(guest.GuestId, 20));
            Assert.That(model.Satisfaction.Evaluate(guest), Is.EqualTo(20).Within(.001f));
            Assert.That(guest.QualityIntegral, Is.GreaterThan(0));
            expected.Guests.Single().QualityIntegral = guest.QualityIntegral;
            Assert.That(State(model), Is.EqualTo(JsonUtility.ToJson(expected)),
                "No cause, exposure, memory, price, service penalty, receipt or clock is reset by history editing.");
            Require(model.SetRoomTemperature(guest.RoomId, 22.5f)); model.Tick(.25f);
            Assert.That(model.Satisfaction.Evaluate(guest), Is.GreaterThan(20), "The target is not a persistent override.");
            Assert.That(guest.EarlyCheckout.State, Is.Not.EqualTo(EarlyCheckoutState.Committed));
        }

        [Test]
        public void SatisfactionHistoryCannotErasePremiumPenaltyOrAlterUnknownAndEndedStays()
        {
            var model = Create(); var guest = AddGuest(model, 600);
            string before = State(model);
            Assert.That(model.DebugSetGuestSatisfaction(guest.GuestId, 100).Success, Is.False,
                "Even zero room-quality deficit cannot erase the actual premium-price penalty.");
            Assert.That(model.DebugSetGuestSatisfaction("unknown", 0).Success, Is.False);
            Assert.That(State(model), Is.EqualTo(before));
            Require(model.DebugSetGuestSatisfaction(guest.GuestId, 0));
            Assert.That(model.Satisfaction.Evaluate(guest), Is.EqualTo(0).Within(.001f));
            Require(model.DebugCheckoutGuest(guest.GuestId)); model.Tick(.25f);
            before = State(model);
            Assert.That(model.DebugSetGuestSatisfaction(guest.GuestId, 50).Success, Is.False);
            Assert.That(State(model), Is.EqualTo(before));
        }

        [Test]
        public void ForcedDemandWaitsForOneRealDueCursorAndKeepsPolicyPriceAndUnpaidReservation()
        {
            var model = Create(new SalesSettings(enabled: true, initiallyOpenRooms: 1, initialPrice: 360, baseDemand: 0));
            int cash = model.Economy.Cash, revision = model.EventRevision;
            float now = model.Elapsed, due = model.NextSalesDecisionAt;
            Require(model.DebugForceNextBookingDemand()); Require(model.DebugForceNextBookingDemand());
            Assert.That(model.DebugNextBookingDemandPending, Is.True);
            Assert.That(model.Elapsed, Is.EqualTo(now)); Assert.That(model.EventRevision, Is.EqualTo(revision));
            Assert.That(model.Reservations, Is.Empty);
            Advance(model, due - .1f); Assert.That(model.Reservations, Is.Empty);
            Advance(model, due);
            var booked = model.Reservations.Single();
            Assert.That(booked.Id, Is.EqualTo("stay-1-1")); Assert.That(booked.RoomId, Is.EqualTo(101));
            Assert.That(booked.Price, Is.EqualTo(360)); Assert.That(booked.IsAutomatic, Is.True);
            Assert.That(booked.Status, Is.EqualTo(ReservationStatus.Reserved));
            Assert.That(model.Guests, Is.Empty); Assert.That(model.Economy.Cash, Is.EqualTo(cash));
            Assert.That(model.DebugNextBookingDemandPending, Is.False);
            var secondRoom = model.RoomSalesPolicies.Single(row => row.RoomId == 102);
            Require(model.SetRoomSalesPolicy(0, 102, true, 360, secondRoom.Revision));
            Advance(model, model.SalesDecisionAt(1, 2));
            Assert.That(model.Reservations.Count, Is.EqualTo(1), "Repeated button use arms one enquiry, never a queue or a persistent demand mode.");
            Assert.That(model.SalesDecisionCursors.Single(row => row.ArrivalDay == 1).NextOfferIndex, Is.EqualTo(3));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ForcedEnquiryIsConsumedEvenWhenNoOpenAvailableRoomExists(bool occupiedInterval)
        {
            var model = Create(new SalesSettings(enabled: true, initiallyOpenRooms: occupiedInterval ? 1 : 0, baseDemand: 0));
            if (occupiedInterval)
            {
                Require(model.DebugForceNextBookingDemand()); Advance(model, model.NextSalesDecisionAt);
                Assert.That(model.Reservations.Count, Is.EqualTo(1));
            }
            Require(model.DebugForceNextBookingDemand()); Advance(model, model.NextSalesDecisionAt);
            Assert.That(model.DebugNextBookingDemandPending, Is.False);
            int count = model.Reservations.Count;
            var available = model.RoomSalesPolicies.Single(row => row.RoomId == 102);
            Require(model.SetRoomSalesPolicy(0, 102, true, 180, available.Revision));
            Advance(model, model.NextSalesDecisionAt);
            Assert.That(model.Reservations.Count, Is.EqualTo(count), "A failed forced enquiry cannot be banked for later room availability.");
        }

        [Test]
        public void DemandOverrideClearsExplicitlyAndDoesNotSurviveANewHotel()
        {
            var settings = new SalesSettings(enabled: true, baseDemand: 0);
            var model = Create(settings);
            Require(model.DebugForceNextBookingDemand()); Require(model.DebugClearNextBookingDemand());
            Advance(model, model.NextSalesDecisionAt);
            Assert.That(model.Reservations, Is.Empty);
            Require(model.DebugForceNextBookingDemand());
            Assert.That(Create(settings).DebugNextBookingDemandPending, Is.False);
            var manual = Create(); string before = State(manual);
            Assert.That(manual.DebugForceNextBookingDemand().Success, Is.False);
            Assert.That(State(manual), Is.EqualTo(before));
        }

        [Test]
        public void EarlyPrimeNeedsCurrentSevereOwnedDisclosedCauseRatherThanLowSatisfactionOrPrivateDistress()
        {
            var model = Create(); var guest = AddGuest(model);
            Require(model.DebugSetGuestSatisfaction(guest.GuestId, 0));
            string before = State(model);
            Assert.That(model.DebugPrimeEarlyCheckoutEligibility(guest.GuestId).Success, Is.False);
            Assert.That(State(model), Is.EqualTo(before));
            HoldCold(model, 60);
            before = State(model);
            Assert.That(model.DebugPrimeEarlyCheckoutEligibility(guest.GuestId).Success, Is.False, "Private severe exposure is not invented staff contact.");
            Assert.That(State(model), Is.EqualTo(before));
            Disclose(model, guest);
            Require(model.SetRoomTemperature(guest.RoomId, 22.5f));
            before = State(model);
            Assert.That(model.DebugPrimeEarlyCheckoutEligibility(guest.GuestId).Success, Is.False,
                "Historical escalation cannot substitute for the current room measurement.");
            Assert.That(State(model), Is.EqualTo(before));
        }

        [Test]
        public void EarlyPrimePreservesNormalWarningGraceThenOnePhysicalCheckoutAndOneReceipt()
        {
            var model = Create(); var guest = AddGuest(model); HoldCold(model, 60); Disclose(model, guest);
            int cash = model.Economy.Cash, events = model.EventRevision;
            float contract = guest.Agent.CheckoutTime, clock = model.Elapsed;
            Require(model.DebugPrimeEarlyCheckoutEligibility(guest.GuestId));
            Assert.That(guest.EarlyCheckout.State, Is.EqualTo(EarlyCheckoutState.Monitoring));
            Assert.That(guest.EarlyCheckout.WarningAt, Is.EqualTo(-1));
            Assert.That(model.Elapsed, Is.EqualTo(clock)); Assert.That(model.EventRevision, Is.EqualTo(events));
            Assert.That(guest.ReceiptPosted, Is.False); Assert.That(model.Economy.Cash, Is.EqualTo(cash));
            HoldCold(model, .25f);
            Assert.That(guest.EarlyCheckout.State, Is.EqualTo(EarlyCheckoutState.Warning));
            Assert.That(model.IsEarlyCheckoutWarningKnown(guest), Is.True);
            float warning = guest.EarlyCheckout.WarningAt;
            Assert.That(guest.EarlyCheckout.GraceRemainingSeconds,
                Is.EqualTo(model.Operations.SecondsPerDay * model.NeedsSettings.EarlyCheckout.GraceHours / 24));
            Require(model.DebugPrimeEarlyCheckoutEligibility(guest.GuestId));
            Assert.That(guest.EarlyCheckout.State, Is.EqualTo(EarlyCheckoutState.Warning));
            Assert.That(guest.EarlyCheckout.WarningAt, Is.EqualTo(warning));
            Assert.That(guest.ReceiptPosted, Is.False);
            HoldCold(model, .25f);
            Assert.That(guest.EarlyCheckout.State, Is.EqualTo(EarlyCheckoutState.Committed));
            Assert.That(guest.Agent.State, Is.EqualTo(GuestAgentState.CheckingOut), "Existing visible body still follows its physical exit route.");
            Assert.That(guest.Agent.CheckoutTime, Is.EqualTo(contract));
            Assert.That(guest.ReceiptPosted, Is.True);
            var paid = model.CaptureSnapshot(841, 2).Operations.PeriodReceipts.Single();
            Assert.That(paid.EarlyCheckout, Is.True); Assert.That(paid.GuestId, Is.EqualTo(guest.GuestId));
            Assert.That(paid.CheckoutAt, Is.EqualTo(guest.EarlyCheckout.CommittedAt));
            int finalCash = model.Economy.Cash;
            Assert.That(model.DebugPrimeEarlyCheckoutEligibility(guest.GuestId).Success, Is.False);
            model.Tick(.25f);
            Assert.That(model.CaptureSnapshot(841, 3).Operations.PeriodReceipts.Length, Is.EqualTo(1));
            Assert.That(model.Economy.Cash, Is.EqualTo(finalCash));
        }

        [Test]
        public void RealRecoveryAfterPrimingWarningStillPreventsCheckoutOnNextTick()
        {
            var model = Create(); var guest = AddGuest(model); HoldCold(model, 60); Disclose(model, guest);
            Require(model.DebugPrimeEarlyCheckoutEligibility(guest.GuestId)); HoldCold(model, .25f);
            Require(model.DebugPrimeEarlyCheckoutEligibility(guest.GuestId));
            Require(model.SetRoomTemperature(guest.RoomId, 22.5f)); model.Tick(.25f);
            Assert.That(guest.EarlyCheckout.State, Is.Not.EqualTo(EarlyCheckoutState.Committed));
            Assert.That(guest.ReceiptPosted, Is.False); Assert.That(guest.Agent.InAssignedRoom, Is.True);
        }

        [Test]
        public void ReadOnlyReplicaRejectsEveryDebugMutationWithoutChangingModelOrPendingEnquiry()
        {
            var sales = new SalesSettings(enabled: true, baseDemand: 0);
            var host = Create(sales); var guest = AddGuest(host);
            var mirror = Create(sales); mirror.EnableReadOnlyMirror();
            Require(mirror.ApplySnapshot(host.CaptureSnapshot(841, 1)));
            string before = State(mirror);
            Assert.That(mirror.DebugSetBoilerStress(1).Success, Is.False);
            Assert.That(mirror.DebugSetGuestSatisfaction(guest.GuestId, 0).Success, Is.False);
            Assert.That(mirror.DebugForceNextBookingDemand().Success, Is.False);
            Assert.That(mirror.DebugClearNextBookingDemand().Success, Is.False);
            Assert.That(mirror.DebugPrimeEarlyCheckoutEligibility(guest.GuestId).Success, Is.False);
            Assert.That(mirror.DebugNextBookingDemandPending, Is.False);
            Assert.That(State(mirror), Is.EqualTo(before));
        }
    }
}
