namespace WorstHotel
{
    public sealed partial class HotelSimulation
    {
        /// <summary>Synchronize placement/activity commands without advancing overload time or the hotel clock.</summary>
        public void RefreshElectrical()
        {
            if (IsReadOnlyMirror) return;
            if (Electrical == null) return;
            Electrical.Tick(guests, rooms.Values, Heaters, 0);
            Noise.Tick(guests, rooms.Values, Elapsed);
        }

        public CommandResult ResetCircuit(int actorId, string circuitId)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            if (Electrical == null) return CommandResult.Fail("The living hotel's electrical system is not active.");
            // Preparation and service both allow the physical breaker to be reset. Settlement never resets it automatically.
            var result = Electrical.ResetCircuit(actorId, circuitId);
            if (result.Success) RefreshElectrical();
            return result;
        }

        public CommandResult DebugTripCircuit(string circuitId)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            if (Electrical == null) return CommandResult.Fail("The living hotel's electrical system is not active.");
            var result = Electrical.ForceTrip(circuitId);
            if (result.Success) RefreshElectrical();
            return result;
        }

        public CommandResult DebugSetCircuitLoad(string circuitId, float? total)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            if (Electrical == null) return CommandResult.Fail("The living hotel's electrical system is not active.");
            var result = Electrical.DebugOverrideLoad(circuitId, total);
            if (result.Success) RefreshElectrical();
            return result;
        }
    }
}

