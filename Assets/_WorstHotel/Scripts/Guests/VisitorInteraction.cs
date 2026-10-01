using UnityEngine;

namespace WorstHotel
{
    public sealed class VisitorInteraction : HotelInteractable
    {
        public string VisitorId;
        HotelVisitor Visitor => GameSession.Instance?.Simulation?.Director?.FindVisitor(VisitorId);
        public override bool CanInteract(PlayerInteractor actor) => base.CanInteract(actor) && actor &&
            (!LocalCoopBootstrap.Instance || LocalCoopBootstrap.Instance.HasWorldAuthority) && Visitor != null &&
            Visitor.State != HotelVisitorState.Leaving && Visitor.State != HotelVisitorState.Left;
        public override string GetPrompt(PlayerInteractor actor) => Visitor == null ? "Visitor" :
            "Visitor · Room " + Visitor.RoomId + (Visitor.Allowed ? " · Talk · Q: ask to leave" : " · Allow visit · Q: ask to leave");
        public override void Interact(PlayerInteractor actor) => Decide(actor, true);
        public override void SecondaryInteract(PlayerInteractor actor) => Decide(actor, false);
        void Decide(PlayerInteractor actor, bool allow)
        {
            if (!CanInteract(actor) || actor.Focused != this) return;
            var result = GameSession.Instance.Simulation.Director.DecideVisitor(actor.ActorId, VisitorId, allow);
            if (result.Success) HotelSubtitle.Say(actor.ActorId, "Visitor", result.Message);
            GameSession.Instance.RaiseChanged();
        }
    }
}
