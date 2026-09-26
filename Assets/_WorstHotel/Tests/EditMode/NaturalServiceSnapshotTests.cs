using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace WorstHotel.Tests
{
    public sealed class NaturalServiceSnapshotTests
    {
        // Model adapters explicitly stand in for travel here. Scene travel is tested separately.
        static HotelSimulation Create(bool populate)
        {
            var profile = new GuestProfile(GuestKind.ColdSensitive, "Cold", "", 300, 1.05f, 20, 1.35f, 16, 65, .4f,
                needs: new NeedProfile(21, 25, 18, 28, .125f, .25f, 65));
            var profiles = new[] { profile,
                new GuestProfile(GuestKind.Budget, "Budget", "", 180, .85f, 18, .7f, 28, 90, .55f),
                new GuestProfile(GuestKind.Business, "Business", "", 450, 1, 19.5f, 1, 10, 45, .25f) };
            var settings = new SessionSettings(profiles, Enumerable.Range(101, 6).Select(id =>
                new RoomProfile(id, "Room " + id, noise: 0, temperature: 22.5f)), new BoilerSettings(), new EconomySettings());
            var hotel = new HotelSimulation(settings, settings.Rooms.Select(r => new RoomState(r)).ToArray(),
                new LivingHotelSettings(firstArrivalSeconds: .2f, arrivalSpacingSeconds: .2f,
                    arrivalJitterSeconds: 0, firstActivityDelay: 1000),
                services: new GuestServiceSettings(eligibility: 0, naturalCommunicationEnabled: true));
            if (!populate) { hotel.EnableReadOnlyMirror(); return hotel; }
            var guest = new BookingApplication("natural-snapshot", "Snapshot guest", profile, 300);
            Assert.That(hotel.StartShift(new[] { new BookingAssignment(104, guest.Id, 300, 0) }, new[] { guest }).Success, Is.True);
            hotel.Tick(.8f);
            Assert.That(hotel.SignalGuestReachedReception(guest.Id).Success, Is.True);
            Assert.That(ModelKeyHandoff.CheckIn(hotel, 0, guest.Id).Success, Is.True);
            Assert.That(hotel.SignalGuestReachedRoom(guest.Id).Success, Is.True);
            Assert.That(hotel.DebugSetMildCold(guest.Id).Success, Is.True);
            hotel.Tick(.2f); // Actual need/incident observation, before any response travel.
            Assert.That(hotel.DebugForceService(guest.Id, ServiceKind.ExtraBlanket).Success, Is.True);
            return hotel;
        }

        static HotelModelSnapshot Wire(HotelSimulation hotel, long sequence) =>
            JsonUtility.FromJson<HotelModelSnapshot>(JsonUtility.ToJson(hotel.CaptureSnapshot(83, sequence)));
        static string State(HotelSimulation hotel) => JsonUtility.ToJson(hotel.CaptureSnapshot(83, 1));
        static void Apply(HotelSimulation mirror, HotelSimulation host, long sequence)
        {
            var result = mirror.ApplySnapshot(Wire(host, sequence));
            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(State(mirror), Is.EqualTo(State(host)));
        }

        static GuestResponse BeginPhone(HotelSimulation host)
        {
            var guest = host.Guests.Single();
            var result = host.DebugBeginGuestContact(guest.GuestId, GuestContactChannel.Phone);
            Assert.That(result.Success, Is.True, result.Message);
            var response = host.Services.FindResponse(guest.Agent.ResponseActionId);
            Assert.That(response, Is.Not.Null);
            return response;
        }

        [Test]
        public void NoticeTravelRingingAndDisclosureRoundTripWithStableGuestIdentityAndReadOnlyMirror()
        {
            var host = Create(true); var mirror = Create(false);
            Apply(mirror, host, 1);
            Assert.That(mirror.Services.Cases.Single().IsKnownToHotel, Is.False);
            var guest = mirror.Guests.Single(); var agent = guest.Agent;
            var response = BeginPhone(host);
            Apply(mirror, host, 2);
            Assert.That(mirror.Guests.Single(), Is.SameAs(guest));
            Assert.That(guest.Agent, Is.SameAs(agent));
            Assert.That(mirror.Services.IncomingCall, Is.Null, "Walking to the room phone cannot ring reception yet.");
            var arrival = host.SignalGuestResponseAnchorReached(response.GuestId, response.Id,
                response.ActionVersion, GuestResponseAnchor.RoomPhone);
            Assert.That(arrival.Success, Is.True, arrival.Message);
            Apply(mirror, host, 3);
            Assert.That(mirror.Services.IncomingCall?.Id, Is.EqualTo(response.Id));
            string before = State(mirror);
            Assert.That(mirror.AnswerIncomingServiceCall(0, response.Id).Success, Is.False);
            Assert.That(mirror.SignalGuestResponseAnchorReached(response.GuestId, response.Id,
                response.ActionVersion, GuestResponseAnchor.RoomPhone).Success, Is.False);
            mirror.Tick(40);
            Assert.That(State(mirror), Is.EqualTo(before));
            Assert.That(host.AnswerIncomingServiceCall(0, response.Id).Success, Is.True);
            Apply(mirror, host, 4);
            var known = mirror.Services.Cases.Single();
            Assert.That(known.IsKnownToHotel, Is.True);
            Assert.That(known.Response, Is.SameAs(mirror.Services.FindResponse(response.Id)));
            Assert.That(mirror.Guests.Single().Memory.ServicesRequested, Is.EqualTo(1));
            Assert.That(host.AnswerIncomingServiceCall(0, response.Id).Success, Is.False);
            Assert.That(host.Guests.Single().Memory.ServicesRequested, Is.EqualTo(1));
        }

        [TestCase("attempts")]
        [TestCase("action-version")]
        [TestCase("source")]
        [TestCase("case-link")]
        [TestCase("false-knowledge")]
        [TestCase("invalid-sentinel")]
        [TestCase("duplicate")]
        public void MalformedResponsePacketsAreRejectedBeforeAnyReplicaMutation(string defect)
        {
            var host = Create(true); var mirror = Create(false);
            BeginPhone(host); Apply(mirror, host, 1);
            string before = State(mirror);
            var packet = Wire(host, 2); var response = packet.ServiceLayer.Responses.Single();
            switch (defect)
            {
                case "attempts": response.ContactAttempts = 3; break;
                case "action-version": packet.Guests[0].Agent.ResponseActionVersion++; break;
                case "source": response.SourceEntityId = "invented/source"; break;
                case "case-link": packet.ServiceLayer.Cases[0].ResponseId = "missing"; break;
                case "false-knowledge": response.CommunicatedAt = 0; break;
                case "invalid-sentinel": response.StaffActionAt = -.5f; break;
                case "duplicate": packet.ServiceLayer.Responses = new[] { response, response }; break;
            }
            Assert.That(mirror.ApplySnapshot(packet).Success, Is.False, defect);
            Assert.That(State(mirror), Is.EqualTo(before), defect + " partially changed the replica");
            Assert.That(mirror.AppliedSnapshotSequence, Is.EqualTo(1));
        }

        [Test]
        public void ReceptionTravelCancellationAndReturnReplicateWithoutLosingRoomOrKey()
        {
            var host = Create(true); var mirror = Create(false); var guest = host.Guests.Single();
            var result = host.DebugBeginGuestContact(guest.GuestId, GuestContactChannel.Reception);
            Assert.That(result.Success, Is.True, result.Message);
            var response = host.Services.FindResponse(guest.Agent.ResponseActionId);
            Apply(mirror, host, 1);
            Assert.That(mirror.Guests.Single().Agent.State, Is.EqualTo(GuestAgentState.GoingToServiceReception));
            Assert.That(host.SignalGuestResponseAnchorReached(guest.GuestId, response.Id, response.ActionVersion,
                GuestResponseAnchor.Reception).Success, Is.True);
            Apply(mirror, host, 2);
            Assert.That(host.DebugCancelGuestContact(response.Id).Success, Is.True);
            Apply(mirror, host, 3);
            Assert.That(mirror.Guests.Single().Agent.State, Is.EqualTo(GuestAgentState.ReturningFromServiceReception));
            Assert.That(mirror.Guests.Single().Agent.CheckedIn, Is.True);
            Assert.That(mirror.Services.Cases.Single().IsKnownToHotel, Is.False);
            Assert.That(host.SignalGuestResponseAnchorReached(guest.GuestId, response.Id, response.ActionVersion,
                GuestResponseAnchor.AssignedRoom).Success, Is.True);
            Apply(mirror, host, 4);
            Assert.That(mirror.Guests.Single().Agent.InAssignedRoom, Is.True);
            Assert.That(mirror.Guests.Single().Agent.ResponseActionId, Is.Null);
        }
    }
}
