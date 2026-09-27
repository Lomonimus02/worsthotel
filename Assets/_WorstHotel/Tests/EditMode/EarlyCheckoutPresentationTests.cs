using NUnit.Framework;

namespace WorstHotel.Tests
{
    public sealed class EarlyCheckoutPresentationTests
    {
        // This suite tests the presentation boundary, not natural eligibility/timing. The
        // established headless fixture supplies a real causal incident and communication;
        // explicitly arranged warning records let malformed/private/stale projections be checked.
        static CompensationFixture WarningFixture(bool communicate)
        {
            var fixture = CompensationFixture.Create();
            if (communicate) fixture.Disclose(GuestContactChannel.RoomConversation);
            var record = fixture.Guest.EarlyCheckout; var incident = fixture.Incident;
            Set(record, nameof(record.State), EarlyCheckoutState.Warning);
            Set(record, nameof(record.IncidentId), incident.Id);
            Set(record, nameof(record.IncidentEpisode), incident.EpisodeCount);
            Set(record, nameof(record.RoomId), fixture.Guest.RoomId);
            Set(record, nameof(record.Reason), incident.Reason);
            Set(record, nameof(record.WarningAt), fixture.Hotel.Elapsed);
            Set(record, nameof(record.SevereExposureSeconds), 90f);
            Set(record, nameof(record.GraceRemainingSeconds), 29.375f);
            return fixture;
        }

        static void Set(object target, string name, object value) => target.GetType().GetProperty(name).SetValue(target, value);

        [Test]
        public void KnownCurrentWarningShowsRiskButNoPrivateCountersAndReadingDoesNotMutateHotel()
        {
            var fixture = WarningFixture(true);
            string before = CompensationFixture.State(fixture.Hotel);
            string warning = GuestLabels.KnownEarlyDepartureWarning(fixture.Guest, fixture.Hotel, fixture.Incident);
            Assert.That(warning, Does.Contain("Reported cold").And.Contain("may leave early"));
            Assert.That(warning, Does.Not.Contain("29.375").And.Not.Contain("90").And.Not.Contain("“"));
            Assert.That(GuestLabels.KnownEarlyDepartureWarning(fixture.Guest, fixture.Hotel), Is.EqualTo(warning));
            Assert.That(CompensationFixture.State(fixture.Hotel), Is.EqualTo(before), "A label cannot renew grace or change cash/clock/events.");
        }

        [TestCase("private")]
        [TestCase("monitoring")]
        [TestCase("old_episode")]
        [TestCase("old_room")]
        [TestCase("inactive_cause")]
        [TestCase("settled")]
        [TestCase("committed")]
        public void PrivateStaleRecoveredAndTerminalRecordsNeverBecomeCurrentPublicWarnings(string defect)
        {
            var fixture = WarningFixture(defect != "private");
            var record = fixture.Guest.EarlyCheckout;
            switch (defect)
            {
                case "monitoring": Set(record, nameof(record.State), EarlyCheckoutState.Monitoring); break;
                case "old_episode": Set(record, nameof(record.IncidentEpisode), fixture.Incident.EpisodeCount + 1); break;
                case "old_room": Set(record, nameof(record.RoomId), fixture.Guest.RoomId + 1); break;
                case "inactive_cause": Set(fixture.Incident, nameof(fixture.Incident.Active), false); break;
                case "settled": Set(fixture.Guest, nameof(fixture.Guest.ReceiptPosted), true); break;
                case "committed": Set(record, nameof(record.State), EarlyCheckoutState.Committed); break;
            }
            Assert.That(GuestLabels.KnownEarlyDepartureWarning(fixture.Guest, fixture.Hotel), Is.Null, defect);
            Assert.That(GuestLabels.KnownEarlyDepartureWarning(fixture.Guest, fixture.Hotel, fixture.Incident), Is.Null, defect);
        }

        [Test]
        public void ImmutableReceiptExplainsEarlyExitWithoutAnyRetainedGuestOrIncidentAndDoesNotChargeAgain()
        {
            var fixture = CompensationFixture.Create(false);
            const string reason = "Severe cold remained unresolved during the stay.";
            var receipt = new GuestReceipt("old-stay", "Former guest", 102, 300, 25, 150,
                "I left early. " + reason, earlyCheckout: true, checkoutAt: 60, departureReason: reason);
            string before = CompensationFixture.State(fixture.Hotel);
            Assert.That(fixture.Hotel.Guests, Is.Empty);
            Assert.That(fixture.Hotel.Incidents.Items, Is.Empty);
            string summary = GuestLabels.EarlyCheckoutReceiptSummary(receipt, fixture.Hotel);
            Assert.That(summary, Does.StartWith("EARLY CHECKOUT").And.Contain(reason));
            Assert.That(summary, Does.Contain(GuestLabels.HotelMoment(fixture.Hotel, receipt.CheckoutAt)));
            Assert.That(receipt.Price, Is.EqualTo(300));
            Assert.That(receipt.Compensation, Is.EqualTo(150));
            Assert.That(receipt.Net, Is.EqualTo(150));
            Assert.That(GuestLabels.EarlyCheckoutReceiptSummary(receipt, fixture.Hotel), Is.EqualTo(summary));
            Assert.That(CompensationFixture.State(fixture.Hotel), Is.EqualTo(before));
            var ordinary = new GuestReceipt("normal-stay", "Guest", 101, 180, 85, 0, "Comfortable stay.");
            Assert.That(GuestLabels.EarlyCheckoutReceiptSummary(ordinary, fixture.Hotel), Is.Null);
        }
    }
}
