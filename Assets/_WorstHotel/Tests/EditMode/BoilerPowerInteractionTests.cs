// Production capacity/heat/power/service tuning. Explicit diagnostic reservations,
// model arrival/key/anchor acknowledgements, and isolated subsystem-time setup
// are labelled below. This is not a physical route or natural-cascade reproduction.
using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace WorstHotel.Tests
{
    public sealed class BoilerPowerInteractionTests
    {
        sealed class Fixture
        {
            public HotelSimulation Hotel;
            public RoomState[] Rooms;
            public SessionSettings Settings;
        }

        [Serializable]
        sealed class ElectricalView
        {
            public CircuitSnapshot[] Circuits;
            public ConsumerSnapshot[] Consumers;
            public HeaterSnapshot[] Heaters;
            public bool[] RoomPower;
        }

        static void Require(CommandResult result) => Assert.That(result.Success, Is.True, result.Message);

        static Fixture Create(int[] occupiedRooms, bool fundPaidEquipment = false)
        {
            var asset = AssetDatabase.LoadAssetAtPath<SessionConfig>(
                "Assets/_WorstHotel/ScriptableObjects/PrototypeSession.asset");
            Assert.That(asset, Is.Not.Null);
            Assert.That(asset.continuousOperations, Is.True);
            Assert.That(asset.services.naturalCommunicationEnabled, Is.True);
            var config = UnityEngine.Object.Instantiate(asset);
            var living = UnityEngine.Object.Instantiate(asset.living);
            var economy = UnityEngine.Object.Instantiate(asset.economy);
            try
            {
                // Isolate equipment causality from unrelated scheduled TV/showers/outings.
                // Natural need observation and self-help remain enabled and unmodified.
                living.firstActivityDelay = 1000;
                if (fundPaidEquipment) economy.startingCash = 5000; // Payment-path fixture, not earnings evidence.
                config.living = living;
                config.economy = economy;
                var settings = config.ToData();
                var rooms = settings.Rooms.OrderBy(room => room.Id).Select(room => new RoomState(room)).ToArray();
                var hotel = new HotelSimulation(settings, rooms, living.ToData(), config.needs.ToData(),
                    config.noise.ToData(), config.heater.ToData(), config.electricity.ToData(),
                    config.housekeeping.ToData(), config.services.ToData(), config.infrastructure.ToData(),
                    ManualBookingFixture.Operations(config));
                Require(hotel.StartOperations());
                foreach (int roomId in occupiedRooms) Require(hotel.DebugSpawnGuest(GuestKind.ColdSensitive, roomId));
                Assert.That(hotel.Guests, Is.Empty, "Diagnostic reservations still wait for their actual scheduled due time.");
                hotel.Tick(1.25f);
                Assert.That(hotel.Guests.Count, Is.EqualTo(occupiedRooms.Length));
                foreach (var guest in hotel.Guests)
                {
                    // Explicit headless reception/key/room/quiet-anchor adapters.
                    Require(hotel.SignalGuestReachedReception(guest.GuestId));
                    Require(ModelKeyHandoff.CheckIn(hotel, 0, guest.GuestId));
                    Require(hotel.SignalGuestReachedRoom(guest.GuestId));
                    Require(hotel.RegisterGuestPhysicalStaging(guest.GuestId));
                    Require(hotel.SignalGuestActivityReady(guest.GuestId, GuestAgentState.InRoom, GuestActivity.QuietRest));
                }
                foreach (var pair in new[] { (id: "causal-heater-a", room: 101), (id: "causal-heater-b", room: 104) })
                {
                    Require(hotel.Heaters.Register(pair.id));
                    Require(hotel.Heaters.AssignRoom(pair.id, pair.room));
                    Assert.That(hotel.Heaters.Find(pair.id).SwitchedOn, Is.False);
                }
                hotel.RefreshElectrical();
                Assert.That(hotel.Boiler.LoadOverride, Is.Null);
                Assert.That(hotel.Electrical.Circuits.All(circuit => !circuit.LoadOverride.HasValue), Is.True);
                return new Fixture { Hotel = hotel, Rooms = rooms, Settings = settings };
            }
            finally
            {
                // ToData copied immutable tuning. Never mutate or save production assets.
                UnityEngine.Object.DestroyImmediate(config);
                UnityEngine.Object.DestroyImmediate(living);
                UnityEngine.Object.DestroyImmediate(economy);
            }
        }

        static string ElectricalState(Fixture fixture)
        {
            var snapshot = fixture.Hotel.CaptureSnapshot(941, 1);
            return JsonUtility.ToJson(new ElectricalView
            {
                Circuits = snapshot.Circuits,
                Consumers = snapshot.Consumers,
                Heaters = snapshot.Heaters,
                RoomPower = fixture.Rooms.Select(room => room.HasPower).ToArray()
            });
        }

        [TestCase("failure")]
        [TestCase("paid restart")]
        [TestCase("maintenance start")]
        public void BoilerTransitionDoesNotAddConsumersChangePowerOrRewriteCircuitExposure(string transition)
        {
            var fixture = Create(new[] { 101, 102, 103 }, fundPaidEquipment: true);
            var hotel = fixture.Hotel;
            Require(hotel.Heaters.SetSwitchedOn("causal-heater-a", true));
            hotel.RefreshElectrical();
            var circuit = hotel.Electrical.Find("A");
            Assert.That(circuit.RequestedLoad,
                Is.EqualTo(3 * hotel.ElectricitySettings.OccupiedRoomLoad + hotel.HeaterSettings.ElectricalLoad).Within(.00001f));
            Assert.That(circuit.RequestedLoad, Is.GreaterThan(circuit.Capacity));

            // Explicit isolated electrical-time setup: create real consumer exposure, not
            // an assigned timer or load override. The upcoming boiler command advances no time.
            float exposure = hotel.ElectricitySettings.WarningSeconds + .25f;
            Assert.That(exposure + .5f, Is.LessThan(hotel.ElectricitySettings.TripSeconds));
            hotel.Electrical.Tick(hotel.Guests, fixture.Rooms, hotel.Heaters, exposure);
            Assert.That(circuit.Warning, Is.True);
            Assert.That(circuit.Tripped, Is.False);
            Assert.That(circuit.OverloadSeconds, Is.EqualTo(exposure));

            if (transition == "paid restart")
            {
                // Controlled initial failure plus low-level relief integration establishes
                // valid support. Physical input authentication is covered by PlayMode.
                hotel.Boiler.ForceFailure();
                Require(hotel.Boiler.SetRelief(0, true));
                float reliefTime = Math.Abs(hotel.Boiler.Pressure - fixture.Settings.Boiler.ReliefTarget) /
                    fixture.Settings.Boiler.ReliefRate;
                hotel.Boiler.Tick(reliefTime);
                Require(hotel.Boiler.CanRestart(1));
            }
            string before = ElectricalState(fixture);
            float clock = hotel.Elapsed;
            int cash = hotel.Economy.Cash;
            if (transition == "failure")
            {
                hotel.Boiler.ForceFailure();
                Assert.That(hotel.Boiler.Failed, Is.True);
                Assert.That(hotel.Boiler.HeatingOutput, Is.EqualTo(fixture.Settings.Boiler.FailedHeatOutput));
            }
            else if (transition == "paid restart")
            {
                Require(hotel.EmergencyPatchBoiler(1));
                Assert.That(hotel.Boiler.Failed, Is.False);
                Assert.That(hotel.Boiler.EmergencyPatchActive, Is.True);
                Assert.That(cash - hotel.Economy.Cash, Is.EqualTo(fixture.Settings.Economy.CheapPatchCost));
            }
            else
            {
                Require(hotel.BeginBoilerMaintenance(0));
                Assert.That(hotel.Boiler.MaintenanceInProgress, Is.True);
                Assert.That(hotel.Boiler.HeatingOutput, Is.Zero);
                Assert.That(cash - hotel.Economy.Cash, Is.EqualTo(fixture.Settings.Economy.ProperRepairCost));
            }
            Assert.That(hotel.Elapsed, Is.EqualTo(clock));
            Assert.That(ElectricalState(fixture), Is.EqualTo(before), "The actual boiler transition must not mutate the electrical state.");
            hotel.RefreshElectrical();
            Assert.That(ElectricalState(fixture), Is.EqualTo(before), "A zero-time refresh must not reveal a hidden boiler/pump consumer.");

            hotel.Electrical.Tick(hotel.Guests, fixture.Rooms, hotel.Heaters, .5f);
            Assert.That(circuit.OverloadSeconds, Is.EqualTo(exposure + .5f).Within(.00001f),
                "The existing real heater overload still advances normally; boiler state must not freeze or reset its timer.");
            Assert.That(circuit.Tripped, Is.False);
            Assert.That(hotel.Electrical.Consumers.Any(consumer => consumer.Id.StartsWith("boiler:", StringComparison.Ordinal) ||
                consumer.Id.StartsWith("pump:", StringComparison.Ordinal)), Is.False);
            TestContext.WriteLine(transition + ": identical electrical identities/load/power/exposure at unchanged clock; then +0.5 measured exposure.");
        }

        [Test]
        public void SevereRoomColdDoesNotAutomaticallySwitchOrPowerAnyPortableHeater()
        {
            var fixture = Create(new[] { 102 });
            var hotel = fixture.Hotel;
            var guest = hotel.Guests.Single();
            var room = fixture.Rooms.Single(item => item.Profile.Id == guest.RoomId);
            Require(hotel.Heaters.AssignRoom("causal-heater-a", room.Profile.Id));
            hotel.RefreshElectrical();
            // One labelled initial cold condition. No per-tick temperature clamp, forced
            // complaint, automatic heater action or fabricated guest outcome follows.
            Require(hotel.SetRoomTemperature(room.Profile.Id, guest.Application.Archetype.Needs.ToleranceTemperatureMin - 6));
            float initial = room.Temperature;
            float target = hotel.Elapsed + hotel.ElectricitySettings.TripSeconds + 6;
            while (hotel.Elapsed < target)
            {
                hotel.Tick(Math.Min(.2f, target - hotel.Elapsed));
                Assert.That(hotel.Heaters.Items.All(heater => !heater.SwitchedOn &&
                    heater.DemandedElectricalLoad == 0 && heater.EffectiveHeatOutput == 0), Is.True);
                Assert.That(hotel.Electrical.Consumers.Where(consumer => consumer.Id.StartsWith("heater:", StringComparison.Ordinal))
                    .All(consumer => consumer.RequestedLoad == 0 && consumer.DeliveredLoad == 0), Is.True);
                Assert.That(hotel.Electrical.Circuits.All(circuit => circuit.HasPower && !circuit.Warning), Is.True);
            }
            Assert.That(guest.Needs.Temperature.ExposureSeconds, Is.GreaterThan(0), "A real perceived problem must have existed.");
            Assert.That(room.Temperature, Is.GreaterThan(initial), "Normal central thermal integration remains active.");
            Assert.That(room.Temperature, Is.LessThan(guest.Application.Archetype.Needs.PreferredTemperatureMin));
            Assert.That(hotel.Boiler.Failed, Is.False);
            Assert.That(hotel.Services.Responses.Any(response => response.GuestId == guest.GuestId), Is.True,
                "The ordinary response model observed the cold; disabling guest behavior must not make this test pass.");
        }

        [Test]
        public void AcknowledgedNaturalGuestRadiatorSelfHelpRaisesOnlyAttributedCentralDemand()
        {
            var fixture = Create(new[] { 102 });
            var hotel = fixture.Hotel;
            var guest = hotel.Guests.Single();
            var room = fixture.Rooms.Single(item => item.Profile.Id == guest.RoomId);
            // Mild initial condition remains inside real thermal integration. The ordinary
            // configured observation dwell must start self-help; no forced response/case.
            Require(hotel.SetRoomTemperature(room.Profile.Id, guest.Application.Archetype.Needs.PreferredTemperatureMin - .75f));
            GuestResponse response = null;
            float deadline = hotel.Elapsed + hotel.Services.Settings.SelfResponseObserveSeconds + 8;
            while (hotel.Elapsed < deadline && response == null)
            {
                hotel.Tick(Math.Min(.2f, deadline - hotel.Elapsed));
                response = hotel.Services.Responses.FirstOrDefault(item => item.GuestId == guest.GuestId &&
                    item.Phase == GuestResponsePhase.SelfResponding);
            }
            Assert.That(response, Is.Not.Null, "The production cause/dwell must lead to radiator self-help while the boiler is working.");
            Assert.That(hotel.Boiler.Failed, Is.False);
            Assert.That(guest.Agent.Activity, Is.EqualTo(GuestActivity.AdjustRadiator));
            var beforeRows = hotel.HeatingDemands.ToDictionary(row => row.RoomId, row => row.Total);
            float load = hotel.Boiler.Load;
            float clock = hotel.Elapsed;
            int setting = room.RadiatorSetting;
            string electrical = ElectricalState(fixture);

            // Explicit model boundary acknowledgement, not a claim of physical guest travel.
            Require(hotel.SignalGuestResponseAnchorReached(guest.GuestId, response.Id,
                guest.Agent.ResponseActionVersion, GuestResponseAnchor.Radiator));
            Assert.That(response.SelfResponseApplied, Is.True);
            Assert.That(room.RadiatorSetting, Is.EqualTo(setting + 1));
            Assert.That(hotel.Boiler.Load, Is.GreaterThan(load));
            Assert.That(hotel.HeatingDemands.Single(row => row.RoomId == room.Profile.Id).Total,
                Is.GreaterThan(beforeRows[room.Profile.Id]));
            foreach (var row in hotel.HeatingDemands.Where(row => row.RoomId != room.Profile.Id))
                Assert.That(row.Total, Is.EqualTo(beforeRows[row.RoomId]), "A room valve must not create another room's consumer.");
            hotel.RefreshElectrical();
            Assert.That(ElectricalState(fixture), Is.EqualTo(electrical),
                "A central radiator is not an electrical heater and must not modify requested/delivered power or breaker state.");
            Assert.That(hotel.Elapsed, Is.EqualTo(clock));
            Assert.That(hotel.Heaters.Items.All(heater => !heater.SwitchedOn), Is.True);
        }
    }
}
