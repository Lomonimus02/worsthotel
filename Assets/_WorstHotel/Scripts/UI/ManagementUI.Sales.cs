using System.Linq;
using UnityEngine;
using static WorstHotel.HotelTheme;

namespace WorstHotel
{
    public sealed partial class ManagementUI
    {
        int salesRoomId, salesPrice, salesRevision;
        bool salesOpen;

        public int DisplayedSalesRoomId => IsOperationsOpen && operationsPage == OperationsPage.RoomSales ? salesRoomId : 0;
        public int DisplayedSalesPolicyRevision => DisplayedSalesRoomId != 0 ? salesRevision : -1;
        public int DisplayedSalesPrice => DisplayedSalesRoomId != 0 ? salesPrice : 0;
        public bool DisplayedSalesOpen => DisplayedSalesRoomId != 0 && salesOpen;

        void ShowRoomSales()
        {
            operationsPage = OperationsPage.Sales; focus = 0;
        }

        void SelectRoomSales(int roomId)
        {
            var policy = Session.Simulation.RoomSalesPolicies.FirstOrDefault(item => item.RoomId == roomId);
            if (policy == null) return;
            salesRoomId = roomId; salesOpen = policy.OpenForSale; salesPrice = policy.Price; salesRevision = policy.Revision;
            operationsPage = OperationsPage.RoomSales; focus = 0;
        }

        void UpdateRoomSales()
        {
            if (!Session.Simulation.AutomaticBookingsEnabled) return;
            int row = 0;
            foreach (var policy in Session.Simulation.RoomSalesPolicies.OrderBy(item => item.RoomId))
            {
                int roomId = policy.RoomId;
                var room = Session.Rooms.FirstOrDefault(item => item.Profile.Id == roomId);
                AddOperationsChoice(42, 276 + row++ * 63, 705, 56,
                    "Room " + roomId + " · " + (policy.OpenForSale ? "OPEN" : "CLOSED") + " · $" + policy.Price +
                    "\n" + (room == null ? "Room unavailable" : room.Profile.Label + " · " + PreparationStatus(room, Session.Simulation.Housekeeping?.Find(roomId))),
                    () => SelectRoomSales(roomId));
            }
        }

        void UpdateRoomSalesDraft()
        {
            var policy = Session.Simulation.RoomSalesPolicies.FirstOrDefault(item => item.RoomId == salesRoomId);
            if (policy == null) return;
            bool stale = policy.Revision != salesRevision;
            bool changed = policy.OpenForSale != salesOpen || policy.Price != salesPrice;
            AddOperationsChoice(42, 374, 705, 41, salesOpen ? "Close to new sales" : "Open to new sales", () => salesOpen = !salesOpen, !stale);
            AddOperationsChoice(42, 435, 90, 39, "− $" + Session.Economy.PriceStep,
                () => salesPrice = Mathf.Max(Session.Economy.MinPrice, salesPrice - Session.Economy.PriceStep), !stale && salesPrice > Session.Economy.MinPrice);
            AddOperationsChoice(657, 435, 90, 39, "+ $" + Session.Economy.PriceStep,
                () => salesPrice = Mathf.Min(MaximumGridPrice, salesPrice + Session.Economy.PriceStep), !stale && salesPrice < MaximumGridPrice);
            int revision = salesRevision;
            AddOperationsChoice(42, 606, 705, 43, "Apply sales policy", () =>
            {
                var result = Session.SetRoomSalesPolicy(owner, salesRoomId, salesOpen, salesPrice, revision);
                if (result.Success && !Session.IsLanReplica) SelectRoomSales(salesRoomId);
            }, !stale && changed);
            AddOperationsChoice(42, 657, 705, 35, "Refresh room policy", () => SelectRoomSales(salesRoomId), stale);
        }

        void DrawRoomSales()
        {
            var model = Session.Simulation;
            int open = model.RoomSalesPolicies.Count(item => item.OpenForSale);
            Label(new Rect(42, 196, 705, 69), open + "/" + Session.Rooms.Length + " rooms open to new sales\n" +
                "Ordinary reservations arrive automatically. Set rates and prepare rooms; existing contracts stay in place.", Body, Muted);
            Label(new Rect(42, 663, 705, 30), "Opening a room does not guarantee a booking. Closing it does not cancel existing stays.", Small, Muted);
        }

        void DrawRoomSalesDraft()
        {
            var policy = Session.Simulation.RoomSalesPolicies.FirstOrDefault(item => item.RoomId == salesRoomId);
            if (policy == null) { Label(new Rect(42, 210, 705, 85), "This room's sales policy is unavailable.", Body, Muted); return; }
            bool stale = policy.Revision != salesRevision;
            Label(new Rect(42, 196, 705, 71), "ROOM " + salesRoomId + " · CURRENTLY " + (policy.OpenForSale ? "OPEN" : "CLOSED") +
                " · $" + policy.Price + "\nChanges below take effect only after Apply.", Body);
            Label(new Rect(42, 286, 705, 68), stale ? "This policy changed while you were editing.\nRefresh before applying a new draft." :
                "DRAFT · " + (salesOpen ? "Open to new reservations" : "Closed to new reservations") +
                "\nBooked guests keep their rooms, dates and agreed prices.", Body, stale ? Wine : Teal);
            Label(new Rect(152, 435, 485, 42), "FUTURE RATE  $" + salesPrice, Heading);
            Label(new Rect(42, 493, 705, 97), "Higher rates attract fewer ordinary bookings and raise guest expectations. They do not change a guest's heating demand." +
                "\nA dirty room can sell for a future arrival; it still needs preparation before check-in. A closed room can receive an existing booking by reassignment.", Small, Muted);
        }
    }
}
