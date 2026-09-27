namespace WorstHotel
{
    public enum InfrastructureChangeKind
    {
        BoilerBand, BoilerStressCritical, BoilerFailure, BoilerService, BoilerPatch,
        RadiatorSetting, HeaterRoom, HeaterSwitch, HeaterPower, CircuitBand, CircuitWarning, CircuitTrip
    }

    /// <summary>Host-local diagnostic evidence. Values are enum ordinals, 0/1 flags or physical valve/room values.</summary>
    public readonly struct InfrastructureHistoryEntry
    {
        public long Sequence { get; }
        public float At { get; }
        public InfrastructureChangeKind Kind { get; }
        public string EntityId { get; }
        public int? RoomId { get; }
        public float PreviousValue { get; }
        public float Value { get; }
        public bool Diagnostic { get; }
        public float BoilerLoad { get; }
        public float BoilerCapacity { get; }
        public float BoilerStress { get; }
        public float BoilerOutput { get; }
        public float CircuitLoad { get; }
        public float CircuitCapacity { get; }
        public float CircuitStress { get; }

        internal InfrastructureHistoryEntry(long sequence, float at, InfrastructureChangeKind kind, string entityId,
            int? roomId, float previous, float value, bool diagnostic, BoilerSystem boiler, ElectricalCircuit circuit)
        {
            Sequence = sequence; At = at; Kind = kind; EntityId = entityId; RoomId = roomId;
            PreviousValue = previous; Value = value; Diagnostic = diagnostic;
            BoilerLoad = boiler.Load; BoilerCapacity = boiler.EffectiveCapacity;
            BoilerStress = boiler.Stress01; BoilerOutput = boiler.HeatingOutput;
            CircuitLoad = circuit?.RequestedLoad ?? 0; CircuitCapacity = circuit?.Capacity ?? 0;
            CircuitStress = circuit?.Stress01 ?? 0;
        }
    }
}
