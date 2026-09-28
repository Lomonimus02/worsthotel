using System.Collections.Generic;
using System.Linq;

namespace WorstHotel
{
    internal static partial class SnapshotValidation
    {
        static void NoiseSource(NoiseSourceSnapshot s,IReadOnlyCollection<int> rooms,HashSet<string> guests)
        {
            Text(s.SourceEntityId,256);Text(s.SourceGuestId);EnumValue(s.Category);Unit(s.NoiseOutput);Range(s.ReceivedNoise,0,1);
            Require(rooms.Contains(s.SourceRoomId) && guests.Contains(s.SourceGuestId),"Noise emitter is outside this hotel.");
            Require(s.SourceEntityId==(s.Category==NoiseCategory.Amplifier ? GuestServiceSystem.LuggageId(s.SourceGuestId,2) : s.SourceGuestId+"/"+s.Category),"Noise source identity disagrees with its guest and category.");
        }
        static void Cause(SituationCauseSnapshot c,IReadOnlyCollection<int> rooms,HashSet<string> guests)
        {
            Text(c.SourceType,160);Text(c.SourceEntityId,256);OptionalId(c.SourceGuestId);Text(c.Description,2048);
            Require(rooms.Contains(c.SourceRoomId) && (string.IsNullOrEmpty(c.SourceGuestId) || guests.Contains(c.SourceGuestId)),"Unknown situation source.");
            Range(c.RawIntensity,-100,100);Range(c.ReceivedIntensity,-100,100);Range(c.GuestThreshold,-100,100);
        }
        static void GuestAgency(GuestSnapshot guest,IReadOnlyCollection<int> rooms,HashSet<string> guests)
        {
            var m=guest.Memory;var p=guest.Perception;
            Require(m!=null && p!=null,"Missing guest perception or memory.");
            Require(m.NumberOfComplaints>=0 && m.NumberOfComplaints<=10000 && m.CompensationReceived>=0 && m.CompensationReceived<=guest.Price &&
                m.ProblemsResolvedSuccessfully>=0 && m.ProblemsResolvedSuccessfully<=10000 && m.ProblemsIgnored>=0 && m.ProblemsIgnored<=10000 &&
                m.PreviousNoiseWarnings>=0 && m.PreviousNoiseWarnings<=10000,"Invalid guest memory count.");
            var categories=Array(m.Categories,7);Unique(categories.Select(c=>c.Reason));
            foreach(var c in categories){EnumValue(c.Reason);Require(c.Count>=0 && c.Count<=10000,"Invalid category memory.");}
            Require(p.RoomId==0 || rooms.Contains(p.RoomId),"Invalid perceived room.");
            if(p.InAssignedRoom)Require(p.RoomId==guest.RoomId,"Guest perceived a different assigned room.");
            Range(p.Temperature,-100,100);Range(p.PerceivedTemperature,-100,108);Unit(p.Noise);
            foreach (int count in new[] { m.ServicesRequested,m.ServicesFulfilled,m.ServicesDeclined,m.PromisesKept,m.PromisesBroken,m.BlanketsDelivered,m.LuggageStored })
                Require(count>=0 && count<=10000,"Invalid service memory.");
            var sources=Array(p.NoiseSources,10);Unique(sources.Select(n=>n.SourceEntityId));
            foreach(var source in sources)NoiseSource(source,rooms,guests);
            // JsonUtility expands a null nested class into a default object on the wire.
            if(p.HasTemperatureCause){Require(p.TemperatureCause!=null,"Missing perceived temperature cause.");Cause(p.TemperatureCause,rooms,guests);}
            foreach(var cause in Array(p.ConditionCauses,3))Cause(cause,rooms,guests);
        }
    }
}
