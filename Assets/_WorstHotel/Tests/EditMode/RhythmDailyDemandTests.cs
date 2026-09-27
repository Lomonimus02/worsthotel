using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using UnityEditor;

namespace WorstHotel.Tests
{
    /// <summary>One production four-stay cohort. Sales alone is isolated to compare an exact
    /// roster. Timed model route/key/anchor acknowledgements are labelled adapters, not physics proof.</summary>
    public sealed class RhythmDailyDemandTests
    {
        sealed class Window
        {
            public string Name;
            public float From, Until, Seconds, PresentSeconds, OwnedSeconds, SleepingSeconds, AwaySeconds;
            public float SpaceIntegral, WaterIntegral, PeakWater, PeakDemand, MediaSeconds;
            public float MeanPresent => PresentSeconds / Seconds;
            public float MeanSpace => SpaceIntegral / Seconds;
            public float MeanWater => WaterIntegral / Seconds;
            public override string ToString() => string.Format(CultureInfo.InvariantCulture,
                "{0}: seconds={1:F1}; present={2:F3}; owned={3:F3}; sleeping={4:F3}; away={5:F3}; space={6:F4}; hotWater={7:F4}; peakWater={8:F4}; peakTotal={9:F4}; mediaGuestSeconds={10:F1}",
                Name, Seconds, MeanPresent, OwnedSeconds / Seconds, SleepingSeconds / Seconds, AwaySeconds / Seconds,
                MeanSpace, MeanWater, PeakWater, PeakDemand, MediaSeconds);
        }
        sealed class Route
        {
            public string Signature;
            public float Due, NextAnchor;
            public int Staff = -1;
            public bool Vacated;
        }
        sealed class Measurement
        {
            public HotelSimulation Hotel;
            public RoomState[] Rooms;
            public SessionSettings Settings;
            public RoomInfrastructureSettings Infrastructure;
            public Window[] Windows;
            public readonly Dictionary<string, List<float>> Showers = new Dictionary<string, List<float>>();
            public readonly Dictionary<string, float> FirstOuting = new Dictionary<string, float>();
            public readonly Dictionary<string, float> Returned = new Dictionary<string, float>();
            public readonly Dictionary<string, float> Checkouts = new Dictionary<string, float>();
            public readonly Dictionary<string, HashSet<GuestActivity>> MorningActivities = new Dictionary<string, HashSet<GuestActivity>>();
            public int CheckIns, RoomArrivals, Departures, AwayHeatSamples, Contacts;
            public float MaximumEquationError;
        }

        static void Require(CommandResult result) => Assert.That(result.Success, Is.True, result.Message);
        static float DoorZ(int room) => 10 + (room - 101) / 2 * 7;

