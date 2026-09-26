using System;
using System.Collections.Generic;
using System.Linq;

namespace WorstHotel
{
    public enum RoomNoiseLinkKind { SharedWall, Corridor }

    public sealed class RoomNoiseLink
    {
        public int RoomA { get; }
        public int RoomB { get; }
        public RoomNoiseLinkKind Kind { get; }
        public RoomNoiseLink(int roomA, int roomB, RoomNoiseLinkKind kind)
        {
            if (roomA <= 0 || roomB <= 0 || roomA == roomB || !Enum.IsDefined(typeof(RoomNoiseLinkKind), kind))
                throw new ArgumentException("A noise link requires two different positive room IDs and a known connection kind.");
            RoomA = Math.Min(roomA, roomB); RoomB = Math.Max(roomA, roomB); Kind = kind;
        }
    }

    /// <summary>Authored direct acoustic neighbors. A corridor link never claims that rooms share a wall.</summary>
    public sealed class RoomAdjacencyGraph
    {
        public IReadOnlyList<int> RoomIds { get; }
        public IReadOnlyList<RoomNoiseLink> Links { get; }
        private readonly HashSet<int> knownRooms;

        public RoomAdjacencyGraph(IEnumerable<int> roomIds, IEnumerable<RoomNoiseLink> links)
        {
            if (roomIds == null || links == null) throw new ArgumentNullException("Room IDs and noise links are required.");
            int[] ids = roomIds.OrderBy(id => id).ToArray();
            if (ids.Length == 0 || ids.Any(id => id <= 0) || ids.Distinct().Count() != ids.Length)
                throw new ArgumentException("An adjacency graph needs unique positive room IDs.");
            knownRooms = new HashSet<int>(ids);
            var copy = links.ToArray();
            if (copy.Any(link => link == null || !knownRooms.Contains(link.RoomA) || !knownRooms.Contains(link.RoomB)))
                throw new ArgumentException("Noise links must reference rooms in this graph.");
            if (copy.GroupBy(link => (link.RoomA, link.RoomB)).Any(group => group.Count() > 1))
                throw new ArgumentException("A room pair may have only one authored acoustic connection.");
            RoomIds = Array.AsReadOnly(ids);
            Links = Array.AsReadOnly(copy.OrderBy(link => link.RoomA).ThenBy(link => link.RoomB).ToArray());
        }

        public bool ContainsRoom(int roomId) => knownRooms.Contains(roomId);

        public static RoomAdjacencyGraph Prototype(IEnumerable<int> roomIds = null)
        {
            var ids = (roomIds ?? new[] { 101, 102, 103, 104, 105, 106 }).ToArray();
            var selected = new HashSet<int>(ids);
            var walls = RoomNoiseLinkKind.SharedWall;
            var corridor = RoomNoiseLinkKind.Corridor;
            var links = new[]
            {
                new RoomNoiseLink(101, 103, walls), new RoomNoiseLink(103, 105, walls),
                new RoomNoiseLink(102, 104, walls), new RoomNoiseLink(104, 106, walls),
                new RoomNoiseLink(101, 102, corridor), new RoomNoiseLink(103, 104, corridor), new RoomNoiseLink(105, 106, corridor),
                new RoomNoiseLink(101, 104, corridor), new RoomNoiseLink(102, 103, corridor),
                new RoomNoiseLink(103, 106, corridor), new RoomNoiseLink(104, 105, corridor)
            };
            return new RoomAdjacencyGraph(ids, links.Where(link => selected.Contains(link.RoomA) && selected.Contains(link.RoomB)));
        }
    }
}
