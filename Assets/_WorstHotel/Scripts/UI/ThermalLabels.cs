using System;
using System.Collections.Generic;
using System.Globalization;

namespace WorstHotel
{
    public enum ThermalMovement { Cooling, Steady, Warming }

    /// <summary>Plain-language projections of measured thermal state, never a second heat model.</summary>
    public static class ThermalLabels
    {
        public static ThermalMovement Movement(RoomThermalBreakdown thermal)
        {
            // Half the displayed thermometer's 0.1°C step avoids a perpetual microscopic
            // warming/cooling claim when the real room has practically reached its target.
            double difference = thermal.TargetTemperature - thermal.CurrentTemperature;
            return difference > .05 ? ThermalMovement.Warming : difference < -.05 ? ThermalMovement.Cooling : ThermalMovement.Steady;
        }

        public static string Trend(RoomThermalBreakdown thermal) => Movement(thermal).ToString();
        public static string Reading(RoomThermalBreakdown thermal) =>
            thermal.CurrentTemperature.ToString("F1", CultureInfo.InvariantCulture) + "°C · " + Trend(thermal);

        public static string Sources(RoomThermalBreakdown thermal, bool coldProne = false)
        {
            string central = thermal.RadiatorSetting == 0 ? "Radiator closed" :
                thermal.BoilerMaintenance ? "Central off: service" :
                thermal.HeatingContribution <= 0 ? "Central heat off" :
                thermal.BoilerFailed || thermal.BoilerOutput < .999f ? "Central heat reduced" : "Central heat on";
            return central + (thermal.SupplementalHeat > 0 ? " + heater" : "") +
                (coldProne ? " · cold-prone" : "");
        }

        public static bool ColdProne(RoomThermalBreakdown thermal, IEnumerable<RoomState> rooms)
        {
            if (rooms == null) return false;
            double loss = 0; int count = 0;
            foreach (var room in rooms)
                if (room != null) { loss += room.Profile.HeatLoss; count++; }
            // A factual comparison with this hotel's insulation, not a new room rule.
            return count > 1 && thermal.HeatLoss > loss / count + .0001;
        }

        public static float RadiatorWarmth01(RoomThermalBreakdown thermal, float fullHeatingContribution) =>
            fullHeatingContribution <= 0 ? 0 : (float)Math.Max(0, Math.Min(1,
                thermal.HeatingContribution / (double)fullHeatingContribution));
    }
}
