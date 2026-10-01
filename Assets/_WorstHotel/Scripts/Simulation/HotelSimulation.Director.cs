using System;
using System.Collections.Generic;
using System.Linq;

namespace WorstHotel
{
    public sealed partial class HotelSimulation
    {
        public HotelDirector Director { get; private set; }
        internal int DirectorRoomCount => rooms.Values.Count(r => r.Operational);
        internal HotelPressure MeasureDirectorPressure(HotelDirectorSettings s)
        {
            float waiting = guests.Count(g => !g.ReceiptPosted && (g.LockedOut || g.Agent.State == GuestAgentState.WaitingForCheckIn)) * s.WaitingWeight;
            // One episode may have both an incident and a service response; count its guest once.
            var concerns = new HashSet<string>(Incidents.Items.Where(i => i.Active && i.HasContactedStaff && i.Stage >= SituationStage.Complaint).Select(i => i.GuestId));
            if (Services != null) foreach (var c in Services.Cases.Where(c => c.Active && (c.IsKnownToHotel || c.Response?.IsRinging == true))) concerns.Add(c.GuestId);
            float service = concerns.Count * s.ServiceWeight;
            float work = rooms.Values.Count(r => r.Operational && !r.Occupied && r.Cleanliness != Cleanliness.Clean) * s.DirtyRoomWeight;
            work += guests.Count(g => g.Agent.State == GuestAgentState.Scheduled && g.Agent.ArrivalTime - Elapsed < 40 &&
                rooms[g.RoomId].Cleanliness != Cleanliness.Clean) * s.UpcomingUnreadyWeight;
            float bags = (Services?.Items.Where(i => i.Kind == ServiceItemKind.Luggage && i.StaffHandling && i.Location != ServiceItemLocation.Delivered &&
                i.Location != ServiceItemLocation.LostProperty).Select(i => i.GuestId).Distinct().Count() ?? 0) * s.LuggageWeight;
            float equipment = Boiler.Failed ? s.FailureWeight : Boiler.MaintenanceInProgress || Boiler.Stress01 >= .4f ? s.InfrastructureWeight : 0;
            if (Electrical != null) equipment += Electrical.Circuits.Sum(c => c.Tripped ? s.FailureWeight : c.Warning || c.Stress01 >= .35f ? s.InfrastructureWeight : 0);
            equipment += rooms.Values.Count(r => r.Occupied && r.LampBroken) * s.ServiceWeight;
            float authored = (Director?.History.Count(h => h.Active) ?? 0) * s.SituationWeight;
            return new HotelPressure(waiting + service + work + bags + equipment + authored, s,
                "waiting=" + waiting + ", service=" + service + ", turnover=" + work + ", bags=" + bags + ", equipment=" + equipment);
        }

