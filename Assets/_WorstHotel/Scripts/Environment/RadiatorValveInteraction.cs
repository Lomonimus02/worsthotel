using System;
using UnityEngine;

namespace WorstHotel
{
    public sealed class RadiatorValveInteraction : HotelInteractable
    {
        public int roomId;
        public Transform knob;
        public TextMesh settingLabel;
        public RoomState State => GameSession.Instance ? Array.Find(GameSession.Instance.Rooms, room => room.Profile.Id == roomId) : null;
        public override bool CanInteract(PlayerInteractor actor) => base.CanInteract(actor) && actor && State != null && GameSession.Instance &&
            (GameSession.Instance.Phase == DayPhase.Planning || GameSession.Instance.Phase == DayPhase.Service);
        public override string GetPrompt(PlayerInteractor actor) => State == null ? "Radiator unavailable" :
            "Radiator " + State.RadiatorSetting + "/3 · " + State.Temperature.ToString("F1") + "°C\nTurn up · Q / X: turn down\nHigher heat increases boiler demand";
        public override void Interact(PlayerInteractor actor)
        { if (CanInteract(actor)) GameSession.Instance.SetRadiatorSetting(actor.ActorId, this, Math.Min(3, State.RadiatorSetting + 1)); }
        public override void SecondaryInteract(PlayerInteractor actor)
        { if (CanInteract(actor)) GameSession.Instance.SetRadiatorSetting(actor.ActorId, this, Math.Max(0, State.RadiatorSetting - 1)); }
        void LateUpdate()
        {
            var room = State;
            if (room == null) return;
            if (knob) knob.localRotation = Quaternion.Euler(0, 0, -65 + room.RadiatorSetting * 43.3f);
            if (settingLabel) settingLabel.text = "RADIATOR " + room.RadiatorSetting + " / 3\n" +
                (room.RadiatorSetting == 0 ? "OFF" : room.RadiatorSetting == 1 ? "LOW" : room.RadiatorSetting == 2 ? "MEDIUM" : "HIGH");
        }
    }
}
