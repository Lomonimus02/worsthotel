namespace WorstHotel
{
    public sealed partial class HotelSimulation
    {
        public int PeriodMaintenanceSpend { get; private set; }

        CommandResult CanPayForMaintenance(int actorId, int cost)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            if (!ContinuousOperations || !Running) return CommandResult.Fail("Maintenance requires an open continuous hotel.");
            if (actorId < 0 || actorId > 1) return CommandResult.Fail("Unknown staff member.");
            if ((long)PeriodMaintenanceSpend + cost + settings.Economy.DailyOperatingCost > int.MaxValue)
                return CommandResult.Fail("This accounting period cannot record another maintenance payment.");
            return CommandResult.Ok();
        }

        public CommandResult EmergencyPatchBoiler(int actorId)
        {
            int cost = settings.Economy.CheapPatchCost;
            var allowed = CanPayForMaintenance(actorId, cost);
            if (!allowed.Success) return allowed;
            allowed = Boiler.CanRestart(actorId);
            if (!allowed.Success) return allowed;
            var paid = Economy.TrySpend(cost);
            if (!paid.Success) return paid;
            // Both guards above are pure. No callback can intervene between payment and commit.
            PeriodMaintenanceSpend += cost;
            var result = Boiler.ApplyEmergencyPatch(actorId);
            SignalEvent("Emergency boiler patch: $" + cost + ". Reduced condition and patch penalty remain.");
            return result;
        }

        public CommandResult BeginBoilerMaintenance(int actorId)
        {
            int cost = settings.Economy.ProperRepairCost;
            var allowed = CanPayForMaintenance(actorId, cost);
            if (!allowed.Success) return allowed;
            allowed = Boiler.CanBeginMaintenance(Elapsed);
            if (!allowed.Success) return allowed;
            var paid = Economy.TrySpend(cost);
            if (!paid.Success) return paid;
            PeriodMaintenanceSpend += cost;
            var result = Boiler.BeginMaintenance(Elapsed);
            SignalEvent("Boiler maintenance started: $" + cost + ". Heating is off until the work finishes.");
            return result;
        }
    }
}
