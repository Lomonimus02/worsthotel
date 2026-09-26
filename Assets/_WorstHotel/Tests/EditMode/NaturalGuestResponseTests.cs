using System;
using System.Linq;
using NUnit.Framework;

namespace WorstHotel.Tests
{
    /// <summary>Explicit model route adapters test causal rules. Physical route completion is proved separately in PlayMode.</summary>
    public sealed class NaturalGuestResponseTests
    {
        sealed class Fixture
        {
            public HotelSimulation Hotel;
            public RoomState[] Rooms;
            public GuestStay Guest => Hotel.Guests.Single(item => item.GuestId == "natural-guest");
            public RoomState Room => Rooms.Single(item => item.Profile.Id == Guest.RoomId);
            public GuestResponse Response => Hotel.Services.Responses.Single(item => item.GuestId == Guest.GuestId && item.IncidentId != null);
        }

        static Fixture Create(GuestKind kind = GuestKind.ColdSensitive, bool waiting = false, int budget = 3,
            float observe = 2, float selfObserve = 1, float tolerance = 2)
        {
            var profiles = new[] {
                new GuestProfile(GuestKind.Budget, "Budget", "", 180, .85f, 18, .7f, 28, 90, .55f),
                new GuestProfile(GuestKind.ColdSensitive, "Cold", "", 300, 1.05f, 20, 1.35f, 16, 65, .4f,
                    needs: new NeedProfile(21, 25, 18, 28, .125f, .25f, 65)),
                new GuestProfile(GuestKind.Business, "Business", "", 450, 1, 19.5f, 1, 10, 45, .25f,
                    needs: new NeedProfile(21, 25, 18, 28, .125f, .25f, 65)) };
            var settings = new SessionSettings(profiles, Enumerable.Range(101, 6).Select(id =>
                new RoomProfile(id, "Room " + id, noise: 0, temperature: 22.5f)), new BoilerSettings(), new EconomySettings(), serviceSeconds: 300);
            var rooms = settings.Rooms.Select(room => new RoomState(room)).ToArray();
            var hotel = new HotelSimulation(settings, rooms,
                new LivingHotelSettings(firstArrivalSeconds: .2f, arrivalJitterSeconds: 0, firstActivityDelay: 1000,
                    activityDurationMin: 120, activityDurationMax: 120),
                new NeedSettings(buildupPerSecond: .1f, complaintExposureSeconds: 20, escalatedExposureSeconds: 40,
                    criticalExposureSeconds: 60, recoverySeconds: 2), services: new GuestServiceSettings(
                    maxCasesPerShift: budget, eligibility: 1, soloFrequencyMultiplier: 1, observationSeconds: observe,
                    naturalCommunicationEnabled: true, selfResponseObserveSeconds: selfObserve, toleranceSeconds: tolerance,
                    phoneRingSeconds: 3, contactRetryDelaySeconds: 3, receptionWaitSeconds: 5));
            var profile = profiles.Single(item => item.Kind == kind);
            var offer = new BookingApplication("natural-guest", "Natural guest", profile, profile.ReferencePrice);
            Assert.That(hotel.StartShift(new[] { new BookingAssignment(102, offer.Id, profile.ReferencePrice, 0) }, new[] { offer }).Success, Is.True);
            hotel.Tick(.4f); Assert.That(hotel.SignalGuestReachedReception(offer.Id).Success, Is.True);
            if (waiting) rooms.Single(room => room.Profile.Id == 102).Cleanliness = Cleanliness.Dirty;
            else
            {
                Assert.That(ModelKeyHandoff.CheckIn(hotel, 0, offer.Id).Success, Is.True);
                Assert.That(hotel.SignalGuestReachedRoom(offer.Id).Success, Is.True);
            }
            return new Fixture { Hotel = hotel, Rooms = rooms };
        }

        static void Advance(Fixture f, float seconds, float temperature = 22.5f)
        {
            while (seconds > .0001f)
            {
                foreach (var room in f.Rooms) room.Temperature = temperature;
                float dt = Math.Min(.2f, seconds); f.Hotel.Tick(dt); seconds -= dt;
            }
        }

