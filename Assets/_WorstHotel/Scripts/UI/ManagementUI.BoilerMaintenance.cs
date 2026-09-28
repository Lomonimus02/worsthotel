using System;
using UnityEngine;
using static WorstHotel.HotelTheme;

namespace WorstHotel
{
    public sealed partial class ManagementUI
    {
        public void OpenBoilerInspection(int actorId) => OpenBook(actorId, HotelBook.BoilerManual);

        void UpdateOperationsMaintenance()
        {
            var simulation = Session.Simulation; var boiler = simulation.Boiler;
            if (boiler.MaintenanceInProgress)
            {
                AddOperationsChoice(42, 604, 705, 43, "Maintenance in progress · " +
                    BoilerMaintenanceLabels.ServiceName(boiler.ActiveServiceKind) + " · heating off", () => { }, false);
                return;
            }
            var tuning = Session.BoilerSettings.Capacity;
            int revision = boiler.MaintenanceRevision;
            // Mirrors display obvious eligibility. The host rechecks authoritative state
            // both at selection and after the actual physical setup is completed.
            float basicCondition = Math.Max(boiler.Condition, Math.Min(tuning.BasicMaintenanceConditionCap,
                boiler.Condition + tuning.BasicMaintenanceConditionGain));
            float basicStress = Math.Max(0, boiler.Stress01 - tuning.BasicMaintenanceStressReduction);
            bool basicBenefit = basicCondition > boiler.Condition || basicStress < boiler.Stress01;
            AddOperationsChoice(42, 604, 342, 43, "Select Basic Service · $" + Session.Economy.BasicMaintenanceCost,
                () => Session.SelectBoilerService(owner, BoilerServiceKind.Basic, revision),
                !boiler.Failed && basicBenefit && simulation.Economy.Cash >= Session.Economy.BasicMaintenanceCost);
            AddOperationsChoice(405, 604, 342, 43, "Select Full Service · $" + Session.Economy.ProperRepairCost,
                () => Session.SelectBoilerService(owner, BoilerServiceKind.Full, revision),
                simulation.Economy.Cash >= Session.Economy.ProperRepairCost);
        }

        void DrawOperationsMaintenance()
        {
            var simulation = Session.Simulation; var boiler = simulation.Boiler;
            var tuning = Session.BoilerSettings.Capacity;
            Label(new Rect(42, 195, 705, 99), BoilerMaintenanceLabels.State(simulation) + " · " +
                BoilerMaintenanceLabels.Condition(boiler) + " condition (" + boiler.Condition.ToString("F0") + "%)\n" +
                "Load: " + BoilerMaintenanceLabels.Load(boiler, tuning) + " · Stress: " + BoilerMaintenanceLabels.Stress(boiler) +
                " · Service: " + BoilerMaintenanceLabels.Recommendation(boiler) + "\n" +
                (boiler.MaintenanceInProgress ? "Ready " + GuestLabels.HotelMoment(simulation, boiler.MaintenanceEndsAt) + " · " +
                    BoilerMaintenanceLabels.Remaining(simulation) + " remaining" : "Demand " + boiler.Load.ToString("F2") + " / " +
                    boiler.EffectiveCapacity.ToString("F2") + " available (rated " + boiler.RatedCapacity.ToString("F2") + ") · stress " + CapacityLabels.Percent(boiler.Stress01)),
                Body, boiler.MaintenanceInProgress || boiler.Failed ? Wine : Teal);
            Label(new Rect(42, 294, 705, 29), "Failure only: use emergency controls for a $" + Session.Economy.CheapPatchCost + " patch. Service does not upgrade capacity.", Small, Muted);

            float basicCondition = Math.Max(boiler.Condition, Math.Min(tuning.BasicMaintenanceConditionCap,
                boiler.Condition + tuning.BasicMaintenanceConditionGain));
            float basicStress = Math.Max(0, boiler.Stress01 - tuning.BasicMaintenanceStressReduction);
            Fill(new Rect(42, 332, 705, 118), LightPaper);
            Label(new Rect(58, 341, 673, 31), "BASIC SERVICE · $" + Session.Economy.BasicMaintenanceCost, Heading);
            Label(new Rect(58, 376, 673, 69), "Heating off for " + tuning.BasicMaintenanceHours.ToString("0.##") + " hotel hours. Working boiler only.\n" +
                "After work: condition " + boiler.Condition.ToString("F0") + " → " + basicCondition.ToString("F0") + "% · stress " +
                CapacityLabels.Percent(boiler.Stress01) + " → " + CapacityLabels.Percent(basicStress) + ".\n" +
                "Partial care; any emergency patch penalty remains.", Small, Muted);

            Fill(new Rect(42, 466, 705, 118), LightPaper);
            Label(new Rect(58, 475, 673, 31), "FULL SERVICE · $" + Session.Economy.ProperRepairCost, Heading);
            Label(new Rect(58, 510, 673, 69), "Heating off for " + tuning.MaintenanceHours.ToString("0.##") + " hotel hours. Rooms may cool during the work.\n" +
                "Restores at least " + tuning.ProperMaintenanceCondition.ToString("F0") + "% condition; clears stress and the patch penalty.\n" +
                "Plan a low-demand window. Paid once when physical setup finishes.", Small, Muted);

            Label(new Rect(42, 655, 705, 39), boiler.MaintenanceInProgress ?
                "Service is already underway. This period's maintenance spend: $" + simulation.PeriodMaintenanceSpend :
                "Selection costs nothing. Close this menu; hold the inspection plate below the boiler gauge to start. Q / X there: inspect again.", Small, Muted);
        }
    }
}
