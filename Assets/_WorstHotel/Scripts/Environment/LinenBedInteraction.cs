using System;
using UnityEngine;

namespace WorstHotel
{
    /// <summary>A short exclusive bed action, advanced by the present player's real interaction time.</summary>
    public sealed class LinenBedInteraction : HotelInteractable
    {
        public int roomId;
        public LinenBundleItem dirtyBundle;
        public GameObject[] madeBedPieces;
        public TextMesh statusLabel;
        public float HoldProgress => capturedTask != null ? capturedTask.Progress01 : 0;
        public int OwnerPlayerId { get; private set; } = -1;
        HotelSimulation capturedSimulation;
        HousekeepingTask capturedTask;
        public override bool AllowsHeldItem(PlayerInteractor player) => player && player.HeldBody &&
            player.HeldBody.GetComponent<LinenBundleItem>();
        public override bool CanInteract(PlayerInteractor player) => base.CanInteract(player) &&
            GameSession.Instance && GameSession.Instance.CanMakeBed(player, this) &&
            (OwnerPlayerId < 0 || OwnerPlayerId == player.ActorId);

        public override string GetPrompt(PlayerInteractor player)
        {
            var session = GameSession.Instance;
            var room = session ? Array.Find(session.Rooms, item => item.Profile.Id == roomId) : null;
            if (room == null) return "Hotel is not ready";
            if (room.Occupied) return "Guest is staying here";
            if (!string.IsNullOrEmpty(room.DepartingGuestId)) return "Wait until the guest has left the room";
            if (room.Cleanliness == Cleanliness.Clean) return "Bed ready for the next guest";
            var task = session.Simulation.Housekeeping.Find(roomId);
            if (task == null) return "Room needs preparation";
            switch (task.Step)
            {
                case RoomPreparationStep.DirtyLinenOnBed: return "Carry the dirty linen bundle to the utility hamper";
                case RoomPreparationStep.DeliverDirtyLinen: return "Dirty linen must reach the utility hamper";
                case RoomPreparationStep.MakingBed:
                    return task.WorkingPlayerId == player?.ActorId ? "Making bed · " + Mathf.RoundToInt(task.Progress01 * 100) + "%" : "Another owner is making this bed";
                default: if (room.Disorder != RoomDisorder.None) return "Reset the room: " + room.Disorder + " before making the bed";
                    return CanInteract(player) ? "Hold briefly to make bed · " +
                    session.Simulation.Housekeeping.Settings.MakeBedSeconds.ToString("0.#") + " seconds" :
                    "Bring a clean bundle from the utility linen shelf";
            }
        }

        public override void Interact(PlayerInteractor player)
        {
            if (!CanInteract(player)) return;
            var session = GameSession.Instance;
            var result = session.BeginLinenBed(player.ActorId, this);
            if (!result.Success) return;
            capturedSimulation = session.Simulation;
            capturedTask = capturedSimulation.Housekeeping.Find(roomId);
            OwnerPlayerId = player.ActorId;
        }

        public override void HoldInteract(PlayerInteractor player, float deltaTime)
        {
            if (!player || OwnerPlayerId != player.ActorId || !player.IsInteracting || !CanInteract(player) ||
                !Number.IsFinite(deltaTime) || deltaTime <= 0) return;
            var session = GameSession.Instance;
            if (session.Simulation != capturedSimulation || capturedSimulation.Housekeeping.Find(roomId) != capturedTask)
            { ClearClaim(); return; }
            session.AdvanceLinenBed(player.ActorId, this, deltaTime);
            if (capturedSimulation.Housekeeping.Find(roomId) != capturedTask) ClearClaim();
        }

        public override void EndInteract(PlayerInteractor player)
        {
            if (player && OwnerPlayerId == player.ActorId) ClearClaim();
        }

        void ClearClaim()
        {
            if (OwnerPlayerId >= 0 && GameSession.Instance && GameSession.Instance.Simulation == capturedSimulation &&
                capturedSimulation.Housekeeping.Find(roomId) == capturedTask)
                capturedSimulation.CancelMakeBed(OwnerPlayerId, roomId);
            OwnerPlayerId = -1; capturedSimulation = null; capturedTask = null;
        }

        void LateUpdate()
        {
            var session = GameSession.Instance;
            if (!session || session.Simulation == null) return;
            if (capturedSimulation != null && session.Simulation != capturedSimulation) ClearClaim();
            var room = Array.Find(session.Rooms, item => item.Profile.Id == roomId);
            bool ready = room != null && room.Cleanliness == Cleanliness.Clean;
            if (madeBedPieces != null)
                foreach (var piece in madeBedPieces) if (piece && piece.activeSelf != ready) piece.SetActive(ready);
            if (statusLabel) statusLabel.text = "ROOM " + roomId + "\n" + (ready ? "READY" :
                room != null && !string.IsNullOrEmpty(room.DepartingGuestId) ? "GUEST LEAVING" : "LINEN CHANGE");
        }
        void OnDisable() => ClearClaim();
    }
}