        static CommandResult Reach(Fixture f, GuestResponseAnchor anchor) => f.Hotel.SignalGuestResponseAnchorReached(
            f.Guest.GuestId, f.Guest.Agent.ResponseActionId, f.Guest.Agent.ResponseActionVersion, anchor);

        static GuestStay AddSecondGuest(Fixture f)
        {
            Assert.That(f.Hotel.DebugSpawnGuest(GuestKind.Business, 104).Success, Is.True);
            var guest = f.Hotel.Guests.Last();
            Assert.That(f.Hotel.SignalGuestReachedReception(guest.GuestId).Success, Is.True);
            Assert.That(ModelKeyHandoff.CheckIn(f.Hotel, 0, guest.GuestId).Success, Is.True);
            Assert.That(f.Hotel.SignalGuestReachedRoom(guest.GuestId).Success, Is.True);
            return guest;
        }

        static void PrepareTwoPrivateColdCases(Fixture f, GuestStay other)
        {
            Advance(f, .2f, 19.5f);
            Assert.That(f.Hotel.DebugForceService(f.Guest.GuestId, ServiceKind.ExtraBlanket).Success, Is.True);
            Assert.That(f.Hotel.DebugForceService(other.GuestId, ServiceKind.ExtraBlanket).Success, Is.True);
            Assert.That(f.Hotel.DebugBeginGuestContact(f.Guest.GuestId, GuestContactChannel.Phone).Success, Is.True);
        }

        static GuestResponse Phone(Fixture f)
        {
            Advance(f, .2f, 19.5f);
            Assert.That(f.Hotel.DebugForceService(f.Guest.GuestId, ServiceKind.ExtraBlanket).Success, Is.True);
            Assert.That(f.Hotel.DebugBeginGuestContact(f.Guest.GuestId, GuestContactChannel.Phone).Success, Is.True);
            var response = f.Response;
            Assert.That(f.Hotel.Services.IncomingCall, Is.Null, "Walking to a telephone is not a ringing call.");
            Assert.That(Reach(f, GuestResponseAnchor.RoomPhone).Success, Is.True);
            return response;
        }

        [Test]
        public void PrivateNoticeWaitsForRealRadiatorArrivalAndMeasuredRecoveryNeverCreatesAStaffTask()
        {
            var f = Create(); Advance(f, 1.4f, 19.5f);
            var response = f.Response; int setting = f.Room.RadiatorSetting; float load = f.Hotel.Boiler.Load;
            Assert.That(response.Phase, Is.EqualTo(GuestResponsePhase.SelfResponding));
            Advance(f, 3, 19.5f);
            Assert.That(f.Room.RadiatorSetting, Is.EqualTo(setting), "No elapsed timer may impersonate arrival at the valve.");
            Assert.That(f.Hotel.Services.Cases, Is.Empty); Assert.That(f.Hotel.Requests.ActiveCount, Is.Zero);
            Assert.That(Reach(f, GuestResponseAnchor.Radiator).Success, Is.True);
            Assert.That(f.Room.RadiatorSetting, Is.EqualTo(setting + 1));
            Assert.That(f.Hotel.Boiler.Load, Is.GreaterThan(load));
            Assert.That(response.SelfResponseApplied, Is.True); Assert.That(response.StaffActionAt, Is.EqualTo(-1));
            Assert.That(Reach(f, GuestResponseAnchor.Radiator).Success, Is.False);
            Advance(f, 3, 22.5f);
            Assert.That(response.Phase, Is.EqualTo(GuestResponsePhase.Cancelled));
            Assert.That(f.Guest.Memory.ServicesRequested, Is.Zero); Assert.That(f.Guest.Memory.ProblemsResolvedSuccessfully, Is.Zero);
        }

        [Test]
        public void CurrentCauseDwellResetsAfterRecoveryInsteadOfBorrowingLifetimeExposure()
        {
            var f = Create(observe: 5, selfObserve: 8, tolerance: 2);
            Advance(f, 3, 19.5f); float history = f.Guest.Needs.Temperature.ExposureSeconds;
            Advance(f, 3, 22.5f); Advance(f, 1, 19.5f);
            Assert.That(f.Guest.Needs.Temperature.ExposureSeconds, Is.GreaterThan(history));
            Assert.That(f.Hotel.Services.Responses.All(item => item.DwellSeconds < 5), Is.True);
            Assert.That(f.Hotel.Services.Cases, Is.Empty); Assert.That(f.Hotel.Services.IncomingCall, Is.Null);
        }

