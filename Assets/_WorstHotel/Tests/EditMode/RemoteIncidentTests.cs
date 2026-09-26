using System;
using System.Linq;
using NUnit.Framework;

namespace WorstHotel.Tests
{
    public sealed class RemoteIncidentTests
    {
        public enum Problem { Cold, Noise, Power, Lamp }

        sealed class Fixture
        {
            public HotelSimulation Hotel;
            public RoomState[] Rooms;
            public string SourceId, AffectedId;
            public bool Cold;
            public GuestStay Source => Hotel.Guests.Single(guest => guest.GuestId == SourceId);
            public GuestStay Affected => Hotel.Guests.Single(guest => guest.GuestId == AffectedId);
            public RoomState AffectedRoom => Rooms.Single(room => room.Profile.Id == Affected.RoomId);
        }

        static void Require(CommandResult result) => Assert.That(result.Success, Is.True, result.Message);

        static Fixture Create(bool continuous = true, bool items = false)
        {
            var comfort = new NeedProfile(21, 25, 18, 28, .08f, .2f, 45);
            var profiles = new[]
            {
                new GuestProfile(GuestKind.Budget, "Budget", "", 180, .85f, 18, .7f, 28, 90, .55f, needs: comfort),
                new GuestProfile(GuestKind.ColdSensitive, "Cold", "", 300, 1.05f, 20, 1.35f, 16, 65, .4f, needs: comfort),
                new GuestProfile(GuestKind.Business, "Business", "", 450, 1, 19.5f, 1, 10, 45, .25f, needs: comfort)
            };
            var settings = new SessionSettings(profiles, Enumerable.Range(101, 6).Select(id =>
                new RoomProfile(id, "Room " + id, noise: 0, temperature: 22.5f)),
                new BoilerSettings(safeLoad: 100, baseWearPerMinute: 0, overloadWearPerMinute: 0), new EconomySettings(), serviceSeconds: 600);
            var rooms = settings.Rooms.Select(profile => new RoomState(profile)).ToArray();
            var hotel = new HotelSimulation(settings, rooms,
                new LivingHotelSettings(firstArrivalSeconds: .2f, arrivalSpacingSeconds: .2f, arrivalJitterSeconds: 0,
                    firstActivityDelay: 10000, quietDurationMin: 10000, quietDurationMax: 10000,
                    awayDurationMin: 10000, awayDurationMax: 10000),
                new NeedSettings(buildupPerSecond: .5f, complaintExposureSeconds: 2, escalatedExposureSeconds: 6,
                    criticalExposureSeconds: 12, recoverySeconds: 2, reopenCooldownSeconds: 2),
                services: items ? new GuestServiceSettings(eligibility: 0) : null,
                infrastructure: new RoomInfrastructureSettings(lampWearPerSecond: 0),
                operations: continuous ? new OperationsSettings() : null);
            var fixture = new Fixture { Hotel = hotel, Rooms = rooms };
            if (continuous)
            {
                Require(hotel.StartOperations());
                var sourceOffer = hotel.BookingOffers.First(offer => offer.ArrivalDay == 1 && offer.Application.Archetype.Kind == GuestKind.Budget);
                var affectedOffer = hotel.BookingOffers.First(offer => offer.ArrivalDay == 1 && offer.Application.Archetype.Kind == GuestKind.Business);
                fixture.SourceId = sourceOffer.Id; fixture.AffectedId = affectedOffer.Id;
                Require(hotel.AcceptBooking(0, sourceOffer.Id, 103, sourceOffer.Application.ReferencePrice));
                Require(hotel.AcceptBooking(0, affectedOffer.Id, 102, affectedOffer.Application.ReferencePrice));
                Advance(fixture, Math.Max(sourceOffer.ArrivalAt, affectedOffer.ArrivalAt) + 1);
            }
            else
            {
                fixture.SourceId = "source"; fixture.AffectedId = "affected";
                var offers = new[] { new BookingApplication(fixture.SourceId, "Source", profiles[0], 180),
                    new BookingApplication(fixture.AffectedId, "Affected", profiles[2], 450) };
                Require(hotel.StartShift(new[] { new BookingAssignment(103, offers[0].Id, 180, 0),
                    new BookingAssignment(102, offers[1].Id, 450, 0) }, offers));
                Advance(fixture, 1);
            }
            return fixture;
        }

