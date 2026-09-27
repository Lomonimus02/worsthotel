using System;

namespace WorstHotel
{
    /// <summary>Bounded ordinary demand using the existing eight enquiries for each hotel date.</summary>
    public sealed class SalesSettings
    {
        public const int DecisionsPerDay = 12;
        public bool Enabled { get; }
        public int InitiallyOpenRooms { get; }
        public int InitialPrice { get; }
        public float BaseDemand { get; }
        public float PriceElasticity { get; }
        public float FirstDayDecisionStartHour { get; }
        public float AdvanceDecisionStartHour { get; }
        public float DecisionSpacingHours { get; }
        public int Seed { get; }

        public SalesSettings(bool enabled = false, int initiallyOpenRooms = 4, int initialPrice = 180,
            float baseDemand = .9f, float priceElasticity = 1.5f, float firstDayDecisionStartHour = 8.5f,
            float advanceDecisionStartHour = 16, float decisionSpacingHours = .4f, int seed = 73129)
        {
            if (initiallyOpenRooms < 0 || initiallyOpenRooms > 10 || initialPrice < 0 ||
                !Number.IsFinite(baseDemand) || baseDemand < 0 || baseDemand > 1 ||
                !Number.IsFinite(priceElasticity) || priceElasticity < 0 ||
                !Hour(firstDayDecisionStartHour) || !Hour(advanceDecisionStartHour) ||
                !Number.IsFinite(decisionSpacingHours) || decisionSpacingHours <= 0 ||
                firstDayDecisionStartHour + (double)decisionSpacingHours * (DecisionsPerDay - 1) >= 24 ||
                advanceDecisionStartHour + (double)decisionSpacingHours * (DecisionsPerDay - 1) >= 24)
                throw new ArgumentException("Invalid automatic room sales settings.");
            Enabled = enabled; InitiallyOpenRooms = initiallyOpenRooms; InitialPrice = initialPrice;
            BaseDemand = baseDemand; PriceElasticity = priceElasticity;
            FirstDayDecisionStartHour = firstDayDecisionStartHour; AdvanceDecisionStartHour = advanceDecisionStartHour;
            DecisionSpacingHours = decisionSpacingHours; Seed = seed;
        }

        static bool Hour(float value) => Number.IsFinite(value) && value >= 0 && value < 24;

        internal void ValidateCalendar(float openingHour, float arrivalStartHour)
        {
            if (!Enabled) return;
            if (FirstDayDecisionStartHour <= openingHour || AdvanceDecisionStartHour <= openingHour ||
                FirstDayDecisionStartHour + (double)DecisionSpacingHours * (DecisionsPerDay - 1) >= arrivalStartHour)
                throw new ArgumentException("Initial sales decisions must follow opening; first-day decisions must precede the first arrival.");
        }

        internal double DemandProbability(int referencePrice, int price)
        {
            if (BaseDemand == 0) return 0;
            if (PriceElasticity == 0) return BaseDemand;
            if (price == 0) return 1; // Only reachable when the economy explicitly permits a free rate.
            return Math.Max(0, Math.Min(1, BaseDemand * Math.Pow(referencePrice / (double)price, PriceElasticity)));
        }
    }

    public sealed class RoomSalesPolicy
    {
        public int RoomId { get; }
        public bool OpenForSale { get; internal set; }
        public int Price { get; internal set; }
        public int Revision { get; internal set; }
        internal RoomSalesPolicy(int roomId, bool openForSale, int price, int revision = 1)
        { RoomId = roomId; OpenForSale = openForSale; Price = price; Revision = revision; }
    }

    public sealed class SalesDayCursor
    {
        public int ArrivalDay { get; }
        public int NextOfferIndex { get; internal set; }
        internal SalesDayCursor(int arrivalDay, int nextOfferIndex = 0)
        { ArrivalDay = arrivalDay; NextOfferIndex = nextOfferIndex; }
    }
}
