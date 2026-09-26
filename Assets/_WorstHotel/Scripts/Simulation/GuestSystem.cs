using System;
using System.Collections.Generic;
using System.Linq;

namespace WorstHotel
{
    public static class GuestSystem
    {
        private static readonly string[] Names =
        { "Mara Bell", "Owen Finch", "Nina Moss", "Theo Vale", "Iris Reed", "Felix Dawn", "Ada Brook", "Luca Hart" };

        public static BookingApplication[] GenerateContinuousApplications(int day, IEnumerable<GuestProfile> profiles)
        {
            if (day < 1) throw new ArgumentOutOfRangeException(nameof(day));
            if (profiles == null) throw new ArgumentNullException(nameof(profiles));
            var byKind = profiles.ToDictionary(profile => profile.Kind);
            var kinds = new[] { GuestKind.Budget, GuestKind.ColdSensitive, GuestKind.Business, GuestKind.Budget,
                GuestKind.ColdSensitive, GuestKind.Business, GuestKind.Business, GuestKind.Budget };
            return kinds.Select((kind, index) => new BookingApplication("stay-" + day + "-" + (index + 1),
                Names[(index + (day - 1) % Names.Length * 3) % Names.Length], byKind[kind], byKind[kind].ReferencePrice)).ToArray();
        }

        public static BookingApplication[] GenerateApplications(int day, IEnumerable<GuestProfile> profiles, int day3BusinessReferencePrice = 525)
        {
            if (day < 1 || day > 3) throw new ArgumentOutOfRangeException(nameof(day));
            if (profiles == null) throw new ArgumentNullException(nameof(profiles));
            var byKind = profiles.ToDictionary(profile => profile.Kind);
            foreach (GuestKind kind in Enum.GetValues(typeof(GuestKind)))
                if (!byKind.ContainsKey(kind)) throw new ArgumentException("All three guest archetypes are required.");
            var kinds = day == 1 ? new[] { GuestKind.Budget, GuestKind.Budget, GuestKind.ColdSensitive, GuestKind.Business } :
                day == 2 ? new[] { GuestKind.Budget, GuestKind.ColdSensitive, GuestKind.Business, GuestKind.Budget,
                    GuestKind.ColdSensitive, GuestKind.Business, GuestKind.Business, GuestKind.Budget } :
                new[] { GuestKind.Business, GuestKind.Business, GuestKind.ColdSensitive, GuestKind.Budget,
                    GuestKind.Business, GuestKind.ColdSensitive, GuestKind.Business, GuestKind.Budget };
            var applications = new BookingApplication[kinds.Length];
            for (int i = 0; i < kinds.Length; i++)
            {
                var profile = byKind[kinds[i]];
                int reference = day == 3 && profile.Kind == GuestKind.Business ? day3BusinessReferencePrice : profile.ReferencePrice;
                applications[i] = new BookingApplication("day" + day + "-guest" + (i + 1), Names[(i + (day - 1) * 3) % Names.Length], profile, reference);
            }
            return applications;
        }
    }
}
