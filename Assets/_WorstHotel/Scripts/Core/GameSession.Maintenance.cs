namespace WorstHotel
{
    public sealed partial class GameSession
    {
        CommandResult ActiveBoilerServiceStaff(int actorId)
        {
            if (actorId < 0 || actorId > 1 || Simulation == null || !Simulation.ContinuousOperations || Phase != DayPhase.Service)
                return CommandResult.Fail("Boiler maintenance requires an active continuous hotel.");
            var staff = LocalCoopBootstrap.Instance;
            if (!staff || actorId >= staff.Players.Length || !staff.Players[actorId] || !staff.Players[actorId].DeviceReady || staff.IsPaused)
                return CommandResult.Fail("An active staff member must authorize maintenance.");
            var lan = LanSession.Instance;
            if (lan && lan.Role == LanRole.Host && actorId == 1 && !lan.PeerConnected)
                return CommandResult.Fail("The second owner is not connected.");
            return CommandResult.Ok();
        }

        public void OpenBoilerInspection(int actorId)
        {
            var allowed = ActiveBoilerServiceStaff(actorId);
            if (!allowed.Success) { GuestCommand(allowed); return; }
            if (IsLanReplica) return; // Only a host's real world interaction opens a remote physical surface.
            var lan = LanSession.Instance;
            if (lan && lan.Role == LanRole.Host && actorId == 1) lan.RequestRemoteBoilerInspection();
            else ManagementUI.Instance?.OpenBoilerInspection(actorId);
        }

        public CommandResult SelectBoilerService(int actorId, BoilerServiceKind kind, int expectedRevision = -1)
        {
            var allowed = ActiveBoilerServiceStaff(actorId);
            if (!allowed.Success) return GuestCommand(allowed);
            if (expectedRevision == -1) expectedRevision = Simulation.Boiler.MaintenanceRevision;
            if (ForwardLan(LanCommandKind.SelectBoilerService, amount: (int)kind, maintenanceRevision: expectedRevision))
                return CommandResult.Ok(LastMessage);
            var station = BoilerServiceInteraction.Instance;
            return GuestCommand(station ? station.Select(actorId, kind, expectedRevision) :
                CommandResult.Fail("The boiler inspection point is unavailable."));
        }

        public CommandResult BeginBoilerMaintenance(int actorId) =>
            BeginBoilerMaintenance(actorId, BoilerServiceKind.Full);

        public CommandResult BeginBoilerMaintenance(int actorId, BoilerServiceKind kind, int expectedRevision = -1)
        {
            var allowed = ActiveBoilerServiceStaff(actorId);
            if (!allowed.Success) return GuestCommand(allowed);
            if (expectedRevision == -1) expectedRevision = Simulation.Boiler.MaintenanceRevision;
            var station = BoilerServiceInteraction.Instance;
            // Selection is a network intention; completed physical work is never a client command.
            if (IsLanReplica || !station || !station.TryConsumePreparedSetup(actorId, kind, expectedRevision))
                return GuestCommand(CommandResult.Fail("Select a service, then hold at the boiler to prepare it."));
            var result = Simulation.BeginBoilerMaintenance(actorId, kind, expectedRevision);
            Cash = Simulation.Economy.Cash;
            return GuestCommand(result);
        }
    }
}
