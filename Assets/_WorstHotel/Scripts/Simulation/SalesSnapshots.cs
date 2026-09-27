using System;
using System.Linq;

namespace WorstHotel
{
    [Serializable] public sealed class SalesSettingsSnapshot
    {
        public bool Enabled;
        public int InitiallyOpenRooms, InitialPrice, Seed;
        public float BaseDemand, PriceElasticity, FirstDayDecisionStartHour, AdvanceDecisionStartHour, DecisionSpacingHours;
        public SalesSettings ToSettings() => new SalesSettings(Enabled, InitiallyOpenRooms, InitialPrice, BaseDemand,
            PriceElasticity, FirstDayDecisionStartHour, AdvanceDecisionStartHour, DecisionSpacingHours, Seed);
    }
    [Serializable] public sealed class RoomSalesPolicySnapshot
    {
        public int RoomId, Price, Revision;
        public bool OpenForSale;
    }
    [Serializable] public sealed class SalesDayCursorSnapshot
    {
        public int ArrivalDay, NextOfferIndex;
    }
    [Serializable] public sealed class SalesSnapshot
    {
        public SalesSettingsSnapshot Settings;
        public RoomSalesPolicySnapshot[] Rooms;
        public SalesDayCursorSnapshot[] Days;
    }

    public sealed partial class HotelSimulation
    {
        SalesSnapshot CaptureSales()
        {
            var settings = Operations.Sales;
            return new SalesSnapshot
            {
                Settings = new SalesSettingsSnapshot { Enabled = settings.Enabled, InitiallyOpenRooms = settings.InitiallyOpenRooms,
                    InitialPrice = settings.InitialPrice, BaseDemand = settings.BaseDemand, PriceElasticity = settings.PriceElasticity,
                    FirstDayDecisionStartHour = settings.FirstDayDecisionStartHour, AdvanceDecisionStartHour = settings.AdvanceDecisionStartHour,
                    DecisionSpacingHours = settings.DecisionSpacingHours, Seed = settings.Seed },
                Rooms = roomSalesPolicies.Select(row => new RoomSalesPolicySnapshot { RoomId = row.RoomId,
                    OpenForSale = row.OpenForSale, Price = row.Price, Revision = row.Revision }).ToArray(),
                Days = salesDays.Select(row => new SalesDayCursorSnapshot { ArrivalDay = row.ArrivalDay, NextOfferIndex = row.NextOfferIndex }).ToArray()
            };
        }
        void RestoreSales(SalesSnapshot value)
        {
            roomSalesPolicies.Clear();
            roomSalesPolicies.AddRange(value.Rooms.Select(row => new RoomSalesPolicy(row.RoomId, row.OpenForSale, row.Price, row.Revision)));
            salesDays.Clear();
            salesDays.AddRange(value.Days.Select(row => new SalesDayCursor(row.ArrivalDay, row.NextOfferIndex)));
        }
    }

    internal static partial class SnapshotValidation
    {
        internal static void SalesModel(HotelModelSnapshot model, SalesSettings expected, EconomySettings economy,
            int[] roomIds, Func<int, int, float> decisionAt)
        {
            if (!model.HasOperations) return;
            var value = model.Operations.Sales;
            Require(value?.Settings != null && expected != null, "Missing automatic-sales configuration.");
            var config = value.Settings.ToSettings();
            Require(config.Enabled == expected.Enabled && config.InitiallyOpenRooms == expected.InitiallyOpenRooms &&
                config.InitialPrice == expected.InitialPrice && config.BaseDemand == expected.BaseDemand &&
                config.PriceElasticity == expected.PriceElasticity && config.FirstDayDecisionStartHour == expected.FirstDayDecisionStartHour &&
                config.AdvanceDecisionStartHour == expected.AdvanceDecisionStartHour && config.DecisionSpacingHours == expected.DecisionSpacingHours &&
                config.Seed == expected.Seed, "Automatic-sales configuration differs from this hotel.");
            var policies = Array(value.Rooms, 6); Unique(policies.Select(row => row.RoomId));
            var cursors = Array(value.Days, 2); Unique(cursors.Select(row => row.ArrivalDay));
            if (!config.Enabled)
            {
                Require(policies.Length == 0 && cursors.Length == 0 && model.Operations.Reservations.All(row => !row.IsAutomatic),
                    "Manual booking mode cannot contain automatic sales.");
                return;
            }
            Require(policies.Length == roomIds.Length && policies.All(row => roomIds.Contains(row.RoomId)), "Incomplete room sales policy.");
            foreach (var row in policies)
                Require(row.Revision >= 1 && row.Price >= economy.MinPrice && row.Price <= economy.MaxPrice &&
                    (row.Price - economy.MinPrice) % economy.PriceStep == 0, "Invalid room sale rate or revision.");
            Require(model.Running ? cursors.Length == 2 && cursors.Any(row => row.ArrivalDay == model.Day) &&
                cursors.Any(row => row.ArrivalDay == model.Day + 1) : cursors.Length == 0, "Automatic-sales horizon differs from the calendar.");
            foreach (var cursor in cursors)
            {
                Require(cursor.ArrivalDay >= 1 && cursor.ArrivalDay <= 1000001 && cursor.NextOfferIndex >= 0 &&
                    cursor.NextOfferIndex <= SalesSettings.DecisionsPerDay, "Invalid sales decision cursor.");
                if (cursor.NextOfferIndex > 0)
                    Require(decisionAt(cursor.ArrivalDay, cursor.NextOfferIndex - 1) <= model.Time,
                        "Sales cursor consumed a future enquiry.");
                if (cursor.NextOfferIndex < SalesSettings.DecisionsPerDay)
                    Require(decisionAt(cursor.ArrivalDay, cursor.NextOfferIndex) > model.Time,
                        "Sales cursor would replay a past enquiry.");
            }
            foreach (var reservation in model.Operations.Reservations)
            {
                int day = reservation.Offer.ArrivalDay;
                int index = Enumerable.Range(0, SalesSettings.DecisionsPerDay).Where(candidate =>
                    reservation.Offer.Application.Id == "stay-" + day + "-" + (candidate + 1)).DefaultIfEmpty(-1).First();
                Require(index < 0 || reservation.IsAutomatic, "An ordinary enquiry bypassed automatic sales.");
                if (!reservation.IsAutomatic) continue; // Explicit debug walk-ins retain their own provenance.
                Require(index >= 0 && day <= model.Day + 1, "Unknown automatic reservation identity or horizon.");
                Require(decisionAt(day, index) <= model.Time, "An automatic reservation precedes its enquiry.");
                var cursor = cursors.FirstOrDefault(row => row.ArrivalDay == day);
                Require(cursor == null || cursor.NextOfferIndex > index, "Automatic reservation and consumed cursor differ.");
            }
        }
    }
}
