using System.Linq;
using UnityEngine;

namespace WorstHotel
{
    public sealed class LaundryHamperInteraction : HotelInteractable
    {
        public Transform depositAnchor;
        public TextMesh statusLabel;
        public override bool AllowsHeldItem(PlayerInteractor player) => player && player.HeldBody &&
            player.HeldBody.GetComponent<LinenBundleItem>();
        public override bool CanInteract(PlayerInteractor player) => base.CanInteract(player) &&
            GameSession.Instance && GameSession.Instance.CanDepositLinen(player, this);
        public override string GetPrompt(PlayerInteractor player) => CanInteract(player) ? "Place dirty linen in hamper" :
            "Bring the dirty bundle from a vacated room";
        public override void Interact(PlayerInteractor player)
        {
            if (CanInteract(player)) GameSession.Instance.DepositLinen(player.ActorId, this);
        }
        void LateUpdate()
        {
            var housekeeping = GameSession.Instance ? GameSession.Instance.Simulation?.Housekeeping : null;
            if (statusLabel && housekeeping != null)
            {
                var model = GameSession.Instance.Simulation;
                int count = model.ContinuousOperations ? model.DirtyLinenWaiting : housekeeping.Linens.Count(linen => linen.Location == LinenLocation.InHamper);
                statusLabel.text = "DIRTY LINEN\n" + count + " waiting for laundry";
            }
        }
    }
}