        // Finite route and staff adapters: actual model transitions/commands only. No forced
        // activity, sleep, excursion, temperature, heat-load, guest arrival date or fault.
        sealed class Boundaries
        {
            readonly Measurement run;
            readonly Dictionary<string, Route> routes = new Dictionary<string, Route>();
            readonly string[] staff = new string[2];
            readonly float[] freeAt = new float[2];
            public Boundaries(Measurement run) => this.run = run;
            public void Tick()
            {
                var hotel = run.Hotel; float now = hotel.Elapsed;
                for (int actor = 0; actor < 2; actor++)
                    if (staff[actor] == "reply" && now >= freeAt[actor]) staff[actor] = null;
                foreach (var guest in hotel.Guests.ToArray())
                {
                    var agent = guest.Agent;
                    if (!routes.TryGetValue(guest.GuestId, out var route))
                    {
                        routes.Add(guest.GuestId, route = new Route());
                        run.Showers.Add(guest.GuestId, new List<float>());
                        run.MorningActivities.Add(guest.GuestId, new HashSet<GuestActivity>());
                        Require(hotel.RegisterGuestPhysicalStaging(guest.GuestId));
                    }
                    string signature = agent.State + ":" + agent.Activity + ":" + agent.StateChangedAt + ":" + agent.ResponseActionId + ":" + agent.ResponseActionVersion;
                    if (signature != route.Signature)
                    {
                        if (route.Staff >= 0) { staff[route.Staff] = null; route.Staff = -1; }
                        route.Signature = signature; route.Vacated = false; route.NextAnchor = now;
                        float corridor = (DoorZ(guest.RoomId) + 10) / 1.35f + .8f;
                        route.Due = now + (agent.State == GuestAgentState.Arriving ? 7 :
                            agent.State == GuestAgentState.GoingToRoom || agent.State == GuestAgentState.LeavingRoom ||
                            agent.State == GuestAgentState.ReturningToRoom || agent.IsServiceReceptionTrip ? corridor :
                            agent.State == GuestAgentState.Leaving ? corridor + 5 : 1.2f);
                    }
                    if (agent.State == GuestAgentState.Arriving && now >= route.Due)
                        Require(hotel.SignalGuestReachedReception(guest.GuestId));
                    else if (agent.State == GuestAgentState.WaitingForCheckIn)
                    {
                        var room = run.Rooms.Single(item => item.Profile.Id == guest.RoomId);
                        bool ready = !room.Occupied && room.DepartingGuestId == null && room.Cleanliness == Cleanliness.Clean;
                        if (ready && route.Staff < 0)
                        {
                            int actor = Array.FindIndex(staff, item => item == null);
                            if (actor >= 0) { route.Staff = actor; staff[actor] = guest.GuestId; route.Due = now + hotel.LivingSettings.KeyRetrievalEstimateSeconds; }
                        }
                        if (ready && route.Staff >= 0 && now >= route.Due)
                        { Require(ModelKeyHandoff.CheckIn(hotel, route.Staff, guest.GuestId)); run.CheckIns++; }
                    }
                    else if (agent.State == GuestAgentState.GoingToRoom && now >= route.Due)
                    { Require(hotel.SignalGuestReachedRoom(guest.GuestId)); run.RoomArrivals++; }
                    else if (agent.State == GuestAgentState.LeavingRoom && now >= route.Due)
                    {
                        Require(hotel.SignalGuestLeftRoom(guest.GuestId));
                        if (!run.FirstOuting.ContainsKey(guest.GuestId)) run.FirstOuting.Add(guest.GuestId, now);
                    }
                    else if (agent.State == GuestAgentState.ReturningToRoom && now >= route.Due)
                    { Require(hotel.SignalGuestReturnedRoom(guest.GuestId)); run.Returned[guest.GuestId] = now; }
                    else if (agent.State == GuestAgentState.Leaving)
                    {
                        if (!route.Vacated && now >= agent.StateChangedAt + 5)
                        {
                            foreach (var room in run.Rooms.Where(item => item.DepartingGuestId == guest.GuestId))
                                Require(hotel.SignalGuestVacatedRoom(guest.GuestId, room.Profile.Id));
                            route.Vacated = true;
                        }
                        if (route.Vacated && now >= route.Due)
                        { Require(hotel.SignalGuestLeft(guest.GuestId)); run.Departures++; }
                    }
                    if (guest.ReceiptPosted && !run.Checkouts.ContainsKey(guest.GuestId)) run.Checkouts.Add(guest.GuestId, now);
                    if (agent.ResponseActionId != null && now >= route.NextAnchor)
                    {
                        var response = hotel.Services.FindResponse(agent.ResponseActionId);
                        GuestResponseAnchor? anchor = agent.State == GuestAgentState.ReturningFromServiceReception && now >= route.Due ? GuestResponseAnchor.AssignedRoom :
                            agent.State == GuestAgentState.GoingToServiceReception && now >= route.Due ? GuestResponseAnchor.Reception :
                            agent.InAssignedRoom && agent.Activity == GuestActivity.AdjustRadiator && now >= route.Due ? GuestResponseAnchor.Radiator :
                            agent.InAssignedRoom && agent.Activity == GuestActivity.CallReception && now >= route.Due && response?.AttemptStartedAt < 0 ? GuestResponseAnchor.RoomPhone : (GuestResponseAnchor?)null;
                        if (anchor.HasValue)
                        {
                            // A concern may recover en route, or the telephone can be busy.
                            // A rejected acknowledgement does not invent completion; normal retry remains bounded.
                            hotel.SignalGuestResponseAnchorReached(guest.GuestId, agent.ResponseActionId, agent.ResponseActionVersion, anchor.Value);
                            route.NextAnchor = now + 1;
                        }
                    }
                    else if (agent.ResponseActionId == null && agent.InAssignedRoom && !agent.ActivityStaged && now >= route.Due)
                    {
                        Require(hotel.SignalGuestActivityReady(guest.GuestId, agent.State, agent.Activity));
                        if (agent.Activity == GuestActivity.Shower) run.Showers[guest.GuestId].Add(now);
                    }
                }
                ReplyToActualContacts();
            }
            void ReplyToActualContacts()
            {
                var hotel = run.Hotel; int actor = Array.FindIndex(staff, item => item == null);
                if (actor < 0) return;
                CommandResult? reply = null;
                if (hotel.Services.IncomingCall != null) reply = hotel.AnswerIncomingServiceCall(actor, hotel.Services.IncomingCall.Id);
                else
                {
                    var response = hotel.Services.Responses.FirstOrDefault(item => item.Phase == GuestResponsePhase.Contacting &&
                        item.Channel == GuestContactChannel.Reception && item.AttemptStartedAt >= 0);
                    if (response != null) reply = hotel.TalkToServiceGuest(actor, response.GuestId, response.Id);
                }
                if (reply?.Success == true) { run.Contacts++; staff[actor] = "reply"; freeAt[actor] = hotel.Elapsed + .6f; }
                // Explicit modest staff policy: decline optional promises after real disclosure,
                // no free fulfillment/blanket/credit or private concern inspection.
                foreach (var item in hotel.Services.Cases.Where(item => item.Active && item.IsKnownToHotel).ToArray())
                    Require(hotel.RespondToService(actor, item.Id, false));
                foreach (var intent in hotel.Services.Intents.Where(item => item.Active && item.Purpose == ServiceIntentPurpose.CompensationDiscussion).ToArray())
                    Require(hotel.AcceptConsequences(actor, intent.GuestId));
            }
        }

