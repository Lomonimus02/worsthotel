using System;
using System.Linq;

namespace WorstHotel
{
    public enum HotelPressureBand { Quiet, Active, Busy, Overloaded }
    public enum HotelSituationSize { Minor, Medium, Headline }
    public enum HotelSituationKind { ForgottenKey, LuggageHelp, ExtraBlanket, SchedulePromise, Rehearsal, NoisyEvening, Visitor, WornLamp, SocialEvening, SpecialArrival }

    [Serializable] public sealed class HotelSituationDefinition
    {
        public string Id;
        public HotelSituationKind Kind;
        public HotelSituationSize Size;
        public float Weight = 1, Cost = 1, StartHour = 9, EndHour = 23, CooldownSeconds = 100, DurationSeconds = 24;
        public int MinimumDay = 1;
        public bool RepeatForSameStay;
        public HotelSituationDefinition() { }
        public HotelSituationDefinition(HotelSituationKind kind, HotelSituationSize size, float start, float end, float duration, int day = 1)
        { Id = kind.ToString(); Kind = kind; Size = size; Cost = (int)size + 1; StartHour = start; EndHour = end; DurationSeconds = duration; MinimumDay = day; }
        public HotelSituationDefinition Copy() => (HotelSituationDefinition)MemberwiseClone();
    }

    [Serializable] public sealed class HotelDirectorSettings
    {
        // Opt in at the production Session asset. Old diagnostic/shift fixtures remain explicit.
        public bool Enabled;
        public int Seed = 709;
        public int FirstDayMinorLimit = 1;
        public float QuietSeconds = 28, ConsiderEverySeconds = 4, RecoverySeconds = 40;
        public float StartHour = 7, EndHour = 23, QuietThreshold = 1.5f, BusyThreshold = 3, OverloadedThreshold = 6;
        public float WaitingWeight = 1.5f, ServiceWeight = 1, DirtyRoomWeight = .25f, UpcomingUnreadyWeight = .8f,
            LuggageWeight = .65f, InfrastructureWeight = 1.5f, FailureWeight = 6, SituationWeight = .3f;
        public float FirstDayBudget = 3, DailyBudgetGrowth = .5f, ExtraRoomBudget = .4f, MaximumBudget = 6;
        public float VisitorWaitSeconds = 18, VisitorStaySeconds = 75, VisitorPower = .45f, VisitorHeat = .25f, VisitorNoise = .32f;
        public HotelSituationDefinition[] Deck = DefaultDeck();
        public static HotelSituationDefinition[] DefaultDeck() => new[] {
            new HotelSituationDefinition(HotelSituationKind.ForgottenKey, HotelSituationSize.Minor, 9, 21, 20),
            new HotelSituationDefinition(HotelSituationKind.LuggageHelp, HotelSituationSize.Minor, 9, 21, 20),
            new HotelSituationDefinition(HotelSituationKind.ExtraBlanket, HotelSituationSize.Minor, 9, 23, 20),
            new HotelSituationDefinition(HotelSituationKind.SchedulePromise, HotelSituationSize.Minor, 8, 23, 20),
            new HotelSituationDefinition(HotelSituationKind.Rehearsal, HotelSituationSize.Medium, 15, 22.5f, 28),
            new HotelSituationDefinition(HotelSituationKind.NoisyEvening, HotelSituationSize.Medium, 16, 22.5f, 26),
            new HotelSituationDefinition(HotelSituationKind.Visitor, HotelSituationSize.Medium, 14, 22, 75),
            new HotelSituationDefinition(HotelSituationKind.WornLamp, HotelSituationSize.Medium, 16, 22.5f, 20, 2),
            new HotelSituationDefinition(HotelSituationKind.SocialEvening, HotelSituationSize.Headline, 17, 22, 38, 3),
            new HotelSituationDefinition(HotelSituationKind.SpecialArrival, HotelSituationSize.Headline, 11, 19, 60, 2)
        };
        public HotelDirectorSettings Copy()
        { var copy = (HotelDirectorSettings)MemberwiseClone(); copy.Deck = Deck?.Select(d => d?.Copy()).ToArray(); copy.Validate(); return copy; }
        public void Validate()
        {
            var values = new[] { QuietSeconds, ConsiderEverySeconds, RecoverySeconds, StartHour, EndHour, QuietThreshold, BusyThreshold,
                OverloadedThreshold, WaitingWeight, ServiceWeight, DirtyRoomWeight, UpcomingUnreadyWeight, LuggageWeight,
                InfrastructureWeight, FailureWeight, SituationWeight, FirstDayBudget, DailyBudgetGrowth, ExtraRoomBudget, MaximumBudget,
                VisitorWaitSeconds, VisitorStaySeconds, VisitorPower, VisitorHeat, VisitorNoise };
            if (values.Any(v => !Number.IsFinite(v) || v < 0) || QuietSeconds < 1 || ConsiderEverySeconds < .2f ||
                StartHour >= EndHour || EndHour > 24 || QuietThreshold <= 0 || BusyThreshold <= QuietThreshold ||
                OverloadedThreshold <= BusyThreshold || MaximumBudget < FirstDayBudget || VisitorNoise > 1 ||
                FirstDayMinorLimit < 0 || FirstDayMinorLimit > 16 ||
                Deck == null || Deck.Length > 16 || Deck.Any(d => d == null || string.IsNullOrWhiteSpace(d.Id)) ||
                Deck.Select(d => d.Id).Distinct().Count() != Deck.Length)
                throw new ArgumentException("Invalid hotel director configuration.");
            foreach (var d in Deck)
                if (!Enum.IsDefined(typeof(HotelSituationKind), d.Kind) || !Enum.IsDefined(typeof(HotelSituationSize), d.Size) ||
                    new[] { d.Weight, d.Cost, d.StartHour, d.EndHour, d.CooldownSeconds, d.DurationSeconds }.Any(v => !Number.IsFinite(v) || v < 0) ||
                    d.Cost <= 0 || d.Weight <= 0 || d.StartHour >= d.EndHour || d.EndHour > 24 || d.DurationSeconds < 1 || d.MinimumDay < 1)
                    throw new ArgumentException("Invalid situation definition.");
        }
    }

    public readonly struct HotelPressure
    {
        public float Score { get; }
        public HotelPressureBand Band { get; }
        public string Reason { get; }
        public HotelPressure(float score, HotelDirectorSettings settings, string reason)
        { Score = score; Reason = reason; Band = score >= settings.OverloadedThreshold ? HotelPressureBand.Overloaded :
            score >= settings.BusyThreshold ? HotelPressureBand.Busy : score >= settings.QuietThreshold ? HotelPressureBand.Active : HotelPressureBand.Quiet; }
    }
}
