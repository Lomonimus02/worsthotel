using System;
using System.Collections.Generic;
using System.Linq;

namespace WorstHotel
{
    public enum RoomKeyLocation { OnRack, HeldByPlayer, HeldByGuest, Dropped, Returned, LeftInside }

    public sealed class RoomKeyState
    {
        public int RoomId { get; }
        public RoomKeyLocation Location { get; internal set; } = RoomKeyLocation.OnRack;
        public int? PlayerId { get; internal set; }
        public string GuestId { get; internal set; }
        internal RoomKeyState(int roomId) => RoomId = roomId;
    }

    /// <summary>One authoritative key per room. Physical adapters authenticate reach and carrying before sending intentions.</summary>
    public sealed partial class RoomKeySystem
    {
        public IReadOnlyList<RoomKeyState> Items { get; }
        public event Action<RoomKeyState, string> Changed;
        readonly Dictionary<int, RoomKeyState> keys;

        public RoomKeySystem(IEnumerable<int> roomIds)
        {
            if (roomIds == null) throw new ArgumentNullException(nameof(roomIds));
            var ids = roomIds.OrderBy(id => id).ToArray();
            if (ids.Length == 0 || ids.Any(id => id <= 0) || ids.Distinct().Count() != ids.Length)
                throw new ArgumentException("Keys require unique positive room IDs.");
            var items = new[] { 0 }.Concat(ids).Select(id => new RoomKeyState(id)).ToArray();
            Items = Array.AsReadOnly(items);
            keys = items.ToDictionary(key => key.RoomId);
        }

        public RoomKeyState Find(int roomId) => keys.TryGetValue(roomId, out var key) ? key : null;

        public CommandResult PickUp(int playerId, int roomId)
        {
            if (ReadOnlyMirror) return CommandResult.Fail(HotelSimulation.MirrorMessage);
            if (playerId < 0) return CommandResult.Fail("Unknown player identity.");
            var key = Find(roomId);
            if (key == null) return CommandResult.Fail("This room has no registered key.");
            if (key.Location != RoomKeyLocation.OnRack && key.Location != RoomKeyLocation.Returned && key.Location != RoomKeyLocation.Dropped)
                return CommandResult.Fail("This key is already held by a player or guest.");
            if (Items.Any(item => item.Location == RoomKeyLocation.HeldByPlayer && item.PlayerId == playerId))
                return CommandResult.Fail("Put down your current room key before taking another.");
            key.Location = RoomKeyLocation.HeldByPlayer; key.PlayerId = playerId; key.GuestId = null;
            Changed?.Invoke(key, "picked up by player " + playerId);
            return CommandResult.Ok(roomId == 0 ? "Carrying the STAFF key. Use it at a locked-out guest's door." : "Carrying the key for room " + roomId + ".");
        }

        public CommandResult Drop(int playerId, int roomId)
        {
            if (ReadOnlyMirror) return CommandResult.Fail(HotelSimulation.MirrorMessage);
            if (playerId < 0) return CommandResult.Fail("Unknown player identity.");
            var key = Find(roomId);
            if (key == null || key.Location != RoomKeyLocation.HeldByPlayer || key.PlayerId != playerId)
                return CommandResult.Fail("This player is not holding that key.");
            key.Location = RoomKeyLocation.Dropped; key.PlayerId = null; key.GuestId = null;
            Changed?.Invoke(key, "dropped in the hotel");
            return CommandResult.Ok("Room key dropped.");
        }

        public CommandResult ReturnToRack(int roomId)
        {
            if (ReadOnlyMirror) return CommandResult.Fail(HotelSimulation.MirrorMessage);
            var key = Find(roomId);
            if (key == null || key.Location != RoomKeyLocation.Dropped)
                return CommandResult.Fail("Only a released physical key can be returned to its rack slot.");
            key.Location = RoomKeyLocation.OnRack; key.PlayerId = null; key.GuestId = null;
            Changed?.Invoke(key, "returned to its rack slot");
            return CommandResult.Ok("Key returned to room " + roomId + " rack slot.");
        }

        public CommandResult ReturnGuestKeys(string guestId)
        {
            if (ReadOnlyMirror) return CommandResult.Fail(HotelSimulation.MirrorMessage);
            if (string.IsNullOrWhiteSpace(guestId)) return CommandResult.Fail("A guest identity is required.");
            var owned = Items.Where(key => (key.Location == RoomKeyLocation.HeldByGuest || key.Location == RoomKeyLocation.LeftInside) && key.GuestId == guestId).ToArray();
            foreach (var key in owned) ReturnGuestKeyState(key);
            foreach (var key in owned) Changed?.Invoke(key, "returned at checkout");
            return CommandResult.Ok(owned.Length == 0 ? "This guest has no outstanding room key." : "Guest room key returned to reception.");
        }

        internal void LeaveInside(string guestId, int roomId)
        {
            var key = Find(roomId);
            if (key?.Location != RoomKeyLocation.HeldByGuest || key.GuestId != guestId) return;
            key.Location = RoomKeyLocation.LeftInside; Changed?.Invoke(key, "left inside before an outing");
        }
        internal void RecoverInside(string guestId, int roomId)
        {
            var key = Find(roomId);
            if (key?.Location != RoomKeyLocation.LeftInside || key.GuestId != guestId) return;
            key.Location = RoomKeyLocation.HeldByGuest; Changed?.Invoke(key, "collected inside the room");
        }

        internal CommandResult CanHandToGuest(int playerId, int roomId, string guestId, int? previousRoomId = null)
        {
            if (playerId < 0 || string.IsNullOrWhiteSpace(guestId)) return CommandResult.Fail("Valid player and guest identities are required.");
            var key = Find(roomId);
            if (key == null || key.Location != RoomKeyLocation.HeldByPlayer || key.PlayerId != playerId)
                return CommandResult.Fail("Bring the physical key for room " + roomId + " and give it to this guest.");
            var previous = Items.FirstOrDefault(item => item.Location == RoomKeyLocation.HeldByGuest && item.GuestId == guestId);
            if (previousRoomId.HasValue)
            {
                if (previous == null || previous.RoomId != previousRoomId.Value)
                    return CommandResult.Fail("The guest must still own the key for their current room before exchanging it.");
            }
            else if (previous != null) return CommandResult.Fail("This guest already owns a room key.");
            return CommandResult.Ok();
        }

        // These two methods are used only after all room/key validation passes. Notifications are
        // published after the room exchange so observers never receive a half-completed handoff.
        internal void CommitHandToGuest(int roomId, string guestId, int? previousRoomId = null)
        {
            if (previousRoomId.HasValue) ReturnGuestKeyState(keys[previousRoomId.Value]);
            var key = keys[roomId];
            key.Location = RoomKeyLocation.HeldByGuest; key.PlayerId = null; key.GuestId = guestId;
        }

        internal void NotifyHandToGuest(int roomId, int? previousRoomId = null)
        {
            if (previousRoomId.HasValue) Changed?.Invoke(keys[previousRoomId.Value], "returned during room-key exchange");
            Changed?.Invoke(keys[roomId], "handed to its guest");
        }

        static void ReturnGuestKeyState(RoomKeyState key)
        { key.Location = RoomKeyLocation.Returned; key.PlayerId = null; key.GuestId = null; }
    }
}


