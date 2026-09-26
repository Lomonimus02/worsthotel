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
    public sealed class RepairCadenceTests
    {
        [Test]
        public void MeasureRepeatedRestartCadenceAfterTwoGreedyPatchedServices()
        {
            var asset = AssetDatabase.LoadAssetAtPath<SessionConfig>("Assets/_WorstHotel/ScriptableObjects/PrototypeSession.asset");
            Assert.That(asset, Is.Not.Null);
            var settings = asset.ToData();
            var report = new StringBuilder();
            report.AppendLine("MEASURED RESTART CADENCE — actual BoilerSystem and configuration snapshots");
            report.AppendLine("Reproduces day1 four guests, patch, day2 highest six offers without intervention, patch, day3 highest six offers.");
            report.AppendLine("Assumes immediate response, continuously valid actor0 relief, and a correctly completed sequence by actor1 after 30 or45 seconds.");
            report.AppendLine("The timing is an explicit model assumption. This test does not exercise scene geometry, physical inputs, walking or human mistakes.");
            foreach (float repairSeconds in new[] { 30f, 45f })
            {
                var boiler = new BoilerSystem(settings.Boiler);
                for (int day = 1; day <= 2; day++)
                {
                    var offers = GuestSystem.GenerateApplications(day, settings.GuestArchetypes, settings.Day3BusinessReferencePrice);
                    var accepted = (day == 1 ? offers.AsEnumerable() : offers.OrderByDescending(offer => offer.ReferencePrice)).Take(day == 1 ? 4 : 6);
                    boiler.SetLoad(accepted.Sum(offer => offer.Archetype.HeatingDemand));
                    boiler.BeginService();
                    for (int tick = 0; tick < Math.Round(settings.ServiceSeconds * settings.TickRate); tick++) boiler.Tick(1 / settings.TickRate);
                    boiler.ApplyPaidMaintenance(Math.Min(100, boiler.Condition + settings.Economy.CheapPatchCondition));
                }
                float thirdLoad = GuestSystem.GenerateApplications(3, settings.GuestArchetypes, settings.Day3BusinessReferencePrice)
                    .OrderByDescending(offer => offer.ReferencePrice).Take(6).Sum(offer => offer.Archetype.HeatingDemand);
                boiler.SetLoad(thirdLoad); boiler.BeginService();
                report.AppendLine();
                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "ASSUMED REPAIR {0:F0}s; day3 starting condition {1:F3}; load {2:F2}", repairSeconds, boiler.Condition, boiler.Load));
                report.AppendLine("failure at | condition | time running since previous restart | restart at | condition at restart");
                int repairTicks = (int)Math.Ceiling(repairSeconds * settings.TickRate);
                int failureTick = -1, lastRestartTick = -1, failureCount = 0, completedRepairs = 0;
                int totalTicks = (int)Math.Round(settings.ServiceSeconds * settings.TickRate), downTicks = 0;
                var runningGaps = new List<float>();
                string pending = null;
                for (int tick = 1; tick <= totalTicks; tick++)
                {
                    bool wasFailed = boiler.Failed;
                    boiler.Tick(1 / settings.TickRate);
                    if (wasFailed) downTicks++;
                    if (boiler.Failed && !wasFailed)
                    {
                        failureTick = tick; failureCount++;
                        float gap = lastRestartTick >= 0 ? (tick - lastRestartTick) / settings.TickRate : -1;
                        if (gap >= 0) runningGaps.Add(gap);
                        pending = string.Format(CultureInfo.InvariantCulture, "{0:F1}s | {1:F2} | {2}",
                            tick / settings.TickRate, boiler.Condition, gap >= 0 ? gap.ToString("F1", CultureInfo.InvariantCulture) + "s" : "initial run");
                        Assert.That(boiler.SetRelief(0, true).Success, Is.True);
                    }
                    if (boiler.Failed && failureTick >= 0 && tick - failureTick >= repairTicks && boiler.InRepairBand)
                    {
                        float before = boiler.Condition;
                        Assert.That(boiler.Restart(1).Success, Is.True);
                        Assert.That(boiler.Condition, Is.EqualTo(before));
                        lastRestartTick = tick; completedRepairs++;
                        report.AppendLine(pending + string.Format(CultureInfo.InvariantCulture, " | {0:F1}s | {1:F2}", tick / settings.TickRate, boiler.Condition));
                        pending = null;
                    }
                }
                if (pending != null) report.AppendLine(pending + " | shift ended before restart | —");
                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "Failures {0}; completed repairs {1}; failed time {2:F1}s / {3:F1}%; post-restart running gaps {4}; end condition {5:F2}",
                    failureCount, completedRepairs, downTicks / settings.TickRate, 100f * downTicks / totalTicks,
                    string.Join(", ", runningGaps.Select(gap => gap.ToString("F1", CultureInfo.InvariantCulture) + "s")), boiler.Condition));
                Assert.That(failureCount, Is.GreaterThan(1), "This measurement should reveal recurrence; a balance change can intentionally update this trace.");
                Assert.That(completedRepairs, Is.GreaterThan(0));
            }
            string text = report.ToString();
            TestContext.WriteLine(text);
            string destination = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "repair-cadence.txt"));
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            File.WriteAllText(destination, text, new UTF8Encoding(false));
        }
    }
}
