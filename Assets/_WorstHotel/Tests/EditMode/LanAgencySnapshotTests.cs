using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace WorstHotel.Tests
{
    /// <summary>Real causal guest episodes transported through JSON. Model route callbacks are explicit headless fixtures.</summary>
    public sealed class LanAgencySnapshotTests
    {
        const long Epoch = 303;
        const string SourceId = "agency-source", AffectedId = "agency-affected";

        sealed class Fixture
        {
            public HotelSimulation Hotel;
            public RoomState[] Rooms;
            public GuestStay Source => Hotel.Guests.Single(g => g.GuestId == SourceId);
            public GuestStay Affected => Hotel.Guests.Single(g => g.GuestId == AffectedId);
            public HotelIncident Case => Hotel.Incidents.Items.Single(i => i.GuestId == AffectedId && i.Reason == IncidentReason.Noise);
        }

        static Fixture Create(bool populated)
        {
            var budget = new GuestProfile(GuestKind.Budget, "Budget", "", 180, .85f, 18, .7f, 28, 90, .55f);
            var cold = new GuestProfile(GuestKind.ColdSensitive, "Cold", "", 300, 1.05f, 20, 1.35f, 16, 65, .4f);
            var business = new GuestProfile(GuestKind.Business, "Business", "", 450, 1, 19.5f, 1, 10, 45, .2f,
                needs: new NeedProfile(21, 25, 18, 28, .08f, .2f, 45));
            var settings = new SessionSettings(new[] { budget, cold, business },
                Enumerable.Range(101, 6).Select(id => new RoomProfile(id, "Room " + id, noise: 0, temperature: 22.5f)),
                new BoilerSettings(), new EconomySettings(), serviceSeconds: 600);
            var living = new LivingHotelSettings(firstArrivalSeconds: .2f, arrivalSpacingSeconds: .2f, arrivalJitterSeconds: 0,
                firstActivityDelay: 1000, activityDurationMin: 120, activityDurationMax: 120);
            var needs = new NeedSettings(buildupPerSecond: .5f, complaintExposureSeconds: 2,
                escalatedExposureSeconds: 6, criticalExposureSeconds: 12, recoverySeconds: 2,
                reopenCooldownSeconds: 2, compensationReliefSeconds: 5, repeatPatienceReduction: .25f);
            var rooms = settings.Rooms.Select(p => new RoomState(p)).ToArray();
            var hotel = new HotelSimulation(settings, rooms, living, needs);
            var fixture = new Fixture { Hotel = hotel, Rooms = rooms };
            if (!populated) return fixture;
            var offers = new[] { new BookingApplication(SourceId, "Noisy caller", budget, 180),
                new BookingApplication(AffectedId, "Returning business guest", business, 450) };
            Assert.That(hotel.StartShift(new[] { new BookingAssignment(103, SourceId, 180, 0),
                new BookingAssignment(102, AffectedId, 450, 0) }, offers).Success, Is.True);
            hotel.Tick(.8f);
            foreach (var guest in hotel.Guests)
            {
                Assert.That(hotel.SignalGuestReachedReception(guest.GuestId).Success, Is.True);
                Assert.That(ModelKeyHandoff.CheckIn(hotel, 0, guest.GuestId).Success, Is.True);
                Assert.That(hotel.SignalGuestReachedRoom(guest.GuestId).Success, Is.True);
                Assert.That(hotel.ForceActivity(guest.GuestId, GuestActivity.QuietRest).Success, Is.True);
            }
            Assert.That(hotel.ForceActivity(SourceId, GuestActivity.LoudRoom).Success, Is.True);
            Advance(fixture, 3);
            Assert.That(fixture.Case.Stage, Is.EqualTo(SituationStage.Complaint));
            Assert.That(hotel.RequestQuiet(0, SourceId).Success, Is.True);
            Advance(fixture, hotel.NoiseSettings.QuietRequestSeconds + 3);
            Assert.That(fixture.Case.EpisodeCount, Is.EqualTo(2), "The wire fixture needs actual repeated-problem memory.");
            Assert.That(fixture.Affected.Memory.ProblemsResolvedSuccessfully, Is.EqualTo(1));
            Assert.That(hotel.OfferCompensation(AffectedId).Success, Is.True);
            Assert.That(fixture.Case.Active, Is.True, "The packet must carry an active cause despite compensation.");
            return fixture;
        }

        static void Advance(Fixture fixture, float duration)
        {
            while (duration > .00001f)
            {
                foreach (var room in fixture.Rooms) room.Temperature = 22.5f;
                float step = Math.Min(.2f, duration);
                fixture.Hotel.Tick(step); duration -= step;
            }
        }

        static Fixture Mirror()
        {
            var mirror = Create(false);
            mirror.Hotel.EnableReadOnlyMirror(); return mirror;
        }
        static HotelModelSnapshot Wire(HotelSimulation hotel, long sequence) =>
            JsonUtility.FromJson<HotelModelSnapshot>(JsonUtility.ToJson(hotel.CaptureSnapshot(Epoch, sequence)));
        static string State(HotelSimulation hotel) => JsonUtility.ToJson(hotel.CaptureSnapshot(Epoch, 1));
        static IncidentSnapshot NoiseCase(HotelModelSnapshot packet) => packet.Incidents.Single(i => i.GuestId == AffectedId && i.Reason == IncidentReason.Noise);
        static GuestSnapshot Affected(HotelModelSnapshot packet) => packet.Guests.Single(g => g.Application.Id == AffectedId);

        [Test]
        public void JsonWirePreservesRealSourceCauseHistoryRepeatPatienceMemoryAndExperiencedNoise()
        {
            var host = Create(true);
            var mirror = Mirror();
            int callbacks = 0;
            mirror.Hotel.Incidents.OnSituationChanged += _ => callbacks++;
            mirror.Hotel.Requests.OnRequestCreated += _ => callbacks++;
            mirror.Hotel.Requests.OnRequestResolved += _ => callbacks++;
            var result = mirror.Hotel.ApplySnapshot(Wire(host.Hotel, 1));
            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(State(mirror.Hotel), Is.EqualTo(State(host.Hotel)));
            var source = mirror.Hotel.Noise.Sources.Single();
            Assert.That(source.SourceGuestId, Is.EqualTo(SourceId));
            Assert.That(source.Category, Is.EqualTo(NoiseCategory.Television));
            Assert.That(source.SourceRoomId, Is.EqualTo(103));
            Assert.That(source.NoiseOutput, Is.GreaterThan(0));
            var heard = mirror.Affected.Perception.NoiseSources.Single();
            Assert.That(heard.SourceEntityId, Is.EqualTo(source.SourceEntityId));
            Assert.That(heard.ReceivedNoise, Is.EqualTo(mirror.Hotel.Noise.GetContributions(102).Single().ReceivedNoise));
            Assert.That(mirror.Affected.Perception.Noise, Is.EqualTo(heard.ReceivedNoise).Within(.0001f));
            Assert.That(mirror.Case.Cause.SourceEntityId, Is.EqualTo(source.SourceEntityId));
            Assert.That(mirror.Case.Cause.SourceGuestId, Is.EqualTo(SourceId));
            Assert.That(mirror.Case.Cause.ReceivedIntensity, Is.EqualTo(heard.ReceivedNoise).Within(.0001f));
            Assert.That(mirror.Case.Key, Is.EqualTo(host.Case.Key));
            Assert.That(mirror.Case.EffectivePatienceMultiplier, Is.LessThan(1));
            Assert.That(mirror.Case.Patience, Is.EqualTo(host.Case.Patience));
            Assert.That(mirror.Case.History.Select(h => (h.Time, h.Reason)), Is.EqualTo(host.Case.History.Select(h => (h.Time, h.Reason))));
            Assert.That(mirror.Affected.Memory.NumberOfComplaints, Is.EqualTo(2));
            Assert.That(mirror.Affected.Memory.RepeatedProblemCount[IncidentReason.Noise], Is.EqualTo(1));
            Assert.That(mirror.Affected.Memory.CompensationReceived, Is.GreaterThan(0));
            Assert.That(mirror.Affected.Memory.ProblemsResolvedSuccessfully, Is.EqualTo(1));
            Assert.That(mirror.Source.Memory.PreviousNoiseWarnings, Is.EqualTo(1));
            Assert.That(callbacks, Is.Zero);

            var retainedGuest = mirror.Affected;
            var retainedCase = mirror.Case;
            Assert.That(host.Hotel.RequestQuiet(0, SourceId).Success, Is.True);
            Advance(host, host.Hotel.NeedsSettings.RecoverySeconds + .4f);
            result = mirror.Hotel.ApplySnapshot(Wire(host.Hotel, 2));
            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(mirror.Affected, Is.SameAs(retainedGuest));
            Assert.That(mirror.Case, Is.SameAs(retainedCase));
            Assert.That(mirror.Case.Resolved, Is.True);
            Assert.That(mirror.Affected.Memory.ProblemsResolvedSuccessfully, Is.EqualTo(2));
            Assert.That(mirror.Source.Memory.PreviousNoiseWarnings, Is.EqualTo(2));
            Assert.That(State(mirror.Hotel), Is.EqualTo(State(host.Hotel)));
            Assert.That(callbacks, Is.Zero, "Installing later recovery must not replay gameplay notifications on the client.");
        }

        [Test]
        public void CaptureAndAppliedAgencyDataAreDeepCopiesAcrossMemoryPerceptionCauseAndHistory()
        {
            var host = Create(true);
            var mirror = Mirror();
            var capture = host.Hotel.CaptureSnapshot(Epoch, 1);
            var packet = Wire(host.Hotel, 1);
            var applied = mirror.Hotel.ApplySnapshot(packet);
            Assert.That(applied.Success, Is.True, applied.Message);
            string before = State(host.Hotel);
            Assert.That(mirror.Affected.Memory, Is.Not.SameAs(host.Affected.Memory));
            Assert.That(mirror.Affected.Perception, Is.Not.SameAs(host.Affected.Perception));
            Assert.That(mirror.Case.Cause, Is.Not.SameAs(host.Case.Cause));
            foreach (var dto in new[] { capture, packet })
            {
                Affected(dto).Memory.NumberOfComplaints += 20;
                Affected(dto).Memory.Categories.Single(c => c.Reason == IncidentReason.Noise).Count += 20;
                Affected(dto).Perception.NoiseSources[0].NoiseOutput = 0;
                Affected(dto).Perception.NoiseSources[0].SourceEntityId = "mutated receiver object";
                NoiseCase(dto).Cause.Description = "mutated cause object";
                NoiseCase(dto).History[0].Reason = "mutated history object";
                dto.NoiseSources[0].NoiseOutput = 0;
            }
            Assert.That(State(host.Hotel), Is.EqualTo(before));
            Assert.That(State(mirror.Hotel), Is.EqualTo(before));
        }

        [Test]
        public void MissingSourceGuestsAndMalformedCausalPacketsRejectAtomicallyWithoutConsumingSequence()
        {
            var host = Create(true);
            var mirror = Mirror();
            Assert.That(mirror.Hotel.ApplySnapshot(Wire(host.Hotel, 7)).Success, Is.True);
            string before = State(mirror.Hotel);
            var corruptions = new Action<HotelModelSnapshot>[]
            {
                p => p.NoiseSources[0].SourceGuestId = "missing-source-guest",
                p => p.NoiseSources[0].SourceEntityId = "unidentified-tv",
                p => NoiseCase(p).Cause.SourceGuestId = "missing-source-guest",
                p => NoiseCase(p).Cause.SourceGuestId = null,
                p => NoiseCase(p).Id += ":wrong-cause",
                p => NoiseCase(p).Cause.SourceEntityId = "a-different-source",
                p => Affected(p).Perception.NoiseSources[0].SourceGuestId = "missing-source-guest",
                p => NoiseCase(p).History[0].Time = -1,
                p => NoiseCase(p).EffectivePatienceMultiplier = 0,
                p => Affected(p).Memory.NumberOfComplaints = -1
            };
            foreach (var corrupt in corruptions)
            {
                var packet = Wire(host.Hotel, 8);
                corrupt(packet);
                Assert.That(mirror.Hotel.ApplySnapshot(packet).Success, Is.False);
                Assert.That(mirror.Hotel.AppliedSnapshotSequence, Is.EqualTo(7));
                Assert.That(State(mirror.Hotel), Is.EqualTo(before));
            }
            var accepted = mirror.Hotel.ApplySnapshot(Wire(host.Hotel, 8));
            Assert.That(accepted.Success, Is.True, accepted.Message);
            Assert.That(mirror.Hotel.AppliedSnapshotSequence, Is.EqualTo(8));
        }

        [Test]
        public void ReplicaCannotAdvanceGuestLifePerceptionSituationsOrGuestResponses()
        {
            var host = Create(true);
            var mirror = Mirror();
            Assert.That(mirror.Hotel.ApplySnapshot(Wire(host.Hotel, 1)).Success, Is.True);
            string before = State(mirror.Hotel);
            mirror.Hotel.Tick(40);
            mirror.Hotel.Clock.Advance(40);
            mirror.Hotel.Incidents.TickLiving(mirror.Hotel.Guests, mirror.Rooms, 40);
            mirror.Hotel.Noise.Tick(mirror.Hotel.Guests, mirror.Rooms, mirror.Hotel.Elapsed + 40);
            mirror.Hotel.NeedEvaluator.Tick(mirror.Affected, mirror.Rooms.Single(r => r.Profile.Id == 102), 40);
            mirror.Hotel.NeedEvaluator.ApplyCompensationRelief(mirror.Affected, new[] { IncidentReason.Noise });
            Assert.That(mirror.Hotel.RequestQuiet(0, SourceId).Success, Is.False);
            Assert.That(mirror.Hotel.ForceActivity(SourceId, GuestActivity.PhoneCall).Success, Is.False);
            Assert.That(mirror.Hotel.ForceSleep(SourceId).Success, Is.False);
            Assert.That(mirror.Hotel.ForceLeaveRoom(SourceId).Success, Is.False);
            Assert.That(mirror.Hotel.SkipActivity(SourceId).Success, Is.False);
            Assert.That(mirror.Hotel.OfferCompensation(AffectedId).Success, Is.False);
            Assert.That(mirror.Hotel.AcceptConsequences(0, AffectedId).Success, Is.False);
            Assert.That(mirror.Hotel.RequestGuestMove(0, AffectedId, 106).Success, Is.False);
            Assert.That(State(mirror.Hotel), Is.EqualTo(before));
        }
    }
}
