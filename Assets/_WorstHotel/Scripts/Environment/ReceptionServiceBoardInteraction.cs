using UnityEngine;

namespace WorstHotel
{
    public sealed class ReceptionServiceBoardInteraction : HotelInteractable
    {
        public TextMesh summary;
        public override bool CanInteract(PlayerInteractor actor) => base.CanInteract(actor) && actor && GameSession.Instance;
        public override string GetPrompt(PlayerInteractor actor) => "Open service board · requests, promises, arrivals and checkouts";
        public override void Interact(PlayerInteractor actor)
        { if (CanInteract(actor)) GameSession.Instance.OpenReceptionServiceBoard(actor.ActorId, this); }
        void LateUpdate()
        {
            var simulation = GameSession.Instance ? GameSession.Instance.Simulation : null;
            if (!summary || simulation?.Services == null) return;
            int active = 0, promises = 0;
            foreach (var item in simulation.Services.Cases) if (GuestLabels.IsKnownOpenService(item)) active++;
            foreach (var item in simulation.Services.Promises) if (item.Status == PromiseStatus.Accepted) promises++;
            summary.text = "GUEST SERVICES\n" + active + " REQUESTS  /  " + promises + " CALLS\nOPEN FOR NOW + UPCOMING";
        }
    }
}
