using System;

namespace WorstHotel
{
    public sealed partial class GameSession
    {
        bool CanHandleLinen(PlayerInteractor player, HotelInteractable target) => Simulation?.Housekeeping != null &&
            (Phase == DayPhase.Planning || Phase == DayPhase.Service) && target && target.isActiveAndEnabled && player && player.CanAct &&
            player.Focused == target && player.HeldBody;

        LinenBundleItem HeldLinen(PlayerInteractor player)
        {
            var item = player && player.HeldBody ? player.HeldBody.GetComponent<LinenBundleItem>() : null;
            return item && item.BoundSimulation == Simulation && item.State?.Location == LinenLocation.HeldByPlayer &&
                item.State.PlayerId == player.ActorId ? item : null;
        }

        public bool CanDepositLinen(PlayerInteractor player, LaundryHamperInteraction target) =>
            CanHandleLinen(player, target) && HeldLinen(player)?.State.Kind == LinenKind.Dirty;

        public CommandResult DepositLinen(int playerId, LaundryHamperInteraction target)
        {
            if (!PlayerInteractor.TryGetPlayer(playerId, out var player) || !CanDepositLinen(player, target))
                return CommandResult.Fail("Carry dirty linen to the hamper first.");
            var item = HeldLinen(player);
            var result = Simulation.DepositDirtyLinen(playerId, item.itemId);
            if (result.Success)
            {
                player.ReleaseGrab();
                if (target.depositAnchor) item.Body.position = target.depositAnchor.position;
            }
            return GuestCommand(result);
        }

        public bool CanMakeBed(PlayerInteractor player, LinenBedInteraction target)
        {
            if (!target || !CanHandleLinen(player, target) || HeldLinen(player)?.State.Kind != LinenKind.Clean) return false;
            var room = Array.Find(Rooms, item => item.Profile.Id == target.roomId);
            var task = Simulation.Housekeeping.Find(target.roomId);
            return room != null && !room.Occupied && string.IsNullOrEmpty(room.DepartingGuestId) && task != null &&
                (task.Step == RoomPreparationStep.NeedsCleanLinen ||
                 task.Step == RoomPreparationStep.MakingBed && task.WorkingPlayerId == player.ActorId);
        }

        public CommandResult BeginLinenBed(int playerId, LinenBedInteraction target)
        {
            if (!PlayerInteractor.TryGetPlayer(playerId, out var player) || !CanMakeBed(player, target))
                return CommandResult.Fail("Bring clean linen after delivering the dirty bundle.");
            return GuestCommand(Simulation.BeginMakeBed(playerId, target.roomId, HeldLinen(player).itemId));
        }

        public CommandResult AdvanceLinenBed(int playerId, LinenBedInteraction target, float realDeltaTime)
        {
            if (!PlayerInteractor.TryGetPlayer(playerId, out var player) || !player.IsInteracting || !CanMakeBed(player, target))
                return CommandResult.Fail("Keep holding the clean bundle at the bed.");
            var result = Simulation.AdvanceMakeBed(playerId, target.roomId, realDeltaTime);
            if (result.Success && HeldLinen(player) == null) player.ReleaseGrab();
            return GuestCommand(result);
        }
    }
}
