using System;
using UnityEngine;

namespace WorstHotel
{
    public sealed class RoomBlanketDeliveryInteraction : HotelInteractable
    {
        public int roomId;
        public GameObject deliveredBlanket;
        public override bool AllowsHeldItem(PlayerInteractor actor) => true;
        public override bool CanInteract(PlayerInteractor actor)
        {
            var session = GameSession.Instance;
            var item = actor && actor.HeldBody ? actor.HeldBody.GetComponent<ServiceSupplyItem>() : null;
            var room = session ? Array.Find(session.Rooms, candidate => candidate.Profile.Id == roomId) : null;
            GuestStay guest = null;
            if (session?.Simulation != null && room != null)
                foreach (var candidate in session.Simulation.Guests) if (candidate.GuestId == room.GuestId) { guest = candidate; break; }
            return base.CanInteract(actor) && actor && session && room != null && room.Occupied &&
                guest?.Agent?.InAssignedRoom == true && guest.BlanketComfortBonus <= 0 &&
                (!session.Simulation.ContinuousOperations || guest.Agent.ActivityStaged &&
                    guest.Agent.State != GuestAgentState.Sleeping && guest.Agent.Activity != GuestActivity.Shower &&
                    !guest.Agent.IsRelocating && session.Simulation.Services?.DropOffIntent(guest.GuestId)?.Status != ServiceIntentStatus.AwaitingReceipt) &&
                item && item.State?.Kind == ServiceItemKind.Blanket && item.State.PlayerId == actor.ActorId;
        }
        public override string GetPrompt(PlayerInteractor actor)
        {
            var session = GameSession.Instance;
            var room = session ? Array.Find(session.Rooms, candidate => candidate.Profile.Id == roomId) : null;
            if (room == null || !room.Occupied) return "Extra blanket · room " + roomId + "\nNo checked-in guest";
            if (session.Simulation?.Services?.DropOffIntent(room.GuestId)?.Status == ServiceIntentStatus.AwaitingReceipt)
                return "Blanket already left outside room " + roomId + "\nWaiting for the guest to receive it";
            if (session.Simulation != null)
                foreach (var guest in session.Simulation.Guests)
                    if (guest.GuestId == room.GuestId && (guest.BlanketComfortBonus > 0 || guest.Memory.BlanketsDelivered > 0))
                        return "Extra blanket already delivered · room " + roomId + "\nComfort only · no electricity";
            return CanInteract(actor) ? "Give extra blanket · room " + roomId + "\nComfort only · no electricity" :
                "Extra blanket for room " + roomId + "\nBring one from CLEAN BLANKETS in linen storage";
        }
        public override void Interact(PlayerInteractor actor)
        { if (CanInteract(actor)) GameSession.Instance.DeliverBlanket(actor.ActorId, this); }
        void LateUpdate()
        {
            var session = GameSession.Instance;
            var room = session ? Array.Find(session.Rooms, candidate => candidate.Profile.Id == roomId) : null;
            bool visible = false;
            if (room != null && room.Occupied && session.Simulation?.Services != null)
                foreach (var item in session.Simulation.Services.Items)
                    if (item.Kind == ServiceItemKind.Blanket && item.Location == ServiceItemLocation.Delivered && item.GuestId == room.GuestId)
                    { visible = true; break; }
            if (deliveredBlanket && deliveredBlanket.activeSelf != visible) deliveredBlanket.SetActive(visible);
        }
    }
}
