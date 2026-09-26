namespace WorstHotel
{
    public sealed partial class GameSession
    {
        // A deliberate developer command boundary keeps the session's UI projection in sync.
        public void RefreshDebugState()
        {
            Cash = Simulation.Economy.Cash;
            RaiseChanged();
        }
    }
}
