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
        public IEnumerator OrdinaryGuestsKeepPhysicallyLivingAfterCheckIn()
        {
            bootstrap.ConfigureSolo(); InputSystem.RemoveDevice(padB); padB = null;
            ManagementUI.Instance.Close(); var session = GameSession.Instance; var hotel = session.Simulation;
            var presentation = Object.FindAnyObjectByType<GuestPresentation>();
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
            System.IO.Directory.CreateDirectory("Logs");
            while (hotel.Elapsed < hotel.Calendar.At(1, 22))
            {
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
                    if (a.InAssignedRoom && !a.ActivityStaged && hotel.Elapsed - a.StateChangedAt > 60)
                    {
                        var target = presentation.roomMarkers.Single(r => r.roomId == guest.RoomId).loud.position;
                        foreach (var hit in Physics.CapsuleCastAll(root.position + Vector3.up * .42f, root.position + Vector3.up * 1.65f,
                            .32f, (target-root.position).normalized, Vector3.Distance(root.position,target), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                            trace.Add("Collision: " + hit.collider.name + " " + hit.collider.bounds + " rigid=" + hit.rigidbody + " own=" + hit.transform.IsChildOf(root));
                        System.IO.File.WriteAllLines("Logs/guest-life-cadence.txt", trace);
                        Assert.Fail("Room " + guest.RoomId + " stuck for " + (hotel.Elapsed-a.StateChangedAt) + "s: " + state + " " + view.Destination + " at " + root.position);
                    }
                }
                if (hotel.Elapsed >= nextLog) { nextLog = hotel.Elapsed + 10; System.IO.File.WriteAllLines("Logs/guest-life-cadence.txt", trace); }
                yield return null;
            }
            System.IO.File.WriteAllLines("Logs/guest-life-cadence.txt", trace);
            Assert.That(changes.Count, Is.GreaterThanOrEqualTo(3));
            Assert.That(changes.Values.Min(), Is.GreaterThanOrEqualTo(5));
            Assert.That(received, Is.True, "The natural blanket delivery must physically reach its recipient.");
            Assert.That(showerLoad && neighborNoise, Is.True, "Activities must affect actual heating and neighbouring rooms.");
            Assert.That(Mathf.Max(longestEveningGap, hotel.Elapsed - lastEveningChange), Is.LessThan(60), "The hotel cannot be inactive for two evening hours.");
            LogAssert.NoUnexpectedReceived();
        }
    }
}
