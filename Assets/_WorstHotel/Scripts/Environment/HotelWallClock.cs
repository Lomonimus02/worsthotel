using UnityEngine;
namespace WorstHotel
{
    public sealed class HotelWallClock : MonoBehaviour
    {
        public Transform hourHand, minuteHand;
        public TextMesh dateCard;
        void LateUpdate()
        {
            var calendar = GameSession.Instance?.Simulation?.Calendar;
            if (calendar == null) return;
            if (hourHand) hourHand.localRotation = Quaternion.Euler(0, 0, -calendar.Hour * 30);
            if (minuteHand) minuteHand.localRotation = Quaternion.Euler(0, 0, -calendar.Hour * 360);
            if (dateCard) dateCard.text = "DAY " + calendar.Day + "   " + calendar.DisplayTime;
        }
    }
}
