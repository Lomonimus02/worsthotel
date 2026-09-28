using UnityEngine;

namespace WorstHotel
{
    public sealed class ReceptionPhoneInteraction : HotelInteractable
    {
        public TextMesh phoneLabel;
        public override bool CanInteract(PlayerInteractor actor) => base.CanInteract(actor) && actor && GameSession.Instance &&
            GameSession.Instance.Phase == DayPhase.Service && (GameSession.Instance.PhoneHolder < 0 || GameSession.Instance.PhoneHolder == actor.ActorId);
        public override string GetPrompt(PlayerInteractor actor) => GameSession.Instance?.Simulation?.Services?.IncomingCall != null ?
            "Reception telephone ringing · pick up" : "Reception telephone · incoming / promised calls";
        public override void Interact(PlayerInteractor actor)
        { if (CanInteract(actor)) GameSession.Instance.OpenWakePhone(actor.ActorId, this); }
        void LateUpdate() { if (phoneLabel) phoneLabel.text = "RECEPTION"; }
    }
}
