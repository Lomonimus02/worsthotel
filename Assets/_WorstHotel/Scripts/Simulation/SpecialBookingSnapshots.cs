using System;
using System.Linq;

namespace WorstHotel
{
    [Serializable] public sealed class SpecialBookingSnapshot
    {
        public SpecialBookingSettings Settings;
        public int LastOfferDay;
        public SpecialEnquirySnapshot[] Enquiries;
    }
    [Serializable] public sealed class SpecialEnquirySnapshot
    {
        public ScheduledOfferSnapshot Offer;
        public SpecialOfferStatus Status;
        public int Revision;
    }
    public sealed partial class HotelSimulation
    {
        SpecialBookingSnapshot CaptureSpecialBookings() => new SpecialBookingSnapshot
        {
            Settings = Operations.SpecialBookings.Copy(), LastOfferDay = lastSpecialOfferDay,
            Enquiries = specialEnquiries.Select(e => new SpecialEnquirySnapshot
                { Offer = SnapshotData.Capture(e.Offer), Status = e.Status, Revision = e.Revision }).ToArray()
        };
        void RestoreSpecialBookings(SpecialBookingSnapshot data)
        {
            lastSpecialOfferDay = data.LastOfferDay; specialEnquiries.Clear();
            specialEnquiries.AddRange(data.Enquiries.Select(e => new SpecialBookingEnquiry(SnapshotData.Offer(e.Offer))
                { Status = e.Status, Revision = e.Revision }));
        }
    }
    internal static partial class SnapshotValidation
    {
        static void SpecialBookings(HotelModelSnapshot model, SpecialBookingSettings expected, HotelCalendar calendar, GuestScheduleSystem schedules)
        {
            var data = model.Operations.SpecialBookings;
            Require(data?.Settings != null, "Missing special booking settings.");
            data.Settings.Validate(); var actual = data.Settings;
            Require(actual.Enabled == expected.Enabled && actual.FirstOfferDay == expected.FirstOfferDay &&
                actual.IntervalDays == expected.IntervalDays && actual.OfferHour == expected.OfferHour, "Special booking settings differ.");
            Require(data.LastOfferDay >= 0 && data.LastOfferDay <= model.Day, "Invalid special offer date.");
            var enquiries = Array(data.Enquiries, 6); Unique(enquiries.Select(e => e.Offer?.Application?.Id));
            Require(enquiries.Count(e => e.Status == SpecialOfferStatus.Pending) <= 1, "Too many unanswered special enquiries.");
            foreach (var e in enquiries)
            {
                EnumValue(e.Status); ValidateScheduledOffer(e.Offer, calendar, schedules);
                Require(e.Offer.Application.SpecialKind != SpecialGuestKind.None && e.Revision == (e.Status == SpecialOfferStatus.Pending ? 1 : 2), "Invalid special enquiry.");
                var reservation = model.Operations.Reservations.FirstOrDefault(r => r.Offer.Application.Id == e.Offer.Application.Id);
                Require((e.Status == SpecialOfferStatus.Accepted) == (reservation != null), "Special decision and reservation disagree.");
                if (reservation != null) Require(!reservation.IsAutomatic && reservation.Price == SpecialGuestDefinition.For(e.Offer.Application.SpecialKind).Payment,
                    "Special stay needs explicit acceptance at the advertised price.");
                if (e.Status == SpecialOfferStatus.Pending) Require(e.Offer.ArrivalAt > model.Time, "Expired enquiry is still pending.");
            }
        }
    }
}
