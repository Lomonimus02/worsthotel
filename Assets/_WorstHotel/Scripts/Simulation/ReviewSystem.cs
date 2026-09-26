using System;
using System.Collections.Generic;
using System.Linq;

namespace WorstHotel
{
    public static class ReviewSystem
    {
        public static string Build(GuestStay stay, float satisfaction, int compensation)
        {
            if (stay == null || !Number.IsFinite(satisfaction) || compensation < 0) throw new ArgumentException("Review requires factual stay and checkout values.");
            if (stay.Elapsed <= 0) return (stay.CheckInDelayPenaltySeconds > 0 ? "Check-in took longer than my patience allowed. " : "") +
                "My stay ended before there was enough time to judge the room.";
            var issues = new[]
            {
                (Seconds: stay.ColdExposureSeconds, Label: "cold", Phrase: "It was cold"),
                (Seconds: stay.HotExposureSeconds, Label: "heat", Phrase: "It was too warm"),
                (Seconds: stay.NoiseExposureSeconds, Label: "noise", Phrase: "Noise disturbed me"),
                (Seconds: stay.DirtyExposureSeconds, Label: "cleanliness", Phrase: "The room was dirty"),
                (Seconds: stay.FixtureExposureSeconds, Label: "fixture", Phrase: stay.Needs != null ? "The bedside lamp did not work" : "Worn or broken fixtures let me down"),
                (Seconds: stay.PowerLossExposureSeconds, Label: "electrical power", Phrase: "My room had no electrical power")
            }.Where(issue => issue.Seconds > 0).OrderByDescending(issue => issue.Seconds).ToArray();
            var sentences = new List<string>();
            if (issues.Length == 0)
            {
                sentences.Add(stay.Price > stay.Application.ReferencePrice && satisfaction < 80
                    ? "The room was comfortable, but the price felt steep."
                    : "Comfortable and quiet; this old hotel did its job.");
            }
            else
            {
                float fraction = stay.Elapsed > 0 ? Number.Clamp(issues[0].Seconds / stay.Elapsed, 0, 1) : 0;
                sentences.Add(issues[0].Phrase + " for " + Math.Round(fraction * 100).ToString("F0") + "% of my stay.");
                if (issues.Length > 1) sentences.Add("There were also " + issues[1].Label + " problems.");
                if (stay.Price > stay.Application.ReferencePrice) sentences.Add("At $" + stay.Price + ", I expected better.");
            }
            if (stay.ExpiredComplaintSeconds > 0) sentences.Add("The room problem outlasted my patience.");
            if (stay.CheckInDelayPenaltySeconds > 0) sentences.Add("Check-in took longer than my patience allowed.");
            if (stay.Memory.ProblemsResolvedSuccessfully > 0) sentences.Add("Staff improved the conditions I reported.");
            if (stay.Memory.ProblemsIgnored > 0) sentences.Add("Staff chose to leave a reported problem unresolved.");
            if (stay.Memory.RepeatedProblemCount.Values.Any(count => count > 0)) sentences.Add("I had to complain about the same kind of problem again.");
            if (stay.Memory.BlanketsDelivered > 0) sentences.Add("They brought an extra blanket for the cold.");
            else if (stay.Memory.LuggageStored > 0) sentences.Add("Staff helped store my luggage while I waited.");
            else if (stay.Memory.ServicesFulfilled > 0) sentences.Add("Staff helped with my service request.");
            if (stay.Memory.PromisesBroken > 0) sentences.Add("I was promised a wake-up call, but nobody called.");
            else if (stay.Memory.PromisesKept > 0) sentences.Add("The promised wake-up call was made.");
            if (compensation > 0) sentences.Add((stay.Compensated ? "Staff offered compensation; $" : "Checkout included $")
                + compensation + (stay.Compensated ? " came off the bill." : " compensation."));
            return string.Join(" ", sentences);
        }
    }
}
