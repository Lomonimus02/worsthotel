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
        bool continuousSalesExpanded, continuousSalesClosed;
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
            public string[] Ids = Array.Empty<string>();
            public bool SalesComplete;
            public readonly Dictionary<string, int> AgreedPrices = new Dictionary<string, int>();
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
            Require(continuousModel.ContinuousOperations && continuousModel.AutomaticBookingsEnabled && continuousModel.Running && legacyVerificationConfig == null,
                "production continuous calendar and automatic sales are active without a cloned legacy fixture");
            Require(continuousModel.Operations.SecondsPerDay == 720 && continuousModel.Operations.StartHour == 8 &&
                continuousModel.CalendarDay == 1 && continuousModel.Elapsed < 30, "fresh production D1 08 hotel");
            Require(continuousModel.Economy.Cash == session.Economy.StartingCash && session.Economy.StartingCash == 750,
                "unaltered production starting cash");
            continuousStartingCash = continuousMinimumCash = continuousModel.Economy.Cash;
            continuousLastTime = continuousModel.Elapsed;
            continuousRepair = new RepairSequence(continuousModel.Boiler, session.BoilerSettings, continuousModel.EmergencyPatchBoiler);
            continuousRepairController = FindAnyObjectByType<RepairSequenceController>();
            Require(continuousRepairController && continuousModel.Boiler.SoloAssistEnabled, "production SOLO repair catch and controller");
            for (int day = 1; day <= 3; day++) continuousCohorts.Add(new ContinuousCohort { Day = day });
            SetContinuousSalesRooms(4);
            continuousReady = true;
            facts.Add("CONTINUOUS POLICY: production demand seed/configuration/cash; four rooms offered at the production initial rate, then five after D1 13:00 for later scheduled enquiries. Close all new sales on D3 after its already-booked cohort, preventing a fourth cohort during the checkout tail. Actual automatic reservations determine cohort identities/counts; no AcceptBooking calls, invented fill counts or contract repricing. A labelled model staff assignment adapter moves each new cold-sensitive booking toward a warmer available open room and tolerant guests toward heat-loss rooms before arrival.");
            facts.Add("ONE STAFF ADAPTER: one exclusive job, declared 3.5m/s corridor/storage travel and action durations in hotel seconds at diagnostic8x. Real model rack-key pickup/giving, dirty-linen pickup/deposit, finite clean-stock pickup and bed progress. No physical staff carry claim. Every guest arrival, room crossing, outing, return and final exit is supplied by production GuestPresentation, never by this driver.");
            facts.Add("REPAIR ADAPTER: only on a naturally failed boiler; paid SOLO relief/catch and ordered panel/breaker/latch/latch/restart model sequence. Physical repair input controller is temporarily suspended for this explicitly separate model-input owner and restored even on failure. No pressure, temperature, condition or failure override.");
            facts.Add("Other policy: inspect each genuinely vacant room valve for 3 hotel seconds during turnover, return it to level1; no optional blanket/wake/late-checkout promise is fabricated. Natural private concerns, self-help, contact attempts, finite waits and schedule choices remain active. No human workload/performance/AltTab claim.");
            ManagementUI.Instance.Close();
            Position(coop.Players[0], new Vector3(1, .08f, -1.5f), new Vector3(-4, 1.5f, 2.5f));
            ManagementUI.Instance.Open(0);
            yield return Capture("continuous-opening", "Production continuous D1 / cash750 / future bookings / one original hotel");
            ManagementUI.Instance.Close(); ParkContinuousStaff();
            yield return Until(() => continuousCohorts[0].SalesComplete, 35, "first day's actual scheduled demand decisions complete");
            ManagementUI.Instance.Open(0);
            yield return ChooseOperationsUI("Today's bookings");
            yield return Capture("continuous-bookings", "Actual first-night automatic reservations; later nights remain scheduled demand");
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
                yield return Until(() => cohort.SalesComplete && cohort.Ids.Length > 0 && cohort.RoomArrivals.Count == cohort.Ids.Length, 135,
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
                "explicit next-morning checkout tail D4 12 and every actually sold guest departure");
            continuousTailEnd = continuousModel.Elapsed;
            ValidateContinuousOutcome();
            continuousSamplingFinished = true;
            RestoreContinuousRepairController();
            ManagementUI.Instance.Open(0);
            yield return ChooseOperationsUI("Daily reports");
            yield return Capture("continuous-final-accounts", "Every automatically sold stay paid once, in its actual departure period; conserved real cash");
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
            Debug.Log("VERIFY CONTINUOUS: completed72h+checkout-tail; automaticReceipts=" + continuousReceipts.Length + " reports3 reset=True");
        }

        void ParkContinuousStaff() => Position(coop.Players[0], new Vector3(.55f, .08f, 5.4f), new Vector3(.55f, 1.7f, 21));

        void SetContinuousSalesRooms(int count)
        {
            int before = continuousModel.Reservations.Count, index = 0;
            foreach (var policy in continuousModel.RoomSalesPolicies.OrderBy(row => row.RoomId))
            {
                var result = continuousModel.SetRoomSalesPolicy(0, policy.RoomId, index++ < count,
                    continuousModel.Operations.Sales.InitialPrice, policy.Revision);
                Require(result.Success, "model staff sales-policy adapter: " + result.Message);
            }
            Require(continuousModel.Reservations.Count == before, "changing room sales never creates an immediate reservation");
            facts.Add("Sales policy at elapsed=" + continuousModel.Elapsed.ToString("F2") + ": open=" + count +
                "; rate=" + continuousModel.Operations.Sales.InitialPrice + "; existing contracts preserved.");
        }

        void ObserveAutomaticContinuousCohorts()
        {
            if (!continuousSalesExpanded && continuousModel.Elapsed >= continuousModel.Calendar.At(1, 13))
            { SetContinuousSalesRooms(5); continuousSalesExpanded = true; }
            if (!continuousSalesClosed && continuousModel.CalendarDay >= 3)
            { SetContinuousSalesRooms(0); continuousSalesClosed = true; }
            foreach (var cohort in continuousCohorts)
            {
                foreach (var reservation in continuousModel.Reservations.Where(row => row.Offer.ArrivalDay == cohort.Day &&
                    !cohort.AgreedPrices.ContainsKey(row.Id)).ToArray())
                {
                    Require(reservation.IsAutomatic && reservation.Status == ReservationStatus.Reserved,
                        "ordinary reservations originate from timed automatic demand before arrival");
                    int agreed = reservation.Price;
                    var available = continuousRooms.Where(room => continuousModel.RoomSalesPolicies.Any(policy =>
                        policy.RoomId == room.Profile.Id && policy.OpenForSale) && continuousModel.CanReserveInterval(room.Profile.Id,
                        reservation.Offer.ArrivalAt, reservation.Offer.CheckoutAt, reservation.Id).Success);
                    bool cold = reservation.Offer.Application.Archetype.Kind == GuestKind.ColdSensitive;
                    var target = available.OrderBy(room => cold ? room.Profile.HeatLoss : -room.Profile.HeatLoss)
                        .ThenBy(room => room.Profile.Id).First();
                    if (target.Profile.Id != reservation.RoomId)
                        Require(continuousModel.ReassignBooking(0, reservation.Id, target.Profile.Id, reservation.Revision).Success,
                            "staff assignment adapter uses the normal future-contract revision and interval gate");
                    Require(reservation.Price == agreed, "room reassignment preserves the automatically agreed rate");
                    cohort.AgreedPrices.Add(reservation.Id, agreed);
                    cohort.Ids = cohort.AgreedPrices.Keys.ToArray();
                    facts.Add("Automatic cohort=" + cohort.Day + " id=" + reservation.Id + " room=" + reservation.RoomId +
                        " agreedPrice=" + agreed + " arrival=" + reservation.Offer.ArrivalAt.ToString("F1") +
                        " observedAt=" + continuousModel.Elapsed.ToString("F2"));
                }
                if (!cohort.SalesComplete && continuousModel.Elapsed >= continuousModel.SalesDecisionAt(cohort.Day, SalesSettings.DecisionsPerDay - 1))
                {
                    cohort.SalesComplete = true;
                    Require(cohort.Ids.Length > 0 && cohort.Ids.Length <= (cohort.Day == 1 ? 4 : 5), "actual sold cohort stays within offered room capacity");
                    Debug.Log("VERIFY CONTINUOUS: scheduled sales complete cohort=" + cohort.Day + " actualCount=" + cohort.Ids.Length);
                }
            }
        }

        void UpdateContinuousVerification()
        {
            if (!continuousReady || continuousSamplingFinished) return;
            Require(ReferenceEquals(continuousModel, session.Simulation) && ReferenceEquals(continuousRooms, session.Rooms) &&
                ReferenceEquals(continuousClock, session.Simulation.Clock) && (LanSession.Instance ? LanSession.Instance.Epoch : 0) == continuousEpoch,
                "original world/model/clock/epoch remain continuous");
            Require(continuousModel.Elapsed >= continuousLastTime && session.Phase == DayPhase.Service, "calendar never rewinds or enters a final-results phase");
            float dt = continuousModel.Elapsed - continuousLastTime; continuousLastTime = continuousModel.Elapsed;
            ObserveAutomaticContinuousCohorts();
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
                if (!cohort.ArrivalLogged && cohort.SalesComplete && cohort.RoomArrivals.Count == cohort.Ids.Length)
                { cohort.ArrivalLogged = true; Debug.Log("VERIFY CONTINUOUS: actual room arrivals cohort=" + cohort.Day + " count=" + cohort.RoomArrivals.Count); }
            }
            TickContinuousStaff(dt);
        }
    }
}
#endif
