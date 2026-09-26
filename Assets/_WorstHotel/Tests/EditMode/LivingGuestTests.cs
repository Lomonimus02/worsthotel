using System;
using System.Linq;
using NUnit.Framework;

namespace WorstHotel.Tests
{
    /// <summary>Production living mode with explicit physical-travel adapter commands; no Unity clock or navigation dependency.</summary>
    public sealed class LivingGuestTests
    {
        private SessionSettings settings;
        private LivingHotelSettings living;
        private RoomState[] rooms;
        private HotelSimulation simulation;

        [SetUp]
        public void Setup()
        {
            var profiles = new[]
            {
                new GuestProfile(GuestKind.Budget, "Budget", "", 180, 0.85f, 18, 0.7f, 28, 90, 0.55f),
                new GuestProfile(GuestKind.ColdSensitive, "Cold-sensitive", "", 300, 1.05f, 20, 1.35f, 16, 65, 0.4f),
                new GuestProfile(GuestKind.Business, "Business", "", 450, 1, 19.5f, 1, 10, 45, 0.25f)
            };
            settings = new SessionSettings(profiles,
                Enumerable.Range(101, 6).Select(id => new RoomProfile(id, "Room " + id)),
                new BoilerSettings(), new EconomySettings());
            living = new LivingHotelSettings();
            simulation = Create(living, out rooms);
        }

        private HotelSimulation Create(LivingHotelSettings config, out RoomState[] states)
        {
            states = settings.Rooms.Select(profile => new RoomState(profile)).ToArray();
            return new HotelSimulation(settings, states, config);
        }

        private void Start(HotelSimulation target, int day = 1, int count = 4, bool reverse = false)
        {
            var offers = GuestSystem.GenerateApplications(day, settings.GuestArchetypes);
            var assignments = offers.Take(count).Select((offer, index) => new BookingAssignment(101 + index, offer.Id,
                (int)Math.Round(offer.ReferencePrice / 10.0, MidpointRounding.AwayFromZero) * 10, index % 2)).ToArray();
            Assert.That(target.StartShift(reverse ? assignments.Reverse() : assignments, offers).Success, Is.True);
            Assert.That(target.LivingEnabled, Is.True, "These tests must exercise the production living model.");
        }

        private static void AdvanceTo(HotelSimulation target, float time)
        {
            for (int i = 0; i < 2000 && target.Elapsed < time && !target.IsServiceComplete; i++)
                target.Tick(Math.Min(0.2f, time - target.Elapsed));
            Assert.That(target.Elapsed, Is.EqualTo(time).Within(0.002f), "The fixture did not reach its requested simulation time.");
        }

        private static void ReceiveAndCheckIn(HotelSimulation target, GuestStay guest, int actor = 0)
        {
            AdvanceTo(target, Math.Max(target.Elapsed, guest.Agent.ArrivalTime + 0.2f));
            Assert.That(target.SignalGuestReachedReception(guest.GuestId).Success, Is.True);
            Assert.That(ModelKeyHandoff.CheckIn(target, actor, guest.GuestId).Success, Is.True);
            Assert.That(target.SignalGuestReachedRoom(guest.GuestId).Success, Is.True);
        }

        [Test]
        public void SameSeedAndGuestIdentityProduceStableStaggeredArrivalsRegardlessOfBookingEnumeration()
        {
            var replay = Create(new LivingHotelSettings(seed: living.Seed), out _);
            var differentSeed = Create(new LivingHotelSettings(seed: living.Seed + 997), out _);
            Start(simulation);
            Start(replay, reverse: true);
            Start(differentSeed);
            var arrivals = simulation.Guests.OrderBy(guest => guest.GuestId).Select(guest => guest.Agent.ArrivalTime).ToArray();
            var replayArrivals = replay.Guests.OrderBy(guest => guest.GuestId).Select(guest => guest.Agent.ArrivalTime).ToArray();
            var otherArrivals = differentSeed.Guests.OrderBy(guest => guest.GuestId).Select(guest => guest.Agent.ArrivalTime).ToArray();
            Assert.That(replayArrivals, Is.EqualTo(arrivals));
            Assert.That(otherArrivals, Is.Not.EqualTo(arrivals), "Changing the configured seed must affect the authored schedule variation.");
            Assert.That(arrivals.Distinct().Count(), Is.EqualTo(arrivals.Length), "Booked guests must not all arrive at once.");
            Assert.That(arrivals.Min(), Is.GreaterThanOrEqualTo(0));
            Assert.That(arrivals.Max(), Is.LessThan(settings.ServiceSeconds));
        }

