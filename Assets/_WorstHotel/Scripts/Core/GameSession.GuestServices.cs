using System;
using System.Linq;
using UnityEngine;

namespace WorstHotel
{
    public sealed partial class GameSession
    {
        sealed class PhoneGrant { public HotelSimulation model; public Transform source; public float expires; }
        readonly PhoneGrant[] phoneGrants = new PhoneGrant[2];
        bool ServiceWorkPhase => Phase == DayPhase.Service || Phase == DayPhase.Planning;
        bool ServiceTarget(int actor, HotelInteractable target, out PlayerInteractor player) =>
            PlayerInteractor.TryGetPlayer(actor, out player) && !IsLanReplica && ServiceWorkPhase && Simulation?.Services != null &&
            player.CanAct && target && target.isActiveAndEnabled && player.Focused == target &&
            Vector3.Distance(player.transform.position, target.transform.position) <= 4;
        ServiceSupplyItem HeldServiceItem(PlayerInteractor player)
        {
            var item = player && player.HeldBody ? player.HeldBody.GetComponent<ServiceSupplyItem>() : null;
            return item && item.BoundSimulation == Simulation && item.State?.Location == ServiceItemLocation.HeldByPlayer &&
                item.State.PlayerId == player.ActorId ? item : null;
        }
        public CommandResult TakeServiceItem(int actor, ServiceSupplyItem item)
        {
            if (IsLanReplica || !ServiceWorkPhase || !item || item.BoundSimulation != Simulation ||
                !PlayerInteractor.TryGetPlayer(actor, out var player) || !player.CanAct || player.HeldBody ||
                !player.FocusedPickup || player.FocusedPickup.gameObject != item.gameObject ||
                Vector3.Distance(player.transform.position, item.transform.position) > 4)
                return CommandResult.Fail("Approach an available physical supply first.");
            return GuestCommand(Simulation.TakeServiceItem(actor, item.ItemId));
        }
        public CommandResult DropServiceItem(int actor, ServiceSupplyItem item)
        {
            if (IsLanReplica || !item || item.BoundSimulation != Simulation || item.State?.PlayerId != actor || item.LastCarrierId != actor)
                return CommandResult.Fail("This item is not held by that employee.");
            return GuestCommand(Simulation.DropServiceItem(actor, item.ItemId));
        }
        public CommandResult ReturnServiceItem(int actor, ServiceSupplyItem item)
        {
            if (!PlayerInteractor.TryGetPlayer(actor, out var player) || !(player.Focused is ServiceStockShelfInteraction shelf) ||
                !ServiceTarget(actor, shelf, out player) || HeldServiceItem(player) != item || item.State.Kind != shelf.kind ||
                !item.SourceAnchor || Vector3.Distance(item.SourceAnchor.position, shelf.transform.position) > 4)
                return CommandResult.Fail("Carry the unused supply back to its shelf.");
            var result = Simulation.ReturnServiceItem(actor, item.ItemId);
            if (result.Success) player.ReleaseGrab();
            return GuestCommand(result);
        }
        public CommandResult DeliverBlanket(int actor, RoomBlanketDeliveryInteraction target)
        {
            if (!ServiceTarget(actor, target, out var player) || HeldServiceItem(player)?.State.Kind != ServiceItemKind.Blanket)
                return CommandResult.Fail("Carry a spare blanket to the occupied bed.");
            var room = Array.Find(Rooms, r => r.Profile.Id == target.roomId);
            var result = Simulation.DeliverBlanket(actor, room?.GuestId);
            if (result.Success) player.ReleaseGrab();
            return GuestCommand(result);
        }
        public CommandResult ReplaceRoomBulb(int actor, RoomLampInteraction target)
        {
            if (!ServiceTarget(actor, target, out var player) || HeldServiceItem(player)?.State.Kind != ServiceItemKind.ReplacementBulb)
                return CommandResult.Fail("Carry a replacement bulb to the failed lamp.");
            var result = Simulation.ReplaceRoomBulb(actor, target.roomId);
            if (result.Success) player.ReleaseGrab();
            return GuestCommand(result);
        }
        public CommandResult StoreLuggage(int actor, LuggageStorageZone target)
        {
            if (!ServiceTarget(actor, target, out var player) || HeldServiceItem(player)?.State.Kind != ServiceItemKind.Luggage)
                return CommandResult.Fail("Carry the suitcase to luggage storage.");
            var item = HeldServiceItem(player);
            var result = Simulation.StoreLuggage(actor, item.State.GuestId);
            if (result.Success) player.ReleaseGrab();
            return GuestCommand(result);
        }
        public CommandResult SetRadiatorSetting(int actor, RadiatorValveInteraction target, int setting)
        {
            if (!ServiceTarget(actor, target, out _)) return CommandResult.Fail("Stand beside the room's radiator valve.");
            return GuestCommand(Simulation.SetRadiatorSetting(actor, target.roomId, setting));
        }
        public CommandResult RespondToService(int actor, string caseId, bool accept)
        {
            if (ForwardLan(LanCommandKind.RespondService, caseId, amount: accept ? 1 : 0)) return CommandResult.Ok(LastMessage);
            if (actor < 0 || actor > 1 || Phase != DayPhase.Service) return CommandResult.Fail("Service decisions require an active stay.");
            return GuestCommand(Simulation.RespondToService(actor, caseId, accept));
        }
        public CommandResult AcknowledgeService(int actor, string caseId)
        {
            if (ForwardLan(LanCommandKind.AcknowledgeService, caseId)) return CommandResult.Ok(LastMessage);
            if (actor < 0 || actor > 1 || Phase != DayPhase.Service) return CommandResult.Fail("Service decisions require an active stay.");
            return GuestCommand(Simulation.AcknowledgeService(actor, caseId));
        }
        public CommandResult OpenReceptionServiceBoard(int actor, HotelInteractable target)
        {
            if (!(target is ReceptionServiceBoardInteraction) || !ServiceTarget(actor, target, out _))
                return CommandResult.Fail("Approach the reception service board.");
            if (LanSession.Instance && LanSession.Instance.Role == LanRole.Host && actor == 1)
                LanSession.Instance.RequestRemoteServiceDesk(false);
            else ManagementUI.Instance?.OpenReceptionServiceBoard(actor);
            return CommandResult.Ok("Reception services and upcoming work.");
        }
        public CommandResult OpenWakePhone(int actor, HotelInteractable target)
        {
            if (!(target is ReceptionPhoneInteraction) || !ServiceTarget(actor, target, out _))
                return CommandResult.Fail("Use the physical reception telephone.");
            if (LanSession.Instance && LanSession.Instance.Role == LanRole.Host && actor == 1)
                LanSession.Instance.RequestRemoteServiceDesk(true);
            else ManagementUI.Instance?.OpenWakePhone(actor);
            phoneGrants[actor] = new PhoneGrant { model = Simulation, source = target.transform, expires = Time.unscaledTime + 60 };
            return CommandResult.Ok("Reception wake-up calls.");
        }
        public void CloseWakePhone(int actor)
        {
            if (ForwardLan(LanCommandKind.CloseWakePhone)) return;
            if (actor >= 0 && actor < phoneGrants.Length) phoneGrants[actor] = null;
        }
        internal void ClearServicePhone(int actor) { if (actor >= 0 && actor < phoneGrants.Length) phoneGrants[actor] = null; }
        public CommandResult CompleteWakeUpCall(int actor, string promiseId)
        {
            if (ForwardLan(LanCommandKind.CompleteWakeUp, promiseId)) return CommandResult.Ok(LastMessage);
            var grant = actor >= 0 && actor < phoneGrants.Length ? phoneGrants[actor] : null;
            if (grant == null || grant.model != Simulation || !grant.source || Time.unscaledTime > grant.expires ||
                Phase != DayPhase.Service || !PlayerInteractor.TryGetPlayer(actor, out var player) ||
                Vector3.Distance(player.transform.position, grant.source.position) > 4)
                return GuestCommand(CommandResult.Fail("Place the call using the reception telephone."));
            return GuestCommand(Simulation.CompleteWakeUpCall(actor, promiseId));
        }
        public CommandResult DebugForceService(string guest, ServiceKind kind) => GuestCommand(Simulation.DebugForceService(guest, kind));
        public CommandResult DebugSetBlanketStock(int count) => GuestCommand(Simulation.DebugSetBlanketStock(count));
        public CommandResult DebugSetMildCold(string guest) => GuestCommand(Simulation.DebugSetMildCold(guest));
        public CommandResult DebugSetRadiator(int room, int setting) => GuestCommand(Simulation.SetRadiatorSetting(0, room, setting));
        public CommandResult DebugBreakLamp(int room) => GuestCommand(Simulation.BreakRoomLamp(room));
        public void AdvanceToNextPromise()
        {
            if (IsLanReplica || Phase != DayPhase.Service || Simulation.Services == null) return;
            var next = Simulation.Services.Promises.Where(p => p.Status == PromiseStatus.Accepted && p.DueTime > Simulation.Elapsed)
                .OrderBy(p => p.DueTime).FirstOrDefault();
            if (next != null) AdvanceTime(next.DueTime - Simulation.Elapsed);
        }
    }
}
