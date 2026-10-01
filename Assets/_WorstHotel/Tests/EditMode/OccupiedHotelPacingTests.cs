using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace WorstHotel.Tests
{
    // Temporary-scale pacing evidence, with explicitly instantaneous headless travel.
    // These observations diagnose gates; they do not certify human gameplay or route timing.
    public sealed class OccupiedHotelPacingTests
    {
        static void Good(CommandResult result) => Assert.That(result.Success, Is.True, result.Message);

        [Test]
        public void CompareShippedEveningWithPreviousPacing()
        {
            foreach (bool previous in new[] { true, false }) Observe(previous);
        }

        static void Observe(bool previous)
        {
            var config = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<SessionConfig>("Assets/_WorstHotel/ScriptableObjects/PrototypeSession.asset"));
            var services = UnityEngine.Object.Instantiate(config.services);
            var living = UnityEngine.Object.Instantiate(config.living);
            if (previous)
            {
                config.hotelDaySeconds = 720; services.maxCasesPerShift = 3;
                services.eligibility = .55f; services.soloFrequencyMultiplier = .8f;
                services.selfResponseObserveSeconds = 5; services.toleranceSeconds = 10;
                living.quietDurationMin = 12; living.quietDurationMax = 22;
                living.awayDurationMin = 28; living.awayDurationMax = 48;
            }
            var settings = config.ToData(); var rooms = settings.Rooms.Select(r => new RoomState(r)).ToArray();
            var h = new HotelSimulation(settings, rooms, living.ToData(), config.needs.ToData(), config.noise.ToData(),
                config.heater.ToData(), config.electricity.ToData(), config.housekeeping.ToData(), services.ToData(),
                config.infrastructure.ToData(), config.OperationsData());
            Good(h.StartOperations()); h.Services.SetStaffCount(1);
            // Normal staff choices: enable sale of the two existing base rooms, prepare 101.
            Good(h.SetRoomSalesPolicy(0, 105, true, config.roomSalePrice));
            Good(h.SetRoomSalesPolicy(0, 106, true, config.roomSalePrice));
            var bed = h.Housekeeping.Find(101);
            Good(h.PickUpLinen(0, bed.DirtyLinenId)); Good(h.DepositDirtyLinen(0, bed.DirtyLinenId));
            Good(h.PickUpLinen(0, "clean:0")); Good(h.BeginMakeBed(0, 101, "clean:0"));
            Good(h.AdvanceMakeBed(0, 101, h.Housekeeping.Settings.MakeBedSeconds));
            var trace = new List<string>(); var contacts = new HashSet<string>();
            var activities = new Dictionary<string, GuestActivity>();
            int eveningContacts = 0, capacityBlockedTicks = 0, peakPending = 0, sample = -1;
            float minLoad = float.MaxValue, maxLoad = 0; bool showerLoad = false, neighborNoise = false;
            while (h.Elapsed < h.Calendar.At(1, 23))
            {
                h.Tick(.2f);
                foreach (var g in h.Guests.ToArray())
                {
                    var a = g.Agent;
                    if (a.State == GuestAgentState.Arriving) Good(h.SignalGuestReachedReception(g.GuestId));
                    if (a.State == GuestAgentState.WaitingForCheckIn && !g.LockedOut)
                    { Good(h.Keys.PickUp(0, g.RoomId)); Good(h.CheckIn(0, g.GuestId)); }
                    if (a.State == GuestAgentState.GoingToRoom) Good(h.SignalGuestReachedRoom(g.GuestId));
                    if (a.State == GuestAgentState.LeavingRoom) Good(h.SignalGuestLeftRoom(g.GuestId));
                    if (a.State == GuestAgentState.ReturningToRoom) Good(h.SignalGuestReturnedRoom(g.GuestId));
                    if (a.State == GuestAgentState.Leaving)
                    { h.SignalGuestVacatedRoom(g.GuestId, g.RoomId); Good(h.SignalGuestLeft(g.GuestId)); }
                    var response = h.Services.FindResponse(a.ResponseActionId);
                    if (response != null)
                    {
                        var anchor = a.State == GuestAgentState.ReturningFromServiceReception ? GuestResponseAnchor.AssignedRoom :
                            a.State == GuestAgentState.GoingToServiceReception ? GuestResponseAnchor.Reception :
                            a.Activity == GuestActivity.AdjustRadiator ? GuestResponseAnchor.Radiator : GuestResponseAnchor.RoomPhone;
                        h.SignalGuestResponseAnchorReached(g.GuestId, response.Id, response.ActionVersion, anchor);
                    }
                    if (a.IsRoomState && (!activities.TryGetValue(g.GuestId, out var last) || last != a.Activity))
                    { activities[g.GuestId] = a.Activity; trace.Add(h.Elapsed.ToString("F1") + " room " + g.RoomId + " " + a.Activity); }
                }
                foreach (var response in h.Services.Responses.Where(r => r.AttemptStartedAt >= 0).ToArray())
                {
                    if (contacts.Add(response.Id))
                    {
                        if (h.Elapsed >= h.Calendar.At(1, 16)) eveningContacts++;
                        trace.Add(h.Elapsed.ToString("F1") + " CONTACT room " + response.RoomId + " " + response.Channel + " " + response.ServiceCaseId + " " + response.IncidentId);
                    }
                    if (response.CommunicatedAt < 0)
                    {
                        var answer = response.Channel == GuestContactChannel.Phone ? h.AnswerIncomingServiceCall(0, response.Id) :
                            h.TalkToServiceGuest(0, response.GuestId, response.Id);
                        if (answer.Success && h.Services.FindCase(response.ServiceCaseId)?.Active == true)
                            h.RespondToService(0, response.ServiceCaseId, true);
                    }
                }
                foreach (var intent in h.Services.Intents.Where(i => i.Kind == ServiceIntentKind.DropOff && i.Status == ServiceIntentStatus.Active).ToArray())
                {
                    var item = h.Services.Items.FirstOrDefault(i => i.Kind == ServiceItemKind.Blanket && i.Location == ServiceItemLocation.OnShelf);
                    if (item != null && h.TakeServiceItem(0, item.Id).Success)
                        Good(h.DropOffBlanket(0, intent.GuestId, intent.RoomId, intent.Revision, item.Generation));
                }
                int charged = h.Services.Cases.Count(c => c.BudgetCharged && c.BudgetDay == h.Calendar.Day);
                if (charged >= services.maxCasesPerShift && h.Services.Responses.Any(r => r.Phase == GuestResponsePhase.WaitingToContact && r.ServiceCaseId == null)) capacityBlockedTicks++;
                peakPending = Math.Max(peakPending, h.Services.Cases.Count(c => c.Active && c.IsKnownToHotel));
                if (h.Elapsed >= h.Calendar.At(1, 16))
                { minLoad = Math.Min(minLoad, h.Boiler.Load); maxLoad = Math.Max(maxLoad, h.Boiler.Load); }
                showerLoad |= h.HeatingDemands.Any(d => d.HotWater > 0);
                neighborNoise |= rooms.Any(r => r.ReceivedNoise > .1f);
                int nextSample = (int)(h.Elapsed * 48 / h.Operations.SecondsPerDay);
                if (sample != nextSample)
                {
                    sample = nextSample;
                    trace.Add(h.Elapsed.ToString("F1") + " STATE cases=" + charged + " pending=" + h.Services.Cases.Count(c => c.Active && c.IsKnownToHotel) +
                        " boiler=" + h.Boiler.Load + " rooms=" + string.Join(";", rooms.Take(6).Select(r => r.Profile.Id + ":temp=" + r.Temperature.ToString("F1") + ",valve=" + r.RadiatorSetting + ",noise=" + r.ReceivedNoise.ToString("F2"))));
                }
            }
            string summary = (previous ? "0.6.8" : "0.6.9") + " headless, six base rooms for sale: guests=" + h.Guests.Count +
                " contacts=" + contacts.Count + " evening contacts=" + eveningContacts + " peak concurrent services=" + peakPending +
                " budget-saturated waiting ticks=" + capacityBlockedTicks + " evening boiler load=" + minLoad + ".." + maxLoad;
            trace.Add(summary); TestContext.Out.WriteLine(summary);
            Assert.That(h.Guests.Count, Is.InRange(4, 6)); Assert.That(showerLoad && neighborNoise, Is.True);
            Assert.That(h.Services.Cases.GroupBy(c => c.GuestId + "/" + c.Kind).All(g => g.Count() == 1), Is.True);
            System.IO.Directory.CreateDirectory("Logs"); System.IO.File.WriteAllLines("Logs/pacing069-" + (previous ? "before" : "after") + ".txt", trace);
            UnityEngine.Object.DestroyImmediate(config); UnityEngine.Object.DestroyImmediate(services); UnityEngine.Object.DestroyImmediate(living);
        }
    }
}
