#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

namespace WorstHotel
{
    public sealed partial class DeveloperPanel
    {
        void DrawHeatingCapacityDebug()
        {
            var simulation = Session.Simulation; var boiler = simulation.Boiler;
            if (!boiler.CapacityModelEnabled) return;
            GUILayout.Label("HEATING CAPACITY / REAL ROOM DEMAND (u)", heading);
            GUILayout.Label(BoilerMaintenanceLabels.State(simulation) + (boiler.MaintenanceInProgress ?
                " / " + BoilerMaintenanceLabels.Remaining(simulation) + " remaining" : "") + " / maintenance spend $" + simulation.PeriodMaintenanceSpend +
                " / boiler upgrade " + boiler.CapacityUpgradePurchased + " / capital spend $" + simulation.PeriodCapitalSpend, body);
            GUILayout.Label("Rated " + boiler.RatedCapacity.ToString("F2") + " / effective " + boiler.EffectiveCapacity.ToString("F2") +
                " / demand " + boiler.Load.ToString("F2") + " / reserve " + CapacityLabels.Reserve(boiler.Reserve) + " u" +
                "\nLoad " + CapacityLabels.Percent(boiler.LoadRatio) + " / stress " + CapacityLabels.Percent(boiler.Stress01) + " / " +
                CapacityLabels.Band(boiler.CapacityBand) + "\nPressure " + boiler.Pressure.ToString("F1") + " is a separate repair reading." +
                (boiler.LoadOverride.HasValue ? "\nDIAGNOSTIC OVERRIDE: demand above differs from the actual room sum below." : ""), body);
            float total = 0;
            foreach (var demand in simulation.HeatingDemands)
            {
                total += demand.Total;
                GUILayout.Label("Room " + demand.RoomId + " / " + (demand.GuestId ?? "vacant") +
                    " · space heat " + demand.SpaceHeating.ToString("F2") + " + hot water " + demand.HotWater.ToString("F2") +
                    " = " + demand.Total.ToString("F2"), body);
            }
            GUILayout.Label("Actual attributed total " + total.ToString("F2"), body);
        }
    }
}
#endif
