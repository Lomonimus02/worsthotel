namespace WorstHotel
{
    public sealed partial class HotelSimulation
    {
        public int PeriodMaintenanceSpend { get; private set; }

        CommandResult CanPayForEquipment(int actorId, int cost)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            if (!ContinuousOperations || !Running) return CommandResult.Fail("Equipment purchases require an open continuous hotel.");
            if (actorId < 0 || actorId > 1) return CommandResult.Fail("Unknown staff member.");
            if ((long)PeriodMaintenanceSpend + PeriodCapitalSpend + cost + settings.Economy.DailyOperatingCost > int.MaxValue)
                return CommandResult.Fail("This accounting period cannot record another equipment payment.");
            return CommandResult.Ok();
        }

        public CommandResult EmergencyPatchBoiler(int actorId)
        {
            int cost = settings.Economy.CheapPatchCost;
            var allowed = CanPayForEquipment(actorId, cost);
            if (!allowed.Success) return allowed;
            allowed = Boiler.CanApplyEmergencyPatch(actorId);
            if (!allowed.Success) return allowed;
            var paid = Economy.TrySpend(cost);
            if (!paid.Success) return paid;
            // Both guards above are pure. No callback can intervene between payment and commit.
            PeriodMaintenanceSpend += cost;
            var result = Boiler.ApplyEmergencyPatch(actorId);
            ObserveInfrastructureChanges();
            SignalEvent("Emergency boiler patch: $" + cost + ". Reduced condition and patch penalty remain.");
            return result;
        }

        public CommandResult BeginBoilerMaintenance(int actorId) => BeginBoilerMaintenance(actorId, BoilerServiceKind.Full);

        public CommandResult CanBeginBoilerMaintenance(int actorId, BoilerServiceKind kind, int expectedRevision = -1)
        {
            if (kind != BoilerServiceKind.Basic && kind != BoilerServiceKind.Full) return CommandResult.Fail("Choose Basic or Full boiler service.");
            if (expectedRevision < -1 || expectedRevision >= 0 && expectedRevision != Boiler.MaintenanceRevision)
                return CommandResult.Fail("The boiler has changed since this service was selected. Inspect it again.");
            int cost = kind == BoilerServiceKind.Basic ? settings.Economy.BasicMaintenanceCost : settings.Economy.ProperRepairCost;
            var allowed = CanPayForEquipment(actorId, cost);
            if (!allowed.Success) return allowed;
            allowed = Boiler.CanBeginMaintenance(Elapsed, kind);
            if (!allowed.Success) return allowed;
            return Economy.Cash < cost ? CommandResult.Fail("Not enough cash for this maintenance choice.") : CommandResult.Ok();
        }

        public CommandResult BeginBoilerMaintenance(int actorId, BoilerServiceKind kind, int expectedRevision = -1)
        {
            var allowed = CanBeginBoilerMaintenance(actorId, kind, expectedRevision);
            if (!allowed.Success) return allowed;
            int cost = kind == BoilerServiceKind.Basic ? settings.Economy.BasicMaintenanceCost : settings.Economy.ProperRepairCost;
            var paid = Economy.TrySpend(cost);
            if (!paid.Success) return paid;
            PeriodMaintenanceSpend += cost;
            var result = Boiler.BeginMaintenance(Elapsed, kind);
            ObserveInfrastructureChanges();
            SignalEvent(kind + " boiler service started: $" + cost + ". Heating is off until the work finishes.");
            return result;
        }
    }
}
