namespace WorstHotel
{
    public sealed partial class GameSession
    {
        public CommandResult MoveGuest(int actorId, string guestId, int roomId) => ForwardLan(LanCommandKind.MoveGuest, guestId, roomId) ?
            CommandResult.Ok(LastMessage) : GuestCommand(Simulation.RequestGuestMove(actorId, guestId, roomId));
        public CommandResult CancelGuestMove(int actorId, string guestId) => ForwardLan(LanCommandKind.CancelMove, guestId) ?
            CommandResult.Ok(LastMessage) : GuestCommand(Simulation.CancelGuestMove(actorId, guestId));
        public CommandResult AcceptConsequences(int actorId, string guestId) => ForwardLan(LanCommandKind.AcceptConsequences, guestId) ?
            CommandResult.Ok(LastMessage) : GuestCommand(Simulation.AcceptConsequences(actorId, guestId));
        public CommandResult AcceptBoilerConsequences(int actorId) => ForwardLan(LanCommandKind.AcceptBoilerConsequences) ?
            CommandResult.Ok(LastMessage) : GuestCommand(Simulation.AcceptBoilerConsequences(actorId));

        public CommandResult RegisterHeater(string id) => RefreshHeaterDelivery(Simulation.Heaters.Register(id));
        public CommandResult UnregisterHeater(string id) => RefreshHeaterDelivery(Simulation.Heaters.Unregister(id));
        public CommandResult SetHeaterPlacement(string id, int? roomId) => RefreshHeaterDelivery(Simulation.Heaters.AssignRoom(id, roomId));
        CommandResult RefreshHeaterDelivery(CommandResult result)
        {
            if (result.Success) { Simulation.RefreshElectrical(); RaiseChanged(); }
            return result;
        }
        public CommandResult SetHeaterSwitch(int actorId, string id, bool switchedOn)
        {
            if (actorId < 0 || actorId > 1) return CommandResult.Fail("Unknown staff actor.");
            if (switchedOn && Phase != DayPhase.Service) return GuestCommand(CommandResult.Fail("Heater power is available during service."));
            var result = Simulation.Heaters.SetSwitchedOn(id, switchedOn);
            if (result.Success) Simulation.RefreshElectrical();
            if (result.Success) Simulation.SignalEvent("Portable heater switched " + (switchedOn ? "on" : "off"));
            return GuestCommand(result);
        }
    }
}