        static void Advance(Fixture fixture, float seconds)
        {
            float target = fixture.Hotel.Elapsed + seconds;
            while (fixture.Hotel.Elapsed < target)
            {
                foreach (var room in fixture.Rooms)
                    room.Temperature = fixture.Cold && room.Profile.Id == 102 ? 5 : 22.5f;
                fixture.Hotel.Tick(Math.Min(.25f, target - fixture.Hotel.Elapsed));
                // Only initial scene arrival is adapted here. Every outing and return below
                // explicitly distinguishes its route start from its actual arrival callback.
                foreach (var guest in fixture.Hotel.Guests.Where(guest => guest.Agent.State == GuestAgentState.Arriving).ToArray())
                {
                    Require(fixture.Hotel.SignalGuestReachedReception(guest.GuestId));
                    Require(ModelKeyHandoff.CheckIn(fixture.Hotel, 0, guest.GuestId));
                    Require(fixture.Hotel.SignalGuestReachedRoom(guest.GuestId));
                    Require(fixture.Hotel.ForceActivity(guest.GuestId, GuestActivity.QuietRest));
                }
            }
        }

        static HotelIncident StartProblem(Fixture fixture, Problem problem)
        {
            switch (problem)
            {
                case Problem.Cold: fixture.Cold = true; break;
                case Problem.Noise: Require(fixture.Hotel.ForceActivity(fixture.SourceId, GuestActivity.LoudRoom)); break;
                case Problem.Power: Require(fixture.Hotel.Electrical.ForceTrip("A")); break;
                case Problem.Lamp: Require(fixture.Hotel.BreakRoomLamp(102)); break;
            }
            Advance(fixture, 3.5f);
            var incident = fixture.Hotel.Incidents.Items.Single(item => item.GuestId == fixture.AffectedId);
            Assert.That(incident.Active, Is.True);
            Assert.That(incident.Stage, Is.EqualTo(SituationStage.Complaint));
            return incident;
        }

        static void FixProblem(Fixture fixture, Problem problem)
        {
            switch (problem)
            {
                // A controlled measured temperature restoration isolates the incident policy
                // from the separately tested boiler/room thermal response.
                case Problem.Cold: fixture.Cold = false; Require(fixture.Hotel.SetRoomTemperature(102, 22.5f)); break;
                case Problem.Noise: Require(fixture.Hotel.RequestQuiet(0, fixture.SourceId)); break;
                case Problem.Power: Require(fixture.Hotel.Electrical.ResetCircuit(0, "A")); break;
                case Problem.Lamp:
                    var bulb = fixture.Hotel.Services.Items.First(item => item.Kind == ServiceItemKind.ReplacementBulb && item.Location == ServiceItemLocation.OnShelf);
                    Require(fixture.Hotel.TakeServiceItem(0, bulb.Id));
                    Require(fixture.Hotel.ReplaceRoomBulb(0, 102));
                    break;
            }
        }

