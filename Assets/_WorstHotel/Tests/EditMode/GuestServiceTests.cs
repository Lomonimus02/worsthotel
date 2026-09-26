using System;
using System.Linq;
using NUnit.Framework;

namespace WorstHotel.Tests
{
    /// <summary>Model acceptance with explicit physical route/key adapters; no time-based random request injection.</summary>
    public sealed class GuestServiceTests
    {
        sealed class Fixture
        {
            public HotelSimulation Hotel;
            public RoomState[] Rooms;
            public GuestStay Guest => Hotel.Guests.Single(item => item.GuestId == "service-guest");
            public RoomState Room => Rooms.Single(item => item.Profile.Id == Guest.RoomId);
        }

        static GuestServiceSettings Settings(int budget = 3, float eligibility = 1, int blankets = 3) => new GuestServiceSettings(
            maxCasesPerShift: budget, blanketStock: blankets, eligibility: eligibility, soloFrequencyMultiplier: 1,
            observationSeconds: 2, wakeLeadSeconds: 35, wakeToleranceSeconds: 5, wakeMissSeconds: 20);

        static Fixture Create(GuestKind kind = GuestKind.ColdSensitive, GuestServiceSettings services = null, bool waiting = false)
        {
            var profiles = new[]
            {
                new GuestProfile(GuestKind.Budget, "Budget", "", 180, .85f, 18, .7f, 28, 90, .55f),
                new GuestProfile(GuestKind.ColdSensitive, "Cold", "", 300, 1.05f, 20, 1.35f, 16, 65, .4f,
                    needs: new NeedProfile(21, 25, 18, 28, .125f, .25f, 65)),
                new GuestProfile(GuestKind.Business, "Business", "", 450, 1, 19.5f, 1, 10, 45, .25f,
                    needs: new NeedProfile(21, 25, 18, 28, .125f, .25f, 65))
            };
            var settings = new SessionSettings(profiles, Enumerable.Range(101, 6).Select(id =>
                new RoomProfile(id, "Room " + id, noise: 0, temperature: 22.5f)), new BoilerSettings(), new EconomySettings(), serviceSeconds: 300);
            var rooms = settings.Rooms.Select(room => new RoomState(room)).ToArray();
            var hotel = new HotelSimulation(settings, rooms,
                new LivingHotelSettings(firstArrivalSeconds: .2f, arrivalJitterSeconds: 0, firstActivityDelay: 1000,
                    activityDurationMin: 120, activityDurationMax: 120),
                new NeedSettings(buildupPerSecond: .1f, complaintExposureSeconds: 20, escalatedExposureSeconds: 40,
                    criticalExposureSeconds: 60, recoverySeconds: 2), services: services ?? Settings());
            var profile = profiles.Single(item => item.Kind == kind);
            var offer = new BookingApplication("service-guest", "Service guest", profile, profile.ReferencePrice);
            Assert.That(hotel.StartShift(new[] { new BookingAssignment(102, offer.Id, profile.ReferencePrice, 0) }, new[] { offer }).Success, Is.True);
            hotel.Tick(.4f);
            Assert.That(hotel.SignalGuestReachedReception(offer.Id).Success, Is.True);
            if (waiting) rooms.Single(room => room.Profile.Id == 102).Cleanliness = Cleanliness.Dirty;
            else
            {
                Assert.That(ModelKeyHandoff.CheckIn(hotel, 0, offer.Id).Success, Is.True);
                Assert.That(hotel.SignalGuestReachedRoom(offer.Id).Success, Is.True);
            }
            return new Fixture { Hotel = hotel, Rooms = rooms };
        }

        static void Advance(Fixture f, float seconds, float temperature = 22.5f)
        {
            while (seconds > .0001f)
            {
                foreach (var room in f.Rooms) room.Temperature = temperature;
                float dt = Math.Min(.2f, seconds); f.Hotel.Tick(dt); seconds -= dt;
            }
        }
        static ServiceCase Case(Fixture f, ServiceKind kind) => f.Hotel.Services.Cases.Single(item => item.GuestId == f.Guest.GuestId && item.Kind == kind);

