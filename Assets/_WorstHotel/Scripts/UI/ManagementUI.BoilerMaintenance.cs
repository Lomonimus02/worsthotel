using UnityEngine;
using static WorstHotel.HotelTheme;

namespace WorstHotel
{
    public sealed partial class ManagementUI
    {
        void UpdateOperationsMaintenance()
        {
            var simulation = Session.Simulation; var boiler = simulation.Boiler;
            bool affordable = simulation.Economy.Cash >= Session.Economy.ProperRepairCost;
            // A replica may propose the action; only the host applies the command guards.
            bool available = !boiler.MaintenanceInProgress;
            AddOperationsChoice(42, 640, 705, 43, boiler.MaintenanceInProgress ? "Maintenance in progress · heating off" :
                !affordable ? "Proper maintenance · insufficient cash" : "Start proper maintenance · $" + Session.Economy.ProperRepairCost,
                () => Session.BeginBoilerMaintenance(owner), available && affordable);
        }

        void DrawOperationsMaintenance()
        {
            var simulation = Session.Simulation; var boiler = simulation.Boiler;
            var tuning = Session.BoilerSettings.Capacity;
            Label(new Rect(42, 197, 705, 75), BoilerMaintenanceLabels.State(simulation) + " · condition " + boiler.Condition.ToString("F0") + "%\n" +
                (boiler.MaintenanceInProgress ? "Ready " + GuestLabels.HotelMoment(simulation, boiler.MaintenanceEndsAt) + " · " + BoilerMaintenanceLabels.Remaining(simulation) + " remaining" :
                    "Load " + CapacityLabels.Percent(boiler.LoadRatio) + " · reserve " + CapacityLabels.Reserve(boiler.Reserve) + " u · stress " + CapacityLabels.Percent(boiler.Stress01)),
                Body, boiler.MaintenanceInProgress || boiler.Failed ? Wine : Teal);
            Fill(new Rect(42, 286, 705, 149), LightPaper);
            Label(new Rect(58, 297, 673, 31), "EMERGENCY PATCH · $" + Session.Economy.CheapPatchCost, Heading);
            Label(new Rect(58, 340, 673, 85), "Use the physical boiler controls after a failure. Restart restores " + tuning.EmergencyPatchCondition.ToString("F0") +
                "% condition.\nThe patch leaves overload stress building " + tuning.EmergencyPatchStressMultiplier.ToString("0.##") +
                "× faster until proper maintenance. Rooms still need time to warm.", Small, Muted);
            Fill(new Rect(42, 453, 705, 173), LightPaper);
            Label(new Rect(58, 464, 673, 31), "PROPER MAINTENANCE · $" + Session.Economy.ProperRepairCost, Heading);
            Label(new Rect(58, 506, 673, 107), "Heating shuts down for " + tuning.MaintenanceHours.ToString("0.##") +
                " hotel hours. Guests can stay and rooms may cool during the work.\nRestores " + tuning.ProperMaintenanceCondition.ToString("F0") +
                "% condition, removes the patch penalty and clears stress. Capacity is not upgraded.\nPaid once at the start · this period's maintenance spend $" + simulation.PeriodMaintenanceSpend,
                Small, Muted);
        }
    }
}
