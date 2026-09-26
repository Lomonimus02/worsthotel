using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace WorstHotel.Tests
{
    public sealed class LanServiceSnapshotTests
    {
        sealed class Fixture
        {
            public HotelSimulation Hotel;
            public RoomState[] Rooms;
            public GuestStay Business => Hotel.Guests.Single(guest => guest.GuestId == "service-business");
            public GuestStay Cold => Hotel.Guests.Single(guest => guest.GuestId == "service-cold");
        }

        static Fixture Create(bool populate)
        {
            var profiles = new[]
            {
                new GuestProfile(GuestKind.Budget, "Budget", "", 180, .85f, 18, .7f, 28, 90, .55f),
                new GuestProfile(GuestKind.ColdSensitive, "Cold", "", 300, 1.05f, 20, 1.35f, 16, 65, .4f,
                    needs: new NeedProfile(21, 25, 18, 28, .125f, .25f, 65)),
                new GuestProfile(GuestKind.Business, "Business", "", 450, 1, 19.5f, 1, 10, 45, .25f)
            };
            var settings = new SessionSettings(profiles, Enumerable.Range(101, 6).Select(id =>
                new RoomProfile(id, "Room " + id, noise: 0, temperature: 22.5f)), new BoilerSettings(), new EconomySettings());
            var rooms = settings.Rooms.Select(room => new RoomState(room)).ToArray();
            var hotel = new HotelSimulation(settings, rooms, new LivingHotelSettings(firstArrivalSeconds: .2f,
                arrivalSpacingSeconds: .2f, arrivalJitterSeconds: 0, firstActivityDelay: 1000),
                services: new GuestServiceSettings(eligibility: 0, wakeLeadSeconds: 35, wakeToleranceSeconds: 5, wakeMissSeconds: 20));
            var result = new Fixture { Hotel = hotel, Rooms = rooms };
            if (!populate) return result;
            var offers = new[] { new BookingApplication("service-business", "Business guest", profiles[2], 450),
                new BookingApplication("service-cold", "Cold guest", profiles[1], 300) };
            Assert.That(hotel.StartShift(new[] { new BookingAssignment(102, offers[0].Id, 450, 0),
                new BookingAssignment(104, offers[1].Id, 300, 1) }, offers).Success, Is.True);
            hotel.Tick(.8f);
            foreach (var guest in hotel.Guests)
            {
                Assert.That(hotel.SignalGuestReachedReception(guest.GuestId).Success, Is.True);
                Assert.That(ModelKeyHandoff.CheckIn(hotel, 0, guest.GuestId).Success, Is.True);
                Assert.That(hotel.SignalGuestReachedRoom(guest.GuestId).Success, Is.True);
            }
            return result;
        }

        static void AddServiceState(Fixture f)
        {
            var hotel = f.Hotel;
            Assert.That(hotel.DebugSetMildCold(f.Cold.GuestId).Success, Is.True);
            Assert.That(hotel.DebugForceService(f.Cold.GuestId, ServiceKind.ExtraBlanket).Success, Is.True);
            Assert.That(hotel.TakeServiceItem(0, "blanket:0").Success, Is.True);
            Assert.That(hotel.DeliverBlanket(0, f.Cold.GuestId).Success, Is.True);
            Assert.That(hotel.TakeServiceItem(0, "blanket:1").Success, Is.True);
            Assert.That(hotel.TakeServiceItem(1, "bulb:0").Success, Is.True);
            Assert.That(hotel.DropServiceItem(1, "bulb:0").Success, Is.True);
            Assert.That(hotel.TakeServiceItem(1, "bulb:1").Success, Is.True);
            Assert.That(hotel.DebugForceService(f.Business.GuestId, ServiceKind.WakeUpCall).Success, Is.True);
            var request = hotel.Services.Cases.Single(item => item.Kind == ServiceKind.WakeUpCall);
            Assert.That(hotel.RespondToService(0, request.Id, true).Success, Is.True);
            hotel.Tick(.2f);
        }

        static HotelSimulation Mirror()
        { var result = Create(false).Hotel; result.EnableReadOnlyMirror(); return result; }
        static HotelModelSnapshot Wire(HotelSimulation hotel, long sequence = 1) =>
            JsonUtility.FromJson<HotelModelSnapshot>(JsonUtility.ToJson(hotel.CaptureSnapshot(77, sequence)));
        static string State(HotelSimulation hotel) => JsonUtility.ToJson(hotel.CaptureSnapshot(77, 1));
        static void Apply(HotelSimulation mirror, HotelModelSnapshot packet)
        { var result = mirror.ApplySnapshot(packet); Assert.That(result.Success, Is.True, result.Message); }
        static void Advance(Fixture f, float seconds)
        {
            while (seconds > .0001f)
            {
                foreach (var room in f.Rooms) room.Temperature = 22.5f;
                float dt = Math.Min(.2f, seconds); f.Hotel.Tick(dt); seconds -= dt;
            }
        }

        [Test]
        public void PlanningAndRichServiceJsonRoundTripsPreserveRealStockOwnersComfortAndMemoryWithoutEvents()
        {
            var planning = Create(false).Hotel; var replica = Mirror();
            Apply(replica, Wire(planning));
            Assert.That(replica.Services.BlanketsAvailable, Is.EqualTo(3));
            Assert.That(State(replica), Is.EqualTo(State(planning)));
            var f = Create(true); AddServiceState(f);
            int notifications = 0;
            replica.Services.Changed += _ => notifications++;
            replica.Services.ItemChanged += _ => notifications++;
            var packet = Wire(f.Hotel, 2); Apply(replica, packet);
            Assert.That(State(replica), Is.EqualTo(State(f.Hotel)));
            Assert.That(notifications, Is.Zero);
            Assert.That(replica.Services.FindItem("blanket:0").Location, Is.EqualTo(ServiceItemLocation.Delivered));
            Assert.That(replica.Services.FindItem("blanket:0").GuestId, Is.EqualTo(f.Cold.GuestId));
            Assert.That(replica.Services.FindItem("blanket:1").PlayerId, Is.EqualTo(0));
            Assert.That(replica.Services.FindItem("bulb:0").Location, Is.EqualTo(ServiceItemLocation.Dropped));
            Assert.That(replica.Services.FindItem("bulb:0").LastPlayerId, Is.EqualTo(1));
            Assert.That(replica.Services.FindItem("bulb:1").PlayerId, Is.EqualTo(1));
            var cold = replica.Guests.Single(guest => guest.GuestId == f.Cold.GuestId);
            Assert.That(cold.BlanketComfortBonus, Is.EqualTo(f.Hotel.Services.Settings.BlanketComfortBonus));
            Assert.That(cold.Perception.PerceivedTemperature, Is.GreaterThan(cold.Perception.Temperature));
            Assert.That(cold.Memory.ServicesRequested, Is.EqualTo(1));
            Assert.That(cold.Memory.ServicesFulfilled, Is.EqualTo(1));
            Assert.That(cold.Memory.BlanketsDelivered, Is.EqualTo(1));
            Assert.That(cold.ServiceSatisfactionAdjustment, Is.EqualTo(f.Hotel.Services.Settings.FulfilledBonus));
            string before = State(replica);
            packet.ServiceLayer.Items[0].Generation += 50;
            packet.ServiceLayer.Cases[0].Description = "mutated transport";
            packet.ServiceLayer.Promises[0].DueTime += 50;
            Assert.That(State(replica), Is.EqualTo(before));
            Assert.That(State(f.Hotel), Is.EqualTo(before), "Capture and restoration must both isolate model-owned service data.");
        }

        [Test]
        public void MutableLateCheckoutRoundTripPreservesExistingGuestAndAgentPresentationIdentity()
        {
            var f = Create(true); var replica = Mirror(); Apply(replica, Wire(f.Hotel));
            var guest = replica.Guests.Single(item => item.GuestId == f.Business.GuestId); var agent = guest.Agent;
            float original = agent.CheckoutTime;
            Assert.That(f.Hotel.DebugForceService(f.Business.GuestId, ServiceKind.LateCheckout).Success, Is.True);
            var request = f.Hotel.Services.Cases.Single(item => item.Kind == ServiceKind.LateCheckout);
            Assert.That(f.Hotel.RespondToService(0, request.Id, true).Success, Is.True);
            Apply(replica, Wire(f.Hotel, 2));
            Assert.That(replica.Guests.Single(item => item.GuestId == guest.GuestId), Is.SameAs(guest));
            Assert.That(guest.Agent, Is.SameAs(agent));
            Assert.That(agent.CheckoutTime, Is.GreaterThan(original));
            Assert.That(agent.CheckoutTime, Is.EqualTo(f.Business.Agent.CheckoutTime));
            Assert.That(guest.Memory.ServicesFulfilled, Is.EqualTo(1));
            Assert.That(State(replica), Is.EqualTo(State(f.Hotel)));
        }

        [Test]
        public void MovingGuestsKeepsPromiseAndCaseDestinationsCoherentAndTheDeliveredBlanketFollowsItsOwner()
        {
            var f = Create(true); AddServiceState(f); var replica = Mirror(); Apply(replica, Wire(f.Hotel));
            var promise = f.Hotel.Services.Promises.Single();
            var blanket = f.Hotel.Services.FindItem("blanket:0");
            string caseId = promise.Id;
            Assert.That(f.Hotel.DropServiceItem(0, "blanket:1").Success, Is.True);
            Assert.That(ModelKeyHandoff.MoveGuest(f.Hotel, 0, f.Business.GuestId, 101).Success, Is.True);
            Assert.That(ModelKeyHandoff.MoveGuest(f.Hotel, 0, f.Cold.GuestId, 106).Success, Is.True);
            Assert.That(promise.RoomId, Is.EqualTo(101));
            Assert.That(f.Hotel.Services.FindCase(caseId).RoomId, Is.EqualTo(101));
            Assert.That(blanket.RoomId, Is.EqualTo(106));
            Assert.That(blanket.GuestId, Is.EqualTo(f.Cold.GuestId));
            Assert.That(f.Hotel.Services.Cases.Single(item => item.Kind == ServiceKind.ExtraBlanket).SourceRoomId, Is.EqualTo(104),
                "The original cold cause remains identifiable after moving the guest.");
            Apply(replica, Wire(f.Hotel, 2)); // Capture immediately, before any later model tick can repair labels.
            Assert.That(State(replica), Is.EqualTo(State(f.Hotel)));
            Assert.That(f.Hotel.SignalGuestReachedRoom(f.Business.GuestId).Success, Is.True);
            Assert.That(f.Hotel.SignalGuestReachedRoom(f.Cold.GuestId).Success, Is.True);
            Advance(f, promise.DueTime - f.Hotel.Elapsed);
            Assert.That(f.Hotel.CompleteWakeUpCall(0, promise.Id).Success, Is.True);
            Assert.That(ModelKeyHandoff.MoveGuest(f.Hotel, 0, f.Business.GuestId, 103).Success, Is.True);
            Assert.That(promise.Status, Is.EqualTo(PromiseStatus.Completed));
            Assert.That(promise.RoomId, Is.EqualTo(103));
            Assert.That(f.Hotel.Services.FindCase(caseId).RoomId, Is.EqualTo(103));
            Apply(replica, Wire(f.Hotel, 3));
            Assert.That(State(replica), Is.EqualTo(State(f.Hotel)));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void CompletedOrMissedWakePromiseRoundTripPreservesItsOutcomeAndServiceMemory(bool call)
        {
            var f = Create(true); AddServiceState(f); var replica = Mirror(); Apply(replica, Wire(f.Hotel));
            var promise = f.Hotel.Services.Promises.Single();
            float target = call ? promise.DueTime : promise.DueTime + f.Hotel.Services.Settings.WakeMissSeconds + .4f;
            Advance(f, target - f.Hotel.Elapsed);
            if (call) Assert.That(f.Hotel.CompleteWakeUpCall(0, promise.Id).Success, Is.True);
            Apply(replica, Wire(f.Hotel, 2));
            Assert.That(replica.Services.Promises.Single().Status, Is.EqualTo(call ? PromiseStatus.Completed : PromiseStatus.Missed));
            Assert.That(replica.Services.FindCase(promise.Id).Status, Is.EqualTo(call ? ServiceStatus.Fulfilled : ServiceStatus.Expired));
            var guest = replica.Guests.Single(item => item.GuestId == f.Business.GuestId);
            Assert.That(guest.Memory.PromisesKept, Is.EqualTo(call ? 1 : 0));
            Assert.That(guest.Memory.PromisesBroken, Is.EqualTo(call ? 0 : 1));
            Assert.That(guest.ServiceSatisfactionAdjustment, Is.EqualTo(f.Business.ServiceSatisfactionAdjustment));
            Assert.That(State(replica), Is.EqualTo(State(f.Hotel)));
        }

        [Test]
        public void ServiceMirrorCannotRespondMoveStockChangePromisesOrRefillLocally()
        {
            var f = Create(true); AddServiceState(f); var replica = Mirror(); Apply(replica, Wire(f.Hotel));
            string before = State(replica); var promise = replica.Services.Promises.Single();
            Assert.That(replica.RespondToService(0, promise.Id, false).Success, Is.False);
            Assert.That(replica.AcknowledgeService(0, promise.Id).Success, Is.False);
            Assert.That(replica.CompleteWakeUpCall(0, promise.Id).Success, Is.False);
            Assert.That(replica.TakeServiceItem(0, "blanket:2").Success, Is.False);
            Assert.That(replica.DropServiceItem(0, "blanket:1").Success, Is.False);
            Assert.That(replica.ReturnServiceItem(0, "blanket:1").Success, Is.False);
            Assert.That(replica.DeliverBlanket(0, f.Business.GuestId).Success, Is.False);
            Assert.That(replica.StoreLuggage(0, f.Business.GuestId).Success, Is.False);
            Assert.That(replica.ReplaceRoomBulb(1, 104).Success, Is.False);
            Assert.That(replica.DebugForceService(f.Business.GuestId, ServiceKind.LateCheckout).Success, Is.False);
            Assert.That(replica.DebugSetBlanketStock(0).Success, Is.False);
            Assert.That(replica.DebugSetMildCold(f.Business.GuestId).Success, Is.False);
            replica.Services.SetStaffCount(2);
            var refill = typeof(GuestServiceSystem).GetMethod("RefillForDay", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(refill, Is.Not.Null);
            refill.Invoke(replica.Services, new object[] { 3 }); // Explicitly exercise the host-only lifecycle guard.
            replica.Tick(10);
            Assert.That(State(replica), Is.EqualTo(before));
        }

        [Test]
        public void MalformedServicesAreRejectedAtomicallyAndDoNotConsumeTheIncomingSequence()
        {
            var f = Create(true); AddServiceState(f); var replica = Mirror(); Apply(replica, Wire(f.Hotel, 10));
            string before = State(replica);
            var invalid = new Action<HotelModelSnapshot>[]
            {
                s => s.ServiceLayer.ServiceEnd = float.NaN,
                s => s.ServiceLayer.Cases[0].RecoverySeconds = float.PositiveInfinity,
                s => s.ServiceLayer.Cases[0].GuestId = "unknown-guest",
                s => s.ServiceLayer.Items[0].GuestId = "unknown-guest",
                s => s.ServiceLayer.Items.Single(item => item.Id == "bulb:1").PlayerId = 0,
                s => s.ServiceLayer.Items.Single(item => item.Id == "blanket:2").Id = "blanket:99",
                s => s.ServiceLayer.Promises[0].Id = "orphan-promise",
                s => s.ServiceLayer.Promises[0].DueTime += 1,
                s => { s.ServiceLayer.Promises[0].Status = PromiseStatus.Completed; s.ServiceLayer.Promises[0].CompletedAt = s.Time; },
                s =>
                {
                    var c = s.ServiceLayer.Cases.Single(item => item.Kind == ServiceKind.ExtraBlanket);
                    c.GuestId = f.Business.GuestId; c.Status = ServiceStatus.Requested;
                    c.Id = c.GuestId + "/service/" + c.Kind + "/" + c.SourceEntityId;
                },
                s =>
                {
                    var c = s.ServiceLayer.Cases.Single(item => item.Kind == ServiceKind.ExtraBlanket);
                    c.Kind = ServiceKind.AskNeighborsQuiet; c.SourceEntityId = f.Business.GuestId + "/Television";
                    c.Id = c.GuestId + "/service/" + c.Kind + "/" + c.SourceEntityId;
                }
            };
            foreach (var mutate in invalid)
            {
                var packet = Wire(f.Hotel, 11); mutate(packet);
                Assert.That(replica.ApplySnapshot(packet).Success, Is.False);
                Assert.That(State(replica), Is.EqualTo(before), "No part of a rejected service packet may update the mirror.");
                Assert.That(replica.AppliedSnapshotSequence, Is.EqualTo(10));
            }
            Apply(replica, Wire(f.Hotel, 11));
            Assert.That(replica.AppliedSnapshotSequence, Is.EqualTo(11));
            Assert.That(State(replica), Is.EqualTo(State(f.Hotel)));
        }
    }
}
