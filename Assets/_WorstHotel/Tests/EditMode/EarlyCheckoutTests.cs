using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace WorstHotel.Tests
{
    /// <summary>Production policy/profile thresholds with labelled constant-temperature and
    /// headless route adapters. These isolate decisions, not thermal balance or physical travel.</summary>
    public sealed class EarlyCheckoutTests
    {
        sealed class Fixture
        {
            public HotelSimulation Hotel;
            public RoomState[] Rooms;
            public GuestStay Guest => Hotel.Guests.Single();
            public RoomState Room => Rooms.Single(room => room.Profile.Id == Guest.RoomId);
            public HotelIncident Incident => Hotel.Incidents.Items.Last(item => item.GuestId == Guest.GuestId &&
                item.RoomId == Guest.RoomId && item.Reason == IncidentReason.Temperature);
        }

        static void Require(CommandResult result) => Assert.That(result.Success, Is.True, result.Message);
        static SessionConfig Production() => AssetDatabase.LoadAssetAtPath<SessionConfig>("Assets/_WorstHotel/ScriptableObjects/PrototypeSession.asset");

        static Fixture Create(GuestKind kind = GuestKind.ColdSensitive, bool natural = true, bool enabled = true)
        {
            var asset = Production();
            var living = UnityEngine.Object.Instantiate(asset.living);
            var needs = UnityEngine.Object.Instantiate(asset.needs);
            var services = UnityEngine.Object.Instantiate(asset.services);
            try
            {
                // Decision isolation: keep ordinary activities quiet and select actual contact
                // explicitly. Every production severity, escalation and early-exit duration stays intact.
                living.firstActivityDelay = living.quietDurationMin = living.quietDurationMax = 10000;
                living.awayDurationMin = living.awayDurationMax = 10000;
                services.selfResponseObserveSeconds = 10000;
                services.naturalCommunicationEnabled = natural;
                services.eligibility = 0; // No unrelated optional service in a causal-policy test.
                needs.earlyCheckoutEnabled = enabled;
                var settings = asset.ToData();
                var rooms = settings.Rooms.Select(room => new RoomState(room)).ToArray();
                var hotel = new HotelSimulation(settings, rooms, living.ToData(), needs.ToData(), asset.noise.ToData(),
                    asset.heater.ToData(), asset.electricity.ToData(), asset.housekeeping.ToData(), services.ToData(),
                    asset.infrastructure.ToData(), asset.OperationsData());
                Require(hotel.StartOperations());
                Require(hotel.DebugSpawnGuest(kind, 102)); // Labelled dated one-night booking fixture.
                hotel.Tick(1.25f);
                var guest = hotel.Guests.Single();
                Require(hotel.SignalGuestReachedReception(guest.GuestId));
                Require(ModelKeyHandoff.CheckIn(hotel, 0, guest.GuestId));
                Require(hotel.SignalGuestReachedRoom(guest.GuestId));
                return new Fixture { Hotel = hotel, Rooms = rooms };
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(living); UnityEngine.Object.DestroyImmediate(needs);
                UnityEngine.Object.DestroyImmediate(services);
            }
        }

        static void Advance(Fixture f, float seconds, float temperature = 18)
        {
            float target = f.Hotel.Elapsed + seconds;
            while (f.Hotel.Elapsed < target - .00001f)
            {
                // Explicit measured-condition fixture; do not label this as a naturally cold hotel.
                f.Room.Temperature = temperature;
                f.Hotel.Tick(Math.Min(.25f, target - f.Hotel.Elapsed));
            }
        }

        static void Until(Fixture f, Func<bool> predicate, float limit = 180, float temperature = 18)
        {
            float deadline = f.Hotel.Elapsed + limit;
            while (!predicate() && f.Hotel.Elapsed < deadline) Advance(f, .25f, temperature);
            Assert.That(predicate(), Is.True, "Expected policy transition before the labelled fixture deadline.");
        }

        static GuestResponse Call(Fixture f, bool answer)
        {
            Require(f.Hotel.DebugBeginGuestContact(f.Guest.GuestId, GuestContactChannel.Phone));
            var response = f.Hotel.Services.FindResponse(f.Guest.Agent.ResponseActionId);
            Require(f.Hotel.SignalGuestResponseAnchorReached(f.Guest.GuestId, response.Id,
                f.Guest.Agent.ResponseActionVersion, GuestResponseAnchor.RoomPhone));
            Assert.That(response.ContactAttempts, Is.GreaterThan(0), "Only actual phone-anchor arrival starts an attempt.");
            if (answer) Require(f.Hotel.AnswerIncomingServiceCall(0, response.Id));
            return response;
        }

        static void Communicate(Fixture f, bool credit = false)
        {
            Call(f, true);
            Require(credit ? f.Hotel.OfferCompensation(f.Guest.GuestId) : f.Hotel.AcceptConsequences(0, f.Guest.GuestId));
            Assert.That(f.Incident.HasContactedStaff, Is.True);
            Assert.That(f.Guest.Agent.ResponseActionId, Is.Null);
        }

        static Fixture Warn()
        {
            var f = Create(); Advance(f, 60); Communicate(f);
            Until(f, () => f.Guest.EarlyCheckout.State == EarlyCheckoutState.Warning);
            return f;
        }

        [Test]
        public void ProductionEnablesConservativePolicyAndRawHistoricalSettingsRemainExplicitlyOptIn()
        {
            var asset = Production(); var policy = asset.needs.ToData().EarlyCheckout;
            Assert.That(asset.needs.earlyCheckoutEnabled && policy.Enabled, Is.True);
            Assert.That(new NeedSettings().EarlyCheckout.Enabled, Is.False,
                "Historical model fixtures retain isolation; the authored production asset explicitly opts in.");
            Assert.That(policy.SevereThreshold, Is.EqualTo(.5f));
            Assert.That(policy.RecoveryThreshold, Is.EqualTo(.35f));
            Assert.That(policy.SevereHours, Is.EqualTo(3));
            Assert.That(policy.GraceHours, Is.EqualTo(1));
            Assert.That(policy.RecoveryHours, Is.EqualTo(.25f));
            Assert.That(policy.MinimumRemainingStayHours, Is.EqualTo(1));
            var f = Warn();
            Assert.That(f.Guest.EarlyCheckout.SevereExposureSeconds, Is.EqualTo(90).Within(.3f));
            Assert.That(f.Guest.EarlyCheckout.GraceRemainingSeconds, Is.EqualTo(30));
            Assert.That(f.Hotel.IsEarlyCheckoutWarningKnown(f.Guest), Is.True);
        }

        [TestCase(21f)] // A one-degree dip below this guest's preference.
        [TestCase(18.9f)] // A long complaint can become Critical while severity stays below .5.
        public void MildOrModerateDiscomfortCannotBecomeEarlyCheckoutByDurationOrStageAlone(float temperature)
        {
            var f = Create(natural: false);
            Advance(f, 250, temperature);
            Assert.That(f.Guest.Needs.Temperature.Severity, Is.LessThan(.5f));
            if (temperature < 20) Assert.That(f.Incident.Stage, Is.EqualTo(SituationStage.Critical));
            Assert.That(f.Guest.EarlyCheckout.State, Is.EqualTo(EarlyCheckoutState.None));
            Assert.That(f.Guest.ReceiptPosted, Is.False);
        }

        [Test]
        public void PrivateCriticalEpisodeWithoutARealAttemptCannotWarnOrLeave()
        {
            var f = Create(); Advance(f, 200);
            Assert.That(f.Incident.Stage, Is.EqualTo(SituationStage.Critical));
            Assert.That(f.Incident.HasContactedStaff, Is.False);
            Assert.That(f.Incident.Response.ContactAttempts, Is.Zero);
            Assert.That(f.Guest.EarlyCheckout.State, Is.EqualTo(EarlyCheckoutState.Monitoring));
            Assert.That(f.Guest.EarlyCheckout.WarningAt, Is.EqualTo(-1));
            Assert.That(f.Hotel.IsEarlyCheckoutWarningKnown(f.Guest), Is.False);
            Assert.That(f.Guest.ReceiptPosted, Is.False);
        }

        [Test]
        public void SevereButShortKnownProblemRecoversBeforeAnyWarning()
        {
            var f = Create(); Advance(f, 60); Communicate(f);
            Assert.That(f.Incident.Stage, Is.AtLeast(SituationStage.Escalated));
            Assert.That(f.Guest.EarlyCheckout.State, Is.EqualTo(EarlyCheckoutState.Monitoring));
            Advance(f, 12, 22.5f);
            Assert.That(f.Guest.EarlyCheckout.State, Is.EqualTo(EarlyCheckoutState.None));
            Advance(f, 120, 22.5f);
            Assert.That(f.Guest.EarlyCheckout.WarningAt, Is.EqualTo(-1));
            Assert.That(f.Guest.ReceiptPosted, Is.False);
        }

        [Test]
        public void GenuineUnansweredAttemptsAllowPrivateDepartureButGenericCancellationDoesNot()
        {
            var f = Create(); Advance(f, 60);
            var response = Call(f, false);
            Advance(f, f.Hotel.Services.Settings.PhoneRingSeconds + .25f);
            Assert.That(response.ContactAttempts, Is.EqualTo(1));
            response = Call(f, false);
            Advance(f, f.Hotel.Services.Settings.PhoneRingSeconds + .25f);
            Assert.That(response.Phase, Is.EqualTo(GuestResponsePhase.Cancelled));
            Assert.That(response.ContactAttempts, Is.EqualTo(2));
            Until(f, () => f.Guest.EarlyCheckout.State == EarlyCheckoutState.Warning);
            Assert.That(f.Hotel.IsEarlyCheckoutWarningKnown(f.Guest), Is.False, "Unanswered calls do not disclose a private room cause.");
            // The explicit debug cancellation clears the genuine missed-attempt marker.
            Require(f.Hotel.DebugCancelGuestContact(response.Id));
            Advance(f, 60);
            Assert.That(f.Guest.EarlyCheckout.State, Is.Not.EqualTo(EarlyCheckoutState.Committed));
            Assert.That(f.Guest.ReceiptPosted, Is.False);

            var untouched = Create(); Advance(untouched, 60);
            Call(untouched, false); Advance(untouched, untouched.Hotel.Services.Settings.PhoneRingSeconds + .25f);
            Call(untouched, false); Advance(untouched, untouched.Hotel.Services.Settings.PhoneRingSeconds + .25f);
            Until(untouched, () => untouched.Guest.EarlyCheckout.State == EarlyCheckoutState.Committed);
            Assert.That(untouched.Guest.ReceiptPosted, Is.True);
            Assert.That(untouched.Incident.HasContactedStaff, Is.False);
        }

        [Test]
        public void SevereKnownEpisodeWarnsThenLeavesOnceWithoutChangingContractOrInventingAnExitCallback()
        {
            var f = Warn(); var guest = f.Guest;
            float contract = guest.Agent.CheckoutTime, warning = guest.EarlyCheckout.WarningAt;
            Advance(f, 29);
            Assert.That(guest.Agent.InAssignedRoom, Is.True);
            Assert.That(guest.ReceiptPosted, Is.False);
            Advance(f, 1);
            Assert.That(guest.EarlyCheckout.State, Is.EqualTo(EarlyCheckoutState.Committed));
            Assert.That(guest.EarlyCheckout.CommittedAt - warning, Is.EqualTo(30).Within(.01f));
            Assert.That(guest.Agent.CheckoutTime, Is.EqualTo(contract));
            Assert.That(guest.Agent.State, Is.EqualTo(GuestAgentState.CheckingOut));
            Assert.That(guest.EarlyCheckout.CauseDescription, Does.Contain("severe cold"));
            Assert.That(f.Room.DepartingGuestId, Is.EqualTo(guest.GuestId));
            Assert.That(f.Room.Cleanliness, Is.EqualTo(Cleanliness.Dirty));
            Assert.That(f.Room.GuestId, Is.Null);
            Assert.That(guest.ReceiptPosted, Is.True);
            int cash = f.Hotel.Economy.Cash;
            Advance(f, 5);
            Assert.That(guest.Agent.State, Is.EqualTo(GuestAgentState.Leaving));
            Assert.That(f.Room.DepartingGuestId, Is.EqualTo(guest.GuestId), "Accounting cannot impersonate physical room vacancy.");
            Assert.That(f.Hotel.Economy.Cash, Is.EqualTo(cash));
        }

        [Test]
        public void OutingAndReturnTravelFreezeWarningAndPartialImprovementCancelsIt()
        {
            var f = Warn(); Advance(f, 15);
            float severe = f.Guest.EarlyCheckout.SevereExposureSeconds, grace = f.Guest.EarlyCheckout.GraceRemainingSeconds;
            Require(f.Hotel.ForceLeaveRoom(f.Guest.GuestId));
            Require(f.Hotel.SignalGuestLeftRoom(f.Guest.GuestId));
            Advance(f, 40, 22);
            Require(f.Hotel.ForceReturnRoom(f.Guest.GuestId));
            Advance(f, 5, 22);
            Assert.That(f.Guest.EarlyCheckout.SevereExposureSeconds, Is.EqualTo(severe));
            Assert.That(f.Guest.EarlyCheckout.GraceRemainingSeconds, Is.EqualTo(grace));
            Assert.That(f.Guest.EarlyCheckout.RecoverySeconds, Is.Zero);
            Require(f.Hotel.SignalGuestReturnedRoom(f.Guest.GuestId));
            Advance(f, 8, 19.6f);
            Assert.That(f.Incident.Active, Is.True, "Partial improvement need not falsely close a still-mild causal complaint.");
            Assert.That(f.Guest.EarlyCheckout.State, Is.EqualTo(EarlyCheckoutState.Monitoring));
            Assert.That(f.Guest.EarlyCheckout.SevereExposureSeconds, Is.Zero);
            Assert.That(f.Guest.EarlyCheckout.GraceRemainingSeconds, Is.Zero);
            Assert.That(f.Hotel.IsEarlyCheckoutWarningKnown(f.Guest), Is.False);
            float firstWarning = f.Guest.EarlyCheckout.WarningAt;
            Advance(f, 90);
            Assert.That(f.Guest.EarlyCheckout.State, Is.EqualTo(EarlyCheckoutState.Warning));
            Assert.That(f.Guest.EarlyCheckout.WarningAt, Is.EqualTo(firstWarning), "Same episode cannot announce another warning after a thermal oscillation.");
        }

        [Test]
        public void ReceivedBlanketAtLastGraceStepUsesFreshComfortInsteadOfStaleNeedSnapshot()
        {
            var f = Create(); Advance(f, 8, 20.5f);
            Require(f.Hotel.DebugForceService(f.Guest.GuestId, ServiceKind.ExtraBlanket));
            Call(f, true);
            var request = f.Hotel.Services.Cases.Single(item => item.Kind == ServiceKind.ExtraBlanket);
            Require(f.Hotel.RespondToService(0, request.Id, true));
            Until(f, () => f.Guest.EarlyCheckout.State == EarlyCheckoutState.Warning);
            Advance(f, 29.75f);
            Require(f.Hotel.ForceLeaveRoom(f.Guest.GuestId));
            Require(f.Hotel.SignalGuestLeftRoom(f.Guest.GuestId));
            var item = f.Hotel.Services.Items.First(supply => supply.Kind == ServiceItemKind.Blanket && supply.Location == ServiceItemLocation.OnShelf);
            Require(f.Hotel.TakeServiceItem(0, item.Id));
            var intent = f.Hotel.Services.DropOffIntent(f.Guest.GuestId);
            Require(f.Hotel.DropOffBlanket(0, f.Guest.GuestId, f.Guest.RoomId, intent.Revision, item.Generation));
            Require(f.Hotel.ForceReturnRoom(f.Guest.GuestId));
            Require(f.Hotel.SignalGuestReturnedRoom(f.Guest.GuestId));
            Advance(f, .25f);
            Assert.That(f.Guest.Memory.BlanketsDelivered, Is.EqualTo(1));
            Assert.That(f.Guest.Needs.Temperature.Severity, Is.GreaterThan(.5f), "Needs ran before this step's actual parcel receipt.");
            Assert.That(f.Hotel.CurrentEarlyCheckoutSeverity(f.Guest, f.Incident), Is.LessThan(.35f));
            Assert.That(f.Guest.EarlyCheckout.State, Is.EqualTo(EarlyCheckoutState.Warning));
            Assert.That(f.Guest.EarlyCheckout.GraceRemainingSeconds, Is.EqualTo(.25f).Within(.01f));
            Assert.That(f.Guest.ReceiptPosted, Is.False);
            Advance(f, 8);
            Assert.That(f.Guest.EarlyCheckout.State, Is.EqualTo(EarlyCheckoutState.Monitoring));
        }

        [Test]
        public void CompensationBuysBoundedGraceButCannotRepairTheRoomOrRenewItsBudget()
        {
            var f = Warn(); Advance(f, 10);
            Require(f.Hotel.Services.BeginCompensationDiscussion(0, f.Guest.GuestId, f.Incident.Id));
            Require(f.Hotel.OfferCompensation(f.Guest.GuestId));
            float grace = f.Guest.EarlyCheckout.GraceRemainingSeconds;
            Advance(f, 20);
            Assert.That(f.Guest.EarlyCheckout.GraceRemainingSeconds, Is.EqualTo(grace));
            Assert.That(f.Hotel.CurrentEarlyCheckoutSeverity(f.Guest, f.Incident), Is.GreaterThan(.5f));
            Assert.That(f.Hotel.OfferCompensation(f.Guest.GuestId).Success, Is.False);
            Until(f, () => f.Guest.EarlyCheckout.State == EarlyCheckoutState.Committed);
            Assert.That(f.Guest.CompensationCredit, Is.GreaterThan(0));
        }

        [Test]
        public void NewDirectWaitAfterWarningCannotRenewGraceOrBeReplacedAfterDecisionMatures()
        {
            var f = Warn(); Advance(f, .25f);
            Require(f.Hotel.Services.BeginCompensationDiscussion(0, f.Guest.GuestId, f.Incident.Id));
            var direct = f.Hotel.Services.CompensationDiscussion(f.Guest.GuestId);
            float deadline = direct.Deadline;
            Advance(f, 30);
            Assert.That(f.Guest.EarlyCheckout.GraceRemainingSeconds, Is.Zero);
            Assert.That(f.Hotel.EarlyCheckoutDecisionPending(f.Guest), Is.True);
            Assert.That(f.Guest.ReceiptPosted, Is.False, "An already active finite decision can still finish.");
            Require(f.Hotel.Services.BeginCompensationDiscussion(0, f.Guest.GuestId, f.Incident.Id));
            Assert.That(direct.Deadline, Is.EqualTo(deadline));
            Require(f.Hotel.Services.EndCompensationDiscussion(0, f.Guest.GuestId, direct.Id, direct.Revision));
            Assert.That(f.Hotel.Services.BeginCompensationDiscussion(0, f.Guest.GuestId, f.Incident.Id).Success, Is.False);
            Advance(f, .25f);
            Assert.That(f.Guest.EarlyCheckout.State, Is.EqualTo(EarlyCheckoutState.Committed));
        }

        [Test]
        public void ActualKeyExchangeResetsOldRoomEvidenceAndNewRoomStartsItsOwnTrial()
        {
            var f = Warn(); string oldIncident = f.Incident.Id;
            Require(ModelKeyHandoff.MoveGuest(f.Hotel, 0, f.Guest.GuestId, 106));
            Advance(f, 1, 22.5f);
            Assert.That(f.Guest.EarlyCheckout.State, Is.EqualTo(EarlyCheckoutState.None));
            Require(f.Hotel.SignalGuestReachedRoom(f.Guest.GuestId));
            Advance(f, 5, 22.5f);
            Assert.That(f.Guest.EarlyCheckout.State, Is.EqualTo(EarlyCheckoutState.None));
            Advance(f, .25f);
            Assert.That(f.Guest.EarlyCheckout.RoomId, Is.EqualTo(106));
            Assert.That(f.Guest.EarlyCheckout.IncidentId, Is.Not.EqualTo(oldIncident));
            Assert.That(f.Guest.EarlyCheckout.WarningAt, Is.EqualTo(-1));
            Assert.That(f.Guest.EarlyCheckout.SevereExposureSeconds, Is.LessThan(1));
            Assert.That(f.Guest.ReceiptPosted, Is.False);
        }

        [Test]
        public void TooLittleContractRemainingUsesNormalCheckoutEvenWithAnOldFrozenWarning()
        {
            var f = Warn(); float contract = f.Guest.Agent.CheckoutTime;
            Require(f.Hotel.ForceLeaveRoom(f.Guest.GuestId));
            Require(f.Hotel.SignalGuestLeftRoom(f.Guest.GuestId));
            Advance(f, contract - f.Hotel.Elapsed - 15, 22.5f);
            Require(f.Hotel.ForceReturnRoom(f.Guest.GuestId));
            Require(f.Hotel.SignalGuestReturnedRoom(f.Guest.GuestId));
            Advance(f, 14);
            Assert.That(f.Guest.ReceiptPosted, Is.False);
            Advance(f, 1);
            Assert.That(f.Guest.ReceiptPosted, Is.True);
            Assert.That(f.Guest.Agent.CheckoutTime, Is.EqualTo(contract));
            Assert.That(f.Guest.EarlyCheckout.State, Is.EqualTo(EarlyCheckoutState.None));
            Assert.That(f.Guest.EarlyCheckout.CommittedAt, Is.EqualTo(-1));
        }

        [Test]
        public void PersonalityAndPreviousEpisodeAffectPatienceOnceWithoutReusingOldSevereExposure()
        {
            var business = Create(GuestKind.Business, natural: false);
            var budget = Create(GuestKind.Budget, natural: false);
            Until(business, () => business.Guest.EarlyCheckout.State == EarlyCheckoutState.Warning, temperature: 15);
            Until(budget, () => budget.Guest.EarlyCheckout.State == EarlyCheckoutState.Warning, temperature: 15);
            Assert.That(business.Guest.EarlyCheckout.SevereExposureSeconds, Is.EqualTo(67.5f).Within(.3f));
            Assert.That(budget.Guest.EarlyCheckout.SevereExposureSeconds, Is.EqualTo(90f * 90 / 65 * 1.15f).Within(.3f));

            var f = Warn(); int oldEpisode = f.Incident.EpisodeCount;
            Advance(f, 21, 22.5f); // Real recovery plus reopen cooldown; the next episode starts from zero.
            Advance(f, .25f);
            Assert.That(f.Incident.EpisodeCount, Is.EqualTo(oldEpisode + 1));
            Assert.That(f.Guest.EarlyCheckout.SevereExposureSeconds, Is.LessThan(1));
            Advance(f, 59.75f); Communicate(f);
            Until(f, () => f.Guest.EarlyCheckout.State == EarlyCheckoutState.Warning);
            Assert.That(f.Incident.EffectivePatienceMultiplier, Is.EqualTo(.8f).Within(.001f));
            Assert.That(f.Guest.EarlyCheckout.SevereExposureSeconds, Is.EqualTo(72).Within(.3f),
                "Use the episode's captured .8 once; current complaint memory must not reduce it again.");
        }

        [Test]
        public void DisabledHistoricalPolicyAndReadOnlyMirrorCannotAdvanceEarlyDeparture()
        {
            var disabled = Create(natural: false, enabled: false); Advance(disabled, 180);
            Assert.That(disabled.Incident.Stage, Is.EqualTo(SituationStage.Critical));
            Assert.That(disabled.Guest.EarlyCheckout.State, Is.EqualTo(EarlyCheckoutState.None));
            var f = Warn();
            string before = JsonUtility.ToJson(f.Hotel.CaptureSnapshot(431, 1));
            f.Hotel.EnableReadOnlyMirror(); f.Hotel.Tick(180);
            Assert.That(JsonUtility.ToJson(f.Hotel.CaptureSnapshot(431, 1)), Is.EqualTo(before));
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(-1f)]
        [TestCase(0f)]
        public void InvalidSevereDurationRejectsBeforeASettingsObjectCanReachTheModel(float hours)
        { Assert.Throws<ArgumentException>(() => new EarlyCheckoutSettings(enabled: true, severeHours: hours)); }

        [Test]
        public void SeverityHysteresisAndPatienceBoundsMustBeOrdered()
        {
            Assert.Throws<ArgumentException>(() => new EarlyCheckoutSettings(severeThreshold: 1));
            Assert.Throws<ArgumentException>(() => new EarlyCheckoutSettings(severeThreshold: .5f, recoveryThreshold: .5f));
            Assert.Throws<ArgumentException>(() => new EarlyCheckoutSettings(minimumPatienceMultiplier: 2, maximumPatienceMultiplier: 1));
        }
    }
}
