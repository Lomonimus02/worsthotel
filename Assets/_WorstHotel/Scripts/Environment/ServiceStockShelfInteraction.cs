using UnityEngine;

namespace WorstHotel
{
    public sealed class ServiceStockShelfInteraction : HotelInteractable
    {
        public ServiceItemKind kind;
        public TextMesh stockLabel;
        public override bool AllowsHeldItem(PlayerInteractor actor) => true;
        public override bool CanInteract(PlayerInteractor actor)
        {
            var item = actor && actor.HeldBody ? actor.HeldBody.GetComponent<ServiceSupplyItem>() : null;
            return base.CanInteract(actor) && item && item.State?.Kind == kind;
        }
        public override string GetPrompt(PlayerInteractor actor) => CanInteract(actor) ? "Return unused " + (kind == ServiceItemKind.Blanket ? "blanket" : "bulb") + " to shelf" :
            (kind == ServiceItemKind.Blanket ? "CLEAN BLANKETS · take a folded blanket" : "REPLACEMENT BULBS · take a boxed bulb");
        public override void Interact(PlayerInteractor actor)
        {
            if (!CanInteract(actor)) return;
            GameSession.Instance.ReturnServiceItem(actor.ActorId, actor.HeldBody.GetComponent<ServiceSupplyItem>());
        }
        void LateUpdate()
        {
            var services = GameSession.Instance ? GameSession.Instance.Simulation?.Services : null;
            if (!stockLabel || services == null) return;
            int count = 0;
            foreach (var item in services.Items) if (item.Kind == kind && item.Location == ServiceItemLocation.OnShelf) count++;
            stockLabel.text = (kind == ServiceItemKind.Blanket ? "CLEAN BLANKETS" : "REPLACEMENT BULBS") + "\n" + count + " AVAILABLE";
        }
    }
}