        [Test]
        public void ReservationsDoNotOccupyRoomsAddHeatDemandOrCreateRoomExposureBeforePhysicalCheckIn()
        {
            Start(simulation, count: 1);
            var guest = simulation.Guests.Single();
            Assert.That(rooms[0].ReservedGuestId, Is.EqualTo(guest.GuestId));
            Assert.That(rooms[0].Reserved, Is.True);
            Assert.That(rooms.Any(room => room.Occupied), Is.False);
            Assert.That(simulation.Boiler.Load, Is.Zero);
            Assert.That(guest.Agent.CheckedIn, Is.False);
            ModelKeyHandoff.TakeKey(simulation, 0, guest.RoomId);
            Assert.That(simulation.CheckIn(0, guest.GuestId).Success, Is.False, "Even the correct key cannot check in a reservation before reaching reception.");
            rooms[0].Temperature = 5;
            rooms[0].Noise = 1;
            AdvanceTo(simulation, guest.Agent.ArrivalTime + 5);
            Assert.That(guest.Agent.WaitingSeconds, Is.Zero, "Travel to reception is not check-in queue time.");
            Assert.That(simulation.SignalGuestReachedReception(guest.GuestId).Success, Is.True);
            simulation.Tick(1);
            Assert.That(rooms[0].Occupied, Is.False);
            Assert.That(simulation.Boiler.Load, Is.Zero);
            Assert.That(guest.ColdExposureSeconds, Is.Zero);
            Assert.That(guest.NoiseExposureSeconds, Is.Zero);
            Assert.That(simulation.Requests.ActiveCount, Is.Zero);
            Assert.That(ModelKeyHandoff.CheckIn(simulation, 0, guest.GuestId).Success, Is.True);
            Assert.That(rooms[0].GuestId, Is.EqualTo(guest.GuestId));
            Assert.That(guest.Agent.CheckedIn, Is.True);
            Assert.That(simulation.Boiler.Load, Is.Zero, "GoingToRoom has no in-room activity demand yet.");
            Assert.That(simulation.SignalGuestReachedRoom(guest.GuestId).Success, Is.True);
            simulation.Tick(0.2f);
            Assert.That(simulation.Boiler.Load, Is.GreaterThan(0));
        }

        [Test]
        public void ReceptionCommandsRejectInvalidActorsUnknownGuestsAndRepeatedCheckIn()
        {
            Start(simulation, count: 1);
            var guest = simulation.Guests.Single();
            Assert.That(simulation.SignalGuestReachedReception("missing").Success, Is.False);
            Assert.That(simulation.SignalGuestReachedRoom(guest.GuestId).Success, Is.False);
            AdvanceTo(simulation, guest.Agent.ArrivalTime + 0.2f);
            Assert.That(simulation.SignalGuestReachedReception(guest.GuestId).Success, Is.True);
            Assert.That(simulation.CheckIn(-1, guest.GuestId).Success, Is.False);
            Assert.That(simulation.CheckIn(-2, guest.GuestId).Success, Is.False);
            Assert.That(simulation.CheckIn(0, "missing").Success, Is.False);
            Assert.That(guest.Agent.CheckedIn, Is.False);
            Assert.That(ModelKeyHandoff.CheckIn(simulation, 1, guest.GuestId).Success, Is.True);
            Assert.That(simulation.CheckIn(0, guest.GuestId).Success, Is.False);
            Assert.That(simulation.Guests.Count, Is.EqualTo(1));
            Assert.That(rooms.Count(room => room.Occupied), Is.EqualTo(1));
        }

        [Test]
        public void ReceptionDelayReducesPatienceAndFinalSatisfactionWithoutFabricatingRoomExposure()
        {
            Start(simulation, count: 1);
            var guest = simulation.Guests.Single();
            AdvanceTo(simulation, guest.Agent.ArrivalTime + 0.2f);
            Assert.That(simulation.SignalGuestReachedReception(guest.GuestId).Success, Is.True);
            float patience = guest.Agent.WaitingPatienceRemaining;
            Assert.That(patience, Is.GreaterThan(0));
            AdvanceTo(simulation, simulation.Elapsed + patience + 5);
            Assert.That(guest.Agent.WaitingSeconds, Is.GreaterThanOrEqualTo(patience));
            Assert.That(guest.Agent.WaitingPatienceRemaining, Is.Zero);
            Assert.That(guest.CheckInDelayPenaltySeconds, Is.GreaterThan(0));
            Assert.That(guest.ColdExposureSeconds + guest.NoiseExposureSeconds + guest.DirtyExposureSeconds + guest.FixtureExposureSeconds, Is.Zero);
            Assert.That(guest.ExpiredComplaintSeconds, Is.Zero, "Check-in delay is not an invented room complaint.");
            Assert.That(ModelKeyHandoff.CheckIn(simulation, 0, guest.GuestId).Success, Is.True);
            Assert.That(simulation.SignalGuestReachedRoom(guest.GuestId).Success, Is.True);
            simulation.Tick(1);
            float delayedScore = simulation.Satisfaction.Evaluate(guest);
            var prompt = Create(living, out _);
            Start(prompt, count: 1);
            ReceiveAndCheckIn(prompt, prompt.Guests.Single());
            prompt.Tick(1);
            Assert.That(delayedScore, Is.LessThan(prompt.Satisfaction.Evaluate(prompt.Guests.Single())),
                "Serving a late queue must preserve its service consequence after check-in.");
        }