        [TestCase(Problem.Cold)]
        [TestCase(Problem.Noise)]
        [TestCase(Problem.Power)]
        [TestCase(Problem.Lamp)]
        public void LeavingDoesNotResolveOrEscalateFactualRemoteProblemWithoutOptionalServiceCase(Problem problem)
        {
            var fixture = Create(); var hotel = fixture.Hotel;
            var incident = StartProblem(fixture, problem);
            Assert.That(hotel.Services, Is.Null, "Remote classification must not depend on an optional request card.");
            var cause = incident.Cause;
            float exposure = incident.ExposureSeconds, dissatisfaction = incident.Dissatisfaction, age = incident.Age;
            float temperatureNeed = fixture.Affected.Needs.Temperature.ExposureSeconds;
            float noiseNeed = fixture.Affected.Needs.Noise.ExposureSeconds;
            float conditionNeed = fixture.Affected.Needs.RoomCondition.ExposureSeconds;
            int history = incident.History.Count, resolvedEvents = 0, complaints = fixture.Affected.Memory.NumberOfComplaints;
            hotel.Incidents.OnIncidentResolved += item => { if (item.Id == incident.Id) resolvedEvents++; };

            Require(hotel.ForceLeaveRoom(fixture.AffectedId));
            Advance(fixture, 3);
            Assert.That(fixture.Affected.Agent.State, Is.EqualTo(GuestAgentState.LeavingRoom));
            Require(hotel.SignalGuestLeftRoom(fixture.AffectedId));
            Advance(fixture, 5);
            Require(hotel.ForceReturnRoom(fixture.AffectedId));
            Advance(fixture, 3);

            Assert.That(fixture.Affected.Agent.State, Is.EqualTo(GuestAgentState.ReturningToRoom));
            Assert.That(fixture.Affected.Perception.InAssignedRoom, Is.False);
            Assert.That(incident.Active, Is.True);
            Assert.That(incident.Resolved, Is.False);
            Assert.That(incident.Stage, Is.EqualTo(SituationStage.Complaint));
            Assert.That(incident.Cause, Is.SameAs(cause));
            Assert.That(incident.ExposureSeconds, Is.EqualTo(exposure));
            Assert.That(incident.Dissatisfaction, Is.EqualTo(dissatisfaction));
            Assert.That(incident.Age, Is.EqualTo(age));
            Assert.That(incident.History.Count, Is.EqualTo(history));
            Assert.That(fixture.Affected.Needs.Temperature.ExposureSeconds, Is.EqualTo(temperatureNeed));
            Assert.That(fixture.Affected.Needs.Noise.ExposureSeconds, Is.EqualTo(noiseNeed));
            Assert.That(fixture.Affected.Needs.RoomCondition.ExposureSeconds, Is.EqualTo(conditionNeed));
            Assert.That(fixture.Affected.Memory.NumberOfComplaints, Is.EqualTo(complaints));
            Assert.That(fixture.Affected.Memory.ProblemsIgnored, Is.Zero);
            Assert.That(fixture.Affected.Memory.ProblemsResolvedSuccessfully, Is.Zero);
            Assert.That(resolvedEvents, Is.Zero);
            Assert.That(hotel.Requests.Items.Single(item => item.Id == incident.Id).Resolved, Is.False);
        }

        [TestCase(Problem.Cold)]
        [TestCase(Problem.Noise)]
        [TestCase(Problem.Power)]
        [TestCase(Problem.Lamp)]
        public void ActualRepairWhileAwayWaitsForPhysicalReturnAndFreshRecovery(Problem problem)
        {
            var fixture = Create(items: true); var hotel = fixture.Hotel;
            var incident = StartProblem(fixture, problem);
            Require(hotel.ForceLeaveRoom(fixture.AffectedId));
            Require(hotel.SignalGuestLeftRoom(fixture.AffectedId));
            Advance(fixture, 2);
            FixProblem(fixture, problem);
            Advance(fixture, 4);
            Assert.That(incident.Active, Is.True);
            Assert.That(fixture.Affected.Memory.ProblemsResolvedSuccessfully, Is.Zero);
            Require(hotel.ForceReturnRoom(fixture.AffectedId));
            Advance(fixture, 3);
            Assert.That(incident.Resolved, Is.False, "Beginning a return route is not room perception.");

            Require(hotel.SignalGuestReturnedRoom(fixture.AffectedId));
            Assert.That(incident.Resolved, Is.False, "Arrival alone does not bypass the recovery dwell.");
            Advance(fixture, .5f);
            Assert.That(fixture.Affected.Perception.InAssignedRoom, Is.True);
            Assert.That(incident.Resolved, Is.False);
            Advance(fixture, 2);

            Assert.That(incident.Active, Is.False);
            Assert.That(incident.Resolved, Is.True);
            Assert.That(incident.EpisodeCount, Is.EqualTo(1));
            Assert.That(hotel.Requests.Items.Single(item => item.Id == incident.Id).Resolved, Is.True);
            Assert.That(fixture.Affected.Memory.ProblemsResolvedSuccessfully, Is.EqualTo(1));
        }

