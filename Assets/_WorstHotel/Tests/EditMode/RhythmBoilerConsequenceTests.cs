using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace WorstHotel.Tests
{
    /// <summary>Production thermal/capacity/profile settings and actual owned-room consumers.
    /// Reservations, keys, room arrivals and response anchors are explicitly headless adapters;
    /// these tests do not claim Unity body movement or human staff pacing.</summary>
    public sealed class RhythmBoilerConsequenceTests
    {
        static readonly GuestKind[] Mixed =
        {
            GuestKind.Budget, GuestKind.ColdSensitive, GuestKind.Business,
            GuestKind.Budget, GuestKind.ColdSensitive, GuestKind.Business
        };

        sealed class Fixture
        {
            public HotelSimulation Hotel;
            public RoomState[] Rooms;
            public SessionSettings Settings;
            public GuestStay ColdGuest => Hotel.Guests.Single(guest => guest.RoomId == 102);
            public RoomState WeakRoom => Rooms.Single(room => room.Profile.Id == 102);
        }

        static void Require(CommandResult result) => Assert.That(result.Success, Is.True, result.Message);

        static Fixture Create(int occupants, bool allowNaturalSelfResponse = false)
        {
            var asset = AssetDatabase.LoadAssetAtPath<SessionConfig>(
                "Assets/_WorstHotel/ScriptableObjects/PrototypeSession.asset");
            Assert.That(asset, Is.Not.Null);
            Assert.That(asset.continuousOperations && asset.services.naturalCommunicationEnabled, Is.True);
            var living = UnityEngine.Object.Instantiate(asset.living);
            var services = UnityEngine.Object.Instantiate(asset.services);
            try
            {
                // LABELLED constant-workload isolation, not a natural day/schedule claim:
                // postpone discretionary TV/showers/outings. The paired thermal experiments
                // also postpone self-help so only room ownership differs. The dedicated
                // response test keeps every production need/contact/self-help timer intact.
                living.firstActivityDelay = 1000;
                if (!allowNaturalSelfResponse) services.selfResponseObserveSeconds = 1000;
                var settings = asset.ToData();
                var rooms = settings.Rooms.OrderBy(room => room.Id).Select(room => new RoomState(room)).ToArray();
                var hotel = new HotelSimulation(settings, rooms, living.ToData(), asset.needs.ToData(),
                    asset.noise.ToData(), asset.heater.ToData(), asset.electricity.ToData(), asset.housekeeping.ToData(),
                    services.ToData(), asset.infrastructure.ToData(), ManualBookingFixture.Operations(asset));
                Require(hotel.StartOperations());
                for (int index = 0; index < occupants; index++)
                    Require(hotel.DebugSpawnGuest(Mixed[index], 101 + index));
                Assert.That(hotel.Guests, Is.Empty, "Diagnostic reservations still materialize only at their dated arrival.");
                hotel.Tick(1.25f);
                foreach (var guest in hotel.Guests)
                {
                    // Explicit model adapters. Physical production presentation owns these callbacks in a player.
                    Require(hotel.SignalGuestReachedReception(guest.GuestId));
                    Require(ModelKeyHandoff.CheckIn(hotel, 0, guest.GuestId));
                    Require(hotel.SignalGuestReachedRoom(guest.GuestId));
                    Require(hotel.RegisterGuestPhysicalStaging(guest.GuestId));
                    Require(hotel.SignalGuestActivityReady(guest.GuestId, GuestAgentState.InRoom, GuestActivity.QuietRest));
                    Assert.That(rooms.Single(room => room.Profile.Id == guest.RoomId).GuestId, Is.EqualTo(guest.GuestId));
                }
                // Model counterparts of the two authored tools: registered, unplaced and OFF.
                // There is no physical pickup/carry claim and no heater switch/placement fixture.
                Require(hotel.Heaters.Register("portable-heater-1"));
                Require(hotel.Heaters.Register("portable-heater-2"));
                hotel.RefreshElectrical();
                Assert.That(hotel.Guests.Count, Is.EqualTo(occupants));
                Assert.That(hotel.Boiler.LoadOverride, Is.Null);
                Assert.That(hotel.Electrical.Circuits.All(circuit => !circuit.LoadOverride.HasValue), Is.True);
                Assert.That(hotel.HeatingDemands.Count(row => row.GuestId != null), Is.EqualTo(occupants));
                Assert.That(hotel.HeatingDemands.All(row => row.HotWater == 0), Is.True);
                Assert.That(hotel.Boiler.Load, Is.EqualTo(hotel.HeatingDemands.Sum(row => row.Total)).Within(.00001f));
                return new Fixture { Hotel = hotel, Rooms = rooms, Settings = settings };
            }
            finally
            {
                // ToData copied immutable settings; production assets were never edited or saved.
                UnityEngine.Object.DestroyImmediate(living);
                UnityEngine.Object.DestroyImmediate(services);
            }
        }

        static void Advance(Fixture fixture, int seconds)
        {
            for (int second = 0; second < seconds; second++)
            {
                fixture.Hotel.Tick(1);
                Assert.That(fixture.Hotel.Boiler.Failed, Is.False,
                    "This acceptance interval must show consequences before an actual boiler failure.");
            }
        }

        static string ElectricalSources(HotelSimulation hotel) => string.Join(";", hotel.Electrical.Consumers
            .OrderBy(source => source.Id).Select(source => FormattableString.Invariant(
                $"{source.Id}/{source.RoomId}/{source.CircuitId}:{source.RequestedLoad:F6}/{source.DeliveredLoad:F6}")));

        static void AssertPowerIndependent(Fixture fixture, string sources)
        {
            var hotel = fixture.Hotel;
            Assert.That(ElectricalSources(hotel), Is.EqualTo(sources), "Central heat stress or a radiator action cannot add a power consumer.");
            Assert.That(hotel.Electrical.Circuits.All(circuit => circuit.HasPower && !circuit.Warning && circuit.TripCount == 0), Is.True);
            Assert.That(hotel.Heaters.Items.Count, Is.EqualTo(2));
            Assert.That(hotel.Heaters.Items.All(heater => !heater.SwitchedOn && heater.DemandedElectricalLoad == 0 && heater.EffectiveHeatOutput == 0), Is.True);
        }

        static void Record(string label, Fixture fixture)
        {
            var hotel = fixture.Hotel; var guest = fixture.ColdGuest;
            TestContext.WriteLine(FormattableString.Invariant(
                $"RHYTHM THERMAL {label}: time={hotel.Elapsed:F3}, guests={hotel.Guests.Count}, demand={hotel.Boiler.Load:F5}, capacity={hotel.Boiler.EffectiveCapacity:F5}, ratio={hotel.Boiler.LoadRatio:F5}, output={hotel.Boiler.HeatingOutput:F5}, stress={hotel.Boiler.Stress01:F5}, condition={hotel.Boiler.Condition:F5}, failed={hotel.Boiler.Failed}, room102={fixture.WeakRoom.Temperature:F4}, perceived={guest.Perception.PerceivedTemperature:F4}, severity={guest.Needs.Temperature.Severity:F5}, dissatisfaction={guest.Needs.Temperature.Dissatisfaction:F5}, satisfaction={hotel.Satisfaction.Evaluate(guest):F4}, valve={fixture.WeakRoom.RadiatorSetting}"));
        }

        [Test]
        public void FourFiveSixOwnedRoomsCauseOrderedHeatAndWeakRoomConsequencesBeforeFailure()
        {
            var fixtures = Enumerable.Range(4, 3).Select(count => Create(count)).ToArray();
            float sameInitialWeakTemperature = fixtures[0].WeakRoom.Temperature;
            foreach (var fixture in fixtures)
            {
                Assert.That(fixture.WeakRoom.Temperature, Is.EqualTo(sameInitialWeakTemperature));
                Assert.That(fixture.WeakRoom.Profile.HeatLoss, Is.EqualTo(fixture.Rooms.Max(room => room.Profile.HeatLoss)));
                Assert.That(fixture.ColdGuest.Application.Archetype.Kind, Is.EqualTo(GuestKind.ColdSensitive));
                string power = ElectricalSources(fixture.Hotel);
                Advance(fixture, 120); // Four hotel hours at the unchanged production720s/day scale.
                AssertPowerIndependent(fixture, power);
                Assert.That(fixture.Hotel.Guests.All(guest => guest.Agent.InAssignedRoom && guest.Agent.ActivityStaged), Is.True);
                Assert.That(fixture.Hotel.HeatingDemands.All(row => row.HotWater == 0), Is.True);
                Assert.That(fixture.ColdGuest.ColdExposureSeconds, Is.GreaterThan(0));
                Record("unmanaged fixed workload", fixture);
            }
            var four = fixtures[0]; var five = fixtures[1]; var six = fixtures[2];
            Assert.That(four.Hotel.Boiler.LoadRatio, Is.LessThan(four.Settings.Boiler.Capacity.StrainedLoadRatio));
            Assert.That(four.Hotel.Boiler.HeatingOutput, Is.EqualTo(1).Within(.00001f));
            Assert.That(five.Hotel.Boiler.LoadRatio, Is.LessThan(1), "The five-room heat reduction precedes exceeding capacity, not just failure.");
            Assert.That(five.Hotel.Boiler.HeatingOutput, Is.LessThan(four.Hotel.Boiler.HeatingOutput));
            Assert.That(six.Hotel.Boiler.LoadRatio, Is.GreaterThan(1));
            Assert.That(six.Hotel.Boiler.HeatingOutput, Is.LessThan(five.Hotel.Boiler.HeatingOutput));
            for (int index = 1; index < fixtures.Length; index++)
            {
                Assert.That(fixtures[index].Hotel.Boiler.Load, Is.GreaterThan(fixtures[index - 1].Hotel.Boiler.Load));
                Assert.That(fixtures[index].Hotel.Boiler.Stress01, Is.GreaterThan(fixtures[index - 1].Hotel.Boiler.Stress01));
                Assert.That(fixtures[index].WeakRoom.Temperature, Is.LessThan(fixtures[index - 1].WeakRoom.Temperature));
                Assert.That(fixtures[index].ColdGuest.Needs.Temperature.Severity,
                    Is.GreaterThan(fixtures[index - 1].ColdGuest.Needs.Temperature.Severity));
                Assert.That(fixtures[index].Hotel.Satisfaction.Evaluate(fixtures[index].ColdGuest),
                    Is.LessThan(fixtures[index - 1].Hotel.Satisfaction.Evaluate(fixtures[index - 1].ColdGuest)));
            }
            Assert.That(six.WeakRoom.Temperature, Is.LessThan(sameInitialWeakTemperature - 1),
                "The real thermal integration must produce visible cooling, not only a capacity label.");
            var cause = six.ColdGuest.Perception.TemperatureCause;
            Assert.That(cause, Is.Not.Null);
            Assert.That(cause.SourceEntityId, Is.EqualTo("room/102/temperature"));
            Assert.That(cause.SourceRoomId, Is.EqualTo(102));
            Assert.That(six.Hotel.Incidents.Items.Any(incident => incident.GuestId == six.ColdGuest.GuestId &&
                incident.Reason == IncidentReason.Temperature && incident.Active), Is.True);
        }

        [Test]
        public void NaturalWeakRoomSelfResponsePrecedesFailureAndOnlyRealAnchorChangesValveDemand()
        {
            var fixture = Create(6, true); var hotel = fixture.Hotel; var guest = fixture.ColdGuest;
            string power = ElectricalSources(hotel);
            float initialTemperature = fixture.WeakRoom.Temperature;
            for (int step = 0; step < 48 && guest.Agent.Activity != GuestActivity.AdjustRadiator; step++) hotel.Tick(.25f);
            Assert.That(guest.Agent.Activity, Is.EqualTo(GuestActivity.AdjustRadiator),
                "Production observation/tolerance and actual cold, not a debug request, must start this self-response.");
            Assert.That(hotel.Boiler.Failed, Is.False);
            Assert.That(hotel.Boiler.HeatingOutput, Is.GreaterThan(0).And.LessThan(1));
            Assert.That(fixture.WeakRoom.Temperature, Is.LessThan(initialTemperature));
            Assert.That(guest.Perception.PerceivedTemperature, Is.EqualTo(fixture.WeakRoom.Temperature).Within(.00001f));
            Assert.That(guest.Perception.TemperatureCause.SourceEntityId, Is.EqualTo("room/102/temperature"));
            var response = hotel.Services.FindResponse(guest.Agent.ResponseActionId);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.SelfResponseApplied, Is.False);
            Assert.That(guest.Agent.ActivityStaged, Is.False);
            int beforeSetting = fixture.WeakRoom.RadiatorSetting;
            float beforeDemand = hotel.Boiler.Load;
            float roomDemand = hotel.HeatingDemands.Single(row => row.RoomId == 102).SpaceHeating;
            int version = guest.Agent.ResponseActionVersion;
            Record("natural self-help en route", fixture);
            // Explicit headless arrival at the real action's radiator anchor; this is not physical route evidence.
            Require(hotel.SignalGuestResponseAnchorReached(guest.GuestId, response.Id, version, GuestResponseAnchor.Radiator));
            Assert.That(response.SelfResponseApplied && response.SelfResponseAttempted, Is.True);
            Assert.That(fixture.WeakRoom.RadiatorSetting, Is.EqualTo(beforeSetting + 1));
            float changedRoomDemand = hotel.HeatingDemands.Single(row => row.RoomId == 102).SpaceHeating;
            Assert.That(changedRoomDemand, Is.GreaterThan(roomDemand));
            Assert.That(hotel.Boiler.Load - beforeDemand, Is.EqualTo(changedRoomDemand - roomDemand).Within(.00001f));
            Assert.That(hotel.SignalGuestResponseAnchorReached(guest.GuestId, response.Id, version, GuestResponseAnchor.Radiator).Success,
                Is.False, "Replaying the same arrival cannot turn the valve another step.");
            Assert.That(hotel.Boiler.Failed, Is.False);
            AssertPowerIndependent(fixture, power);
            Record("natural self-help applied once", fixture);
        }

        [Test]
        public void RemovingOtherRoomRadiatorDemandRestoresHeatAndReducesStoredStressWithoutRepairingCondition()
        {
            var fixture = Create(6); var hotel = fixture.Hotel;
            string power = ElectricalSources(hotel);
            Advance(fixture, 120);
            float lowOutput = hotel.Boiler.HeatingOutput;
            float oldStress = hotel.Boiler.Stress01;
            float oldCondition = hotel.Boiler.Condition;
            float coldTemperature = fixture.WeakRoom.Temperature;
            float oldSeverity = fixture.ColdGuest.Needs.Temperature.Severity;
            var guestIds = hotel.Guests.Select(guest => guest.GuestId).ToArray();
            var sources = hotel.HeatingDemands.ToArray();
            Assert.That(oldStress, Is.GreaterThan(0));
            Record("before deliberate load reduction", fixture);
            // Explicit staff model command adapter, not a physics/visit claim. Keep102's
            // valve and guest; close the other actual room valves, without removing owners.
            foreach (var room in fixture.Rooms.Where(room => room.Profile.Id != 102))
            {
                Require(hotel.RequestStaffRoomAccess(0, room.Profile.Id));
                Require(hotel.SetRadiatorSetting(0, room.Profile.Id, 0));
            }
            Assert.That(hotel.Boiler.Load, Is.EqualTo(sources.Single(row => row.RoomId == 102).SpaceHeating).Within(.00001f));
            Assert.That(hotel.Boiler.Stress01, Is.EqualTo(oldStress), "Closing valves does not instantly erase stored stress.");
            Assert.That(hotel.Boiler.Condition, Is.EqualTo(oldCondition), "Demand management is not a free repair.");
            Assert.That(fixture.WeakRoom.RadiatorSetting, Is.EqualTo(1));
            Advance(fixture, 90);
            Assert.That(hotel.Boiler.HeatingOutput, Is.GreaterThan(lowOutput));
            Assert.That(hotel.Boiler.HeatingOutput, Is.EqualTo(1).Within(.00001f));
            Assert.That(hotel.Boiler.Stress01, Is.LessThan(oldStress));
            Assert.That(hotel.Boiler.Condition, Is.LessThanOrEqualTo(oldCondition));
            Assert.That(fixture.WeakRoom.Temperature, Is.GreaterThan(coldTemperature + .5f));
            Assert.That(fixture.ColdGuest.Needs.Temperature.Severity, Is.LessThan(oldSeverity));
            Assert.That(hotel.Guests.Select(guest => guest.GuestId), Is.EqualTo(guestIds));
            Assert.That(hotel.HeatingDemands.Count(row => row.GuestId != null), Is.EqualTo(6));
            AssertPowerIndependent(fixture, power);
            Record("same six owners after radiator management", fixture);
        }
    }
}
