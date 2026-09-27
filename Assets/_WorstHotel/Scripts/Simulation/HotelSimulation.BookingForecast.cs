using System;
using System.Collections.Generic;
using System.Linq;

namespace WorstHotel
{
    public sealed partial class HotelSimulation
    {
        readonly struct ForecastStay
        {
            public readonly int RoomId;
            public readonly GuestProfile Profile;
            public readonly float Start, End;
            public ForecastStay(int roomId, GuestProfile profile, float start, float end)
            { RoomId = roomId; Profile = profile; Start = start; End = end; }
        }

        /// <summary>Derives the busiest overlapping segment, not the sum of sequential stays.
        /// Safe on mirrors: it never advances schedules, reserves a room, edits a price, or emits events.</summary>
        public BookingLoadForecast ForecastBookingLoad(string offerId, int roomId)
        {
            if (!ContinuousOperations || !Running || !LivingEnabled)
                return new BookingLoadForecast(offerId, roomId, "Open continuous operations before forecasting a stay.");
            if (string.IsNullOrEmpty(offerId) || !rooms.ContainsKey(roomId))
                return new BookingLoadForecast(offerId, roomId, "Choose a current enquiry and a real room.");
            var reservation = FindReservation(offerId);
            if (reservation != null && reservation.Status != ReservationStatus.Reserved)
                return new BookingLoadForecast(offerId, roomId, "This enquiry is no longer a future booking.");
            if (reservation != null && reservation.RoomId != roomId)
                return new BookingLoadForecast(offerId, roomId, "This booking already has a different assigned room.");
            var offer = reservation?.Offer ?? bookingOffers.FirstOrDefault(item => item.Id == offerId);
            if (offer == null || offer.ArrivalAt <= Elapsed)
                return new BookingLoadForecast(offerId, roomId, "This arrival enquiry is no longer available.");
            var available = CanReserveInterval(roomId, offer.ArrivalAt, offer.CheckoutAt, reservation?.Id);
            if (!available.Success) return new BookingLoadForecast(offerId, roomId, available.Message);

            float start = offer.ArrivalAt, end = offer.CheckoutAt;
            var stays = new List<ForecastStay>();
            var boundaries = new SortedSet<float> { start, end };
            var included = new HashSet<string>(StringComparer.Ordinal) { offer.Id };
            void Include(string id, int assignedRoom, GuestProfile profile, float arrival, float checkout)
            {
                if (!included.Add(id) || !rooms.ContainsKey(assignedRoom)) return;
                float clippedStart = Math.Max(start, arrival), clippedEnd = Math.Min(end, checkout);
                if (clippedStart >= clippedEnd) return;
                stays.Add(new ForecastStay(assignedRoom, profile, clippedStart, clippedEnd));
                boundaries.Add(clippedStart); boundaries.Add(clippedEnd);
            }
            // Add the selected candidate exactly once, whether it is still an enquiry or already reserved.
            stays.Add(new ForecastStay(roomId, offer.Application.Archetype, start, end));
            foreach (var other in reservations.Where(item => item.Active && item.Id != offer.Id))
            {
                var guest = guests.FirstOrDefault(item => item.GuestId == other.Id);
                if (guest != null && (guest.ReceiptPosted || GuestHasEnded(guest))) continue;
                int assignedRoom = guest?.Agent?.CheckedIn == true ? guest.RoomId : other.RoomId;
                Include(other.Id, assignedRoom, other.Offer.Application.Archetype, other.Offer.ArrivalAt,
                    guest?.Agent?.CheckoutTime ?? other.Offer.CheckoutAt);
            }
            // Defensive support for a currently owned stay without a reservation record. Ordinary
            // continuous bookings take the path above, so materialized guests are never counted twice.
            foreach (var room in rooms.Values.Where(item => item.Occupied))
            {
                var guest = guests.FirstOrDefault(item => item.GuestId == room.GuestId);
                if (guest == null || guest.ReceiptPosted || GuestHasEnded(guest) || guest.Agent == null || !guest.Agent.CheckedIn) continue;
                Include(guest.GuestId, room.Profile.Id, guest.Application.Archetype, Elapsed, guest.Agent.CheckoutTime);
            }

            var circuit = Electrical.CircuitForRoom(roomId);
            if (circuit == null) return new BookingLoadForecast(offerId, roomId, "This room has no known electrical branch.");
            double heaterDemand = Heaters.Items.Where(item => item.RoomId.HasValue &&
                Electrical.CircuitForRoom(item.RoomId.Value) == circuit).Sum(item => (double)item.DemandedElectricalLoad);
            double typical = 0, peak = 0, circuitDemand = 0;
            int maximumGuests = 0;
            foreach (float boundary in boundaries)
            {
                if (boundary >= end) break;
                // Half-open intervals: an ending stay is absent when another starts at this exact instant.
                var active = stays.Where(item => item.Start <= boundary && boundary < item.End).ToArray();
                double space = 0;
                foreach (var room in rooms.Values.OrderBy(item => item.Profile.Id))
                {
                    var occupant = active.FirstOrDefault(item => item.RoomId == room.Profile.Id);
                    space += roomSystem.TypicalSpaceHeating(room, occupant.Profile, LivingSettings);
                }
                float shower = active.Select(item => roomSystem.ShowerHotWaterDemand(item.Profile, LivingSettings)).DefaultIfEmpty(0).Max();
                double branch = active.Count(item => Electrical.CircuitForRoom(item.RoomId) == circuit) *
                    (double)ElectricitySettings.OccupiedRoomLoad + heaterDemand;
                typical = Math.Max(typical, space); peak = Math.Max(peak, space + shower);
                circuitDemand = Math.Max(circuitDemand, branch); maximumGuests = Math.Max(maximumGuests, active.Length);
            }
            float typicalResult = (float)Math.Min(float.MaxValue, typical);
            float peakResult = (float)Math.Min(float.MaxValue, peak);
            float circuitResult = (float)Math.Min(float.MaxValue, circuitDemand);
            double ratio = (double)typicalResult / Boiler.EffectiveCapacity;
            var band = CapacityBands.ForLoad((float)Math.Min(float.MaxValue, ratio),
                settings.Boiler.Capacity.BusyLoadRatio, settings.Boiler.Capacity.StrainedLoadRatio);
            return new BookingLoadForecast(offerId, roomId, start, end, Boiler.Load, Boiler.CapacityBand,
                Boiler.EffectiveCapacity, typicalResult, peakResult, band, circuit.Id, circuit.Capacity, circuitResult, maximumGuests);
        }

        static bool GuestHasEnded(GuestStay guest) => guest.Agent != null &&
            (guest.Agent.State == GuestAgentState.CheckingOut || guest.Agent.State == GuestAgentState.Leaving || guest.Agent.State == GuestAgentState.Left);
    }
}
