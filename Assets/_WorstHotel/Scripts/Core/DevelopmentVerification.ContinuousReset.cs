#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Linq;
using UnityEngine;

namespace WorstHotel
{
    public sealed partial class DevelopmentVerification
    {
        // Called after NewGame and the caller's presentation-settle frames. All assertions
        // observe the replacement model and existing bodies; no reset/repair command is issued.
        void ValidateContinuousFreshReset()
        {
            var fresh = session.Simulation;
            Require(fresh.ContinuousOperations && fresh.Guests.Count == 0, "fresh-reset inventory belongs to the new empty continuous hotel");
            Require(fresh.AutomaticBookingsEnabled && fresh.RoomSalesPolicies.Count == session.Rooms.Length &&
                fresh.RoomSalesPolicies.Count(row => row.OpenForSale) == fresh.Operations.Sales.InitiallyOpenRooms &&
                fresh.RoomSalesPolicies.All(row => row.Price == fresh.Operations.Sales.InitialPrice && row.Revision == 1) &&
                fresh.SalesDecisionCursors.Count == 2 && fresh.SalesDecisionCursors.All(row => row.NextOfferIndex == 0),
                "new game resets sales policies and timed demand without instantly creating reservations");
            Require(fresh.Keys.Items.Count == session.Rooms.Length && fresh.Keys.Items.All(item =>
                item.Location == RoomKeyLocation.OnRack && !item.PlayerId.HasValue && string.IsNullOrEmpty(item.GuestId)),
                "fresh reset returns exactly one unowned key per room to its rack");
            var physicalKeys = FindObjectsByType<RoomKeyItem>(FindObjectsSortMode.None);
            Require(physicalKeys.Length == fresh.Keys.Items.Count && physicalKeys.All(item =>
                ReferenceEquals(item.BoundSimulation, fresh) && ReferenceEquals(item.State, fresh.Keys.Find(item.roomId)) &&
                item.State.Location == RoomKeyLocation.OnRack), "authored key bodies are bound to the fresh on-rack identities");
            Require(coop.Players.Where(player => player).All(player => player.Interactor && player.Interactor.HeldBody == null),
                "fresh reset leaves no staff member carrying an old key, linen, blanket, suitcase or heater");

            var services = fresh.Services;
            Require(services != null && services.Cases.Count == 0 && services.Promises.Count == 0 && services.Responses.Count == 0 &&
                services.Intents.Count == 0 && services.IncomingCall == null, "fresh reset clears cases, promises, responses, delivery intents and ringing calls");
            Require(services.BlanketsAvailable == services.Settings.BlanketStock && services.BulbsAvailable == services.Settings.BulbStock &&
                services.Items.Count == services.Settings.BlanketStock + services.Settings.BulbStock && services.LastRefillDay == 1 &&
                services.Items.All(item => item.Kind != ServiceItemKind.Luggage && item.Location == ServiceItemLocation.OnShelf &&
                    !item.PlayerId.HasValue && !item.LastPlayerId.HasValue && !item.RoomId.HasValue && string.IsNullOrEmpty(item.GuestId)),
                "fresh blanket and bulb stock has no old carrier, guest, pending parcel, delivered item or suitcase");
            Require(FindObjectsByType<ServiceSupplyItem>(FindObjectsSortMode.None).All(item => ReferenceEquals(item.BoundSimulation, fresh) &&
                !item.LastCarrierId.HasValue && (item.State == null || item.State.Location == ServiceItemLocation.OnShelf)),
                "physical service bodies bind to new stock or unused hidden slots without retained carry state");

            var linen = fresh.Housekeeping;
            Require(linen.Tasks.Count == 0 && !linen.HasPendingWork && linen.CurrentTask == null && linen.LastRefillDay == 1,
                "fresh reset clears all dirty-room and bed-making work");
            Require(linen.Linens.Count(item => item.Kind == LinenKind.Clean) == linen.Settings.CleanLinenPerDay &&
                linen.Linens.Count(item => item.Kind == LinenKind.Dirty) == session.Rooms.Length &&
                linen.Linens.All(item => !item.PlayerId.HasValue && item.Location ==
                    (item.Kind == LinenKind.Clean ? LinenLocation.OnShelf : LinenLocation.Consumed)),
                "fresh linen shelf stock is full; unused dirty slots are consumed placeholders, not carried/dropped/hamper bundles");

            var heaters = FindObjectsByType<PortableHeater>(FindObjectsSortMode.None);
            Require(heaters.Length == 2 && fresh.Heaters.Items.Count == heaters.Length && heaters.All(item =>
                !item.IsCarried && item.State != null && ReferenceEquals(item.State, fresh.Heaters.Find(item.heaterId))) &&
                fresh.Heaters.Items.All(item => !item.SwitchedOn && item.EffectiveHeatOutput == 0 && item.DemandedElectricalLoad == 0),
                "both authored heaters have new registry state and are switched off, uncarried and drawing no power");
            Require(fresh.Electrical != null && (!fresh.Electrical.IsCapacityUpgraded("A") && !fresh.Electrical.IsCapacityUpgraded("B")) &&
                fresh.Electrical.Circuits.All(circuit => !circuit.Tripped && circuit.TripCount == 0 && circuit.HasPower),
                "fresh circuits have power with no old trips or purchased branch upgrade");
            Require(!fresh.Boiler.Failed && !fresh.Boiler.EmergencyPatchActive && !fresh.Boiler.MaintenanceInProgress &&
                fresh.Boiler.MaintenanceEndsAt == 0 && !fresh.Boiler.CapacityUpgradePurchased && fresh.Boiler.ReliefActorId == -1 &&
                fresh.PeriodMaintenanceSpend == 0 && fresh.PeriodCapitalSpend == 0,
                "fresh boiler has no old failure, patch, maintenance job, relief owner, upgrade or equipment debit");
            facts.Add("ContinuousFreshInventoryReset=True KeysOnRack=" + physicalKeys.Length + " CleanLinen=" + linen.Settings.CleanLinenPerDay +
                " Blankets=" + services.BlanketsAvailable + " Bulbs=" + services.BulbsAvailable +
                " HeatersOff=" + heaters.Length + " OldServiceRecords=0 OldCarriers=0 EquipmentFaultsAndUpgradesCleared=True");
        }
    }
}
#endif
