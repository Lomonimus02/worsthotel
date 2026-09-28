using System;
using System.Collections.Generic;
using System.Linq;

namespace WorstHotel
{
    public sealed partial class HotelSimulation
    {
        readonly List<SpecialBookingEnquiry> specialEnquiries = new List<SpecialBookingEnquiry>();
        int lastSpecialOfferDay;
        public IReadOnlyList<SpecialBookingEnquiry> SpecialEnquiries => specialEnquiries.AsReadOnly();

        void RefreshSpecialBookings(float now)
        {
            foreach (var enquiry in specialEnquiries.Where(e => e.Status == SpecialOfferStatus.Pending && e.Offer.ArrivalAt <= now))
            { enquiry.Status = SpecialOfferStatus.Expired; enquiry.Revision++; }
            var tuning = Operations.SpecialBookings;
            int day = Calendar.DayAt(now);
            specialEnquiries.RemoveAll(e => e.Status != SpecialOfferStatus.Pending && e.Offer.ArrivalDay < day - 2);
            if (!tuning.Enabled || day < tuning.FirstOfferDay || (day - tuning.FirstOfferDay) % tuning.IntervalDays != 0 ||
                day <= lastSpecialOfferDay || now < Calendar.At(day, tuning.OfferHour) ||
                specialEnquiries.Any(e => e.Status == SpecialOfferStatus.Pending)) return;
            lastSpecialOfferDay = day;
            var kind = (SpecialGuestKind)(1 + ((day - tuning.FirstOfferDay) / tuning.IntervalDays) % 3);
            var definition = SpecialGuestDefinition.For(kind);
            var profile = settings.GuestArchetypes.First(p => p.Kind == definition.BaseKind);
            var application = new BookingApplication("special-" + day, definition.Name, profile, definition.Payment, kind);
            float arrival = Calendar.At(day + 1, (Operations.ArrivalStartHour + Operations.ArrivalEndHour) * .5f);
            var timing = BookingTiming(application, day + 1, arrival);
            var offer = new ScheduledBookingOffer(application, day + 1, arrival, timing.SleepAt, timing.WakeAt,
                Calendar.At(day + 2, Operations.CheckoutHour));
            specialEnquiries.Add(new SpecialBookingEnquiry(offer));
            // No HUD alert: this is correspondence in the reservation ledger.
        }

        public CommandResult DecideSpecialBooking(int actorId, string id, int roomId, bool accept, int revision)
        {
            var gate = BookingCommandGate(actorId); if (!gate.Success) return gate;
            var enquiry = specialEnquiries.FirstOrDefault(e => e.Offer.Id == id);
            if (enquiry == null || enquiry.Status != SpecialOfferStatus.Pending || enquiry.Revision != revision || enquiry.Offer.ArrivalAt <= Elapsed)
                return CommandResult.Fail("This special enquiry has changed or expired. Read the ledger again.");
            if (accept)
            {
                var result = CommitBooking(enquiry.Offer, roomId, enquiry.Definition.Payment, actorId, false);
                if (!result.Success) return result;
            }
            enquiry.Status = accept ? SpecialOfferStatus.Accepted : SpecialOfferStatus.Declined;
            enquiry.Revision++;
            return CommandResult.Ok(accept ? "Special one-night stay accepted at the offered premium." : "Special enquiry politely declined.");
        }
    }
}
