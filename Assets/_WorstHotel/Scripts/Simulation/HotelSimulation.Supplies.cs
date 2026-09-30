using System;
using System.Collections.Generic;
using System.Linq;

namespace WorstHotel
{
    public sealed partial class HotelSimulation
    {
        public int SupplyRevision { get; private set; }
        public int DirtyLinenWaiting { get; private set; }
        public int LinenAtLaundry { get; private set; }
        public int CleanLinenReserve { get; private set; }
        public int BulbsInTransit { get; private set; }
        public int CleanLinenAvailable => Housekeeping?.Linens.Count(l => l.Kind == LinenKind.Clean &&
            l.Location == LinenLocation.OnShelf) ?? 0;
        /// <summary>Positive infinity means no pending delivery; snapshots use a finite zero sentinel.</summary>
        public float NextSupplyDeliveryAt => pendingSupplyDeliveryAt > 0 ? pendingSupplyDeliveryAt : float.PositiveInfinity;
        public float LastSupplyDeliveryAt { get; private set; } = -1;
        float pendingSupplyDeliveryAt;
        readonly Dictionary<int, int> depositedLinenGenerations = new Dictionary<int, int>();
        bool suppliesInitialized;
        int LinenSetLimit => (Housekeeping?.Settings.CleanLinenPerDay ?? 0) + rooms.Count;

        internal void InitializeSupplies()
        {
            if (suppliesInitialized || !ContinuousOperations) return;
            suppliesInitialized = true;
            if (Housekeeping == null) return;
            foreach (int id in rooms.Keys) depositedLinenGenerations.Add(id, 0);
            Housekeeping.PaidLaundryEnabled = true;
            Housekeeping.ValidateSupplyDeposit = CanRecordSupplyDeposit;
            Housekeeping.DirtyLinenDeposited += RecordSupplyDeposit;
            Housekeeping.BedPrepared += OnSupplyBedPrepared;
        }

        CommandResult CanRecordSupplyDeposit(LinenBundleState linen)
        {
            if (IsReadOnlyMirror || OwnershipLost) return CommandResult.Fail("This hotel cannot receive laundry.");
            if (SupplyRevision == int.MaxValue || linen.SourceRoomId == null ||
                !depositedLinenGenerations.TryGetValue(linen.SourceRoomId.Value, out int previous) || linen.Generation <= previous ||
                (long)DirtyLinenWaiting + LinenAtLaundry + CleanLinenReserve >= LinenSetLimit)
                return CommandResult.Fail("This dirty set cannot be recorded again or exceed the hotel's linen stock.");
            return CommandResult.Ok();
        }

        void RecordSupplyDeposit(LinenBundleState linen)
        {
            depositedLinenGenerations[linen.SourceRoomId.Value] = linen.Generation;
            DirtyLinenWaiting++;
            AdvanceSupplyRevision();
        }

        void OnSupplyBedPrepared(int roomId)
        {
            if (IsReadOnlyMirror) return;
            Services?.RecoverBlanketsAfterTurnover(roomId);
            FillCleanSupplyShelf();
        }

        void AdvanceSupplyRevision() { if (SupplyRevision < int.MaxValue) SupplyRevision++; }

        float FutureSupplyWindow(float now)
        {
            int day = Calendar.DayAt(now);
            float at = Calendar.At(day, settings.Economy.SupplyDeliveryHour);
            if (at <= now) at = Calendar.At(day + 1, settings.Economy.SupplyDeliveryHour);
            return at;
        }

        CommandResult CanOrderSupply(int actorId, int expectedRevision)
        {
            if (!suppliesInitialized || !ContinuousOperations || !Running || OwnershipLost)
                return CommandResult.Fail("Supply orders require an open continuous hotel.");
            if (actorId < 0 || actorId > 1) return CommandResult.Fail("Unknown staff member.");
            if (expectedRevision != SupplyRevision || SupplyRevision == int.MaxValue)
                return CommandResult.Fail("Supply stock changed. Review the current ledger before ordering.");
            if (NextSupplyDeliveryAt > 0 && NextSupplyDeliveryAt <= Elapsed)
                return CommandResult.Fail("The due supply delivery must be received before placing another order.");
            float delivery = FutureSupplyWindow(Elapsed);
            if (!Number.IsFinite(delivery) || delivery <= Elapsed)
                return CommandResult.Fail("The next supply delivery time cannot be represented.");
            return CommandResult.Ok();
        }

