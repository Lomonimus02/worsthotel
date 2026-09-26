namespace WorstHotel
{
    public sealed partial class GameSession
    {
        public CommandResult ResetCircuit(int actorId, string circuitId)
        {
            if (Phase != DayPhase.Planning && Phase != DayPhase.Service)
                return GuestCommand(CommandResult.Fail("Reset the circuit during hotel preparation or service."));
            return GuestCommand(Simulation.ResetCircuit(actorId, circuitId));
        }
    }
}
