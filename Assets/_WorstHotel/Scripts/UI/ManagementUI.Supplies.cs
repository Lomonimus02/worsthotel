namespace WorstHotel
{
    public sealed partial class ManagementUI
    {
        void SupplyPages()
        {
            var model = Session.Simulation;
            var economy = Session.Economy;
            bookHeading = "SUPPLIES & LAUNDRY";
            if (!model.ContinuousOperations || model.Housekeeping == null || model.Services == null)
            {
                bookCopy = "Supply orders require an open continuous hotel.";
                return;
            }
            if (bookPage == 1)
            {
                bookCopy = "SERVICE TERMS\n\nOne used bed = one linen set.\nStrip the bed and bring the dirty bundle to the hamper.\n\nLaundry costs $" + economy.LaundrySetCost +
                    " per set.\nBulbs: " + economy.BulbPackSize + " for $" + economy.BulbPackCost +
                    ".\n\nOrders are paid immediately.\nContract money is not reserved.\n\nDelivery: next " + SupplyDeliveryClock(economy.SupplyDeliveryHour) +
                    ".\nFind clean stock on these shelves.\nBlankets are reused after turnover.\nNothing is ordered automatically.";
                BookChoice("‹ Stock and orders", () => BookPage(0));
                return;
            }
            string next = Number.IsFinite(model.NextSupplyDeliveryAt) ? GuestLabels.HotelMoment(model, model.NextSupplyDeliveryAt) : "No order pending";
            bookCopy = "CLEAN SETS ON SHELF   " + model.CleanLinenAvailable +
                "\nDelivered reserve   " + model.CleanLinenReserve +
                "\n\nDIRTY WAITING   " + model.DirtyLinenWaiting +
                "\nAT LAUNDRY   " + model.LinenAtLaundry +
                "\n\nBULBS ON SHELF   " + model.Services.BulbsAvailable +
                "\nBulbs ordered   " + model.BulbsInTransit +
                "\n\nNEXT DELIVERY\n" + next +
                "\n\nAvailable cash   $" + model.Economy.Cash +
                "\nLaundry   $" + economy.LaundrySetCost + " / set";
            int revision = model.SupplyRevision;
            var laundry = model.CanOrderLaundry(owner, revision);
            var bulbs = model.CanOrderBulbs(owner, revision);
            BookChoice("Send " + model.DirtyLinenWaiting + " sets · $" + ((long)model.DirtyLinenWaiting * economy.LaundrySetCost),
                () => Session.OrderLaundry(owner, revision), laundry.Success);
            BookChoice("Order " + economy.BulbPackSize + " bulbs · $" + economy.BulbPackCost,
                () => Session.OrderBulbs(owner, revision), bulbs.Success);
            BookChoice("Service terms ›", () => BookPage(1));
        }

        static string SupplyDeliveryClock(float hour) => ((int)hour).ToString("00") + ":" + ((int)((hour % 1) * 60)).ToString("00");
    }
}
