using System;

namespace WorstHotel
{
    public static class BoilerMaintenanceLabels
    {
        public static string ServiceName(BoilerServiceKind kind) => kind == BoilerServiceKind.Basic ? "Basic Service" :
            kind == BoilerServiceKind.Full ? "Full Service" : "Service";

        public static string Condition(BoilerSystem boiler) => boiler.Condition >= 80 ? "Good" : boiler.Condition >= 50 ? "Worn" :
            boiler.Condition >= 25 ? "Poor" : "Critical";

        public static string Load(BoilerSystem boiler, BoilerCapacitySettings tuning) => boiler.MaintenanceInProgress ? "Offline" :
            boiler.LoadRatio > 1 ? "Overloaded" : boiler.LoadRatio >= tuning.StrainedLoadRatio ? "Strained" :
            boiler.LoadRatio >= tuning.BusyLoadRatio ? "High" : "Comfortable";

        public static string Stress(BoilerSystem boiler) => boiler.Stress01 >= .8f ? "Critical" : boiler.Stress01 >= .5f ? "High" :
            boiler.Stress01 >= .15f ? "Rising" : "Low";

        public static string Recommendation(BoilerSystem boiler) => boiler.MaintenanceInProgress ? "Work in progress" :
            boiler.Failed || boiler.Condition < 35 || boiler.Stress01 >= .8f ? "Urgent" :
            boiler.Condition < 70 || boiler.Stress01 >= .25f || boiler.EmergencyPatchActive ? "Recommended" : "None";

        public static string Remaining(HotelSimulation simulation)
        {
            int minutes = (int)Math.Ceiling(simulation.Boiler.MaintenanceRemaining(simulation.Elapsed) * 1440d /
                simulation.Operations.SecondsPerDay);
            return minutes / 60 + "h " + (minutes % 60).ToString("D2") + "m hotel time";
        }
        public static string State(HotelSimulation simulation) => simulation.Boiler.MaintenanceInProgress ?
            ServiceName(simulation.Boiler.ActiveServiceKind).ToUpperInvariant() + " · HEAT OFF" : simulation.Boiler.Failed ? "FAILED" :
            simulation.Boiler.EmergencyPatchActive ? "PATCHED · reduced reliability" : "RUNNING";
    }
}