        [Test]
        public void DistressAloneDoesNotCommunicateCreatePublicRequestOrRecordIgnoredComplaint()
        {
            // Business uses its room phone, retaining actual room exposure. A reception
            // trip correctly suspends exposure and cannot prove progression to Critical.
            var f = Create(GuestKind.Business, budget: 0); Advance(f, 65, 5);
            var incident = f.Hotel.Incidents.Items.Single(item => item.Reason == IncidentReason.Temperature);
            Assert.That(incident.Stage, Is.EqualTo(SituationStage.Critical));
            Assert.That(incident.HasContactedStaff, Is.False); Assert.That(incident.ComplaintRecorded, Is.False);
            Assert.That(f.Hotel.Requests.Items, Is.Empty); Assert.That(f.Guest.Memory.NumberOfComplaints, Is.Zero);
            f.Hotel.EndShift(); Assert.That(f.Guest.Memory.ProblemsIgnored, Is.Zero);
        }

        [Test]
        public void AnswerDisclosesSameEpisodeExactlyOnceAndBlanketNeedsSustainedPerceivedRecovery()
        {
            var f = Create(); var response = Phone(f); var request = f.Hotel.Services.Cases.Single();
            Assert.That(request.Response, Is.SameAs(response));
            Assert.That(f.Hotel.Incidents.Items.Single().Response, Is.SameAs(response));
            Assert.That(request.IsKnownToHotel, Is.False); Assert.That(request.BudgetCharged, Is.True);
            Assert.That(f.Hotel.RespondToService(0, request.Id, true).Success, Is.False);
            Assert.That(f.Hotel.AnswerIncomingServiceCall(2, response.Id).Success, Is.False);
            Assert.That(f.Hotel.AnswerIncomingServiceCall(0, response.Id).Success, Is.True);
            Assert.That(f.Hotel.AnswerIncomingServiceCall(1, response.Id).Success, Is.False);
            Assert.That(f.Guest.Memory.ServicesRequested, Is.EqualTo(1));
            Assert.That(f.Hotel.AcknowledgeService(0, request.Id).Success, Is.True);
            Assert.That(response.StaffActionAt, Is.EqualTo(-1), "Acknowledgement does not repair a physical cause.");
            Assert.That(f.Hotel.TakeServiceItem(0, "blanket:0").Success, Is.True);
            Assert.That(f.Hotel.DeliverBlanket(0, f.Guest.GuestId).Success, Is.True);
            Assert.That(request.Active, Is.True); Assert.That(f.Guest.Memory.ServicesFulfilled, Is.Zero);
            Advance(f, .4f, 19.5f); Assert.That(request.Active, Is.True);
            Advance(f, 2, 19.5f); Assert.That(request.Status, Is.EqualTo(ServiceStatus.Fulfilled));
            Assert.That(f.Guest.Memory.ServicesFulfilled, Is.EqualTo(1));
            Assert.That(f.Room.Temperature, Is.LessThan(21));
        }

        [Test]
        public void MissedTelephoneContactHasOnlyOneRetryAndNeverSpendsASecondBudgetSlot()
        {
            var f = Create(); var response = Phone(f); var request = f.Hotel.Services.Cases.Single();
            int oldVersion = response.ActionVersion;
            Advance(f, 3.4f, 19.5f); Assert.That(f.Hotel.Services.IncomingCall, Is.Null);
            Advance(f, 3.4f, 19.5f);
            Assert.That(f.Guest.Agent.Activity, Is.EqualTo(GuestActivity.CallReception));
            Assert.That(f.Hotel.SignalGuestResponseAnchorReached(f.Guest.GuestId, response.Id, oldVersion, GuestResponseAnchor.RoomPhone).Success, Is.False);
            Assert.That(Reach(f, GuestResponseAnchor.RoomPhone).Success, Is.True);
            Advance(f, 12, 19.5f);
            Assert.That(response.ContactAttempts, Is.EqualTo(2)); Assert.That(response.Phase, Is.EqualTo(GuestResponsePhase.Cancelled));
            Assert.That(f.Hotel.Services.IncomingCall, Is.Null); Assert.That(f.Hotel.Services.Cases.Single(), Is.SameAs(request));
            Assert.That(f.Guest.Memory.ServicesRequested, Is.Zero);
        }

