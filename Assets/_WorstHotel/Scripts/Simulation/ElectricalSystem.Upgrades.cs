namespace WorstHotel
{
    public sealed partial class ElectricalSystem
    {
        private readonly System.Collections.Generic.HashSet<string> upgradedCircuitIds =
            new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);

        /// <summary>Each existing branch can receive one permanent capacity purchase.</summary>
        public bool IsCapacityUpgraded(string circuitId) => circuitId != null && upgradedCircuitIds.Contains(circuitId);

        internal CommandResult CanInstallCapacityUpgrade(string circuitId)
        {
            if (ReadOnlyMirror) return CommandResult.Fail(HotelSimulation.MirrorMessage);
            if (!ContinuousStress) return CommandResult.Fail("Capacity upgrades require continuous hotel operations.");
            if (Find(circuitId) == null) return CommandResult.Fail("Unknown electrical circuit.");
            if (IsCapacityUpgraded(circuitId)) return CommandResult.Fail("This circuit's capacity upgrade is already installed.");
            double upgraded = (double)Settings.CircuitCapacity + Settings.CapacityUpgradeAmount;
            if (upgraded > float.MaxValue || (float)upgraded <= Settings.CircuitCapacity)
                return CommandResult.Fail("The configured electrical upgrade cannot represent a larger finite capacity.");
            return CommandResult.Ok();
        }

        internal CommandResult InstallCapacityUpgrade(string circuitId)
        {
            var allowed = CanInstallCapacityUpgrade(circuitId);
            if (!allowed.Success) return allowed;
            upgradedCircuitIds.Add(circuitId);
            // Existing consumers, thermal stress and a tripped breaker's power state are unchanged.
            Changed?.Invoke(Find(circuitId), "capacity upgrade installed");
            return CommandResult.Ok("Circuit " + circuitId + " capacity upgraded. A tripped breaker still needs its physical reset.");
        }
    }
}
