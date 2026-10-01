using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace WorstHotel.Tests
{
    public sealed partial class Phase1PlayModeTests
    {
        [UnityTest, Category("ContinuousOperations"), Category("AutomaticSales")]
        public IEnumerator BedApproachesClearTheFullGuestBody()
        {
            yield return null;
            var presentation = Object.FindAnyObjectByType<GuestPresentation>();
            var probe = new GameObject("Bed approach clearance probe");
            foreach (var room in presentation.roomMarkers)
            {
                var original = room.bedApproach.position; original.x = Mathf.Sign(original.x) * 5;
                var originalHits = Physics.OverlapCapsule(original + Vector3.up * .42f, original + Vector3.up * 1.65f,
                    .32f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                TestContext.Out.WriteLine("Old bed approach " + room.roomId + ": " + string.Join(",", originalHits.Select(c => c.name)));
                var route = AuthoredGuestRoute.Activity(room.rest.position, room, GuestActivity.QuietRest, true);
                var from = room.rest.position;
                foreach (var point in route.Points)
                {
                    Assert.That(GuestPhysicalReaction.SegmentClear(probe.transform, from, point), Is.True,
                        "Bed " + room.roomId + " full guest capsule: " + from + " -> " + point);
                    from = point;
                }
            }
            Object.Destroy(probe);
        }

        [UnityTest, Category("ContinuousOperations"), Category("AutomaticSales")]
        public IEnumerator RestAndTvApproachesClearFurniture()
        {
            yield return null;
            var presentation = Object.FindAnyObjectByType<GuestPresentation>();
            var failures = new List<string>();
            foreach (var clutter in Object.FindObjectsByType<RoomResetInteraction>(FindObjectsSortMode.None).Where(c => c.element == RoomDisorder.Towels))
            { clutter.disorderVisual.SetActive(true); clutter.GetComponent<Collider>().enabled = true; }
            Physics.SyncTransforms();
            var probe = new GameObject("Guest sweep probe");
            foreach (var room in presentation.roomMarkers)
            {
                var route = AuthoredGuestRoute.Activity(room.shower.position, room, GuestActivity.WatchTV);
                var from = room.shower.position;
                foreach (var point in route.Points)
                {
                    if (!GuestPhysicalReaction.SegmentClear(probe.transform, from, point))
                        failures.Add(room.roomId + ": " + from + " -> " + point);
                    from = point;
                }
            }
            Object.Destroy(probe);
            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }

        [UnityTest, Category("ContinuousOperations"), Category("AutomaticSales"), Timeout(480000)]
        public IEnumerator OrdinaryGuestsKeepPhysicallyLivingAfterCheckIn() => ObserveOccupiedHotel(false);

        [UnityTest, Category("ContinuousOperations"), Category("AutomaticSales"), Timeout(540000)]
        public IEnumerator SixRoomEveningAndMorningUseShippedPacing() => ObserveOccupiedHotel(true);

        IEnumerator ObserveOccupiedHotel(bool pacing)
        {
            bootstrap.ConfigureSolo(); InputSystem.RemoveDevice(padB); padB = null;
            ManagementUI.Instance.Close(); var session = GameSession.Instance; var hotel = session.Simulation;
            var presentation = Object.FindAnyObjectByType<GuestPresentation>();
            if (pacing)
            {
                Assert.That(hotel.Operations.SecondsPerDay, Is.EqualTo(480));
                Assert.That(Time.timeScale, Is.EqualTo(1));
                Assert.That(hotel.SetRoomSalesPolicy(0, 105, true, session.config.roomSalePrice).Success, Is.True);
                Assert.That(hotel.SetRoomSalesPolicy(0, 106, true, session.config.roomSalePrice).Success, Is.True);
            }
            // Explicit staff adapter only: prepare the starting bed and hand over real model keys.
            // All guest travel, activities, services, room conditions and clocks remain production.
            var bed = hotel.Housekeeping.Find(101);
            Assert.That(hotel.PickUpLinen(0, bed.DirtyLinenId).Success, Is.True);
            Assert.That(hotel.DepositDirtyLinen(0, bed.DirtyLinenId).Success, Is.True);
            Assert.That(hotel.PickUpLinen(0, "clean:0").Success, Is.True);
            Assert.That(hotel.BeginMakeBed(0, 101, "clean:0").Success, Is.True);
            Assert.That(hotel.AdvanceMakeBed(0, 101, hotel.Housekeeping.Settings.MakeBedSeconds).Success, Is.True);
            var last = new Dictionary<string, string>(); var changes = new Dictionary<string, int>();
            var trace = new List<string>(); float nextLog = 0, lastEveningChange = hotel.Calendar.At(1, 16), longestEveningGap = 0;
            GuestServiceIntent delivery = null; bool received = false, showerLoad = false, neighborNoise = false;
            var contacts = new HashSet<string>(); float lastOperationalChange = hotel.Calendar.At(1, 16), longestOperationalGap = 0;
            float previousLoad = hotel.Boiler.Load, previousNoise = 0; int pending = 0, peakPending = 0;
            bool slept = false, morningShower = false; float nextStateLog = 0; int basicServices = 0;
            string tracePath = pacing ? "Logs/pacing069-scene.txt" : "Logs/guest-life-cadence.txt";
            System.IO.Directory.CreateDirectory("Logs");
            while (hotel.Elapsed < (pacing ? hotel.Calendar.At(2, 9) : hotel.Calendar.At(1, 22)))
            {
                // An explicit staff maintenance adapter reacts to actual measured stress,
                // pays the full existing price and waits through the ordinary outage.
                // It does not alter loads/needs or certify the player's maintenance route.
                if (pacing && basicServices < 2 && hotel.Boiler.Stress01 >= .35f && !hotel.Boiler.MaintenanceInProgress &&
                    hotel.CanBeginBoilerMaintenance(0, BoilerServiceKind.Basic).Success)
                {
                    Assert.That(hotel.BeginBoilerMaintenance(0, BoilerServiceKind.Basic).Success, Is.True);
                    basicServices++; trace.Add(hotel.Elapsed.ToString("F1") + " STAFF paid Basic service; measured stress=" + hotel.Boiler.Stress01);
                }
                if (pacing && !slept && hotel.Elapsed >= hotel.Calendar.At(1, 23))
                {
                    // Explicit staff approach adapter; real bed targeting, input consent and
                    // normal 8x HOTEL sleep clock. No daytime skip or guest callbacks.
                    yield return ConsentStaffBed(0, 0);
                    slept = true; trace.Add(hotel.Elapsed.ToString("F1") + " STAFF SLEEP " + Waiter.Reason);
                }
                // Respond only to an ordinary naturally generated blanket call. This is a staff
                // adapter, not an injected need, route completion or receipt acknowledgement.
                var call = hotel.Services.IncomingCall;
                var request = call == null ? null : hotel.Services.FindCase(call.ServiceCaseId);
                if (delivery == null && request?.Kind == ServiceKind.ExtraBlanket)
                {
                    Assert.That(hotel.AnswerIncomingServiceCall(0, call.Id).Success, Is.True);
                    Assert.That(hotel.RespondToService(0, request.Id, true).Success, Is.True);
                    Assert.That(hotel.TakeServiceItem(0, "blanket:0").Success, Is.True);
                    delivery = hotel.Services.DropOffIntent(request.GuestId);
                    Assert.That(hotel.DropOffBlanket(0, request.GuestId, request.RoomId, delivery.Revision, hotel.Services.HeldBy(0).Generation).Success, Is.True);
                    session.RaiseChanged();
                }
                if (pacing)
                {
                    foreach (var response in hotel.Services.Responses.Where(r => r.AttemptStartedAt >= 0))
                        if (contacts.Add(response.Id)) trace.Add(hotel.Elapsed.ToString("F1") + " CONTACT " + response.RoomId + " " + response.Channel + " " + response.ServiceCaseId + " " + response.IncidentId);
                    float noise = session.Rooms.Sum(r => r.ReceivedNoise);
                    int active = hotel.Services.Cases.Count(c => c.Active && c.IsKnownToHotel && c.Kind != ServiceKind.WakeUpCall);
                    peakPending = Mathf.Max(peakPending, active);
                    bool operational = Mathf.Abs(hotel.Boiler.Load - previousLoad) >= .5f || Mathf.Abs(noise - previousNoise) >= .2f || active != pending;
                    if (operational)
                    {
                        previousLoad = hotel.Boiler.Load; previousNoise = noise; pending = active;
                        if (hotel.Elapsed >= hotel.Calendar.At(1, 16) && hotel.Elapsed <= hotel.Calendar.At(1, 23))
                        { longestOperationalGap = Mathf.Max(longestOperationalGap, hotel.Elapsed - lastOperationalChange); lastOperationalChange = hotel.Elapsed; }
                    }
                    if (hotel.Elapsed >= nextStateLog)
                    {
                        nextStateLog = hotel.Elapsed + 10;
                        trace.Add(hotel.Elapsed.ToString("F1") + " WORLD boiler=" + hotel.Boiler.Load.ToString("F2") + "/" + hotel.Boiler.CapacityBand +
                            " noise=" + noise.ToString("F2") + " active service=" + active + " calls=" + contacts.Count +
                            " valves=" + string.Join(",", session.Rooms.Take(6).Select(r => r.RadiatorSetting)));
                    }
                    morningShower |= hotel.Calendar.Day == 2 && hotel.HeatingDemands.Any(d => d.HotWater > 0);
                    Assert.That(Time.timeScale, Is.EqualTo(1));
                }
                if (!received && delivery?.Status == ServiceIntentStatus.Completed)
                {
                    var recipient = hotel.Guests.Single(g => g.GuestId == delivery.GuestId);
                    presentation.TryGetGuestTransform(recipient.GuestId, out var receiver);
                    var shelf = Object.FindObjectsByType<RoomBlanketDropOffInteraction>(FindObjectsSortMode.None).Single(s => s.roomId == recipient.RoomId);
                    Assert.That(Vector3.Distance(receiver.position, new Vector3(Mathf.Sign(shelf.transform.position.x) * 1.03f, .01f, shelf.transform.position.z)), Is.LessThan(.15f));
                    Assert.That(recipient.BlanketComfortBonus, Is.GreaterThan(0));
                    Assert.That(recipient.Memory.BlanketsDelivered, Is.EqualTo(1));
                    Assert.That(recipient.Memory.ServicesFulfilled, Is.GreaterThan(0));
                    trace.Add("Physical blanket received at " + hotel.Elapsed + " room " + recipient.RoomId + " bonus " + recipient.BlanketComfortBonus);
                    received = true;
                }
                showerLoad |= hotel.HeatingDemands.Any(d => d.HotWater > 0);
                neighborNoise |= session.Rooms.Any(r => r.ReceivedNoise > .1f);
                foreach (var guest in hotel.Guests.ToArray())
                {
                    var a = guest.Agent;
                    if (a.State == GuestAgentState.WaitingForCheckIn && !guest.LockedOut)
                        Assert.That(CheckInWithModelKeyFixture(session, 0, guest.GuestId).Success, Is.True);
                    if (!presentation.TryGetGuestDebugSnapshot(guest.GuestId, out var view)) continue;
                    presentation.TryGetGuestTransform(guest.GuestId, out var root);
                    string state = a.State + "/" + a.Activity + "/staged=" + a.ActivityStaged + "/" + a.ResponseActionId + "/" + view.PathStatus;
                    if (!last.TryGetValue(guest.GuestId, out var before) || before != state)
                    {
                        last[guest.GuestId] = state;
                        trace.Add(hotel.Elapsed.ToString("F1") + " room " + guest.RoomId + " " + state + " at " + root.position + " -> " + view.Destination);
                        if (a.ActivityStaged && a.IsRoomState) changes[guest.GuestId] = changes.TryGetValue(guest.GuestId, out int n) ? n + 1 : 1;
                        if (hotel.Elapsed >= hotel.Calendar.At(1, 16) && (a.ActivityStaged && a.IsRoomState || a.State == GuestAgentState.GuestAway))
                        { longestEveningGap = Mathf.Max(longestEveningGap, hotel.Elapsed - lastEveningChange); lastEveningChange = hotel.Elapsed; }
                    }
                    if (a.InAssignedRoom && !a.ActivityStaged && a.ResponseActionId == null && !Waiter.IsSleeping && hotel.Elapsed - a.StateChangedAt > 60)
                    {
                        var target = presentation.roomMarkers.Single(r => r.roomId == guest.RoomId).loud.position;
                        foreach (var hit in Physics.CapsuleCastAll(root.position + Vector3.up * .42f, root.position + Vector3.up * 1.65f,
                            .32f, (target-root.position).normalized, Vector3.Distance(root.position,target), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                            trace.Add("Collision: " + hit.collider.name + " " + hit.collider.bounds + " rigid=" + hit.rigidbody + " own=" + hit.transform.IsChildOf(root));
                        System.IO.File.WriteAllLines(tracePath, trace);
                        Assert.Fail("Room " + guest.RoomId + " stuck for " + (hotel.Elapsed-a.StateChangedAt) + "s: " + state + " " + view.Destination + " at " + root.position);
                    }
                }
                if (hotel.Elapsed >= nextLog) { nextLog = hotel.Elapsed + 10; System.IO.File.WriteAllLines(tracePath, trace); }
                yield return null;
            }
            if (pacing)
            {
                longestOperationalGap = Mathf.Max(longestOperationalGap, hotel.Calendar.At(1, 23) - lastOperationalChange);
                trace.Add("SUMMARY contacts=" + contacts.Count + " peak pending=" + peakPending + " longest evening operational gap=" + longestOperationalGap + " sleep=" + slept + " morning shower=" + morningShower);
            }
            System.IO.File.WriteAllLines(tracePath, trace);
            Assert.That(changes.Count, Is.GreaterThanOrEqualTo(3));
            Assert.That(changes.Values.Min(), Is.GreaterThanOrEqualTo(5));
            Assert.That(received, Is.True, "The natural blanket delivery must physically reach its recipient.");
            Assert.That(showerLoad && neighborNoise, Is.True, "Activities must affect actual heating and neighbouring rooms.");
            if (pacing)
            {
                Assert.That(changes.Count, Is.EqualTo(6));
                Assert.That(slept && morningShower, Is.True);
                Assert.That(longestOperationalGap, Is.LessThan(45), "Meaningful load/noise/service changes must not vanish for an evening minute.");
            }
            else Assert.That(Mathf.Max(longestEveningGap, hotel.Elapsed - lastEveningChange), Is.LessThan(60), "The hotel cannot be inactive for two evening hours.");
            LogAssert.NoUnexpectedReceived();
        }
    }
}
