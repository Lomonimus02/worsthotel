using UnityEngine;

namespace WorstHotel
{
    public sealed class ReceptionPhoneInteraction : HotelInteractable
    {
        public TextMesh phoneLabel;
        public override bool CanInteract(PlayerInteractor actor) => base.CanInteract(actor) && actor && GameSession.Instance &&
            GameSession.Instance.Phase == DayPhase.Service;
        public override string GetPrompt(PlayerInteractor actor) => GameSession.Instance?.Simulation?.Services?.IncomingCall != null ?
            "Reception telephone ringing · pick up" : "Reception telephone · incoming / promised calls";
        public override void Interact(PlayerInteractor actor)
        { if (CanInteract(actor)) GameSession.Instance.OpenWakePhone(actor.ActorId, this); }
        void LateUpdate()
        {
            var simulation = GameSession.Instance ? GameSession.Instance.Simulation : null;
            if (!phoneLabel || simulation?.Services == null) return;
            if (simulation.Services.IncomingCall != null)
            { phoneLabel.text = "RECEPTION PHONE\nINCOMING CALL"; return; }
            PromiseWakeUp next = null;
            foreach (var promise in simulation.Services.Promises)
                if (promise.Status == PromiseStatus.Accepted && (next == null || promise.DueTime < next.DueTime)) next = promise;
            phoneLabel.text = next == null ? "RECEPTION PHONE\nNO CALLS PROMISED" : "WAKE ROOM " + next.RoomId + "\n" +
                (next.DueTime <= simulation.Elapsed ? "DUE NOW" : "DUE IN " + GuestLabels.HotelDuration(simulation, next.DueTime - simulation.Elapsed));
        }
    }
}
