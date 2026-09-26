using System;
using System.Collections.Generic;
using System.Linq;

namespace WorstHotel
{
    [Serializable] public sealed class ServiceLayerSnapshot
    {
        public int Day, LastRefillDay, StaffCount;
        public float ServiceEnd;
        public bool NaturalCommunicationEnabled;
        public GuestResponseSnapshot[] Responses;
        public ServiceCaseSnapshot[] Cases;
        public WakePromiseSnapshot[] Promises;
        public ServiceItemSnapshot[] Items;
    }
    [Serializable] public sealed class ServiceCaseSnapshot
    {
        public string Id, GuestId, SourceEntityId, Description, ResponseId, ResolutionReason;
        public int RoomId, SourceRoomId;
        public ServiceKind Kind;
        public ServiceStatus Status;
        public float CreatedAt, DueTime, RecoverySeconds;
        public bool BudgetCharged;
        public float ResolutionAt;
    }
    [Serializable] public sealed class WakePromiseSnapshot
    {
        public string Id, GuestId;
        public int RoomId;
        public float DueTime, CompletedAt;
        public PromiseStatus Status;
        public bool DueNotified;
    }
    [Serializable] public sealed class ServiceItemSnapshot
    {
        public string Id, GuestId;
        public int PlayerId, LastPlayerId, RoomId, Generation;
        public ServiceItemKind Kind;
        public ServiceItemLocation Location;
    }

    public sealed partial class GuestServiceSystem
    {
        internal ServiceLayerSnapshot CaptureSnapshot() => new ServiceLayerSnapshot
        {
            Day = day, ServiceEnd = serviceEnd, LastRefillDay = LastRefillDay, StaffCount = StaffCount,
            NaturalCommunicationEnabled = NaturalCommunicationEnabled, Responses = CaptureResponses(),
            Cases = cases.Select(c => new ServiceCaseSnapshot { Id = c.Id, GuestId = c.GuestId,
                RoomId = c.RoomId, SourceRoomId = c.SourceRoomId, Kind = c.Kind, Status = c.Status,
                CreatedAt = c.CreatedAt, DueTime = c.DueTime, RecoverySeconds = c.RecoverySeconds,
                SourceEntityId = c.SourceEntityId, Description = c.Description,
                ResponseId = SnapshotData.OptionalId(c.Response?.Id), BudgetCharged = c.BudgetCharged,
                ResolutionAt = c.ResolutionAt, ResolutionReason = c.ResolutionReason }).ToArray(),
            Promises = promises.Select(p => new WakePromiseSnapshot { Id = p.Id, GuestId = p.GuestId,
                RoomId = p.RoomId, DueTime = p.DueTime, CompletedAt = p.CompletedAt,
                Status = p.Status, DueNotified = p.DueNotified }).ToArray(),
            Items = items.Select(i => new ServiceItemSnapshot { Id = i.Id, GuestId = SnapshotData.OptionalId(i.GuestId),
                PlayerId = i.PlayerId ?? -1, LastPlayerId = i.LastPlayerId ?? -1, RoomId = i.RoomId ?? 0,
                Generation = i.Generation, Kind = i.Kind, Location = i.Location }).ToArray()
        };

        internal void RestoreSnapshot(ServiceLayerSnapshot data)
        {
            day = data.Day; serviceEnd = data.ServiceEnd; LastRefillDay = data.LastRefillDay; StaffCount = data.StaffCount;
            cases.Clear(); promises.Clear(); items.Clear();
            foreach (var c in data.Cases) cases.Add(new ServiceCase(c.Id, c.GuestId, c.RoomId, c.Kind,
                c.CreatedAt, c.DueTime, c.SourceEntityId, c.SourceRoomId, c.Description)
                { Status = c.Status, RecoverySeconds = c.RecoverySeconds, BudgetCharged = c.BudgetCharged,
                    ResolutionAt = c.ResolutionAt, ResolutionReason = c.ResolutionReason });
            foreach (var p in data.Promises) promises.Add(new PromiseWakeUp(p.Id, p.GuestId, p.RoomId, p.DueTime)
                { Status = p.Status, DueNotified = p.DueNotified, CompletedAt = p.CompletedAt });
            foreach (var i in data.Items) items.Add(new ServiceItemState(i.Id, i.Kind, i.Generation, SnapshotData.OptionalId(i.GuestId))
                { Location = i.Location, PlayerId = i.PlayerId < 0 ? (int?)null : i.PlayerId,
                  LastPlayerId = i.LastPlayerId < 0 ? (int?)null : i.LastPlayerId, RoomId = i.RoomId == 0 ? (int?)null : i.RoomId });
            RestoreResponses(data);
        }
    }

