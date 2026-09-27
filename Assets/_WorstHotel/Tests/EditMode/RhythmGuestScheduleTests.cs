using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace WorstHotel.Tests
{
    public sealed class RhythmGuestScheduleTests
    {
        static GuestProfile Profile(GuestKind kind) => new GuestProfile(kind, kind.ToString(), "", 180, 1,
            18, 1, 10, 90, .8f, needs: new NeedProfile(0, 40, -10, 50, .9f, 1, 90));
        static BookingApplication Application(string id, GuestKind kind = GuestKind.Business) =>
            new BookingApplication(id, id, Profile(kind), 180);
        static void Require(CommandResult value) => Assert.That(value.Success, Is.True, value.Message);
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        [Test]
        public void DatedPreferencesArePureStableStaggeredAndPersonalitySpecific()
        {
            var schedules = new GuestScheduleSystem(new LivingHotelSettings(rhythm: new GuestRhythmSettings(enabled: true)));
            var clock = new HotelGameClock(); var calendar = new HotelCalendar(clock, new OperationsSettings());
            var business = Enumerable.Range(0, 24).Select(index => Application("business-" + index)).ToArray();
            var forward = business.Select(app => schedules.DatedTimingFor(app, 1, calendar.At(1, 14), calendar)).ToArray();
            var backward = business.Reverse().Select(app => schedules.DatedTimingFor(app, 1, calendar.At(1, 14), calendar)).Reverse().ToArray();
            Assert.That(backward, Is.EqualTo(forward));
            Assert.That(forward.Max(item => item.WakeAt) - forward.Min(item => item.WakeAt), Is.GreaterThan(10));
            Assert.That(forward.All(item => item.WakeAt >= calendar.At(2, 6.5f) && item.WakeAt <= calendar.At(2, 7.5f)), Is.True);
            var ordinary = business.Select(app => schedules.DatedTimingFor(Application(app.Id, GuestKind.Budget), 1,
                calendar.At(1, 14), calendar)).ToArray();
            for (int i = 0; i < business.Length; i++)
            {
                Assert.That(ordinary[i].WakeAt - forward[i].WakeAt, Is.EqualTo(30).Within(.001f));
                Assert.That(ordinary[i].SleepAt - forward[i].SleepAt, Is.EqualTo(15).Within(.001f));
            }
            Assert.That(clock.SimulationTime, Is.Zero);
            Assert.That(schedules.Items, Is.Empty, "A forecast/validator timing read cannot attach or mutate a guest.");
        }

        [TestCase(240, 9f, 10f, .5f)]
        [TestCase(720, 21.5f, 22f, 0f)]
        [TestCase(1200, 14f, 23.5f, 6f)]
        public void CustomCalendarsKeepStrictlyOrderedFiniteDates(float seconds, float arrival, float sleep, float checkout)
        {
            var operations = new OperationsSettings(secondsPerDay: seconds, arrivalStartHour: arrival,
                arrivalEndHour: arrival + .25f, sleepHour: sleep, checkoutHour: checkout);
            var calendar = new HotelCalendar(new HotelGameClock(), operations);
            var schedules = new GuestScheduleSystem(new LivingHotelSettings(rhythm: new GuestRhythmSettings(enabled: true)));
            for (int day = 1; day <= 4; day++)
            {
                var timing = schedules.DatedTimingFor(Application("short-" + day), day, calendar.At(day, arrival), calendar);
                Assert.That(timing.OutingReturnAt, Is.GreaterThan(calendar.At(day, arrival)).And.LessThan(timing.SleepAt));
                Assert.That(timing.SleepAt, Is.LessThan(timing.WakeAt));
                Assert.That(timing.WakeAt, Is.LessThan(calendar.At(day + 1, checkout)));
                Assert.That(Finite(timing.SleepAt) && Finite(timing.WakeAt) && Finite(timing.OutingReturnAt), Is.True);
            }
        }

        [Test]
        public void LegacyTimingAndAttachmentRemainExactWithoutTheDatedOptIn()
        {
            var system = new GuestScheduleSystem(new LivingHotelSettings());
            var calendar = new HotelCalendar(new HotelGameClock(), new OperationsSettings());
            var app = Application("legacy"); var timing = system.DatedTimingFor(app, 1, 180, calendar);
            Assert.That(timing.SleepAt, Is.EqualTo(450)); Assert.That(timing.WakeAt, Is.EqualTo(720));
            Assert.That(timing.OutingReturnAt, Is.EqualTo(-1));
            var guest = new GuestStay(app, 101, 180);
            system.AttachStay(guest, 1, 180, 450, 780, 720);
            Assert.That(guest.Agent.Schedule.HasDailyRhythm, Is.False);
            Assert.That(guest.Agent.Schedule.MorningActivityIndex, Is.EqualTo(-1));
        }

        sealed class Fixture
        {
            public HotelSimulation Hotel;
            public GuestStay Guest;
            public ScheduledBookingOffer Offer;
        }

        static AgentSnapshot AgentState(Fixture f) => f.Hotel.CaptureSnapshot(6041, 1).Guests.Single(item =>
            item.Application.Id == f.Guest.GuestId).Agent;

        static void ExtendDeadlineFixture(Fixture f, float checkout)
        {
            // Explicit deadline-only adapter: late-checkout conversation/eligibility is tested
            // separately. This case isolates a still-accepted wake promise from deadline changes.
            var method = typeof(HotelSimulation).GetMethod("ApplyServiceLateCheckout", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            Require((CommandResult)method.Invoke(f.Hotel, new object[] { f.Guest, checkout }));
        }

        static Fixture Create(bool outing = false, bool checkIn = true)
        {
            // A narrow state-machine fixture: broad comfort and no incidental requests,
            // ordinary dated clock/durations, prescribed one-stay roster. Production cohort
            // physics/economy is covered independently by RhythmDailyDemandTests.
            var profiles = new[] { Profile(GuestKind.Budget), Profile(GuestKind.ColdSensitive), Profile(GuestKind.Business) };
            var settings = new SessionSettings(profiles, Enumerable.Range(101, 6).Select(id => new RoomProfile(id, "Room " + id)),
                new BoilerSettings(), new EconomySettings());
            var living = new LivingHotelSettings(rhythm: new GuestRhythmSettings(enabled: true,
                businessOutingProbability: outing ? 1 : 0, budgetOutingProbability: outing ? 1 : 0,
                coldSensitiveOutingProbability: outing ? 1 : 0));
            var hotel = new HotelSimulation(settings, settings.Rooms.Select(room => new RoomState(room)).ToArray(), living,
                services: new GuestServiceSettings(eligibility: 0), operations: new OperationsSettings());
            Require(hotel.StartOperations());
            var offer = hotel.BookingOffers.First(item => item.ArrivalDay == 1 && item.Application.Archetype.Kind == GuestKind.Business);
            Require(hotel.AcceptBooking(0, offer.Id, 101, 180));
            var f = new Fixture { Hotel = hotel, Offer = offer };
            AdvanceTo(f, offer.ArrivalAt + .25f, false);
            f.Guest = hotel.Guests.Single();
            Require(hotel.SignalGuestReachedReception(f.Guest.GuestId));
            if (checkIn) CheckIn(f);
            return f;
        }

        static void CheckIn(Fixture f)
        {
            // Explicit headless key/room/anchor adapter, never a scene-navigation claim.
            Require(ModelKeyHandoff.CheckIn(f.Hotel, 0, f.Guest.GuestId));
            Require(f.Hotel.SignalGuestReachedRoom(f.Guest.GuestId));
            Require(f.Hotel.RegisterGuestPhysicalStaging(f.Guest.GuestId));
            Stage(f);
        }

        static void Stage(Fixture f)
        {
            var agent = f.Guest?.Agent;
            if (agent != null && agent.InAssignedRoom && !agent.ActivityStaged && agent.ResponseActionId == null)
                Require(f.Hotel.SignalGuestActivityReady(f.Guest.GuestId, agent.State, agent.Activity));
        }

        static void AdvanceTo(Fixture f, float target, bool stage = true)
        {
            while (target - f.Hotel.Elapsed > .0001f)
            {
                f.Hotel.Tick(Math.Min(.25f, target - f.Hotel.Elapsed));
                if (stage) Stage(f);
            }
        }

        [Test]
        public void MorningShowerIsOneActualStagedActionAndLateCheckoutCannotLoopTheItinerary()
        {
            var f = Create(); var agent = f.Guest.Agent;
            AdvanceTo(f, agent.Schedule.SleepTime + 1);
            Assert.That(agent.State, Is.EqualTo(GuestAgentState.Sleeping));
            Assert.That(agent.NextPlannedActivity, Is.EqualTo(GuestActivity.Shower));
            AdvanceTo(f, agent.Schedule.WakeTime + .25f, false);
            Assert.That(agent.Activity, Is.EqualTo(GuestActivity.Shower));
            Assert.That(agent.ActivityStaged, Is.False);
            Assert.That(AgentState(f).ActivityIndex, Is.EqualTo(agent.Schedule.MorningActivityIndex + 1));
            Assert.That(f.Hotel.HeatingDemands.Single(item => item.RoomId == 101).HotWater, Is.Zero);
            Stage(f);
            Assert.That(f.Hotel.HeatingDemands.Single(item => item.RoomId == 101).HotWater, Is.GreaterThan(0));
            float showerEnd = agent.NextActivityTime;
            ExtendDeadlineFixture(f, agent.CheckoutTime + 120);
            AdvanceTo(f, showerEnd + .25f);
            Assert.That(agent.Activity, Is.Not.EqualTo(GuestActivity.Shower));
            while (f.Hotel.Elapsed < agent.CheckoutTime - 1)
            {
                AdvanceTo(f, Math.Min(agent.CheckoutTime - 1, f.Hotel.Elapsed + 1));
                Assert.That(agent.Activity, Is.Not.EqualTo(GuestActivity.Shower).And.Not.EqualTo(GuestActivity.Unpack).And.Not.EqualTo(GuestActivity.LeaveHotel));
            }
            Assert.That(AgentState(f).ActivityIndex, Is.LessThan(agent.Schedule.Activities.Count));
            AdvanceTo(f, agent.CheckoutTime + .25f);
            Assert.That(f.Guest.ReceiptPosted, Is.True);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void PromisedWakeUsesImmutableStaggeredWakeAndCannotStartTwoMorningShowers(bool earlyCall)
        {
            var f = Create(); var hotel = f.Hotel; var agent = f.Guest.Agent;
            // Explicit request fixture isolates successful call timing from optional request eligibility.
            Require(hotel.DebugForceService(f.Guest.GuestId, ServiceKind.WakeUpCall));
            var request = hotel.Services.Cases.Single();
            Require(hotel.RespondToService(0, request.Id, true));
            var promise = hotel.Services.Promises.Single();
            Assert.That(promise.DueTime, Is.EqualTo(agent.Schedule.WakeTime));
            ExtendDeadlineFixture(f, agent.CheckoutTime + 30);
            Assert.That(promise.DueTime, Is.EqualTo(agent.Schedule.WakeTime));
            AdvanceTo(f, promise.DueTime + (earlyCall ? -2 : 2));
            Assert.That(agent.State == GuestAgentState.Sleeping, Is.EqualTo(earlyCall));
            float beforeStarted = agent.StateChangedAt;
            Require(hotel.CompleteWakeUpCall(0, promise.Id));
            Assert.That(agent.Activity, Is.EqualTo(GuestActivity.Shower));
            Assert.That(AgentState(f).ActivityIndex, Is.EqualTo(agent.Schedule.MorningActivityIndex + 1));
            if (!earlyCall) Assert.That(agent.StateChangedAt, Is.EqualTo(beforeStarted), "A late answer must not restart an already-running shower.");
            Stage(f); float endsAt = agent.NextActivityTime;
            Assert.That(hotel.CompleteWakeUpCall(1, promise.Id).Success, Is.False);
            AdvanceTo(f, endsAt + 1);
            Assert.That(agent.Activity, Is.Not.EqualTo(GuestActivity.Shower));
            Assert.That(f.Guest.Memory.PromisesKept, Is.EqualTo(1));
        }

        [Test]
        public void NaturalOutingNeedsExitAndReturnAcknowledgementsAndKeepsOnlyActualRoomDemand()
        {
            var f = Create(outing: true); var hotel = f.Hotel; var agent = f.Guest.Agent;
            while (agent.State != GuestAgentState.LeavingRoom && hotel.Elapsed < agent.Schedule.OutingReturnAt)
                AdvanceTo(f, hotel.Elapsed + .25f);
            Assert.That(agent.State, Is.EqualTo(GuestAgentState.LeavingRoom));
            Assert.That(agent.CurrentLocation, Is.EqualTo(GuestLocation.Travelling));
            Require(hotel.SignalGuestLeftRoom(f.Guest.GuestId)); // labelled exterior-arrival adapter
            Assert.That(AgentState(f).AwayReturnTime, Is.EqualTo(agent.Schedule.OutingReturnAt));
            AdvanceTo(f, AgentState(f).AwayReturnTime - .5f);
            var demand = hotel.HeatingDemands.Single(row => row.RoomId == 101);
            Assert.That(agent.State, Is.EqualTo(GuestAgentState.GuestAway));
            Assert.That(demand.SpaceHeating, Is.GreaterThan(0)); Assert.That(demand.HotWater, Is.Zero);
            Assert.That(f.Guest.Perception.InAssignedRoom, Is.False);
            AdvanceTo(f, AgentState(f).AwayReturnTime + .25f);
            Assert.That(agent.State, Is.EqualTo(GuestAgentState.ReturningToRoom));
            Require(hotel.SignalGuestReturnedRoom(f.Guest.GuestId)); // labelled full return-route adapter
            Stage(f);
            Assert.That(agent.InAssignedRoom, Is.True);
            Assert.That(hotel.Keys.Find(101).GuestId, Is.EqualTo(f.Guest.GuestId));
        }

        [Test]
        public void LateRealCheckInSkipsExpiredOutingWithoutChangingTheContractOrMorning()
        {
            var f = Create(outing: true, checkIn: false); var agent = f.Guest.Agent;
            float checkout = agent.CheckoutTime;
            AdvanceTo(f, agent.Schedule.OutingReturnAt + 1);
            CheckIn(f);
            while (f.Hotel.Elapsed < agent.Schedule.SleepTime + 1)
            {
                AdvanceTo(f, Math.Min(agent.Schedule.SleepTime + 1, f.Hotel.Elapsed + .5f));
                Assert.That(agent.State, Is.Not.EqualTo(GuestAgentState.LeavingRoom).And.Not.EqualTo(GuestAgentState.GuestAway));
            }
            Assert.That(agent.CheckoutTime, Is.EqualTo(checkout));
            Assert.That(agent.State, Is.EqualTo(GuestAgentState.Sleeping));
            AdvanceTo(f, agent.Schedule.WakeTime + .5f);
            Assert.That(agent.Activity, Is.EqualTo(GuestActivity.Shower));
        }
    }
}
