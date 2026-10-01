using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace WorstHotel.Tests
{
    public sealed partial class HotelDirectorTests
    {
        static void Good(CommandResult r) => Assert.That(r.Success, Is.True, r.Message);
        static HotelSimulation Create(HotelSituationKind kind = HotelSituationKind.NoisyEvening, float budget = 3, float hour = 16)
        {
            // Broad comfort, no wear and no random willingness isolate Director decisions.
            // Physical travel is explicitly acknowledged only in this model fixture.
            var profiles = Enum.GetValues(typeof(GuestKind)).Cast<GuestKind>().Select(k => new GuestProfile(k, k.ToString(), "", 180,
                1, 18, 1, 10, 90, .9f, needs: new NeedProfile(0, 40, -10, 50, .9f, 1, 90))).ToArray();
            var settings = new SessionSettings(profiles, Enumerable.Range(101, 6).Select(i => new RoomProfile(i, "Room " + i)),
                new BoilerSettings(safeLoad: 20, baseWearPerMinute: 0, overloadWearPerMinute: 0), new EconomySettings(startingCash: 20000));
            var director = new HotelDirectorSettings { Enabled = true, QuietSeconds = 3, ConsiderEverySeconds = .2f, RecoverySeconds = 6,
                FirstDayBudget = budget, MaximumBudget = Math.Max(6, budget), Deck = new[] { HotelDirectorSettings.DefaultDeck().Single(d => d.Kind == kind) } };
            var h = new HotelSimulation(settings, settings.Rooms.Select(r => new RoomState(r)).ToArray(),
                new LivingHotelSettings(rhythm: new GuestRhythmSettings(enabled: true)),
                electricity: new ElectricitySettings(circuitCapacity: 20),
                services: new GuestServiceSettings(eligibility: 0, naturalCommunicationEnabled: true),
                operations: new OperationsSettings(secondsPerDay: 480, startHour: hour, sales: new SalesSettings(enabled: false),
                    specialBookings: new SpecialBookingSettings(true)), director: director);
            Good(h.StartOperations()); ManualTurnoverModelAdapter.PrepareRoom(h, 101);
            AddGuest(h, 102);
            return h;
        }
        static GuestStay AddGuest(HotelSimulation h, int room)
        {
            Good(h.DebugSpawnGuest(GuestKind.Budget, room)); h.Tick(1.1f);
            var g = h.Guests.Single(g => g.RoomId == room && !g.ReceiptPosted);
            Good(h.SignalGuestReachedReception(g.GuestId)); Good(ModelKeyHandoff.CheckIn(h, 0, g.GuestId));
            Good(h.SignalGuestReachedRoom(g.GuestId)); Good(h.ForceActivity(g.GuestId, GuestActivity.QuietRest));
            return g;
        }
        static void Advance(HotelSimulation h, float seconds)
        { for (float t = 0; t < seconds; t += .2f) h.Tick(Math.Min(.2f, seconds - t)); }

        [Test] public void QuietDwellStartsRealActivityWithoutInventingComplaintOrFailure()
        {
            var h = Create(); var g = h.Guests.Single();
            Advance(h, 2); Assert.That(h.Director.History, Is.Empty);
            Advance(h, 2); Assert.That(h.Director.History.Count, Is.EqualTo(1));
            Assert.That(g.Agent.Activity, Is.EqualTo(GuestActivity.LoudRoom));
            Assert.That(h.Noise.Sources.Any(s => s.SourceGuestId == g.GuestId && s.Category == NoiseCategory.Television), Is.True);
            Assert.That(h.Incidents.Items.Any(i => i.HasContactedStaff), Is.False);
            Assert.That(h.Boiler.Failed || h.Electrical.Circuits.Any(c => c.Tripped), Is.False);
        }
        [Test] public void OnlyEligibleDeckEntriesAreConsidered()
        {
            var h = Create(HotelSituationKind.Rehearsal);
            h.Director.Settings.Deck = HotelDirectorSettings.DefaultDeck();
            var opportunities = h.Director.EligibleOpportunities();
            Assert.That(opportunities.Any(o => o.Definition.Kind == HotelSituationKind.Rehearsal || o.Definition.Kind == HotelSituationKind.ExtraBlanket), Is.False);
            Assert.That(opportunities.Any(o => o.Definition.Kind == HotelSituationKind.Visitor), Is.True);
        }
        [Test] public void ActualFailureSuppressesOpportunitiesAndQuietDwell()
        {
            var h = Create(); h.Boiler.ForceFailure(); Advance(h, 9);
            Assert.That(h.Director.Pressure.Band, Is.EqualTo(HotelPressureBand.Overloaded));
            Assert.That(h.Director.QuietElapsed, Is.Zero); Assert.That(h.Director.History, Is.Empty);
        }
        [Test] public void CooldownAndPerStayHistoryPreventRepeatEvenAfterNaturalResolution()
        {
            var h = Create(budget: 6); Advance(h, 4); var g = h.Guests.Single();
            Good(h.ForceActivity(g.GuestId, GuestActivity.QuietRest)); Advance(h, 1);
            Assert.That(h.Director.History.Single().Active, Is.False);
            Assert.That(h.Director.CooldownUntil, Is.GreaterThan(h.Elapsed));
            Advance(h, 8); Assert.That(h.Director.History.Count, Is.EqualTo(1));
            Assert.That(h.Director.EligibleOpportunities(), Is.Empty);
        }
        [Test] public void BudgetCannotBeSpentOnAnUnaffordablePremise()
        {
            var h = Create(budget: 1); Advance(h, 8);
            Assert.That(h.Director.History, Is.Empty); Assert.That(h.Director.SpentToday, Is.Zero);
        }
        [Test] public void SleepingGuestIsNotAnActivityParticipant()
        {
            var h = Create(); Good(h.ForceSleep(h.Guests.Single().GuestId)); Advance(h, 4);
            Assert.That(h.Director.History, Is.Empty);
        }
        [Test] public void AcceleratedSleepTimeDoesNotBuyDirectorQuietTime()
        {
            var h = Create(); h.Clock.SetSpeed(8); Advance(h, 5);
            Assert.That(h.Director.History, Is.Empty); Assert.That(h.Director.QuietElapsed, Is.Zero);
        }
        [Test] public void VisitorNeedsPhysicalArrivalBeforeConsumingUtilitiesAndCanBeDismissed()
        {
            var h = Create(HotelSituationKind.Visitor); Advance(h, 4);
            var v = h.Director.Visitors.Single();
            Assert.That(h.Electrical.Consumers.Any(c => c.Id == v.Id), Is.False);
            Good(h.Director.SignalVisitorReached(v.Id, HotelVisitorState.Arriving));
            Good(h.Director.DecideVisitor(0, v.Id, true));
            Assert.That(h.Director.SignalVisitorReached(v.Id, HotelVisitorState.Arriving).Success, Is.False);
            Good(h.Director.SignalVisitorReached(v.Id, HotelVisitorState.GoingToRoom)); h.Tick(.2f);
            Assert.That(h.Electrical.Consumers.Single(c => c.Id == v.Id).RequestedLoad, Is.EqualTo(.45f));
            Assert.That(h.Noise.Sources.Any(s => s.Category == NoiseCategory.Visitor && s.SourceRoomId == v.RoomId), Is.True);
            Assert.That(h.Director.VisitorHeatFor(v.RoomId), Is.GreaterThan(0));
            Good(h.RequestQuiet(0, v.HostGuestId)); h.Tick(.2f);
            Assert.That(h.Noise.Sources.Single(s => s.Category == NoiseCategory.Visitor).NoiseOutput, Is.LessThan(.1f));
            Good(h.Director.DecideVisitor(1, v.Id, false)); h.Tick(.2f);
            Assert.That(h.Electrical.Consumers.Any(c => c.Id == v.Id), Is.False);
            Good(h.Director.SignalVisitorReached(v.Id, HotelVisitorState.Leaving)); h.Tick(.2f);
            Assert.That(h.Director.History.Single().Active, Is.False);
        }
        [Test] public void HostSnapshotCarriesVisitorBudgetAndHistoryAndRejectsMalformedUpdateAtomically()
        {
            var host = Create(HotelSituationKind.Visitor); Advance(host, 4);
            var replica = Create(HotelSituationKind.Visitor); replica.EnableReadOnlyMirror();
            var data = JsonUtility.FromJson<HotelModelSnapshot>(JsonUtility.ToJson(host.CaptureSnapshot(70, 1)));
            Good(replica.ApplySnapshot(data));
            Assert.That(replica.Director.History.Count, Is.EqualTo(1));
            Assert.That(replica.Director.SpentToday, Is.EqualTo(host.Director.SpentToday));
            Assert.That(replica.Director.DecideVisitor(1, host.Director.Visitors.Single().Id, true).Success, Is.False);
            replica.Tick(4); Assert.That(replica.Elapsed, Is.EqualTo(host.Elapsed));
            data.Sequence++; data.Director.Visitors[0].RoomId = 999;
            Assert.That(replica.ApplySnapshot(data).Success, Is.False);
            Assert.That(replica.Director.Visitors.Single().RoomId, Is.EqualTo(102));
        }
        [Test] public void SameSeedAndHotelProduceSameChoice()
        {
            var a = Create(); var b = Create();
            a.Director.Settings.Deck = HotelDirectorSettings.DefaultDeck(); b.Director.Settings.Deck = HotelDirectorSettings.DefaultDeck();
            Advance(a, 4); Advance(b, 4);
            Assert.That(a.Director.History.Single().Kind, Is.EqualTo(b.Director.History.Single().Kind));
            Assert.That(a.Director.History.Single().GuestId, Is.EqualTo(b.Director.History.Single().GuestId));
        }
    }
}
