using System;
using System.Linq;
using NUnit.Framework;

namespace WorstHotel.Tests
{
    public sealed class ContinuousGuestScheduleTests
    {
        static GuestStay Guest(string id, GuestKind kind = GuestKind.Business)
        {
            var profile = new GuestProfile(kind, kind.ToString(), "", 300, 1, 20, 1, 10, 65, .3f);
            return new GuestStay(new BookingApplication(id, id, profile, 300), 101, 300);
        }

        [Test]
        public void DatedSchedulesAppendWithoutReplacingExistingGuestAndKeepExactCalendarTimes()
        {
            var system = new GuestScheduleSystem(new LivingHotelSettings());
            var clock = new HotelGameClock();
            var calendar = new HotelCalendar(clock, new OperationsSettings());
            var first = Guest("arrival-1");
            var second = Guest("arrival-2");
            var agent = system.AttachStay(first, 1, calendar.At(1, 14), calendar.At(1, 23),
                calendar.At(2, 10), calendar.At(2, 8));
            var schedule = agent.Schedule;
            clock.Advance(calendar.At(2, 6));
            system.AttachStay(second, 2, calendar.At(2, 15), calendar.At(2, 23),
                calendar.At(3, 10), calendar.At(3, 8));

            Assert.That(first.Agent, Is.SameAs(agent));
            Assert.That(first.Agent.Schedule, Is.SameAs(schedule));
            Assert.That(system.Items.Count, Is.EqualTo(2));
            Assert.That(schedule.ArrivalTime, Is.EqualTo(180));
            Assert.That(schedule.SleepTime, Is.EqualTo(450));
            Assert.That(schedule.WakeTime, Is.EqualTo(720));
            Assert.That(schedule.CheckoutTime, Is.EqualTo(780));
            Assert.That(second.Agent.ArrivalTime, Is.GreaterThan(first.Agent.CheckoutTime));
            Assert.That(clock.SimulationTime, Is.EqualTo(660), "Attaching a booking must not reset or advance the hotel clock.");
            Assert.That(schedule.Activities[0].Activity, Is.EqualTo(GuestActivity.Unpack));
            Assert.That(schedule.Activities.All(entry => entry.Activity != GuestActivity.AdjustRadiator && entry.Activity != GuestActivity.CallReception), Is.True);
        }

        [Test]
        public void AbsoluteScheduleActivityVariationIsStableAcrossAttachmentOrderAndCalendarOrigin()
        {
            var a = new GuestScheduleSystem(new LivingHotelSettings(seed: 52));
            var b = new GuestScheduleSystem(new LivingHotelSettings(seed: 52));
            var businessA = Guest("business");
            var budgetA = Guest("budget", GuestKind.Budget);
            var businessB = Guest("business");
            var budgetB = Guest("budget", GuestKind.Budget);
            a.AttachStay(businessA, 5, 3000, 3250, 3500, 3420);
            a.AttachStay(budgetA, 5, 3005, 3250, 3500, 3420);
            b.AttachStay(budgetB, 5, 3005, 3250, 3500, 3420);
            b.AttachStay(businessB, 5, 3000, 3250, 3500, 3420);
            Assert.That(businessA.Agent.Schedule.Activities, Is.EqualTo(businessB.Agent.Schedule.Activities));
            Assert.That(budgetA.Agent.Schedule.Activities, Is.EqualTo(budgetB.Agent.Schedule.Activities));
            Assert.That(businessA.Agent.Schedule.Activities, Is.Not.EqualTo(budgetA.Agent.Schedule.Activities));
        }

        [Test]
        public void DuplicateOrInvalidAttachmentCannotReplaceAnExistingSchedule()
        {
            var system = new GuestScheduleSystem(new LivingHotelSettings());
            var first = Guest("same");
            var original = system.AttachStay(first, 1, 180, 450, 780, 720);
            Assert.Throws<InvalidOperationException>(() => system.AttachStay(first, 1, 181, 451, 781, 721));
            var duplicate = Guest("same");
            Assert.Throws<InvalidOperationException>(() => system.AttachStay(duplicate, 1, 180, 450, 780));
            Assert.That(duplicate.Agent, Is.Null);
            Assert.That(first.Agent, Is.SameAs(original));
            Assert.That(system.Items.Count, Is.EqualTo(1));

            foreach (float wake in new[] { float.NaN, float.NegativeInfinity, 450f, 780f, 900f })
            {
                var invalid = Guest("invalid-" + wake);
                Assert.Throws<ArgumentException>(() => system.AttachStay(invalid, 1, 180, 450, 780, wake));
                Assert.That(invalid.Agent, Is.Null);
                Assert.That(system.Items.Count, Is.EqualTo(1));
            }
            Assert.Throws<ArgumentException>(() => system.AttachStay(Guest("bad-arrival"), 0, 180, 450, 780));
            Assert.Throws<ArgumentException>(() => system.AttachStay(Guest("bad-order"), 1, 460, 450, 780));
            Assert.Throws<ArgumentException>(() => system.AttachStay(Guest("bad-checkout"), 1, 180, 450, float.PositiveInfinity));
        }

        [Test]
        public void OptionalWakeAndLegacyScheduleRemainExplicitlyUnbounded()
        {
            var system = new GuestScheduleSystem(new LivingHotelSettings());
            var dated = Guest("optional");
            system.AttachStay(dated, 1, 180, 450, 780);
            Assert.That(float.IsPositiveInfinity(dated.Agent.Schedule.WakeTime), Is.True);
            var legacy = Guest("legacy");
            system.StartDay(1, new[] { legacy }, 300);
            Assert.That(float.IsPositiveInfinity(legacy.Agent.Schedule.WakeTime), Is.True);
            Assert.That(legacy.Agent.CheckoutTime, Is.LessThan(300));
        }
    }
}
