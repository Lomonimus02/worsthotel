using System;

namespace WorstHotel
{
    public interface IGameClock
    {
        float SimulationTime { get; }
        float Speed { get; }
    }

    /// <summary>Time owned by the hotel simulation. Speed budgets fixed ticks; it never alters Unity physics time.</summary>
    public sealed partial class HotelGameClock : IGameClock
    {
        public float SimulationTime { get; private set; }
        public float Speed { get; private set; } = 1;
        public void SetSpeed(float speed)
        {
            if (ReadOnlyMirror) return;
            if (speed != 1 && speed != 4 && speed != 8)
                throw new ArgumentOutOfRangeException(nameof(speed), "Hotel speed must be 1, 4 or 8.");
            Speed = speed;
        }
        public void Advance(float simulationDelta)
        {
            if (ReadOnlyMirror) return;
            if (!Number.IsFinite(simulationDelta) || simulationDelta < 0 || !Number.IsFinite(SimulationTime + simulationDelta))
                throw new ArgumentOutOfRangeException(nameof(simulationDelta));
            SimulationTime += simulationDelta;
        }
        public void Reset() {
            if (ReadOnlyMirror) return; SimulationTime = 0; Speed = 1; }
    }
}


