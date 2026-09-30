using UnityEngine;

namespace WorstHotel
{
    /// <summary>Physical delivery slip beside the existing clean-stock shelves, never a HUD.</summary>
    public sealed class SupplyDeliveryNotice : MonoBehaviour
    {
        public TextMesh label;
        void LateUpdate()
        {
            var hotel = GameSession.Instance ? GameSession.Instance.Simulation : null;
            if (!label || hotel == null) return;
            string next = Number.IsFinite(hotel.NextSupplyDeliveryAt) ?
                "Due " + GuestLabels.HotelMoment(hotel, hotel.NextSupplyDeliveryAt) : "No delivery pending";
            string last = hotel.LastSupplyDeliveryAt >= 0 ?
                "Received " + GuestLabels.HotelMoment(hotel, hotel.LastSupplyDeliveryAt) : "Order at the supply ledger";
            label.text = "SERVICE DELIVERY\n" + next + "\n" + last + "\nCollect stock from these shelves";
        }
    }
}