        [Test]
        public void NeverCheckedInBookingsCannotGenerateRoomRevenueAtSettlement()
        {
            Start(simulation, count: 2);
            var waitingGuest = simulation.Guests.OrderBy(guest => guest.Agent.ArrivalTime).First();
            AdvanceTo(simulation, waitingGuest.Agent.ArrivalTime + 0.2f);
            Assert.That(simulation.SignalGuestReachedReception(waitingGuest.GuestId).Success, Is.True);
            AdvanceTo(simulation, settings.ServiceSeconds);
            var report = simulation.EndShift();
            Assert.That(report.Gross, Is.Zero);
            Assert.That(report.Receipts.All(receipt => receipt.Price == 0 && receipt.Compensation == 0), Is.True);
            Assert.That(report.Cash, Is.EqualTo(settings.Economy.StartingCash - settings.Economy.DailyOperatingCost));
            Assert.That(rooms.Any(room => room.Occupied), Is.False);
            Assert.That(simulation.Boiler.Load, Is.Zero);
        }

        [Test]
        public void ShowerActivityAddsDemandQuietRestRemovesItAndSkipSelectsTheNextScheduledEntry()
        {
            Start(simulation, count: 1);
            var guest = simulation.Guests.Single();
            Assert.That(simulation.ForceActivity(guest.GuestId, GuestActivity.Shower).Success, Is.False,
                "A booked guest who has not reached a room cannot take a shower there.");
            ReceiveAndCheckIn(simulation, guest);
            Assert.That(simulation.ForceActivity(guest.GuestId, GuestActivity.QuietRest).Success, Is.True);
            simulation.Tick(0.2f);
            float quietLoad = simulation.Boiler.Load;
            int revision = simulation.EventRevision;
            Assert.That(simulation.ForceActivity(guest.GuestId, GuestActivity.Shower).Success, Is.True);
            simulation.Tick(0.2f);
            Assert.That(guest.Agent.Activity, Is.EqualTo(GuestActivity.Shower));
            Assert.That(simulation.Boiler.Load, Is.EqualTo(guest.Application.Archetype.HeatingDemand * living.ShowerDemandMultiplier).Within(0.001f));
            Assert.That(simulation.Boiler.Load, Is.GreaterThan(quietLoad));
            Assert.That(simulation.EventRevision, Is.EqualTo(revision), "Ordinary shower activity is not an actionable notification.");
            Assert.That(simulation.ForceActivity(guest.GuestId, GuestActivity.QuietRest).Success, Is.True);
            simulation.Tick(0.2f);
            Assert.That(guest.Agent.Activity, Is.EqualTo(GuestActivity.QuietRest));
            Assert.That(simulation.Boiler.Load, Is.EqualTo(quietLoad).Within(0.001f));
            var next = guest.Agent.NextPlannedActivity;
            int beforeSkip = simulation.EventRevision;
            Assert.That(simulation.SkipActivity(guest.GuestId).Success, Is.True);
            Assert.That(guest.Agent.Activity, Is.EqualTo(next));
            Assert.That(simulation.EventRevision, Is.EqualTo(beforeSkip), "Schedule changes are background life.");
            Assert.That(simulation.ForceActivity("missing", GuestActivity.Shower).Success, Is.False);
            Assert.That(simulation.ForceActivity(guest.GuestId, (GuestActivity)999).Success, Is.False);
        }

        [Test]
        public void ShiftSettlementLetsVisibleGuestsFinishTheirExitButDoesNotSpawnFutureArrivals()
        {
            Start(simulation, count: 2);
            var ordered = simulation.Guests.OrderBy(guest => guest.Agent.ArrivalTime).ToArray();
            var visible = ordered[0];
            var future = ordered[1];
            ReceiveAndCheckIn(simulation, visible);
            simulation.Tick(0.2f);
            Assert.That(future.Agent.State, Is.EqualTo(GuestAgentState.Scheduled));
            simulation.EndShift();
            Assert.That(simulation.Running, Is.False);
            Assert.That(visible.Agent.State, Is.EqualTo(GuestAgentState.Leaving));
            Assert.That(future.Agent.State, Is.EqualTo(GuestAgentState.Left));
            Assert.That(rooms.Any(room => room.Occupied || room.Reserved), Is.False);
            Assert.That(simulation.Boiler.Load, Is.Zero);
            Assert.That(simulation.CheckIn(0, visible.GuestId).Success, Is.False);
            Assert.That(simulation.ForceActivity(visible.GuestId, GuestActivity.Shower).Success, Is.False);
            Assert.That(simulation.SignalGuestLeft(visible.GuestId).Success, Is.True,
                "A visual guest must be able to report reaching the exit after the ledger closes.");
            Assert.That(visible.Agent.State, Is.EqualTo(GuestAgentState.Left));
            Assert.That(simulation.SignalGuestLeft(visible.GuestId).Success, Is.False);
        }