        static Measurement RunProductionCohort()
        {
            var config = AssetDatabase.LoadAssetAtPath<SessionConfig>("Assets/_WorstHotel/ScriptableObjects/PrototypeSession.asset");
            Assert.That(config, Is.Not.Null);
            var living = config.living.ToData();
            Assert.That(living.Rhythm.Enabled, Is.True, "This comparison must retain the authored dated-stay rhythm.");
            var settings = config.ToData();
            var run = new Measurement { Settings = settings, Infrastructure = config.infrastructure.ToData(),
                Rooms = settings.Rooms.OrderBy(item => item.Id).Select(item => new RoomState(item)).ToArray() };
            run.Hotel = new HotelSimulation(settings, run.Rooms, living, config.needs.ToData(), config.noise.ToData(),
                config.heater.ToData(), config.electricity.ToData(), config.housekeeping.ToData(), config.services.ToData(),
                run.Infrastructure, ManualBookingFixture.Operations(config));
            var hotel = run.Hotel; Require(hotel.StartOperations());
            var offers = hotel.BookingOffers.Where(item => item.ArrivalDay == 1).OrderBy(item => item.ArrivalAt).Take(4).ToArray();
            for (int index = 0; index < offers.Length; index++) Require(hotel.AcceptBooking(0, offers[index].Id, 101 + index, offers[index].Application.ReferencePrice));
            run.Windows = new[] {
                new Window { Name = "afternoon17-19", From = hotel.Calendar.At(1,17), Until = hotel.Calendar.At(1,19) },
                new Window { Name = "evening19.5-22.5", From = hotel.Calendar.At(1,19.5f), Until = hotel.Calendar.At(1,22.5f) },
                new Window { Name = "night00-06", From = hotel.Calendar.At(2,0), Until = hotel.Calendar.At(2,6) },
                new Window { Name = "morning06-10", From = hotel.Calendar.At(2,6), Until = hotel.Calendar.At(2,10) },
                new Window { Name = "midday11-13", From = hotel.Calendar.At(2,11), Until = hotel.Calendar.At(2,13) }
            };
            var adapter = new Boundaries(run);
            float until = run.Windows.Last().Until;
            while (hotel.Elapsed < until)
            {
                float before = hotel.Elapsed;
                hotel.Tick(Math.Min(1f / settings.TickRate, until - before));
                adapter.Tick(); Sample(run, before, hotel.Elapsed);
            }
            return run;
        }

