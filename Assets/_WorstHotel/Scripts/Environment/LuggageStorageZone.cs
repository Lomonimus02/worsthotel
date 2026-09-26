using System;
using UnityEngine;

namespace WorstHotel
{
    public sealed class LuggageStorageZone : HotelInteractable
    {
        public Transform[] storageAnchors = Array.Empty<Transform>();
        public BoxCollider storageBounds;
        public Transform StorageAnchor(int slot) => storageAnchors.Length == 0 ? transform : storageAnchors[Mathf.Clamp(slot, 0, storageAnchors.Length - 1)];
        public override bool AllowsHeldItem(PlayerInteractor actor) => true;
        public override bool CanInteract(PlayerInteractor actor)
        {
            var item = actor && actor.HeldBody ? actor.HeldBody.GetComponent<ServiceSupplyItem>() : null;
            return base.CanInteract(actor) && item && item.State?.Kind == ServiceItemKind.Luggage && item.State.PlayerId == actor.ActorId;
        }
        public override string GetPrompt(PlayerInteractor actor) => CanInteract(actor) ? "Place guest luggage in storage" : "LUGGAGE STORAGE\nAccept a guest's storage request, then carry their suitcase here";
        public override void Interact(PlayerInteractor actor)
        { if (CanInteract(actor)) GameSession.Instance.StoreLuggage(actor.ActorId, this); }
    }
}