        [Test]
        public void ReceptionTravelCannotFalselyResolveTheCauseAndCancelledReturnPreservesTheRoom()
        {
            var f = Create(); Advance(f, .2f, 19.5f);
            Assert.That(f.Hotel.DebugForceService(f.Guest.GuestId, ServiceKind.ExtraBlanket).Success, Is.True);
            Assert.That(f.Hotel.DebugBeginGuestContact(f.Guest.GuestId, GuestContactChannel.Reception).Success, Is.True);
            var response = f.Response; var incident = f.Hotel.Incidents.Items.Single();
            Advance(f, 10, 19.5f); Assert.That(incident.Active, Is.True); Assert.That(incident.Resolved, Is.False);
            Assert.That(f.Room.GuestId, Is.EqualTo(f.Guest.GuestId));
            Assert.That(Reach(f, GuestResponseAnchor.Reception).Success, Is.True);
            Assert.That(response.CommunicatedAt, Is.EqualTo(-1));
            Assert.That(f.Hotel.DebugCancelGuestContact(response.Id).Success, Is.True);
            Assert.That(f.Guest.Agent.State, Is.EqualTo(GuestAgentState.ReturningFromServiceReception));
            Advance(f, 3, 22.5f);
            Assert.That(Reach(f, GuestResponseAnchor.AssignedRoom).Success, Is.True);
            Assert.That(f.Guest.Agent.InAssignedRoom, Is.True); Assert.That(f.Guest.Agent.ResponseActionId, Is.Null);
            Assert.That(f.Room.GuestId, Is.EqualTo(f.Guest.GuestId)); Assert.That(f.Hotel.Housekeeping.Find(102), Is.Null);
        }

        [Test]
        public void ACommunicatedMildCaseEscalatesThroughTheSameResponseWithoutAnotherCallOrMemoryEntry()
        {
            var f = Create(); var response = Phone(f);
            Assert.That(f.Hotel.AnswerIncomingServiceCall(0, response.Id).Success, Is.True);
            Advance(f, 25, 5);
            var incident = f.Hotel.Incidents.Items.Single(item => item.Reason == IncidentReason.Temperature);
            Assert.That(incident.Response, Is.SameAs(response)); Assert.That(incident.ComplaintRecorded, Is.True);
            Assert.That(f.Hotel.Services.Cases.Single().Status, Is.EqualTo(ServiceStatus.Escalated));
            Assert.That(f.Hotel.Requests.ActiveCount, Is.EqualTo(1)); Assert.That(f.Guest.Memory.NumberOfComplaints, Is.EqualTo(1));
            Assert.That(response.ContactAttempts, Is.EqualTo(1)); Assert.That(f.Hotel.Services.IncomingCall, Is.Null);
            Advance(f, 20, 5); Assert.That(f.Guest.Memory.NumberOfComplaints, Is.EqualTo(1));
        }

        [Test]
        public void RoomConversationCanExplainAnObservedConcernButDoesNotAcceptOrResolveIt()
        {
            var f = Create(selfObserve: 20); Advance(f, 2.4f, 19.5f);
            var response = f.Response;
            Assert.That(f.Hotel.DiscussRoomConcern(0, "another-guest", response.Id).Success, Is.False);
            Assert.That(f.Hotel.DiscussRoomConcern(0, f.Guest.GuestId, response.Id).Success, Is.True);
            Assert.That(response.Channel, Is.EqualTo(GuestContactChannel.RoomConversation));
            Assert.That(response.ContactAttempts, Is.Zero);
            Assert.That(f.Hotel.Services.Cases.Single().Status, Is.EqualTo(ServiceStatus.Requested));
            Assert.That(response.StaffActionAt, Is.EqualTo(-1)); Assert.That(f.Guest.Needs.Temperature.Severity, Is.GreaterThan(0));
        }