        internal IEnumerable<string> DirectorParticipants(HotelSituationDefinition d)
        {
            if (d.Kind == HotelSituationKind.SpecialArrival)
            {
                if (Operations.SpecialBookings.Enabled && !specialEnquiries.Any(e => e.Status == SpecialOfferStatus.Pending) &&
                    rooms.Values.Any(r => r.Operational && !r.Occupied && !r.Reserved) && guests.Any(g => g.Agent.CheckedIn && !g.ReceiptPosted))
                    yield return "offer-day-" + CalendarDay;
                yield break;
            }
            foreach (var g in guests.OrderBy(g => g.GuestId, StringComparer.Ordinal))
                if (DirectorEligible(d.Kind, g)) yield return g.GuestId;
        }
        bool DirectorAvailable(GuestStay g) => g?.Agent != null && !g.ReceiptPosted && g.Agent.CheckedIn && g.Agent.InAssignedRoom &&
            g.Agent.ActivityStaged && g.Agent.State != GuestAgentState.Sleeping && !g.Agent.IsRelocating && !g.LockedOut &&
            g.Agent.ResponseActionId == null && Services?.DirectIntent(g.GuestId) == null &&
            g.Agent.Activity != GuestActivity.Shower && g.Agent.Activity != GuestActivity.Pack &&
            g.Agent.Activity != GuestActivity.Rehearsal && g.Agent.QuietUntil <= Elapsed &&
            g.Agent.CheckoutTime - Elapsed > 45 && (g.Agent.SleepStarted || g.Agent.Schedule.SleepTime - Elapsed > 40);
        bool DirectorEligible(HotelSituationKind kind, GuestStay g)
        {
            var a = g?.Agent; if (a == null || g.ReceiptPosted) return false;
            if (kind == HotelSituationKind.ForgottenKey) return a.State == GuestAgentState.LeavingRoom && a.HasReachedRoom &&
                !g.LockedOut && Keys.Find(g.RoomId)?.Location == RoomKeyLocation.HeldByGuest && a.CheckoutTime - Elapsed > 120;
            if (kind == HotelSituationKind.LuggageHelp) return Services?.CanEncourageService(g, ServiceKind.LuggageStorage) == true;
            if (kind == HotelSituationKind.ExtraBlanket) return Services?.CanEncourageService(g, ServiceKind.ExtraBlanket) == true;
            if (kind == HotelSituationKind.SchedulePromise) return Services?.CanEncourageService(g, ServiceKind.WakeUpCall) == true ||
                Services?.CanEncourageService(g, ServiceKind.LateCheckout) == true;
            if (!DirectorAvailable(g)) return false;
            var room = rooms[g.RoomId];
            switch (kind)
            {
                case HotelSituationKind.Rehearsal:
                case HotelSituationKind.SocialEvening:
                    return g.Application.SpecialKind == SpecialGuestKind.TouringMusician && room.HasPower &&
                        Services.Items.Any(i => i.GuestId == g.GuestId && i.Payload == LuggagePayload.Amplifier && i.RoomId == g.RoomId &&
                            i.Location == ServiceItemLocation.Delivered && !i.EquipmentSwitchedOff) &&
                        Services.Items.Any(i => i.GuestId == g.GuestId && i.Payload == LuggagePayload.InstrumentCase && i.RoomId == g.RoomId && i.Location == ServiceItemLocation.Delivered) &&
                        (kind != HotelSituationKind.SocialEvening || guests.Count(DirectorAvailable) >= 4);
                case HotelSituationKind.NoisyEvening:
                    return (g.Application.Archetype.Traits & GuestTraits.Noisy) != 0 && room.HasPower && a.Activity != GuestActivity.LoudRoom;
                case HotelSituationKind.Visitor:
                    return g.Application.SpecialKind == SpecialGuestKind.None && !Director.Visitors.Any(v => v.State != HotelVisitorState.Left);
                case HotelSituationKind.WornLamp:
                    return room.HasPower && !room.LampBroken && room.LampCondition <= 25 && room.UsedHours >= 2 &&
                        (a.Activity == GuestActivity.Work || a.Activity == GuestActivity.WatchTV);
                default: return false;
            }
        }
        internal float DirectorRelevance(HotelSituationDefinition d, string id)
        {
            var g = guests.FirstOrDefault(g => g.GuestId == id);
            if (d.Kind == HotelSituationKind.SpecialArrival) return guests.Any(g => g.Agent.State == GuestAgentState.Arriving) ? 1.5f : 1;
            if (d.Kind == HotelSituationKind.Rehearsal || d.Kind == HotelSituationKind.NoisyEvening)
                return rooms.Values.Any(r => r.Occupied && r.Profile.Id != g.RoomId && Math.Abs(r.Profile.Id - g.RoomId) <= 2) ? 1.5f : 1;
            return 1;
        }

