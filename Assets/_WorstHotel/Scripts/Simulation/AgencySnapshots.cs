using System;
using System.Collections.Generic;
using System.Linq;

namespace WorstHotel
{
    [Serializable] public sealed class NoiseSourceSnapshot
    {
        public string SourceEntityId, SourceGuestId;
        public int SourceRoomId;
        public NoiseCategory Category;
        public float NoiseOutput, ReceivedNoise;
    }
    [Serializable] public sealed class SituationCauseSnapshot
    {
        public string SourceType, SourceEntityId, SourceGuestId, Description;
        public int SourceRoomId;
        public float RawIntensity, ReceivedIntensity, GuestThreshold;
    }
    [Serializable] public sealed class SituationHistorySnapshot { public float Time; public string Reason; }
    [Serializable] public sealed class CategoryMemorySnapshot { public IncidentReason Reason; public int Count; }
    [Serializable] public sealed class GuestMemorySnapshot
    {
        public int NumberOfComplaints, CompensationReceived, ProblemsResolvedSuccessfully, ProblemsIgnored, PreviousNoiseWarnings;
        public int ServicesRequested, ServicesFulfilled, ServicesDeclined, PromisesKept, PromisesBroken, BlanketsDelivered, LuggageStored;
        public CategoryMemorySnapshot[] Categories;
    }
    [Serializable] public sealed class GuestPerceptionSnapshot
    {
        public bool InAssignedRoom, HasTemperatureCause;
        public int RoomId;
        public float Temperature, PerceivedTemperature, Noise;
        public NoiseSourceSnapshot[] NoiseSources;
        public SituationCauseSnapshot TemperatureCause;
        public SituationCauseSnapshot[] ConditionCauses;
    }

    internal static partial class SnapshotData
    {
        internal static NoiseSourceSnapshot Capture(RoomNoiseSource s) => new NoiseSourceSnapshot
        { SourceEntityId=s.SourceEntityId,SourceGuestId=s.SourceGuestId,SourceRoomId=s.SourceRoomId,Category=s.Category,NoiseOutput=s.NoiseOutput,ReceivedNoise=s.ReceivedNoise };
        internal static RoomNoiseSource Source(NoiseSourceSnapshot s) => new RoomNoiseSource(s.SourceEntityId,s.SourceGuestId,s.SourceRoomId,s.Category,s.NoiseOutput,s.ReceivedNoise);
        internal static SituationCauseSnapshot Capture(SituationCause c) => c == null ? null : new SituationCauseSnapshot
        { SourceType=c.SourceType,SourceEntityId=c.SourceEntityId,SourceGuestId=OptionalId(c.SourceGuestId),SourceRoomId=c.SourceRoomId,
            RawIntensity=c.RawIntensity,ReceivedIntensity=c.ReceivedIntensity,GuestThreshold=c.GuestThreshold,Description=c.Description };
        internal static SituationCause Cause(SituationCauseSnapshot c) => c == null ? null : new SituationCause(c.SourceType,c.SourceEntityId,
            OptionalId(c.SourceGuestId),c.SourceRoomId,c.RawIntensity,c.ReceivedIntensity,c.GuestThreshold,c.Description);
        internal static GuestMemorySnapshot Capture(GuestMemory m) => new GuestMemorySnapshot
        { NumberOfComplaints=m.NumberOfComplaints,CompensationReceived=m.CompensationReceived,ProblemsResolvedSuccessfully=m.ProblemsResolvedSuccessfully,
            ProblemsIgnored=m.ProblemsIgnored,PreviousNoiseWarnings=m.PreviousNoiseWarnings,
            ServicesRequested=m.ServicesRequested,ServicesFulfilled=m.ServicesFulfilled,ServicesDeclined=m.ServicesDeclined,
            PromisesKept=m.PromisesKept,PromisesBroken=m.PromisesBroken,BlanketsDelivered=m.BlanketsDelivered,LuggageStored=m.LuggageStored,
            Categories=m.categoryComplaints.OrderBy(x=>x.Key).Select(x=>new CategoryMemorySnapshot{Reason=x.Key,Count=x.Value}).ToArray() };
        internal static void Restore(GuestMemory m,GuestMemorySnapshot s)
        {
            m.NumberOfComplaints=s.NumberOfComplaints;m.CompensationReceived=s.CompensationReceived;
            m.ProblemsResolvedSuccessfully=s.ProblemsResolvedSuccessfully;m.ProblemsIgnored=s.ProblemsIgnored;m.PreviousNoiseWarnings=s.PreviousNoiseWarnings;
            m.ServicesRequested=s.ServicesRequested;m.ServicesFulfilled=s.ServicesFulfilled;m.ServicesDeclined=s.ServicesDeclined;
            m.PromisesKept=s.PromisesKept;m.PromisesBroken=s.PromisesBroken;m.BlanketsDelivered=s.BlanketsDelivered;m.LuggageStored=s.LuggageStored;
            m.categoryComplaints.Clear();foreach(var c in s.Categories)m.categoryComplaints.Add(c.Reason,c.Count);
        }
        internal static GuestPerceptionSnapshot Capture(GuestPerception p) => new GuestPerceptionSnapshot
        { InAssignedRoom=p.InAssignedRoom,RoomId=p.RoomId,Temperature=p.Temperature,PerceivedTemperature=p.PerceivedTemperature,Noise=p.Noise,
            NoiseSources=p.NoiseSources.Select(Capture).ToArray(),HasTemperatureCause=p.TemperatureCause!=null,
            TemperatureCause=Capture(p.TemperatureCause),ConditionCauses=p.ConditionCauses.Select(Capture).ToArray() };
        internal static void Restore(GuestPerception p,GuestPerceptionSnapshot s)
        {
            p.InAssignedRoom=s.InAssignedRoom;p.RoomId=s.RoomId;p.Temperature=s.Temperature;p.Noise=s.Noise;
            p.PerceivedTemperature=s.PerceivedTemperature;
            p.NoiseSources=System.Array.AsReadOnly(s.NoiseSources.Select(Source).ToArray());
            p.TemperatureCause=s.HasTemperatureCause ? Cause(s.TemperatureCause) : null;
            p.ConditionCauses=System.Array.AsReadOnly(s.ConditionCauses.Select(Cause).ToArray());
        }
    }
    public sealed partial class NoiseSystem
    {
        // Install host measurements without ticking the client model or inventing noise emitters.
        internal void RestoreMeasuredSources(NoiseSourceSnapshot[] data)
        {
            actualSources.Clear();contributions.Clear();
            foreach(int id in Graph.RoomIds)contributions.Add(id,new List<RoomNoiseSource>());
            foreach(var state in data)
            {
                var source=SnapshotData.Source(state);actualSources.Add(source);
                foreach(var link in Graph.Links)
                {
                    int target=link.RoomA==source.SourceRoomId?link.RoomB:link.RoomB==source.SourceRoomId?link.RoomA:0;
                    if(target==0)continue;
                    float transmission=link.Kind==RoomNoiseLinkKind.SharedWall?Settings.SharedWallTransmission:Settings.CorridorTransmission;
                    contributions[target].Add(new RoomNoiseSource(source.SourceEntityId,source.SourceGuestId,source.SourceRoomId,
                        source.Category,source.NoiseOutput,source.NoiseOutput*transmission));
                }
            }
        }
    }
}