        public CommandResult CanOrderLaundry(int actorId, int expectedRevision)
        {
            var allowed = CanOrderSupply(actorId, expectedRevision);
            if (!allowed.Success) return allowed;
            if (Housekeeping == null || DirtyLinenWaiting == 0) return CommandResult.Fail("Deposit dirty linen in the hamper first.");
            long cost = (long)DirtyLinenWaiting * settings.Economy.LaundrySetCost;
            if (cost > int.MaxValue || cost > Economy.Cash) return CommandResult.Fail("Not enough cash for this laundry order.");
            return CommandResult.Ok();
        }

        public CommandResult OrderLaundry(int actorId, int expectedRevision)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            var allowed = CanOrderLaundry(actorId, expectedRevision);
            if (!allowed.Success) return allowed;
            int quantity = DirtyLinenWaiting;
            var paid = PaySupplyOrder(quantity * settings.Economy.LaundrySetCost, true);
            if (!paid.Success) return paid;
            LinenAtLaundry += quantity; DirtyLinenWaiting = 0;
            pendingSupplyDeliveryAt = FutureSupplyWindow(Elapsed);
            AdvanceSupplyRevision();
            SignalEvent(quantity + " linen sets sent to laundry; return at the next supply delivery.");
            return CommandResult.Ok("Laundry sent. Clean sets return at the scheduled delivery.");
        }

        public CommandResult CanOrderBulbs(int actorId, int expectedRevision)
        {
            var allowed = CanOrderSupply(actorId, expectedRevision);
            if (!allowed.Success) return allowed;
            if (Services == null || Services.BulbDeliverySpace - BulbsInTransit < settings.Economy.BulbPackSize)
                return CommandResult.Fail("There is not enough unreserved space for a complete bulb pack.");
            if (Economy.Cash < settings.Economy.BulbPackCost) return CommandResult.Fail("Not enough cash for a bulb pack.");
            return CommandResult.Ok();
        }

        public CommandResult OrderBulbs(int actorId, int expectedRevision)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            var allowed = CanOrderBulbs(actorId, expectedRevision);
            if (!allowed.Success) return allowed;
            var paid = PaySupplyOrder(settings.Economy.BulbPackCost, false);
            if (!paid.Success) return paid;
            BulbsInTransit += settings.Economy.BulbPackSize;
            pendingSupplyDeliveryAt = FutureSupplyWindow(Elapsed);
            AdvanceSupplyRevision();
            SignalEvent("Bulb pack ordered for the next supply delivery.");
            return CommandResult.Ok("Bulbs ordered. They will appear on the supply shelf at delivery.");
        }

        void FillCleanSupplyShelf()
        {
            if (CleanLinenReserve == 0 || Housekeeping == null) return;
            int placed = Housekeeping.PlaceDeliveredCleanLinen(CleanLinenReserve);
            CleanLinenReserve -= placed;
            if (placed > 0) AdvanceSupplyRevision();
        }

        internal void TickSupplies()
        {
            if (!suppliesInitialized || IsReadOnlyMirror || !Running || OwnershipLost) return;
            if (NextSupplyDeliveryAt > 0 && Elapsed >= NextSupplyDeliveryAt)
            {
                if (BulbsInTransit > 0 && (Services == null || !Services.DeliverPurchasedBulbs(BulbsInTransit)))
                    throw new InvalidOperationException("Reserved bulb delivery slots are unavailable.");
                CleanLinenReserve += LinenAtLaundry;
                LinenAtLaundry = 0; BulbsInTransit = 0;
                LastSupplyDeliveryAt = pendingSupplyDeliveryAt; pendingSupplyDeliveryAt = 0;
                AdvanceSupplyRevision();
                FillCleanSupplyShelf();
                SignalEvent("Supply delivery arrived: paid linen and bulbs are available in storage.");
            }
            else FillCleanSupplyShelf();
        }
    }
}
