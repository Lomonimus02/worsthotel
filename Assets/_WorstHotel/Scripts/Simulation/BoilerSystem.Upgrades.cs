namespace WorstHotel
{
    public sealed partial class BoilerSystem
    {
        public bool CapacityUpgradePurchased { get; private set; }

        internal CommandResult CanInstallCapacityUpgrade()
        {
            if (ReadOnlyMirror) return CommandResult.Fail(HotelSimulation.MirrorMessage);
            if (!CapacityModelEnabled) return CommandResult.Fail("Capacity upgrades require continuous hotel operations.");
            if (CapacityUpgradePurchased) return CommandResult.Fail("The boiler capacity upgrade is already installed.");
            double upgraded = (double)settings.SafeLoad * settings.Capacity.CapacityUpgradeMultiplier;
            if (upgraded > float.MaxValue || (float)upgraded <= settings.SafeLoad)
                return CommandResult.Fail("The configured boiler upgrade cannot represent a larger finite capacity.");
            return CommandResult.Ok();
        }

        internal CommandResult InstallCapacityUpgrade()
        {
            var allowed = CanInstallCapacityUpgrade();
            if (!allowed.Success) return allowed;
            // Capacity changes delivery when running, but cannot repair, reset stress or finish maintenance.
            CapacityUpgradePurchased = true;
            UpdateOutput();
            return CommandResult.Ok("Boiler capacity upgraded. Existing wear and any ongoing repair still need attention.");
        }
    }
}
