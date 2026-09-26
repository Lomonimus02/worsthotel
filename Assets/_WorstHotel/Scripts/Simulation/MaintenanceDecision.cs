namespace WorstHotel
{
    public enum MaintenanceChoice { CheapPatch, ProperRepair, Defer }

    public sealed class MaintenanceDecision
    {
        public int DayNumber { get; }
        public int ActorId { get; }
        public MaintenanceChoice Choice { get; }
        public int Cost { get; }
        public float ConditionBefore { get; }
        public float ConditionAfter { get; }
        public int CashAfter { get; }

        public MaintenanceDecision(int dayNumber, int actorId, MaintenanceChoice choice, int cost,
            float conditionBefore, float conditionAfter, int cashAfter)
        {
            DayNumber = dayNumber; ActorId = actorId; Choice = choice; Cost = cost;
            ConditionBefore = conditionBefore; ConditionAfter = conditionAfter; CashAfter = cashAfter;
        }
    }
}
