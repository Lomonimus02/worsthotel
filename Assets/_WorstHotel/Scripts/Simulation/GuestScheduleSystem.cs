using System;
using System.Collections.Generic;
using System.Linq;

namespace WorstHotel
{
    public readonly struct GuestScheduleEntry
    {
        public GuestActivity Activity { get; }
        public float Duration { get; }
        public GuestScheduleEntry(GuestActivity activity, float duration) { Activity = activity; Duration = duration; }
    }

    public sealed class GuestSchedule
    {
        public string GuestId { get; }
        public float ArrivalTime { get; }
        public float SleepTime { get; }
        public float CheckoutTime { get; internal set; }
        public IReadOnlyList<GuestScheduleEntry> Activities { get; }
        internal GuestSchedule(string id, float arrival, float sleep, float checkout, GuestScheduleEntry[] activities)
        { GuestId = id; ArrivalTime = arrival; SleepTime = sleep; CheckoutTime = checkout; Activities = Array.AsReadOnly(activities); }
    }

    public sealed partial class GuestScheduleSystem
    {
        public LivingHotelSettings Settings { get; }
        public IReadOnlyList<GuestSchedule> Items => schedules.AsReadOnly();
        private readonly List<GuestSchedule> schedules = new List<GuestSchedule>();
        public GuestScheduleSystem(LivingHotelSettings settings) => Settings = settings ?? throw new ArgumentNullException(nameof(settings));

        public void StartDay(int day, IEnumerable<GuestStay> guests, float serviceSeconds)
        {
            if (ReadOnlyMirror) return;
            if (day < 1 || guests == null || !Number.IsFinite(serviceSeconds) || serviceSeconds <= 0)
                throw new ArgumentException("A schedule needs a valid day, guests and service duration.");
            schedules.Clear();
            var ordered = guests.OrderBy(guest => guest.GuestId, StringComparer.Ordinal).ToArray();
            float first = Math.Min(Settings.FirstArrivalSeconds, serviceSeconds * 0.2f);
            float spacing = Math.Min(Settings.ArrivalSpacingSeconds, (serviceSeconds * 0.45f - first) / Math.Max(1, ordered.Length));
            float jitter = Math.Min(Settings.ArrivalJitterSeconds, spacing * 0.8f);
            for (int i = 0; i < ordered.Length; i++)
                Attach(ordered[i], day, first + spacing * i, jitter, serviceSeconds);
        }

        public GuestAgent AddWalkIn(GuestStay guest, int day, float now, float serviceSeconds)
        {
            if (ReadOnlyMirror) throw new InvalidOperationException(HotelSimulation.MirrorMessage);
            var agent = Attach(guest, day, now, 0, serviceSeconds);
            agent.State = GuestAgentState.Arriving; agent.StateChangedAt = now;
            return agent;
        }

        private GuestAgent Attach(GuestStay guest, int day, float arrival, float jitter, float serviceSeconds)
        {
            uint random = Seed(Settings.Seed, day, guest.GuestId);
            arrival += Next(ref random) * jitter;
            float checkout = serviceSeconds * Settings.CheckoutFraction;
            checkout += Next(ref random) * serviceSeconds * (1 - Settings.CheckoutFraction) * 0.3f;
            var entries = new GuestScheduleEntry[24];
            var archetype = guest.Application.Archetype;
            bool noisy = (archetype.Traits & GuestTraits.Noisy) != 0;
            for (int i = 0; i < entries.Length; i++)
            {
                // Seeded choices preserve each guest's personality without a shared shower/TV clock.
                var activity = i == 0 ? GuestActivity.Unpack : PickActivity(archetype.Kind, noisy, ref random);
                if (i > 0 && activity == entries[i - 1].Activity)
                    activity = activity == GuestActivity.QuietRest ? GuestActivity.WatchTV : GuestActivity.QuietRest;
                float minimum = activity == GuestActivity.LeaveHotel ? Settings.AwayDurationMin :
                    activity == GuestActivity.QuietRest || activity == GuestActivity.Work ? Settings.QuietDurationMin : Settings.ActivityDurationMin;
                float maximum = activity == GuestActivity.LeaveHotel ? Settings.AwayDurationMax :
                    activity == GuestActivity.QuietRest || activity == GuestActivity.Work ? Settings.QuietDurationMax : Settings.ActivityDurationMax;
                float duration = minimum + Next(ref random) * (maximum - minimum);
                if (activity == GuestActivity.Shower && (archetype.Traits & GuestTraits.ColdSensitive) != 0)
                    duration *= Settings.ColdShowerDurationMultiplier;
                entries[i] = new GuestScheduleEntry(activity, duration);
            }
            float sleepFraction = Settings.SleepStartFraction - (archetype.Kind == GuestKind.Business ? Settings.BusinessSleepAdvanceFraction : 0);
            float sleep = serviceSeconds * sleepFraction + (Next(ref random) - .5f) * Settings.QuietDurationMin;
            var schedule = new GuestSchedule(guest.GuestId, arrival, Math.Min(checkout - Settings.ActivityDurationMin, Math.Max(arrival, sleep)), checkout, entries);
            schedules.Add(schedule);
            return guest.Agent = new GuestAgent(guest.GuestId, schedule, guest.Application.Archetype.Needs.PatienceSeconds * Settings.WaitingPatienceMultiplier);
        }

        GuestActivity PickActivity(GuestKind kind, bool noisy, ref uint random)
        {
            // Use an independent trait roll so Noisy does not erase a budget traveller's
            // outing window (the same low roll must not select both alternatives).
            if (noisy && Next(ref random) < Settings.NoisyLoudProbability * .48f)
                return Next(ref random) < .70f ? GuestActivity.LoudRoom : GuestActivity.PhoneCall;
            float roll = Next(ref random);
            if (kind == GuestKind.Business)
            {
                if (roll < .36f) return GuestActivity.Work;
                if (roll < .55f) return GuestActivity.PhoneCall;
                if (roll < .68f) return GuestActivity.Shower;
                if (roll < .74f) return GuestActivity.LeaveHotel;
                if (roll < .85f) return GuestActivity.WatchTV;
                return GuestActivity.QuietRest;
            }
            if (kind == GuestKind.Budget)
            {
                if (roll < .29f) return GuestActivity.LeaveHotel;
                if (roll < .51f) return GuestActivity.WatchTV;
                if (roll < .67f) return GuestActivity.Shower;
                if (roll < .76f) return GuestActivity.PhoneCall;
                if (roll < .76f + Settings.NormalLoudProbability * .20f) return GuestActivity.LoudRoom;
                return GuestActivity.QuietRest;
            }
            if (roll < .31f) return GuestActivity.Shower;
            if (roll < .50f) return GuestActivity.QuietRest;
            if (roll < .67f) return GuestActivity.WatchTV;
            if (roll < .80f) return GuestActivity.LeaveHotel;
            if (roll < .91f) return GuestActivity.PhoneCall;
            return GuestActivity.Work;
        }

        private static uint Seed(int seed, int day, string id)
        {
            uint value = 2166136261;
            unchecked
            {
                value = (value ^ (uint)seed) * 16777619;
                value = (value ^ (uint)day) * 16777619;
                foreach (char character in id) value = (value ^ character) * 16777619;
            }
            return value == 0 ? 1u : value;
        }
        private static float Next(ref uint state)
        {
            state ^= state << 13; state ^= state >> 17; state ^= state << 5;
            return (state & 0x00ffffff) / 16777216f;
        }
    }
}


