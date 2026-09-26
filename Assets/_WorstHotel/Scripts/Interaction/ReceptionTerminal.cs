using UnityEngine;

namespace WorstHotel
{
    public sealed class ReceptionTerminal : HotelInteractable
    {
        public override string GetPrompt(PlayerInteractor actor) => "Open the reception ledger";
        public override void Interact(PlayerInteractor actor)
        {
            var coop = LocalCoopBootstrap.Instance;
            if (coop && coop.RequestRemoteLedger(actor.ActorId)) return;
            if (ManagementUI.Instance != null) ManagementUI.Instance.Open(actor.ActorId);
        }
    }
}
