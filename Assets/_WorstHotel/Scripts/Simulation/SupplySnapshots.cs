using System;
using System.Linq;

namespace WorstHotel
{
    [Serializable] public sealed class SupplyDepositSnapshot
    {
        public int RoomId, Generation;
    }

    [Serializable] public sealed class SupplySnapshot
    {
        public int Revision, DirtyLinenWaiting, LinenAtLaundry, CleanLinenReserve, BulbsInTransit;
        public float NextDeliveryAt, LastDeliveryAt;
        public int CleanSlots, InitialBulbs, InitialBlankets, LaundrySetCost, BulbPackSize, BulbPackCost;
        public float DeliveryHour;
        public SupplyDepositSnapshot[] Deposits;
    }

    public sealed partial class HotelSimulation
    {
        internal SupplySnapshot CaptureSupplies() => !ContinuousOperations ? null : new SupplySnapshot
        {
            Revision = SupplyRevision, DirtyLinenWaiting = DirtyLinenWaiting, LinenAtLaundry = LinenAtLaundry,
            CleanLinenReserve = CleanLinenReserve, BulbsInTransit = BulbsInTransit,
            NextDeliveryAt = pendingSupplyDeliveryAt, LastDeliveryAt = LastSupplyDeliveryAt,
            CleanSlots = Housekeeping?.Settings.CleanLinenPerDay ?? 0,
            InitialBulbs = Services?.Settings.BulbStock ?? 0, InitialBlankets = Services?.Settings.BlanketStock ?? 0,
            LaundrySetCost = settings.Economy.LaundrySetCost, BulbPackSize = settings.Economy.BulbPackSize,
            BulbPackCost = settings.Economy.BulbPackCost, DeliveryHour = settings.Economy.SupplyDeliveryHour,
            Deposits = depositedLinenGenerations.OrderBy(pair => pair.Key)
                .Select(pair => new SupplyDepositSnapshot { RoomId = pair.Key, Generation = pair.Value }).ToArray()
        };

        // Called only after the complete packet has passed validation, and after stock restoration.
        internal void RestoreSupplies(SupplySnapshot data)
        {
            if (!ContinuousOperations || data == null) return;
            SupplyRevision = data.Revision; DirtyLinenWaiting = data.DirtyLinenWaiting;
            LinenAtLaundry = data.LinenAtLaundry; CleanLinenReserve = data.CleanLinenReserve;
            BulbsInTransit = data.BulbsInTransit; pendingSupplyDeliveryAt = data.NextDeliveryAt;
            LastSupplyDeliveryAt = data.LastDeliveryAt;
            depositedLinenGenerations.Clear();
            foreach (var row in data.Deposits) depositedLinenGenerations.Add(row.RoomId, row.Generation);
        }

