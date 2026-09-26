using System;

namespace WorstHotel
{
    public static class BoilerMaintenanceLabels
    {
        public static string Remaining(HotelSimulation simulation)
        {
            int minutes = (int)Math.Ceiling(simulation.Boiler.MaintenanceRemaining(simulation.Elapsed) * 1440d /
                simulation.Operations.SecondsPerDay);
            return minutes / 60 + "h " + (minutes % 60).ToString("D2") + "m hotel time";
        }
        public static string State(HotelSimulation simulation) => simulation.Boiler.MaintenanceInProgress ?
            "MAINTENANCE · HEAT OFF" : simulation.Boiler.Failed ? "FAILED" :
            simulation.Boiler.EmergencyPatchActive ? "PATCHED · reduced reliability" : "RUNNING";
    }
}
