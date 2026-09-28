using System;
using System.Collections.Generic;

namespace WorstHotel
{
    /// <summary>Recomputes direct external sound from actual guest activities; received sound never becomes another source.</summary>
    public sealed partial class NoiseSystem
    {
        public NoiseSettings Settings { get; }
        public RoomAdjacencyGraph Graph { get; }
        private readonly Dictionary<int, RoomState> rooms = new Dictionary<int, RoomState>();
        private readonly Dictionary<int, double> sources = new Dictionary<int, double>();
        private readonly Dictionary<int, double> received = new Dictionary<int, double>();
        private readonly Dictionary<int, float> overrides = new Dictionary<int, float>();
        private readonly List<GuestStay> orderedGuests = new List<GuestStay>();
        private readonly HashSet<string> guestIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<RoomNoiseSource> actualSources = new List<RoomNoiseSource>();
        private readonly Dictionary<int, List<RoomNoiseSource>> contributions = new Dictionary<int, List<RoomNoiseSource>>();
        public IReadOnlyList<RoomNoiseSource> Sources => actualSources.AsReadOnly();
        public IReadOnlyList<RoomNoiseSource> GetContributions(int roomId) => contributions.TryGetValue(roomId, out var items) ?
            items.AsReadOnly() : (IReadOnlyList<RoomNoiseSource>)Array.Empty<RoomNoiseSource>();

        public NoiseSystem(NoiseSettings settings, RoomAdjacencyGraph graph)
        { Settings = settings ?? throw new ArgumentNullException(nameof(settings)); Graph = graph ?? throw new ArgumentNullException(nameof(graph)); }

        public CommandResult SetNoiseOverride(int roomId, float? totalNoise)
        {
            if (ReadOnlyMirror) return CommandResult.Fail(HotelSimulation.MirrorMessage);
            if (!Graph.ContainsRoom(roomId)) return CommandResult.Fail("Noise override requires a real room.");
            if (totalNoise.HasValue && (!Number.IsFinite(totalNoise.Value) || totalNoise.Value < 0 || totalNoise.Value > 1))
                return CommandResult.Fail("Noise override must be finite and between 0 and 1.");
            if (totalNoise.HasValue) overrides[roomId] = totalNoise.Value;
            else overrides.Remove(roomId);
            return CommandResult.Ok(totalNoise.HasValue ? "Explicit total received-noise override set." : "Room noise returned to its measured sources.");
        }

        public float? GetNoiseOverride(int roomId) => overrides.TryGetValue(roomId, out var value) ? value : (float?)null;
        public void ClearOverrides() { if (!ReadOnlyMirror) overrides.Clear(); }

