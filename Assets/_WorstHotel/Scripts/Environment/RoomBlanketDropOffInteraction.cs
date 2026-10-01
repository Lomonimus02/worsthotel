using System;
using UnityEngine;

namespace WorstHotel
{
    /// <summary>The actual corridor shelf for a requested blanket; it never grants room entry.</summary>
    public sealed class RoomBlanketDropOffInteraction : HotelInteractable
    {
        public int roomId;
        public Transform deliveryAnchor;
        public string DeliveryPointId => "room/" + roomId + "/blanket-drop";
        public GuestServiceIntent Intent
        {
            get
            {
                var model = GameSession.Instance ? GameSession.Instance.Simulation : null;
                var room = model == null ? null : Array.Find(GameSession.Instance.Rooms, value => value.Profile.Id == roomId);
                return room?.GuestId == null ? null : model.Services?.DropOffIntent(room.GuestId);
            }
        }
        public override bool AllowsHeldItem(PlayerInteractor actor) => true;
        public override bool CanInteract(PlayerInteractor actor)
        {
            var intent = Intent;
            var item = actor && actor.HeldBody ? actor.HeldBody.GetComponent<ServiceSupplyItem>() : null;
            return base.CanInteract(actor) && intent?.Status == ServiceIntentStatus.Active && intent.RoomId == roomId &&
                GameSession.Instance.Simulation.Services.FindCase(intent.CaseId)?.Status == ServiceStatus.InProgress &&
                item && item.State?.Kind == ServiceItemKind.Blanket && item.State.Location == ServiceItemLocation.HeldByPlayer &&
                item.State.PlayerId == actor.ActorId;
        }
        public override string GetPrompt(PlayerInteractor actor)
        {
            var intent = Intent;
            if (intent?.Status == ServiceIntentStatus.AwaitingReceipt)
                return "Room " + roomId + " delivery shelf\n" + (intent.Collecting ? "The guest is coming to collect the blanket" : "Blanket left here · waiting for the guest");
            var model = GameSession.Instance ? GameSession.Instance.Simulation : null;
            if (intent == null && model != null)
                foreach (var guest in model.Guests)
                    if (guest.RoomId == roomId && guest.Agent?.InAssignedRoom == true && guest.Memory.BlanketsDelivered > 0)
                        return "Room " + roomId + " delivery shelf\nBlanket received · thank you";
            if (intent?.Status != ServiceIntentStatus.Active)
                return "Room " + roomId + " delivery shelf\nNo blanket delivery agreed";
            if (GameSession.Instance.Simulation.Services.FindCase(intent.CaseId)?.Status != ServiceStatus.InProgress)
                return "Room " + roomId + " delivery shelf\nReply to the guest before leaving a blanket";
            return CanInteract(actor) ? "Leave requested blanket · room " + roomId + "\nThe guest will receive it when available" :
                "Blanket requested · room " + roomId + "\nBring a blanket from CLEAN BLANKETS";
        }
        public override void Interact(PlayerInteractor actor)
        { if (CanInteract(actor)) GameSession.Instance.DropOffBlanket(actor.ActorId, this); }
    }
}
