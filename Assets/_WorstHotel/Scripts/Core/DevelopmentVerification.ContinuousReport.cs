#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace WorstHotel
{
    public sealed partial class DevelopmentVerification
    {
        void ValidateContinuousOutcome()
        {
            Require(ReferenceEquals(session.Simulation, continuousModel) && ReferenceEquals(session.Rooms, continuousRooms) &&
                ReferenceEquals(session.Simulation.Clock, continuousClock) && (LanSession.Instance ? LanSession.Instance.Epoch : 0) == continuousEpoch,
                "one original model, room registry, clock and network epoch through checkout tail");
            Require(continuousObservationEnd >= continuousModel.Calendar.At(4, 8) && continuousTailEnd >= continuousModel.Calendar.At(4, 12),
                "full72 hours and explicitly separate final checkout tail completed");
            completedReports = continuousModel.DayReports.ToArray();
            continuousFinalSnapshot = continuousModel.CaptureSnapshot(Math.Max(1, continuousEpoch), 1);
            continuousReceipts = completedReports.SelectMany(report => report.Receipts).Select(SnapshotData.Capture)
                .Concat(continuousFinalSnapshot.Operations.PeriodReceipts).ToArray();
            Require(completedReports.Length == 3 && continuousModel.ReportSequence == 3 &&
                completedReports.Select(r => r.DayNumber).SequenceEqual(new[] { 1, 2, 3 }), "exactly three consecutive nonmodal reports");
            Require(completedReports.Select(r => r.Receipts.Count).SequenceEqual(new[] { 0, 4, 5 }) &&
                continuousFinalSnapshot.Operations.PeriodReceipts.Length == 5,
                "06:00 report receipts0/4/5 and the final five next-morning checkouts in the current period");
            string[] booked = continuousCohorts.SelectMany(c => c.Ids).ToArray();
            Require(booked.Length == 14 && booked.Distinct().Count() == 14 && continuousReceipts.Length == 14 &&
                continuousReceipts.Select(r => r.GuestId).Distinct().Count() == 14 &&
                booked.OrderBy(id => id).SequenceEqual(continuousReceipts.Select(r => r.GuestId).OrderBy(id => id)),
                "fourteen and only fourteen booked identities are paid exactly once");
            Require(continuousReceipts.All(r => r.Price > 0 && r.Price - r.Compensation > 0), "every actual stay produces a positive paid receipt");
            foreach (var cohort in continuousCohorts)
            {
                Require(cohort.CheckedIn.Count == cohort.Ids.Length && cohort.RoomArrivals.Count == cohort.Ids.Length &&
                    cohort.Departures.Count == cohort.Ids.Length && cohort.Stays.Count == cohort.Ids.Length &&
                    cohort.Stays.Values.All(g => g.ReceiptPosted && g.Agent.State == GuestAgentState.Left),
                    "cohort " + cohort.Day + " really checks in, reaches rooms, checks out and physically leaves");
                Require(cohort.WalkedMetres > 15 * cohort.Ids.Length, "cohort " + cohort.Day + " has observed actual body travel");
            }
            Require(continuousKeyHandoffs == 14 && continuousTurnovers >= 9 && continuousInspections >= continuousTurnovers &&
                continuousInspections <= continuousTurnovers + 1,
                "one staff supplies fourteen keys and prepares the reused rooms after real departures");
            long receiptsNet = continuousReceipts.Sum(r => (long)r.Price - r.Compensation);
            long operating = completedReports.Sum(r => (long)r.OperatingCost);
            long maintenance = completedReports.Sum(r => (long)r.MaintenanceSpend) + continuousModel.PeriodMaintenanceSpend;
            long capital = completedReports.Sum(r => (long)r.CapitalSpend) + continuousModel.PeriodCapitalSpend;
            long expected = continuousStartingCash + receiptsNet - operating - maintenance - capital;
            Require(expected == continuousModel.Economy.Cash, "cash conservation across paid receipts, reports, maintenance and capacity spending");
            Require(maintenance == (long)continuousPatches * session.Economy.CheapPatchCost,
                "maintenance spending is exactly the completed natural paid patch sequence count");
            Require(capital == (continuousUpgradeBought ? session.Economy.BoilerUpgradeCost : 0), "only the optional earned boiler upgrade contributes capital spending");
            foreach (var report in completedReports)
                Require((long)report.OpeningCash + report.Net == report.Cash, "report " + report.DayNumber + " cash arithmetic");
            Require(continuousMinimumCash >= 0, "production-cash strategy never requires invented funds or debt");
            finalCash = continuousModel.Economy.Cash; finalReputation = continuousModel.Economy.Reputation;
            continuousCashConserved = true;
            File.WriteAllText(Path.Combine(output, "continuous-final-snapshot.json"), JsonUtility.ToJson(continuousFinalSnapshot, true));
            facts.Add("Cash conservation: start=" + continuousStartingCash + " +netReceipts=" + receiptsNet + " -operating=" + operating +
                " -maintenance=" + maintenance + " -capital=" + capital + " =cash=" + expected + ". Reports never post checkout revenue a second time.");
            facts.Add("Optional earned upgrade purchased=" + continuousUpgradeBought + "; if false the production policy did not reach its affordable pre-D3-evening threshold while keeping bill450+patch200 buffer. This is an observation, not a guaranteed purchase objective.");
        }

        void WriteContinuousReport(string outcome)
        {
            // Cleanup also covers coroutine failures and watchdog exits. It never restarts the world.
            RestoreContinuousRepairController();
            var text = new StringBuilder();
            text.AppendLine("Built-player continuous hotel verification / SOLO / production settings");
            text.AppendLine("ApplicationVersion=" + Application.version + " UnityVersion=" + Application.unityVersion);
            text.AppendLine("Outcome=" + outcome + " Errors=" + errors + " ResetVerified=" + resetVerified);
            text.AppendLine("Mode=Solo SyntheticPads=" + ActiveActors);
            text.AppendLine("ContinuousOperationsVerified=" + continuousVerified + " CashConserved=" + continuousCashConserved +
                " ModelContinuity=" + continuousCashConserved + " RoomRegistryContinuity=" + continuousCashConserved + " ClockContinuity=" + continuousCashConserved + " EpochContinuity=" + continuousCashConserved);
            text.AppendLine("ObservationTarget=2160 CheckoutTailTarget=2280 ObservationElapsed=" + F(continuousObservationEnd) + " TailElapsed=" + F(continuousTailEnd));
            text.AppendLine("Graphics=" + SystemInfo.graphicsDeviceName + " Resolution=" + Screen.width + "x" + Screen.height +
                " RuntimeSeconds=" + F(Time.realtimeSinceStartup - began));
            text.AppendLine("OffscreenGPUAvailable=" + offscreenAvailable + " Captures=" + captures.Count);
            var reports = completedReports ?? continuousModel?.DayReports.ToArray() ?? Array.Empty<DayReport>();
            text.AppendLine("Reports=" + reports.Length + " UniquePaidStays=" + continuousReceipts.Select(r => r.GuestId).Distinct().Count() +
                " CurrentPeriodReceipts=" + (continuousFinalSnapshot?.Operations?.PeriodReceipts?.Length ?? 0) +
                " FinalCash=" + F(finalCash) + " FinalReputation=" + F(finalReputation) + " MinimumCash=" + continuousMinimumCash);
            text.AppendLine("StaffSlots=1 ModelKeyHandoffs=" + continuousKeyHandoffs + " ModelLinenTurnovers=" + continuousTurnovers +
                " VacantValveInspections=" + continuousInspections + " ModelPaidPatches=" + continuousPatches + " EarnedBoilerUpgrade=" + continuousUpgradeBought);
            text.AppendLine("NaturalBoilerFailures=" + continuousFailures + " NaturalCircuitTrips=" + continuousTrips +
                " PeakHeatingDemand=" + F(continuousPeakLoad) + " PeakLoadRatio=" + F(continuousPeakRatio) + " MinimumBoilerCondition=" + F(continuousMinimumCondition));
            foreach (var cohort in continuousCohorts)
            {
                var ids = cohort.Ids;
                var paid = continuousReceipts.Where(r => ids.Contains(r.GuestId)).ToArray();
                text.AppendLine("Cohort " + cohort.Day + ": booked=" + ids.Length + " checkedIn=" + cohort.CheckedIn.Count +
                    " actualRoomArrivals=" + cohort.RoomArrivals.Count + " actualDepartures=" + cohort.Departures.Count +
                    " paidStays=" + paid.Length + " gross=" + paid.Sum(r => r.Price) + " refunds=" + paid.Sum(r => r.Compensation) +
                    " net=" + paid.Sum(r => r.Price - r.Compensation) + " guestWalkMetres=" + F(cohort.WalkedMetres) +
                    " satisfaction=" + F(paid.Length == 0 ? 0 : paid.Average(r => r.Satisfaction)) +
                    " requestedServices=" + cohort.Stays.Values.Sum(g => g.Memory.ServicesRequested) +
                    " coldExposureSeconds=" + F(cohort.Stays.Values.Sum(g => g.ColdExposureSeconds)));
                if (outcome != "PASS") foreach (string state in cohort.LastStates.Values) text.AppendLine("Last observed: " + state);
            }
            foreach (var report in reports)
                text.AppendLine("Period " + report.DayNumber + ": receipts=" + report.Receipts.Count + " gross=" + report.Gross +
                    " refunds=" + report.Compensation + " operating=" + report.OperatingCost + " maintenance=" + report.MaintenanceSpend +
                    " capital=" + report.CapitalSpend + " net=" + report.Net + " openingCash=" + report.OpeningCash + " closingCash=" + report.Cash);
            foreach (var receipt in continuousReceipts)
                text.AppendLine("Receipt id=" + receipt.GuestId + " room=" + receipt.RoomId + " gross=" + receipt.Price + " refund=" + receipt.Compensation +
                    " net=" + (receipt.Price - receipt.Compensation) + " satisfaction=" + F(receipt.Satisfaction));
            text.AppendLine("CurrentStaffJob=" + (continuousJob ?? "idle") + " CurrentStaffPhase=" + continuousStaffPhase + " NextActionAt=" + F(continuousDue));
            text.AppendLine("Evidence limits: ordinary production GameSession8x ticks and actual guest navigation; timed model staff travel/key/linen/repair adapters, not physical carrying or human SOLO pacing. GPU images require separate visual review. No hardware performance, long-pause, AltTab, disk-save or multiplayer claim.");
            foreach (string fact in facts) text.AppendLine(fact);
            File.WriteAllText(Path.Combine(output, "runtime-verification.txt"), text.ToString());
        }

        static string F(float value) => value.ToString("F2", CultureInfo.InvariantCulture);
    }
}
#endif
