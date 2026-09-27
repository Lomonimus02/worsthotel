namespace WorstHotel
{
    public sealed partial class ElectricalSystem
    {
        /// <summary>The hotel has one electrical upgrade purchase, installed on one existing branch.</summary>
        public string UpgradedCircuitId { get; private set; } = string.Empty;

        internal CommandResult CanInstallCapacityUpgrade(string circuitId)
        {
            if (ReadOnlyMirror) return CommandResult.Fail(HotelSimulation.MirrorMessage);
            if (!ContinuousStress) return CommandResult.Fail("Capacity upgrades require continuous hotel operations.");
            if (Find(circuitId) == null) return CommandResult.Fail("Unknown electrical circuit.");
            if (!string.IsNullOrEmpty(UpgradedCircuitId)) return CommandResult.Fail("The hotel's electrical capacity upgrade is already installed.");
            double upgraded = (double)Settings.CircuitCapacity + Settings.CapacityUpgradeAmount;
            if (upgraded > float.MaxValue || (float)upgraded <= Settings.CircuitCapacity)
                return CommandResult.Fail("The configured electrical upgrade cannot represent a larger finite capacity.");
            return CommandResult.Ok();
        }

        internal CommandResult InstallCapacityUpgrade(string circuitId)
        {
            var allowed = CanInstallCapacityUpgrade(circuitId);
            if (!allowed.Success) return allowed;
            UpgradedCircuitId = circuitId;
            // Existing consumers, thermal stress and a tripped breaker's power state are unchanged.
            Changed?.Invoke(Find(circuitId), "capacity upgrade installed");
            return CommandResult.Ok("Circuit " + circuitId + " capacity upgraded. A tripped breaker still needs its physical reset.");
        }
    }
}
