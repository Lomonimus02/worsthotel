namespace WorstHotel
{
    public sealed partial class HotelSimulation
    {
        public int PeriodLaundrySpend { get; private set; }
        public int PeriodBulbSpend { get; private set; }

        // The order command validates its actor, stock and delivery before calling this once.
        // Reports and delivery completion only carry the posted amounts; they never pay again.
        internal CommandResult PaySupplyOrder(int cost, bool laundry)
        {
            var allowed = CanPayOperatingExpense(cost);
            if (!allowed.Success) return allowed;
            var paid = Economy.TrySpend(cost);
            if (!paid.Success) return paid;
            if (laundry) PeriodLaundrySpend += cost;
            else PeriodBulbSpend += cost;
            return paid;
        }
    }
}
