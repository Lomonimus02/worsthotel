using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WorstHotel
{
    /// <summary>A nearby guest answers a knock, then accepts a brief request about their actual loud activity.</summary>
    public sealed class RoomNoiseInteraction : HotelInteractable
    {
        public int roomId;
        public Transform knocker;
        public const float AnswerWindowSeconds = 6;
        public const float EmergencyHoldSeconds = 3;
        public string GuestId { get { BindRoom(); return guest?.GuestId; } }
        readonly Dictionary<int, float> answers = new Dictionary<int, float>();
        readonly Dictionary<int, float> emergencyArmedUntil = new Dictionary<int, float>();
        readonly Dictionary<int, float> emergencyProgress = new Dictionary<int, float>();
        readonly Dictionary<int, string> responses = new Dictionary<int, string>();
        DoorInteractable door;
        HotelSimulation simulation;
        GuestStay guest;
        RoomState room;
        Quaternion knockerRest;
        float knockAnimation;

        void Awake() { if (knocker) knockerRest = knocker.localRotation; door = GetComponentInParent<DoorInteractable>(); }

        void BindRoom()
        {
            var session = GameSession.Instance;
            var current = session ? session.Simulation : null;
            var nextRoom = session ? System.Array.Find(session.Rooms, item => item.Profile.Id == roomId) : null;
            var nextGuest = current?.Guests.FirstOrDefault(stay => nextRoom != null && stay.GuestId == nextRoom.GuestId);
            if (simulation != current || guest != nextGuest)
            {
                answers.Clear(); emergencyArmedUntil.Clear(); emergencyProgress.Clear(); responses.Clear(); knockAnimation = 0;
                simulation = current; guest = nextGuest;
            }
            room = nextRoom;
        }

        bool CanHearLoudActivity => ManagementUI.CanAskForQuiet(simulation, guest);

        public bool HasAnswered(int playerId)
        {
            BindRoom();
            return playerId >= 0 && answers.TryGetValue(playerId, out float until) && until >= Time.time;
        }

        public bool CanRequestQuietFor(int playerId, string guestId) => HasAnswered(playerId) &&
            CanHearLoudActivity && guest.GuestId == guestId;

        public override bool AllowsHeldItem(PlayerInteractor player) => true;
        public override bool CanInteract(PlayerInteractor player)
        {
            BindRoom();
            return base.CanInteract(player) && player && player.CanAct && GameSession.Instance &&
                GameSession.Instance.Phase == DayPhase.Service && room != null && room.Occupied;
        }

        public override string GetPrompt(PlayerInteractor player)
        {
            BindRoom();
            if (room == null || !room.Occupied) return "Vacant room · Use the door handle";
            if (player && IsEmergencyArmed(player.ActorId))
                return "EMERGENCY ACCESS · Hold E / A " + (EmergencyHoldSeconds - Progress(player.ActorId)).ToString("0.0") + "s";
            if (player && HasAnswered(player.ActorId))
            {
                if (responses.TryGetValue(player.ActorId, out string response)) return response + " · Q / X: emergency access";
                if (room.PrivacyState == RoomPrivacyState.Private) return "Guest needs privacy · Knock again / Q / X: emergency access";
                return "Guest answers · Talk / choose a response · Q / X: emergency access";
            }
            return (room.PrivacyState == RoomPrivacyState.Private ? "PRIVATE · Locked · " : CanHearLoudActivity ? "Loud activity inside · " : "Occupied room · ") +
                "Knock · Q / X: emergency access";
        }

        public override void Interact(PlayerInteractor player)
        {
            if (!CanInteract(player) || !IsFocused(player) || IsEmergencyArmed(player.ActorId)) return;
            if (!HasAnswered(player.ActorId))
            {
                answers[player.ActorId] = Time.time + AnswerWindowSeconds;
                knockAnimation = .35f;
                HotelFeedback.PlayRoomKnock(transform.position);
                if (guest?.Agent == null || !guest.Agent.InAssignedRoom) responses[player.ActorId] = "No answer; the guest is away";
                else if (room.PrivacyState == RoomPrivacyState.Private) responses[player.ActorId] = guest.Agent.State == GuestAgentState.Sleeping ?
                    "I'm sleeping. Please come back later" : "I'm in the shower. Please come back later";
                else responses.Remove(player.ActorId);
                if (responses.TryGetValue(player.ActorId, out var reply)) HotelSubtitle.Say(player.ActorId, "Room " + roomId, reply);
                else if (guest != null) GameSession.Instance.OpenGuestConversation(player.ActorId, guest.GuestId, true);
                return;
            }
            if (guest?.Agent == null || !guest.Agent.InAssignedRoom || room.PrivacyState == RoomPrivacyState.Private) return;
            var conversation = GameSession.Instance.OpenGuestConversation(player.ActorId, guest.GuestId, true);
            if (conversation.Success) { answers.Remove(player.ActorId); responses.Remove(player.ActorId); }
        }

        bool IsFocused(PlayerInteractor player) => player && (player.Focused == this || player.Focused == door);
        bool IsEmergencyArmed(int actorId) => emergencyArmedUntil.TryGetValue(actorId, out float until) && until >= Time.time;
        float Progress(int actorId) => emergencyProgress.TryGetValue(actorId, out float progress) ? progress : 0;
        public override void SecondaryInteract(PlayerInteractor player)
        {
            if (!CanInteract(player) || !IsFocused(player)) return;
            emergencyArmedUntil[player.ActorId] = Time.time + 8;
            emergencyProgress[player.ActorId] = 0;
        }
        public override void HoldInteract(PlayerInteractor player, float deltaTime)
        {
            if (!CanInteract(player) || !IsFocused(player) || !player.IsInteracting || !IsEmergencyArmed(player.ActorId) || deltaTime <= 0) return;
            emergencyProgress[player.ActorId] = Progress(player.ActorId) + Mathf.Min(deltaTime, .1f);
            if (Progress(player.ActorId) < EmergencyHoldSeconds) return;
            emergencyArmedUntil.Remove(player.ActorId); emergencyProgress.Remove(player.ActorId);
            answers.Clear(); responses.Clear();
            if (door) door.OpenForStaff(player);
            simulation.SignalEvent("Staff used deliberate emergency access to room " + roomId);
            GameSession.Instance.RaiseChanged();
        }
        public override void EndInteract(PlayerInteractor player)
        {
            if (player) emergencyProgress.Remove(player.ActorId);
        }

        void LateUpdate()
        {
            BindRoom();
            if (!knocker) return;
            knockAnimation = Mathf.Max(0, knockAnimation - Time.deltaTime);
            knocker.localRotation = knockerRest * Quaternion.Euler(
                Mathf.Sin(knockAnimation / .35f * Mathf.PI * 4) * 18 * (knockAnimation / .35f), 0, 0);
        }
        void OnDisable() { answers.Clear(); emergencyArmedUntil.Clear(); emergencyProgress.Clear(); responses.Clear(); knockAnimation = 0; if (knocker) knocker.localRotation = knockerRest; }
    }
}
