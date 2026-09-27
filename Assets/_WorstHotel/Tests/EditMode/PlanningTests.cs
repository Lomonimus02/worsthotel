using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace WorstHotel.Tests
{
    public sealed class PlanningTests
    {
        private static GuestProfile[] Profiles() => new[]
        {
            new GuestProfile(GuestKind.Budget, "Budget traveler", "Price-sensitive", 180, 0.85f, 18, 0.7f, 28, 90, 0.55f),
            new GuestProfile(GuestKind.ColdSensitive, "Cold-sensitive guest", "Needs warmth", 300, 1.05f, 20, 1.35f, 16, 65, 0.4f),
            new GuestProfile(GuestKind.Business, "Business guest", "High expectations", 450, 1, 19.5f, 1, 10, 45, 0.25f)
        };

        private static RoomState[] Rooms() => Enumerable.Range(101, 6)
            .Select(id => new RoomState(new RoomProfile(id, "Room " + id))).ToArray();

        private static PlanningSystem Plan(int day = 1) => new PlanningSystem(Rooms(),
            GuestSystem.GenerateApplications(day, Profiles()), new EconomySettings(), new BoilerSettings());

        [Test]
        public void OneGuestCannotTakeTwoRoomsAndOneRoomCannotTakeTwoGuests()
        {
            var plan = Plan();
            Assert.That(plan.Assign(0, plan.Applications[0].Id, 101, 180).Success, Is.True);
            Assert.That(plan.Assign(1, plan.Applications[0].Id, 102, 180).Success, Is.False);
            Assert.That(plan.Assign(1, plan.Applications[1].Id, 101, 180).Success, Is.False);
            Assert.That(plan.Assignments.Count, Is.EqualTo(1));
            Assert.That(plan.Remove(1, 101).Success, Is.True);
            Assert.That(plan.Assign(1, plan.Applications[0].Id, 102, 200).Success, Is.True);
            Assert.That(plan.Assignments.Single().ActorId, Is.EqualTo(1));
        }

        [TestCase(-1)]
        [TestCase(2)]
        [TestCase(int.MaxValue)]
        public void UnknownActorCannotMutateOrCommitPlan(int actorId)
        {
            var plan = Plan();
            Assert.That(plan.Assign(actorId, plan.Applications[0].Id, 101, 180).Success, Is.False);
            Assert.That(plan.Assign(0, plan.Applications[0].Id, 101, 180).Success, Is.True);
            Assert.That(plan.SetPrice(actorId, 101, 250).Success, Is.False);
            Assert.That(plan.Remove(actorId, 101).Success, Is.False);
            Assert.That(plan.TryCommit(actorId, out _, out _), Is.False);
            Assert.That(plan.IsCommitted, Is.False);
            Assert.That(plan.ProjectedGross, Is.EqualTo(180));
        }

        [TestCase(0)]
        [TestCase(110)]
        [TestCase(125)]
        [TestCase(525)]
        [TestCase(660)]
        public void InvalidPriceIsRejectedWithoutChangingAcceptedBookings(int price)
        {
            var plan = Plan();
            Assert.That(plan.Assign(0, plan.Applications[0].Id, 101, price).Success, Is.False);
            Assert.That(plan.Assignments, Is.Empty);
            Assert.That(plan.Assign(0, plan.Applications[0].Id, 101, 180).Success, Is.True);
            Assert.That(plan.SetPrice(1, 101, price).Success, Is.False);
            Assert.That(plan.ProjectedGross, Is.EqualTo(180));
        }

        [Test]
        public void ForecastReflectsAcceptedGuestDemandAndAgreedPrices()
        {
            var plan = Plan();
            for (int i = 0; i < plan.Applications.Count; i++)
                Assert.That(plan.Assign(i % 2, plan.Applications[i].Id, 101 + i, plan.Applications[i].ReferencePrice).Success, Is.True);
            Assert.That(plan.ProjectedGross, Is.EqualTo(1110));
            Assert.That(plan.ProjectedLoad, Is.EqualTo(3.75f).Within(0.0001f));
            Assert.That(plan.IsOverSafeLoad, Is.False);
            Assert.That(plan.SetPrice(1, 101, 300).Success, Is.True);
            Assert.That(plan.ProjectedGross, Is.EqualTo(1230));
            Assert.That(plan.ProjectedLoad, Is.EqualTo(3.75f).Within(0.0001f), "Charging more must not invent an extra guest's physical demand.");
            var busy = Plan(2);
            for (int i = 0; i < 6; i++) busy.Assign(i % 2, busy.Applications[i].Id, 101 + i, busy.Applications[i].ReferencePrice);
            Assert.That(busy.ProjectedLoad, Is.EqualTo(5.8f).Within(0.0001f));
            Assert.That(busy.ProjectedGross, Is.EqualTo(1860));
            Assert.That(busy.IsOverSafeLoad, Is.True);
            Assert.That(busy.CanCommit(out _), Is.True, "Overbooking is an informed player choice, not a forbidden action.");
        }

        [Test]
        public void CommitLocksCommandsAndReturnsIndependentAssignmentArray()
        {
            var plan = Plan();
            Assert.That(plan.TryCommit(0, out _, out _), Is.False, "An empty service cannot be committed.");
            plan.Assign(0, plan.Applications[0].Id, 101, 180);
            Assert.That(plan.TryCommit(1, out var committed, out var error), Is.True, error);
            Assert.That(plan.IsCommitted, Is.True);
            committed[0] = new BookingAssignment(101, "tampered", 650, 1);
            Assert.That(plan.Assignments.Single().Price, Is.EqualTo(180));
            Assert.That(plan.SetPrice(0, 101, 650).Success, Is.False);
            Assert.That(plan.Remove(0, 101).Success, Is.False);
            Assert.That(plan.Assign(0, plan.Applications[1].Id, 102, 180).Success, Is.False);
            Assert.That(plan.TryCommit(1, out var secondCommit, out _), Is.False);
            Assert.That(secondCommit, Is.Empty);
        }

        [Test]
        public void OccupancyIsValidatedAtAssignmentAndAgainAtCommit()
        {
            var rooms = Rooms();
            var applications = GuestSystem.GenerateApplications(1, Profiles());
            var plan = new PlanningSystem(rooms, applications, new EconomySettings(), new BoilerSettings());
            rooms[0].GuestId = "already-here";
            Assert.That(plan.Assign(0, applications[0].Id, 101, 180).Success, Is.False);
            Assert.That(plan.Assign(0, applications[0].Id, 102, 180).Success, Is.True);
            rooms[1].GuestId = "arrived-before-commit";
            Assert.That(plan.TryCommit(0, out _, out _), Is.False);
        }

        [Test]
        public void OffersAreDeterministicAndThirdDayBusinessReferenceIsVisible()
        {
            var profiles = Profiles();
            var day1 = GuestSystem.GenerateApplications(1, profiles);
            var day2 = GuestSystem.GenerateApplications(2, profiles);
            var day3 = GuestSystem.GenerateApplications(3, profiles);
            Assert.That(day1.Length, Is.EqualTo(4));
            Assert.That(day2.Length, Is.EqualTo(8));
            Assert.That(day3.Length, Is.EqualTo(8));
            Assert.That(day1.Count(a => a.Archetype.Kind == GuestKind.Budget), Is.EqualTo(2));
            Assert.That(day3.Where(a => a.Archetype.Kind == GuestKind.Business).All(a => a.ReferencePrice == 525), Is.True);
            Assert.That(profiles.Single(p => p.Kind == GuestKind.Business).ReferencePrice, Is.EqualTo(450), "Day offers must not mutate their archetype.");
            var repeat = GuestSystem.GenerateApplications(3, profiles);
            Assert.That(repeat.Select(a => a.Id + a.GuestName + a.ReferencePrice), Is.EqualTo(day3.Select(a => a.Id + a.GuestName + a.ReferencePrice)));
            Assert.That(day3.Select(a => a.Id).Distinct().Count(), Is.EqualTo(8));
        }

        [Test]
        public void RuntimeSnapshotsDoNotChangeWhenSourceAssetsAreEdited()
        {
            var guest = ScriptableObject.CreateInstance<GuestArchetypeDefinition>();
            var room = ScriptableObject.CreateInstance<RoomDefinition>();
            var boiler = ScriptableObject.CreateInstance<BoilerConfig>();
            try
            {
                var guestData = guest.ToData();
                var roomData = room.ToData();
                var boilerData = boiler.ToData();
                guest.referencePrice = 650; room.heatLoss = 12; boiler.safeLoad = 99;
                Assert.That(guestData.ReferencePrice, Is.EqualTo(180));
                Assert.That(roomData.HeatLoss, Is.EqualTo(0));
                Assert.That(boilerData.SafeLoad, Is.EqualTo(4.7f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(guest);
                UnityEngine.Object.DestroyImmediate(room);
                UnityEngine.Object.DestroyImmediate(boiler);
            }
        }

        [Test]
        public void InvalidConfigurationCannotCreateNaNForecastsOrZeroPriceSteps()
        {
            Assert.Throws<ArgumentException>(() => new BoilerSettings(safeLoad: float.NaN));
            Assert.Throws<ArgumentException>(() => new EconomySettings(priceStep: 0));
            Assert.Throws<ArgumentException>(() => new RoomProfile(101, "Invalid", temperature: float.PositiveInfinity));
        }
    }
}
