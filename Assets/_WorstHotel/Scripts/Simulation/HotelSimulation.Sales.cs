using System;
using System.Collections.Generic;
using System.Linq;

namespace WorstHotel
{
    public sealed partial class HotelSimulation
    {
        internal readonly List<RoomSalesPolicy> roomSalesPolicies = new List<RoomSalesPolicy>();
        internal readonly List<SalesDayCursor> salesDays = new List<SalesDayCursor>(2);
        public IReadOnlyList<RoomSalesPolicy> RoomSalesPolicies => roomSalesPolicies.AsReadOnly();
        public IReadOnlyList<SalesDayCursor> SalesDecisionCursors => salesDays.AsReadOnly();
        public bool AutomaticBookingsEnabled => Operations?.Sales.Enabled == true;
        public float ReputationDemandMultiplier => Operations?.Sales.ReputationDemandMultiplier(Economy.Reputation) ?? 1;
        public string DemandReputationLabel => SalesSettings.ReputationLabel(Economy.Reputation);
        public string DemandLabel => Operations?.Sales.DemandLabel(Economy.Reputation) ?? "Normal";
        public float NextSalesDecisionAt => TryGetNextSalesDecision(out float at, out _, out _) ? at : float.PositiveInfinity;

        void InitializeSales()
        {
            if (!AutomaticBookingsEnabled) return;
            var sales = Operations.Sales;
            if (!ValidBookingPrice(sales.InitialPrice))
                throw new ArgumentException("The initial room sale price must be on the hotel's allowed price grid.");
            int index = 0;
            foreach (int roomId in rooms.Keys.OrderBy(id => id))
                roomSalesPolicies.Add(new RoomSalesPolicy(roomId, index++ < sales.InitiallyOpenRooms && rooms[roomId].Operational, sales.InitialPrice));
        }

        public CommandResult SetRoomSalesPolicy(int actorId, int roomId, bool open, int price, int expectedPolicyRevision = -1)
        {
            var gate = BookingCommandGate(actorId); if (!gate.Success) return gate;
            if (!AutomaticBookingsEnabled) return CommandResult.Fail("Automatic ordinary room sales are disabled in this hotel.");
            var policy = roomSalesPolicies.FirstOrDefault(item => item.RoomId == roomId);
            if (policy == null) return CommandResult.Fail("Choose a real room sales policy.");
            if (!IsRoomOperational(roomId)) return CommandResult.Fail("Restore the North Wing before opening this room for sale.");
            if (expectedPolicyRevision >= 0 && expectedPolicyRevision != policy.Revision)
                return CommandResult.Fail("This room's sales policy changed. Refresh it before applying the draft.");
            if (!ValidBookingPrice(price)) return CommandResult.Fail("Choose a room rate on the hotel's allowed price grid.");
            if (policy.OpenForSale == open && policy.Price == price) return CommandResult.Ok("The room's sales policy is unchanged.");
            if (policy.Revision == int.MaxValue) return CommandResult.Fail("The room sales revision is exhausted.");
            policy.OpenForSale = open; policy.Price = price; policy.Revision++;
            // A policy edit never consumes an enquiry. Only the next scheduled decision sees it.
            SignalEvent("Room " + roomId + " sales " + (open ? "open at $" + price : "closed to new bookings"));
            return CommandResult.Ok("Room sales updated. Existing reservations keep their agreed price and dates.");
        }

        public float SalesDecisionAt(int arrivalDay, int offerIndex)
        {
            if (!AutomaticBookingsEnabled || arrivalDay < 1 || offerIndex < 0 || offerIndex >= SalesSettings.DecisionsPerDay)
                throw new ArgumentOutOfRangeException(nameof(arrivalDay), "Choose an enabled sales date and enquiry index 0–7.");
            var sales = Operations.Sales;
            float hour = (arrivalDay == 1 ? sales.FirstDayDecisionStartHour : sales.AdvanceDecisionStartHour) +
                offerIndex * sales.DecisionSpacingHours;
            return Calendar.At(arrivalDay == 1 ? 1 : arrivalDay - 1, hour);
        }

        public bool TryGetNextSalesDecision(out float at, out int arrivalDay, out int offerIndex)
        {
            at = 0; arrivalDay = 0; offerIndex = 0;
            if (!AutomaticBookingsEnabled || !Running) return false;
            bool found = false;
            foreach (var cursor in salesDays)
            {
                if (cursor.NextOfferIndex >= SalesSettings.DecisionsPerDay) continue;
                float candidate = SalesDecisionAt(cursor.ArrivalDay, cursor.NextOfferIndex);
                if (found && candidate >= at) continue;
                at = candidate; arrivalDay = cursor.ArrivalDay; offerIndex = cursor.NextOfferIndex; found = true;
            }
            return found;
        }

        void RefreshSalesDays(float now)
        {
            if (!AutomaticBookingsEnabled || IsReadOnlyMirror || !Running) return;
            int day = Calendar.DayAt(now);
            salesDays.RemoveAll(item => item.ArrivalDay < day || item.ArrivalDay > day + 1);
            for (int arrivalDay = day; arrivalDay <= day + 1; arrivalDay++)
                if (!salesDays.Any(item => item.ArrivalDay == arrivalDay))
                    salesDays.Add(new SalesDayCursor(arrivalDay));
            salesDays.Sort((left, right) => left.ArrivalDay.CompareTo(right.ArrivalDay));
        }

        void ProcessDueSalesDecisions(float now)
        {
            if (!AutomaticBookingsEnabled || IsReadOnlyMirror || !Running) return;
            while (TryGetNextSalesDecision(out float at, out int arrivalDay, out int offerIndex) && at <= now)
            {
                var cursor = salesDays.First(item => item.ArrivalDay == arrivalDay);
                bool forceDemand = debugNextBookingDemand;
                debugNextBookingDemand = false; // One due enquiry, even if no room/offer remains eligible.
                cursor.NextOfferIndex++; // Consumed even if demand, room supply or ledger space is absent.
                string id = "stay-" + arrivalDay + "-" + (offerIndex + 1);
                var offer = bookingOffers.FirstOrDefault(item => item.Id == id);
                if (offer == null || FindReservation(id) != null || offer.ArrivalAt <= now) continue;
                double roll = SalesRoll(offer.Id, Operations.Sales.Seed);
                foreach (var policy in roomSalesPolicies.Where(item => item.OpenForSale).OrderBy(item => item.Price).ThenBy(item => item.RoomId))
                {
                    if (!CanReserveInterval(policy.RoomId, offer.ArrivalAt, offer.CheckoutAt).Success ||
                        !forceDemand && roll >= Operations.Sales.DemandProbability(offer.Application.ReferencePrice, policy.Price, Economy.Reputation)) continue;
                    // Same validated reservation/physical arrival pipeline, with explicit automatic origin.
                    var result = CommitBooking(offer, policy.RoomId, policy.Price, -1, true);
                    if (result.Success || reservations.Count >= 128) break;
                }
            }
        }

        static double SalesRoll(string offerId, int seed)
        {
            uint hash;
            unchecked
            {
                hash = 2166136261u ^ (uint)seed;
                foreach (char value in offerId) hash = (hash ^ value) * 16777619u;
                hash ^= hash >> 16; hash *= 0x7feb352du; hash ^= hash >> 15;
                hash *= 0x846ca68bu; hash ^= hash >> 16;
            }
            return hash / 4294967296d;
        }
    }
}