        static void Sample(Measurement run, float before, float now)
        {
            var hotel = run.Hotel;
            float space = 0, water = 0;
            foreach (var row in hotel.HeatingDemands)
            {
                var room = run.Rooms.Single(item => item.Profile.Id == row.RoomId);
                var guest = row.GuestId == null ? null : hotel.Guests.Single(item => item.GuestId == row.GuestId);
                bool shower = guest?.Agent.State == GuestAgentState.PerformingActivity && guest.Agent.InAssignedRoom &&
                    guest.Agent.ActivityStaged && guest.Agent.Activity == GuestActivity.Shower;
                float expectedWater = shower ? guest.Application.Archetype.HeatingDemand *
                    Math.Max(0, hotel.LivingSettings.ShowerDemandMultiplier - hotel.LivingSettings.QuietDemandMultiplier) : 0;
                Assert.That(row.HotWater, Is.EqualTo(expectedWater).Within(.00001f), "Hot water requires a current real staged shower: " + row.RoomId);
                float expectedSpace = (guest == null ? run.Infrastructure.VacantRadiatorDemand :
                    guest.Application.Archetype.HeatingDemand * hotel.LivingSettings.QuietDemandMultiplier) *
                    run.Infrastructure.DemandMultiplier(room.RadiatorSetting) *
                    (1 + Math.Max(0, room.Profile.HeatLoss) * run.Infrastructure.HeatLossDemandFactor);
                Assert.That(row.SpaceHeating, Is.EqualTo(expectedSpace).Within(.00001f), "Space heat follows owner/valve/loss, not a hidden hourly multiplier.");
                if (guest?.Agent.State == GuestAgentState.GuestAway)
                {
                    Assert.That(row.SpaceHeating, Is.GreaterThan(0)); Assert.That(row.HotWater, Is.Zero);
                    Assert.That(room.GuestId, Is.EqualTo(guest.GuestId));
                    Assert.That(hotel.Keys.Find(room.Profile.Id).GuestId, Is.EqualTo(guest.GuestId));
                    run.AwayHeatSamples++;
                }
                space += row.SpaceHeating; water += row.HotWater;
            }
            run.MaximumEquationError = Math.Max(run.MaximumEquationError, Math.Abs(hotel.Boiler.Load - space - water));
            Assert.That(hotel.Boiler.Load, Is.EqualTo(space + water).Within(.00001f));
            var active = hotel.Guests.Where(guest => !guest.ReceiptPosted && guest.Agent.CheckedIn).ToArray();
            foreach (var guest in active.Where(guest => now >= guest.Agent.Schedule.WakeTime && guest.Agent.InAssignedRoom && guest.Agent.ActivityStaged))
                run.MorningActivities[guest.GuestId].Add(guest.Agent.Activity);
            foreach (var window in run.Windows)
            {
                float dt = Math.Max(0, Math.Min(now, window.Until) - Math.Max(before, window.From));
                if (dt <= 0) continue;
                window.Seconds += dt;
                window.PresentSeconds += active.Count(guest => guest.Agent.InAssignedRoom) * dt;
                window.OwnedSeconds += active.Length * dt;
                window.SleepingSeconds += active.Count(guest => guest.Agent.State == GuestAgentState.Sleeping) * dt;
                window.AwaySeconds += active.Count(guest => guest.Agent.State == GuestAgentState.GuestAway) * dt;
                window.MediaSeconds += active.Count(guest => guest.Agent.InAssignedRoom && guest.Agent.ActivityStaged &&
                    (guest.Agent.Activity == GuestActivity.LoudRoom || guest.Agent.Activity == GuestActivity.PhoneCall || guest.Agent.Activity == GuestActivity.WatchTV)) * dt;
                window.SpaceIntegral += space * dt; window.WaterIntegral += water * dt;
                window.PeakWater = Math.Max(window.PeakWater, water); window.PeakDemand = Math.Max(window.PeakDemand, space + water);
            }
        }

