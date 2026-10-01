using System;
using System.Collections.Generic;
using System.Linq;

namespace WorstHotel
{
    public enum NoiseCategory { Television, PhoneCall, Plumbing, Amplifier, Visitor }

    /// <summary>A measured, staged world emitter. Room ambience and developer overrides are not emitters.</summary>
    public interface INoiseSource
    {
        string SourceEntityId { get; }
        string SourceGuestId { get; }
        int SourceRoomId { get; }
        NoiseCategory Category { get; }
        float NoiseOutput { get; }
        bool Active { get; }
    }

    public sealed class RoomNoiseSource : INoiseSource
    {
        public string SourceEntityId { get; }
        public string SourceGuestId { get; }
        public int SourceRoomId { get; }
        public NoiseCategory Category { get; }
        public float NoiseOutput { get; }
        public float ReceivedNoise { get; }
        public bool Active => NoiseOutput > 0;
        public string Label => Category == NoiseCategory.Amplifier ? "Amplifier" : Category == NoiseCategory.Television ? "Television" : Category == NoiseCategory.PhoneCall ? "Phone call" : Category == NoiseCategory.Visitor ? "Visitor conversation" : "Shower / plumbing";
        public RoomNoiseSource(string entityId, string guestId, int roomId, NoiseCategory category, float output, float received = 0)
        { SourceEntityId = entityId; SourceGuestId = guestId; SourceRoomId = roomId; Category = category; NoiseOutput = output; ReceivedNoise = received; }
    }

    public readonly struct SituationKey : IEquatable<SituationKey>
    {
        public IncidentReason Reason { get; }
        public string GuestId { get; }
        public string SourceEntityId { get; }
        public SituationKey(IncidentReason reason, string guestId, string sourceEntityId)
        {
            if (string.IsNullOrWhiteSpace(guestId) || string.IsNullOrWhiteSpace(sourceEntityId))
                throw new ArgumentException("A situation needs both an affected guest and an identifiable source.");
            Reason = reason; GuestId = guestId; SourceEntityId = sourceEntityId;
        }
        public bool Equals(SituationKey other) => Reason == other.Reason && GuestId == other.GuestId && SourceEntityId == other.SourceEntityId;
        public override bool Equals(object other) => other is SituationKey key && Equals(key);
        public override int GetHashCode() => ToString().GetHashCode();
        public override string ToString() => GuestId + ":" + Reason + ":" + SourceEntityId;
    }

    public sealed class SituationCause
    {
        public string SourceType { get; }
        public string SourceEntityId { get; }
        public string SourceGuestId { get; }
        public int SourceRoomId { get; }
        public float RawIntensity { get; }
        public float ReceivedIntensity { get; }
        public float GuestThreshold { get; }
        public string Description { get; }
        public SituationCause(string type, string entity, string sourceGuest, int sourceRoom, float raw, float received, float threshold, string description)
        { SourceType = type; SourceEntityId = entity; SourceGuestId = sourceGuest; SourceRoomId = sourceRoom; RawIntensity = raw; ReceivedIntensity = received; GuestThreshold = threshold; Description = description; }
    }

    public sealed class SituationHistoryEntry
    {
        public float Time { get; }
        public string Reason { get; }
        public SituationHistoryEntry(float time, string reason) { Time = time; Reason = reason; }
    }

    /// <summary>Only the environment the actor is actually occupying; never the reserved room at a distance.</summary>
    public sealed class GuestPerception
    {
        public bool InAssignedRoom { get; internal set; }
        public int RoomId { get; internal set; }
        public float Temperature { get; internal set; }
        public float PerceivedTemperature { get; internal set; }
        public float Noise { get; internal set; }
        public IReadOnlyList<RoomNoiseSource> NoiseSources { get; internal set; } = Array.Empty<RoomNoiseSource>();
        public SituationCause TemperatureCause { get; internal set; }
        public IReadOnlyList<SituationCause> ConditionCauses { get; internal set; } = Array.Empty<SituationCause>();
    }

    public sealed class GuestMemory
    {
        public int NumberOfComplaints { get; internal set; }
        public int CompensationReceived { get; internal set; }
        public int ProblemsResolvedSuccessfully { get; internal set; }
        public int ProblemsIgnored { get; internal set; }
        public int PreviousNoiseWarnings { get; internal set; }
        public int ServicesRequested { get; internal set; }
        public int ServicesFulfilled { get; internal set; }
        public int ServicesDeclined { get; internal set; }
        public int PromisesKept { get; internal set; }
        public int PromisesBroken { get; internal set; }
        public int BlanketsDelivered { get; internal set; }
        public int LuggageStored { get; internal set; }
        internal readonly Dictionary<IncidentReason, int> categoryComplaints = new Dictionary<IncidentReason, int>();
        public IReadOnlyDictionary<IncidentReason, int> RepeatedProblemCount => new System.Collections.ObjectModel.ReadOnlyDictionary<IncidentReason, int>(
            categoryComplaints.ToDictionary(x => x.Key, x => Math.Max(0, x.Value - 1)));
        public int ComplaintsInCategory(IncidentReason reason) => categoryComplaints.TryGetValue(reason, out int count) ? count : 0;
        public float PatienceMultiplier(IncidentReason reason, NeedSettings settings) =>
            Math.Max(settings.MinimumRepeatPatienceMultiplier, 1 - ComplaintsInCategory(reason) * settings.RepeatPatienceReduction);
        internal void RecordComplaint(IncidentReason reason, NeedSettings settings)
        {
            NumberOfComplaints = Math.Min(settings.MemoryCountLimit, NumberOfComplaints + 1);
            categoryComplaints[reason] = Math.Min(settings.MemoryCountLimit, ComplaintsInCategory(reason) + 1);
        }
    }
}
