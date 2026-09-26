using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace WorstHotel.Tests
{
    public sealed class IncidentRetentionTests
    {
        sealed class Fixture
        {
            public readonly RoomState[] Rooms;
            public readonly GuestStay[] Guests;
            public readonly IncidentSystem Incidents;
            public readonly RequestSystem Requests;

            public Fixture(int count = 1)
            {
                var profiles = new[]
                {
                    new GuestProfile(GuestKind.Budget, "Budget", "", 180, .85f, 18, .7f, 28, 90, .55f),
                    new GuestProfile(GuestKind.ColdSensitive, "Cold", "", 300, 1.05f, 20, 1.35f, 16, 65, .4f),
                    new GuestProfile(GuestKind.Business, "Business", "", 450, 1, 19.5f, 1, 10, 45, .25f)
                };
                var roomProfiles = Enumerable.Range(101, 6).Select(id => new RoomProfile(id, "Room " + id)).ToArray();
                var settings = new SessionSettings(profiles, roomProfiles, new BoilerSettings(), new EconomySettings());
                Rooms = roomProfiles.Select(profile => new RoomState(profile)).ToArray();
                Guests = Enumerable.Range(0, count).Select(index => new GuestStay(
                    new BookingApplication("retention-" + index, "Guest " + index, profiles[0], 180), 101 + index, 180)).ToArray();
                Incidents = new IncidentSystem(settings);
                Requests = new RequestSystem(Incidents);
            }

            public void Tick(float seconds)
            {
                Incidents.Tick(Guests, Rooms, seconds);
                Requests.Tick();
            }

            public void CreateColdComplaints()
            {
                foreach (var guest in Guests) Rooms.Single(room => room.Profile.Id == guest.RoomId).Temperature = 17;
                Tick(15);
                Assert.That(Requests.ActiveCount, Is.EqualTo(Guests.Length));
            }

            public Action AssertNoEventsAfterThisPoint()
            {
                int events = 0;
                Incidents.OnIncidentStarted += _ => events++;
                Incidents.OnIncidentResolved += _ => events++;
                Incidents.OnSituationChanged += _ => events++;
                Requests.OnRequestCreated += _ => events++;
                Requests.OnRequestResolved += _ => events++;
                return () => Assert.That(events, Is.Zero, "Pruning must not simulate an outcome or notify staff.");
            }
        }

        // These are internal lifecycle hooks, intentionally not player commands. Reflection
        // exercises their contract without exposing mutation APIs to external game callers.
        static HotelIncident[] All(IncidentSystem system) => ((IEnumerable<HotelIncident>)typeof(IncidentSystem)
            .GetProperty("AllIncidents", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(system)).ToArray();

        static void Prune(object system, ISet<string> owners, ISet<string> incidentIds) => system.GetType()
            .GetMethod("PruneCompletedStays", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(system, new object[] { owners, incidentIds });

        static HashSet<string> Set(params string[] ids) => new HashSet<string>(ids);

        static void PruneBoth(Fixture fixture, ISet<string> owners, ISet<string> incidentIds, bool requestsFirst)
        {
            if (requestsFirst) Prune(fixture.Requests, owners, incidentIds);
            Prune(fixture.Incidents, owners, incidentIds);
            if (!requestsFirst) Prune(fixture.Requests, owners, incidentIds);
        }

        [Test]
        public void NeverOccurredRecordsAreIncludedInRetentionAndRetiredWithoutNotifications()
        {
            var fixture = new Fixture(2);
            fixture.Tick(1);
            Assert.That(fixture.Incidents.Items, Is.Empty);
            Assert.That(All(fixture.Incidents).Length, Is.EqualTo(8), "The public list conceals four never-triggered legacy causes per guest.");
            var retained = All(fixture.Incidents).Where(incident => incident.GuestId == fixture.Guests[1].GuestId).ToArray();
            var assertQuiet = fixture.AssertNoEventsAfterThisPoint();

            PruneBoth(fixture, Set(fixture.Guests[1].GuestId), Set(), true);

            Assert.That(All(fixture.Incidents), Is.EquivalentTo(retained));
            Assert.That(fixture.Incidents.Items, Is.Empty);
            Assert.That(fixture.Requests.Items, Is.Empty);
            assertQuiet();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CompletedHistoryAndCompensationAreRetiredQuietlyInEitherSubsystemOrder(bool requestsFirst)
        {
            var fixture = new Fixture();
            fixture.CreateColdComplaints();
            fixture.Requests.SetCompensated(fixture.Guests[0].GuestId, true);
            fixture.Rooms[0].Temperature = 22;
            fixture.Tick(10);
            var incident = fixture.Incidents.Items.Single();
            var request = fixture.Requests.Items.Single();
            float age = incident.Age;
            string cause = incident.MeasuredCause;
            var assertQuiet = fixture.AssertNoEventsAfterThisPoint();

            PruneBoth(fixture, Set(), Set(), requestsFirst);
            PruneBoth(fixture, Set(), Set(), requestsFirst);

            Assert.That(All(fixture.Incidents), Is.Empty);
            Assert.That(fixture.Requests.Items, Is.Empty);
            Assert.That(incident.Resolved && request.Resolved, Is.True);
            Assert.That(incident.Age, Is.EqualTo(age));
            Assert.That(incident.MeasuredCause, Is.EqualTo(cause));
            Assert.That(request.Compensated, Is.True, "Retired objects must not be rewritten as another outcome.");
            assertQuiet();

            // Reusing this unit-fixture ID probes the private compensation index, not the
            // production booking generator (which assigns a fresh identity to every stay).
            fixture.CreateColdComplaints();
            Assert.That(fixture.Requests.Items.Single().Compensated, Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void HistoryOwnerExplicitIncidentAndActiveIncidentPinsPreserveObjectIdentity(bool requestsFirst)
        {
            var fixture = new Fixture(4);
            fixture.CreateColdComplaints();
            foreach (var guest in fixture.Guests) fixture.Requests.SetCompensated(guest.GuestId, true);
            fixture.Rooms[0].Temperature = fixture.Rooms[1].Temperature = fixture.Rooms[3].Temperature = 22;
            fixture.Tick(10);
            var original = fixture.Requests.Items.ToArray();
            var explicitPin = fixture.Incidents.Items.Single(incident => incident.GuestId == fixture.Guests[1].GuestId);
            var active = fixture.Incidents.Items.Single(incident => incident.GuestId == fixture.Guests[2].GuestId);
            var assertQuiet = fixture.AssertNoEventsAfterThisPoint();

            PruneBoth(fixture, Set(fixture.Guests[0].GuestId), Set(explicitPin.Id), requestsFirst);

            Assert.That(All(fixture.Incidents).Length, Is.EqualTo(6), "Four owner records, one explicit reference, and one active cause survive.");
            Assert.That(fixture.Requests.Items, Is.EquivalentTo(original.Take(3)));
            Assert.That(fixture.Incidents.Items.Single(incident => incident.Id == active.Id), Is.SameAs(active));
            Assert.That(active.Active, Is.True);
            Assert.That(fixture.Requests.Items.All(request => request.Compensated), Is.True);
            assertQuiet();
        }

        [Test]
        public void OrphanProjectionAndCompensationAreRemovedWithoutResolvingTheirDetachedSource()
        {
            var fixture = new Fixture();
            fixture.CreateColdComplaints();
            fixture.Requests.SetCompensated(fixture.Guests[0].GuestId, true);
            var detached = fixture.Incidents.Items.Single();
            fixture.Incidents.Clear();
            var assertQuiet = fixture.AssertNoEventsAfterThisPoint();

            Prune(fixture.Requests, Set(), Set());

            Assert.That(fixture.Requests.Items, Is.Empty);
            Assert.That(detached.Active, Is.True, "An orphan's removal must not simulate recovery of its old source.");
            assertQuiet();
            fixture.CreateColdComplaints();
            Assert.That(fixture.Requests.Items.Single().Compensated, Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void InvalidRetentionArgumentsRejectBeforeMutatingEitherDictionary(bool missingOwners)
        {
            var fixture = new Fixture();
            fixture.CreateColdComplaints();
            var original = All(fixture.Incidents);
            var request = fixture.Requests.Items.Single();
            var assertQuiet = fixture.AssertNoEventsAfterThisPoint();
            foreach (object system in new object[] { fixture.Incidents, fixture.Requests })
            {
                var error = Assert.Throws<TargetInvocationException>(() => Prune(system,
                    missingOwners ? null : Set(), missingOwners ? Set() : null));
                Assert.That(error.InnerException, Is.TypeOf<ArgumentNullException>());
            }
            Assert.That(All(fixture.Incidents), Is.EquivalentTo(original));
            Assert.That(fixture.Requests.Items.Single(), Is.SameAs(request));
            assertQuiet();
        }

        [Test]
        public void MirrorLifecycleHooksCannotPruneReplicatedHistories()
        {
            var fixture = new Fixture();
            fixture.CreateColdComplaints();
            fixture.Rooms[0].Temperature = 22;
            fixture.Tick(10);
            var original = All(fixture.Incidents);
            var request = fixture.Requests.Items.Single();
            // Subsystems receive this authority flag from HotelSimulation.EnableReadOnlyMirror.
            foreach (object system in new object[] { fixture.Incidents, fixture.Requests })
                system.GetType().GetField("ReadOnlyMirror", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(system, true);
            var assertQuiet = fixture.AssertNoEventsAfterThisPoint();

            PruneBoth(fixture, Set(), Set(), true);

            Assert.That(All(fixture.Incidents), Is.EquivalentTo(original));
            Assert.That(fixture.Requests.Items.Single(), Is.SameAs(request));
            assertQuiet();
        }
    }
}
