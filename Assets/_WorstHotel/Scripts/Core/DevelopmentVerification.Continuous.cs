#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WorstHotel
{
    public sealed partial class DevelopmentVerification
    {
        bool continuousTour, continuousReady, continuousVerified, continuousSamplingFinished, continuousCashConserved;
        HotelSimulation continuousModel;
        RoomState[] continuousRooms;
        object continuousClock;
        long continuousEpoch;
        int continuousStartingCash, continuousMinimumCash, continuousCalendarSeen, continuousReportsSeen;
        float continuousLastTime, continuousPeakLoad, continuousPeakRatio, continuousMinimumCondition = 100;
        int continuousFailures, continuousTrips;
        bool continuousWasFailed;
        readonly HashSet<string> continuousTripped = new HashSet<string>();
        readonly List<ContinuousCohort> continuousCohorts = new List<ContinuousCohort>();
        ReceiptSnapshot[] continuousReceipts = Array.Empty<ReceiptSnapshot>();
        HotelModelSnapshot continuousFinalSnapshot;
        float continuousObservationEnd, continuousTailEnd;

        sealed class ContinuousCohort
        {
            public int Day;
            public string[] Ids;
            public readonly Dictionary<string, GuestStay> Stays = new Dictionary<string, GuestStay>();
            public readonly HashSet<string> CheckedIn = new HashSet<string>(), RoomArrivals = new HashSet<string>(), Departures = new HashSet<string>();
            public readonly Dictionary<string, Vector3> Positions = new Dictionary<string, Vector3>();
            public readonly Dictionary<string, string> LastStates = new Dictionary<string, string>();
            public float WalkedMetres;
            public bool ArrivalLogged;
        }

        IEnumerator VerifyContinuousHotel()
        {
            Require(soloTour && !operationsUI && !agencyFixtures && !serviceFixtures && !presenceFixtures,
                "continuous SOLO verification uses its own production-mode branch");
            continuousModel = session.Simulation; continuousRooms = session.Rooms;
            continuousClock = continuousModel.Clock; continuousEpoch = LanSession.Instance ? LanSession.Instance.Epoch : 0;
            Require(continuousModel.ContinuousOperations && continuousModel.Running && legacyVerificationConfig == null,
                "production continuous calendar is active without a cloned legacy fixture");
            Require(continuousModel.Operations.SecondsPerDay == 720 && continuousModel.Operations.StartHour == 8 &&
                continuousModel.CalendarDay == 1 && continuousModel.Elapsed < 30, "fresh production D1 08 hotel");
            Require(continuousModel.Economy.Cash == session.Economy.StartingCash && session.Economy.StartingCash == 750,
                "unaltered production starting cash");
            continuousStartingCash = continuousMinimumCash = continuousModel.Economy.Cash;
            continuousLastTime = continuousModel.Elapsed;
            continuousRepair = new RepairSequence(continuousModel.Boiler, session.BoilerSettings, continuousModel.EmergencyPatchBoiler);
            continuousRepairController = FindAnyObjectByType<RepairSequenceController>();
            Require(continuousRepairController && continuousModel.Boiler.SoloAssistEnabled, "production SOLO repair catch and controller");
            continuousReady = true;
            facts.Add("CONTINUOUS POLICY: production seed/configuration/cash; first 4/5/5 ordinary dated offers; cold-sensitive guests assigned the lowest-heat-loss available rooms. Reference prices on the legal price grid; no edits after acceptance.");
            facts.Add("ONE STAFF ADAPTER: one exclusive job, declared 3.5m/s corridor/storage travel and action durations in hotel seconds at diagnostic8x. Real model rack-key pickup/giving, dirty-linen pickup/deposit, finite clean-stock pickup and bed progress. No physical staff carry claim. Every guest arrival, room crossing, outing, return and final exit is supplied by production GuestPresentation, never by this driver.");
            facts.Add("REPAIR ADAPTER: only on a naturally failed boiler; paid SOLO relief/catch and ordered panel/breaker/latch/latch/restart model sequence. Physical repair input controller is temporarily suspended for this explicitly separate model-input owner and restored even on failure. No pressure, temperature, condition or failure override.");
            facts.Add("Other policy: inspect each genuinely vacant room valve for 3 hotel seconds during turnover, return it to level1; no optional blanket/wake/late-checkout promise is fabricated. Natural private concerns, self-help, contact attempts, finite waits and schedule choices remain active. No human workload/performance/AltTab claim.");
            BookContinuousCohort(1, 4); BookContinuousCohort(2, 5);
            ManagementUI.Instance.Close();
            Position(coop.Players[0], new Vector3(1, .08f, -1.5f), new Vector3(-4, 1.5f, 2.5f));
            ManagementUI.Instance.Open(0);
            yield return Capture("continuous-opening", "Production continuous D1 / cash750 / future bookings / one original hotel");
            yield return ChooseOperationsUI("Today's bookings");
            yield return Capture("continuous-bookings", "Four accepted first-night dated bookings; next-night five also reserved");
            ManagementUI.Instance.Close();
            ParkContinuousStaff();
            for (int day = 1; day <= 3; day++)
            {
                if (day > 1)
                {
                    int date = day;
                    yield return Until(() => continuousModel.Elapsed >= continuousModel.Calendar.At(date, 10.05f), 135,
                        "normal clock reaches checkout/preparation on day " + date);
                    yield return Capture("continuous-day" + day + "-preparation", "Real prior-cohort departures and timed manual turnover; no overnight world reset");
                }
                var cohort = continuousCohorts.Single(c => c.Day == day);
                yield return Until(() => cohort.RoomArrivals.Count == cohort.Ids.Length, 135,
                    "all cohort " + day + " guests physically reach their assigned rooms");
                yield return Capture("continuous-day" + day + "-guests", "Cohort " + day + " / real reception and room routes completed / natural life remains active");
            }
            yield return Until(() => continuousModel.Elapsed >= continuousModel.Calendar.At(4, 8), 135,
                "one hotel reaches the full 72-hour observation boundary D4 08");
            continuousObservationEnd = continuousModel.Elapsed;
            Require(continuousModel.DayReports.Count == 3 && continuousCohorts.Count == 3, "three automatic reports without a day-three finale");
            ManagementUI.Instance.Open(0);
            yield return Capture("continuous-72-hours", "72 hours on one model, room registry and clock; day-three stays await next-morning checkout");
            yield return ChooseOperationsUI("Daily reports");
            yield return Capture("continuous-reports", "Three real reports; reporting periods and arrival cohorts are deliberately separate");
            ManagementUI.Instance.Close(); ParkContinuousStaff();
            yield return Until(() => continuousModel.Elapsed >= continuousModel.Calendar.At(4, 12) &&
                continuousCohorts.All(c => c.Departures.Count == c.Ids.Length), 70,
                "explicit next-morning checkout tail D4 12 and all fourteen actual departures");
            continuousTailEnd = continuousModel.Elapsed;
            ValidateContinuousOutcome();
            continuousSamplingFinished = true;
            RestoreContinuousRepairController();
            ManagementUI.Instance.Open(0);
            yield return ChooseOperationsUI("Daily reports");
            yield return Capture("continuous-final-accounts", "Fourteen unique paid stays: report receipt counts0/4/5 plus current-period5; conserved real cash");
            ManagementUI.Instance.Close();
            Position(coop.Players[0], new Vector3(-.52f, .1f, 33.9f), new Vector3(-.52f, 2.4f, 36.12f));
            yield return Capture("continuous-final-boiler", "Actual end-of-run boiler condition/capacity; faults and paid work are measured, never forced");
            continuousReady = false;
            // This is the sole reset and occurs only after preserving every original-world result.
            session.NewGame(); driveSpeed = 1;
            yield return null; yield return null;
            Require(!ReferenceEquals(session.Simulation, continuousModel) && !ReferenceEquals(session.Rooms, continuousRooms), "NewGame replaces the model and room registry only after the tour");
            Require(session.Simulation.ContinuousOperations && session.Simulation.CalendarDay == 1 && session.Simulation.Elapsed < 2 &&
                session.Simulation.Economy.Cash == continuousStartingCash && session.Reports.Count == 0 &&
                session.Simulation.Guests.Count == 0 && session.Simulation.Reservations.Count == 0 &&
                session.Rooms.All(r => !r.Occupied && r.DepartingGuestId == null && r.Cleanliness == Cleanliness.Clean) &&
                !session.Simulation.Boiler.CapacityUpgradePurchased && session.Simulation.PeriodCapitalSpend == 0 &&
                session.Simulation.PeriodMaintenanceSpend == 0 && !session.Simulation.Boiler.Failed,
                "fresh continuous SOLO has clean rooms, no old stays/receipts/upgrades/faults and starting cash");
            ValidateContinuousFreshReset();
            VerifySoloComposition();
            resetVerified = true; continuousVerified = true;
            Position(coop.Players[0], new Vector3(1, .08f, -1.5f), new Vector3(-4, 1.5f, 2.5f));
            ManagementUI.Instance.Open(0);
            yield return Capture("continuous-new-session", "Explicit NewGame after successful three-day trace / D1 / cash750 / six fresh rooms / one SOLO staff");
            Require(offscreenAvailable, "all continuous evidence images use the actual GPU and IMGUI overlay capture path");
            ManagementUI.Instance.Close();
            Debug.Log("VERIFY CONTINUOUS: completed72h+checkout-tail; receipts14 reports3 reset=True");
        }

        void ParkContinuousStaff() => Position(coop.Players[0], new Vector3(.55f, .08f, 5.4f), new Vector3(.55f, 1.7f, 21));

        void BookContinuousCohort(int day, int count)
        {
            var offers = continuousModel.BookingOffers.Where(o => o.ArrivalDay == day).OrderBy(o => o.ArrivalAt).Take(count).ToArray();
            Require(offers.Length == count, "ordinary dated offers available for cohort " + day);
            var free = continuousRooms.OrderBy(r => r.Profile.HeatLoss).ThenBy(r => r.Profile.Id).ToList();
            var assigned = new Dictionary<string, int>();
            foreach (var offer in offers.OrderBy(o => o.Application.Archetype.Kind == GuestKind.ColdSensitive ? 0 : 1).ThenBy(o => o.ArrivalAt))
            {
                var room = free.First(); free.Remove(room); assigned.Add(offer.Id, room.Profile.Id);
            }
            foreach (var offer in offers)
            {
                int price = session.Economy.MinPrice + (int)Math.Round((offer.Application.ReferencePrice - session.Economy.MinPrice) /
                    (double)session.Economy.PriceStep) * session.Economy.PriceStep;
                price = Math.Max(session.Economy.MinPrice, Math.Min(session.Economy.MaxPrice, price));
                var result = session.AcceptBooking(0, offer.Id, assigned[offer.Id], price);
                Require(result.Success, "ordinary dated booking " + offer.Id + ": " + result.Message);
                facts.Add("Booked cohort=" + day + " id=" + offer.Id + " room=" + assigned[offer.Id] + " price=" + price +
                    " arrival=" + offer.ArrivalAt.ToString("F1") + " checkout=" + offer.CheckoutAt.ToString("F1"));
            }
            continuousCohorts.Add(new ContinuousCohort { Day = day, Ids = offers.Select(o => o.Id).ToArray() });
            Debug.Log("VERIFY CONTINUOUS: booked cohort=" + day + " count=" + count);
        }

        void UpdateContinuousVerification()
        {
            if (!continuousReady || continuousSamplingFinished) return;
            Require(ReferenceEquals(continuousModel, session.Simulation) && ReferenceEquals(continuousRooms, session.Rooms) &&
                ReferenceEquals(continuousClock, session.Simulation.Clock) && (LanSession.Instance ? LanSession.Instance.Epoch : 0) == continuousEpoch,
                "original world/model/clock/epoch remain continuous");
            Require(continuousModel.Elapsed >= continuousLastTime && session.Phase == DayPhase.Service, "calendar never rewinds or enters a final-results phase");
            float dt = continuousModel.Elapsed - continuousLastTime; continuousLastTime = continuousModel.Elapsed;
            if (continuousModel.CalendarDay >= 2 && continuousCohorts.All(c => c.Day != 3)) BookContinuousCohort(3, 5);
            continuousMinimumCash = Math.Min(continuousMinimumCash, continuousModel.Economy.Cash);
            continuousPeakLoad = Math.Max(continuousPeakLoad, continuousModel.Boiler.Load);
            continuousPeakRatio = Math.Max(continuousPeakRatio, continuousModel.Boiler.LoadRatio);
            continuousMinimumCondition = Math.Min(continuousMinimumCondition, continuousModel.Boiler.Condition);
            if (continuousModel.Boiler.Failed && !continuousWasFailed) { continuousFailures++; Debug.Log("VERIFY CONTINUOUS: natural boiler fault at " + continuousModel.Elapsed); }
            continuousWasFailed = continuousModel.Boiler.Failed;
            foreach (var circuit in continuousModel.Electrical.Circuits)
            {
                if (circuit.Tripped && continuousTripped.Add(circuit.Id)) continuousTrips++;
                if (!circuit.Tripped) continuousTripped.Remove(circuit.Id);
            }
            if (continuousCalendarSeen != continuousModel.CalendarDay || continuousReportsSeen != continuousModel.ReportSequence)
            {
                continuousCalendarSeen = continuousModel.CalendarDay; continuousReportsSeen = continuousModel.ReportSequence;
                Debug.Log("VERIFY CONTINUOUS: D" + continuousCalendarSeen + " " + continuousModel.Calendar.DisplayTime +
                    " reports=" + continuousReportsSeen + " cash=" + continuousModel.Economy.Cash + " staff=" + (continuousJob ?? "idle"));
            }
            foreach (var cohort in continuousCohorts)
            foreach (string id in cohort.Ids)
            {
                var guest = continuousModel.Guests.FirstOrDefault(g => g.GuestId == id);
                if (guest == null) continue;
                cohort.Stays[id] = guest;
                if (guest.Agent.CheckedIn) cohort.CheckedIn.Add(id);
                if (guest.Agent.HasReachedRoom) cohort.RoomArrivals.Add(id);
                bool hasBody = guests.TryGetGuestTransform(id, out var body);
                if (guest.Agent.State == GuestAgentState.Left && guest.ReceiptPosted && !hasBody &&
                    continuousRooms.All(r => r.DepartingGuestId != id && r.GuestId != id)) cohort.Departures.Add(id);
                if (hasBody)
                {
                    if (cohort.Positions.TryGetValue(id, out var previous)) cohort.WalkedMetres += Vector3.Distance(previous, body.position);
                    cohort.Positions[id] = body.position;
                }
                else cohort.Positions.Remove(id);
                cohort.LastStates[id] = "id=" + id + " state=" + guest.Agent.State + " checkedIn=" + guest.Agent.CheckedIn +
                    " roomReached=" + guest.Agent.HasReachedRoom + " paid=" + guest.ReceiptPosted + " body=" + hasBody +
                    " room=" + guest.RoomId + " wait=" + guest.Agent.WaitingSeconds.ToString("F1");
                if (!cohort.ArrivalLogged && cohort.RoomArrivals.Count == cohort.Ids.Length)
                { cohort.ArrivalLogged = true; Debug.Log("VERIFY CONTINUOUS: actual room arrivals cohort=" + cohort.Day + " count=" + cohort.RoomArrivals.Count); }
            }
            TickContinuousStaff(dt);
        }
    }
}
#endif
