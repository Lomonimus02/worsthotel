using System;
using System.Collections.Generic;
using System.Linq;

namespace WorstHotel
{
    public sealed partial class HotelSimulation
    {
        /// <summary>Derived from current room ownership and actual activities, including on mirrors.</summary>
        public IReadOnlyList<RoomHeatingDemand> HeatingDemands
        {
            get
            {
                if (!LivingEnabled) return Array.Empty<RoomHeatingDemand>();
                var result = new List<RoomHeatingDemand>(rooms.Count);
                foreach (var room in rooms.Values.OrderBy(item => item.Profile.Id))
                {
                    var occupant = room.Occupied ? guests.FirstOrDefault(guest => guest.GuestId == room.GuestId &&
                        guest.RoomId == room.Profile.Id && !guest.ReceiptPosted) : null;
                    result.Add(roomSystem.HeatingDemandForRoom(room, occupant, LivingSettings));
                }
                return result.AsReadOnly();
            }
        }
    }
}
