using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace WorstHotel.Tests
{
    public sealed class ContinuousServiceSnapshotTests
    {
        static void Require(CommandResult result) => Assert.That(result.Success, Is.True, result.Message);

        static HotelSimulation Create()
        {
            var profiles = new[]
            {
                new GuestProfile(GuestKind.Budget, "Budget", "", 180, .85f, 18, .7f, 28, 90, .55f,
                    needs: new NeedProfile(21, 25, 18, 28, .125f, .25f, 65)),
                new GuestProfile(GuestKind.ColdSensitive, "Cold", "", 300, 1.05f, 20, 1.35f, 16, 65, .4f,
                    needs: new NeedProfile(21, 25, 18, 28, .125f, .25f, 65)),
                new GuestProfile(GuestKind.Business, "Business", "", 450, 1, 19.5f, 1, 10, 45, .25f,
                    needs: new NeedProfile(21, 25, 18, 28, .125f, .25f, 65))
            };
            var settings = new SessionSettings(profiles, Enumerable.Range(101, 6).Select(id =>
                new RoomProfile(id, "Room " + id, noise: 0, temperature: 22)),
                new BoilerSettings(baseWearPerMinute: 0, overloadWearPerMinute: 0), new EconomySettings());
            var hotel = new HotelSimulation(settings, settings.Rooms.Select(room => new RoomState(room)).ToArray(),
                new LivingHotelSettings(firstActivityDelay: 1000), services: new GuestServiceSettings(
                    maxCasesPerShift: 1, eligibility: 0, naturalCommunicationEnabled: true), operations: new OperationsSettings());
            Require(hotel.StartOperations());
            return hotel;
        }

        static void AdvanceTo(HotelSimulation hotel, float target)
        { while (hotel.Elapsed < target) hotel.Tick(Math.Min(1f, target - hotel.Elapsed)); }

        static GuestResponse BeginPhone(HotelSimulation hotel, GuestStay guest)
        {
            Require(hotel.DebugBeginGuestContact(guest.GuestId, GuestContactChannel.Phone));
            var response = hotel.Services.FindResponse(guest.Agent.ResponseActionId);
            // Explicit headless phone-anchor adapter; actual routing is covered by scene tests.
            Require(hotel.SignalGuestResponseAnchorReached(guest.GuestId, response.Id,
                response.ActionVersion, GuestResponseAnchor.RoomPhone));
            return response;
        }

        static HotelSimulation CreateTwoBudgetDays()
        {
            var hotel = Create();
            AdvanceTo(hotel, hotel.Calendar.At(4, 8));
            var first = hotel.BookingOffers.First(offer => offer.ArrivalDay == 4 && offer.Application.Archetype.Kind == GuestKind.Business);
            var second = hotel.BookingOffers.First(offer => offer.ArrivalDay == 4 && offer.Id != first.Id);
            Require(hotel.AcceptBooking(0, first.Id, 101, first.Application.ReferencePrice));
            Require(hotel.AcceptBooking(0, second.Id, 103, second.Application.ReferencePrice));
            AdvanceTo(hotel, Math.Max(first.ArrivalAt, second.ArrivalAt) + 1);
            foreach (var guest in hotel.Guests)
            {
                // Model fixture adapters use the real room/key transition commands.
                Require(hotel.SignalGuestReachedReception(guest.GuestId));
                Require(ModelKeyHandoff.CheckIn(hotel, 0, guest.GuestId));
                Require(hotel.SignalGuestReachedRoom(guest.GuestId));
            }
            var business = hotel.Guests.Single(guest => guest.GuestId == first.Id);
            var other = hotel.Guests.Single(guest => guest.GuestId == second.Id);
            AdvanceTo(hotel, business.Agent.Schedule.SleepTime - hotel.Services.Settings.ReplySeconds + 1);
            Require(hotel.DebugForceService(business.GuestId, ServiceKind.WakeUpCall));
            var response = BeginPhone(hotel, business);
            Require(hotel.AnswerIncomingServiceCall(0, response.Id));
            Require(hotel.RespondToService(0, response.ServiceCaseId, true));
            Assert.That(hotel.Services.Cases.Single().BudgetDay, Is.EqualTo(4));

            AdvanceTo(hotel, hotel.Calendar.At(5, 0) + .25f);
            // A labelled diagnostic wake isolates next-day allowance; normal guest sleep remains unchanged.
            Require(hotel.ForceActivity(other.GuestId, GuestActivity.QuietRest));
            Require(hotel.DebugSetMildCold(other.GuestId));
            hotel.Tick(.25f);
            Require(hotel.DebugForceService(other.GuestId, ServiceKind.ExtraBlanket));
            BeginPhone(hotel, other);
            Require(hotel.TakeServiceItem(0, "blanket:0"));
            Require(hotel.TakeServiceItem(1, "bulb:0"));
            Assert.That(hotel.Services.Cases.Select(item => item.BudgetDay).OrderBy(day => day), Is.EqualTo(new[] { 4, 5 }));
            return hotel;
        }

        static HotelModelSnapshot Wire(HotelSimulation hotel, long sequence) =>
            JsonUtility.FromJson<HotelModelSnapshot>(JsonUtility.ToJson(hotel.CaptureSnapshot(204, sequence)));
        static string State(HotelSimulation hotel) => JsonUtility.ToJson(hotel.CaptureSnapshot(204, 1));

        [Test]
        public void PastDayThreeServicesKeepTheirOriginalBudgetDatesAcrossJsonAndReadOnlyRestore()
        {
            var host = CreateTwoBudgetDays();
            var mirror = Create(); mirror.EnableReadOnlyMirror();
            var packet = Wire(host, 1);
            Assert.That(packet.HasOperations, Is.True);
            Require(mirror.ApplySnapshot(packet));
            Assert.That(State(mirror), Is.EqualTo(State(host)));
            Assert.That(mirror.Services.Cases.Count(item => item.BudgetCharged), Is.EqualTo(2));
            Assert.That(mirror.Services.Settings.MaxCasesPerShift, Is.EqualTo(1),
                "A contact on each of two calendar dates is valid even when their retained total exceeds one daily allowance.");
            Assert.That(mirror.Services.Cases.Select(item => item.BudgetDay).OrderBy(day => day), Is.EqualTo(new[] { 4, 5 }));
            Assert.That(mirror.Services.LastRefillDay, Is.EqualTo(5));
            Assert.That(mirror.Services.Promises.Single().Status, Is.EqualTo(PromiseStatus.Accepted));
            Assert.That(mirror.Services.IncomingCall?.Id, Is.EqualTo(host.Services.IncomingCall.Id));
            Assert.That(mirror.Services.FindItem("blanket:0").PlayerId, Is.EqualTo(0));
            Assert.That(mirror.Services.FindItem("bulb:0").PlayerId, Is.EqualTo(1));
            string before = State(mirror);
            mirror.Tick(1440);
            Assert.That(State(mirror), Is.EqualTo(before));
        }

        [TestCase("zero-charged-day")]
        [TestCase("negative-day")]
        [TestCase("future-budget-day")]
        [TestCase("uncharged-dated-case")]
        [TestCase("future-layer-day")]
        [TestCase("future-refill")]
        [TestCase("duplicate-stock-owner")]
        [TestCase("unknown-guest")]
        [TestCase("nonfinite-deadline")]
        [TestCase("case-cap")]
        [TestCase("promise-cap")]
        [TestCase("item-cap")]
        [TestCase("response-cap")]
        public void InvalidContinuousServiceDataIsRejectedBeforeAnyReplicaMutation(string defect)
        {
            var host = CreateTwoBudgetDays();
            var mirror = Create(); mirror.EnableReadOnlyMirror();
            Require(mirror.ApplySnapshot(Wire(host, 1)));
            string before = State(mirror);
            var packet = Wire(host, 2);
            var layer = packet.ServiceLayer;
            var item = layer.Cases[0];
            switch (defect)
            {
                case "zero-charged-day": item.BudgetDay = 0; break;
                case "negative-day": item.BudgetDay = -1; break;
                case "future-budget-day": item.BudgetDay = layer.Day + 1; break;
                case "uncharged-dated-case": item.BudgetCharged = false; break;
                case "future-layer-day": layer.Day = packet.Day + 1; break;
                case "future-refill": layer.LastRefillDay = packet.Day + 1; break;
                case "duplicate-stock-owner": layer.Items.Single(stock => stock.Id == "bulb:0").PlayerId = 0; break;
                case "unknown-guest": item.GuestId = "absent-guest"; break;
                case "nonfinite-deadline": item.DueTime = float.NaN; break;
                case "case-cap": layer.Cases = Enumerable.Repeat(item, 257).ToArray(); break;
                case "promise-cap": layer.Promises = Enumerable.Repeat(layer.Promises[0], 129).ToArray(); break;
                case "item-cap": layer.Items = Enumerable.Repeat(layer.Items[0], 141).ToArray(); break;
                case "response-cap": layer.Responses = Enumerable.Repeat(layer.Responses[0], 513).ToArray(); break;
            }
            Assert.That(mirror.ApplySnapshot(packet).Success, Is.False, defect);
            Assert.That(State(mirror), Is.EqualTo(before), defect);
            Require(mirror.ApplySnapshot(Wire(host, 3)));
            Assert.That(State(mirror), Is.EqualTo(State(host)));
        }
    }
}
