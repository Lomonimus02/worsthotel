using System;

namespace WorstHotel
{
    /// <summary>Current nonnegative consumers belonging to one physical room.</summary>
    public readonly struct RoomHeatingDemand
    {
        public int RoomId { get; }
        public string GuestId { get; }
        public float SpaceHeating { get; }
        public float HotWater { get; }
        public float Total => SpaceHeating + HotWater;

        public RoomHeatingDemand(int roomId, string guestId, float spaceHeating, float hotWater)
        {
            if (roomId <= 0 || !Number.IsFinite(spaceHeating) || spaceHeating < 0 ||
                !Number.IsFinite(hotWater) || hotWater < 0 || !Number.IsFinite(spaceHeating + hotWater))
                throw new ArgumentException("Room heating demand must be finite and nonnegative.");
            RoomId = roomId; GuestId = guestId; SpaceHeating = spaceHeating; HotWater = hotWater;
        }
    }
}