        [Test]
        public void AcknowledgementAndCompensationDoNotEarnPhysicalRepairCreditWhenTheRoomRecoversByItself()
        {
            var f = Create(); var response = Phone(f);
            Assert.That(f.Hotel.AnswerIncomingServiceCall(0, response.Id).Success, Is.True);
            Advance(f, 25, 5);
            Assert.That(f.Guest.Memory.NumberOfComplaints, Is.EqualTo(1));
            Assert.That(f.Hotel.OfferCompensation(f.Guest.GuestId).Success, Is.True);
            Assert.That(response.StaffActionAt, Is.EqualTo(-1));
            Advance(f, 3, 22.5f);
            Assert.That(f.Hotel.Incidents.Items.Single().Resolved, Is.True);
            Assert.That(f.Guest.Memory.ProblemsResolvedSuccessfully, Is.Zero);
            Assert.That(f.Guest.Memory.NumberOfComplaints, Is.EqualTo(1));
        }

        [Test]
        public void ARealStaffRadiatorAdjustmentAndLaterMeasuredRecoveryEarnOneRepairMemory()
        {
            var f = Create(); var response = Phone(f);
            Assert.That(f.Hotel.AnswerIncomingServiceCall(0, response.Id).Success, Is.True);
            Advance(f, 25, 5);
            Assert.That(f.Hotel.SetRadiatorSetting(0, f.Guest.RoomId, 2).Success, Is.True);
            Assert.That(response.StaffActionAt, Is.GreaterThanOrEqualTo(0));
            Advance(f, 3, 22.5f);
            Assert.That(f.Guest.Memory.ProblemsResolvedSuccessfully, Is.EqualTo(1));
            Advance(f, 3, 22.5f);
            Assert.That(f.Guest.Memory.ProblemsResolvedSuccessfully, Is.EqualTo(1));
        }

        [Test]
        public void WakeAndLateCheckoutRequireARealScheduleContextAndCannotBeRequestedDuringShower()
        {
            var f = Create(GuestKind.Business, selfObserve: 100);
            Assert.That(f.Hotel.DebugForceService(f.Guest.GuestId, ServiceKind.WakeUpCall).Success, Is.False);
            Assert.That(f.Hotel.DebugForceService(f.Guest.GuestId, ServiceKind.LateCheckout).Success, Is.False);
            Advance(f, f.Guest.Agent.Schedule.SleepTime - f.Hotel.Services.Settings.ReplySeconds + 1 - f.Hotel.Elapsed);
            Assert.That(f.Hotel.ForceActivity(f.Guest.GuestId, GuestActivity.Shower).Success, Is.True);
            Assert.That(f.Hotel.DebugForceService(f.Guest.GuestId, ServiceKind.WakeUpCall).Success, Is.False);
            Assert.That(f.Hotel.DebugForceService(f.Guest.GuestId, ServiceKind.LateCheckout).Success, Is.False);
            Assert.That(f.Hotel.ForceActivity(f.Guest.GuestId, GuestActivity.QuietRest).Success, Is.True);
            // The naturally created private intent may already exist at the start of this window.
            var wake = f.Hotel.Services.Cases.FirstOrDefault(item => item.Kind == ServiceKind.WakeUpCall && item.Active);
            if (wake == null) Assert.That(f.Hotel.DebugForceService(f.Guest.GuestId, ServiceKind.WakeUpCall).Success, Is.True);
            Assert.That(f.Hotel.Services.Cases.Last().IsKnownToHotel, Is.False);
        }

        [Test]
        public void LuggageRequiresRoomReadinessAndActualDeskDisclosureBeforeAcceptance()
        {
            var f = Create(waiting: true); Advance(f, 6);
            var request = f.Hotel.Services.Cases.Single(item => item.Kind == ServiceKind.LuggageStorage);
            var response = request.Response;
            Assert.That(response.Channel, Is.EqualTo(GuestContactChannel.Reception));
            Assert.That(request.IsKnownToHotel, Is.False); Assert.That(f.Hotel.RespondToService(0, request.Id, true).Success, Is.False);
            Assert.That(f.Hotel.TalkToServiceGuest(0, f.Guest.GuestId, response.Id).Success, Is.True);
            Assert.That(f.Hotel.RespondToService(0, request.Id, true).Success, Is.True);
            Assert.That(f.Hotel.TakeServiceItem(0, "luggage:" + f.Guest.GuestId).Success, Is.True);
            Assert.That(f.Hotel.StoreLuggage(0, f.Guest.GuestId).Success, Is.True);
            Assert.That(f.Room.Cleanliness, Is.EqualTo(Cleanliness.Dirty));
            Assert.That(f.Guest.Memory.ServicesRequested, Is.EqualTo(1)); Assert.That(f.Guest.Memory.ServicesFulfilled, Is.EqualTo(1));
        }