        internal void ValidateSupplies(HotelModelSnapshot snapshot)
        {
            if (!ContinuousOperations) return; // The model validator owns rejection of legacy Operations envelopes.
            var data = snapshot.Operations?.Supplies;
            SnapshotValidation.Require(snapshot.HasOperations && data != null, "Missing operating supply state.");
            SnapshotValidation.Require(data.CleanSlots == (Housekeeping?.Settings.CleanLinenPerDay ?? 0) &&
                data.InitialBulbs == (Services?.Settings.BulbStock ?? 0) &&
                data.InitialBlankets == (Services?.Settings.BlanketStock ?? 0) &&
                data.LaundrySetCost == settings.Economy.LaundrySetCost && data.BulbPackSize == settings.Economy.BulbPackSize &&
                data.BulbPackCost == settings.Economy.BulbPackCost && data.DeliveryHour == settings.Economy.SupplyDeliveryHour,
                "Supply configuration differs from this hotel.");
            SnapshotValidation.Require(data.Revision >= 0 && data.DirtyLinenWaiting >= 0 && data.LinenAtLaundry >= 0 &&
                data.CleanLinenReserve >= 0 && data.BulbsInTransit >= 0 && data.BulbsInTransit <= GuestServiceSystem.PhysicalBulbSlots &&
                (long)data.DirtyLinenWaiting + data.LinenAtLaundry + data.CleanLinenReserve <= LinenSetLimit,
                "Invalid bounded supply quantities.");
            SnapshotValidation.Nonnegative(data.NextDeliveryAt);
            SnapshotValidation.Require(Number.IsFinite(data.LastDeliveryAt) && (data.LastDeliveryAt == -1 ||
                data.LastDeliveryAt >= 0 && data.LastDeliveryAt <= snapshot.Time && IsSupplyWindow(data.LastDeliveryAt)),
                "Invalid completed supply delivery time.");
            bool pending = data.LinenAtLaundry > 0 || data.BulbsInTransit > 0;
            SnapshotValidation.Require(pending == (data.NextDeliveryAt > 0) && (!pending ||
                data.NextDeliveryAt > data.LastDeliveryAt && data.NextDeliveryAt == FutureSupplyWindow(snapshot.Time)),
                "Supply delivery must be the next strictly future delivery window.");
            SnapshotValidation.Require(data.BulbsInTransit % settings.Economy.BulbPackSize == 0,
                "Pending bulbs must consist of complete purchased packs.");

            var deposits = SnapshotValidation.Array(data.Deposits, 10);
            SnapshotValidation.Unique(deposits.Select(d => d.RoomId));
            SnapshotValidation.Require(deposits.Length == (Housekeeping == null ? 0 : rooms.Count),
                "Incomplete laundry deposit generations.");
            var linens = SnapshotValidation.Array(snapshot.Linens, 20);
            SnapshotValidation.Unique(linens.Select(l => l.Id));
            if (Housekeeping == null)
                SnapshotValidation.Require(data.DirtyLinenWaiting == 0 && data.LinenAtLaundry == 0 && data.CleanLinenReserve == 0,
                    "Laundry requires a physical linen registry.");
            else
            {
                SnapshotValidation.Require(linens.Length == Housekeeping.Linens.Count, "Incomplete physical linen stock.");
                Housekeeping.ValidateSnapshot(linens);
                foreach (var linen in linens)
                {
                    SnapshotValidation.Require(linen.Generation >= 0, "Invalid linen generation.");
                    SnapshotValidation.Require(linen.Kind == LinenKind.Clean ?
                        linen.Location == LinenLocation.OnShelf || linen.Location == LinenLocation.HeldByPlayer ||
                        linen.Location == LinenLocation.Dropped || linen.Location == LinenLocation.Consumed :
                        linen.Location == LinenLocation.OnBed || linen.Location == LinenLocation.HeldByPlayer ||
                        linen.Location == LinenLocation.Dropped || linen.Location == LinenLocation.InHamper || linen.Location == LinenLocation.Consumed,
                        "Linen kind and physical location disagree.");
                }
                foreach (var deposit in deposits)
                {
                    var dirty = linens.FirstOrDefault(l => l.Kind == LinenKind.Dirty && l.SourceRoomId == deposit.RoomId);
                    SnapshotValidation.Require(rooms.ContainsKey(deposit.RoomId) && dirty != null && deposit.Generation >= 0 &&
                        deposit.Generation <= dirty.Generation && (dirty.Location == LinenLocation.InHamper ?
                        deposit.Generation == dirty.Generation : dirty.Generation == 0 || deposit.Generation < dirty.Generation),
                        "Laundry deposit does not match its physical generation.");
                }
                var turnover = SnapshotValidation.Array(snapshot.Turnover, 10);
                int emptyBeds = turnover.Count(t => t.Step == RoomPreparationStep.NeedsCleanLinen || t.Step == RoomPreparationStep.MakingBed);
                int unused = linens.Count(l => l.Kind == LinenKind.Clean && l.Location != LinenLocation.Consumed);
                SnapshotValidation.Require((long)data.DirtyLinenWaiting + data.LinenAtLaundry + data.CleanLinenReserve +
                    unused + rooms.Count - emptyBeds == LinenSetLimit, "Linen sets were duplicated or lost.");
            }

            if (Services == null)
                SnapshotValidation.Require(data.BulbsInTransit == 0, "Bulb delivery requires a physical service registry.");
            else
            {
                SnapshotValidation.Require(snapshot.HasServices && snapshot.ServiceLayer != null, "Missing supply item registry.");
                var items = SnapshotValidation.Array(snapshot.ServiceLayer.Items, 256);
                SnapshotValidation.Unique(items.Select(i => i.Id));
                SnapshotValidation.Require(Enumerable.Range(0, data.InitialBulbs).All(slot =>
                    items.Any(i => i.Id == "bulb:" + slot && i.Kind == ServiceItemKind.ReplacementBulb)) &&
                    Enumerable.Range(0, data.InitialBlankets).All(slot =>
                    items.Any(i => i.Id == "blanket:" + slot && i.Kind == ServiceItemKind.Blanket)),
                    "Initial physical supply slots cannot disappear.");
                foreach (var item in items.Where(i => i.Kind != ServiceItemKind.Luggage))
                {
                    SnapshotValidation.Require(item.Kind == ServiceItemKind.Blanket || item.Kind == ServiceItemKind.ReplacementBulb,
                        "Unknown physical supply kind.");
                    string prefix = item.Kind == ServiceItemKind.Blanket ? "blanket:" : "bulb:";
                    SnapshotValidation.Require(Enumerable.Range(0, 6).Any(i => item.Id == prefix + i) && item.Generation >= 0,
                        "Invalid physical supply slot or generation.");
                    if (item.Kind == ServiceItemKind.ReplacementBulb)
                        SnapshotValidation.Require(string.IsNullOrEmpty(item.GuestId) &&
                            (item.Location == ServiceItemLocation.OnShelf || item.Location == ServiceItemLocation.HeldByPlayer ||
                             item.Location == ServiceItemLocation.Dropped || item.Location == ServiceItemLocation.Delivered),
                            "Invalid replacement bulb state.");
                }
                int space = Enumerable.Range(0, GuestServiceSystem.PhysicalBulbSlots).Count(slot =>
                {
                    var bulb = items.FirstOrDefault(i => i.Id == "bulb:" + slot);
                    return bulb == null || bulb.Kind == ServiceItemKind.ReplacementBulb &&
                        bulb.Location == ServiceItemLocation.Delivered && bulb.Generation < int.MaxValue;
                });
                SnapshotValidation.Require(data.BulbsInTransit <= space, "Bulb orders overbook physical stock slots.");
            }
            if (snapshot.Epoch == AppliedSnapshotEpoch)
            {
                SnapshotValidation.Require(data.Revision >= SupplyRevision && data.LastDeliveryAt >= LastSupplyDeliveryAt,
                    "Supply state cannot move backwards within a host epoch.");
                foreach (var deposit in deposits)
                    SnapshotValidation.Require(!depositedLinenGenerations.TryGetValue(deposit.RoomId, out int previous) ||
                        deposit.Generation >= previous, "A processed dirty-linen generation cannot be replayed.");
                foreach (var linen in linens)
                    SnapshotValidation.Require(Housekeeping == null || linen.Generation >= Housekeeping.FindLinen(linen.Id).Generation,
                        "Physical linen generations cannot move backwards.");
                if (Services != null)
                    foreach (var previous in Services.Items.Where(i => i.Kind != ServiceItemKind.Luggage))
                    {
                        var incoming = snapshot.ServiceLayer.Items.FirstOrDefault(i => i.Id == previous.Id);
                        SnapshotValidation.Require(incoming != null && incoming.Kind == previous.Kind && incoming.Generation >= previous.Generation,
                            "Physical supply slots cannot disappear or replay an older generation.");
                    }
                if (data.Revision == SupplyRevision)
                    SnapshotValidation.Require(data.DirtyLinenWaiting == DirtyLinenWaiting && data.LinenAtLaundry == LinenAtLaundry &&
                        data.CleanLinenReserve == CleanLinenReserve && data.BulbsInTransit == BulbsInTransit &&
                        data.NextDeliveryAt == pendingSupplyDeliveryAt && data.LastDeliveryAt == LastSupplyDeliveryAt &&
                        deposits.All(d => depositedLinenGenerations.TryGetValue(d.RoomId, out int previous) && d.Generation == previous),
                        "Supply quantities changed without a new revision.");
            }
        }

        bool IsSupplyWindow(float at)
        {
            int day = Calendar.DayAt(at);
            // DayAt can round across midnight when a configured delivery window is 00:00.
            return Calendar.At(day, settings.Economy.SupplyDeliveryHour) == at ||
                day > 1 && Calendar.At(day - 1, settings.Economy.SupplyDeliveryHour) == at ||
                Calendar.At(day + 1, settings.Economy.SupplyDeliveryHour) == at;
        }
    }
}
