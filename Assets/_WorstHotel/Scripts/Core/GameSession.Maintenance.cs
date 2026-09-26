namespace WorstHotel
{
    public sealed partial class GameSession
    {
        public CommandResult BeginBoilerMaintenance(int actorId)
        {
            if (actorId < 0 || actorId > 1 || Simulation == null || !Simulation.ContinuousOperations || Phase != DayPhase.Service)
                return GuestCommand(CommandResult.Fail("Boiler maintenance requires an active continuous hotel."));
            var staff = LocalCoopBootstrap.Instance;
            if (!staff || actorId >= staff.Players.Length || !staff.Players[actorId] || !staff.Players[actorId].DeviceReady || staff.IsPaused)
                return GuestCommand(CommandResult.Fail("An active staff member must authorize maintenance."));
            if (ForwardLan(LanCommandKind.BeginBoilerMaintenance)) return CommandResult.Ok(LastMessage);
            var result = Simulation.BeginBoilerMaintenance(actorId);
            Cash = Simulation.Economy.Cash;
            return GuestCommand(result);
        }
    }
}
