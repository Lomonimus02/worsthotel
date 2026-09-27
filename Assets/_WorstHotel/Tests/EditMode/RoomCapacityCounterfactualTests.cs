using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;

namespace WorstHotel.Tests
{
    /// <summary>Production room/profile tuning, with explicitly headless arrivals, key handoffs and quiet anchor acknowledgements.</summary>
    public sealed class RoomCapacityCounterfactualTests
    {
        static readonly GuestKind[] Mixed =
        {
            GuestKind.Budget, GuestKind.ColdSensitive, GuestKind.Business,
            GuestKind.Budget, GuestKind.ColdSensitive, GuestKind.Business
        };

        static void Require(CommandResult result) => Assert.That(result.Success, Is.True, result.Message);

        static HotelSimulation CheckedInQuietHotel(GuestKind[] kinds)
        {
            var asset = AssetDatabase.LoadAssetAtPath<SessionConfig>("Assets/_WorstHotel/ScriptableObjects/PrototypeSession.asset");
            Assert.That(asset, Is.Not.Null);
            Assert.That(asset.living, Is.Not.Null);
            Assert.That(asset.infrastructure, Is.Not.Null);
            var settings = asset.ToData();
            var rooms = settings.Rooms.OrderBy(room => room.Id).Select(room => new RoomState(room)).ToArray();
            var hotel = new HotelSimulation(settings, rooms, asset.living.ToData(), asset.needs.ToData(),
                asset.noise.ToData(), asset.heater.ToData(), asset.electricity.ToData(), asset.housekeeping.ToData(),
                asset.services.ToData(), asset.infrastructure.ToData(), asset.OperationsData());
            Assert.That(hotel.ContinuousOperations, Is.True, "This test measures the authored continuous capacity model.");
            Require(hotel.StartOperations());
            for (int index = 0; index < kinds.Length; index++)
                Require(hotel.DebugSpawnGuest(kinds[index], rooms[index].Profile.Id));
            // The diagnostic walk-ins still create dated reservations. They do not exist until due.
            Assert.That(hotel.Guests, Is.Empty);
            hotel.Tick(1.25f);
            Assert.That(hotel.Guests.Count, Is.EqualTo(kinds.Length));
            foreach (var guest in hotel.Guests)
            {
                Require(hotel.SignalGuestReachedReception(guest.GuestId));
                Require(ModelKeyHandoff.CheckIn(hotel, 0, guest.GuestId));
                Require(hotel.SignalGuestReachedRoom(guest.GuestId));
                Require(hotel.RegisterGuestPhysicalStaging(guest.GuestId));
                Require(hotel.SignalGuestActivityReady(guest.GuestId, GuestAgentState.InRoom, GuestActivity.QuietRest));
                Assert.That(rooms.Single(room => room.Profile.Id == guest.RoomId).GuestId, Is.EqualTo(guest.GuestId));
                Assert.That(hotel.Keys.Find(guest.RoomId).GuestId, Is.EqualTo(guest.GuestId));
                Assert.That(guest.Agent.ActivityStaged, Is.True);
            }
            Assert.That(hotel.HeatingDemands.All(row => row.HotWater == 0), Is.True);
            Assert.That(hotel.HeatingDemands.Count(row => row.GuestId != null), Is.EqualTo(kinds.Length));
            Assert.That(hotel.Boiler.Load, Is.EqualTo(hotel.HeatingDemands.Sum(row => row.Total)).Within(.00001f));
            return hotel;
        }

        static void Record(string label, HotelSimulation hotel)
        {
            TestContext.WriteLine(FormattableString.Invariant($"{label}: guests={hotel.Guests.Count}, space={hotel.HeatingDemands.Sum(row => row.SpaceHeating):F6}, water={hotel.HeatingDemands.Sum(row => row.HotWater):F6}, demand={hotel.Boiler.Load:F6}, capacity={hotel.Boiler.EffectiveCapacity:F6}, ratio={hotel.Boiler.LoadRatio:F6}, condition={hotel.Boiler.Condition:F6}"));
        }

