using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace WorstHotel.Tests
{
    /// <summary>Production asset measurements, using timed navigation boundaries rather than rendered movement.</summary>
    public sealed class LivingBalanceTests
    {
        const string ConfigPath = "Assets/_WorstHotel/ScriptableObjects/PrototypeSession.asset";
        const string HeaterId = "balance-portable-heater";

        sealed class DayTrace
        {
            public int Day, MaxCases, MaxCauseTypes, MaxIndependentCauses, Gross, Refunds, Cash, Trips;
            public bool TemperatureNoiseConditionConcurrent;
            public float Load, Pressure, ConditionStart, ConditionEnd, Failure = -1, HeaterAt = -1;
            public float MaxWait, MeanWait, LongestEventGap, LongestQuietSpan, Preparation, Reputation, Satisfaction;
            public string Maintenance = "final", Rows, Needs, Causes = "none", IndependentCauses = "none";
            public readonly StringBuilder Events = new StringBuilder();
            public readonly HashSet<IncidentReason> ExperiencedCauses = new HashSet<IncidentReason>();
        }

        sealed class Trace
        {
            public string Name;
            public readonly List<DayTrace> Days = new List<DayTrace>();
        }

        // Authored prototype geometry, speeds and door allowance. These boundary callbacks are
        // intentionally explicit; this test supplies neither collision nor navigation evidence.
        sealed class TimedNavigation
        {
            sealed class Journey
            {
                public GuestAgentState State = GuestAgentState.Scheduled;
                public float Due, VacateAt;
                public bool Vacated;
                public int Actor = -1;
            }
            readonly HotelSimulation simulation;
            readonly RoomState[] rooms;
            readonly Dictionary<string, Journey> journeys = new Dictionary<string, Journey>();
            readonly string[] actors = new string[2];
            readonly float[] actorX = { -2.8f, -3.85f };
            readonly TimedManualTurnoverAdapter manualTurnover;
            public TimedNavigation(HotelSimulation simulation, RoomState[] rooms)
            { this.simulation = simulation; this.rooms = rooms; manualTurnover = new TimedManualTurnoverAdapter(simulation, rooms, actors); }

            public void Tick()
            {
                float now = simulation.Elapsed;
                for (int index = 0; index < simulation.Guests.Count; index++)
                {
                    var guest = simulation.Guests[index];
                    if (!journeys.TryGetValue(guest.GuestId, out var journey))
                        journeys.Add(guest.GuestId, journey = new Journey());
                    var state = guest.Agent.State;
                    float receptionX = -2.8f - index * 1.05f;
                    if (state != journey.State)
                    {
                        if (journey.Actor >= 0) { actors[journey.Actor] = null; journey.Actor = -1; }
                        journey.State = state;
                        if (state == GuestAgentState.Arriving)
                            journey.Due = now + (4.15f + Math.Abs(receptionX) + .95f) / 1.35f;
                        if (state == GuestAgentState.GoingToRoom)
                        {
                            float side = guest.RoomId % 2 == 1 ? -1 : 1;
                            journey.Due = now + (.95f + Math.Abs(receptionX - side * .32f) +
                                DoorZ(guest.RoomId) - .35f + 3.98f + .7f) / 1.35f + .8f;
                        }
                        if (state == GuestAgentState.Leaving)
                        {
                            journey.VacateAt = now + 7;
                            journey.Due = journey.VacateAt + (DoorZ(guest.RoomId) + 5.5f) / 1.35f;
                        }
                        if (state == GuestAgentState.LeavingRoom || state == GuestAgentState.ReturningToRoom)
                            journey.Due = now + (DoorZ(guest.RoomId) + 12.5f) / 1.35f + .8f;
                    }
                    if (state == GuestAgentState.Arriving && now >= journey.Due)
                        Require(simulation.SignalGuestReachedReception(guest.GuestId));
                    if (state == GuestAgentState.WaitingForCheckIn)
                    {
                        if (journey.Actor < 0)
                        {
                            int actor = Array.FindIndex(actors, value => value == null);
                            if (actor >= 0)
                            {
                                actors[actor] = guest.GuestId; journey.Actor = actor;
                                journey.Due = now + Math.Abs(actorX[actor] - receptionX) / 3.5f + simulation.LivingSettings.KeyRetrievalEstimateSeconds;
                                actorX[actor] = receptionX;
                            }
                        }
                        if (journey.Actor >= 0 && now >= journey.Due) Require(ModelKeyHandoff.CheckIn(simulation, journey.Actor, guest.GuestId));
                    }
                    if (state == GuestAgentState.GoingToRoom && now >= journey.Due)
                        Require(simulation.SignalGuestReachedRoom(guest.GuestId));
                    if (state == GuestAgentState.LeavingRoom && now >= journey.Due)
                        Require(simulation.SignalGuestLeftRoom(guest.GuestId));
                    if (state == GuestAgentState.ReturningToRoom && now >= journey.Due)
                        Require(simulation.SignalGuestReturnedRoom(guest.GuestId));
                    if (state == GuestAgentState.Leaving)
                    {
                        if (!journey.Vacated && now >= journey.VacateAt)
                        {
                            foreach (var room in rooms.Where(room => room.DepartingGuestId == guest.GuestId))
                                Require(simulation.SignalGuestVacatedRoom(guest.GuestId, room.Profile.Id));
                            journey.Vacated = true;
                        }
                        if (now >= journey.Due) Require(simulation.SignalGuestLeft(guest.GuestId));
                    }
                }
                manualTurnover.Tick();
            }
            public static float DoorZ(int room) => 10 + (room - 101) / 2 * 7;
        }

        [Test]
        public void ProductionLivingAssetsProduceMeasuredThreeDayDecisionRoutes()
        {
            var asset = AssetDatabase.LoadAssetAtPath<SessionConfig>(ConfigPath);
            Assert.That(asset, Is.Not.Null);
            Assert.That(asset.living && asset.needs && asset.noise && asset.heater && asset.electricity && asset.housekeeping, Is.True,
                "Measurements require every attached production configuration, without fallback settings.");
            var traces = new[] { Run(asset, true), Run(asset, false) };
            var output = new StringBuilder();
            output.AppendLine("PROTOTYPE 0.3 — MEASURED PRODUCTION GUEST AGENCY BALANCE");
            output.AppendLine("Assets: PrototypeSession and all attached living/needs/noise/heater/electricity/housekeeping snapshots.");
            output.AppendLine(FormattableString.Invariant($"Seed={asset.living.seed}; service={asset.serviceSeconds}s; arrival spacing={asset.living.arrivalSpacingSeconds}s; rack retrieval estimate={asset.living.keyRetrievalEstimateSeconds}s; tick={asset.tickRate}Hz."));
            output.AppendLine("Normal seeded activities, timed authored-route boundary adapter (guest1.35m/s, player3.5m/s including manual linen routes, door allowance0.8s). Two staff reserve a configured headless rack retrieval estimate then hand over the numbered key instantly; shared staff slots perform explicit linen pickup/deposit/shelf/make-bed commands; no forced states, temperatures, loads, noise or failures.");
            output.AppendLine("Lower occupancy / premium four: highest reference offers after day1, rooms selected using visible local heating/noise/fixture risk, proper repair when affordable else patch/defer. Risky: first six offers after day1, all six rooms, cheap patches; one heater delivered after first real cold complaint in circuit B, then left on. Neither policy uses emergency repair, quiet requests, relocation or voluntary credits. Premium four avoids infrastructure catastrophe, not all complaints; its room heuristic does not optimize acoustic adjacency.");
            output.AppendLine("Event gap is hotel seconds between model events. Quiet span excludes boiler failure and active requests; it can include useful arrivals/activities and is not empty waiting. Independent cause counts exclude derivative Service cases. Preparation includes physical exit callbacks, cleaner travel and configured cleaning; planning uses no boiler wear or guest exposure. Dividing hotel seconds by WAIT speed is only a mathematical lower bound: votes, interruptions and player movement are not measured here.");
            foreach (var trace in traces)
            {
                output.AppendLine(); output.AppendLine(trace.Name);
                foreach (var day in trace.Days)
                {
                    output.AppendLine(FormattableString.Invariant($"DAY {day.Day}: {day.Rows}"));
                    output.AppendLine(FormattableString.Invariant($"condition={day.ConditionStart:F1}->{day.ConditionEnd:F1}, peakLoad={day.Load:F2}, peakPressure={day.Pressure:F1}, failureAt={day.Failure:F1}s, heaterAt={day.HeaterAt:F1}s, circuitsTripped={day.Trips}; check-in wait mean/max={day.MeanWait:F1}/{day.MaxWait:F1}s"));
                    output.AppendLine(FormattableString.Invariant($"max active cases={day.MaxCases}, simultaneous cause types={day.MaxCauseTypes} ({day.Causes}), longest event gap={day.LongestEventGap:F1}s, quiet span={day.LongestQuietSpan:F1}s, post-shift turnover={day.Preparation:F1}s"));
                    output.AppendLine($"independent concurrent causes={day.MaxIndependentCauses} ({day.IndependentCauses}), actual Temperature+Noise+RoomCondition overlap={day.TemperatureNoiseConditionConcurrent}");
                    output.AppendLine(FormattableString.Invariant($"gross=${day.Gross}, refunds=${day.Refunds}, satisfaction={day.Satisfaction:F1}, maintenance={day.Maintenance}, cash=${day.Cash}, reputation={day.Reputation:F1}"));
                    output.AppendLine(day.Needs); output.Append(day.Events);
                }
                output.AppendLine(FormattableString.Invariant($"TOTAL gross=${trace.Days.Sum(day => day.Gross)}, refunds=${trace.Days.Sum(day => day.Refunds)}, final cash=${trace.Days.Last().Cash}, reputation={trace.Days.Last().Reputation:F1}"));
            }
            string report = output.ToString(); TestContext.WriteLine(report);
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "living-balance-scenarios.txt"));
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, report, new UTF8Encoding(false));
            Assert.That(traces.All(trace => trace.Days.Count == 3), Is.True);
            Assert.That(traces.All(trace => trace.Days[0].Failure < 0 && trace.Days[0].Trips == 0), Is.True,
                "The shared introductory day must remain free of guaranteed infrastructure catastrophe.");
            Assert.That(traces.All(trace => trace.Days[0].MaxCases <= 2 && trace.Days[0].Satisfaction >= 80), Is.True,
                "The introductory layout may produce a minor complaint, without starting as an overwhelmed hotel.");
            Assert.That(traces[0].Days.All(day => day.Failure < 0 && day.Trips == 0), Is.True);
            Assert.That(traces[0].Days.Last().Cash, Is.GreaterThan(0));
            Assert.That(traces[1].Days.Last().MaxIndependentCauses, Is.GreaterThanOrEqualTo(2));
            Assert.That(traces[1].Days.Last().ExperiencedCauses,
                Is.EquivalentTo(new[] { IncidentReason.Temperature, IncidentReason.Noise, IncidentReason.RoomCondition }),
                "The risky day must expose all three real families over time; quiet intervals and recovery need not make them overlap simultaneously.");
            Assert.That(traces.All(trace => trace.Days.All(day => day.LongestQuietSpan > 0)), Is.True,
                "Normal life must include genuinely quiet time; ambient activity is not a notification quota.");
            Assert.That(traces[0].Days.Sum(day => day.Gross), Is.LessThan(traces[1].Days.Sum(day => day.Gross)),
                "The lower occupancy policy must actually sacrifice gross revenue.");
        }

        static Trace Run(SessionConfig asset, bool cautious)
        {
            var settings = asset.ToData();
            var rooms = settings.Rooms.Select(profile => new RoomState(profile)).ToArray();
            var simulation = new HotelSimulation(settings, rooms, asset.living.ToData(), asset.needs.ToData(),
                asset.noise.ToData(), asset.heater.ToData(), asset.electricity.ToData(), asset.housekeeping.ToData());
            Require(simulation.Heaters.Register(HeaterId));
            var trace = new Trace { Name = cautious ? "LOWER OCCUPANCY / premium four / proper when affordable" : "RISKY SIX / patches + cold-response heater" };
            float dt = 1 / settings.TickRate;
            for (int day = 1; day <= settings.TotalDays; day++)
            {
                var offers = GuestSystem.GenerateApplications(day, settings.GuestArchetypes, settings.Day3BusinessReferencePrice);
                var accepted = (cautious && day > 1 ? offers.OrderByDescending(offer => offer.ReferencePrice) : offers.AsEnumerable())
                    .Take(day == 1 || cautious ? 4 : 6).ToArray();
                var assignments = Assign(settings, rooms, accepted, day, cautious);
                Require(simulation.StartShift(assignments, offers));
                var navigation = new TimedNavigation(simulation, rooms);
                var record = new DayTrace { Day = day, ConditionStart = simulation.Boiler.Condition,
                    Rows = string.Join("; ", assignments.Select(booking => {
                        var offer = accepted.Single(candidate => candidate.Id == booking.BookingId);
                        return offer.Id + " " + offer.Archetype.Kind + "→" + booking.RoomId + " $" + booking.Price;
                    })) };
                int previousRevision = simulation.EventRevision;
                Action<HotelRequest> recordRequest = request =>
                {
                    record.ExperiencedCauses.Add(request.Reason);
                    record.Events.AppendLine(F(simulation.Elapsed) + "s: room " + request.RoomId + " " + request.Reason + " complaint");
                };
                simulation.Requests.OnRequestCreated += recordRequest;
                float lastEvent = 0, quietRun = 0, heaterDue = float.PositiveInfinity;
                int heaterRoom = 0;
                for (int tick = 0; tick < 20000 && !simulation.IsServiceComplete; tick++)
                {
                    simulation.Tick(dt); navigation.Tick();
                    float now = simulation.Elapsed;
                    record.Load = Math.Max(record.Load, simulation.Boiler.Load);
                    record.Pressure = Math.Max(record.Pressure, simulation.Boiler.Pressure);
                    if (simulation.Boiler.Failed && record.Failure < 0)
                    { record.Failure = now; record.Events.AppendLine(F(now) + "s: natural boiler failure"); }
                    var active = simulation.Requests.Items.Where(request => !request.Resolved).ToArray();
                    foreach (var request in active)
                    {
                        var cause = simulation.Incidents.Items.Single(item => item.Id == request.Id).Cause;
                        Assert.That(cause, Is.Not.Null, "Every natural complaint must preserve its physical cause.");
                        Assert.That(cause.SourceEntityId, Is.Not.Null.And.Not.Empty);
                    }
                    var causes = active.Select(request => request.Reason).Distinct().OrderBy(reason => reason).ToArray();
                    var independent = causes.Where(reason => reason != IncidentReason.Service).ToArray();
                    record.MaxCases = Math.Max(record.MaxCases, active.Length);
                    if (causes.Length > record.MaxCauseTypes)
                    { record.MaxCauseTypes = causes.Length; record.Causes = string.Join("+", causes) + " at " + F(now) + "s"; }
                    if (independent.Length > record.MaxIndependentCauses)
                    { record.MaxIndependentCauses = independent.Length; record.IndependentCauses = string.Join("+", independent) + " at " + F(now) + "s"; }
                    record.TemperatureNoiseConditionConcurrent |= independent.Contains(IncidentReason.Temperature) &&
                        independent.Contains(IncidentReason.Noise) && independent.Contains(IncidentReason.RoomCondition);
                    if (!cautious && heaterRoom == 0)
                    {
                        var cold = active.FirstOrDefault(request => request.Reason == IncidentReason.Temperature && request.RoomId >= 104 &&
                            rooms.Single(room => room.Profile.Id == request.RoomId).Temperature < simulation.Guests.Single(guest => guest.GuestId == request.GuestId).Application.Archetype.Needs.PreferredTemperatureMin);
                        if (cold != null)
                        {
                            heaterRoom = cold.RoomId;
                            // Two route legs: reception to utility tool, then carrying to the chosen room.
                            heaterDue = now + (31.4f + 3.8f + 31.4f - TimedNavigation.DoorZ(heaterRoom) + 8) / 3.5f + 2;
                            record.Events.AppendLine(F(now) + "s: staff choose heater for " + heaterRoom + "; delivered after " + F(heaterDue - now) + "s route budget");
                        }
                    }
                    if (record.HeaterAt < 0 && now >= heaterDue)
                    {
                        Require(simulation.Heaters.AssignRoom(HeaterId, heaterRoom));
                        Require(simulation.Heaters.SetSwitchedOn(HeaterId, true)); simulation.RefreshElectrical();
                        record.HeaterAt = now; record.Events.AppendLine(F(now) + "s: heater ON room " + heaterRoom);
                    }
                    if (simulation.EventRevision != previousRevision)
                    {
                        record.LongestEventGap = Math.Max(record.LongestEventGap, now - lastEvent);
                        lastEvent = now; previousRevision = simulation.EventRevision;
                    }
                    quietRun = !simulation.Boiler.Failed && active.Length == 0 ? quietRun + dt : 0;
                    record.LongestQuietSpan = Math.Max(record.LongestQuietSpan, quietRun);
                }
                Assert.That(simulation.IsServiceComplete, Is.True);
                simulation.Requests.OnRequestCreated -= recordRequest;
                Assert.That(simulation.Guests.All(guest => guest.Agent.HasReachedRoom && guest.Elapsed > 0), Is.True);
                Assert.That(simulation.Boiler.LoadOverride, Is.Null);
                Assert.That(rooms.All(room => !simulation.Noise.GetNoiseOverride(room.Profile.Id).HasValue), Is.True);
                record.LongestEventGap = Math.Max(record.LongestEventGap, simulation.Elapsed - lastEvent);
                record.MaxWait = simulation.Guests.Max(guest => guest.CheckInWaitingSeconds);
                record.MeanWait = simulation.Guests.Average(guest => guest.CheckInWaitingSeconds);
                record.ConditionEnd = simulation.Boiler.Condition;
                record.Trips = new[] { "A", "B" }.Count(id => simulation.Electrical.Find(id).Tripped);
                record.Needs = "EXPOSURES " + string.Join("; ", simulation.Guests.Select(guest => FormattableString.Invariant(
                    $"{guest.RoomId}: temp/noise/condition/service={guest.Needs.Temperature.ExposureSeconds:F0}/{guest.Needs.Noise.ExposureSeconds:F0}/{guest.Needs.RoomCondition.ExposureSeconds:F0}/{guest.Needs.Service.ExposureSeconds:F0}s, cold={guest.ColdExposureSeconds:F0}s, power={guest.PowerLossExposureSeconds:F0}s, roomTime={guest.Elapsed:F0}s")));
                var settled = simulation.EndShift();
                record.Gross = settled.Gross; record.Refunds = settled.Compensation; record.Cash = settled.Cash;
                record.Reputation = settled.Reputation; record.Satisfaction = settled.AverageSatisfaction;
                Assert.That(settled.Receipts.All(receipt => receipt.Price > 0), Is.True);
                if (simulation.MaintenanceRequired)
                {
                    var choice = cautious && simulation.Economy.Cash >= settings.Economy.ProperRepairCost ? MaintenanceChoice.ProperRepair :
                        simulation.Economy.Cash >= settings.Economy.CheapPatchCost ? MaintenanceChoice.CheapPatch : MaintenanceChoice.Defer;
                    Require(simulation.ApplyMaintenance(day % 2, choice)); record.Maintenance = choice.ToString(); record.Cash = simulation.Economy.Cash;
                }
                // The final day enters Results; only earlier days expose a preparation clock.
                if (day < settings.TotalDays)
                {
                    float preparationStart = simulation.Elapsed, conditionBefore = simulation.Boiler.Condition;
                    for (int tick = 0; tick < 20000 && (simulation.Housekeeping.HasPendingWork || rooms.Any(room => room.DepartingGuestId != null) ||
                        simulation.Guests.Any(guest => guest.Agent.State != GuestAgentState.Left)); tick++)
                    { simulation.AdvancePreparation(dt); navigation.Tick(); }
                    record.Preparation = simulation.Elapsed - preparationStart;
                    Assert.That(rooms.All(room => room.Cleanliness == Cleanliness.Clean && room.DepartingGuestId == null), Is.True);
                    Assert.That(simulation.Housekeeping.HasPendingWork, Is.False);
                    Assert.That(simulation.Boiler.Condition, Is.EqualTo(conditionBefore));
                }
                trace.Days.Add(record);
            }
            return trace;
        }

        static BookingAssignment[] Assign(SessionSettings settings, RoomState[] rooms, BookingApplication[] accepted, int day, bool cautious)
        {
            var available = rooms.ToList();
            var placements = new Dictionary<string, int>();
            if (day == 1)
            {
                int[] first = { 102, 106, 101, 105 };
                for (int i = 0; i < accepted.Length; i++) placements.Add(accepted[i].Id, first[i]);
            }
            else if (!cautious)
                for (int i = 0; i < accepted.Length; i++) placements.Add(accepted[i].Id, 101 + i);
            else
                foreach (var offer in accepted.OrderByDescending(offer => offer.Archetype.ColdThreshold))
                {
                    var room = available.OrderBy(candidate => candidate.Profile.HeatLoss / 4 * offer.Archetype.ColdPenaltyWeight +
                        Math.Max(0, candidate.Profile.Noise - offer.Archetype.NoiseTolerance) + (candidate.RepairState == RepairState.Degraded ? .2f : 0))
                        .ThenBy(candidate => candidate.Profile.Id).First();
                    available.Remove(room); placements.Add(offer.Id, room.Profile.Id);
                }
            return accepted.Select((offer, index) => new BookingAssignment(placements[offer.Id], offer.Id,
                settings.Economy.MinPrice + (int)Math.Round((double)(offer.ReferencePrice - settings.Economy.MinPrice) / settings.Economy.PriceStep,
                    MidpointRounding.AwayFromZero) * settings.Economy.PriceStep, index % 2)).ToArray();
        }

        static string F(float value) => value.ToString("F1", CultureInfo.InvariantCulture);
        static void Require(CommandResult result) => Assert.That(result.Success, Is.True, result.Message);
    }
}
