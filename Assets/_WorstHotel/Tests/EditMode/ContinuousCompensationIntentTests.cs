using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace WorstHotel.Tests
{
    // Explicit headless route/key/phone anchor adapters. Sustained cold is a labelled causal
    // fixture, not a claim that these tests reproduce the natural three-day balance scenario.
    internal sealed class CompensationFixture
    {
        internal HotelSimulation Hotel;
        internal RoomState[] Rooms;
        internal GuestStay Guest => Hotel.Guests.Single();
        internal HotelIncident Incident => Hotel.Incidents.Items.Single(item => item.GuestId == Guest.GuestId && item.Reason == IncidentReason.Temperature);
        internal GuestResponse Response => Incident.Response;
        internal GuestServiceIntent Discussion => Hotel.Services.CompensationDiscussion(Guest.GuestId);
        internal static void Require(CommandResult result) => Assert.That(result.Success, Is.True, result.Message);
        internal static string State(HotelSimulation hotel) => JsonUtility.ToJson(hotel.CaptureSnapshot(808, 1));

        internal static CompensationFixture Create(bool populate = true)
        {
            var needs = new NeedProfile(21, 25, 18, 28, .125f, .25f, 65);
            var profiles = new[]
            {
                new GuestProfile(GuestKind.Budget, "Budget", "", 180, .85f, 18, .7f, 28, 90, .55f, needs: needs),
                new GuestProfile(GuestKind.ColdSensitive, "Cold", "", 300, 1.05f, 20, 1.35f, 16, 65, .4f, needs: needs),
                new GuestProfile(GuestKind.Business, "Business", "", 450, 1, 19.5f, 1, 10, 45, .25f, needs: needs)
            };
            var settings = new SessionSettings(profiles, Enumerable.Range(101, 6).Select(id =>
                new RoomProfile(id, "Room " + id, noise: 0, temperature: 22)), new BoilerSettings(), new EconomySettings());
            var rooms = settings.Rooms.Select(profile => new RoomState(profile)).ToArray();
            var hotel = new HotelSimulation(settings, rooms,
                new LivingHotelSettings(firstActivityDelay: 10000, quietDurationMin: 10000, quietDurationMax: 10000),
                new NeedSettings(buildupPerSecond: .1f, complaintExposureSeconds: 2, escalatedExposureSeconds: 20,
                    criticalExposureSeconds: 40, recoverySeconds: 2),
                services: new GuestServiceSettings(eligibility: 0, naturalCommunicationEnabled: true,
                    selfResponseObserveSeconds: 1000, toleranceSeconds: 1000),
                infrastructure: new RoomInfrastructureSettings(lampWearPerSecond: 0), operations: new OperationsSettings());
            var fixture = new CompensationFixture { Hotel = hotel, Rooms = rooms };
            Require(hotel.StartOperations());
            if (!populate) return fixture;
            Require(hotel.DebugSpawnGuest(GuestKind.Business, 101)); hotel.Tick(1.25f);
            Require(hotel.SignalGuestReachedReception(fixture.Guest.GuestId));
            Require(ModelKeyHandoff.CheckIn(hotel, 0, fixture.Guest.GuestId));
            Require(hotel.SignalGuestReachedRoom(fixture.Guest.GuestId));
            fixture.AdvanceTo(hotel.Elapsed + 8);
            Assert.That(fixture.Incident.Stage, Is.GreaterThanOrEqualTo(SituationStage.Complaint));
            Assert.That(fixture.Incident.HasContactedStaff, Is.False);
            return fixture;
        }

        internal void AdvanceTo(float time, bool cold = true)
        {
            while (Hotel.Elapsed < time)
            {
                if (cold && Hotel.Guests.Count > 0) Rooms.Single(room => room.Profile.Id == Guest.RoomId).Temperature = 13;
                Hotel.Tick(Math.Min(.25f, time - Hotel.Elapsed));
            }
        }

        internal void Disclose(GuestContactChannel channel)
        {
            if (channel == GuestContactChannel.RoomConversation)
                Require(Hotel.DiscussRoomConcern(0, Guest.GuestId, Response.Id));
            else
            {
                Require(Hotel.DebugBeginGuestContact(Guest.GuestId, channel));
                Require(Hotel.SignalGuestResponseAnchorReached(Guest.GuestId, Response.Id, Response.ActionVersion,
                    channel == GuestContactChannel.Phone ? GuestResponseAnchor.RoomPhone : GuestResponseAnchor.Reception));
                Require(channel == GuestContactChannel.Phone ? Hotel.AnswerIncomingServiceCall(0, Response.Id) :
                    Hotel.TalkToServiceGuest(0, Guest.GuestId, Response.Id));
            }
            Assert.That(Discussion, Is.Not.Null);
            Assert.That(Discussion.Purpose, Is.EqualTo(ServiceIntentPurpose.CompensationDiscussion));
        }
    }

    public sealed class ContinuousCompensationIntentTests
    {
        static void Require(CommandResult result) => CompensationFixture.Require(result);

        [TestCase(GuestContactChannel.RoomConversation)]
        [TestCase(GuestContactChannel.Phone)]
        [TestCase(GuestContactChannel.Reception)]
        public void PhysicalConversationStartsOneFiniteWaitAndReopeningNeverExtendsIt(GuestContactChannel channel)
        {
            var f = CompensationFixture.Create(); var hotel = f.Hotel;
            Assert.That(hotel.Services.DirectIntent(f.Guest.GuestId), Is.Null, "A private Remote problem must not hold its guest.");
            f.Disclose(channel); var intent = f.Discussion;
            string id = intent.Id; int revision = intent.Revision; float deadline = intent.Deadline;
            Assert.That(deadline, Is.EqualTo(hotel.Elapsed + hotel.Services.Settings.DirectWaitSeconds));
            if (channel == GuestContactChannel.Reception) Assert.That(f.Guest.Agent.State, Is.EqualTo(GuestAgentState.WaitingAtServiceReception));
            if (channel != GuestContactChannel.RoomConversation) Assert.That(f.Guest.Agent.ResponseActionId, Is.EqualTo(f.Response.Id));
            f.AdvanceTo(hotel.Elapsed + 5);
            Require(hotel.BeginCompensationDiscussion(1, f.Guest.GuestId));
            Assert.That(f.Discussion.Id, Is.EqualTo(id)); Assert.That(f.Discussion.Deadline, Is.EqualTo(deadline));
            Assert.That(f.Discussion.Revision, Is.EqualTo(revision));
            Assert.That(hotel.ForceActivity(f.Guest.GuestId, GuestActivity.Shower).Success, Is.False);
            Assert.That(hotel.ForceLeaveRoom(f.Guest.GuestId).Success, Is.False);
            Assert.That(hotel.SkipActivity(f.Guest.GuestId).Success, Is.False);
        }

        [Test]
        public void CompensationWaitSurvivesBedtimeThenTimesOutOnceAndScheduleCanSleep()
        {
            var f = CompensationFixture.Create(); var hotel = f.Hotel;
            f.AdvanceTo(f.Guest.Agent.Schedule.SleepTime - 20); f.Disclose(GuestContactChannel.Phone);
            var intent = f.Discussion;
            f.AdvanceTo(f.Guest.Agent.Schedule.SleepTime + 1);
            Assert.That(intent.Active, Is.True); Assert.That(f.Guest.Agent.State, Is.Not.EqualTo(GuestAgentState.Sleeping));
            f.AdvanceTo(intent.Deadline + .5f);
            Assert.That(intent.Status, Is.EqualTo(ServiceIntentStatus.TimedOut));
            Assert.That(f.Guest.Agent.DirectServiceIntentId, Is.Null);
            Assert.That(f.Guest.Agent.ResponseActionId, Is.Null);
            Assert.That(f.Guest.Agent.State, Is.EqualTo(GuestAgentState.Sleeping));
            int revision = intent.Revision; float resolution = intent.ResolutionAt;
            f.AdvanceTo(hotel.Elapsed + 3);
            Assert.That(intent.Revision, Is.EqualTo(revision)); Assert.That(intent.ResolutionAt, Is.EqualTo(resolution));
            Assert.That(f.Guest.Compensated, Is.False); Assert.That(f.Incident.Active, Is.True);
        }

        [Test]
        public void DeskCreditCompletesWaitOnceButKeepsTheCauseAndRequiresTheRealReturnBoundary()
        {
            var f = CompensationFixture.Create(); f.Disclose(GuestContactChannel.Reception);
            var hotel = f.Hotel; var intent = f.Discussion; int cash = hotel.Economy.Cash;
            string responseId = f.Response.Id; int oldVersion = f.Response.ActionVersion;
            Require(hotel.OfferCompensation(f.Guest.GuestId));
            Assert.That(intent.Status, Is.EqualTo(ServiceIntentStatus.Completed));
            Assert.That(f.Guest.Compensated, Is.True); Assert.That(f.Guest.CompensationCredit, Is.GreaterThan(0));
            Assert.That(f.Guest.Memory.CompensationReceived, Is.EqualTo(f.Guest.CompensationCredit));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(cash), "Credit is reserved until checkout, not charged twice as cash now.");
            Assert.That(f.Incident.Active, Is.True); Assert.That(f.Incident.Resolved, Is.False);
            Assert.That(hotel.Services.Intents.Any(item => item.Kind == ServiceIntentKind.Remote && item.IncidentId == f.Incident.Id && item.Active), Is.True);
            Assert.That(f.Guest.Agent.State, Is.EqualTo(GuestAgentState.ReturningFromServiceReception));
            string after = CompensationFixture.State(hotel);
            Assert.That(hotel.OfferCompensation(f.Guest.GuestId).Success, Is.False);
            Assert.That(hotel.SignalGuestResponseAnchorReached(f.Guest.GuestId, responseId, oldVersion, GuestResponseAnchor.AssignedRoom).Success, Is.False);
            Assert.That(CompensationFixture.State(hotel), Is.EqualTo(after));
            Require(hotel.SignalGuestResponseAnchorReached(f.Guest.GuestId, responseId, f.Guest.Agent.ResponseActionVersion, GuestResponseAnchor.AssignedRoom));
            Assert.That(f.Guest.Agent.InAssignedRoom, Is.True); Assert.That(f.Guest.Agent.ResponseActionId, Is.Null);
        }

        [Test]
        public void ExplicitRefusalAtReceptionReleasesOnlyDiscussionAndStartsReturn()
        {
            var f = CompensationFixture.Create(); f.Disclose(GuestContactChannel.Reception); var intent = f.Discussion;
            Require(f.Hotel.AcceptConsequences(0, f.Guest.GuestId));
            Assert.That(intent.Status, Is.EqualTo(ServiceIntentStatus.Cancelled));
            Assert.That(f.Guest.Compensated, Is.False); Assert.That(f.Incident.Active, Is.True);
            Assert.That(f.Incident.AttentionAcknowledged, Is.True);
            Assert.That(f.Guest.Agent.State, Is.EqualTo(GuestAgentState.ReturningFromServiceReception));
        }

        [Test]
        public void InvalidRoomChoicePreservesDiscussionAndValidKeyProposalReplacesItAtomically()
        {
            var f = CompensationFixture.Create(); f.Disclose(GuestContactChannel.Phone); var hotel = f.Hotel; var original = f.Discussion;
            string before = CompensationFixture.State(hotel);
            Assert.That(hotel.RequestGuestMove(0, f.Guest.GuestId, 999).Success, Is.False);
            Assert.That(CompensationFixture.State(hotel), Is.EqualTo(before));
            Require(hotel.RequestGuestMove(0, f.Guest.GuestId, 106));
            Assert.That(original.Status, Is.EqualTo(ServiceIntentStatus.Cancelled));
            var move = hotel.Services.DirectIntent(f.Guest.GuestId);
            Assert.That(move.Purpose, Is.EqualTo(ServiceIntentPurpose.RoomMove));
            Assert.That(f.Guest.Agent.DirectServiceIntentId, Is.EqualTo(move.Id));
            Assert.That(f.Guest.Agent.ResponseActionId, Is.Null);
            Assert.That(hotel.Services.Intents.Count(item => item.Kind == ServiceIntentKind.Direct && item.Active), Is.EqualTo(1));
            Assert.That(hotel.BeginCompensationDiscussion(0, f.Guest.GuestId).Success, Is.False);
            Require(ModelKeyHandoff.MoveGuest(hotel, 0, f.Guest.GuestId, 106));
            Assert.That(move.Status, Is.EqualTo(ServiceIntentStatus.Completed));
            Assert.That(f.Guest.Agent.State, Is.EqualTo(GuestAgentState.GoingToRoom));
        }

        [Test]
        public void StaleCancelCannotEndAnotherDiscussionAndUnauthorizedOrPrivateRequestsNeverMutate()
        {
            var f = CompensationFixture.Create(); var hotel = f.Hotel;
            string before = CompensationFixture.State(hotel);
            Assert.That(hotel.BeginCompensationDiscussion(0, f.Guest.GuestId).Success, Is.False);
            Assert.That(hotel.OfferCompensation(f.Guest.GuestId).Success, Is.False);
            Assert.That(hotel.BeginCompensationDiscussion(2, f.Guest.GuestId).Success, Is.False);
            Assert.That(hotel.BeginCompensationDiscussion(0, "unknown").Success, Is.False);
            Assert.That(CompensationFixture.State(hotel), Is.EqualTo(before));
            f.Disclose(GuestContactChannel.RoomConversation); var old = f.Discussion;
            Require(hotel.EndCompensationDiscussion(0, f.Guest.GuestId, old.Id, old.Revision));
            Require(hotel.BeginCompensationDiscussion(1, f.Guest.GuestId)); var current = f.Discussion;
            Assert.That(current.Id, Is.Not.EqualTo(old.Id));
            before = CompensationFixture.State(hotel);
            Assert.That(hotel.EndCompensationDiscussion(0, f.Guest.GuestId, old.Id, old.Revision).Success, Is.False);
            Assert.That(hotel.EndCompensationDiscussion(0, f.Guest.GuestId, current.Id, current.Revision + 1).Success, Is.False);
            Assert.That(CompensationFixture.State(hotel), Is.EqualTo(before));
            Require(hotel.EndCompensationDiscussion(0, f.Guest.GuestId, current.Id, current.Revision));
            Require(hotel.ForceActivity(f.Guest.GuestId, GuestActivity.Shower));
            before = CompensationFixture.State(hotel);
            Assert.That(hotel.BeginCompensationDiscussion(0, f.Guest.GuestId).Success, Is.False);
            Assert.That(CompensationFixture.State(hotel), Is.EqualTo(before));
        }

        [Test]
        public void MeasuredRecoveryCancelsAnUnpaidDiscussionWithoutInventingCompensation()
        {
            var f = CompensationFixture.Create(); f.Disclose(GuestContactChannel.RoomConversation); var intent = f.Discussion;
            float target = f.Hotel.Elapsed + 5;
            while (f.Hotel.Elapsed < target)
            { f.Rooms.Single(room => room.Profile.Id == f.Guest.RoomId).Temperature = 22.5f; f.Hotel.Tick(.25f); }
            Assert.That(f.Incident.Active, Is.False);
            Assert.That(intent.Status, Is.EqualTo(ServiceIntentStatus.Cancelled));
            Assert.That(f.Guest.Compensated, Is.False); Assert.That(f.Guest.Agent.DirectServiceIntentId, Is.Null);
        }
    }
}