        [Test]
        public void MildActualColdCreatesOneSparseBlanketCaseBeforeAnySeriousComplaint()
        {
            var f = Create(); Advance(f, 4, 19.5f);
            var request = Case(f, ServiceKind.ExtraBlanket);
            Assert.That(request.Status, Is.EqualTo(ServiceStatus.Requested));
            Assert.That(request.SourceEntityId, Is.EqualTo("room/102/temperature"));
            Assert.That(f.Hotel.Incidents.Items.Any(item => item.HasContactedStaff), Is.False);
            Advance(f, 8, 19.5f);
            Assert.That(f.Hotel.Services.Cases.Single(), Is.SameAs(request));
            Assert.That(f.Guest.Memory.ServicesRequested, Is.EqualTo(1));
        }

        [Test]
        public void PhysicalBlanketChangesPersonalComfortWithoutChangingThermometerOrPowerAndHasLimitedEffect()
        {
            var f = Create(services: Settings(blankets: 1)); Advance(f, 3, 19.5f);
            float temperature = f.Room.Temperature;
            float load = f.Hotel.Electrical.Find(f.Room.CircuitId).RequestedLoad;
            Assert.That(f.Hotel.DeliverBlanket(0, f.Guest.GuestId).Success, Is.False);
            Assert.That(f.Hotel.TakeServiceItem(0, "blanket:0").Success, Is.True);
            Assert.That(f.Hotel.TakeServiceItem(1, "blanket:0").Success, Is.False);
            Assert.That(f.Hotel.DeliverBlanket(1, f.Guest.GuestId).Success, Is.False);
            Assert.That(f.Hotel.DeliverBlanket(0, f.Guest.GuestId).Success, Is.True);
            Assert.That(f.Room.Temperature, Is.EqualTo(temperature));
            Assert.That(f.Hotel.Electrical.Find(f.Room.CircuitId).RequestedLoad, Is.EqualTo(load));
            Assert.That(f.Hotel.Services.BlanketsAvailable, Is.Zero);
            Assert.That(f.Hotel.Services.FindItem("blanket:0").GuestId, Is.EqualTo(f.Guest.GuestId));
            Advance(f, .2f, 19.5f);
            Assert.That(f.Guest.Perception.PerceivedTemperature, Is.GreaterThan(f.Room.Temperature));
            Assert.That(f.Guest.Needs.Temperature.Severity, Is.Zero);
            Assert.That(Case(f, ServiceKind.ExtraBlanket).Status, Is.EqualTo(ServiceStatus.Fulfilled));
            Advance(f, 24, 5);
            Assert.That(f.Guest.Needs.Temperature.Severity, Is.GreaterThan(f.Hotel.Services.Settings.MildColdMaximum));
            Assert.That(f.Hotel.Incidents.Items.Any(item => item.Reason == IncidentReason.Temperature && item.HasContactedStaff), Is.True);
        }

        [Test]
        public void SevereColdCreatesARealTemperatureComplaintWithoutOfferingAnInsufficientBlanketAsAChore()
        {
            var f = Create(); Advance(f, 23, 5);
            Assert.That(f.Hotel.Services.Cases.Any(item => item.Kind == ServiceKind.ExtraBlanket), Is.False);
            Assert.That(f.Hotel.Incidents.Items.Single(item => item.Reason == IncidentReason.Temperature).HasContactedStaff, Is.True);
        }

        [Test]
        public void ZeroBudgetAllowsAGuestToCompleteQuietLifeWithoutAnyGuaranteedServiceRequest()
        {
            var f = Create(services: Settings(budget: 0)); Advance(f, 50, 19.5f);
            Assert.That(f.Hotel.Services.Cases, Is.Empty);
            Assert.That(f.Guest.Memory.ServicesRequested, Is.Zero);
        }

        [Test]
        public void ADeclinedServiceDoesNotReappearOrRemoveItsUnderlyingCold()
        {
            var f = Create(); Advance(f, 3, 19.5f);
            var request = Case(f, ServiceKind.ExtraBlanket);
            Assert.That(f.Hotel.RespondToService(0, request.Id, false).Success, Is.True);
            Assert.That(f.Guest.BlanketComfortBonus, Is.Zero);
            Advance(f, 12, 19.5f);
            Assert.That(f.Hotel.Services.Cases.Count(item => item.Kind == ServiceKind.ExtraBlanket), Is.EqualTo(1));
            Assert.That(f.Guest.Memory.ServicesDeclined, Is.EqualTo(1));
            Assert.That(f.Guest.ServiceSatisfactionAdjustment, Is.EqualTo(-f.Hotel.Services.Settings.DeclinedPenalty));
        }