        internal bool TryBeginDirectorPremise(HotelSituationOpportunity opportunity, out HotelSituationRecord record)
        {
            record = null;
            if (IsReadOnlyMirror || !Running || OwnershipLost || !ContinuousOperations) return false;
            var d = opportunity.Definition;
            if (!DirectorParticipants(d).Contains(opportunity.GuestId)) return false;
            var g = guests.FirstOrDefault(g => g.GuestId == opportunity.GuestId);
            string related = null;
            switch (d.Kind)
            {
                case HotelSituationKind.ForgottenKey:
                    Keys.LeaveInside(g.GuestId, g.RoomId); g.KeyLossConsidered = true; break;
                case HotelSituationKind.LuggageHelp:
                case HotelSituationKind.ExtraBlanket:
                case HotelSituationKind.SchedulePromise:
                    var kind = d.Kind == HotelSituationKind.LuggageHelp ? ServiceKind.LuggageStorage : d.Kind == HotelSituationKind.ExtraBlanket ? ServiceKind.ExtraBlanket :
                        Services.CanEncourageService(g, ServiceKind.WakeUpCall) ? ServiceKind.WakeUpCall : ServiceKind.LateCheckout;
                    related = Services.EncourageService(g, kind);
                    if (related == null) return false; break;
                case HotelSituationKind.Rehearsal:
                case HotelSituationKind.SocialEvening:
                case HotelSituationKind.NoisyEvening:
                    // SetActivity retains real anchor travel, power and sound measurement.
                    SetActivity(g, d.Kind == HotelSituationKind.NoisyEvening ? GuestActivity.LoudRoom : GuestActivity.Rehearsal, Elapsed, d.DurationSeconds);
                    break;
                case HotelSituationKind.Visitor:
                    related = Director.InviteVisitor(g).Id; break;
                case HotelSituationKind.WornLamp:
                    // A worn powered lamp actually burns out; repair uses the existing physical bulb.
                    rooms[g.RoomId].LampCondition = 0; rooms[g.RoomId].LampBroken = true; break;
                case HotelSituationKind.SpecialArrival:
                    var specialKind = guests.Any(g => g.Agent.State == GuestAgentState.Arriving || g.Agent.State == GuestAgentState.WaitingForCheckIn) ?
                        SpecialGuestKind.Overpacker : Calendar.Hour > 17 ? SpecialGuestKind.NightOwl : SpecialGuestKind.TouringMusician;
                    var special = SpecialGuestDefinition.For(specialKind);
                    var app = new BookingApplication("director-special-" + CalendarDay, special.Name,
                        settings.GuestArchetypes.First(p => p.Kind == special.BaseKind), special.Payment, specialKind);
                    if (specialEnquiries.Any(e => e.Offer.Id == app.Id)) return false;
                    float arrival = Math.Min(Calendar.At(CalendarDay, 21), Elapsed + Math.Max(35, Operations.SecondsPerDay / 12));
                    var timing = BookingTiming(app, CalendarDay, arrival);
                    specialEnquiries.Add(new SpecialBookingEnquiry(new ScheduledBookingOffer(app, CalendarDay, arrival, timing.SleepAt,
                        timing.WakeAt, Calendar.At(CalendarDay + 1, Operations.CheckoutHour))));
                    related = app.Id; break;
                default: return false;
            }
            RefreshGuestLoad(); RefreshElectrical();
            record = new HotelSituationRecord { GuestId = opportunity.GuestId, RoomId = g?.RoomId ?? 0, RelatedId = related };
            return true;
        }

        internal bool DirectorSituationRelevant(HotelSituationRecord record)
        {
            if (record.Kind == HotelSituationKind.SpecialArrival) return specialEnquiries.Any(e => e.Offer.Id == record.RelatedId && e.Status == SpecialOfferStatus.Pending);
            if (record.Kind == HotelSituationKind.Visitor) return Director.Visitors.Any(v => v.Id == record.RelatedId && v.State != HotelVisitorState.Left);
            var g = guests.FirstOrDefault(g => g.GuestId == record.GuestId && !g.ReceiptPosted);
            if (g == null || g.Agent.State == GuestAgentState.Leaving || g.Agent.State == GuestAgentState.Left || g.Agent.State == GuestAgentState.CheckingOut) return false;
            switch (record.Kind)
            {
                case HotelSituationKind.ForgottenKey: return Keys.Find(g.RoomId)?.Location == RoomKeyLocation.LeftInside || g.LockedOut;
                case HotelSituationKind.LuggageHelp:
                case HotelSituationKind.ExtraBlanket:
                case HotelSituationKind.SchedulePromise: return Services.FindCase(record.RelatedId)?.Active == true;
                case HotelSituationKind.WornLamp: return rooms[record.RoomId].LampBroken;
                case HotelSituationKind.NoisyEvening: return g.Agent.Activity == GuestActivity.LoudRoom && g.Agent.IsRoomState;
                default: return g.Agent.Activity == GuestActivity.Rehearsal && g.Agent.IsRoomState;
            }
        }
    }
}
