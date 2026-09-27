using System;
using UnityEngine;

namespace WorstHotel
{
    /// <summary>The complaint's actual lamp. Circuit loss and a burnt bulb both turn this same luminaire off.</summary>
    public sealed class RoomLampInteraction : HotelInteractable
    {
        public int roomId;
        public Light bulbLight;
        public Renderer bulbSurface;
        public TextMesh statusLabel;
        public bool IsLit { get; private set; }
        public RoomState State => GameSession.Instance ? Array.Find(GameSession.Instance.Rooms, room => room.Profile.Id == roomId) : null;
        MaterialPropertyBlock properties;
        int previousKey = -1;
        void Awake() => properties = new MaterialPropertyBlock();
        public override bool AllowsHeldItem(PlayerInteractor actor) => true;
        public override bool CanInteract(PlayerInteractor actor)
        {
            var item = actor && actor.HeldBody ? actor.HeldBody.GetComponent<ServiceSupplyItem>() : null;
            return base.CanInteract(actor) && State != null && State.LampBroken && item &&
                item.State?.Kind == ServiceItemKind.ReplacementBulb && item.State.PlayerId == actor.ActorId;
        }
        public override string GetPrompt(PlayerInteractor actor)
        {
            var room = State;
            if (room == null) return "Bedside lamp unavailable";
            if (room.LampBroken) return CanInteract(actor) ? "Replace burnt bulb · room " + roomId :
                "BURNT BULB · room " + roomId + "\nBring a replacement from maintenance storage";
            return !room.HasPower ? "Lamp intact · CIRCUIT " + room.CircuitId + " HAS NO POWER" :
                "Bedside lamp works · condition " + Mathf.CeilToInt(room.LampCondition) + "%";
        }
        public override void Interact(PlayerInteractor actor)
        { if (CanInteract(actor)) GameSession.Instance.ReplaceRoomBulb(actor.ActorId, this); }
        void LateUpdate()
        {
            var room = State;
            if (room == null) return;
            IsLit = room.Operational && !room.LampBroken && room.HasPower;
            int key = (IsLit ? 1000 : 0) + (room.LampBroken ? 200 : 0) + Mathf.CeilToInt(room.LampCondition);
            if (key == previousKey) return;
            previousKey = key;
            if (bulbLight) bulbLight.enabled = IsLit;
            if (bulbSurface && bulbSurface.sharedMaterial)
            {
                bulbSurface.GetPropertyBlock(properties);
                properties.SetColor("_BaseColor", IsLit ? new Color(1, .80f, .46f) : room.LampBroken ? new Color(.20f, .17f, .13f) : new Color(.55f, .48f, .36f));
                properties.SetColor("_EmissionColor", IsLit ? new Color(1, .88f, .70f) * .35f : Color.black);
                bulbSurface.SetPropertyBlock(properties);
            }
            if (statusLabel) statusLabel.text = "ROOM " + roomId + " LAMP\n" + (room.LampBroken ? "BURNT BULB" : !room.HasPower ? "NO POWER" :
                "WORKING " + Mathf.CeilToInt(room.LampCondition) + "%");
        }
    }
}