    internal static partial class SnapshotValidation
    {
        internal static void Services(HotelModelSnapshot snapshot, bool enabled, IReadOnlyCollection<int> roomIds, bool naturalCommunicationEnabled = false)
        {
            Require(snapshot.HasServices == enabled, "Service configuration differs from this hotel.");
            // JsonUtility may materialize an empty nested class for an absent optional subsystem.
            if (!enabled) return;
            var s = snapshot.ServiceLayer;
            Require(s != null, "Missing guest services.");
            Require(s.NaturalCommunicationEnabled == naturalCommunicationEnabled, "Guest communication configuration differs.");
            Require(s.Day >= 0 && s.Day <= 3 && s.LastRefillDay >= 1 && s.LastRefillDay <= 4 &&
                s.StaffCount >= 1 && s.StaffCount <= 2, "Invalid service day or staff count.");
            Range(s.ServiceEnd);
            var guests = new HashSet<string>(snapshot.Guests.Select(g => g.Application.Id));
            bool Room(int id, bool optional = false) => optional && id == 0 || roomIds.Contains(id);
            var cases = Array(s.Cases, 32); var promises = Array(s.Promises, 6); var items = Array(s.Items, 18);
            Unique(cases.Select(c => c.Id)); Unique(cases.Select(c => c.GuestId + "/" + c.Kind));
            Unique(cases.Where(c => c.Status == ServiceStatus.Requested || c.Status == ServiceStatus.Acknowledged ||
                c.Status == ServiceStatus.InProgress).Select(c => c.GuestId));
            Unique(promises.Select(p => p.Id)); Unique(promises.Select(p => p.GuestId)); Unique(items.Select(i => i.Id));
            Unique(items.Where(i => i.PlayerId >= 0).Select(i => i.PlayerId));
            foreach (var c in cases)
            {
                Text(c.Id, 512); Text(c.GuestId); Text(c.SourceEntityId, 256); Text(c.Description, 2048);
                EnumValue(c.Kind); EnumValue(c.Status); Nonnegative(c.CreatedAt, c.DueTime, c.RecoverySeconds);
                Require(guests.Contains(c.GuestId) && Room(c.RoomId) && Room(c.SourceRoomId), "Invalid service guest or room.");
                Require(c.Id == c.GuestId + "/service/" + c.Kind + "/" + c.SourceEntityId, "Invalid service identity.");
                Require(c.DueTime >= c.CreatedAt, "Service deadline precedes its request.");
                OptionalId(c.ResponseId, 512); Text(c.ResolutionReason, 2048, true); OptionalHotelTime(c.ResolutionAt);
                if (c.Kind == ServiceKind.AskNeighborsQuiet)
                    Require(snapshot.Incidents.Any(i => i.GuestId == c.GuestId && i.Reason == IncidentReason.Noise &&
                        i.Cause != null && i.Cause.SourceEntityId == c.SourceEntityId), "Noise service has no real causal situation.");
                else
                {
                    string source = c.Kind == ServiceKind.ExtraBlanket ? "room/" + c.SourceRoomId + "/temperature" :
                        c.Kind == ServiceKind.LuggageStorage ? "room/" + c.SourceRoomId + "/readiness" :
                        "schedule/" + c.GuestId + (c.Kind == ServiceKind.WakeUpCall ? "/departure" : "/checkout");
                    Require(c.SourceEntityId == source, "Invalid service cause.");
                }
            }
            foreach (var p in promises)
            {
                Text(p.Id, 512); Text(p.GuestId); EnumValue(p.Status); Range(p.DueTime); Range(p.CompletedAt, -1);
                var agreement = cases.SingleOrDefault(c => c.Id == p.Id && c.GuestId == p.GuestId && c.Kind == ServiceKind.WakeUpCall);
                Require(guests.Contains(p.GuestId) && Room(p.RoomId) && agreement != null,
                    "Promise has no matching service agreement.");
                Require(agreement.RoomId == p.RoomId && agreement.DueTime == p.DueTime, "Promise differs from its service agreement.");
                ServiceStatus expected = p.Status == PromiseStatus.Accepted ? ServiceStatus.InProgress :
                    p.Status == PromiseStatus.Completed ? ServiceStatus.Fulfilled :
                    p.Status == PromiseStatus.Missed ? ServiceStatus.Expired : ServiceStatus.Declined;
                Require(agreement.Status == expected, "Promise and service completion disagree.");
                Require((p.Status == PromiseStatus.Completed) == (p.CompletedAt >= 0), "Invalid promise completion.");
            }
            foreach (var i in items)
            {
                Text(i.Id, 256); OptionalId(i.GuestId); EnumValue(i.Kind); EnumValue(i.Location);
                Require(i.Generation >= 0 && i.PlayerId >= -1 && i.PlayerId <= 1 && i.LastPlayerId >= -1 && i.LastPlayerId <= 1 &&
                    Room(i.RoomId, true), "Invalid service item state.");
                Require((i.Location == ServiceItemLocation.HeldByPlayer) == (i.PlayerId >= 0), "Invalid service item ownership.");
                Require(string.IsNullOrEmpty(i.GuestId) || guests.Contains(i.GuestId), "Unknown service item guest.");
                if (i.Kind == ServiceItemKind.Luggage)
                    Require(!string.IsNullOrEmpty(i.GuestId) && i.Id == "luggage:" + i.GuestId, "Invalid luggage identity.");
                else
                {
                    string prefix = i.Kind == ServiceItemKind.Blanket ? "blanket:" : "bulb:";
                    Require(Enumerable.Range(0, 6).Any(slot => i.Id == prefix + slot), "Invalid stock identity.");
                    Require(i.Location != ServiceItemLocation.Stored, "Only luggage belongs in luggage storage.");
                }
            }
            GuestResponses(snapshot, naturalCommunicationEnabled);
        }
    }
}
