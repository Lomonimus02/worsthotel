using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;

namespace WorstHotel.Tests
{
    /// <summary>Production settings and natural seeded schedules; explicitly headless travel,
    /// staging, staff key/linen actions and repair holds. This is economic evidence, not an EXE/physics playtest.</summary>
    public sealed class ContinuousEconomyComparisonTests
    {
        const string AssetPath = "Assets/_WorstHotel/ScriptableObjects/PrototypeSession.asset";
        enum Policy { SequentialRooms, WarmRoomAssignment, WarmRoomsAndWelcomeBlankets }

        sealed class Run
        {
            public readonly int Occupancy;
            public readonly Policy Management;
            public readonly SessionSettings Settings;
            public readonly HotelSimulation Hotel;
            public readonly RoomState[] Rooms;
            public readonly Dictionary<string, string> Schedules = new Dictionary<string, string>();
            public readonly Dictionary<int, float> FirstFailureAt = new Dictionary<int, float>();
            public readonly Dictionary<int, int> FailuresByDay = new Dictionary<int, int>();
            public Action<float> AdvanceHeadlessAdapters;
            public int Failures, CheckIns, Departures, Contacts, Showers, Gross, Refunds, Maintenance, Capital, Net, Receipts;
            public int MinCash, EndingCash, ReportCount;
            public float MaxLoad, MaxStress, MinOccupiedTemperature = 100, QuietSeconds, LongestQuiet, quietRun;
            public int CashAtFirstReport, ReceiptsAtFirstReport;
            public int EarlyReceipts;
            public string FirstReportEarlyCheckoutTimes, EarlyCheckoutTimes;
            public int BlanketsDelivered, BlanketVisits;
            public int RadiatorInspections, RadiatorAdjustments, MaxOccupiedRadiatorSetting;
            public float BlanketStaffSeconds;
            public Run(int occupancy, SessionConfig config, Policy management = Policy.SequentialRooms)
            {
                Occupancy = occupancy; Management = management; Settings = config.ToData();
                Rooms = Settings.Rooms.Select(profile => new RoomState(profile)).ToArray();
                Hotel = new HotelSimulation(Settings, Rooms, config.living.ToData(), config.needs.ToData(),
                    config.noise.ToData(), config.heater.ToData(), config.electricity.ToData(), config.housekeeping.ToData(),
                    config.services.ToData(), config.infrastructure.ToData(), config.OperationsData());
                MinCash = Settings.Economy.StartingCash;
                Hotel.Boiler.OnFailureStarted += () =>
                {
                    Failures++;
                    int day = Hotel.Calendar.Day;
                    if (!FirstFailureAt.ContainsKey(day)) FirstFailureAt.Add(day, Hotel.Elapsed);
                    FailuresByDay[day] = FailuresByDay.TryGetValue(day, out int count) ? count + 1 : 1;
                };
                Require(Hotel.StartOperations());
            }

            public void Sample(float dt)
            {
                MaxLoad = Math.Max(MaxLoad, Hotel.Boiler.Load);
                MaxStress = Math.Max(MaxStress, Hotel.Boiler.Stress01);
                MinCash = Math.Min(MinCash, Hotel.Economy.Cash);
                var occupied = Rooms.Where(room => room.Occupied).ToArray();
                if (occupied.Length > 0)
                {
                    MinOccupiedTemperature = Math.Min(MinOccupiedTemperature, occupied.Min(room => room.Temperature));
                    MaxOccupiedRadiatorSetting = Math.Max(MaxOccupiedRadiatorSetting, occupied.Max(room => room.RadiatorSetting));
                }
                // Quiet means actual occupied room operation has spare heating capacity, no
                // shower draw and no media/plumbing source above .2, not merely an empty lobby.
                bool quiet = occupied.Length > 0 && !Hotel.Boiler.Failed && Hotel.Boiler.Reserve >= 0 &&
                    Hotel.HeatingDemands.All(row => row.HotWater == 0) && Hotel.Noise.Sources.All(source => source.NoiseOutput <= .2f);
                quietRun = quiet ? quietRun + dt : 0;
                if (quiet) QuietSeconds += dt;
                LongestQuiet = Math.Max(LongestQuiet, quietRun);
                if (ReportCount == 0 && Hotel.DayReports.Count > 0)
                {
                    CashAtFirstReport = Hotel.Economy.Cash;
                    var first = Hotel.DayReports[0];
                    ReceiptsAtFirstReport = first.Receipts.Count;
                    // Scheduled checkout is after this report. Earlier revenue must come
                    // from an actual committed early departure, never a booking payment.
                    foreach (var receipt in first.Receipts)
                    {
                        var guest = Hotel.Guests.Single(item => item.GuestId == receipt.GuestId);
                        Assert.That(receipt.EarlyCheckout, Is.True, receipt.GuestId);
                        Assert.That(guest.EarlyCheckout.State, Is.EqualTo(EarlyCheckoutState.Committed));
                        Assert.That(guest.Agent.HasReachedRoom && guest.ReceiptPosted, Is.True);
                        Assert.That(guest.Agent.State == GuestAgentState.CheckingOut || guest.Agent.State == GuestAgentState.Leaving ||
                            guest.Agent.State == GuestAgentState.Left, Is.True, "Payment follows the real departure transition.");
                        Assert.That(receipt.CheckoutAt, Is.EqualTo(guest.EarlyCheckout.CommittedAt));
                        Assert.That(receipt.CheckoutAt, Is.GreaterThanOrEqualTo(0).And.LessThan(Hotel.Calendar.FirstReportAt));
                        Assert.That(receipt.CheckoutAt, Is.LessThan(guest.Agent.CheckoutTime), "The original checkout contract is preserved.");
                        Assert.That(receipt.DepartureReason, Is.Not.Null.And.Not.Empty);
                        Assert.That(receipt.DepartureReason, Is.EqualTo(guest.EarlyCheckout.CauseDescription));
                    }
                    Assert.That((long)first.Cash, Is.EqualTo((long)first.OpeningCash + first.Receipts.Sum(item => item.Net) -
                        first.OperatingCost - first.MaintenanceSpend - first.CapitalSpend), "Early checkout revenue is paid exactly once in its real report period.");
                    FirstReportEarlyCheckoutTimes = string.Join(",", first.Receipts.Select(item => CheckoutTime(item.GuestId, item.CheckoutAt)));
                }
                ReportCount = Hotel.DayReports.Count;
            }