        public void Tick(IEnumerable<GuestStay> guests, IEnumerable<RoomState> roomStates, float simulationTime, GuestServiceSystem services = null)
        {
            if (ReadOnlyMirror) return;
            if (guests == null || roomStates == null) throw new ArgumentNullException("Guests and room states are required.");
            if (!Number.IsFinite(simulationTime) || simulationTime < 0) throw new ArgumentOutOfRangeException(nameof(simulationTime));
            rooms.Clear(); sources.Clear(); received.Clear(); orderedGuests.Clear(); guestIds.Clear(); actualSources.Clear(); contributions.Clear();
            foreach (var room in roomStates)
            {
                if (room == null || !Graph.ContainsRoom(room.Profile.Id) || rooms.ContainsKey(room.Profile.Id))
                    throw new ArgumentException("Noise rooms must uniquely match the authored adjacency graph.");
                rooms.Add(room.Profile.Id, room); sources.Add(room.Profile.Id, 0); received.Add(room.Profile.Id, 0);
                contributions.Add(room.Profile.Id, new List<RoomNoiseSource>());
            }
            if (rooms.Count != Graph.RoomIds.Count) throw new ArgumentException("Every authored graph room must have a state.");
            foreach (var guest in guests)
            {
                if (guest == null) throw new ArgumentException("Guest collection contains a null stay.");
                if (!guestIds.Add(guest.GuestId)) throw new ArgumentException("A guest may contribute noise only once per tick.");
                if (!rooms.ContainsKey(guest.RoomId)) throw new ArgumentException("Guest references a room outside the noise graph.");
                if (guest.Agent != null && (!Number.IsFinite(guest.Agent.NoiseOutput) || guest.Agent.NoiseOutput < 0 || guest.Agent.NoiseOutput > 1 ||
                    !Number.IsFinite(guest.Agent.QuietUntil) || guest.Agent.QuietUntil < 0))
                    throw new ArgumentException("Guest source output and quiet deadline must be valid.");
                orderedGuests.Add(guest);
            }
            orderedGuests.Sort((a, b) => StringComparer.Ordinal.Compare(a.GuestId, b.GuestId));
            foreach (var guest in orderedGuests)
            {
                var agent = guest.Agent;
                if (agent == null || !agent.InAssignedRoom || !agent.ActivityStaged || rooms[guest.RoomId].GuestId != guest.GuestId) continue;
                // Television/music needs delivered electricity. The requested activity and its electrical demand remain intact.
                var amplifier = services?.ActiveAmplifier(guest);
                if (amplifier != null && rooms[guest.RoomId].HasPower)
                {
                    float volume = guest.Application.Special.AmplifierNoise * (agent.QuietUntil > simulationTime ? Settings.QuietSourceMultiplier : 1);
                    actualSources.Add(new RoomNoiseSource(amplifier.Id, guest.GuestId, guest.RoomId, NoiseCategory.Amplifier, volume));
                    sources[guest.RoomId] += volume;
                }
                bool television = agent.Activity == GuestActivity.LoudRoom || agent.Activity == GuestActivity.WatchTV;
                bool phone = agent.Activity == GuestActivity.PhoneCall;
                bool shower = agent.Activity == GuestActivity.Shower;
                if (!television && !phone && !shower) continue;
                if (television && !rooms[guest.RoomId].HasPower) continue;
                bool quietLoudActivity = (television || phone) && agent.QuietUntil > simulationTime;
                float output = agent.NoiseOutput * (quietLoudActivity ? Settings.QuietSourceMultiplier : 1);
                var category = television ? NoiseCategory.Television : phone ? NoiseCategory.PhoneCall : NoiseCategory.Plumbing;
                var source = new RoomNoiseSource(guest.GuestId + "/" + category, guest.GuestId, guest.RoomId, category, output);
                if (source.Active) actualSources.Add(source);
                sources[guest.RoomId] += output;
            }
            foreach (var source in actualSources)
                foreach (var link in Graph.Links)
                {
                    int target = link.RoomA == source.SourceRoomId ? link.RoomB : link.RoomB == source.SourceRoomId ? link.RoomA : 0;
                    if (target == 0) continue;
                    float transmission = link.Kind == RoomNoiseLinkKind.SharedWall ? Settings.SharedWallTransmission : Settings.CorridorTransmission;
                    contributions[target].Add(new RoomNoiseSource(source.SourceEntityId, source.SourceGuestId, source.SourceRoomId,
                        source.Category, source.NoiseOutput, source.NoiseOutput * transmission));
                }
            foreach (var link in Graph.Links)
            {
                float transmission = link.Kind == RoomNoiseLinkKind.SharedWall ? Settings.SharedWallTransmission : Settings.CorridorTransmission;
                received[link.RoomA] += sources[link.RoomB] * transmission;
                received[link.RoomB] += sources[link.RoomA] * transmission;
            }
            // Apply only after all inputs validate and the complete source/receiver pass finishes.
            foreach (int id in Graph.RoomIds)
            {
                var room = rooms[id];
                room.SourceNoise = (float)Math.Min(float.MaxValue, sources[id]);
                room.ReceivedNoise = (float)Math.Min(float.MaxValue, received[id]);
                room.Noise = overrides.TryGetValue(id, out float overridden) ? overridden :
                    Number.Clamp(room.Profile.Noise + room.ReceivedNoise, 0, 1);
            }
        }
    }
}



