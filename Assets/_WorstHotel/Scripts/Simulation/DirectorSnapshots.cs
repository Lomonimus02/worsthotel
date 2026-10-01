using System;
using System.Linq;

namespace WorstHotel
{
    [Serializable] public sealed class DirectorSnapshot
    {
        public int BudgetDay, SelectionSequence;
        public float QuietElapsed, SpentToday, NextConsiderAt, CooldownUntil;
        public HotelSituationRecord[] History;
        public HotelVisitor[] Visitors;
    }
    public sealed partial class HotelDirector
    {
        internal DirectorSnapshot Capture() => new DirectorSnapshot { BudgetDay = BudgetDay, SelectionSequence = SelectionSequence,
            QuietElapsed = QuietElapsed, SpentToday = SpentToday, NextConsiderAt = NextConsiderAt, CooldownUntil = CooldownUntil,
            History = history.Select(h => h.Copy()).ToArray(), Visitors = visitors.Select(v => v.Copy()).ToArray() };
        internal void Validate(DirectorSnapshot data, HotelModelSnapshot model)
        {
            if (data == null || data.BudgetDay < 1 || data.BudgetDay > Math.Max(1, model.Day) || data.SelectionSequence < 0 ||
                new[] { data.QuietElapsed, data.SpentToday, data.NextConsiderAt, data.CooldownUntil }.Any(v => !Number.IsFinite(v) || v < 0) ||
                data.SpentToday > Settings.MaximumBudget || data.History == null || data.History.Length > 64 ||
                data.Visitors == null || data.Visitors.Length > 4)
                throw new ArgumentException("Invalid director snapshot.");
            foreach (var r in data.History)
                if (r == null || string.IsNullOrEmpty(r.Id) || r.Id.Length > 256 || string.IsNullOrEmpty(r.GuestId) || r.GuestId.Length > 256 ||
                    !Settings.Deck.Any(d => d.Id == r.DefinitionId && d.Kind == r.Kind && d.Cost == r.Cost) ||
                    r.Day < 1 || r.Day > model.Day || !Number.IsFinite(r.StartedAt) || r.StartedAt < 0 || r.StartedAt > model.Time ||
                    !Number.IsFinite(r.EndedAt) || r.EndedAt != -1 && (r.EndedAt < r.StartedAt || r.EndedAt > model.Time) ||
                    r.Kind != HotelSituationKind.SpecialArrival && !model.Rooms.Any(room => room.Id == r.RoomId))
                    throw new ArgumentException("Invalid director history.");
            if (data.History.Select(r => r.Id).Distinct().Count() != data.History.Length || data.Visitors.Any(v => v == null) ||
                data.Visitors.Select(v => v.Id).Distinct().Count() != data.Visitors.Length)
                throw new ArgumentException("Duplicate director identity.");
            foreach (var v in data.Visitors)
                if (v.Id != "visitor/" + v.HostGuestId || !Enum.IsDefined(typeof(HotelVisitorState), v.State) ||
                    !model.Rooms.Any(r => r.Id == v.RoomId) || string.IsNullOrEmpty(v.HostGuestId) || v.HostGuestId.Length > 256 ||
                    new[] { v.CreatedAt, v.StateSince, v.LeaveAt }.Any(t => !Number.IsFinite(t) || t < 0) ||
                    v.StateSince > model.Time || v.CreatedAt > v.StateSince || v.LeaveAt < v.CreatedAt ||
                    v.State != HotelVisitorState.Leaving && v.State != HotelVisitorState.Left && !model.Guests.Any(g => g.Application.Id == v.HostGuestId))
                    throw new ArgumentException("Invalid physical visitor.");
        }
        internal void Restore(DirectorSnapshot data)
        {
            BudgetDay = data.BudgetDay; SelectionSequence = data.SelectionSequence; QuietElapsed = data.QuietElapsed;
            SpentToday = data.SpentToday; NextConsiderAt = data.NextConsiderAt; CooldownUntil = data.CooldownUntil;
            history.Clear(); history.AddRange(data.History.Select(h => h.Copy()));
            visitors.Clear(); visitors.AddRange(data.Visitors.Select(v => v.Copy()));
            Pressure = hotel.MeasureDirectorPressure(Settings);
        }
    }
}
