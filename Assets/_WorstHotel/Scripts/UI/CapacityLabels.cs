namespace WorstHotel
{
    /// <summary>Presentation of measured capacity. Pressure and physical failure remain separate facts.</summary>
    public static class CapacityLabels
    {
        public static string Band(CapacityBand band) => band == CapacityBand.Comfortable ? "COMFORTABLE" :
            band == CapacityBand.Strained ? "STRAINED" : band == CapacityBand.Overloaded ? "OVERLOADED" : "CRITICAL";
        public static string Percent(float ratio) => ratio > 9.99f ? ">999%" : (ratio * 100).ToString("F0") + "%";
        public static string Reserve(float reserve) => (reserve >= 0 ? "+" : "") + reserve.ToString("F2");
        public static string BoilerReadout(BoilerSystem boiler) =>
            (boiler.LoadOverride.HasValue ? "OVERRIDE " : "DEMAND ") + boiler.Load.ToString("F2") + " / " + boiler.EffectiveCapacity.ToString("F2") + " u EFFECTIVE\n" +
            "RATED " + boiler.RatedCapacity.ToString("F2") + " u · RES " + Reserve(boiler.Reserve) + " u\n" +
            (boiler.Failed ? "FAILED" : Band(boiler.CapacityBand)) + " · " + Percent(boiler.LoadRatio) + " LOAD · " + Percent(boiler.Stress01) + " STRESS";
        public static string CircuitReadout(ElectricalCircuit circuit) =>
            (circuit.LoadOverride.HasValue ? "OVERRIDE " : "REQUESTED ") + circuit.RequestedLoad.ToString("F2") + " / " + circuit.Capacity.ToString("F2") + " u\n" +
            (circuit.LoadOverride.HasValue ? "ACTUAL REQUEST " + circuit.ActualRequestedLoad.ToString("F2") + " u\n" : "") +
            "DELIVERED " + circuit.ActualDeliveredLoad.ToString("F2") + " u · RES " + Reserve(circuit.Reserve) + " u\n" +
            Percent(circuit.LoadRatio) + " LOAD · " + Percent(circuit.Stress01) + " STRESS\n" +
            (circuit.Tripped ? "TRIPPED · POWER OFF" : Band(circuit.CapacityBand));
    }
}