        [Test]
        public void WakeUpPromiseUsesHotelTimeAcceptsOnlyItsCallWindowAndCompletesExactlyOnce()
        {
            var f = Create(GuestKind.Business, Settings(eligibility: 0));
            Assert.That(f.Hotel.DebugForceService(f.Guest.GuestId, ServiceKind.WakeUpCall).Success, Is.True);
            var request = Case(f, ServiceKind.WakeUpCall);
            Assert.That(request.DueTime, Is.EqualTo(f.Guest.Agent.CheckoutTime - f.Hotel.Services.Settings.WakeLeadSeconds));
            Assert.That(f.Hotel.RespondToService(0, request.Id, true).Success, Is.True);
            var promise = f.Hotel.Services.Promises.Single();
            Assert.That(promise.DueTime, Is.EqualTo(request.DueTime));
            Assert.That(f.Hotel.CompleteWakeUpCall(0, promise.Id).Success, Is.False);
            Advance(f, promise.DueTime - f.Hotel.Services.Settings.WakeToleranceSeconds + .2f - f.Hotel.Elapsed);
            Assert.That(f.Hotel.CompleteWakeUpCall(0, promise.Id).Success, Is.True);
            Assert.That(f.Hotel.CompleteWakeUpCall(1, promise.Id).Success, Is.False);
            Assert.That(promise.Status, Is.EqualTo(PromiseStatus.Completed));
            Assert.That(f.Guest.Memory.PromisesKept, Is.EqualTo(1));
            Assert.That(f.Guest.Memory.ServicesFulfilled, Is.EqualTo(1));
            Assert.That(f.Guest.ServiceSatisfactionAdjustment, Is.EqualTo(f.Hotel.Services.Settings.FulfilledBonus));
        }

        [Test]
        public void ForgottenWakeUpHasOneModestMemoryAndReviewConsequenceBeforeSettlement()
        {
            var f = Create(GuestKind.Business, Settings(eligibility: 0));
            Assert.That(f.Hotel.DebugForceService(f.Guest.GuestId, ServiceKind.WakeUpCall).Success, Is.True);
            var request = Case(f, ServiceKind.WakeUpCall);
            Assert.That(f.Hotel.RespondToService(0, request.Id, true).Success, Is.True);
            Advance(f, request.DueTime + f.Hotel.Services.Settings.WakeMissSeconds + 1 - f.Hotel.Elapsed);
            Assert.That(f.Hotel.Services.Promises.Single().Status, Is.EqualTo(PromiseStatus.Missed));
            Assert.That(f.Guest.Memory.PromisesBroken, Is.EqualTo(1));
            Assert.That(f.Guest.ServiceSatisfactionAdjustment, Is.EqualTo(-f.Hotel.Services.Settings.BrokenPromisePenalty));
            var report = f.Hotel.EndShift();
            Assert.That(report.Receipts.Single().Review, Does.Contain("promised a wake-up call, but nobody called"));
            Assert.That(f.Guest.Memory.PromisesBroken, Is.EqualTo(1));
        }

        [Test]
        public void AcceptedPromiseRemainsInProgressAfterDueTimeAndALateCallStillCompletesTheSameCase()
        {
            var f = Create(GuestKind.Business, Settings(eligibility: 0));
            Assert.That(f.Hotel.DebugForceService(f.Guest.GuestId, ServiceKind.WakeUpCall).Success, Is.True);
            var request = Case(f, ServiceKind.WakeUpCall);
            Assert.That(f.Hotel.RespondToService(0, request.Id, true).Success, Is.True);
            Advance(f, request.DueTime + f.Hotel.Services.Settings.WakeToleranceSeconds + 1 - f.Hotel.Elapsed);
            Assert.That(request.Status, Is.EqualTo(ServiceStatus.InProgress));
            Assert.That(f.Hotel.Services.Promises.Single().Status, Is.EqualTo(PromiseStatus.Accepted));
            Assert.That(f.Hotel.CompleteWakeUpCall(0, request.Id).Success, Is.True);
            Assert.That(request.Status, Is.EqualTo(ServiceStatus.Fulfilled));
            Assert.That(f.Guest.Memory.ServicesFulfilled, Is.EqualTo(1));
            Assert.That(f.Guest.Memory.PromisesKept, Is.EqualTo(1));
            Assert.That(f.Guest.Memory.PromisesBroken, Is.Zero);
            Assert.That(f.Guest.ServiceSatisfactionAdjustment, Is.EqualTo(-f.Hotel.Services.Settings.DeclinedPenalty));
        }

