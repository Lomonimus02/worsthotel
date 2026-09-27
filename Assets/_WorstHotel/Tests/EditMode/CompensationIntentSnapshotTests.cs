using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace WorstHotel.Tests
{
    public sealed class CompensationIntentSnapshotTests
    {
        static HotelModelSnapshot Wire(HotelSimulation hotel, long sequence) =>
            JsonUtility.FromJson<HotelModelSnapshot>(JsonUtility.ToJson(hotel.CaptureSnapshot(808, sequence)));
        static void Require(CommandResult result) => CompensationFixture.Require(result);
        static void Apply(HotelSimulation host, HotelSimulation mirror, long sequence)
        {
            Require(mirror.ApplySnapshot(Wire(host, sequence)));
            Assert.That(CompensationFixture.State(mirror), Is.EqualTo(CompensationFixture.State(host)));
        }

        [TestCase(GuestContactChannel.Phone)]
        [TestCase(GuestContactChannel.Reception)]
        public void ActualDiscussionAndCreditRoundTripWithoutMirrorDecisionsOrPrematureReturn(GuestContactChannel channel)
        {
            var f = CompensationFixture.Create(); f.Disclose(channel);
            var mirror = CompensationFixture.Create(false).Hotel; mirror.EnableReadOnlyMirror(); Apply(f.Hotel, mirror, 1);
            var intent = mirror.Services.CompensationDiscussion(f.Guest.GuestId);
            Assert.That(intent.IncidentId, Is.EqualTo(f.Incident.Id)); Assert.That(intent.ResponseId, Is.EqualTo(f.Response.Id));
            Assert.That(mirror.Guests.Single().Agent.DirectServiceIntentId, Is.EqualTo(intent.Id));
            string before = CompensationFixture.State(mirror);
            Assert.That(mirror.BeginCompensationDiscussion(0, f.Guest.GuestId).Success, Is.False);
            Assert.That(mirror.OfferCompensation(f.Guest.GuestId).Success, Is.False);
            Assert.That(mirror.EndCompensationDiscussion(0, f.Guest.GuestId, intent.Id, intent.Revision).Success, Is.False);
            mirror.Tick(100);
            Assert.That(CompensationFixture.State(mirror), Is.EqualTo(before));
            Require(f.Hotel.OfferCompensation(f.Guest.GuestId)); Apply(f.Hotel, mirror, 2);
            Assert.That(mirror.Services.FindIntent(intent.Id).Status, Is.EqualTo(ServiceIntentStatus.Completed));
            Assert.That(mirror.Guests.Single().CompensationCredit, Is.EqualTo(f.Guest.CompensationCredit));
            Assert.That(mirror.Incidents.Items.Single(item => item.Id == f.Incident.Id).Active, Is.True);
        }

        [Test]
        public void TimeoutAndRoomMoveSnapshotsRemainSelfConsistentAtTheExactCommandBoundary()
        {
            var f = CompensationFixture.Create(); f.Disclose(GuestContactChannel.Phone); var hotel = f.Hotel;
            var mirror = CompensationFixture.Create(false).Hotel; mirror.EnableReadOnlyMirror(); Apply(hotel, mirror, 1);
            var original = f.Discussion;
            Require(hotel.RequestGuestMove(0, f.Guest.GuestId, 106)); Apply(hotel, mirror, 2);
            Require(ModelKeyHandoff.MoveGuest(hotel, 0, f.Guest.GuestId, 106)); Apply(hotel, mirror, 3);
            Assert.That(mirror.Services.FindIntent(original.Id).Status, Is.EqualTo(ServiceIntentStatus.Cancelled));
            var other = CompensationFixture.Create(); other.Disclose(GuestContactChannel.Reception); var pending = other.Discussion;
            other.AdvanceTo(pending.Deadline + .25f);
            var otherMirror = CompensationFixture.Create(false).Hotel; otherMirror.EnableReadOnlyMirror(); Apply(other.Hotel, otherMirror, 1);
            Assert.That(otherMirror.Services.FindIntent(pending.Id).Status, Is.EqualTo(ServiceIntentStatus.TimedOut));
            Assert.That(otherMirror.Guests.Single().Agent.State, Is.EqualTo(GuestAgentState.ReturningFromServiceReception));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void APrivateRecurringEpisodePreservesThePreviousCompletedDiscussionOnTheWire(bool credit)
        {
            var f = CompensationFixture.Create(); f.Disclose(GuestContactChannel.RoomConversation);
            var hotel = f.Hotel; var previous = f.Discussion; var incident = f.Incident;
            string originalResponse = previous.ResponseId; int originalEpisode = incident.EpisodeCount;
            Require(credit ? hotel.OfferCompensation(f.Guest.GuestId) : hotel.AcceptConsequences(0, f.Guest.GuestId));
            var expectedStatus = credit ? ServiceIntentStatus.Completed : ServiceIntentStatus.Cancelled;
            Assert.That(previous.Status, Is.EqualTo(expectedStatus));
            var mirror = CompensationFixture.Create(false).Hotel; mirror.EnableReadOnlyMirror(); Apply(hotel, mirror, 1);

            // Only the physical temperature fixture changes. Normal need/incident ticking must
            // measure recovery and age the configured cooldown; no episode fields are rewritten.
            float recoveredAt = hotel.Elapsed + hotel.NeedsSettings.RecoverySeconds +
                hotel.NeedsSettings.ReopenCooldownSeconds + 1;
            while (hotel.Elapsed < recoveredAt)
            {
                f.Rooms.Single(room => room.Profile.Id == f.Guest.RoomId).Temperature = 22.5f;
                hotel.Tick(.25f);
            }
            Assert.That(incident.Active, Is.False);
            Assert.That(Wire(hotel, 2).Incidents.Single(item => item.Id == incident.Id).ReopenCooldown, Is.Zero);
            Apply(hotel, mirror, 2);
            f.AdvanceTo(hotel.Elapsed + 3);

            Assert.That(f.Incident, Is.SameAs(incident), "Recurrence reuses the stable factual incident identity.");
            Assert.That(incident.Active, Is.True); Assert.That(incident.EpisodeCount, Is.EqualTo(originalEpisode + 1));
            Assert.That(incident.HasContactedStaff, Is.False); Assert.That(f.Response.CommunicatedAt, Is.LessThan(0));
            Assert.That(f.Response.Id, Is.Not.EqualTo(originalResponse));
            Assert.That(hotel.Services.FindResponse(originalResponse), Is.Null, "Unreferenced old contact is normally pruned.");
            Assert.That(previous.ResponseId, Is.Null); Assert.That(previous.IncidentId, Is.EqualTo(incident.Id));
            Assert.That(previous.Status, Is.EqualTo(expectedStatus)); Assert.That(f.Discussion, Is.Null);
            Apply(hotel, mirror, 3);
            Assert.That(mirror.Services.FindIntent(previous.Id).Status, Is.EqualTo(expectedStatus));
            Assert.That(mirror.Incidents.Items.Single(item => item.Id == incident.Id).HasContactedStaff, Is.False);
        }

        [TestCase("missing-cause")]
        [TestCase("case-impersonation")]
        [TestCase("missing-owner")]
        [TestCase("sleeping-owner")]
        [TestCase("past-checkout")]
        [TestCase("private-response")]
        [TestCase("private-current-cause")]
        [TestCase("unearned-credit")]
        [TestCase("old-schema")]
        public void InvalidDiscussionFramesAreRejectedBeforeChangingAnyMirroredState(string defect)
        {
            var f = CompensationFixture.Create(); f.Disclose(GuestContactChannel.Reception);
            var mirror = CompensationFixture.Create(false).Hotel; mirror.EnableReadOnlyMirror(); Apply(f.Hotel, mirror, 1);
            string before = CompensationFixture.State(mirror); var packet = Wire(f.Hotel, 2);
            var intent = packet.ServiceLayer.Intents.Single(item => item.Purpose == ServiceIntentPurpose.CompensationDiscussion);
            var guest = packet.Guests.Single();
            if (defect == "missing-cause") intent.IncidentId = "unknown";
            if (defect == "case-impersonation") intent.CaseId = "invented-agreement";
            if (defect == "missing-owner") guest.Agent.DirectServiceIntentId = null;
            if (defect == "sleeping-owner") guest.Agent.State = GuestAgentState.Sleeping;
            if (defect == "past-checkout") intent.Deadline = guest.Agent.CheckoutTime + 1;
            if (defect == "private-response") packet.ServiceLayer.Responses.Single(item => item.Id == intent.ResponseId).CommunicatedAt = -1;
            if (defect == "private-current-cause") packet.Incidents.Single(item => item.Id == intent.IncidentId).HasContactedStaff = false;
            if (defect == "unearned-credit")
            { intent.Status = ServiceIntentStatus.Completed; intent.ResolutionAt = packet.Time; guest.Agent.DirectServiceIntentId = null; }
            if (defect == "old-schema") packet.Version = HotelModelSnapshot.ProtocolVersion - 1;
            Assert.That(mirror.ApplySnapshot(packet).Success, Is.False, defect);
            Assert.That(CompensationFixture.State(mirror), Is.EqualTo(before), defect);
            Assert.That(mirror.AppliedSnapshotSequence, Is.EqualTo(1));
        }
    }
}
