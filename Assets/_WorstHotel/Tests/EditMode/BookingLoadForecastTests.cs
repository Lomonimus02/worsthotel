using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace WorstHotel.Tests
{
    /// <summary>Read-only forecast integration. Navigation, key handoff and contact anchors are explicitly headless adapters.</summary>
    public sealed class BookingLoadForecastTests
    {
        static readonly float[] Loss = { 0, 1.4f, .2f, .4f, .3f, .7f };
        sealed class Fixture
        {
            public SessionSettings Settings;
            public HotelSimulation Hotel;
            public RoomState[] Rooms;
        }

        static void Require(CommandResult result) => Assert.That(result.Success, Is.True, result.Message);
        static string State(HotelSimulation hotel) => JsonUtility.ToJson(hotel.CaptureSnapshot(92, 1));
        static Fixture Create(bool continuous = true, bool open = true, float demand = 1, GuestServiceSettings services = null)
        {
            var profiles = new[]
            {
                new GuestProfile(GuestKind.Budget, "Budget", "", 180, demand * .85f, 18, .7f, 28, 90, .55f),
                new GuestProfile(GuestKind.ColdSensitive, "Cold", "", 300, (float)Math.Min(float.MaxValue, (double)demand * 1.05f), 20, 1.35f, 16, 65, .4f),
                new GuestProfile(GuestKind.Business, "Business", "", 450, demand, 19.5f, 1, 10, 45, .25f)
            };
            var settings = new SessionSettings(profiles, Enumerable.Range(0, 6).Select(index =>
                new RoomProfile(101 + index, "Room " + (101 + index), heatLoss: Loss[index], noise: 0, temperature: 22)),
                new BoilerSettings(), new EconomySettings(startingCash: 7000));
            var rooms = settings.Rooms.Select(profile => new RoomState(profile)).ToArray();
            var hotel = new HotelSimulation(settings, rooms, new LivingHotelSettings(firstActivityDelay: 1000),
                services: services ?? new GuestServiceSettings(eligibility: 0, naturalCommunicationEnabled: true,
                    selfResponseObserveSeconds: 1000, toleranceSeconds: 1000),
                operations: continuous ? new OperationsSettings() : null);
            if (open && continuous) Require(hotel.StartOperations());
            return new Fixture { Settings = settings, Hotel = hotel, Rooms = rooms };
        }

        static ScheduledBookingOffer Offer(Fixture fixture, int day = 1, GuestKind kind = GuestKind.Budget, int ordinal = 0) =>
            fixture.Hotel.BookingOffers.Where(item => item.ArrivalDay == day && item.Application.Archetype.Kind == kind)
                .OrderBy(item => item.ArrivalAt).Skip(ordinal).First();
        static void Book(Fixture fixture, ScheduledBookingOffer offer, int room) =>
            Require(fixture.Hotel.AcceptBooking(0, offer.Id, room, fixture.Settings.Economy.MinPrice));
        static void AdvanceTo(HotelSimulation hotel, float time)
        { while (hotel.Elapsed < time) hotel.Tick(Math.Min(.5f, time - hotel.Elapsed)); }
        static GuestStay CheckIn(Fixture fixture, ScheduledBookingOffer offer, int room)
        {
            Book(fixture, offer, room); AdvanceTo(fixture.Hotel, offer.ArrivalAt + .25f);
            var guest = fixture.Hotel.Guests.Single(item => item.GuestId == offer.Id);
            Require(fixture.Hotel.SignalGuestReachedReception(guest.GuestId));
            Require(ModelKeyHandoff.CheckIn(fixture.Hotel, 0, guest.GuestId));
            Require(fixture.Hotel.SignalGuestReachedRoom(guest.GuestId));
            return guest;
        }
        static Fixture Mirror(Fixture source, Action<HotelModelSnapshot> fixtureEdit = null)
        {
            var packet = JsonUtility.FromJson<HotelModelSnapshot>(State(source.Hotel));
            fixtureEdit?.Invoke(packet);
            var mirror = Create(); mirror.Hotel.EnableReadOnlyMirror(); Require(mirror.Hotel.ApplySnapshot(packet));
            return mirror;
        }
        static float Space(Fixture fixture, int roomId, GuestProfile profile = null)
        {
            var room = fixture.Rooms.Single(item => item.Profile.Id == roomId);
            var settings = fixture.Hotel.InfrastructureSettings;
            return (profile == null ? settings.VacantRadiatorDemand : profile.HeatingDemand * fixture.Hotel.LivingSettings.QuietDemandMultiplier) *
                settings.DemandMultiplier(room.RadiatorSetting) * (1 + room.Profile.HeatLoss * settings.HeatLossDemandFactor);
        }
        static float Empty(Fixture fixture) => fixture.Rooms.Sum(room => Space(fixture, room.Profile.Id));
        static float Add(Fixture fixture, int room, GuestProfile profile) => Space(fixture, room, profile) - Space(fixture, room);
        static float Shower(Fixture fixture, GuestProfile profile) => profile.HeatingDemand *
            (fixture.Hotel.LivingSettings.ShowerDemandMultiplier - fixture.Hotel.LivingSettings.QuietDemandMultiplier);
        static void SameForecast(BookingLoadForecast a, BookingLoadForecast b)
        {
            Assert.That(b.Available, Is.EqualTo(a.Available)); Assert.That(b.OfferId, Is.EqualTo(a.OfferId));
            Assert.That(b.RoomId, Is.EqualTo(a.RoomId)); Assert.That(b.ArrivalAt, Is.EqualTo(a.ArrivalAt));
            Assert.That(b.CheckoutAt, Is.EqualTo(a.CheckoutAt)); Assert.That(b.CurrentDemand, Is.EqualTo(a.CurrentDemand));
            Assert.That(b.CurrentBand, Is.EqualTo(a.CurrentBand)); Assert.That(b.EffectiveCapacity, Is.EqualTo(a.EffectiveCapacity));
            Assert.That(b.TypicalDemand, Is.EqualTo(a.TypicalDemand)); Assert.That(b.OneShowerPeakDemand, Is.EqualTo(a.OneShowerPeakDemand));
            Assert.That(b.TypicalRatio, Is.EqualTo(a.TypicalRatio)); Assert.That(b.PeakRatio, Is.EqualTo(a.PeakRatio));
            Assert.That(b.TypicalBand, Is.EqualTo(a.TypicalBand)); Assert.That(b.CircuitId, Is.EqualTo(a.CircuitId));
            Assert.That(b.CircuitCapacity, Is.EqualTo(a.CircuitCapacity)); Assert.That(b.TypicalCircuitDemand, Is.EqualTo(a.TypicalCircuitDemand));
            Assert.That(b.CircuitReserve, Is.EqualTo(a.CircuitReserve)); Assert.That(b.MaxConcurrentGuests, Is.EqualTo(a.MaxConcurrentGuests));
        }

        [Test]
        public void FutureCandidateAddsOneQuietConsumerToActualVacantValvesAndLargestSingleShower()
        {
            var fixture = Create(); var hotel = fixture.Hotel; var offer = Offer(fixture, 2, GuestKind.Business);
            Require(hotel.SetRadiatorSetting(0, 106, 3)); // Current vacant radiator demand must remain in the forecast.
            string before = State(hotel); var forecast = hotel.ForecastBookingLoad(offer.Id, 104);
            Assert.That(forecast.Available, Is.True, forecast.Reason);
            Assert.That(forecast.ArrivalAt, Is.EqualTo(offer.ArrivalAt)); Assert.That(forecast.CheckoutAt, Is.EqualTo(offer.CheckoutAt));
            float expected = Empty(fixture) + Add(fixture, 104, offer.Application.Archetype);
            Assert.That(forecast.TypicalDemand, Is.EqualTo(expected).Within(.00001f));
            Assert.That(forecast.OneShowerPeakDemand, Is.EqualTo(expected + Shower(fixture, offer.Application.Archetype)).Within(.00001f));
            Assert.That(forecast.MaxConcurrentGuests, Is.EqualTo(1)); Assert.That(forecast.CircuitId, Is.EqualTo("B"));
            Assert.That(forecast.TypicalCircuitDemand, Is.EqualTo(hotel.ElectricitySettings.OccupiedRoomLoad));
            Assert.That(forecast.CurrentDemand, Is.EqualTo(hotel.Boiler.Load));
            Assert.That(State(hotel), Is.EqualTo(before), "Forecasting cannot create guests, stock, events, reservations or elapsed time.");
        }

        [Test]
        public void OverlappingFutureReservationsCountWithoutBodiesAndAcceptedCandidateCountsExactlyOnce()
        {
            var fixture = Create(); var hotel = fixture.Hotel;
            var booked = hotel.BookingOffers.Where(item => item.ArrivalDay == 2).Take(3).ToArray();
            for (int i = 0; i < booked.Length; i++) Book(fixture, booked[i], 101 + i);
            var offer = hotel.BookingOffers.First(item => item.ArrivalDay == 2 && !booked.Contains(item));
            var before = hotel.ForecastBookingLoad(offer.Id, 104);
            Assert.That(before.MaxConcurrentGuests, Is.EqualTo(4)); Assert.That(hotel.Guests, Is.Empty);
            float expected = Empty(fixture) + Add(fixture, 104, offer.Application.Archetype);
            for (int i = 0; i < booked.Length; i++) expected += Add(fixture, 101 + i, booked[i].Application.Archetype);
            Assert.That(before.TypicalDemand, Is.EqualTo(expected).Within(.00001f));
            float maxShower = booked.Select(item => Shower(fixture, item.Application.Archetype))
                .Append(Shower(fixture, offer.Application.Archetype)).Max();
            Assert.That(before.OneShowerPeakDemand, Is.EqualTo(expected + maxShower).Within(.00001f));
            Book(fixture, offer, 104); SameForecast(before, hotel.ForecastBookingLoad(offer.Id, 104));
            Assert.That(hotel.ForecastBookingLoad(offer.Id, 106).Available, Is.False, "A preview cannot move an accepted reservation.");
            Require(hotel.SetBookingPrice(1, offer.Id, fixture.Settings.Economy.MinPrice + fixture.Settings.Economy.PriceStep));
            SameForecast(before, hotel.ForecastBookingLoad(offer.Id, 104));
            Require(hotel.CancelBooking(1, booked[1].Id));
            var reduced = hotel.ForecastBookingLoad(offer.Id, 104);
            Assert.That(reduced.MaxConcurrentGuests, Is.EqualTo(3));
            Assert.That(reduced.TypicalDemand, Is.EqualTo(expected - Add(fixture, 102, booked[1].Application.Archetype)).Within(.00001f));
            Require(hotel.CancelBooking(0, offer.Id));
            Assert.That(hotel.ForecastBookingLoad(offer.Id, 104).Available, Is.False);
        }

        [TestCase(0)]
        [TestCase(1)]
        public void ExactCheckoutBoundaryIsHalfOpenAndSelectedRoomConflictIsReported(int overlapSeconds)
        {
            var fixture = Create(); var previous = Offer(fixture); Book(fixture, previous, 101);
            var candidate = Offer(fixture, 2);
            // Explicit validated wire-time fixture: an early next-day enquiry tests equality at checkout.
            // No clock, ownership or guest navigation is fabricated by the forecast itself.
            var mirror = Mirror(fixture, packet => packet.Operations.Offers.Single(item => item.Application.Id == candidate.Id)
                .ArrivalAt = previous.CheckoutAt - overlapSeconds);
            string before = State(mirror.Hotel);
            var forecast = mirror.Hotel.ForecastBookingLoad(candidate.Id, 101);
            Assert.That(forecast.Available, Is.EqualTo(overlapSeconds == 0), forecast.Reason);
            if (overlapSeconds == 0) Assert.That(forecast.MaxConcurrentGuests, Is.EqualTo(1));
            else Assert.That(forecast.Reason, Does.Contain("overlapping"));
            Assert.That(State(mirror.Hotel), Is.EqualTo(before));
        }

        [Test]
        public void SequentialCohortsAreSweptInsteadOfSummingEveryReservationTouchingTheCandidate()
        {
            var fixture = Create(); var earlier = Offer(fixture, 1, GuestKind.ColdSensitive);
            var later = Offer(fixture, 2, GuestKind.Budget); var candidate = Offer(fixture, 2, GuestKind.Business);
            Book(fixture, earlier, 101); Book(fixture, later, 101);
            var mirror = Mirror(fixture, packet => packet.Operations.Offers.Single(item => item.Application.Id == candidate.Id)
                .ArrivalAt = fixture.Hotel.Calendar.At(2, 8));
            var forecast = mirror.Hotel.ForecastBookingLoad(candidate.Id, 102);
            Assert.That(forecast.Available, Is.True, forecast.Reason);
            Assert.That(forecast.MaxConcurrentGuests, Is.EqualTo(2), "Three distinct stays intersect the whole window, but only two coexist.");
            float common = Empty(fixture) + Add(fixture, 102, candidate.Application.Archetype);
            float earlierSpace = common + Add(fixture, 101, earlier.Application.Archetype);
            float laterSpace = common + Add(fixture, 101, later.Application.Archetype);
            Assert.That(forecast.TypicalDemand, Is.EqualTo(Math.Max(earlierSpace, laterSpace)).Within(.00001f));
            float earlierPeak = earlierSpace + Math.Max(Shower(fixture, earlier.Application.Archetype), Shower(fixture, candidate.Application.Archetype));
            float laterPeak = laterSpace + Math.Max(Shower(fixture, later.Application.Archetype), Shower(fixture, candidate.Application.Archetype));
            Assert.That(forecast.OneShowerPeakDemand, Is.EqualTo(Math.Max(earlierPeak, laterPeak)).Within(.00001f));
            Assert.That(forecast.TypicalCircuitDemand, Is.EqualTo(2 * mirror.Hotel.ElectricitySettings.OccupiedRoomLoad));
        }

        [Test]
        public void MaterializedRoomOwnerIsNotCountedTwiceAndCommittedKeyMoveImmediatelyChangesAttribution()
        {
            var fixture = Create(); var hotel = fixture.Hotel;
            var current = Offer(fixture, 1, GuestKind.Budget); var guest = CheckIn(fixture, current, 101);
            var candidate = hotel.BookingOffers.Where(item => item.ArrivalDay == 1 && item.ArrivalAt > hotel.Elapsed).Last();
            var before = hotel.ForecastBookingLoad(candidate.Id, 102);
            Assert.That(before.MaxConcurrentGuests, Is.EqualTo(2));
            Require(hotel.RequestGuestMove(0, guest.GuestId, 106));
            var pending = hotel.ForecastBookingLoad(candidate.Id, 102);
            SameForecast(before, pending);
            Assert.That(hotel.ForecastBookingLoad(candidate.Id, 106).Available, Is.False, "A promised destination remains unavailable during the key exchange.");
            Require(ModelKeyHandoff.MoveGuest(hotel, 0, guest.GuestId, 106));
            var after = hotel.ForecastBookingLoad(candidate.Id, 102); // Deliberately before the next tick or room-arrival adapter.
            Assert.That(hotel.FindReservation(guest.GuestId).RoomId, Is.EqualTo(106));
            Assert.That(after.MaxConcurrentGuests, Is.EqualTo(2));
            Assert.That(after.TypicalDemand - before.TypicalDemand,
                Is.EqualTo(Add(fixture, 106, current.Application.Archetype) - Add(fixture, 101, current.Application.Archetype)).Within(.00001f));
            Assert.That(before.TypicalCircuitDemand - after.TypicalCircuitDemand, Is.EqualTo(hotel.ElectricitySettings.OccupiedRoomLoad).Within(.00001f));
        }

        [Test]
        public void CurrentAgreedLateCheckoutExtendsOverlapBeyondTheOriginalOffer()
        {
            // Explicit longer extension isolates the calendar overlap; normal service commands still authorize it.
            var fixture = Create(services: new GuestServiceSettings(eligibility: 0, naturalCommunicationEnabled: true,
                lateCheckoutExtension: 180, lateCheckoutRequestLead: 240, selfResponseObserveSeconds: 1000, toleranceSeconds: 1000));
            var hotel = fixture.Hotel; var current = Offer(fixture, 1, GuestKind.Business);
            var guest = CheckIn(fixture, current, 101); var candidate = Offer(fixture, 2);
            AdvanceTo(hotel, guest.Agent.Schedule.WakeTime + .25f);
            var before = hotel.ForecastBookingLoad(candidate.Id, 104); Assert.That(before.MaxConcurrentGuests, Is.EqualTo(1));
            Require(hotel.DebugForceService(guest.GuestId, ServiceKind.LateCheckout));
            var request = hotel.Services.Cases.Single(item => item.GuestId == guest.GuestId && item.Kind == ServiceKind.LateCheckout);
            Require(hotel.DebugBeginGuestContact(guest.GuestId, GuestContactChannel.Phone));
            Require(hotel.SignalGuestResponseAnchorReached(guest.GuestId, guest.Agent.ResponseActionId,
                guest.Agent.ResponseActionVersion, GuestResponseAnchor.RoomPhone));
            Require(hotel.AnswerIncomingServiceCall(0, request.Response.Id));
            Require(hotel.RespondToService(0, request.Id, true));
            Assert.That(guest.Agent.CheckoutTime, Is.GreaterThan(candidate.ArrivalAt));
            Assert.That(hotel.FindReservation(guest.GuestId).Offer.CheckoutAt, Is.LessThan(candidate.ArrivalAt));
            var after = hotel.ForecastBookingLoad(candidate.Id, 104);
            Assert.That(after.MaxConcurrentGuests, Is.EqualTo(2));
            Assert.That(after.TypicalDemand - before.TypicalDemand, Is.EqualTo(Add(fixture, 101, current.Application.Archetype)).Within(.00001f));
            Assert.That(hotel.ForecastBookingLoad(candidate.Id, 101).Available, Is.False);
        }

        [Test]
        public void ClosedValvesRemoveOnlySpaceHeatingAndCannotHideTheSingleShowerPeak()
        {
            var fixture = Create(); var hotel = fixture.Hotel; var candidate = Offer(fixture, 2, GuestKind.ColdSensitive);
            var before = hotel.ForecastBookingLoad(candidate.Id, 102);
            Require(hotel.SetRadiatorSetting(0, 102, 0)); Require(hotel.SetRadiatorSetting(0, 106, 0));
            var after = hotel.ForecastBookingLoad(candidate.Id, 102);
            Assert.That(after.TypicalDemand, Is.LessThan(before.TypicalDemand));
            Assert.That(after.OneShowerPeakDemand - after.TypicalDemand, Is.EqualTo(Shower(fixture, candidate.Application.Archetype)).Within(.00001f));
            Assert.That(after.MaxConcurrentGuests, Is.EqualTo(1));
            Assert.That(after.TypicalCircuitDemand, Is.EqualTo(before.TypicalCircuitDemand));
        }

        [Test]
        public void LargestShowerCanBelongToAClosedRadiatorAndNeverUsesDeveloperLoadAsAFutureConsumer()
        {
            var fixture = Create(); var hotel = fixture.Hotel;
            var cold = Offer(fixture, 2, GuestKind.ColdSensitive); var budget = Offer(fixture, 2);
            var candidate = Offer(fixture, 2, GuestKind.Business);
            Book(fixture, cold, 105); Book(fixture, budget, 101);
            Require(hotel.SetRadiatorSetting(0, 105, 0)); Require(hotel.SetRadiatorSetting(0, 101, 3));
            var forecast = hotel.ForecastBookingLoad(candidate.Id, 102);
            Assert.That(forecast.OneShowerPeakDemand - forecast.TypicalDemand,
                Is.EqualTo(Shower(fixture, cold.Application.Archetype)).Within(.00001f));
            hotel.Boiler.OverrideLoad(50); // Labelled current diagnostic gauge override, never projected as a reservation.
            var overridden = hotel.ForecastBookingLoad(candidate.Id, 102);
            Assert.That(overridden.CurrentDemand, Is.EqualTo(50));
            Assert.That(overridden.TypicalDemand, Is.EqualTo(forecast.TypicalDemand));
            Assert.That(overridden.OneShowerPeakDemand, Is.EqualTo(forecast.OneShowerPeakDemand));
            Assert.That(overridden.MaxConcurrentGuests, Is.EqualTo(3));
        }

        [Test]
        public void BranchForecastUsesCurrentAssignedOnHeatersEvenWhenTrippedAndExcludesUnassignedOrOffBodies()
        {
            var fixture = Create(); var hotel = fixture.Hotel; var candidate = Offer(fixture, 2);
            foreach (string id in new[] { "on-A", "on-B", "off-B", "unassigned" }) Require(hotel.Heaters.Register(id));
            Require(hotel.Heaters.AssignRoom("on-A", 101)); Require(hotel.Heaters.SetSwitchedOn("on-A", true));
            Require(hotel.Heaters.AssignRoom("on-B", 104)); Require(hotel.Heaters.SetSwitchedOn("on-B", true));
            Require(hotel.Heaters.AssignRoom("off-B", 106)); Require(hotel.Heaters.SetSwitchedOn("unassigned", true));
            hotel.RefreshElectrical();
            var forecast = hotel.ForecastBookingLoad(candidate.Id, 104);
            Assert.That(forecast.TypicalCircuitDemand, Is.EqualTo(hotel.ElectricitySettings.OccupiedRoomLoad + hotel.Heaters.Settings.ElectricalLoad));
            Require(hotel.DebugTripCircuit("B"));
            Assert.That(hotel.Heaters.Find("on-B").EffectiveHeatOutput, Is.Zero);
            SameForecast(forecast, hotel.ForecastBookingLoad(candidate.Id, 104));
            Require(hotel.Heaters.SetSwitchedOn("on-B", false));
            Assert.That(hotel.ForecastBookingLoad(candidate.Id, 104).TypicalCircuitDemand, Is.EqualTo(hotel.ElectricitySettings.OccupiedRoomLoad));
        }

        [Test]
        public void CurrentConditionAndPurchasedCapacityChangeHeadroomWithoutChangingForecastConsumers()
        {
            var fixture = Create(); var hotel = fixture.Hotel; var candidate = Offer(fixture, 2);
            var original = hotel.ForecastBookingLoad(candidate.Id, 104);
            hotel.Boiler.SetCondition(20); // Explicit initial equipment condition, not a wear/failure claim.
            var worn = hotel.ForecastBookingLoad(candidate.Id, 104);
            Assert.That(worn.EffectiveCapacity, Is.LessThan(original.EffectiveCapacity));
            Assert.That(worn.TypicalRatio, Is.GreaterThan(original.TypicalRatio));
            Require(hotel.PurchaseBoilerUpgrade(0)); Require(hotel.PurchaseElectricalUpgrade(1, "B"));
            var upgraded = hotel.ForecastBookingLoad(candidate.Id, 104);
            Assert.That(upgraded.TypicalDemand, Is.EqualTo(original.TypicalDemand));
            Assert.That(upgraded.OneShowerPeakDemand, Is.EqualTo(original.OneShowerPeakDemand));
            Assert.That(upgraded.TypicalCircuitDemand, Is.EqualTo(original.TypicalCircuitDemand));
            Assert.That(upgraded.TypicalRatio, Is.LessThan(worn.TypicalRatio));
            Assert.That(upgraded.CircuitReserve - worn.CircuitReserve, Is.EqualTo(hotel.ElectricitySettings.CapacityUpgradeAmount).Within(.00001f));
            hotel.Boiler.ForceFailure();
            var failed = hotel.ForecastBookingLoad(candidate.Id, 104);
            Assert.That(failed.CurrentBand, Is.EqualTo(CapacityBand.Critical));
            Assert.That(failed.TypicalBand, Is.EqualTo(upgraded.TypicalBand), "A demand estimate is not a prediction that the current failure will repeat or be repaired.");
        }

        [Test]
        public void HostAndMirrorDeriveTheSameFreshForecastWithoutChangingEitherSnapshot()
        {
            var fixture = Create(); var hotel = fixture.Hotel; var candidate = Offer(fixture, 2, GuestKind.Business);
            Book(fixture, Offer(fixture, 2), 101); Require(hotel.SetRadiatorSetting(0, 106, 3));
            Require(hotel.PurchaseBoilerUpgrade(0)); Require(hotel.PurchaseElectricalUpgrade(1, "B"));
            var mirror = Mirror(fixture); string hostBefore = State(hotel), mirrorBefore = State(mirror.Hotel);
            SameForecast(hotel.ForecastBookingLoad(candidate.Id, 106), mirror.Hotel.ForecastBookingLoad(candidate.Id, 106));
            Assert.That(mirror.Hotel.ForecastBookingLoad("missing", 106).Available, Is.False);
            Assert.That(State(hotel), Is.EqualTo(hostBefore)); Assert.That(State(mirror.Hotel), Is.EqualTo(mirrorBefore));
            Assert.That(mirror.Hotel.IsReadOnlyMirror, Is.True);
        }

        [TestCase(null, 101)]
        [TestCase("", 101)]
        [TestCase("missing", 101)]
        [TestCase("stay-1-1", 999)]
        public void InvalidSelectionsReturnAnExplicitReasonAndNeverMutateState(string id, int room)
        {
            var fixture = Create(); string before = State(fixture.Hotel);
            var forecast = fixture.Hotel.ForecastBookingLoad(id, room);
            Assert.That(forecast.Available, Is.False); Assert.That(forecast.Reason, Is.Not.Null.And.Not.Empty);
            Assert.That(State(fixture.Hotel), Is.EqualTo(before));
        }

        [Test]
        public void ClosedLegacyCancelledAndArrivedEnquiriesAreExplicitlyUnavailable()
        {
            foreach (var fixture in new[] { Create(open: false), Create(continuous: false) })
                Assert.That(fixture.Hotel.ForecastBookingLoad("stay-1-1", 101).Available, Is.False);
            var active = Create(); var first = Offer(active);
            Book(active, first, 101); Require(active.Hotel.CancelBooking(0, first.Id));
            Assert.That(active.Hotel.ForecastBookingLoad(first.Id, 101).Available, Is.False);
            var next = Offer(active, ordinal: 1); Book(active, next, 102);
            AdvanceTo(active.Hotel, next.ArrivalAt);
            Assert.That(active.Hotel.ForecastBookingLoad(next.Id, 102).Available, Is.False);
            Assert.That(active.Hotel.ForecastBookingLoad(first.Id, 101).Available, Is.False);
        }

        [Test]
        public void ExtremeButFiniteProfileAndHeaterDemandsProduceFiniteSaturatedForecasts()
        {
            var fixture = Create(demand: float.MaxValue); var hotel = fixture.Hotel;
            var candidates = hotel.BookingOffers.Where(item => item.ArrivalDay == 2).Take(3).ToArray();
            Book(fixture, candidates[0], 101); Book(fixture, candidates[1], 102);
            foreach (string id in new[] { "extreme-1", "extreme-2" })
            {
                Require(hotel.Heaters.Register(id, new HeaterSettings(electricalLoad: float.MaxValue)));
                Require(hotel.Heaters.AssignRoom(id, 103)); Require(hotel.Heaters.SetSwitchedOn(id, true));
            }
            // No simulation step or future body is created: only the pure estimator reads these finite inputs.
            var forecast = hotel.ForecastBookingLoad(candidates[2].Id, 103);
            Assert.That(forecast.Available, Is.True, forecast.Reason);
            foreach (float value in new[] { forecast.TypicalDemand, forecast.OneShowerPeakDemand, forecast.TypicalRatio,
                forecast.PeakRatio, forecast.TypicalCircuitDemand, forecast.CircuitReserve })
                Assert.That(float.IsNaN(value) || float.IsInfinity(value), Is.False);
            Assert.That(forecast.TypicalDemand, Is.EqualTo(float.MaxValue));
            Assert.That(forecast.OneShowerPeakDemand, Is.EqualTo(float.MaxValue));
            Assert.That(forecast.TypicalCircuitDemand, Is.EqualTo(float.MaxValue));
            Assert.That(forecast.TypicalBand, Is.EqualTo(CapacityBand.Overloaded));
        }
    }
}
