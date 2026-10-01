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
        // Real production clock, NPC movement and director. Explicit staff model adapters
        // handle routine work at intervals; this observes integration, not human difficulty.
        [UnityTest, Category("ContinuousOperations"), Category("AutomaticSales"), Timeout(1100000)]
        public IEnumerator DirectorObservesTwoProductionDaysWithRealGuestTravelAndPaidTurnover() => ObserveDirectorProductionDays(false);

        [UnityTest, Category("ContinuousOperations"), Category("AutomaticSales"), Timeout(420000)]
        public IEnumerator DirectorFirstDayKeepsBudgetForAMediumPremise() => ObserveDirectorProductionDays(true);

        IEnumerator ObserveDirectorProductionDays(bool firstDayOnly)
        {
            bootstrap.ConfigureSolo(); InputSystem.RemoveDevice(padB); padB = null;
            var session = GameSession.Instance; var h = session.Simulation;
            ManagementUI.Instance.Close();
            Assert.That(h.Director.Settings.Enabled, Is.True);
            Assert.That(h.Operations.SecondsPerDay, Is.EqualTo(480));
            var lines = new List<string>(); var seen = new HashSet<string>();
            var occupiedDays = new HashSet<int>(); float nextWork = 0, nextLog = 0;
            int sleeps = 0, turnovers = 0, paidServices = 0;
            string tracePath = firstDayOnly ? "Logs/director070-firstday-final.txt" : "Logs/director070-multiday.txt";
            System.IO.Directory.CreateDirectory("Logs");
            while (h.Elapsed < (firstDayOnly ? h.Calendar.At(1, 23) : h.Calendar.At(3, 9)) && h.Running)
            {
                if (h.Calendar.Day > sleeps && h.Calendar.Hour >= 23 && !Waiter.IsSleeping)
                {
                    yield return ConsentStaffBed(0, 0);
                    sleeps = h.Calendar.Day;
                    lines.Add(h.Elapsed.ToString("F1") + " STAFF sleep " + Waiter.Reason);
                }
                if (!Waiter.IsSleeping && h.Elapsed >= nextWork)
                {
                    nextWork = h.Elapsed + 8;
                    foreach (var room in session.Rooms.Where(r => r.Operational && !r.Occupied && r.Cleanliness == Cleanliness.Dirty))
                    {
                        var task = h.Housekeeping.Find(room.Profile.Id);
                        if (task == null) continue;
                        foreach (var element in new[] { RoomDisorder.Waste, RoomDisorder.Towels, RoomDisorder.Chair })
                            if ((room.Disorder & element) != 0) h.Housekeeping.ResetRoomElement(room.Profile.Id, element);
                        if (task.Step == RoomPreparationStep.DirtyLinenOnBed && h.PickUpLinen(0, task.DirtyLinenId).Success)
                            Assert.That(h.DepositDirtyLinen(0, task.DirtyLinenId).Success, Is.True);
                        var linen = h.Housekeeping.Linens.FirstOrDefault(l => l.Kind == LinenKind.Clean && l.Location == LinenLocation.OnShelf);
                        if (task.Step != RoomPreparationStep.NeedsCleanLinen || linen == null) continue;
                        Assert.That(h.PickUpLinen(0, linen.Id).Success, Is.True);
                        Assert.That(h.BeginMakeBed(0, room.Profile.Id, linen.Id).Success, Is.True);
                        Assert.That(h.AdvanceMakeBed(0, room.Profile.Id, task.RequiredSeconds).Success, Is.True);
                        turnovers++;
                    }
                    if (h.CanOrderLaundry(0, h.SupplyRevision).Success) h.OrderLaundry(0, h.SupplyRevision);
                    foreach (var guest in h.Guests.Where(g => g.Agent.State == GuestAgentState.WaitingForCheckIn).ToArray())
                    {
                        if (guest.LockedOut)
                        {
                            if (h.Keys.PickUp(0, 0).Success)
                            { h.UnlockForGuest(0, guest.RoomId); h.Keys.Drop(0, 0); h.Keys.ReturnToRack(0); }
                        }
                        else if (session.Rooms.Single(r => r.Profile.Id == guest.RoomId).Cleanliness == Cleanliness.Clean)
                            Assert.That(CheckInWithModelKeyFixture(session, 0, guest.GuestId).Success, Is.True);
                    }
                    if (h.Boiler.Stress01 >= .35f && h.CanBeginBoilerMaintenance(0, BoilerServiceKind.Basic).Success)
                    { h.BeginBoilerMaintenance(0, BoilerServiceKind.Basic); paidServices++; }
                    var call = h.Services.IncomingCall;
                    if (call != null && h.AnswerIncomingServiceCall(0, call.Id).Success)
                    {
                        var request = h.Services.FindCase(call.ServiceCaseId);
                        if (request != null)
                        {
                            bool blanket = request.Kind == ServiceKind.ExtraBlanket;
                            h.RespondToService(0, request.Id, blanket);
                            if (blanket)
                            {
                                var item = h.Services.Items.FirstOrDefault(i => i.Kind == ServiceItemKind.Blanket && i.Location == ServiceItemLocation.OnShelf);
                                if (item != null && h.TakeServiceItem(0, item.Id).Success)
                                {
                                    var intent = h.Services.DropOffIntent(request.GuestId);
                                    h.DropOffBlanket(0, request.GuestId, request.RoomId, intent.Revision, item.Generation);
                                }
                            }
                        }
                    }
                    session.RaiseChanged();
                }
                if (h.Guests.Any(g => g.Agent.InAssignedRoom && !g.ReceiptPosted)) occupiedDays.Add(h.Calendar.Day);
                foreach (var record in h.Director.History)
                    if (seen.Add(record.Id)) lines.Add(h.Elapsed.ToString("F1") + " PREMISE " + record.Day + " " + record.Kind + " guest=" + record.GuestId);
                if (h.Elapsed >= nextLog)
                {
                    nextLog = h.Elapsed + 10;
                    lines.Add(h.Elapsed.ToString("F1") + " day=" + h.Calendar.Day + " hour=" + h.Calendar.Hour.ToString("F1") +
                        " pressure=" + h.Director.Pressure.Band + "/" + h.Director.Pressure.Score.ToString("F2") +
                        " quiet=" + h.Director.QuietElapsed.ToString("F1") + " spent=" + h.Director.SpentToday +
                        " cash=" + h.Economy.Cash + " guests=" + h.Guests.Count(g => g.Agent.InAssignedRoom) +
                        " reasons=" + h.Director.Pressure.Reason);
                    System.IO.File.WriteAllLines(tracePath, lines);
                }
                yield return null;
            }
            lines.Add("END turnovers=" + turnovers + " paid services=" + paidServices + " contract=" + h.ContractSequence + " lost=" + h.OwnershipLost);
            System.IO.File.WriteAllLines(tracePath, lines);
            Assert.That(h.OwnershipLost, Is.False, "Observe genuine expenses/receipts; do not refill cash in this scenario.");
            Assert.That(occupiedDays, Has.Member(1));
            if (!firstDayOnly)
            {
                Assert.That(occupiedDays, Has.Member(2));
                Assert.That(turnovers, Is.GreaterThan(1));
                Assert.That(h.ContractSequence, Is.GreaterThanOrEqualTo(1));
            }
            else Assert.That(h.Director.History.Any(r => h.Director.Settings.Deck.Single(d => d.Id == r.DefinitionId).Size == HotelSituationSize.Medium), Is.True,
                "Opening minor work must leave affordable room for an eligible larger premise in this tended hotel.");
            Assert.That(seen.Count, Is.GreaterThan(0), "A tended occupied hotel should offer an eligible premise during these days.");
            LogAssert.NoUnexpectedReceived();
        }
    }
}
