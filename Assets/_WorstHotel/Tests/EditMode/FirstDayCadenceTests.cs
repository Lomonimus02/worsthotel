using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace WorstHotel.Tests
{
    public sealed class FirstDayCadenceTests
    {
        static void Good(CommandResult result) => Assert.That(result.Success, Is.True, result.Message);
        static HotelSimulation Shipped(out RoomState[] rooms)
        {
            var c = AssetDatabase.LoadAssetAtPath<SessionConfig>("Assets/_WorstHotel/ScriptableObjects/PrototypeSession.asset");
            var settings = c.ToData(); rooms = settings.Rooms.Select(r => new RoomState(r)).ToArray();
            var hotel = new HotelSimulation(settings, rooms, c.living.ToData(), c.needs.ToData(), c.noise.ToData(),
                c.heater.ToData(), c.electricity.ToData(), c.housekeeping.ToData(), c.services.ToData(),
                c.infrastructure.ToData(), c.OperationsData());
            Good(hotel.StartOperations()); return hotel;
        }
        [Test]
        public void ShippedOpeningIsSoonVisibleAndReplicatesWithoutAFreeReadyRoom()
        {
            var h = Shipped(out var rooms);
            Assert.That(h.Reservations.Count, Is.EqualTo(1));
            Assert.That(h.Reservations[0].RoomId, Is.EqualTo(101));
            float minutes = h.Reservations[0].Offer.ArrivalAt * 24 * 60 / h.Operations.SecondsPerDay;
            Assert.That(minutes, Is.InRange(20, 40));
            Assert.That(RoomStatusBoard.Status(h, rooms[0]), Is.EqualTo("NOT READY"));
            Assert.That(RoomStatusBoard.Status(h, rooms[1]), Is.EqualTo("READY"));
            Assert.That(h.Housekeeping.FindLinen("dirty:101").Location, Is.EqualTo(LinenLocation.OnBed));
            var mirror = Shipped(out _); mirror.EnableReadOnlyMirror();
            Good(mirror.ApplySnapshot(JsonUtility.FromJson<HotelModelSnapshot>(JsonUtility.ToJson(h.CaptureSnapshot(967, 1)))));
            var first = h.BookingOffers.Where(o => o.ArrivalDay == 1).Take(3).Select(o => o.ArrivalAt).ToArray();
            Assert.That(first[2] - first[0], Is.LessThan(30));
            TestContext.Out.WriteLine("Opening arrival: " + minutes.ToString("0") + " hotel minutes / " + first[0].ToString("0.0") + " real seconds; first three at " + string.Join(", ", first));
        }

        [Test]
        public void AwakeArcDoesNotCollapseIntoAllDayOutingOrRepeatedQuietTail()
        {
            // A headless travel adapter, with comfortable profiles and no incidental service holds.
            // It checks schedule bounds only; the actual first-day playthrough remains separate.
            var profiles = Enum.GetValues(typeof(GuestKind)).Cast<GuestKind>().Select(k => new GuestProfile(k, k.ToString(), "", 180,
                1, 18, 1, 10, 90, .8f, needs: new NeedProfile(0, 40, -10, 50, .9f, 1, 90))).ToArray();
            var settings = new SessionSettings(profiles, new[] { new RoomProfile(101, "101") },
                new BoilerSettings(baseWearPerMinute: 0, overloadWearPerMinute: 0), new EconomySettings(startingCash: 20000));
            var rooms = settings.Rooms.Select(r => new RoomState(r)).ToArray();
            var h = new HotelSimulation(settings, rooms, new LivingHotelSettings(rhythm: new GuestRhythmSettings(enabled: true,
                businessOutingProbability: 1, budgetOutingProbability: 1, coldSensitiveOutingProbability: 1)),
                operations: new OperationsSettings(sales: new SalesSettings(enabled: true, initiallyOpenRooms: 1)));
            Good(h.StartOperations()); var task = h.Housekeeping.Find(101);
            Good(h.PickUpLinen(0, task.DirtyLinenId)); Good(h.DepositDirtyLinen(0, task.DirtyLinenId));
            Good(h.PickUpLinen(0, "clean:0")); Good(h.BeginMakeBed(0, 101, "clean:0"));
            Good(h.AdvanceMakeBed(0, 101, h.Housekeeping.Settings.MakeBedSeconds));
            var activities = new HashSet<GuestActivity>();
            float quiet = 0, longestQuiet = 0, away = 0, longestAway = 0; int outings = 0; bool slept = false, showerAfterSleep = false;
            while (h.Elapsed < h.Calendar.At(2, 11))
            {
                h.Tick(.25f);
                foreach (var g in h.Guests.ToArray())
                {
                    var a = g.Agent;
                    if (a.State == GuestAgentState.Arriving) Good(h.SignalGuestReachedReception(g.GuestId));
                    if (a.State == GuestAgentState.WaitingForCheckIn)
                    {
                        if (g.LockedOut) { Good(h.Keys.PickUp(0, 0)); Good(h.UnlockForGuest(0, 101)); Good(h.Keys.Drop(0, 0)); }
                        else { Good(h.Keys.PickUp(0, 101)); Good(h.CheckIn(0, g.GuestId)); Good(h.SignalGuestReachedRoom(g.GuestId)); }
                    }
                    if (a.State == GuestAgentState.LeavingRoom) { outings++; Good(h.SignalGuestLeftRoom(g.GuestId)); }
                    if (a.State == GuestAgentState.ReturningToRoom) Good(h.SignalGuestReturnedRoom(g.GuestId));
                    if (a.State == GuestAgentState.Leaving) { Good(h.SignalGuestVacatedRoom(g.GuestId, 101)); Good(h.SignalGuestLeft(g.GuestId)); }
                    if (a.IsRoomState) activities.Add(a.Activity);
                    slept |= a.State == GuestAgentState.Sleeping;
                    showerAfterSleep |= slept && a.Activity == GuestActivity.Shower && a.State != GuestAgentState.Sleeping;
                    quiet = a.IsRoomState && a.State != GuestAgentState.Sleeping && a.Activity == GuestActivity.QuietRest ? quiet + .25f : 0;
                    away = a.State == GuestAgentState.GuestAway ? away + .25f : 0;
                    longestQuiet = Math.Max(longestQuiet, quiet); longestAway = Math.Max(longestAway, away);
                }
            }
            Assert.That(outings, Is.EqualTo(1)); Assert.That(longestAway, Is.LessThanOrEqualTo(48.5f));
            Assert.That(longestQuiet, Is.LessThanOrEqualTo(18.5f));
            Assert.That(activities.Count, Is.GreaterThanOrEqualTo(4)); Assert.That(slept && showerAfterSleep, Is.True);
            Assert.That(rooms[0].Cleanliness, Is.EqualTo(Cleanliness.Dirty));
            TestContext.Out.WriteLine("Headless arc: " + string.Join(", ", activities) + "; longest awake rest " + longestQuiet + "s; away " + longestAway + "s; morning shower and turnover present.");
        }
    }
}
