using System;

namespace WorstHotel
{
    /// <summary>Bounded ordinary demand using the existing timed enquiries for each hotel date.</summary>
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
        public float ReputationBaseline { get; }
        public float MinimumReputationDemand { get; }
        public float MaximumReputationDemand { get; }

        public SalesSettings(bool enabled = false, int initiallyOpenRooms = 4, int initialPrice = 180,
            float baseDemand = .9f, float priceElasticity = 1.5f, float firstDayDecisionStartHour = 8.5f,
            float advanceDecisionStartHour = 16, float decisionSpacingHours = .4f, int seed = 73129,
            float reputationBaseline = 60, float minimumReputationDemand = .8f, float maximumReputationDemand = 1.1f)
        {
            if (initiallyOpenRooms < 0 || initiallyOpenRooms > 10 || initialPrice < 0 ||
                !Number.IsFinite(reputationBaseline) || reputationBaseline <= 0 || reputationBaseline >= 100 ||
                !Number.IsFinite(minimumReputationDemand) || minimumReputationDemand <= 0 || minimumReputationDemand > 1 ||
                !Number.IsFinite(maximumReputationDemand) || maximumReputationDemand < 1 ||
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
            ReputationBaseline = reputationBaseline; MinimumReputationDemand = minimumReputationDemand;
            MaximumReputationDemand = maximumReputationDemand;
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

        public float ReputationDemandMultiplier(float reputation)
        {
            if (!Number.IsFinite(reputation)) throw new ArgumentException("Reputation must be finite.", nameof(reputation));
            float score = Number.Clamp(reputation, 0, 100);
            return score <= ReputationBaseline ? MinimumReputationDemand + (1 - MinimumReputationDemand) * score / ReputationBaseline :
                1 + (MaximumReputationDemand - 1) * (score - ReputationBaseline) / (100 - ReputationBaseline);
        }

        public double DemandProbability(int referencePrice, int price, float reputation) =>
            Math.Max(0, Math.Min(1, DemandProbability(referencePrice, price) * ReputationDemandMultiplier(reputation)));

        public static string ReputationLabel(float reputation) => reputation < 40 ? "Poor" : reputation < 60 ? "Fair" :
            reputation < 80 ? "Good" : "Excellent";

        public string DemandLabel(float reputation)
        {
            float multiplier = ReputationDemandMultiplier(reputation);
            return multiplier < .95f ? "Low" : multiplier >= 1.05f ? "Strong" : "Normal";
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
