using System.Linq;

namespace WorstHotel
{
    public sealed partial class GuestServiceSystem
    {
        internal const int PhysicalBulbSlots = 6;

        internal int BulbDeliverySpace => Enumerable.Range(0, PhysicalBulbSlots).Count(slot =>
        {
            var item = FindItem("bulb:" + slot);
            return item == null || item.Kind == ServiceItemKind.ReplacementBulb &&
                item.Location == ServiceItemLocation.Delivered && item.Generation < int.MaxValue;
        });

        internal bool DeliverPurchasedBulbs(int quantity)
        {
            if (simulation.IsReadOnlyMirror || quantity < 0 || quantity > BulbDeliverySpace) return false;
            for (int slot = 0; slot < PhysicalBulbSlots && quantity > 0; slot++)
            {
                var item = FindItem("bulb:" + slot);
                if (item != null && (item.Location != ServiceItemLocation.Delivered || item.Generation == int.MaxValue)) continue;
                if (item == null)
                {
                    item = new ServiceItemState("bulb:" + slot, ServiceItemKind.ReplacementBulb, 0);
                    items.Add(item);
                }
                else item.Generation++;
                item.Location = ServiceItemLocation.OnShelf;
                item.PlayerId = item.LastPlayerId = null;
                item.GuestId = null; item.RoomId = null;
                quantity--;
                ItemChanged?.Invoke(item);
            }
            return true;
        }

        internal void RecoverBlanketsAfterTurnover(int roomId)
        {
            if (simulation.IsReadOnlyMirror || !simulation.ContinuousOperations) return;
            foreach (var item in items.Where(i => i.Kind == ServiceItemKind.Blanket &&
                i.Location == ServiceItemLocation.Delivered && i.RoomId == roomId).ToArray())
            {
                var guest = item.GuestId == null ? null : Guest(item.GuestId);
                if (guest != null && !Departed(guest)) continue;
                if (item.Generation == int.MaxValue) continue;
                item.Location = ServiceItemLocation.OnShelf;
                item.GuestId = null; item.RoomId = null;
                item.PlayerId = item.LastPlayerId = null;
                item.Generation++;
                ItemChanged?.Invoke(item);
            }
        }
    }
}