            public void SettleMeasuredTotals()
            {
                // D4 08 ends the three-calendar-day observation. The explicitly labelled D4
                // 11 tail posts the last cohort's real checkout, after the third 06:00 report.
                var snapshot = Hotel.CaptureSnapshot(804, 1);
                var published = Hotel.DayReports.SelectMany(report => report.Receipts).ToArray();
                var current = snapshot.Operations.PeriodReceipts;
                var ids = published.Select(item => item.GuestId).Concat(current.Select(item => item.GuestId)).ToArray();
                Assert.That(ids.Distinct().Count(), Is.EqualTo(ids.Length), "A stay must not appear in both a closed and current accounting period.");
                Receipts = ids.Length;
                EarlyReceipts = published.Count(item => item.EarlyCheckout) + current.Count(item => item.EarlyCheckout);
                EarlyCheckoutTimes = string.Join(",", published.Where(item => item.EarlyCheckout).Select(item => CheckoutTime(item.GuestId, item.CheckoutAt))
                    .Concat(current.Where(item => item.EarlyCheckout).Select(item => CheckoutTime(item.GuestId, item.CheckoutAt))));
                Gross = published.Sum(item => item.Price) + current.Sum(item => item.Price);
                Refunds = published.Sum(item => item.Compensation) + current.Sum(item => item.Compensation);
                Maintenance = Hotel.DayReports.Sum(report => report.MaintenanceSpend) + Hotel.PeriodMaintenanceSpend;
                Capital = Hotel.DayReports.Sum(report => report.CapitalSpend) + Hotel.PeriodCapitalSpend;
                Net = Gross - Refunds - Hotel.DayReports.Sum(report => report.OperatingCost) - Maintenance - Capital;
                EndingCash = Hotel.Economy.Cash;
                foreach (var report in Hotel.DayReports)
                    Assert.That((long)report.Cash, Is.EqualTo((long)report.OpeningCash + report.Net), "Report must display paid expenses without another debit.");
                Assert.That(published.All(item => item.Price > 0) && current.All(item => item.Price > 0), Is.True,
                    "Every receipt requires a real checked-in stay, not a fabricated booking payment.");
            }

            public override string ToString() => string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "N={0}; receipts={1}; gross={2}; refunds={3}; maintenance={4}; capital={5}; net={6}; cash={7}; minCash={8}; " +
                "peakLoad={9:F3}; peakStress={10:F3}; failures={11}; minOccupiedC={12:F2}; quietSeconds={13:F1}; longestQuiet={14:F1}; " +
                "showers={15}; contacts={16}; reportCount={17}; firstReportCash={18}; firstReportReceipts={19}; " +
                "policy={20}; blankets={21}; blanketVisits={22}; blanketStaffSeconds={23:F1}; " +
                "valveInspections={24}; valveAdjustments={25}; peakOccupiedValve={26}; firstFailureElapsedAndCountByDay={27}; " +
                "earlyReceipts={28}; earlyCheckoutElapsed={29}; firstReportEarlyCheckoutElapsed={30}",
                Occupancy, Receipts, Gross, Refunds, Maintenance, Capital, Net, EndingCash, MinCash,
                MaxLoad, MaxStress, Failures, MinOccupiedTemperature, QuietSeconds, LongestQuiet, Showers, Contacts,
                ReportCount, CashAtFirstReport, ReceiptsAtFirstReport, Management, BlanketsDelivered, BlanketVisits, BlanketStaffSeconds,
                RadiatorInspections, RadiatorAdjustments, MaxOccupiedRadiatorSetting,
                string.Join(",", FirstFailureAt.OrderBy(pair => pair.Key).Select(pair => string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "D{0}@{1:F1}/{2}", pair.Key, pair.Value, FailuresByDay[pair.Key]))),
                EarlyReceipts, EarlyCheckoutTimes, FirstReportEarlyCheckoutTimes);

