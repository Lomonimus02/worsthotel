using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace WorstHotel.Tests
{
    public sealed class SpecialGuestTests
    {
        static void Good(CommandResult result) => Assert.That(result.Success, Is.True, result.Message);
        static HotelSimulation Create(bool ordinary = false)
        {
            // Broad comfort and zero incidental services isolate the new scheduling/equipment links.
            var profiles = Enum.GetValues(typeof(GuestKind)).Cast<GuestKind>().Select(k =>
                new GuestProfile(k, k.ToString(), "", 180, 1, 18, 1, 10, 90, .8f,
                    needs: new NeedProfile(0, 40, -10, 50, .9f, 1, 90))).ToArray();
            var settings = new SessionSettings(profiles, Enumerable.Range(101, 10).Select(id => new RoomProfile(id, "Room " + id)),
                new BoilerSettings(baseWearPerMinute: 0, overloadWearPerMinute: 0), new EconomySettings(startingCash: 20000));
            var model = new HotelSimulation(settings, settings.Rooms.Select(r => new RoomState(r)).ToArray(),
                new LivingHotelSettings(firstActivityDelay: 1, rhythm: new GuestRhythmSettings(enabled: true)),
                services: new GuestServiceSettings(eligibility: 0), operations: new OperationsSettings(
                    sales: new SalesSettings(enabled: ordinary, baseDemand: 1, priceElasticity: 0), specialBookings: new SpecialBookingSettings(true)));
            Good(model.StartOperations()); return model;
        }
        static void To(HotelSimulation h, float time, Action step = null)
        {
            while (time - h.Elapsed > .0001f)
            {
                h.Tick(Math.Min(.25f, time - h.Elapsed));
                // Explicit headless route/anchor boundary adapters, never a physical navigation claim.
                foreach (var guest in h.Guests)
                {
                    var a = guest.Agent;
                    if (a.State == GuestAgentState.LeavingRoom) Good(h.SignalGuestLeftRoom(guest.GuestId));
                    if (a.State == GuestAgentState.ReturningToRoom) Good(h.SignalGuestReturnedRoom(guest.GuestId));
                    if (a.InAssignedRoom && !a.ActivityStaged && string.IsNullOrEmpty(a.ResponseActionId))
                        Good(h.SignalGuestActivityReady(guest.GuestId, a.State, a.Activity));
                }
                step?.Invoke();
            }
        }
        static SpecialBookingEnquiry Offer(HotelSimulation h, SpecialGuestKind kind)
        {
            for (int day = 2; day <= 6; day += 2)
            {
                To(h, h.Calendar.At(day, 10) + .25f);
                var offer = h.SpecialEnquiries.Single(e => e.Status == SpecialOfferStatus.Pending);
                if (offer.Definition.Kind == kind) return offer;
                Good(h.DecideSpecialBooking(0, offer.Offer.Id, 0, false, offer.Revision));
            }
            throw new Exception("Expected special enquiry missing.");
        }
        static GuestStay Arrive(HotelSimulation h, SpecialBookingEnquiry e, int room, bool checkIn = true)
        {
            Good(h.DecideSpecialBooking(0, e.Offer.Id, room, true, e.Revision));
            To(h, e.Offer.ArrivalAt + .25f);
            var guest = h.Guests.Single(g => g.GuestId == e.Offer.Id);
            Good(h.SignalGuestReachedReception(guest.GuestId));
            Good(h.Services.OfferLuggage(0, guest.GuestId, false));
            if (checkIn)
            {
                Good(ModelKeyHandoff.CheckIn(h, 0, guest.GuestId));
                Good(h.SignalGuestReachedRoom(guest.GuestId));
                Good(h.RegisterGuestPhysicalStaging(guest.GuestId));
            }
            return guest;
        }
        static void Mirror(HotelSimulation host, bool ordinary = false)
        {
            var replica = Create(ordinary); replica.EnableReadOnlyMirror();
            // Use the same JSON boundary as LAN, including enum and payload fields.
            var packet = JsonUtility.FromJson<HotelModelSnapshot>(JsonUtility.ToJson(host.CaptureSnapshot(31, 1)));
            Good(replica.ApplySnapshot(packet));
            Assert.That(replica.SpecialEnquiries.Count, Is.EqualTo(host.SpecialEnquiries.Count));
            Assert.That(replica.Services.Items.Count, Is.EqualTo(host.Services.Items.Count));
            Assert.That(replica.DecideSpecialBooking(0, "special-2", 101, true, 1).Success, Is.False);
        }

        [Test]
        public void RareEnquiriesRequireExplicitDecisionWhileOrdinarySalesContinue()
        {
            var h = Create(true);
            To(h, h.Calendar.At(1, 12));
            Assert.That(h.Reservations.Count, Is.GreaterThan(0));
            Assert.That(h.Reservations.All(r => r.IsAutomatic), Is.True);
            Assert.That(h.SpecialEnquiries, Is.Empty);
            var offer = Offer(h, SpecialGuestKind.TouringMusician);
            Assert.That(h.FindReservation(offer.Offer.Id), Is.Null);
            Mirror(h, true);
            Assert.That(h.DecideSpecialBooking(0, offer.Offer.Id, 108, true, 1).Success, Is.False, "Closed wing cannot be booked.");
            Assert.That(h.DecideSpecialBooking(0, offer.Offer.Id, 105, true, 99).Success, Is.False);
            Good(h.DecideSpecialBooking(1, offer.Offer.Id, 0, false, 1));
            Assert.That(h.DecideSpecialBooking(0, offer.Offer.Id, 105, true, 1).Success, Is.False);
            Mirror(h, true);
            To(h, h.Calendar.At(3, 10));
            Assert.That(h.SpecialEnquiries.All(e => e.Status != SpecialOfferStatus.Pending), Is.True);
        }

        [TestCase(102)]
        [TestCase(108)]
        public void DeliveredAmplifierUsesActualCircuitNoiseQuietRequestAndStopsWhenCarried(int room)
        {
            var h = Create(); if (room > 106) Good(h.RestoreNorthWing(0));
            var guest = Arrive(h, Offer(h, SpecialGuestKind.TouringMusician), room);
            var bags = h.Services.Items.Where(i => i.GuestId == guest.GuestId).ToArray();
            Assert.That(bags.Select(i => i.Payload), Is.EquivalentTo(new[] { LuggagePayload.Suitcase, LuggagePayload.InstrumentCase, LuggagePayload.Amplifier }));
            Good(h.ForceActivity(guest.GuestId, GuestActivity.Rehearsal));
            Good(h.SignalGuestActivityReady(guest.GuestId, guest.Agent.State, guest.Agent.Activity));
            var amp = bags.Single(i => i.Payload == LuggagePayload.Amplifier);
            h.Tick(.25f);
            Assert.That(h.Electrical.Consumers.Any(c => c.Id == "equipment:" + amp.Id), Is.False);
            Good(h.Services.PlaceLuggage(amp.Id, false)); // labelled delivery-zone adapter
            h.Tick(.25f);
            var load = h.Electrical.Consumers.Single(c => c.Id == "equipment:" + amp.Id);
            Assert.That(load.CircuitId, Is.EqualTo("B")); Assert.That(load.RequestedLoad, Is.EqualTo(1.4f));
            Assert.That(h.Electrical.Find("B").Tripped, Is.False, "A musician alone must not force a failure.");
            Assert.That(h.Noise.Sources.Single(s => s.Category == NoiseCategory.Amplifier).SourceEntityId, Is.EqualTo(amp.Id));
            float loud = h.Noise.Sources.Single(s => s.Category == NoiseCategory.Amplifier).NoiseOutput;
            Mirror(h);
            Good(h.RequestQuiet(0, guest.GuestId));
            Assert.That(h.Noise.Sources.Single(s => s.Category == NoiseCategory.Amplifier).NoiseOutput, Is.LessThan(loud));
            Assert.That(guest.Memory.PreviousNoiseWarnings, Is.EqualTo(1));
            Good(h.Electrical.ForceTrip("B")); h.Tick(.25f); // isolated blackout, not musician event logic
            Assert.That(h.Noise.Sources.Any(s => s.Category == NoiseCategory.Amplifier), Is.False);
            Assert.That(h.Electrical.Consumers.Single(c => c.Id == "equipment:" + amp.Id).DeliveredLoad, Is.Zero);
            Good(h.ResetCircuit(0, "B"));
            Good(h.Services.TakeItem(0, amp.Id)); h.Tick(.25f);
            Assert.That(h.Electrical.Consumers.Any(c => c.Id == "equipment:" + amp.Id), Is.False);
            Assert.That(h.Noise.Sources.Any(s => s.Category == NoiseCategory.Amplifier), Is.False);
            Mirror(h);
            Good(h.Services.DropItem(0, amp.Id)); Good(h.Services.PlaceLuggage(amp.Id, false)); h.Tick(.25f);
            Good(h.AskToUnplugAmplifier(0, guest.GuestId));
            Assert.That(h.Electrical.Consumers.Any(c => c.Id == "equipment:" + amp.Id), Is.False);
            Assert.That(amp.EquipmentSwitchedOff, Is.True); Mirror(h);
        }

        [Test]
        public void ScheduledRehearsalAndOrdinaryHeaterCanTripTheRealBreakerTogether()
        {
            var h = Create(); var guest = Arrive(h, Offer(h, SpecialGuestKind.TouringMusician), 102);
            var amp = h.Services.Items.Single(i => i.GuestId == guest.GuestId && i.Payload == LuggagePayload.Amplifier);
            Good(h.Services.PlaceLuggage(amp.Id, false));
            Good(h.Heaters.Register("test-heater")); Good(h.Heaters.AssignRoom("test-heater", 102));
            Good(h.Heaters.SetSwitchedOn("test-heater", true));
            Assert.That(h.Electrical.Find("B").Tripped, Is.False);
            for (int i = 0; i < 500 && !h.Electrical.Find("B").Tripped; i++) To(h, h.Elapsed + .25f);
            Assert.That(h.Electrical.Find("B").Tripped, Is.True, "Normal room + 2-unit heater + delivered amp must accumulate real overload stress.");
            Assert.That(h.Electrical.Find("B").ActualRequestedLoad, Is.EqualTo(4.25f).Within(.01f));
            Assert.That(h.Noise.Sources.Any(s => s.Category == NoiseCategory.Amplifier), Is.False);
            Good(h.AskToUnplugAmplifier(0, guest.GuestId)); Good(h.ResetCircuit(0, "B"));
            Assert.That(h.Electrical.Find("B").RequestedLoad, Is.LessThan(h.Electrical.Find("B").Capacity));
        }

        [Test]
        public void SixBagsUseOneResponsibilityAndExistingStorageCarryDelivery()
        {
            var h = Create(); var guest = Arrive(h, Offer(h, SpecialGuestKind.Overpacker), 105);
            var bags = h.Services.Items.Where(i => i.GuestId == guest.GuestId).ToArray();
            Assert.That(bags.Length, Is.EqualTo(6)); Assert.That(bags.All(i => i.StaffHandling), Is.True);
            Assert.That(bags.Select(i => i.Id).Distinct().Count(), Is.EqualTo(6));
            foreach (var bag in bags)
            {
                Good(h.Services.PlaceLuggage(bag.Id, true));
                Good(h.Services.TakeItem(0, bag.Id)); Good(h.Services.DropItem(0, bag.Id));
                Good(h.Services.PlaceLuggage(bag.Id, false));
            }
            Assert.That(h.Services.Cases.Count(c => c.GuestId == guest.GuestId && c.Kind == ServiceKind.LuggageStorage), Is.LessThanOrEqualTo(1));
            Assert.That(guest.Price, Is.EqualTo(480)); Mirror(h);
        }

        [Test]
        public void NightOwlActuallyReturnsWatchesTelevisionAndShowersAfterMidnight()
        {
            var h = Create(); var e = Offer(h, SpecialGuestKind.NightOwl); var guest = Arrive(h, e, 106);
            int nextDay = e.Offer.ArrivalDay + 1;
            Assert.That(guest.Agent.Schedule.SleepTime, Is.EqualTo(h.Calendar.At(nextDay, 4.25f)));
            Assert.That(guest.Agent.Schedule.WakeTime, Is.EqualTo(h.Calendar.At(nextDay, 9.25f)));
            bool outing = false, tv = false, shower = false;
            To(h, h.Calendar.At(nextDay, 4), () =>
            {
                outing |= guest.Agent.State == GuestAgentState.GuestAway;
                if (h.Calendar.Day == nextDay && guest.Agent.ActivityStaged)
                {
                    tv |= guest.Agent.Activity == GuestActivity.WatchTV && h.Electrical.Consumers.Any(c => c.Id == "guest:" + guest.GuestId && c.DeliveredLoad > 0);
                    shower |= guest.Agent.Activity == GuestActivity.Shower && h.HeatingDemands.Any(d => d.RoomId == 106 && d.HotWater > 0);
                }
            });
            Assert.That(outing && tv && shower, Is.True, "Actual itinerary must produce the late-night loads.");
            To(h, h.Calendar.At(nextDay, 5)); Assert.That(guest.Agent.State, Is.EqualTo(GuestAgentState.Sleeping));
            Mirror(h);
        }
    }
}