        [Test]
        public void ProductionDatedCohortCreatesMorningShowerDemandAfternoonOutingsAndQuietNightWithoutHourlyLoadMultiplier()
        {
            var run = RunProductionCohort(); var hotel = run.Hotel;
            TestContext.Out.WriteLine("Authored production seed=" + hotel.LivingSettings.Seed + "; four actual one-night bookings; production rhythm/needs/services/capacity preserved. Sales alone is isolated. Finite headless route/anchor/key/reply adapters; no activity/clock/fault forcing, no maintenance or turnover reset.");
            foreach (var window in run.Windows) TestContext.Out.WriteLine(window);
            TestContext.Out.WriteLine("checkedIn=" + run.CheckIns + "; roomArrivals=" + run.RoomArrivals + "; departed=" + run.Departures +
                "; contacts=" + run.Contacts + "; awayHeatSamples=" + run.AwayHeatSamples + "; maxEquationError=" + run.MaximumEquationError.ToString("G9", CultureInfo.InvariantCulture));
            foreach (var guest in hotel.Guests)
                TestContext.Out.WriteLine(guest.GuestId + "; kind=" + guest.Application.Archetype.Kind +
                    "; sleepAt=" + guest.Agent.Schedule.SleepTime.ToString("F3", CultureInfo.InvariantCulture) +
                    "; wakeAt=" + guest.Agent.Schedule.WakeTime.ToString("F3", CultureInfo.InvariantCulture) +
                    "; showersAt=" + string.Join(",", run.Showers[guest.GuestId].Select(at => at.ToString("F3", CultureInfo.InvariantCulture))) +
                    "; firstOuting=" + (run.FirstOuting.TryGetValue(guest.GuestId, out float left) ? left.ToString("F3", CultureInfo.InvariantCulture) : "none") +
                    "; return=" + (run.Returned.TryGetValue(guest.GuestId, out float returned) ? returned.ToString("F3", CultureInfo.InvariantCulture) : "none") +
                    "; checkout=" + (run.Checkouts.TryGetValue(guest.GuestId, out float checkout) ? checkout.ToString("F3", CultureInfo.InvariantCulture) : "none"));
            var afternoon = run.Windows[0]; var evening = run.Windows[1]; var night = run.Windows[2]; var morning = run.Windows[3]; var midday = run.Windows[4];
            Assert.That(run.CheckIns, Is.EqualTo(4)); Assert.That(run.RoomArrivals, Is.EqualTo(4)); Assert.That(run.Departures, Is.EqualTo(4));
            Assert.That(run.FirstOuting.Count, Is.GreaterThan(0)); Assert.That(run.AwayHeatSamples, Is.GreaterThan(0));
            Assert.That(afternoon.AwaySeconds, Is.GreaterThan(0));
            Assert.That(evening.MeanPresent, Is.GreaterThan(afternoon.MeanPresent), "Actual returns, not a boiler multiplier, make the evening hotel busier.");
            Assert.That(night.SleepingSeconds / night.OwnedSeconds, Is.GreaterThan(.8f), "Most owned guest-time should be sleeping overnight.");
            Assert.That(night.MeanWater, Is.Zero.Within(.00001f));
            Assert.That(night.MediaSeconds, Is.EqualTo(0).Within(.001f));
            Assert.That(morning.MeanWater, Is.GreaterThan(night.MeanWater));
            Assert.That(morning.PeakWater, Is.GreaterThan(0));
            Assert.That(morning.PeakDemand, Is.GreaterThan(night.PeakDemand), "The morning peak must be contributed by actual taps, not an hourly boost.");
            foreach (var guest in hotel.Guests)
            {
                var schedule = guest.Agent.Schedule;
                Assert.That(run.Showers[guest.GuestId].Count(at => at >= schedule.WakeTime && at < schedule.CheckoutTime), Is.EqualTo(1),
                    "Each natural morning starts one staged shower and must not loop back to it: " + guest.GuestId);
                Assert.That(run.MorningActivities[guest.GuestId].All(activity => activity == GuestActivity.Shower ||
                    activity == GuestActivity.QuietRest || activity == GuestActivity.Work || activity == GuestActivity.Pack ||
                    activity == GuestActivity.AdjustRadiator || activity == GuestActivity.CallReception), Is.True,
                    "Morning tail is finite; a real service response may interrupt it.");
                Assert.That(guest.ReceiptPosted, Is.True);
                Assert.That(guest.EarlyCheckout.State, Is.Not.EqualTo(EarlyCheckoutState.Committed));
                Assert.That(run.Checkouts[guest.GuestId], Is.EqualTo(schedule.CheckoutTime).Within(1f / run.Settings.TickRate + .001f));
                Assert.That(hotel.Keys.Find(guest.RoomId).Location, Is.EqualTo(RoomKeyLocation.Returned),
                    "The guest returned the key; this adapter does not include staff reclamation to the rack.");
            }
            Assert.That(midday.PresentSeconds + midday.OwnedSeconds + midday.WaterIntegral, Is.Zero.Within(.0001f));
            Assert.That(midday.MeanSpace, Is.LessThan(evening.MeanSpace), "Real checkouts leave only the actual vacant-radiator baseline.");
            Assert.That(run.Rooms.Count(room => room.Cleanliness == Cleanliness.Dirty), Is.EqualTo(4));
            Assert.That(hotel.DayReports.Count, Is.EqualTo(1)); Assert.That(hotel.Calendar.Day, Is.EqualTo(2));
            Assert.That(hotel.PeriodMaintenanceSpend + hotel.PeriodCapitalSpend, Is.Zero);
            Assert.That(hotel.Boiler.Condition, Is.LessThan(run.Settings.Boiler.InitialCondition), "The overnight/report boundary did not reset real wear.");
        }
    }
}
