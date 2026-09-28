namespace WorstHotel
{
    public sealed class ServiceBellInteraction : HotelInteractable
    {
        public override string GetPrompt(PlayerInteractor actor) => "Ring bell";
        public override void Interact(PlayerInteractor actor) => HotelFeedback.PlayReceptionArrival();
    }
}