        [Test]
        public void AContextualWakeConversationBecomesOnePromiseAndRemainsValidThroughItsCallWindow()
        {
            var f = Create(GuestKind.Business, selfObserve: 100);
            Advance(f, f.Guest.Agent.Schedule.SleepTime - f.Hotel.Services.Settings.ReplySeconds + 1 - f.Hotel.Elapsed);
            var request = f.Hotel.Services.Cases.Single(item => item.Kind == ServiceKind.WakeUpCall);
            Assert.That(request.IsKnownToHotel, Is.False);
            Assert.That(f.Hotel.DebugBeginGuestContact(f.Guest.GuestId, GuestContactChannel.Phone).Success, Is.True);
            Assert.That(Reach(f, GuestResponseAnchor.RoomPhone).Success, Is.True);
            Assert.That(f.Hotel.AnswerIncomingServiceCall(0, request.Response.Id).Success, Is.True);
            Assert.That(f.Hotel.RespondToService(0, request.Id, true).Success, Is.True);
            var promise = f.Hotel.Services.Promises.Single();
            Advance(f, promise.DueTime + 1 - f.Hotel.Elapsed);
            Assert.That(request.Status, Is.EqualTo(ServiceStatus.InProgress));
            Assert.That(f.Hotel.CompleteWakeUpCall(0, promise.Id).Success, Is.True);
            Assert.That(f.Hotel.CompleteWakeUpCall(1, promise.Id).Success, Is.False);
            Assert.That(f.Guest.Memory.PromisesKept, Is.EqualTo(1));
            Assert.That(f.Guest.Memory.ServicesRequested, Is.EqualTo(1));
        }

        [TestCase(GuestContactChannel.Phone)]
        [TestCase(GuestContactChannel.Reception)]
        public void LastContactSlotTakenDuringTravelCancelsUnchargedSoftActionAndLetsGuestResumeLife(GuestContactChannel channel)
        {
            var f = Create(budget: 1, selfObserve: 100); var other = AddSecondGuest(f);
            PrepareTwoPrivateColdCases(f, other);
            Assert.That(f.Hotel.DebugBeginGuestContact(other.GuestId, channel).Success, Is.True);
            var response = f.Hotel.Services.Responses.Single(item => item.GuestId == other.GuestId);
            string oldAction = other.Agent.ResponseActionId; int oldVersion = other.Agent.ResponseActionVersion;
            Assert.That(Reach(f, GuestResponseAnchor.RoomPhone).Success, Is.True, "The first real arrival spends the single slot.");
            Advance(f, .2f, 19.5f);
            Assert.That(response.Phase, Is.EqualTo(GuestResponsePhase.Cancelled));
            Assert.That(response.ContactAttempts, Is.Zero);
            Assert.That(f.Hotel.Services.FindCase(response.ServiceCaseId).Status, Is.EqualTo(ServiceStatus.Expired));
            Assert.That(f.Hotel.Services.Cases.Count(item => item.BudgetCharged), Is.EqualTo(1));
            Assert.That(f.Hotel.SignalGuestResponseAnchorReached(other.GuestId, oldAction, oldVersion,
                channel == GuestContactChannel.Phone ? GuestResponseAnchor.RoomPhone : GuestResponseAnchor.Reception).Success, Is.False);
            if (channel == GuestContactChannel.Reception)
            {
                Assert.That(other.Agent.State, Is.EqualTo(GuestAgentState.ReturningFromServiceReception));
                Assert.That(f.Hotel.SignalGuestResponseAnchorReached(other.GuestId, response.Id, response.ActionVersion,
                    GuestResponseAnchor.AssignedRoom).Success, Is.True);
            }
            Assert.That(other.Agent.ResponseActionId, Is.Null);
            Assert.That(other.Agent.InAssignedRoom, Is.True); Assert.That(other.Memory.ServicesRequested, Is.Zero);
        }

