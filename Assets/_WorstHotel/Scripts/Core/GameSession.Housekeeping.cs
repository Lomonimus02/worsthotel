namespace WorstHotel
{
    public sealed partial class GameSession
    {
        public CommandResult PrioritizeCleaning(int actorId, int roomId) => GuestCommand(Simulation.PrioritizeCleaning(actorId, roomId));
        public CommandResult ReportHousekeeperReachedRoom(int roomId) => GuestCommand(Simulation.SignalHousekeeperReachedRoom(roomId));
        public CommandResult ReportGuestVacatedRoom(string guestId, int roomId)
        {
            var result = Simulation.SignalGuestVacatedRoom(guestId, roomId);
            if (result.Success) RaiseChanged();
            return result;
        }
    }
}
