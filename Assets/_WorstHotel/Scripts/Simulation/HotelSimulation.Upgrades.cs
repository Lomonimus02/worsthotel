namespace WorstHotel
{
    public sealed partial class HotelSimulation
    {
        public int PeriodCapitalSpend { get; private set; }

        public CommandResult PurchaseBoilerUpgrade(int actorId)
        {
            int cost = settings.Economy.BoilerUpgradeCost;
            var allowed = CanPayForEquipment(actorId, cost);
            if (!allowed.Success) return allowed;
            allowed = Boiler.CanInstallCapacityUpgrade();
            if (!allowed.Success) return allowed;
            var paid = Economy.TrySpend(cost);
            if (!paid.Success) return paid;
            PeriodCapitalSpend += cost;
            var result = Boiler.InstallCapacityUpgrade();
            ObserveInfrastructureChanges();
            SignalEvent("Boiler capacity upgraded for $" + cost + ". Current condition and repair state remain.");
            return result;
        }

        public CommandResult PurchaseElectricalUpgrade(int actorId, string circuitId)
        {
            int cost = settings.Economy.ElectricalUpgradeCost;
            var allowed = CanPayForEquipment(actorId, cost);
            if (!allowed.Success) return allowed;
            if (Electrical == null) return CommandResult.Fail("This hotel has no electrical circuit registry.");
            allowed = Electrical.CanInstallCapacityUpgrade(circuitId);
            if (!allowed.Success) return allowed;
            var paid = Economy.TrySpend(cost);
            if (!paid.Success) return paid;
            PeriodCapitalSpend += cost;
            var result = Electrical.InstallCapacityUpgrade(circuitId);
            RefreshElectrical();
            SignalEvent("Circuit " + circuitId + " upgraded for $" + cost + ". A tripped breaker still needs resetting.");
            return result;
        }
    }
}
