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
        public CommandResult SetHeaterPlacement(string id, int? roomId)
        {
            bool changed = Simulation.Heaters.Find(id)?.RoomId != roomId;
            var result = RefreshHeaterDelivery(Simulation.Heaters.AssignRoom(id, roomId));
            if (result.Success && changed) RecordHeaterHelp(id);
            return result;
        }
        void RecordHeaterHelp(string id)
        {
            var heater = Simulation.Heaters.Find(id);
            if (heater == null || heater.EffectiveHeatOutput <= 0 || !heater.RoomId.HasValue) return;
            var room = System.Array.Find(Rooms, r => r.Profile.Id == heater.RoomId.Value);
            if (room?.GuestId != null) Simulation.Services?.RecordStaffAction(room.GuestId, IncidentReason.Temperature);
        }
        CommandResult RefreshHeaterDelivery(CommandResult result)
        {
            if (result.Success) { Simulation.RefreshElectrical(); RaiseChanged(); }
            return result;
        }
        public CommandResult SetHeaterSwitch(int actorId, string id, bool switchedOn)
        {
            if (actorId < 0 || actorId > 1) return CommandResult.Fail("Unknown staff actor.");
            if (switchedOn && Phase != DayPhase.Service) return GuestCommand(CommandResult.Fail("Heater power is available during service."));
            bool changed = Simulation.Heaters.Find(id)?.SwitchedOn != switchedOn;
            var result = Simulation.Heaters.SetSwitchedOn(id, switchedOn);
            if (result.Success) Simulation.RefreshElectrical();
            if (result.Success && changed && switchedOn) RecordHeaterHelp(id);
            if (result.Success) Simulation.SignalEvent("Portable heater switched " + (switchedOn ? "on" : "off"));
            return GuestCommand(result);
        }
    }
}
