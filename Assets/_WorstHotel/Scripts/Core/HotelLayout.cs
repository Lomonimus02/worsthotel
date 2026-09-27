namespace WorstHotel
{
    public static class HotelLayout
    {
        public const int RoomCount = 10;
        public static float RoomZ(int index) => index < 6 ? 10 + index / 2 * 7 : 46 + (index - 6) / 2 * 7;
    }
}
