using System;
using System.Collections.Generic;

namespace WorstHotel
{
    public enum SpecialGuestKind { None, TouringMusician, Overpacker, NightOwl }
    public enum LuggagePayload { Suitcase, InstrumentCase, Amplifier }
    public enum SpecialOfferStatus { Pending, Accepted, Declined, Expired }

    // Small immutable compositions; the ordinary stay, item and utility systems do the work.
    public sealed class SpecialGuestDefinition
    {
        public SpecialGuestKind Kind { get; }
        public string Name { get; }
        public string Label { get; }
        public string Note { get; }
        public int Payment { get; }
        public GuestKind BaseKind { get; }
        public IReadOnlyList<LuggagePayload> Baggage { get; }
        public float AmplifierLoad { get; }
        public float AmplifierNoise { get; }
        public float SleepHour { get; }
        public float WakeHour { get; }
        public float ReturnHour { get; }
        public IReadOnlyList<SpecialActivitySlot> Activities { get; }

        SpecialGuestDefinition(SpecialGuestKind kind, string name, string label, string note, int payment,
            GuestKind baseKind, LuggagePayload[] baggage, SpecialActivitySlot[] activities,
            float amplifierLoad = 0, float amplifierNoise = 0, float sleepHour = -1, float wakeHour = -1, float returnHour = -1)
        {
            Kind = kind; Name = name; Label = label; Note = note; Payment = payment; BaseKind = baseKind;
            Baggage = Array.AsReadOnly(baggage); Activities = Array.AsReadOnly(activities);
            AmplifierLoad = amplifierLoad; AmplifierNoise = amplifierNoise;
            SleepHour = sleepHour; WakeHour = wakeHour; ReturnHour = returnHour;
        }

        static readonly SpecialGuestDefinition[] catalog = {
            null,
            new SpecialGuestDefinition(SpecialGuestKind.TouringMusician, "Elliot Barnes", "Touring musician",
                "Travelling with a suitcase, a large instrument case and an amplifier. May practise during the evening. Help with the equipment would be appreciated.",
                420, GuestKind.Budget, new[] { LuggagePayload.Suitcase, LuggagePayload.InstrumentCase, LuggagePayload.Amplifier },
                new[] { new SpecialActivitySlot(1, GuestActivity.Rehearsal, .85f), new SpecialActivitySlot(2, GuestActivity.LeaveHotel, 1),
                    new SpecialActivitySlot(3, GuestActivity.QuietRest, .4f), new SpecialActivitySlot(4, GuestActivity.Rehearsal, .85f) }, 1.4f, .88f),
            new SpecialGuestDefinition(SpecialGuestKind.Overpacker, "Florence Bell", "Overpacker",
                "Six bags for one night. I like to be prepared! A little help getting everything to the room would be lovely.",
                480, GuestKind.ColdSensitive, new[] { LuggagePayload.Suitcase, LuggagePayload.Suitcase, LuggagePayload.Suitcase,
                    LuggagePayload.Suitcase, LuggagePayload.Suitcase, LuggagePayload.Suitcase }, Array.Empty<SpecialActivitySlot>()),
            new SpecialGuestDefinition(SpecialGuestKind.NightOwl, "Robin Vale", "Night owl",
                "Working a late engagement in town. I'll be back after midnight and may watch a little television or shower before bed. Please let me sleep in.",
                360, GuestKind.Business, new[] { LuggagePayload.Suitcase },
                new[] { new SpecialActivitySlot(1, GuestActivity.QuietRest, .5f), new SpecialActivitySlot(2, GuestActivity.LeaveHotel, 1),
                    new SpecialActivitySlot(3, GuestActivity.WatchTV, .65f), new SpecialActivitySlot(4, GuestActivity.Shower, .5f) },
                sleepHour: 4.25f, wakeHour: 9.25f, returnHour: 1.75f)
        };
        public static SpecialGuestDefinition For(SpecialGuestKind kind) => (int)kind > 0 && (int)kind < catalog.Length ? catalog[(int)kind] : null;
    }

    public readonly struct SpecialActivitySlot
    {
        public int Index { get; }
        public GuestActivity Activity { get; }
        public float Hours { get; }
        public SpecialActivitySlot(int index, GuestActivity activity, float hours) { Index = index; Activity = activity; Hours = hours; }
    }

    [Serializable] public sealed class SpecialBookingSettings
    {
        public bool Enabled;
        public int FirstOfferDay = 2, IntervalDays = 2;
        public float OfferHour = 10;
        public SpecialBookingSettings(bool enabled = false, int firstOfferDay = 2, int intervalDays = 2, float offerHour = 10)
        {
            Enabled = enabled; FirstOfferDay = firstOfferDay; IntervalDays = intervalDays; OfferHour = offerHour;
            Validate();
        }
        public void Validate()
        {
            if (FirstOfferDay < 2 || IntervalDays < 1 || !Number.IsFinite(OfferHour) || OfferHour < 0 || OfferHour >= 24)
                throw new ArgumentException("Invalid special reservation frequency.");
        }
        public SpecialBookingSettings Copy() => new SpecialBookingSettings(Enabled, FirstOfferDay, IntervalDays, OfferHour);
    }

    public sealed class SpecialBookingEnquiry
    {
        public ScheduledBookingOffer Offer { get; }
        public SpecialGuestDefinition Definition => Offer.Application.Special;
        public SpecialOfferStatus Status { get; internal set; }
        public int Revision { get; internal set; } = 1;
        public SpecialBookingEnquiry(ScheduledBookingOffer offer) { Offer = offer; }
    }
}
