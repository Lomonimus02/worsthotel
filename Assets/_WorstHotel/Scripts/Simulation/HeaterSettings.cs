using System;

namespace WorstHotel
{
    public sealed class HeaterSettings
    {
        /// <summary>Supplement added to the same room equilibrium temperature model, in degrees Celsius.</summary>
        public float HeatOutput { get; }
        public float ElectricalLoad { get; }
        public HeaterSettings(float heatOutput = 14, float electricalLoad = 2)
        {
            if (!Number.IsFinite(heatOutput) || heatOutput <= 0 || !Number.IsFinite(electricalLoad) || electricalLoad <= 0)
                throw new ArgumentException("A portable heater needs finite positive heat output and electrical demand.");
            HeatOutput = heatOutput; ElectricalLoad = electricalLoad;
        }
    }
}