            static string CheckoutTime(string guestId, float at) => string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0}@{1:F3}", guestId, at);
        }

        sealed class Travel
        {
            public string Signature;
            public float Due, NextAnchor, VacateAt;
            public int Actor = -1;
            public bool Vacated;
        }

        sealed class BlanketVisit
        {
            public string GuestId, ItemId;
            public int Stage; // 0: walk to shelf, 1: walk/wait at room, 2: return to shelf.
            public float Due, GiveUpAt, Hallway;
        }

        /// <summary>Two staff slots share actual key handling, timed manual linen work and repair.
        /// Guest route durations are declared adapters, never simulated collider or NavMesh travel.</summary>
        sealed class Boundaries
        {
            readonly Run run;
            readonly Dictionary<string, Travel> travel = new Dictionary<string, Travel>();
            readonly string[] staff = new string[2];
            readonly float[] releaseReplyAt = new float[2];
            readonly TimedManualTurnoverAdapter turnover;
            readonly RepairSequence repair;
            readonly BlanketVisit[] blankets = new BlanketVisit[2];
            readonly HashSet<string> blanketVisits = new HashSet<string>();
            bool repairing;
            public Boundaries(Run run)
            {
                this.run = run;
                turnover = new TimedManualTurnoverAdapter(run.Hotel, run.Rooms, staff,
                    run.Management == Policy.WarmRoomsAndWelcomeBlankets ? (Action<int, int>)InspectVacantRadiator : null, 3);
                repair = new RepairSequence(run.Hotel.Boiler, run.Settings.Boiler, run.Hotel.EmergencyPatchBoiler);
            }

            void InspectVacantRadiator(int actor, int roomId)
            {
                var room = run.Rooms.Single(item => item.Profile.Id == roomId);
                Assert.That(room.Occupied, Is.False, "This is a turnover inspection, never an override of a current guest's valve.");
                Assert.That(room.DepartingGuestId, Is.Null, "The departing body must have cleared the room before staff prepare it.");
                Assert.That(room.Cleanliness, Is.EqualTo(Cleanliness.Dirty), "Inspection consumes staff time before the room is released as ready.");
                Require(run.Hotel.RequestStaffRoomAccess(actor, roomId));
                run.RadiatorInspections++;
                if (room.RadiatorSetting == 1) return;
                Require(run.Hotel.SetRadiatorSetting(actor, roomId, 1));
                run.RadiatorAdjustments++;
            }

            public void Tick(float dt)
            {
                var hotel = run.Hotel; float now = hotel.Elapsed;
                for (int actor = 0; actor < 2; actor++)
                    if (staff[actor] == "reply" && now >= releaseReplyAt[actor]) staff[actor] = null;
                foreach (var guest in hotel.Guests.ToArray())
                {
                    var agent = guest.Agent;
                    if (!travel.TryGetValue(guest.GuestId, out var route)) travel.Add(guest.GuestId, route = new Travel());
                    if (!run.Schedules.ContainsKey(guest.GuestId))
                    {
                        run.Schedules.Add(guest.GuestId, string.Join("|", agent.Schedule.Activities.Select(item => item.Activity + ":" + item.Duration.ToString("R", System.Globalization.CultureInfo.InvariantCulture))));
                        Require(hotel.RegisterGuestPhysicalStaging(guest.GuestId));
                    }
                    string signature = agent.State + ":" + agent.Activity + ":" + agent.StateChangedAt + ":" + agent.ResponseActionId + ":" + agent.ResponseActionVersion;
                    if (signature != route.Signature)
                    {
                        if (route.Actor >= 0) { staff[route.Actor] = null; route.Actor = -1; }
                        route.Signature = signature; route.Vacated = false; route.NextAnchor = now;
                        float hallway = (DoorZ(guest.RoomId) + 10) / 1.35f + .8f;
                        route.Due = now + (agent.State == GuestAgentState.Arriving ? 7 :
                            agent.State == GuestAgentState.GoingToRoom || agent.State == GuestAgentState.LeavingRoom ||
                            agent.State == GuestAgentState.ReturningToRoom || agent.IsServiceReceptionTrip ? hallway : 1.2f);
                        route.VacateAt = now + 5;
                    }

                    if (agent.State == GuestAgentState.Arriving && now >= route.Due)
                        Require(hotel.SignalGuestReachedReception(guest.GuestId));
                    else if (agent.State == GuestAgentState.WaitingForCheckIn)
                    {
                        var room = run.Rooms.Single(item => item.Profile.Id == guest.RoomId);
                        bool ready = !room.Occupied && room.DepartingGuestId == null && room.Cleanliness == Cleanliness.Clean;
                        if (ready && route.Actor < 0)
                        {
                            int actor = Array.FindIndex(staff, slot => slot == null);
                            if (actor >= 0) { route.Actor = actor; staff[actor] = guest.GuestId; route.Due = now + hotel.LivingSettings.KeyRetrievalEstimateSeconds; }
                        }
                        if (ready && route.Actor >= 0 && now >= route.Due)
                        { Require(ModelKeyHandoff.CheckIn(hotel, route.Actor, guest.GuestId)); run.CheckIns++; }
                    }
                    else if (agent.State == GuestAgentState.GoingToRoom && now >= route.Due)
                        Require(hotel.SignalGuestReachedRoom(guest.GuestId));
                    else if (agent.State == GuestAgentState.LeavingRoom && now >= route.Due)
                        Require(hotel.SignalGuestLeftRoom(guest.GuestId));
                    else if (agent.State == GuestAgentState.ReturningToRoom && now >= route.Due)
                        Require(hotel.SignalGuestReturnedRoom(guest.GuestId));
                    else if (agent.State == GuestAgentState.Leaving)
                    {
                        if (!route.Vacated && now >= route.VacateAt)
                        {
                            foreach (var room in run.Rooms.Where(item => item.DepartingGuestId == guest.GuestId))
                                Require(hotel.SignalGuestVacatedRoom(guest.GuestId, room.Profile.Id));
                            route.Vacated = true;
                        }
                        if (route.Vacated && now >= route.VacateAt + (DoorZ(guest.RoomId) + 5) / 1.35f)
                        { Require(hotel.SignalGuestLeft(guest.GuestId)); run.Departures++; }
                    }

                    if (agent.ResponseActionId != null && now >= route.NextAnchor)
                    {
                        var response = hotel.Services.FindResponse(agent.ResponseActionId);
                        GuestResponseAnchor? anchor = agent.State == GuestAgentState.ReturningFromServiceReception && now >= route.Due ? GuestResponseAnchor.AssignedRoom :
                            agent.State == GuestAgentState.GoingToServiceReception && now >= route.Due ? GuestResponseAnchor.Reception :
                            agent.InAssignedRoom && agent.Activity == GuestActivity.AdjustRadiator && now >= route.Due ? GuestResponseAnchor.Radiator :
                            agent.InAssignedRoom && agent.Activity == GuestActivity.CallReception && now >= route.Due && response?.AttemptStartedAt < 0 ? GuestResponseAnchor.RoomPhone : (GuestResponseAnchor?)null;
                        if (anchor.HasValue)
                        {
                            // A real cause may recover en route, or the line may be busy. These
                            // rejected acknowledgements are normal; the response system cancels/retries.
                            hotel.SignalGuestResponseAnchorReached(guest.GuestId, agent.ResponseActionId, agent.ResponseActionVersion, anchor.Value);
                            route.NextAnchor = now + 1;
                        }
                    }
                    else if (agent.ResponseActionId == null && agent.InAssignedRoom && !agent.ActivityStaged && now >= route.Due)
                    {
                        Require(hotel.SignalGuestActivityReady(guest.GuestId, agent.State, agent.Activity));
                        if (agent.Activity == GuestActivity.Shower) run.Showers++;
                    }
                }
                ReplyToKnownContacts();
                AdvanceRepair(dt);
                AdvanceWelcomeBlankets(dt);
                turnover.Tick();
            }

            void ReplyToKnownContacts()
            {
                var hotel = run.Hotel; int actor = Array.FindIndex(staff, slot => slot == null);
                if (actor < 0) return;
                CommandResult? result = null;
                var incoming = hotel.Services.IncomingCall;
                if (incoming != null) result = hotel.AnswerIncomingServiceCall(actor, incoming.Id);
                else
                {
                    var response = hotel.Services.Responses.FirstOrDefault(item => item.Phase == GuestResponsePhase.Contacting &&
                        item.Channel == GuestContactChannel.Reception && item.AttemptStartedAt >= 0);
                    if (response != null) result = hotel.TalkToServiceGuest(actor, response.GuestId, response.Id);
                }
                if (result.HasValue && result.Value.Success)
                { run.Contacts++; staff[actor] = "reply"; releaseReplyAt[actor] = hotel.Elapsed + .6f; }
                // Shared deliberately modest service policy: promptly decline optional promises
                // rather than invent delivery/credits or leave guests locked in a direct decision.
                foreach (var item in hotel.Services.Cases.Where(item => item.Active && item.IsKnownToHotel).ToArray())
                    Require(hotel.RespondToService(actor, item.Id, false));
                // Closing a real disclosed compensation discussion is an explicit refusal,
                // not a hidden case resolution or a free credit. No private concern is inspected.
                foreach (var intent in hotel.Services.Intents.Where(item => item.Active &&
                    item.Purpose == ServiceIntentPurpose.CompensationDiscussion).ToArray())
                    Require(hotel.AcceptConsequences(actor, intent.GuestId));
            }

            void AdvanceWelcomeBlankets(float dt)
            {
                if (run.Management != Policy.WarmRoomsAndWelcomeBlankets) return;
                var hotel = run.Hotel; float now = hotel.Elapsed;
                for (int actor = 0; actor < blankets.Length; actor++)
                {
                    var visit = blankets[actor];
                    if (visit != null)
                    {
                        run.BlanketStaffSeconds += dt;
                        if (now < visit.Due) continue;
                        if (visit.Stage == 0)
                        {
                            var item = hotel.Services.Items.FirstOrDefault(candidate => candidate.Kind == ServiceItemKind.Blanket &&
                                candidate.Location == ServiceItemLocation.OnShelf);
                            if (item == null) { blankets[actor] = null; staff[actor] = null; continue; }
                            Require(hotel.TakeServiceItem(actor, item.Id)); visit.ItemId = item.Id;
                            visit.Stage = 1; visit.Due = now + visit.Hallway + 3; // Walk and knock/ask permission.
                            visit.GiveUpAt = visit.Due + 10; // One short doorway wait; never wait through sleep/a shower.
                        }
                        else if (visit.Stage == 1)
                        {
                            var guest = hotel.Guests.FirstOrDefault(candidate => candidate.GuestId == visit.GuestId);
                            bool available = guest?.Agent.InAssignedRoom == true && guest.Agent.ActivityStaged &&
                                guest.Agent.State != GuestAgentState.Sleeping && guest.Agent.Activity != GuestActivity.Shower &&
                                !guest.Agent.IsRelocating;
                            if (available && guest.Memory.BlanketsDelivered == 0 &&
                                hotel.RequestStaffRoomAccess(actor, guest.RoomId).Success)
                            {
                                Require(hotel.DeliverBlanket(actor, guest.GuestId)); run.BlanketsDelivered++;
                                visit.Stage = 2; visit.Due = now + visit.Hallway;
                            }
                            else if (now >= visit.GiveUpAt)
                            { visit.Stage = 2; visit.Due = now + visit.Hallway; }
                        }
                        else
                        {
                            if (hotel.Services.HeldBy(actor)?.Id == visit.ItemId)
                                Require(hotel.ReturnServiceItem(actor, visit.ItemId));
                            blankets[actor] = null; staff[actor] = null;
                        }
                        continue;
                    }
                    if (staff[actor] != null || hotel.Boiler.Failed) continue;
                    // A disclosed booking preference drives a once-per-stay welcome action.
                    // No private current need, future activity, valve or stress is read to trigger it.
                    var target = hotel.Guests.FirstOrDefault(guest => guest.Application.Archetype.Kind == GuestKind.ColdSensitive &&
                        guest.Agent.CheckedIn && (guest.Agent.State == GuestAgentState.GoingToRoom || guest.Agent.InAssignedRoom) &&
                        guest.Memory.BlanketsDelivered == 0 && !blanketVisits.Contains(guest.GuestId));
                    if (target == null || !hotel.Services.Items.Any(item => item.Kind == ServiceItemKind.Blanket &&
                        item.Location == ServiceItemLocation.OnShelf)) continue;
                    blanketVisits.Add(target.GuestId); run.BlanketVisits++;
                    staff[actor] = "blanket:" + target.GuestId;
                    // Same 3.5m/s staff speed and storage/door path allowance as the shared
                    // manual linen adapter. Start from reception conservatively each visit.
                    blankets[actor] = new BlanketVisit { GuestId = target.GuestId,
                        Due = now + (Math.Abs(30.65f - 1.3f) + 5.8f) / 3.5f + .8f,
                        Hallway = (Math.Abs(30.65f - DoorZ(target.RoomId)) + 9.9f) / 3.5f + .8f };
                }
            }

            void AdvanceRepair(float dt)
            {
                var hotel = run.Hotel; var boiler = hotel.Boiler;
                if (!boiler.Failed)
                {
                    if (repairing) { staff[0] = staff[1] = null; repairing = false; }
                    return;
                }
                if (!repairing)
                {
                    if (staff.Any(slot => slot != null) || hotel.Economy.Cash < run.Settings.Economy.CheapPatchCost) return;
                    repairing = true; staff[0] = "relief"; staff[1] = "repair";
                    Require(boiler.SetRelief(0, true)); repair.ResetForFailure();
                }
                repair.Refresh();
                if (!boiler.InRepairBand) return;
                switch (repair.Step)
                {
                    case RepairStep.Panel: Require(repair.Press(RepairControlKind.Panel, 1)); break;
                    case RepairStep.Breaker: Require(repair.Press(RepairControlKind.Breaker, 1)); break;
                    case RepairStep.LatchA:
                        Require(repair.Press(RepairControlKind.LatchA, 1)); Require(repair.HoldLatch(RepairControlKind.LatchA, 1, dt)); break;
                    case RepairStep.LatchB:
                        Require(repair.Press(RepairControlKind.LatchB, 1)); Require(repair.HoldLatch(RepairControlKind.LatchB, 1, dt)); break;
                    case RepairStep.Restart:
                        if (hotel.Economy.Cash >= run.Settings.Economy.CheapPatchCost) Require(repair.Press(RepairControlKind.Restart, 1));
                        break;
                }
            }
            static float DoorZ(int room) => 10 + (room - 101) / 2 * 7;
        }

        static void Require(CommandResult result) => Assert.That(result.Success, Is.True, result.Message);

        static Run Simulate(int occupancy, SessionConfig config, Policy policy = Policy.SequentialRooms)
        {
            var run = new Run(occupancy, config, policy); var hotel = run.Hotel; var boundaries = new Boundaries(run);
            run.AdvanceHeadlessAdapters = boundaries.Tick;
            int bookedThrough = 0;
            float until = hotel.Calendar.At(4, 11); // Third-cohort checkout tail; no fourth-day bookings.
            while (hotel.Elapsed < until)
            {
                if (hotel.Calendar.Day <= 3 && hotel.Calendar.Day > bookedThrough)
                {
                    bookedThrough = hotel.Calendar.Day;
                    var offers = hotel.BookingOffers.Where(item => item.ArrivalDay == bookedThrough).OrderBy(item => item.Id, StringComparer.Ordinal).Take(occupancy).ToArray();
                    Assert.That(offers.Length, Is.EqualTo(occupancy));
                    // Same accepted offers and prices. Colder guests get the least heat loss;
                    // the two tolerant budget guests take 102/104 and the business guest 105.
                    int[] warmRooms = { 102, 101, 105, 104, 103 };
                    for (int i = 0; i < offers.Length; i++) Require(hotel.AcceptBooking(0, offers[i].Id,
                        policy == Policy.SequentialRooms ? 101 + i : warmRooms[i], offers[i].Application.ReferencePrice));
                }
                float dt = Math.Min(1 / run.Settings.TickRate, until - hotel.Elapsed);
                hotel.Tick(dt); boundaries.Tick(dt); run.Sample(dt);
            }
            run.SettleMeasuredTotals();
            return run;
        }

        [Test]
        [Timeout(300000)] // Six natural three-night policies plus their earned-capital continuation.
        public void ProductionSeededThreeDayBookingStrategiesExposeBothRevenueOpportunityAndCausalCosts()
        {
            var config = AssetDatabase.LoadAssetAtPath<SessionConfig>(AssetPath);
            Assert.That(config, Is.Not.Null);
            Assert.That(config.continuousOperations, Is.True);
            Assert.That(config.economy.startingCash, Is.EqualTo(750), "Production working capital covers the first 06:00 bill before the first 10:00 checkout.");
            Assert.That(config.economy.dailyOperatingCost, Is.EqualTo(450));
            var runs = Enumerable.Range(3, 4).Select(count => Simulate(count, config)).ToArray();
            var placement = Simulate(5, config, Policy.WarmRoomAssignment);
            var managed = Simulate(5, config, Policy.WarmRoomsAndWelcomeBlankets);
            TestContext.Out.WriteLine("Production seed=" + config.living.seed + "; natural schedules; D1 08 to D4 08 + D4 11 checkout tail; no day4 bookings. " +
                "Headless two-staff key/linen/repair adapters; optional services declined, no comp credit or upgrades. Quiet requires an occupied hotel with spare heat capacity and no active shower/loud emitter.");
            TestContext.Out.WriteLine("Proactive counterfactuals: same five offers/prices, cold-sensitive guests in 101/103; optional welcome blankets use real finite stock, shared staff, 3.5m/s storage/room travel + 3s permission + max10s wait + return. One visit per cold-sensitive stay. Managed turnover also spends3s inspecting each vacant used room's radiator and returns it to1 through the normal staff command before the bed is ready; never overrides an occupied guest or changes their schedule.");
            foreach (var run in runs.Concat(new[] { placement, managed }))
            {
                TestContext.Out.WriteLine(run.ToString());
                Assert.That(run.CheckIns, Is.EqualTo(run.Occupancy * 3), run.ToString());
                Assert.That(run.Departures, Is.EqualTo(run.Occupancy * 3), run.ToString());
                Assert.That(run.Receipts, Is.EqualTo(run.Occupancy * 3), run.ToString());
                Assert.That(run.ReportCount, Is.EqualTo(3));
                Assert.That(run.EndingCash, Is.EqualTo(run.Settings.Economy.StartingCash + run.Net), "Every checkout, report and patch is counted once. " + run);
                Assert.That(run.Capital, Is.Zero);
                Assert.That(run.Showers, Is.GreaterThan(0), "Natural schedules must reach real staged demand, not remain frozen in quiet state.");
                foreach (var pair in runs[0].Schedules)
                    Assert.That(run.Schedules[pair.Key], Is.EqualTo(pair.Value), "The same accepted guest retains the same seeded activity plan across strategies.");
            }
            foreach (var cautious in runs.Take(2))
            {
                Assert.That(cautious.ReceiptsAtFirstReport, Is.Zero, "Cautious three/four-room stays reach their scheduled checkout after the first overnight bill. " + cautious);
                Assert.That(cautious.MinCash, Is.GreaterThanOrEqualTo(0), cautious.ToString());
                Assert.That(cautious.Net, Is.GreaterThan(0), "Three/four-booking cautious operation must remain viable. " + cautious);
                Assert.That(cautious.LongestQuiet, Is.GreaterThan(20), "Even an occupied hotel must have a real quiet interval. " + cautious);
            }
            for (int i = 1; i < runs.Length; i++)
                Assert.That(runs[i].Gross, Is.GreaterThan(runs[i - 1].Gross), "Accepting an additional real stay offers greater revenue before its consequences.");
            Assert.That(runs[3].MaxLoad, Is.GreaterThan(runs[0].MaxLoad));
            Assert.That(runs[3].MaxStress, Is.GreaterThan(runs[0].MaxStress));
            Assert.That(runs[3].Refunds + runs[3].Maintenance, Is.GreaterThan(0), "Ambitious occupancy must pay its measured consequences, without a forced day/failure trigger.");
            foreach (var proactive in new[] { placement, managed })
            {
                Assert.That(proactive.Gross, Is.EqualTo(runs[2].Gross), "Management changes room choice and staff work, never accepted revenue.");
                foreach (var plan in runs[2].Schedules) Assert.That(proactive.Schedules[plan.Key], Is.EqualTo(plan.Value));
            }
            Assert.That(managed.BlanketVisits, Is.EqualTo(6), "Exactly two cold-sensitive welcome visits per cohort.");
            Assert.That(managed.BlanketsDelivered, Is.GreaterThan(0).And.LessThanOrEqualTo(6));
            Assert.That(managed.BlanketStaffSeconds, Is.GreaterThan(managed.BlanketsDelivered * 30), "Delivery consumes shared finite staff time, not immediate inventory assignment.");
            Assert.That(managed.RadiatorInspections, Is.GreaterThan(0));
            Assert.That(managed.RadiatorAdjustments, Is.GreaterThan(0), "Preparation must actually change inherited guest valve settings through staff actions.");
            Assert.That(managed.Failures, Is.LessThan(runs[2].Failures), "A practical management policy must improve the actual causal failure count. " + managed);
            Assert.That(managed.Failures, Is.LessThanOrEqualTo(3), "Managed five-room operation should require at most one emergency per occupied overnight on average, not a slightly slower repair loop. " + managed);
            Assert.That(managed.Maintenance, Is.LessThan(runs[2].Maintenance), managed.ToString());
            Assert.That(managed.Net, Is.GreaterThan(runs[2].Net), managed.ToString());
            Assert.That(managed.Net, Is.GreaterThan(runs[1].Net),
                "Successfully managing a fifth real booking should offer more net income than cautious four-room operation. " + managed);
            Assert.That(managed.MinCash, Is.GreaterThanOrEqualTo(0), "Managed five-room operation must not depend on invisible credit. " + managed);
            Assert.That(managed.Net, Is.GreaterThan(0), managed.ToString());
            AssertEarnedUpgradeAndLaterGrowth(managed);
        }

        static void AssertEarnedUpgradeAndLaterGrowth(Run run)
        {
            // Continue the already measured hotel, preserving the actual staff/linen/carry
            // adapter state. The six policy rows above still end at D4 11, before this separate
            // investment scenario. No new matrix, cash injection or reconstructed checkpoint.
            var hotel = run.Hotel;
            Assert.That(hotel.Calendar.Day, Is.EqualTo(4));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(run.EndingCash));
            int price = run.Settings.Economy.BoilerUpgradeCost;
            Assert.That(hotel.Economy.Cash, Is.GreaterThanOrEqualTo(price + run.Settings.Economy.DailyOperatingCost),
                "Actual settled stays must fund both the investment and the next overnight bill.");
            var offers = hotel.BookingOffers.Where(offer => offer.ArrivalDay == 4)
                .OrderBy(offer => offer.ArrivalAt).ToArray();
            Assert.That(offers.Length, Is.EqualTo(8));
            int[] warmRooms = { 102, 101, 105, 104, 103 };
            for (int index = 0; index < 5; index++)
                Require(hotel.AcceptBooking(0, offers[index].Id, warmRooms[index], offers[index].Application.ReferencePrice));
            var growthOffer = offers.Last(); // A real later enquiry: its time/guest profile is never edited.
            while (hotel.Elapsed < growthOffer.ArrivalAt - 1 &&
                (hotel.HeatingDemands.Count(row => row.GuestId != null) < 5 || hotel.Boiler.Failed))
                AdvanceInvestmentContinuation(run, Math.Min(1 / run.Settings.TickRate, growthOffer.ArrivalAt - 1 - hotel.Elapsed));
            Assert.That(hotel.HeatingDemands.Count(row => row.GuestId != null), Is.EqualTo(5),
                "Normal dated arrivals, finite key handoffs and turnover must establish five actual room owners first.");
            Assert.That(hotel.Boiler.Failed, Is.False);
            Assert.That(hotel.Elapsed, Is.LessThan(growthOffer.ArrivalAt));
            Assert.That(hotel.Boiler.LoadOverride, Is.Null);
            float beforeRatio = hotel.Boiler.LoadRatio, beforeHeat = hotel.Boiler.HeatingOutput;
            float load = hotel.Boiler.Load, condition = hotel.Boiler.Condition, stress = hotel.Boiler.Stress01;
            float pressure = hotel.Boiler.Pressure, capacity = hotel.Boiler.EffectiveCapacity, rated = hotel.Boiler.RatedCapacity;
            bool patched = hotel.Boiler.EmergencyPatchActive;
            var rows = hotel.HeatingDemands.ToArray();
            float vacantSixth = rows.Single(row => row.RoomId == 106).Total;
            int cash = hotel.Economy.Cash, capital = hotel.PeriodCapitalSpend;
            Assert.That(hotel.Boiler.CapacityBand, Is.Not.EqualTo(CapacityBand.Comfortable),
                "The actual natural five-room workload must make this upgrade relevant.");

            // Authoritative model transaction; actual walking/UI purchase input has its own
            // PlayMode test. No simulated time or customer/demand adjustment intervenes here.
            Require(hotel.PurchaseBoilerUpgrade(0));
            Assert.That(hotel.Economy.Cash, Is.EqualTo(cash - price));
            Assert.That(hotel.Economy.Cash, Is.GreaterThanOrEqualTo(run.Settings.Economy.DailyOperatingCost));
            Assert.That(hotel.PeriodCapitalSpend, Is.EqualTo(capital + price));
            Assert.That(hotel.HeatingDemands, Is.EqualTo(rows));
            Assert.That(hotel.Boiler.Load, Is.EqualTo(load));
            Assert.That(hotel.Boiler.Condition, Is.EqualTo(condition));
            Assert.That(hotel.Boiler.Stress01, Is.EqualTo(stress));
            Assert.That(hotel.Boiler.Pressure, Is.EqualTo(pressure));
            Assert.That(hotel.Boiler.EmergencyPatchActive, Is.EqualTo(patched));
            Assert.That(hotel.Boiler.RatedCapacity, Is.EqualTo(rated * run.Settings.Boiler.Capacity.CapacityUpgradeMultiplier).Within(.00001f));
            Assert.That(hotel.Boiler.EffectiveCapacity, Is.GreaterThan(capacity));
            Assert.That(hotel.Boiler.LoadRatio, Is.LessThan(beforeRatio));
            Assert.That(hotel.Boiler.HeatingOutput, Is.GreaterThanOrEqualTo(beforeHeat));
            float upgradedRated = hotel.Boiler.RatedCapacity, ratioAfterPurchase = hotel.Boiler.LoadRatio;

            Require(hotel.AcceptBooking(0, growthOffer.Id, 106, growthOffer.Application.ReferencePrice));
            Assert.That(hotel.HeatingDemands.Count(row => row.GuestId != null), Is.EqualTo(5), "The enquiry itself cannot create a sixth live consumer.");
            float reachedRoomDeadline = hotel.Calendar.At(4, 21);
            GuestStay sixth = null;
            while (hotel.Elapsed < reachedRoomDeadline)
            {
                AdvanceInvestmentContinuation(run, Math.Min(1 / run.Settings.TickRate, reachedRoomDeadline - hotel.Elapsed));
                sixth = hotel.Guests.FirstOrDefault(guest => guest.GuestId == growthOffer.Id);
                if (sixth?.Agent.HasReachedRoom == true && sixth.Agent.InAssignedRoom) break;
            }
            Assert.That(sixth, Is.Not.Null);
            Assert.That(sixth.Agent.HasReachedRoom && sixth.Agent.InAssignedRoom, Is.True, "The additional dated guest must receive a key and reach their actual owned room.");
            Assert.That(hotel.HeatingDemands.Count(row => row.GuestId != null), Is.EqualTo(6));
            var sixthDemand = hotel.HeatingDemands.Single(row => row.RoomId == 106);
            Assert.That(sixthDemand.GuestId, Is.EqualTo(growthOffer.Id));
            Assert.That(sixthDemand.Total, Is.GreaterThan(vacantSixth));
            float sameMomentFiveLoad = hotel.Boiler.Load - sixthDemand.Total + vacantSixth;
            float sameMomentFiveRatio = sameMomentFiveLoad / hotel.Boiler.EffectiveCapacity;
            float actualSixRatio = hotel.Boiler.LoadRatio;
            Assert.That(hotel.Boiler.Load, Is.EqualTo(hotel.HeatingDemands.Sum(row => row.Total)).Within(.00001f));
            Assert.That(actualSixRatio, Is.GreaterThan(sameMomentFiveRatio),
                "Use the other five rooms' contemporaneous demand so unrelated natural shower timing cannot masquerade as the growth effect.");
            Assert.That(hotel.Boiler.RatedCapacity, Is.EqualTo(upgradedRated));
            float growthAt = hotel.Elapsed, peakRatio = actualSixRatio, peakStress = hotel.Boiler.Stress01;
            int failuresAtGrowth = run.Failures;
            float midnight = hotel.Calendar.At(5, .1f);
            while (hotel.Elapsed < midnight)
            {
                AdvanceInvestmentContinuation(run, Math.Min(1 / run.Settings.TickRate, midnight - hotel.Elapsed));
                peakRatio = Math.Max(peakRatio, hotel.Boiler.LoadRatio);
                peakStress = Math.Max(peakStress, hotel.Boiler.Stress01);
            }
            Assert.That(hotel.Calendar.Day, Is.EqualTo(5));
            Assert.That(hotel.Boiler.CapacityUpgradePurchased, Is.True);
            Assert.That(hotel.Boiler.RatedCapacity, Is.EqualTo(upgradedRated), "Midnight and increased ambition cannot secretly remove purchased capacity.");
            Assert.That(hotel.PeriodCapitalSpend, Is.EqualTo(capital + price), "No second capital debit or daily upgrade reset.");
            TestContext.Out.WriteLine(FormattableString.Invariant(
                $"Separate earned-capital continuation: day4 pre-purchase cash={cash}, paid={price}, ownedRooms=5, sameLoad={load:F4}, condition={condition:F3}, retainedStress={stress:F4}, ratio={beforeRatio:F4}->{ratioAfterPurchase:F4}; sixth actual arrival elapsed={growthAt:F1}, sixRatio={actualSixRatio:F4}, contemporaneous-fiveRatio={sameMomentFiveRatio:F4}; through day5 00:06 peakRatio={peakRatio:F4}, peakStress={peakStress:F4}, additionalFailures={run.Failures - failuresAtGrowth}, retainedRatedCapacity={hotel.Boiler.RatedCapacity:F3}, finalCash={hotel.Economy.Cash}. Natural pressure is measured, not a mandated failure."));
        }

        static void AdvanceInvestmentContinuation(Run run, float dt)
        {
            run.Hotel.Tick(dt);
            run.AdvanceHeadlessAdapters(dt);
        }

        [Test]
        public void NextFailureEndsAPaidPatchWithoutErasingItsCashDebitOrPeriodExpense()
        {
            var config = AssetDatabase.LoadAssetAtPath<SessionConfig>(AssetPath);
            var run = new Run(0, config); var hotel = run.Hotel;
            hotel.Boiler.ForceFailure(); // Labelled paid-transaction fixture, separate from the natural strategy run.
            Require(hotel.Boiler.SetRelief(0, true)); hotel.Tick(20);
            Require(hotel.EmergencyPatchBoiler(1));
            int cash = hotel.Economy.Cash, spent = hotel.PeriodMaintenanceSpend;
            Assert.That(spent, Is.EqualTo(run.Settings.Economy.CheapPatchCost));
            Assert.That(cash, Is.EqualTo(run.Settings.Economy.StartingCash - spent));
            hotel.Boiler.ForceFailure();
            Assert.That(hotel.Boiler.EmergencyPatchActive, Is.False);
            Assert.That(hotel.Economy.Cash, Is.EqualTo(cash));
            Assert.That(hotel.PeriodMaintenanceSpend, Is.EqualTo(spent));
            Assert.That(hotel.Boiler.Condition, Is.EqualTo(run.Settings.Boiler.Capacity.EmergencyPatchCondition));
        }
    }
}