        [Test]
        public void GuestsOutsideCannotRequestRoomBlanketsAndStaffCannotDeliverToAnEmptyReservedRoom()
        {
            var f = Create();
            Assert.That(f.Hotel.ForceLeaveRoom(f.Guest.GuestId).Success, Is.True);
            Assert.That(f.Hotel.SignalGuestLeftRoom(f.Guest.GuestId).Success, Is.True);
            Advance(f, 10, 19.5f);
            Assert.That(f.Hotel.Services.Cases, Is.Empty);
            Assert.That(f.Hotel.TakeServiceItem(0, "blanket:0").Success, Is.True);
            Assert.That(f.Hotel.DeliverBlanket(0, f.Guest.GuestId).Success, Is.False);
            Assert.That(f.Hotel.ForceReturnRoom(f.Guest.GuestId).Success, Is.True);
            Assert.That(f.Hotel.SignalGuestReturnedRoom(f.Guest.GuestId).Success, Is.True);
            Advance(f, 3, 19.5f);
            Assert.That(Case(f, ServiceKind.ExtraBlanket), Is.Not.Null);
        }

        [Test]
        public void AcceptedLateCheckoutRetainsActualRoomOwnershipAndDefersTurnover()
        {
            var f = Create(GuestKind.Business, Settings(eligibility: 0));
            float original = f.Guest.Agent.CheckoutTime;
            Assert.That(f.Hotel.DebugForceService(f.Guest.GuestId, ServiceKind.LateCheckout).Success, Is.True);
            var request = Case(f, ServiceKind.LateCheckout);
            Assert.That(f.Hotel.RespondToService(0, request.Id, true).Success, Is.True);
            Assert.That(f.Guest.Agent.CheckoutTime, Is.GreaterThan(original));
            Advance(f, original + .4f - f.Hotel.Elapsed);
            Assert.That(f.Room.GuestId, Is.EqualTo(f.Guest.GuestId));
            Assert.That(f.Hotel.Housekeeping.Find(102), Is.Null);
            Advance(f, f.Guest.Agent.CheckoutTime + .4f - f.Hotel.Elapsed);
            Assert.That(f.Room.Cleanliness, Is.EqualTo(Cleanliness.Dirty));
            Assert.That(f.Hotel.Housekeeping.Find(102), Is.Not.Null);
        }