        [TestCase(GuestContactChannel.Phone)]
        [TestCase(GuestContactChannel.Reception)]
        [TestCase(GuestContactChannel.RoomConversation)]
        public void SeriousComplaintBypassesExhaustedSoftBudgetWithoutGrantingExtraServiceOrServiceMemory(GuestContactChannel channel)
        {
            var f = Create(budget: 1, selfObserve: 100); var other = AddSecondGuest(f);
            PrepareTwoPrivateColdCases(f, other);
            Assert.That(Reach(f, GuestResponseAnchor.RoomPhone).Success, Is.True);
            Advance(f, 25, 5);
            var response = f.Hotel.Services.Responses.Single(item => item.GuestId == other.GuestId);
            var request = f.Hotel.Services.FindCase(response.ServiceCaseId);
            Assert.That(f.Hotel.Incidents.Items.Single(item => item.GuestId == other.GuestId).Stage,
                Is.GreaterThanOrEqualTo(SituationStage.Complaint));
            if (channel == GuestContactChannel.RoomConversation)
                Assert.That(f.Hotel.DiscussRoomConcern(0, other.GuestId, response.Id).Success, Is.True);
            else
            {
                Assert.That(f.Hotel.DebugBeginGuestContact(other.GuestId, channel).Success, Is.True);
                Assert.That(f.Hotel.SignalGuestResponseAnchorReached(other.GuestId, response.Id, response.ActionVersion,
                    channel == GuestContactChannel.Phone ? GuestResponseAnchor.RoomPhone : GuestResponseAnchor.Reception).Success, Is.True);
                Assert.That(channel == GuestContactChannel.Phone ? f.Hotel.AnswerIncomingServiceCall(0, response.Id).Success :
                    f.Hotel.TalkToServiceGuest(0, other.GuestId, response.Id).Success, Is.True);
            }
            Assert.That(request.Status, Is.EqualTo(ServiceStatus.Escalated));
            Assert.That(request.BudgetCharged, Is.False); Assert.That(other.Memory.ServicesRequested, Is.Zero);
            Assert.That(other.Memory.NumberOfComplaints, Is.EqualTo(1));
            Assert.That(f.Hotel.RespondToService(0, request.Id, true).Success, Is.False);
            Assert.That(f.Hotel.Services.Cases.Count(item => item.BudgetCharged), Is.EqualTo(1));
        }

        [Test]
        public void WithdrawnUnattemptedSoftContactCanLaterReportASeriousContinuationWithoutAnotherServiceSlot()
        {
            var f = Create(budget: 1, selfObserve: 1); var other = AddSecondGuest(f);
            PrepareTwoPrivateColdCases(f, other);
            Assert.That(f.Hotel.DebugBeginGuestContact(other.GuestId, GuestContactChannel.Phone).Success, Is.True);
            Assert.That(Reach(f, GuestResponseAnchor.RoomPhone).Success, Is.True);
            Advance(f, .2f, 19.5f);
            var response = f.Hotel.Services.Responses.Single(item => item.GuestId == other.GuestId);
            Assert.That(response.Phase, Is.EqualTo(GuestResponsePhase.Cancelled));
            Advance(f, 25, 5);
            Assert.That(response.Phase, Is.EqualTo(GuestResponsePhase.Contacting));
            Assert.That(other.Agent.Activity, Is.EqualTo(GuestActivity.CallReception));
            Assert.That(f.Hotel.SignalGuestResponseAnchorReached(other.GuestId, response.Id, response.ActionVersion,
                GuestResponseAnchor.RoomPhone).Success, Is.True);
            Assert.That(f.Hotel.AnswerIncomingServiceCall(0, response.Id).Success, Is.True);
            Assert.That(other.Memory.NumberOfComplaints, Is.EqualTo(1)); Assert.That(other.Memory.ServicesRequested, Is.Zero);
            Assert.That(f.Hotel.Services.Cases.Count(item => item.BudgetCharged), Is.EqualTo(1));
            Assert.That(f.Hotel.Services.FindCase(response.ServiceCaseId).Status, Is.EqualTo(ServiceStatus.Expired));
        }
    }
}
