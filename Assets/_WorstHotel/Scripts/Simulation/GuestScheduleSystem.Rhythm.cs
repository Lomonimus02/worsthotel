using System;

namespace WorstHotel
{
    public sealed partial class GuestScheduleSystem
    {
        const int MorningIndex = 20;

        /// <summary>Pure dated preferences, shared by reservation creation and wire validation.</summary>
        public GuestDailyTiming DatedTimingFor(BookingApplication application, int arrivalDay,
            float arrivalAt, HotelCalendar calendar)
        {
            if (application == null || calendar == null || arrivalDay < 1 || !Number.IsFinite(arrivalAt) || arrivalAt < 0)
                throw new ArgumentException("Dated guest timing needs a valid booking, calendar and arrival.");
            float nominalSleep = calendar.At(arrivalDay, calendar.Settings.SleepHour);
            float checkout = calendar.At(arrivalDay + 1, calendar.Settings.CheckoutHour);
            float hour = calendar.Settings.SecondsPerDay / 24;
            if (!Settings.Rhythm.Enabled)
                return new GuestDailyTiming(nominalSleep,
                    Math.Max(nominalSleep + (checkout - nominalSleep) * .5f, checkout - calendar.Settings.SecondsPerDay / 12), -1);
            if (checkout <= arrivalAt) throw new ArgumentException("A dated stay must end after arrival.");

            var rhythm = Settings.Rhythm;
            bool business = application.Archetype.Kind == GuestKind.Business;
            uint sleepRandom = Seed(Settings.Seed, arrivalDay, application.Id + "|sleep");
            uint wakeRandom = Seed(Settings.Seed, arrivalDay, application.Id + "|wake");
            uint returnRandom = Seed(Settings.Seed, arrivalDay, application.Id + "|outing-return");
            // Preserve ordered dates even with a deliberately short custom operating calendar.
            double gap = Math.Min(hour * .25, (checkout - (double)arrivalAt) * .1);
            float sleep = (float)Clamp(nominalSleep + hour * ((Next(ref sleepRandom) * 2 - 1) * rhythm.SleepJitterHours -
                (business ? rhythm.BusinessSleepAdvanceHours : 0)), arrivalAt + gap, checkout - gap * 2);
            double wakeBase = Math.Max(sleep + (checkout - sleep) * .5f, checkout - calendar.Settings.SecondsPerDay / 12);
            float wake = (float)Clamp(wakeBase + hour * ((Next(ref wakeRandom) * 2 - 1) * rhythm.WakeJitterHours -
                (business ? rhythm.BusinessWakeAdvanceHours : 0)), sleep + gap, checkout - gap);
            float outing = (float)Clamp(calendar.At(arrivalDay, rhythm.OutingReturnHour) +
                hour * (Next(ref returnRandom) * 2 - 1) * rhythm.OutingReturnJitterHours,
                arrivalAt + Math.Min(gap, (sleep - (double)arrivalAt) / 3),
                sleep - Math.Min(gap, (sleep - (double)arrivalAt) / 3));
            var special = application.Special;
            if (special != null && special.SleepHour >= 0)
            {
                sleep = (float)Clamp(calendar.At(arrivalDay + 1, special.SleepHour), arrivalAt + gap, checkout - gap * 2);
                wake = (float)Clamp(calendar.At(arrivalDay + 1, special.WakeHour), sleep + gap, checkout - gap);
                outing = (float)Clamp(calendar.At(arrivalDay + 1, special.ReturnHour), arrivalAt + gap, sleep - gap);
            }
            if (!(arrivalAt < outing && outing < sleep && sleep < wake && wake < checkout))
                throw new ArgumentException("The dated guest timing cannot be represented by this calendar.");
            return new GuestDailyTiming(sleep, wake, outing);
        }

        static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));

        GuestScheduleEntry[] BuildRhythmActivities(GuestStay guest, ref uint random, float secondsPerHour)
        {
            var result = new GuestScheduleEntry[24];
            var profile = guest.Application.Archetype;
            var rhythm = Settings.Rhythm;
            bool business = profile.Kind == GuestKind.Business;
            float outingChance = business ? rhythm.BusinessOutingProbability : profile.Kind == GuestKind.Budget ?
                rhythm.BudgetOutingProbability : rhythm.ColdSensitiveOutingProbability;
            bool outing = Next(ref random) < outingChance;
            bool noisy = (profile.Traits & GuestTraits.Noisy) != 0;
            for (int index = 0; index < result.Length; index++)
            {
                GuestActivity activity;
                if (index == 0) activity = GuestActivity.Unpack;
                else if (index == 1) activity = business ? GuestActivity.Work : GuestActivity.QuietRest;
                else if (index == 2 && outing) activity = GuestActivity.LeaveHotel;
                else if (index == MorningIndex) activity = GuestActivity.Shower;
                else if (index == MorningIndex - 1 || index > MorningIndex)
                    activity = index == MorningIndex + 2 && business ? GuestActivity.Work : GuestActivity.QuietRest;
                else
                {
                    activity = PickActivity(profile.Kind, noisy, ref random);
                    if (activity == GuestActivity.LeaveHotel) activity = GuestActivity.QuietRest;
                    if (index > 0 && activity == result[index - 1].Activity)
                        activity = activity == GuestActivity.QuietRest ? GuestActivity.WatchTV : GuestActivity.QuietRest;
                }
                float min = activity == GuestActivity.LeaveHotel ? Settings.AwayDurationMin :
                    activity == GuestActivity.QuietRest || activity == GuestActivity.Work ? Settings.QuietDurationMin : Settings.ActivityDurationMin;
                float max = activity == GuestActivity.LeaveHotel ? Settings.AwayDurationMax :
                    activity == GuestActivity.QuietRest || activity == GuestActivity.Work ? Settings.QuietDurationMax : Settings.ActivityDurationMax;
                float duration = min + Next(ref random) * (max - min);
                if (activity == GuestActivity.Shower && (profile.Traits & GuestTraits.ColdSensitive) != 0)
                    duration *= Settings.ColdShowerDurationMultiplier;
                result[index] = new GuestScheduleEntry(activity, duration);
            }
            if (guest.Application.Special != null)
                foreach (var slot in guest.Application.Special.Activities)
                    result[slot.Index] = new GuestScheduleEntry(slot.Activity, slot.Hours * secondsPerHour);
            return result;
        }
    }
}
