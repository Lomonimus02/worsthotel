using System.Linq;
using NUnit.Framework;

namespace WorstHotel.Tests
{
    public sealed class GuestAgencyScheduleTests
    {
        static GuestStay Stay(string id, GuestKind kind, GuestTraits traits) => new GuestStay(
            new BookingApplication(id, id, new GuestProfile(kind, kind.ToString(), "", 200, 1, 19, 1, 20, 70, .4f, traits), 200), 101, 200);

        static GuestSchedule Schedule(string id, GuestKind kind, GuestTraits traits, LivingHotelSettings settings = null)
        {
            var guest = Stay(id, kind, traits);
            var system = new GuestScheduleSystem(settings ?? new LivingHotelSettings());
            system.StartDay(1, new[] { guest }, 300);
            return guest.Agent.Schedule;
        }

        [Test]
        public void SameIdentitySeedAndTraitsReproduceVariedSchedulesAndDurations()
        {
            var first = Schedule("traveller-17", GuestKind.Budget, GuestTraits.None);
            var repeat = Schedule("traveller-17", GuestKind.Budget, GuestTraits.None);
            var other = Schedule("traveller-18", GuestKind.Budget, GuestTraits.None);
            Assert.That(first.Activities.Select(item => (item.Activity, item.Duration)),
                Is.EqualTo(repeat.Activities.Select(item => (item.Activity, item.Duration))));
            Assert.That(first.Activities.Select(item => item.Activity), Is.Not.EqualTo(other.Activities.Select(item => item.Activity)));
            Assert.That(first.Activities.First().Activity, Is.EqualTo(GuestActivity.Unpack));
            Assert.That(first.Activities.Select(item => item.Activity).Distinct().Count(), Is.GreaterThanOrEqualTo(5));
            Assert.That(first.Activities.Zip(first.Activities.Skip(1), (a, b) => a.Activity == b.Activity).Any(same => same), Is.False);
        }

        [Test]
        public void BusinessBudgetAndNoisyTraitsChangeActivityTendenciesAndSleepWindow()
        {
            var business = Enumerable.Range(0, 48).Select(i => Schedule("guest-" + i, GuestKind.Business, GuestTraits.None)).ToArray();
            var budget = Enumerable.Range(0, 48).Select(i => Schedule("guest-" + i, GuestKind.Budget, GuestTraits.None)).ToArray();
            var noisy = Enumerable.Range(0, 48).Select(i => Schedule("guest-" + i, GuestKind.Budget, GuestTraits.Noisy)).ToArray();
            int Count(GuestSchedule[] schedules, GuestActivity type) => schedules.Sum(s => s.Activities.Count(a => a.Activity == type));
            Assert.That(Count(business, GuestActivity.Work), Is.GreaterThan(Count(budget, GuestActivity.Work)));
            Assert.That(Count(business, GuestActivity.PhoneCall), Is.GreaterThan(Count(budget, GuestActivity.PhoneCall)));
            Assert.That(Count(budget, GuestActivity.LeaveHotel), Is.GreaterThan(Count(business, GuestActivity.LeaveHotel)));
            Assert.That(Count(budget, GuestActivity.WatchTV), Is.GreaterThan(Count(business, GuestActivity.WatchTV)));
            Assert.That(Count(noisy, GuestActivity.LoudRoom), Is.GreaterThan(Count(budget, GuestActivity.LoudRoom)));
            Assert.That(Count(noisy, GuestActivity.LeaveHotel), Is.GreaterThan(Count(business, GuestActivity.LeaveHotel)),
                "A noisy budget trait must not erase the traveller's ordinary excursions.");
            Assert.That(business.Average(s => s.SleepTime), Is.LessThan(budget.Average(s => s.SleepTime)));
        }

        [Test]
        public void ColdSensitiveShowersUseConfiguredLongerDurationWithoutChangingTheirIdentitySeed()
        {
            var settings = new LivingHotelSettings(coldShowerDurationMultiplier: 1.6f);
            var ordinary = Schedule("cold-guest", GuestKind.ColdSensitive, GuestTraits.None, settings);
            var cold = Schedule("cold-guest", GuestKind.ColdSensitive, GuestTraits.ColdSensitive, settings);
            Assert.That(ordinary.Activities.Select(a => a.Activity), Is.EqualTo(cold.Activities.Select(a => a.Activity)));
            Assert.That(cold.Activities.Any(a => a.Activity == GuestActivity.Shower), Is.True);
            for (int i = 0; i < ordinary.Activities.Count; i++)
                Assert.That(cold.Activities[i].Duration, Is.EqualTo(ordinary.Activities[i].Duration *
                    (cold.Activities[i].Activity == GuestActivity.Shower ? settings.ColdShowerDurationMultiplier : 1)).Within(.001f));
        }
    }
}
