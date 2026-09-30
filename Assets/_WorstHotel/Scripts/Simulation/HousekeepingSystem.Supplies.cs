using System.Linq;

namespace WorstHotel
{
    public sealed partial class HousekeepingSystem
    {
        // Delivered reserve materializes only in unused physical slots, never over carried linen.
        internal int PlaceDeliveredCleanLinen(int quantity)
        {
            if (ReadOnlyMirror || quantity <= 0) return 0;
            var available = Linens.Where(l => l.Kind == LinenKind.Clean &&
                l.Location == LinenLocation.Consumed && l.Generation < int.MaxValue).Take(quantity).ToArray();
            foreach (var linen in available)
            {
                linen.Location = LinenLocation.OnShelf;
                linen.PlayerId = null;
                linen.Generation++;
                LinenChanged?.Invoke(linen, "paid laundry returned to the clean shelf");
            }
            return available.Length;
        }
    }
}
