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
    /// <summary>Measured causal traces using the checked-in configuration assets; no forced failures or automatic tuning.</summary>
    public sealed class BalanceScenarioTests
    {
        private sealed class DayTrace
        {
            public int Day, Guests, Gross, Refunds, CashBeforeMaintenance, CashAfterMaintenance, ColdRequests;
            public float Load, ConditionStart, ConditionEnd, FirstFailure = -1, PeakPressure, Reputation;
            public string Maintenance = "Final results";
        }

        private sealed class Trace
        {
            public string Name;
            public readonly List<DayTrace> Days = new List<DayTrace>();
        }

        [Test]
        public void AssetDefaultsHaveMeasuredCautiousAndGreedyThreeDayRoutes()
        {
            const string configPath = "Assets/_WorstHotel/ScriptableObjects/PrototypeSession.asset";
            var asset = AssetDatabase.LoadAssetAtPath<SessionConfig>(configPath);
            Assert.That(asset, Is.Not.Null, "Build the prototype scene/configuration before running the balance scenarios.");
            var settings = asset.ToData();
            var traces = new[]
            {
                Run(settings, "Cautious four / patches", 4, false),
                Run(settings, "Greedy six / patches", 6, false),
                Run(settings, "Cautious four / proper when affordable", 4, true),
                Run(settings, "Greedy six / proper when affordable", 6, true)
            };
            var report = new StringBuilder();
            report.AppendLine("MEASURED BALANCE SCENARIOS — real PrototypeSession.asset snapshots");
            report.AppendLine("5 Hz or configured tick rate; configured service duration. Day 1 accepts its four offers at reference rates.");
            report.AppendLine("Days 2–3 select highest reference offers, assign suitable rooms by visible defects, and round rates to the configured price grid.");
            report.AppendLine("No forced failures, no emergency restarts, no voluntary compensation. Proper routes fall back to patch, then defer, if unaffordable.");
            report.AppendLine("These are deterministic model measurements, not a human fun/ergonomics test.");
            foreach (var trace in traces)
            {
                report.AppendLine(); report.AppendLine(trace.Name);
                report.AppendLine("day | guests | load | condition start/end | first failure sec | peak pressure | cold requests | gross | refunds | checkout cash | maintenance | next cash | reputation");
                foreach (var day in trace.Days)
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0} | {1} | {2:F2} | {3:F1}/{4:F1} | {5} | {6:F1} | {7} | ${8} | ${9} | ${10} | {11} | ${12} | {13:F1}",
                        day.Day, day.Guests, day.Load, day.ConditionStart, day.ConditionEnd,
                        day.FirstFailure < 0 ? "none" : day.FirstFailure.ToString("F1", CultureInfo.InvariantCulture),
                        day.PeakPressure, day.ColdRequests, day.Gross, day.Refunds, day.CashBeforeMaintenance,
                        day.Maintenance, day.CashAfterMaintenance, day.Reputation));
                report.AppendLine("TOTAL gross $" + trace.Days.Sum(day => day.Gross) + "; refunds $" + trace.Days.Sum(day => day.Refunds)
                    + "; final cash $" + trace.Days.Last().CashAfterMaintenance + "; reputation " + trace.Days.Last().Reputation.ToString("F1", CultureInfo.InvariantCulture));
            }
            string text = report.ToString();
            TestContext.WriteLine(text);
            string destination = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "balance-scenarios.txt"));
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            File.WriteAllText(destination, text, new UTF8Encoding(false));
            Assert.That(traces[0].Days.All(day => day.FirstFailure < 0), Is.True, "The cautious healthy four-guest route should not contain a scripted major failure.");
            Assert.That(traces[0].Days.Last().CashAfterMaintenance, Is.GreaterThan(0), "A cautious route should remain economically viable.");
            Assert.That(traces[1].Days.Last().FirstFailure, Is.GreaterThanOrEqualTo(0), "Repeated six-guest overloading with patches should cause a causal day-three failure.");
            Assert.That(traces[2].Days.Any(day => day.Maintenance == MaintenanceChoice.ProperRepair.ToString()), Is.True,
                "Proper repair should become affordable on a commercially sensible cautious route.");
        }

        private static Trace Run(SessionSettings settings, string name, int laterGuestCount, bool preferProper)
        {
            var trace = new Trace { Name = name };
            var rooms = settings.Rooms.Select(profile => new RoomState(profile)).ToArray();
            var simulation = new HotelSimulation(settings, rooms);
            for (int day = 1; day <= settings.TotalDays; day++)
            {
                var offers = GuestSystem.GenerateApplications(day, settings.GuestArchetypes, settings.Day3BusinessReferencePrice);
                var accepted = (day == 1 ? offers.AsEnumerable() : offers.OrderByDescending(offer => offer.ReferencePrice))
                    .Take(day == 1 ? 4 : laterGuestCount).ToArray();
                var assignments = AssignSuitableRooms(settings, rooms, accepted);
                var start = simulation.StartShift(assignments, offers);
                Assert.That(start.Success, Is.True, name + ": " + start.Message);
                var record = new DayTrace { Day = day, Guests = accepted.Length, Load = simulation.Boiler.Load,
                    ConditionStart = simulation.Boiler.Condition, PeakPressure = simulation.Boiler.Pressure };
                while (!simulation.IsServiceComplete)
                {
                    simulation.Tick(1 / settings.TickRate);
                    record.PeakPressure = Math.Max(record.PeakPressure, simulation.Boiler.Pressure);
                    if (simulation.Boiler.Failed && record.FirstFailure < 0) record.FirstFailure = simulation.Elapsed;
                }
                record.ConditionEnd = simulation.Boiler.Condition;
                record.ColdRequests = simulation.Requests.Items.Count(request => request.Reason == IncidentReason.Cold);
                var settled = simulation.EndShift();
                record.Gross = settled.Gross; record.Refunds = settled.Compensation;
                record.CashBeforeMaintenance = settled.Cash; record.CashAfterMaintenance = settled.Cash; record.Reputation = settled.Reputation;
                if (simulation.MaintenanceRequired)
                {
                    MaintenanceChoice choice = preferProper && simulation.Economy.Cash >= settings.Economy.ProperRepairCost
                        ? MaintenanceChoice.ProperRepair : simulation.Economy.Cash >= settings.Economy.CheapPatchCost
                        ? MaintenanceChoice.CheapPatch : MaintenanceChoice.Defer;
                    Assert.That(simulation.ApplyMaintenance(day % 2, choice).Success, Is.True);
                    record.Maintenance = choice.ToString(); record.CashAfterMaintenance = simulation.Economy.Cash;
                }
                trace.Days.Add(record);
            }
            return trace;
        }

        private static BookingAssignment[] AssignSuitableRooms(SessionSettings settings, RoomState[] rooms, BookingApplication[] accepted)
        {
            var free = rooms.ToList();
            var result = new List<BookingAssignment>();
            // Visible heating needs get first pick; this is a reproducible booking policy, not hidden simulation difficulty.
            var ordered = accepted.OrderByDescending(offer => offer.Archetype.ColdThreshold)
                .ThenByDescending(offer => offer.ReferencePrice);
            foreach (var offer in ordered)
            {
                RoomState room = free.OrderBy(candidate => RoomRisk(candidate, offer.Archetype, settings.Economy))
                    .ThenBy(candidate => candidate.Profile.Id).First();
                free.Remove(room);
                int step = settings.Economy.PriceStep;
                int gridMax = settings.Economy.MinPrice + (settings.Economy.MaxPrice - settings.Economy.MinPrice) / step * step;
                int price = settings.Economy.MinPrice + (int)Math.Round((double)(offer.ReferencePrice - settings.Economy.MinPrice) / step,
                    MidpointRounding.AwayFromZero) * step;
                price = Math.Max(settings.Economy.MinPrice, Math.Min(gridMax, price));
                result.Add(new BookingAssignment(room.Profile.Id, offer.Id, price, result.Count % 2));
            }
            return result.ToArray();
        }

        private static float RoomRisk(RoomState room, GuestProfile guest, EconomySettings settings)
        {
            float fixture = room.RepairState == RepairState.Broken ? settings.BrokenSeverity :
                room.RepairState == RepairState.Degraded ? settings.DegradedSeverity : 0;
            float dirty = room.Cleanliness == Cleanliness.Dirty ? settings.DirtySeverity : 0;
            return room.Profile.HeatLoss / settings.ColdSeverityDegrees * guest.ColdPenaltyWeight
                + Math.Max(0, room.Noise - guest.NoiseTolerance) + fixture + dirty;
        }
    }
}
