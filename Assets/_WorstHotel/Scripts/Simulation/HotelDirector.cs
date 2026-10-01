using System;
using System.Collections.Generic;
using System.Linq;

namespace WorstHotel
{
    [Serializable] public sealed class HotelSituationRecord
    {
        public string Id, DefinitionId, GuestId, RelatedId;
        public HotelSituationKind Kind;
        public int Day, RoomId;
        public float StartedAt, EndedAt = -1, Cost;
        public bool Active => EndedAt < 0;
        public HotelSituationRecord Copy() => (HotelSituationRecord)MemberwiseClone();
    }
    public readonly struct HotelSituationOpportunity
    {
        public HotelSituationDefinition Definition { get; }
        public string GuestId { get; }
        public float Weight { get; }
        public HotelSituationOpportunity(HotelSituationDefinition definition, string guestId, float weight)
        { Definition = definition; GuestId = guestId; Weight = weight; }
    }

    /// <summary>Host-only choice of premises. No satisfaction, failure, receipt or complaint outcomes live here.</summary>
    public sealed partial class HotelDirector
    {
        readonly HotelSimulation hotel;
        readonly List<HotelSituationRecord> history = new List<HotelSituationRecord>();
        public HotelDirectorSettings Settings { get; }
        public IReadOnlyList<HotelSituationRecord> History => history.AsReadOnly();
        public HotelPressure Pressure { get; private set; }
        public float QuietElapsed { get; private set; }
        public float SpentToday { get; private set; }
        public float NextConsiderAt { get; private set; }
        public float CooldownUntil { get; private set; }
        public int BudgetDay { get; private set; } = 1;
        public int SelectionSequence { get; private set; }
        public float Budget => Math.Min(Settings.MaximumBudget, Settings.FirstDayBudget +
            Math.Max(0, hotel.CalendarDay - 1) * Settings.DailyBudgetGrowth + Math.Max(0, hotel.DirectorRoomCount - 6) * Settings.ExtraRoomBudget);
        internal HotelDirector(HotelSimulation hotel, HotelDirectorSettings settings)
        { this.hotel = hotel; Settings = settings.Copy(); Pressure = new HotelPressure(0, Settings, "Opening"); }

        internal void Tick(float dt)
        {
            if (hotel.IsReadOnlyMirror || !hotel.Running) return;
            TickVisitors();
            foreach (var current in history.Where(r => r.Active))
                if (!hotel.DirectorSituationRelevant(current)) { current.EndedAt = hotel.Elapsed; CooldownUntil = Math.Max(CooldownUntil, hotel.Elapsed + Settings.RecoverySeconds * .5f); }
            history.RemoveAll(r => !r.Active && r.Day < hotel.CalendarDay - 3 && !hotel.Guests.Any(g => g.GuestId == r.GuestId && !g.ReceiptPosted));
            if (BudgetDay != hotel.CalendarDay) { BudgetDay = hotel.CalendarDay; SpentToday = 0; QuietElapsed = 0; }
            Pressure = hotel.MeasureDirectorPressure(Settings);
            float hour = hotel.Calendar.Hour;
            // Sleeping/accelerated time never buys quiet dwell or opportunity rolls.
            if (!Settings.Enabled || hour < Settings.StartHour || hour >= Settings.EndHour || hotel.Clock.Speed > 1 ||
                Pressure.Band >= HotelPressureBand.Busy || !hotel.Guests.Any(g => g.Agent.CheckedIn && !g.ReceiptPosted))
            { QuietElapsed = 0; return; }
            // A single arrival may briefly overlap an opportunity considered after a real
            // quiet spell. Active time itself never earns more quiet credit; it decays it.
            QuietElapsed = Pressure.Band == HotelPressureBand.Quiet ? Math.Min(Settings.QuietSeconds * 2, QuietElapsed + dt) : Math.Max(0, QuietElapsed - dt * .25f);
            if (QuietElapsed < Settings.QuietSeconds || hotel.Elapsed < CooldownUntil || hotel.Elapsed < NextConsiderAt || history.Count >= 64) return;
            NextConsiderAt = hotel.Elapsed + Settings.ConsiderEverySeconds;
            var eligible = EligibleOpportunities();
            if (eligible.Count == 0) return;
            uint random = unchecked((uint)(Settings.Seed + ++SelectionSequence * 747796405));
            random ^= random << 13; random ^= random >> 17; random ^= random << 5;
            float draw = (random / (float)uint.MaxValue) * eligible.Sum(c => c.Weight);
            var choice = eligible[eligible.Count - 1];
            foreach (var candidate in eligible) { draw -= candidate.Weight; if (draw <= 0) { choice = candidate; break; } }
            // Eligibility is checked again by the real action before spending budget.
            if (!hotel.TryBeginDirectorPremise(choice, out var record)) return;
            record.Id = "director/" + BudgetDay + "/" + SelectionSequence;
            record.DefinitionId = choice.Definition.Id; record.Kind = choice.Definition.Kind; record.Day = BudgetDay;
            record.StartedAt = hotel.Elapsed; record.Cost = choice.Definition.Cost;
            history.Add(record); SpentToday += record.Cost; QuietElapsed = 0;
            CooldownUntil = hotel.Elapsed + Settings.RecoverySeconds + 12 * (int)choice.Definition.Size;
        }

        public IReadOnlyList<HotelSituationOpportunity> EligibleOpportunities()
        {
            var result = new List<HotelSituationOpportunity>();
            if (!Settings.Enabled || hotel.IsReadOnlyMirror || !hotel.Running) return result;
            float hour = hotel.Calendar.Hour;
            foreach (var d in Settings.Deck)
            {
                // Preserve Day 1 room for a larger eligible premise instead of spending
                // the entire opening budget on several encouraged phone requests.
                if (hotel.CalendarDay == 1 && d.Size == HotelSituationSize.Minor &&
                    history.Count(h => h.Day == 1 && Settings.Deck.Any(card => card.Id == h.DefinitionId && card.Size == HotelSituationSize.Minor)) >= Settings.FirstDayMinorLimit) continue;
                if (d.MinimumDay > hotel.CalendarDay || hour < d.StartHour || hour >= d.EndHour || d.Cost + SpentToday > Budget ||
                    Pressure.Band == HotelPressureBand.Active && d.Size != HotelSituationSize.Minor && d.Kind != HotelSituationKind.SpecialArrival ||
                    history.Any(h => h.DefinitionId == d.Id && (h.Active || hotel.Elapsed - h.StartedAt < d.CooldownSeconds))) continue;
                foreach (var guestId in hotel.DirectorParticipants(d))
                {
                    if (!d.RepeatForSameStay && history.Any(h => h.DefinitionId == d.Id && h.GuestId == guestId)) continue;
                    float recent = history.Skip(Math.Max(0, history.Count - 2)).Any(h => h.Kind == d.Kind) ? .25f : 1;
                    float relevance = hotel.DirectorRelevance(d, guestId);
                    result.Add(new HotelSituationOpportunity(d, guestId, d.Weight * recent * relevance));
                }
            }
            return result;
        }
    }
}