        [Test]
        public void ReturningToUnchangedColdResumesSameEpisodeWithoutCountingAbsentTime()
        {
            var fixture = Create(); var hotel = fixture.Hotel;
            var incident = StartProblem(fixture, Problem.Cold);
            float exposure = incident.ExposureSeconds;
            Require(hotel.ForceLeaveRoom(fixture.AffectedId));
            Require(hotel.SignalGuestLeftRoom(fixture.AffectedId));
            Advance(fixture, 20);
            Require(hotel.ForceReturnRoom(fixture.AffectedId));
            Advance(fixture, 2);
            Require(hotel.SignalGuestReturnedRoom(fixture.AffectedId));
            Advance(fixture, 3);

            Assert.That(hotel.Incidents.Items.Single(item => item.Id == incident.Id), Is.SameAs(incident));
            Assert.That(incident.ExposureSeconds, Is.EqualTo(exposure + 3).Within(.001f));
            Assert.That(incident.Stage, Is.EqualTo(SituationStage.Escalated));
            Assert.That(incident.EpisodeCount, Is.EqualTo(1));
            Assert.That(fixture.Affected.Memory.NumberOfComplaints, Is.EqualTo(1));
            Assert.That(fixture.Affected.Memory.ProblemsResolvedSuccessfully, Is.Zero);
        }

        [Test]
        public void CheckoutStillEndsSuspendedRemoteEpisodeAndRecordsAnUnresolvedKnownComplaintOnce()
        {
            var fixture = Create(); var hotel = fixture.Hotel;
            var incident = StartProblem(fixture, Problem.Cold);
            Require(hotel.ForceLeaveRoom(fixture.AffectedId));
            Require(hotel.SignalGuestLeftRoom(fixture.AffectedId));
            Advance(fixture, fixture.Affected.Agent.CheckoutTime - hotel.Elapsed + 1);

            Assert.That(fixture.Affected.Agent.State, Is.EqualTo(GuestAgentState.Left));
            Assert.That(incident.Active, Is.False);
            Assert.That(incident.Resolved, Is.True);
            Assert.That(fixture.Affected.ReceiptPosted, Is.True);
            Assert.That(fixture.Affected.Memory.ProblemsIgnored, Is.EqualTo(1));
            Assert.That(fixture.Affected.Memory.ProblemsResolvedSuccessfully, Is.Zero);
            Advance(fixture, 2);
            Assert.That(fixture.Affected.Memory.ProblemsIgnored, Is.EqualTo(1));
        }

        [Test]
        public void ClosedEpisodeCooldownStillElapsesWhileGuestIsAway()
        {
            var fixture = Create(); var hotel = fixture.Hotel;
            var incident = StartProblem(fixture, Problem.Cold);
            FixProblem(fixture, Problem.Cold);
            Advance(fixture, 2.25f);
            Assert.That(incident.Resolved, Is.True);
            Require(hotel.ForceLeaveRoom(fixture.AffectedId));
            Require(hotel.SignalGuestLeftRoom(fixture.AffectedId));
            Advance(fixture, 4);
            fixture.Cold = true;
            Require(hotel.ForceReturnRoom(fixture.AffectedId));
            Require(hotel.SignalGuestReturnedRoom(fixture.AffectedId));
            Advance(fixture, .25f);

            Assert.That(incident.Active, Is.True);
            Assert.That(incident.EpisodeCount, Is.EqualTo(2));
            Assert.That(incident.Stage, Is.EqualTo(SituationStage.Observed));
        }

        [Test]
        public void ExplicitLegacyShiftKeepsItsExistingAwayRecoveryPolicy()
        {
            var fixture = Create(continuous: false); var hotel = fixture.Hotel;
            var incident = StartProblem(fixture, Problem.Cold);
            Require(hotel.ForceLeaveRoom(fixture.AffectedId));
            Require(hotel.SignalGuestLeftRoom(fixture.AffectedId));
            Advance(fixture, 3);

            Assert.That(incident.Resolved, Is.True);
            Assert.That(fixture.Affected.Memory.ProblemsResolvedSuccessfully, Is.Zero);
        }
    }
}
