namespace WorstHotel
{
    public sealed partial class GameSession
    {
        public CommandResult PurchaseInsulation(int actorId)
        {
            var allowed = CanAuthorizeUpgrade(actorId); if (!allowed.Success) return GuestCommand(allowed);
            if (ForwardLan(LanCommandKind.PurchaseInsulation)) return CommandResult.Ok(LastMessage);
            var result = Simulation.PurchaseInsulation(actorId); Cash = Simulation.Economy.Cash; return GuestCommand(result);
        }

        public CommandResult RestoreNorthWing(int actorId)
        {
            var allowed = CanAuthorizeUpgrade(actorId); if (!allowed.Success) return GuestCommand(allowed);
            if (ForwardLan(LanCommandKind.RestoreNorthWing)) return CommandResult.Ok(LastMessage);
            var result = Simulation.RestoreNorthWing(actorId); Cash = Simulation.Economy.Cash; return GuestCommand(result);
        }

        CommandResult CanAuthorizeUpgrade(int actorId)
        {
            if (actorId < 0 || actorId > 1 || Simulation == null || !Simulation.ContinuousOperations || Phase != DayPhase.Service)
                return CommandResult.Fail("Capacity upgrades require an active continuous hotel.");
            var staff = LocalCoopBootstrap.Instance;
            if (!staff || actorId >= staff.Players.Length || !staff.Players[actorId] || !staff.Players[actorId].DeviceReady || staff.IsPaused)
                return CommandResult.Fail("An active staff member must authorize the purchase.");
            return CommandResult.Ok();
        }

        public CommandResult PurchaseBoilerUpgrade(int actorId)
        {
            var allowed = CanAuthorizeUpgrade(actorId);
            if (!allowed.Success) return GuestCommand(allowed);
            if (ForwardLan(LanCommandKind.PurchaseBoilerUpgrade)) return CommandResult.Ok(LastMessage);
            var result = Simulation.PurchaseBoilerUpgrade(actorId);
            Cash = Simulation.Economy.Cash;
            return GuestCommand(result);
        }

        public CommandResult PurchaseElectricalUpgrade(int actorId, string circuitId)
        {
            var allowed = CanAuthorizeUpgrade(actorId);
            if (!allowed.Success) return GuestCommand(allowed);
            if (circuitId != "A" && circuitId != "B") return GuestCommand(CommandResult.Fail("Choose circuit A or B."));
            if (ForwardLan(LanCommandKind.PurchaseElectricalUpgrade, circuitId)) return CommandResult.Ok(LastMessage);
            var result = Simulation.PurchaseElectricalUpgrade(actorId, circuitId);
            Cash = Simulation.Economy.Cash;
            return GuestCommand(result);
        }
    }
}
