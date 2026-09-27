#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace WorstHotel
{
    public sealed partial class LanDevelopmentVerification
    {
        const int ContinuousCapitalFixtureCash = 8000;

        IEnumerator RunContinuousCapitalHost()
        {
            var model = session.Simulation; var rooms = session.Rooms; long epoch = lan.Epoch;
            var boiler = model.Boiler; var circuit = model.Electrical.Find("B");
            var turnover = model.Housekeeping.Find(106); string dirtyId = turnover.DirtyLinenId;
            int dirtyGeneration = turnover.Generation, cashBefore = model.Economy.Cash;
            int capitalCost = session.Economy.BoilerUpgradeCost + session.Economy.ElectricalUpgradeCost;
            float conditionBefore = boiler.Condition, ratedBefore = boiler.RatedCapacity;
            float circuitCapacityBefore = circuit.Capacity, otherCapacityBefore = model.Electrical.Find("A").Capacity;
            Require(cashBefore == ContinuousCapitalFixtureCash - session.Economy.DailyOperatingCost &&
                boiler.Failed && circuit.Tripped && model.DayReports.Count == 1, "funded capital test follows unchanged first-boundary assertions");
            WriteStage("continuous-capital-ready");
            yield return Until(() => boiler.CapacityUpgradePurchased && model.Electrical.UpgradedCircuitId == "B", 50,
                "actual client controller purchases both capacity categories through NGO");
            Require(boiler.Failed && boiler.Condition == conditionBefore && circuit.Tripped && !circuit.HasPower,
                "paid upgrades neither repair the failed boiler nor reset the tripped circuit");
            Require(Mathf.Approximately(boiler.RatedCapacity, ratedBefore * session.BoilerSettings.Capacity.CapacityUpgradeMultiplier) &&
                Mathf.Approximately(circuit.Capacity, circuitCapacityBefore + model.Electrical.Settings.CapacityUpgradeAmount) &&
                model.Electrical.Find("A").Capacity == otherCapacityBefore, "only purchased capacities change");
            Require(model.PeriodCapitalSpend == capitalCost && model.Economy.Cash == cashBefore - capitalCost,
                "host debits each actual capacity purchase exactly once");
            WriteStage("continuous-capital-installed");
            yield return Stage("continuous-capital-observed", 15);

            WriteStage("continuous-duplicate-boiler-ready");
            yield return Stage("continuous-duplicate-boiler-sent", 12);
            yield return Until(() => session.LastMessage == "The boiler capacity upgrade is already installed.", 8,
                "normal host command rejects duplicate boiler purchase");
            Require(model.Economy.Cash == cashBefore - capitalCost && model.PeriodCapitalSpend == capitalCost,
                "rejected duplicate boiler purchase does not charge");
            WriteStage("continuous-duplicate-boiler-rejected");
            yield return Stage("continuous-other-circuit-sent", 12);
            yield return Until(() => session.LastMessage == "The hotel's electrical capacity upgrade is already installed.", 8,
                "normal host command rejects purchasing the other branch");
            Require(model.Electrical.UpgradedCircuitId == "B" && model.Electrical.Find("A").Capacity == otherCapacityBefore &&
                model.Economy.Cash == cashBefore - capitalCost && model.PeriodCapitalSpend == capitalCost,
                "one electrical purchase remains enforced without an extra debit");
            WriteStage("continuous-other-circuit-rejected");

            yield return Until(() => boiler.MaintenanceInProgress, 40, "actual remote controller starts paid proper maintenance");
            float endsAt = boiler.MaintenanceEndsAt;
            int afterEquipment = cashBefore - capitalCost - session.Economy.ProperRepairCost;
            Require(boiler.Failed && boiler.HeatingOutput == 0 && model.PeriodMaintenanceSpend == session.Economy.ProperRepairCost &&
                model.Economy.Cash == afterEquipment && boiler.MaintenanceRemaining(model.Elapsed) > 0,
                "paid maintenance has real downtime and one host debit; starting it does not prematurely fix failure");
            WriteStage("continuous-maintenance-started");
            yield return Stage("continuous-maintenance-observed", 15);
            yield return Stage("continuous-duplicate-maintenance-sent", 12);
            yield return Until(() => session.LastMessage == "The boiler is already offline for maintenance.", 8,
                "normal host command rejects a duplicate active maintenance job");
            Require(boiler.MaintenanceEndsAt == endsAt && model.Economy.Cash == afterEquipment &&
                model.PeriodMaintenanceSpend == session.Economy.ProperRepairCost, "duplicate maintenance changes neither deadline nor cash");
            WriteStage("continuous-duplicate-maintenance-rejected");
            yield return Stage("continuous-maintenance-advance-ready", 10);
            Require(boiler.MaintenanceInProgress, "real job is still active before labelled deadline advance");
            session.AdvanceTime(Mathf.Max(.1f, endsAt - model.Elapsed + .2f));
            Require(!boiler.MaintenanceInProgress && !boiler.Failed && !boiler.EmergencyPatchActive &&
                boiler.Condition >= session.BoilerSettings.Capacity.ProperMaintenanceCondition - .1f &&
                boiler.CapacityUpgradePurchased && circuit.Tripped, "actual deadline restores condition while installed capacity and branch trip persist");
            Require(model.Economy.Cash == afterEquipment, "maintenance completion never debits again");
            facts.Add("DIAGNOSTIC CLOCK: bounded advance to paid maintenance deadline; actual model completion and replicated condition are checked, not elapsed human work time.");
            WriteStage("continuous-maintenance-completed");
            yield return Stage("continuous-maintenance-completion-observed", 15);

            for (int sequence = 2; sequence <= 3; sequence++)
            {
                float from = model.Elapsed, target = model.NextReportAt + .2f;
                Require(target > from && target - from <= session.config.hotelDaySeconds, "each accounting advance is bounded to one production hotel day");
                AdvanceContinuousDiagnosticTo(target, "accounting boundary " + sequence);
                Require(session.Simulation == model && session.Rooms == rooms && lan.Epoch == epoch && session.Phase == DayPhase.Service &&
                    model.ReportSequence == sequence && model.DayReports.Count == sequence,
                    "accounting boundary preserves host identities and active hotel: expectedReport=" + sequence +
                    " actualReport=" + model.ReportSequence + " count=" + model.DayReports.Count + " clock=" + model.Elapsed.ToString("R") +
                    " target=" + target.ToString("R") + " nextReport=" + model.NextReportAt.ToString("R") + " phase=" + session.Phase +
                    " modelSame=" + ReferenceEquals(session.Simulation, model) + " roomsSame=" + ReferenceEquals(session.Rooms, rooms) +
                    " expectedEpoch=" + epoch + " actualEpoch=" + lan.Epoch);
                var report = model.LastReport;
                Require(report.DayNumber == sequence && report.MaintenanceSpend == (sequence == 2 ? session.Economy.ProperRepairCost : 0) &&
                    report.CapitalSpend == (sequence == 2 ? capitalCost : 0), "each report records equipment spend in its original period only");
                Require(model.DayReports.Sum(value => value.MaintenanceSpend) == session.Economy.ProperRepairCost &&
                    model.DayReports.Sum(value => value.CapitalSpend) == capitalCost && model.PeriodMaintenanceSpend == 0 && model.PeriodCapitalSpend == 0,
                    "historical purchases are not reapplied at subsequent boundaries");
                Require(model.Economy.Cash == ContinuousCapitalFixtureCash + model.DayReports.Sum(value => value.Receipts.Sum(receipt => receipt.Net)) -
                    sequence * session.Economy.DailyOperatingCost - capitalCost - session.Economy.ProperRepairCost,
                    "cash reconciles with initial diagnostic funds, actual receipts, three distinct bills and one-time equipment payments");
                Require(boiler.CapacityUpgradePurchased && model.Electrical.UpgradedCircuitId == "B" && circuit.Tripped &&
                    rooms.Single(room => room.Profile.Id == 106).Cleanliness == Cleanliness.Dirty && model.Housekeeping.Find(106) == turnover &&
                    turnover.DirtyLinenId == dirtyId && turnover.Generation == dirtyGeneration && model.Keys.Find(101).Location == RoomKeyLocation.OnRack,
                    "upgrades, untouched trip, dirty turnover identity and unissued room key survive later dates");
                facts.Add("DIAGNOSTIC ACCOUNTING BOUNDARY " + sequence + ": clock " + from.ToString("F2") + " -> " + model.Elapsed.ToString("F2") +
                    "; Day=" + session.Day + " cash=" + model.Economy.Cash + ". Production guest travel may continue, but no guest check-in or three played LAN days is claimed.");
                WriteStage("continuous-report-" + sequence + "-ready");
                yield return Stage("continuous-report-" + sequence + "-observed", 20);
            }
            facts.Add("RemotePaidUpgrades=True UpgradeDoesNotRepair=True DuplicateCapitalRejected=True OtherBranchRejected=True RemotePaidMaintenance=True MaintenanceDowntime=True DuplicateMaintenanceRejected=True MaintenanceRestored=True ThreeAccountingBoundaries=True ContinuousCapitalVerified=True");
        }

        void AdvanceContinuousDiagnosticTo(float target, string label)
        {
            var model = session.Simulation; var rooms = session.Rooms; long epoch = lan.Epoch;
            float before = model.Elapsed, requested = target - before;
            Require(requested > 0 && requested <= session.config.hotelDaySeconds,
                label + " requests at most one production hotel day");
            facts.Add("DIAGNOSTIC ADVANCE " + label + ": before=" + before.ToString("R") + " target=" + target.ToString("R") +
                " requested=" + requested.ToString("R") + " tickRate=" + session.Settings.TickRate.ToString("R"));
            session.AdvanceTime(requested);
            // AdvanceTime consumes a float duration budget in fixed ticks; independently
            // accumulated float hotel time can end slightly short of an absolute target.
            // Observe that clock and permit only a small, bounded diagnostic remainder.
            // Never compensate for changed identities, a phase transition or stalled time.
            for (int correction = 0; ; correction++)
            {
                facts.Add("DIAGNOSTIC ADVANCE RESULT " + label + ": correction=" + correction + " actual=" + model.Elapsed.ToString("R") +
                    " shortfall=" + (target - model.Elapsed).ToString("R") + " report=" + model.ReportSequence +
                    " count=" + model.DayReports.Count + " nextReport=" + model.NextReportAt.ToString("R") +
                    " phase=" + session.Phase + " modelSame=" + ReferenceEquals(session.Simulation, model) +
                    " roomsSame=" + ReferenceEquals(session.Rooms, rooms) + " expectedEpoch=" + epoch + " actualEpoch=" + lan.Epoch +
                    " message=" + session.LastMessage);
                Require(ReferenceEquals(session.Simulation, model) && ReferenceEquals(session.Rooms, rooms) && lan.Epoch == epoch &&
                    session.Phase == DayPhase.Service && model.Elapsed > before,
                    label + " diagnostic advance preserves identity and advances the active clock");
                if (model.Elapsed >= target) break;
                float remainder = target - model.Elapsed;
                Require(correction < 3 && remainder <= 1f,
                    label + " shortfall exceeds bounded floating-point correction: " + remainder.ToString("R"));
                before = model.Elapsed;
                session.AdvanceTime(Mathf.Max(1f / session.Settings.TickRate, remainder));
            }
        }

        IEnumerator RunContinuousCapitalClient()
        {
            var mirror = session.Simulation; var rooms = session.Rooms; long epoch = lan.Epoch;
            yield return Stage("continuous-capital-ready", 10);
            int cashBefore = mirror.Economy.Cash;
            int capitalCost = session.Economy.BoilerUpgradeCost + session.Economy.ElectricalUpgradeCost;
            float ratedBefore = mirror.Boiler.RatedCapacity, conditionBefore = mirror.Boiler.Condition;
            float circuitBefore = mirror.Electrical.Find("B").Capacity;
            yield return ContinuousChoose("Back to bookings");
            yield return ContinuousChoose("Back to operations");
            yield return ContinuousChoose("Capacity upgrades");
            if (capture) yield return Capture("client-continuous-capital-before");
            yield return ContinuousChoose("Buy boiler capacity upgrade");
            yield return Until(() => mirror.Boiler.CapacityUpgradePurchased, 8, "authoritative boiler purchase returns to readonly mirror");
            Require(mirror.Boiler.Failed && mirror.Boiler.Condition == conditionBefore &&
                Mathf.Approximately(mirror.Boiler.RatedCapacity, ratedBefore * session.BoilerSettings.Capacity.CapacityUpgradeMultiplier),
                "client sees actual extra boiler capacity without a repair");
            yield return ContinuousChoose("Upgrade circuit B");
            yield return Until(() => mirror.Electrical.UpgradedCircuitId == "B" && mirror.PeriodCapitalSpend == capitalCost, 8,
                "authoritative branch purchase and full capital ledger return");
            Require(mirror.Electrical.Find("B").Tripped && !mirror.Electrical.Find("B").HasPower &&
                Mathf.Approximately(mirror.Electrical.Find("B").Capacity, circuitBefore + mirror.Electrical.Settings.CapacityUpgradeAmount) &&
                mirror.Economy.Cash == cashBefore - capitalCost, "client sees one-time debits and no automatic breaker reset");
            if (capture) yield return Capture("client-continuous-capital-installed");
            yield return Stage("continuous-capital-installed", 10);
            WriteStage("continuous-capital-observed");
            yield return Stage("continuous-duplicate-boiler-ready", 10);
            // Explicit negative requests use the ordinary authenticated command bridge. They
            // do not bypass a disabled UI control, inject an RPC or mutate the local mirror.
            session.PurchaseBoilerUpgrade(1); WriteStage("continuous-duplicate-boiler-sent");
            yield return Stage("continuous-duplicate-boiler-rejected", 12);
            session.PurchaseElectricalUpgrade(1, "A"); WriteStage("continuous-other-circuit-sent");
            yield return Stage("continuous-other-circuit-rejected", 12);
            Require(mirror.Economy.Cash == cashBefore - capitalCost && mirror.Electrical.UpgradedCircuitId == "B", "negative capital requests cannot alter replica balances or chosen branch");
            yield return ContinuousChoose("Back to operations");
            yield return ContinuousChoose("Boiler maintenance");
            yield return ContinuousChoose("Start proper maintenance");
            yield return Until(() => mirror.Boiler.MaintenanceInProgress && mirror.PeriodMaintenanceSpend == session.Economy.ProperRepairCost, 8,
                "real maintenance command replicates its active job and payment");
            float endsAt = mirror.Boiler.MaintenanceEndsAt;
            int afterEquipment = cashBefore - capitalCost - session.Economy.ProperRepairCost;
            Require(mirror.Boiler.Failed && mirror.Boiler.HeatingOutput == 0 && mirror.Boiler.MaintenanceRemaining(mirror.Elapsed) > 0 &&
                mirror.Economy.Cash == afterEquipment, "client sees zero heat during paid downtime, not premature repair");
            if (capture) yield return Capture("client-continuous-maintenance-active");
            yield return Stage("continuous-maintenance-started", 10);
            WriteStage("continuous-maintenance-observed");
            session.BeginBoilerMaintenance(1); WriteStage("continuous-duplicate-maintenance-sent");
            yield return Stage("continuous-duplicate-maintenance-rejected", 12);
            Require(mirror.Boiler.MaintenanceEndsAt == endsAt && mirror.Economy.Cash == afterEquipment,
                "rejected duplicate keeps the replicated original deadline and cash");
            WriteStage("continuous-maintenance-advance-ready");
            yield return Stage("continuous-maintenance-completed", 12);
            yield return Until(() => !mirror.Boiler.MaintenanceInProgress && !mirror.Boiler.Failed, 8,
                "completed maintenance returns through a normal model snapshot");
            Require(mirror.Boiler.Condition >= session.BoilerSettings.Capacity.ProperMaintenanceCondition - 1 &&
                !mirror.Boiler.EmergencyPatchActive && mirror.Boiler.CapacityUpgradePurchased && mirror.Electrical.Find("B").Tripped &&
                mirror.Economy.Cash == afterEquipment, "client sees restored condition with upgrade and unresolved branch retained");
            if (capture) yield return Capture("client-continuous-maintenance-complete");
            WriteStage("continuous-maintenance-completion-observed");
            yield return ContinuousChoose("Back to operations");
            yield return ContinuousChoose("Daily reports");
            for (int sequence = 2; sequence <= 3; sequence++)
            {
                yield return Stage("continuous-report-" + sequence + "-ready", 18);
                yield return Until(() => mirror.ReportSequence == sequence && session.Reports.Count == sequence, 12, "later accounting report replicates");
                Require(session.Simulation == mirror && session.Rooms == rooms && lan.Epoch == epoch && session.Phase == DayPhase.Service &&
                    ManagementUI.Instance.IsOperationsOpen && ManagementUI.Instance.Owner == 1, "three accounting boundaries preserve client identities, live menu and connection");
                Require(session.Reports.Sum(value => value.MaintenanceSpend) == session.Economy.ProperRepairCost &&
                    session.Reports.Sum(value => value.CapitalSpend) == capitalCost && mirror.PeriodMaintenanceSpend == 0 && mirror.PeriodCapitalSpend == 0,
                    "replicated reports retain equipment expenses exactly once");
                Require(mirror.Economy.Cash == ContinuousCapitalFixtureCash + session.Reports.Sum(value => value.Receipts.Sum(receipt => receipt.Net)) -
                    sequence * session.Economy.DailyOperatingCost - capitalCost - session.Economy.ProperRepairCost &&
                    mirror.Boiler.CapacityUpgradePurchased && mirror.Electrical.UpgradedCircuitId == "B" && mirror.Electrical.Find("B").Tripped &&
                    rooms.Single(room => room.Profile.Id == 106).Cleanliness == Cleanliness.Dirty,
                    "replica cash reconciles while installed capacities and physical problems carry across dates");
                yield return ContinuousChoose("Operating report " + sequence);
                if (capture) yield return Capture("client-continuous-report-" + sequence);
                yield return ContinuousChoose("Back to reports");
                WriteStage("continuous-report-" + sequence + "-observed");
            }
            facts.Add("RemotePaidUpgrades=True UpgradeDoesNotRepair=True DuplicateCapitalRejected=True OtherBranchRejected=True RemotePaidMaintenance=True MaintenanceDowntime=True DuplicateMaintenanceRejected=True MaintenanceRestored=True ThreeAccountingBoundaries=True ContinuousCapitalVerified=True");
            facts.Add("ThreeAccountingBoundaries ends after D4 06:00 from D1 08:00; bounded host diagnostic advances, not 72 hours of human play or three paid guest cohorts. All equipment choices used actual client controller through normal NGO authorization; duplicate attempts used labelled ordinary session commands.");
        }
    }
}
#endif