        [Test]
        public void TheSameEarlyNextDayBookingWaitsWhenLateCheckoutRemovedTheAvailablePreparationWindow()
        {
            var regular = Create(GuestKind.Business, Settings(eligibility: 0));
            var late = Create(GuestKind.Business, Settings(eligibility: 0));
            float preparationWindow = regular.Guest.Agent.CheckoutTime + 4;
            Assert.That(late.Hotel.DebugForceService(late.Guest.GuestId, ServiceKind.LateCheckout).Success, Is.True);
            Assert.That(late.Hotel.RespondToService(0, Case(late, ServiceKind.LateCheckout).Id, true).Success, Is.True);
            Advance(regular, preparationWindow - regular.Hotel.Elapsed);
            Advance(late, preparationWindow - late.Hotel.Elapsed);
            // Explicit room-exit and manual-linen boundary adapters. This tests availability,
            // not navigation speed: staff have the same scheduled opportunity to prepare the bed.
            Assert.That(regular.Hotel.SignalGuestVacatedRoom(regular.Guest.GuestId, 102).Success, Is.True);
            ManualTurnoverModelAdapter.PrepareRoom(regular.Hotel, 102);
            Assert.That(late.Room.Occupied, Is.True);
            Assert.That(late.Hotel.SignalGuestVacatedRoom(late.Guest.GuestId, 102).Success, Is.False);
            Assert.That(late.Hotel.PickUpLinen(0, "dirty:102").Success, Is.False);
            foreach (var f in new[] { regular, late })
            {
                Advance(f, f.Hotel.Remaining);
                f.Hotel.EndShift();
                if (f.Room.DepartingGuestId != null)
                    Assert.That(f.Hotel.SignalGuestVacatedRoom(f.Guest.GuestId, 102).Success, Is.True);
                Assert.That(f.Hotel.ApplyMaintenance(0, MaintenanceChoice.Defer).Success, Is.True);
                var next = new BookingApplication("next-day-service-guest", "Next guest", f.Guest.Application.Archetype, f.Guest.Price);
                Assert.That(f.Hotel.StartShift(new[] { new BookingAssignment(102, next.Id, next.ReferencePrice, 0) }, new[] { next }).Success, Is.True);
                f.Hotel.Tick(.4f);
                Assert.That(f.Hotel.SignalGuestReachedReception(next.Id).Success, Is.True);
                ModelKeyHandoff.TakeKey(f.Hotel, 0, 102);
            }
            string nextGuest = regular.Hotel.Guests.Single().GuestId;
            Assert.That(regular.Hotel.CheckIn(0, nextGuest).Success, Is.True);
            Assert.That(late.Hotel.CheckIn(0, nextGuest).Success, Is.False);
            Assert.That(late.Rooms.Single(room => room.Profile.Id == 102).Cleanliness, Is.EqualTo(Cleanliness.Dirty));
            Assert.That(late.Hotel.Guests.Single().Agent.State, Is.EqualTo(GuestAgentState.WaitingForCheckIn));
        }

        [Test]
        public void DayRefillPreservesPreparedCarriedAndDroppedSuppliesAndStartShiftCannotRefillTwice()
        {
            var f = Create(services: Settings(eligibility: 0));
            Assert.That(f.Hotel.TakeServiceItem(0, "blanket:0").Success, Is.True);
            var held = f.Hotel.Services.FindItem("blanket:0"); int generation = held.Generation;
            Assert.That(f.Hotel.TakeServiceItem(1, "bulb:0").Success, Is.True);
            Assert.That(f.Hotel.DropServiceItem(1, "bulb:0").Success, Is.True);
            Assert.That(f.Hotel.TakeServiceItem(1, "blanket:1").Success, Is.True);
            Assert.That(f.Hotel.DeliverBlanket(1, f.Guest.GuestId).Success, Is.True);
            f.Hotel.EndShift();
            Assert.That(f.Hotel.ApplyMaintenance(0, MaintenanceChoice.Defer).Success, Is.True);
            Assert.That(f.Hotel.Services.LastRefillDay, Is.EqualTo(2));
            Assert.That(held.Location, Is.EqualTo(ServiceItemLocation.HeldByPlayer));
            Assert.That(held.PlayerId, Is.EqualTo(0));
            Assert.That(held.Generation, Is.EqualTo(generation));
            Assert.That(f.Hotel.Services.FindItem("bulb:0").Location, Is.EqualTo(ServiceItemLocation.Dropped));
            Assert.That(f.Hotel.Services.FindItem("blanket:1").Location, Is.EqualTo(ServiceItemLocation.OnShelf));
            Assert.That(f.Hotel.DebugSetBlanketStock(1).Success, Is.True, "Proactive shelf setup is available in planning.");
            int available = f.Hotel.Services.BlanketsAvailable;
            var next = new BookingApplication("stock-next-day", "Next guest", f.Guest.Application.Archetype, f.Guest.Price);
            Assert.That(f.Hotel.StartShift(new[] { new BookingAssignment(101, next.Id, next.ReferencePrice, 0) }, new[] { next }).Success, Is.True);
            Assert.That(f.Hotel.Services.FindItem("blanket:0"), Is.SameAs(held));
            Assert.That(held.PlayerId, Is.EqualTo(0));
            Assert.That(f.Hotel.Services.BlanketsAvailable, Is.EqualTo(available));
            Assert.That(f.Hotel.TakeServiceItem(2, "blanket:1").Success, Is.False);
        }

