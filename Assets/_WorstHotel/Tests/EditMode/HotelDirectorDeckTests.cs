using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace WorstHotel.Tests
{
    public sealed partial class HotelDirectorTests
    {
        [Test] public void FirstDayReservesBudgetForALargerPremiseAfterOneMinor()
        {
            var h = Create(budget: 3);
            // A low-cost noisy card isolates size/budget policy without manufacturing a
            // physical lockout or service cause. The production noisy card remains Medium.
            var minor = h.Director.Settings.Deck[0]; minor.Size = HotelSituationSize.Minor; minor.Cost = 1;
            Advance(h, 4);
            Assert.That(h.Director.History.Count, Is.EqualTo(1));
            Good(h.ForceActivity(h.Guests.Single().GuestId, GuestActivity.QuietRest));
            AddGuest(h, 104);
            h.Director.Settings.Deck = new[] { minor, HotelDirectorSettings.DefaultDeck().Single(d => d.Kind == HotelSituationKind.Visitor) };
            Advance(h, 8);
            Assert.That(h.Director.History.Count(r => r.Kind == HotelSituationKind.NoisyEvening), Is.EqualTo(1));
            Assert.That(h.Director.History.Any(r => r.Kind == HotelSituationKind.Visitor), Is.True);
            Assert.That(h.Director.SpentToday, Is.EqualTo(3));
        }

        [Test] public void ClosedUnrestoredWingMayRemainClosedButCannotOpenForSale()
        {
            var c = UnityEditor.AssetDatabase.LoadAssetAtPath<SessionConfig>("Assets/_WorstHotel/ScriptableObjects/PrototypeSession.asset");
            var s = c.ToData();
            var h = new HotelSimulation(s, s.Rooms.Select(r => new RoomState(r)).ToArray(), c.living.ToData(), operations: c.OperationsData());
            Good(h.StartOperations());
            foreach (var p in h.RoomSalesPolicies.Where(p => p.RoomId > 106))
            {
                Good(h.SetRoomSalesPolicy(0, p.RoomId, false, p.Price, p.Revision));
                Assert.That(h.SetRoomSalesPolicy(0, p.RoomId, true, p.Price, p.Revision).Success, Is.False);
            }
        }

        [Test] public void PremiumOpportunityRequiresAcceptanceAndRealEquipmentBeforeDirectorRehearsal()
        {
            var h = Create(HotelSituationKind.SpecialArrival, budget: 6);
            h.Director.Settings.Deck[0].MinimumDay = 1; // Isolate this later-day card in a short model scenario.
            Advance(h, 4);
            var offer = h.SpecialEnquiries.Single(e => e.Status == SpecialOfferStatus.Pending);
            Assert.That(h.FindReservation(offer.Offer.Id), Is.Null);
            Assert.That(h.DecideSpecialBooking(0, offer.Offer.Id, 102, true, 1).Success, Is.False, "Occupied room cannot be overwritten.");
            Good(h.DecideSpecialBooking(0, offer.Offer.Id, 103, true, 1));
            Assert.That(h.FindReservation(offer.Offer.Id).Price, Is.EqualTo(420));
            h.Director.Settings.Enabled = false;
            Advance(h, offer.Offer.ArrivalAt + .3f - h.Elapsed);
            var musician = h.Guests.Single(g => g.GuestId == offer.Offer.Id);
            Good(h.SignalGuestReachedReception(musician.GuestId));
            Good(ModelKeyHandoff.CheckIn(h, 0, musician.GuestId)); Good(h.SignalGuestReachedRoom(musician.GuestId));
            Good(h.ForceActivity(musician.GuestId, GuestActivity.QuietRest));
            h.Director.Settings.Deck = HotelDirectorSettings.DefaultDeck();
            h.Director.Settings.Enabled = true;
            Assert.That(h.Director.EligibleOpportunities().Any(o => o.Definition.Kind == HotelSituationKind.Rehearsal), Is.False);
            var bags = h.Services.Items.Where(i => i.GuestId == musician.GuestId).ToArray();
            // Explicit model parcel boundary. Scene equipment/cart coverage checks the bodies.
            h.Services.SettleOwnLuggage(bags.Single(b => b.Payload == LuggagePayload.Amplifier).Id);
            Assert.That(h.Director.EligibleOpportunities().Any(o => o.Definition.Kind == HotelSituationKind.Rehearsal), Is.False, "The instrument is still outside.");
            h.Services.SettleOwnLuggage(bags.Single(b => b.Payload == LuggagePayload.InstrumentCase).Id);
            // Keep all definitions for history validation; only the rehearsal card is affordable/eligible for selection now.
            foreach (var d in h.Director.Settings.Deck) if (d.Kind != HotelSituationKind.Rehearsal) d.MinimumDay = 20;
            Assert.That(h.Director.EligibleOpportunities().Any(o => o.Definition.Kind == HotelSituationKind.Rehearsal), Is.True);
            float deadline = System.Math.Max(h.Elapsed, h.Director.CooldownUntil) + 8;
            for (int step = 0; step < 200 && h.Director.History.Count < 2 && h.Elapsed < deadline; step++)
            {
                Good(h.ForceActivity(musician.GuestId, GuestActivity.QuietRest));
                h.Tick(.2f);
            }
            Assert.That(h.Director.History.Last().Kind, Is.EqualTo(HotelSituationKind.Rehearsal));
            h.Tick(.2f);
            Assert.That(h.Services.ActiveAmplifier(musician), Is.Not.Null);
            Assert.That(h.Electrical.Consumers.Any(c => c.Id.StartsWith("equipment:") && c.RequestedLoad > 0), Is.True);
            Assert.That(h.Noise.Sources.Any(s => s.Category == NoiseCategory.Amplifier), Is.True);
            Assert.That(h.Boiler.Failed || h.Electrical.Circuits.Any(c => c.Tripped), Is.False);
            var replica = Create(HotelSituationKind.SpecialArrival, budget: 6);
            replica.Director.Settings.Deck = HotelDirectorSettings.DefaultDeck(); replica.EnableReadOnlyMirror();
            Good(replica.ApplySnapshot(JsonUtility.FromJson<HotelModelSnapshot>(JsonUtility.ToJson(h.CaptureSnapshot(70, 3)))));
            Assert.That(replica.Director.History.Last().Kind, Is.EqualTo(HotelSituationKind.Rehearsal));
        }
    }
}
