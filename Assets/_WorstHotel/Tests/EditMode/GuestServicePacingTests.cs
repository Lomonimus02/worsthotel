using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;

namespace WorstHotel.Tests
{
    public sealed class GuestServicePacingTests
    {
        sealed class Travel
        {
            public GuestAgentState State = GuestAgentState.Scheduled;
            public float Due;
        }

        static string[] QuietDay()
        {
            var asset = AssetDatabase.LoadAssetAtPath<SessionConfig>("Assets/_WorstHotel/ScriptableObjects/PrototypeSession.asset");
            Assert.That(asset.services, Is.Not.Null, "The production scene must opt into the service layer.");
            var settings = asset.ToData();
            var rooms = settings.Rooms.Select(room => new RoomState(room)).ToArray();
            var hotel = new HotelSimulation(settings, rooms, asset.living.ToData(), asset.needs.ToData(), asset.noise.ToData(),
                asset.heater.ToData(), asset.electricity.ToData(), asset.housekeeping.ToData(), asset.services.ToData(), asset.infrastructure.ToData());
            hotel.Services.SetStaffCount(1);
            var offers = GuestSystem.GenerateApplications(1, settings.GuestArchetypes);
            int[] assignments = { 104, 101, 106, 102 };
            Assert.That(hotel.StartShift(offers.Select((offer, i) => new BookingAssignment(assignments[i], offer.Id, offer.ReferencePrice, 0)), offers).Success, Is.True);
            var travel = new Dictionary<string, Travel>();
            float dt = 1 / settings.TickRate;
            bool failed = false;
            for (int step = 0; step < 10000 && !hotel.IsServiceComplete; step++)
            {
                hotel.Tick(dt); failed |= hotel.Boiler.Failed;
                foreach (var guest in hotel.Guests)
                {
                    if (!travel.TryGetValue(guest.GuestId, out var route)) travel.Add(guest.GuestId, route = new Travel());
                    var state = guest.Agent.State;
                    if (state != route.State)
                    {
                        route.State = state;
                        // Explicit model boundary adapter: walk/key durations, never forced activity choices.
                        float door = 10 + (guest.RoomId - 101) / 2 * 7;
                        float duration = state == GuestAgentState.WaitingForCheckIn ? asset.living.keyRetrievalEstimateSeconds :
                            state == GuestAgentState.GoingToRoom ? (door + 8) / 1.35f :
                            state == GuestAgentState.LeavingRoom || state == GuestAgentState.ReturningToRoom ? (door + 12.5f) / 1.35f : 5;
                        route.Due = hotel.Elapsed + duration;
                    }
                    if (hotel.Elapsed < route.Due) continue;
                    if (state == GuestAgentState.Arriving) Assert.That(hotel.SignalGuestReachedReception(guest.GuestId).Success, Is.True);
                    if (state == GuestAgentState.WaitingForCheckIn) Assert.That(ModelKeyHandoff.CheckIn(hotel, 0, guest.GuestId).Success, Is.True);
                    if (state == GuestAgentState.GoingToRoom) Assert.That(hotel.SignalGuestReachedRoom(guest.GuestId).Success, Is.True);
                    if (state == GuestAgentState.LeavingRoom) Assert.That(hotel.SignalGuestLeftRoom(guest.GuestId).Success, Is.True);
                    if (state == GuestAgentState.ReturningToRoom) Assert.That(hotel.SignalGuestReturnedRoom(guest.GuestId).Success, Is.True);
                    if (state == GuestAgentState.Leaving)
                    {
                        var room = rooms.Single(item => item.Profile.Id == guest.RoomId);
                        if (room.DepartingGuestId == guest.GuestId) Assert.That(hotel.SignalGuestVacatedRoom(guest.GuestId, guest.RoomId).Success, Is.True);
                        Assert.That(hotel.SignalGuestLeft(guest.GuestId).Success, Is.True);
                    }
                }
                foreach (var item in hotel.Services.Cases.Where(item => item.Active).ToArray())
                {
                    var guest = hotel.Guests.Single(guest => guest.GuestId == item.GuestId);
                    Assert.That(item.SourceEntityId, Is.Not.Empty);
                    Assert.That(hotel.Services.Cases.Count(other => other.GuestId == guest.GuestId && other.Active), Is.LessThanOrEqualTo(1));
                    if (hotel.Elapsed < item.CreatedAt + 12) continue;
                    if (item.Status != ServiceStatus.InProgress) hotel.RespondToService(0, item.Id, true);
                    if (item.Kind == ServiceKind.ExtraBlanket && guest.Agent.InAssignedRoom)
                    {
                        var blanket = hotel.Services.Items.FirstOrDefault(supply => supply.Kind == ServiceItemKind.Blanket && supply.Location == ServiceItemLocation.OnShelf);
                        if (blanket != null)
                        {
                            Assert.That(hotel.TakeServiceItem(0, blanket.Id).Success, Is.True);
                            Assert.That(hotel.DeliverBlanket(0, guest.GuestId).Success, Is.True);
                        }
                    }
                    if (item.Kind == ServiceKind.AskNeighborsQuiet)
                    {
                        var source = hotel.Noise.Sources.FirstOrDefault(source => source.SourceEntityId == item.SourceEntityId);
                        if (source != null) hotel.RequestQuiet(0, source.SourceGuestId);
                    }
                }
                foreach (var promise in hotel.Services.Promises.Where(item => item.Status == PromiseStatus.Accepted && hotel.Elapsed >= item.DueTime).ToArray())
                    hotel.CompleteWakeUpCall(0, promise.Id);
            }
            Assert.That(hotel.IsServiceComplete, Is.True);
            Assert.That(failed, Is.False, "This representative quiet day must have service texture without forcing a boiler disaster.");
            Assert.That(hotel.Services.Cases.Count, Is.InRange(1, Math.Min(3, asset.services.maxCasesPerShift)));
            Assert.That(hotel.Services.Cases.Any(item => item.Kind != ServiceKind.WakeUpCall), Is.True);
            Assert.That(hotel.Guests.Any(guest => guest.Memory.ServicesRequested == 0), Is.True);
            Assert.That(hotel.Services.Cases.GroupBy(item => item.GuestId + "/" + item.Kind).All(group => group.Count() == 1), Is.True);
            TestContext.WriteLine("Production quiet day: seed=" + asset.living.seed + ", cases=" + hotel.Services.Cases.Count + ", boilerFailed=" + failed +
                "; " + string.Join("; ", hotel.Services.Cases.Select(item => item.CreatedAt.ToString("F1") + "s " + item.GuestId + " " + item.Kind + " " + item.Status)));
            return hotel.Services.Cases.Select(item => item.Id).ToArray();
        }

        [Test]
        public void ProductionSeedQuietDayHasSparseReproducibleCausalServicesAndGuestsWithNoRequests()
        {
            var first = QuietDay(); var second = QuietDay();
            Assert.That(second, Is.EqualTo(first));
        }
    }
}