        [Test]
        public void LuggageRequiresAnUnreadyRoomAgreementAndActualCarriedSuitcase()
        {
            var f = Create(services: Settings(eligibility: 0), waiting: true);
            Assert.That(f.Hotel.DebugForceService(f.Guest.GuestId, ServiceKind.LuggageStorage).Success, Is.True);
            Advance(f, 3);
            var request = Case(f, ServiceKind.LuggageStorage);
            string luggage = "luggage:" + f.Guest.GuestId;
            Assert.That(f.Hotel.TakeServiceItem(0, luggage).Success, Is.False);
            Assert.That(f.Hotel.RespondToService(0, request.Id, true).Success, Is.True);
            Assert.That(f.Hotel.StoreLuggage(0, f.Guest.GuestId).Success, Is.False);
            Assert.That(f.Hotel.TakeServiceItem(0, luggage).Success, Is.True);
            Assert.That(f.Hotel.StoreLuggage(1, f.Guest.GuestId).Success, Is.False);
            Assert.That(f.Hotel.StoreLuggage(0, f.Guest.GuestId).Success, Is.True);
            Assert.That(f.Hotel.Services.FindItem(luggage).Location, Is.EqualTo(ServiceItemLocation.Stored));
            Assert.That(request.Status, Is.EqualTo(ServiceStatus.Fulfilled));
            Assert.That(f.Guest.Memory.LuggageStored, Is.EqualTo(1));
            Assert.That(f.Room.Cleanliness, Is.EqualTo(Cleanliness.Dirty), "Storing luggage does not prepare the room.");
        }

        [Test]
        public void SoftNoiseRequiresAnActualSourceAndEscalatesTheSameServiceCaseToTheExistingComplaint()
        {
            var f = Create(services: Settings(eligibility: 0));
            f.Hotel.SetRoomNoise(102, 1); Advance(f, 3);
            Assert.That(f.Hotel.DebugForceService(f.Guest.GuestId, ServiceKind.AskNeighborsQuiet).Success, Is.False);
            var source = ModelNoiseSource.AddTelevision(f.Hotel, 104);
            Advance(f, 3);
            Assert.That(f.Hotel.DebugForceService(f.Guest.GuestId, ServiceKind.AskNeighborsQuiet).Success, Is.True);
            var request = Case(f, ServiceKind.AskNeighborsQuiet);
            Assert.That(request.SourceEntityId, Is.EqualTo(source.GuestId + "/Television"));
            Advance(f, 22);
            Assert.That(request.Status, Is.EqualTo(ServiceStatus.Escalated));
            Assert.That(f.Hotel.Services.Cases.Single(), Is.SameAs(request));
            Assert.That(f.Hotel.Incidents.Items.Any(item => item.GuestId == f.Guest.GuestId && item.Reason == IncidentReason.Noise && item.HasContactedStaff), Is.True);
        }

        [Test]
        public void RepairingTheActualLampConsumesOnlyARealCarriedBulbAndClearsItsConcreteCause()
        {
            var f = Create(services: Settings(eligibility: 0));
            Assert.That(f.Hotel.BreakRoomLamp(102).Success, Is.True);
            Advance(f, 1);
            Assert.That(f.Guest.Perception.ConditionCauses.Any(cause => cause.SourceEntityId == "room/102/lamp"), Is.True);
            Assert.That(f.Hotel.ReplaceRoomBulb(0, 102).Success, Is.False);
            Assert.That(f.Hotel.TakeServiceItem(0, "bulb:0").Success, Is.True);
            Assert.That(f.Hotel.ReplaceRoomBulb(0, 102).Success, Is.True);
            Assert.That(f.Room.LampBroken, Is.False);
            Assert.That(f.Hotel.Services.FindItem("bulb:0").Location, Is.EqualTo(ServiceItemLocation.Delivered));
            Advance(f, .2f);
            Assert.That(f.Guest.Perception.ConditionCauses.Any(cause => cause.SourceEntityId == "room/102/lamp"), Is.False);
        }
    }
}