        [Test]
        public void ScheduledActivityStartsFromMeasuredHotelTimeWithoutAnActionableEvent()
        {
            Start(simulation, count: 1);
            var guest = simulation.Guests.Single();
            ReceiveAndCheckIn(simulation, guest);
            float next = guest.Agent.NextActivityTime;
            Assert.That(next, Is.GreaterThan(simulation.Elapsed));
            int revision = simulation.EventRevision;
            AdvanceTo(simulation, next - 0.1f);
            Assert.That(simulation.EventRevision, Is.EqualTo(revision));
            simulation.Tick(0.2f);
            Assert.That(simulation.EventRevision, Is.EqualTo(revision));
            Assert.That(guest.Agent.ActivityEndsAt, Is.GreaterThan(simulation.Elapsed));
        }

        [Test]
        public void ThreeDaysReplayWithPhysicalTravelCommandsAndBillEachCheckedInStayOnlyOnce()
        {
            var replay = Create(new LivingHotelSettings(seed: living.Seed), out _);
            RunThreeDays(simulation);
            RunThreeDays(replay);
            Assert.That(simulation.DayReports.Count, Is.EqualTo(3));
            Assert.That(simulation.DayReports.SelectMany(report => report.Receipts).Count(), Is.EqualTo(12));
            for (int day = 0; day < 3; day++)
            {
                var report = simulation.DayReports[day];
                var repeated = replay.DayReports[day];
                Assert.That(report.Receipts.Select(receipt => receipt.GuestId).Distinct().Count(), Is.EqualTo(4));
                Assert.That(report.Receipts.All(receipt => receipt.Price > 0), Is.True);
                Assert.That(repeated.Cash, Is.EqualTo(report.Cash));
                Assert.That(repeated.Reputation, Is.EqualTo(report.Reputation));
                Assert.That(repeated.Receipts.Select(receipt => receipt.Satisfaction),
                    Is.EqualTo(report.Receipts.Select(receipt => receipt.Satisfaction)));
            }
        }

        private void RunThreeDays(HotelSimulation target)
        {
            for (int day = 1; day <= 3; day++)
            {
                Start(target, day);
                for (int step = 0; step < 1600 && !target.IsServiceComplete; step++)
                {
                    target.Tick(0.2f);
                    foreach (var guest in target.Guests)
                    {
                        // Headless travel adapter: production presentation reports these only
                        // after the complete lobby/exterior route and the return room route.
                        if (guest.Agent.State == GuestAgentState.LeavingRoom) target.SignalGuestLeftRoom(guest.GuestId);
                        if (guest.Agent.State == GuestAgentState.ReturningToRoom) target.SignalGuestReturnedRoom(guest.GuestId);
                        if (guest.Agent.CheckedIn || target.Elapsed < guest.Agent.ArrivalTime) continue;
                        Assert.That(target.SignalGuestReachedReception(guest.GuestId).Success, Is.True);
                        Assert.That(ModelKeyHandoff.CheckIn(target, 0, guest.GuestId).Success, Is.True);
                        Assert.That(target.SignalGuestReachedRoom(guest.GuestId).Success, Is.True);
                    }
                }
                Assert.That(target.IsServiceComplete, Is.True);
                var report = target.EndShift();
                Assert.That(target.EndShift(), Is.SameAs(report));
                if (day < 3)
                {
                    Assert.That(target.ApplyMaintenance(0, MaintenanceChoice.Defer).Success, Is.True);
                    // The headless adapter now completes physical departures and every manual linen command,
                    // just as it already reports reception/room arrivals. It never resets cleanliness.
                    foreach (var guest in target.Guests)
                        if (guest.Agent.State != GuestAgentState.Left)
                            Assert.That(target.SignalGuestVacatedRoom(guest.GuestId, guest.RoomId).Success, Is.True);
                    foreach (var room in target.Guests.Select(guest => guest.RoomId).Distinct())
                        ManualTurnoverModelAdapter.PrepareRoom(target, room);
                    Assert.That(target.Housekeeping.HasPendingWork, Is.False);
                }
            }
        }
    }
}