        [Test]
        public void ThreeToSixRealMixedStaysRaiseMeasuredDemandAndClosingValvesReducesItWithoutRemovingGuests()
        {
            var hotels = Enumerable.Range(3, 4).Select(count => CheckedInQuietHotel(Mixed.Take(count).ToArray())).ToArray();
            for (int index = 0; index < hotels.Length; index++)
            {
                Record("mixed quiet baseline", hotels[index]);
                Assert.That(hotels[index].Boiler.Condition, Is.EqualTo(hotels[0].Boiler.Condition),
                    "All fixtures have the same elapsed empty-building setup; occupancy is the changed cause.");
                if (index == 0) continue;
                Assert.That(hotels[index].Boiler.Load, Is.GreaterThan(hotels[index - 1].Boiler.Load));
                Assert.That(hotels[index].Boiler.LoadRatio, Is.GreaterThan(hotels[index - 1].Boiler.LoadRatio));
            }
            Assert.That(hotels[1].Boiler.LoadRatio, Is.LessThan(1), "Four ordinary mixed stays fit the authored capacity at the starting condition.");
            Assert.That(hotels[2].Boiler.CapacityBand, Is.EqualTo(CapacityBand.Strained),
                "Five mixed stays strain the authored boiler before demand exceeds its effective capacity.");
            Assert.That(hotels[2].Boiler.LoadRatio, Is.LessThan(1));
            Assert.That(hotels[3].Boiler.LoadRatio, Is.GreaterThan(1), "Six mixed stays create real pressure; this is not a forced failure assertion.");
            float vacantSixthRoom = hotels[2].HeatingDemands.Single(row => row.RoomId == 106).Total;
            float ownedSixthRoom = hotels[3].HeatingDemands.Single(row => row.RoomId == 106).Total;
            Assert.That(hotels[3].Boiler.Load - hotels[2].Boiler.Load,
                Is.EqualTo(ownedSixthRoom - vacantSixthRoom).Within(.00001f),
                "The sixth actual checked-in room, not a changed capacity or hidden multiplier, accounts for this growth.");

            var six = hotels[3];
            var before = six.HeatingDemands.ToArray();
            var guestIds = six.Guests.Select(guest => guest.GuestId).ToArray();
            float condition = six.Boiler.Condition;
            foreach (int roomId in new[] { 102, 105 }) Require(six.SetRadiatorSetting(0, roomId, 0));
            float removed = before.Where(row => row.RoomId == 102 || row.RoomId == 105).Sum(row => row.SpaceHeating);
            Assert.That(six.Boiler.Load, Is.EqualTo(before.Sum(row => row.Total) - removed).Within(.00001f));
            Assert.That(six.Boiler.LoadRatio, Is.LessThan(before.Sum(row => row.Total) / six.Boiler.EffectiveCapacity));
            Assert.That(six.Boiler.Condition, Is.EqualTo(condition));
            Assert.That(six.Guests.Select(guest => guest.GuestId), Is.EqualTo(guestIds));
            Assert.That(six.Guests.All(guest => guest.Agent.CheckedIn && guest.Agent.InAssignedRoom && guest.Agent.ActivityStaged), Is.True);
            Assert.That(six.HeatingDemands.Count(row => row.GuestId != null), Is.EqualTo(6));
            foreach (var row in before.Where(row => row.RoomId != 102 && row.RoomId != 105))
                Assert.That(six.HeatingDemands.Single(current => current.RoomId == row.RoomId), Is.EqualTo(row));
            Record("same six after closing radiators 102 and 105", six);
        }

        [Test]
        public void SixColdSensitiveGuestsDemandMoreThanSixBudgetGuestsAtIdenticalValvesRoomsAndCondition()
        {
            var budget = CheckedInQuietHotel(Enumerable.Repeat(GuestKind.Budget, 6).ToArray());
            var cold = CheckedInQuietHotel(Enumerable.Repeat(GuestKind.ColdSensitive, 6).ToArray());
            Assert.That(cold.Boiler.Condition, Is.EqualTo(budget.Boiler.Condition));
            Assert.That(cold.Boiler.EffectiveCapacity, Is.EqualTo(budget.Boiler.EffectiveCapacity));
            Assert.That(cold.Elapsed, Is.EqualTo(budget.Elapsed));
            Assert.That(cold.Guests.Count, Is.EqualTo(budget.Guests.Count));
            foreach (var row in cold.HeatingDemands)
            {
                var comparison = budget.HeatingDemands.Single(other => other.RoomId == row.RoomId);
                Assert.That(row.SpaceHeating, Is.GreaterThan(comparison.SpaceHeating));
                Assert.That(row.HotWater, Is.Zero);
            }
            Assert.That(cold.Boiler.Load, Is.GreaterThan(budget.Boiler.Load));
            Assert.That(cold.Boiler.LoadRatio, Is.GreaterThan(budget.Boiler.LoadRatio));
            Record("six budget quiet", budget);
            Record("six cold-sensitive quiet", cold);
        }
    }
}
