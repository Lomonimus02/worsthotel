using System;
using UnityEngine;

namespace WorstHotel
{
    public sealed class LuggageStorageZone : HotelInteractable
    {
        public Transform[] storageAnchors = Array.Empty<Transform>();
        public BoxCollider storageBounds;
        public bool Contains(Vector3 point) => storageBounds && storageBounds.bounds.Contains(point);
        public Transform StorageAnchor(int slot) => storageAnchors.Length == 0 ? transform : storageAnchors[Mathf.Clamp(slot, 0, storageAnchors.Length - 1)];
        public override bool AllowsHeldItem(PlayerInteractor actor) => true;
        public override bool CanInteract(PlayerInteractor actor)
        {
            var item = actor && actor.HeldBody ? actor.HeldBody.GetComponent<ServiceSupplyItem>() : null;
            return base.CanInteract(actor) && item && item.State?.Kind == ServiceItemKind.Luggage && item.State.PlayerId == actor.ActorId &&
                GameSession.Instance.Simulation.Services.CanStoreLuggage(actor.ActorId, item.State.GuestId).Success;
        }
        public override string GetPrompt(PlayerInteractor actor)
        {
            if (!CanInteract(actor)) return "LUGGAGE STORAGE\nCarry agreed luggage or a departed guest's suitcase here";
            var item = actor.HeldBody.GetComponent<ServiceSupplyItem>();
            return GameSession.Instance.Simulation.Services.IsDepartedLuggage(item.ItemId) ?
                "Lower suitcase here, then put it down" : "Lower luggage onto this platform, then put it down";
        }
        public override void Interact(PlayerInteractor actor)
        { if (CanInteract(actor)) GameSession.Instance.StoreLuggage(actor.ActorId, this); }
    }
}
