#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace WorstHotel
{
    /// <summary>Opt-in diagnostic player tour. Production navigation supplies every physical arrival.</summary>
    [DefaultExecutionOrder(-450)] // After device assignment/read, before gameplay and ledger input.
    public sealed partial class DevelopmentVerification : MonoBehaviour
    {
        const string VerificationPadLayout = "WorstHotelVerificationGamepad";
        string output;
        int errors, actualRoomMoves, cleaningArrivals, cleaningCompletions;
        bool initialized, finished, resetVerified, capturing, heaterDemonstration;
        bool verificationLayoutRegistered;
        bool soloTour;
        bool presenceFixtures, presenceVerified;
        bool agencyFixtures;
        int ActiveActors => soloTour ? 1 : 2;
        int Actor(int choice) => soloTour ? 0 : choice % 2;
        float began, driveSpeed = 8, heaterStartTemperature, heaterPeakTemperature;
        GameSession session;
        LocalCoopBootstrap coop;
        GuestPresentation guests;
        PortableHeater heater;
        ElectricalPanelPresentation electricalPanel;
        HotelSimulation observedSimulation;
        DaySample currentDay;
        readonly Gamepad[] verificationPads = new Gamepad[2];
        readonly List<DaySample> days = new List<DaySample>();
        readonly List<string> facts = new List<string>();
        SessionConfig legacyVerificationConfig;
        readonly HashSet<HousekeepingTask> reachedCleaning = new HashSet<HousekeepingTask>();
        readonly HashSet<HousekeepingTask> finishedCleaning = new HashSet<HousekeepingTask>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            var arguments = Environment.GetCommandLineArgs();
            int flag = Array.IndexOf(arguments, "-verifyHotel");
            if (flag < 0 || flag + 1 >= arguments.Length) return;
            var runner = new GameObject("Explicit living-hotel developer verification").AddComponent<DevelopmentVerification>();
            runner.output = Path.GetFullPath(arguments[flag + 1]);
            runner.soloTour = Array.IndexOf(arguments, "-hotelSolo") >= 0;
            runner.presenceFixtures = Array.IndexOf(arguments, "-verifyPresence") >= 0;
            runner.agencyFixtures = Array.IndexOf(arguments, "-verifyAgency") >= 0;
            runner.serviceFixtures = Array.IndexOf(arguments, "-verifyServices") >= 0;
            runner.operationsUI = Array.IndexOf(arguments, "-verifyOperationsUI") >= 0;
            runner.continuousTour = Array.IndexOf(arguments, "-verifyHotelContinuous") >= 0;
            Directory.CreateDirectory(runner.output);
            File.WriteAllText(Path.Combine(runner.output, "capture-manifest.txt"), string.Empty);
            DontDestroyOnLoad(runner.gameObject);
            Application.runInBackground = true; Application.targetFrameRate = 60;
            // Input System 1.17 disables ordinary synthetic Gamepads in a hidden player.
            // Only our private layout opts into background input; global focus policy and
            // physical-device layouts/settings remain untouched.
            InputSystem.RegisterLayout("{\"extend\":\"Gamepad\",\"runInBackground\":\"enabled\"}", VerificationPadLayout);
            runner.verificationLayoutRegistered = true;
            runner.verificationPads[0] = (Gamepad)InputSystem.AddDevice(VerificationPadLayout, "VerificationStaffA");
            if (!runner.soloTour) runner.verificationPads[1] = (Gamepad)InputSystem.AddDevice(VerificationPadLayout, "VerificationStaffB");
            SceneManager.sceneLoaded += runner.PrepareLegacyVerification;
            Application.logMessageReceived += runner.OnLog;
            runner.began = Time.realtimeSinceStartup;
        }

        // This opt-in historical driver verifies the original three-shift regression flow.
        // Configure before scene Start/transport startup; ordinary production sessions never use it.
        public static bool ShouldUseLegacyFixture(string[] arguments) => arguments != null &&
            Array.IndexOf(arguments, "-verifyLanSleep") < 0 &&
            Array.IndexOf(arguments, "-verifyLanContinuous") < 0 && Array.IndexOf(arguments, "-verifyOperationsUI") < 0 &&
            Array.IndexOf(arguments, "-verifyHotelContinuous") < 0;

        void PrepareLegacyVerification(Scene scene, LoadSceneMode mode)
        {
            var current = GameSession.Instance;
            if (!ShouldUseLegacyFixture(Environment.GetCommandLineArgs()) || legacyVerificationConfig || !current || current.gameObject.scene != scene) return;
            legacyVerificationConfig = Instantiate(current.config);
            legacyVerificationConfig.continuousOperations = false;
            current.config = legacyVerificationConfig;
            current.NewGame();
            facts.Add("LEGACY SHIFT FIXTURE: cloned configuration; this driver does not verify continuous 0.4 bookings.");
        }

        void OnLog(string message, string trace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            errors++;
            if (facts.Count < 150) facts.Add("ERROR: " + message);
        }

        // Flatten nested coroutines so failed assertions always write a report and exit.
        IEnumerator Start()
        {
            var stack = new Stack<IEnumerator>(); stack.Push(Tour());
            while (stack.Count > 0)
            {
                object wait = null;
                Exception failure = null;
                try
                {
                    var top = stack.Peek();
                    if (!top.MoveNext()) { stack.Pop(); continue; }
                    if (top.Current is IEnumerator child) { stack.Push(child); continue; }
                    wait = top.Current;
                }
                catch (Exception exception) { failure = exception; }
                if (failure != null)
                {
                    Debug.LogError("VERIFY: " + failure);
                    finished = true; WriteReport("FAIL"); Application.Quit(2); yield break;
                }
                yield return wait;
            }
            finished = true;
            WriteReport(errors == 0 ? "PASS" : "FAIL");
            Debug.Log("VERIFY: " + (operationsUI ? "operations UI verification complete" : "living three-day tour complete") + "; errors=" + errors);
            Application.Quit(errors == 0 ? 0 : 2);
        }

        void Update()
        {
            if (finished) return;
            float watchdog = 570 + (agencyFixtures ? 530 : 0) + (serviceFixtures ? 600 : 0);
            if (continuousTour) watchdog = 720;
            if (Time.realtimeSinceStartup - began > watchdog)
            {
                Debug.LogError("VERIFY: internal " + watchdog + "-second watchdog expired.");
                finished = true; WriteReport("TIMEOUT"); Application.Quit(2); return;
            }
            if (!initialized || session == null || coop == null) return;
            BindSyntheticStaff();
            if (coop.IsPaused) return;
            if (session.Phase == DayPhase.Planning || session.Phase == DayPhase.Service)
                session.Simulation.Clock.SetSpeed(capturing ? 1 : driveSpeed);
            if (continuousTour)
            {
                try { UpdateContinuousVerification(); }
                catch (Exception exception)
                {
                    Debug.LogError("VERIFY continuous: " + exception);
                    finished = true; WriteReport("FAIL"); Application.Quit(2);
                }
                return;
            }
            MaintainAgencyActivities();
            if (session.Phase != DayPhase.Service || currentDay == null) return;
            var simulation = session.Simulation;
            foreach (var guest in simulation.Guests)
            {
                var agent = guest.Agent;
                if (agent.State == GuestAgentState.WaitingForCheckIn &&
                    agent.WaitingSeconds >= simulation.LivingSettings.KeyRetrievalEstimateSeconds)
                {
                    var room = session.Rooms.First(r => r.Profile.Id == guest.RoomId);
                    if (room.Cleanliness == Cleanliness.Clean && room.DepartingGuestId == null &&
                        room.TurnoverState != HousekeepingState.Moving && room.TurnoverState != HousekeepingState.Cleaning)
                    {
                        // Explicit model adapter for this lifecycle tour. Real rack pickup and
                        // one-press giving are independently exercised through PlayMode input.
                        int playerId = Actor(guest.RoomId);
                        Require(simulation.Keys.PickUp(playerId, guest.RoomId).Success, "diagnostic model rack-key pickup");
                        var result = simulation.CheckIn(playerId, guest.GuestId);
                        Require(result.Success, "diagnostic model key handoff: " + result.Message);
                    }
                }
                ObserveGuest(guest);
            }
            currentDay.NaturalBoilerFailure |= simulation.Boiler.Failed;
            if (simulation.Services != null)
            {
                currentDay.ChargedContacts = simulation.Services.Cases.Count(c => c.BudgetCharged);
                currentDay.PrivateCases = simulation.Services.Cases.Count(c => !c.IsKnownToHotel);
                currentDay.SelfHelpActions = simulation.Services.Responses.Count(r => r.SelfResponseApplied);
                currentDay.ContactAttempts = simulation.Services.Responses.Sum(r => r.ContactAttempts);
                currentDay.HeardConcerns = simulation.Services.Responses.Count(r => r.CommunicatedAt >= 0);
                Require(currentDay.ChargedContacts <= simulation.Services.Settings.MaxCasesPerShift,
                    "production natural tour respects its optional service contact budget");
            }
            if (heaterDemonstration && heater.State != null && heater.State.EffectiveHeatOutput > 0)
                heaterPeakTemperature = Mathf.Max(heaterPeakTemperature, session.Rooms.First(r => r.Profile.Id == 106).Temperature);
        }

        void LateUpdate()
        {
            if (finished || !initialized || session == null || coop == null || coop.IsPaused) return;
            // The early Update binds our exact synthetic devices before gameplay input. GameSession
            // can reset diagnostic speed on an event during its later Update. Reapply after that,
            // before the default-order guest LateUpdates consume their travel budgets.
            if (session.Phase == DayPhase.Planning || session.Phase == DayPhase.Service)
                session.Simulation.Clock.SetSpeed(capturing ? 1 : driveSpeed);
        }

        IEnumerator Tour()
        {
            for (int i = 0; i < 40; i++) yield return null;
            session = GameSession.Instance; coop = LocalCoopBootstrap.Instance;
            Require(session != null && coop != null && coop.Players.Count(p => p != null) == ActiveActors,
                "composition root and expected actual staff count");
            if (soloTour) VerifySoloComposition();
            BindSyntheticStaff();
            guests = FindAnyObjectByType<GuestPresentation>();
            heater = FindAnyObjectByType<PortableHeater>();
            electricalPanel = FindAnyObjectByType<ElectricalPanelPresentation>();
            Require(guests != null && heater != null && electricalPanel != null && FindAnyObjectByType<LinenStorage>(), "living physical components and linen source");
            Require(FindAnyObjectByType<HousekeeperPresentation>() == null && !session.Simulation.Housekeeping.WorkerAvailable,
                "the hotel starts without an automatic employee");
            coop.SendMessage("OnApplicationFocus", true); coop.SetPaused(false);
            // Explicitly NOT human WAIT. Normal GameSession.Update still advances fixed hotel ticks.
            session.Wait.Stop("Developer lifecycle driver; not human WAIT"); session.Wait.enabled = false;
            observedSimulation = session.Simulation;
            observedSimulation.Housekeeping.Changed += ObserveCleaning;
            initialized = true;
            if (continuousTour) { yield return VerifyContinuousHotel(); yield break; }
            if (operationsUI) { yield return VerifyOperationsUI(); yield break; }
            facts.Add("The three-day lifecycle uses no forced failure, temperature override, activity override or synthetic route-completion callback. " +
                "Optional presence/agency fixtures use separate preliminary sessions and are explicitly reset before day1.");
            facts.Add("Diagnostic commands: clock8x, model rack-key pickup and giving after retrieval-time estimate, planning/move/switch/reset, physical heater repositioning for the electrical case.");
            facts.Add("This three-day lifecycle driver explicitly issues model dirty-pickup/deposit/clean-pickup/bed commands after real guest departure. It does not prove physical linen carrying; the input-driven PlayMode route supplies that evidence.");
            facts.Add("Exact synthetic staff device IDs=" + string.Join(",", verificationPads.Where(p => p != null).Select(p => p.deviceId)) +
                "; private background-capable layout=" + VerificationPadLayout + ". Physical devices are not enabled, disabled, removed or sent synthetic events by this driver.");
            if (presenceFixtures) yield return VerifyGuestPresence();
            if (agencyFixtures) yield return VerifyGuestAgency();
            if (serviceFixtures) yield return VerifyGuestServices();
            ManagementUI.Instance.Close();
            Position(coop.Players[0], new Vector3(-1.6f, .08f, 1.6f), new Vector3(-1.6f, 1.85f, 3.35f));
            Position(coop.Players[1], new Vector3(-.6f, .08f, 11.62f), new Vector3(-1.75f, 1.1f, 11.62f));
            yield return Capture("physical-sources", "Six numbered physical rack keys / fixed door knocker outside Room101");
            Position(coop.Players[0], new Vector3(1, .1f, -1.5f), new Vector3(-4, 1.5f, 2.5f));
            Position(coop.Players[1], new Vector3(0, .1f, 7), new Vector3(.1f, 1.7f, 23));
            AssignPlan(4);
            if (!ManagementUI.Instance.IsOpen) ManagementUI.Instance.Open(0);
            yield return Capture("planning", "Original day-one ledger / four production offers");
            yield return CaptureDeveloperPanel("developer-panel");

            for (int day = 1; day <= 3; day++)
            {
                if (day > 1)
                {
                    yield return PrepareNextDay(day);
                    AssignPlan(6);
                    yield return Capture("planning-day" + day, "Prepared rooms / production day " + day + " bookings");
                }
                int count = day == 1 ? 4 : 6;
                currentDay = new DaySample { Day = day, Bookings = count }; days.Add(currentDay);
                session.CommitPlan(Actor(day));
                Require(session.Phase == DayPhase.Service, "day " + day + " service opens: " + session.LastMessage);
                currentDay.Stays = session.Simulation.Guests.ToArray();
                yield return Until(() => currentDay.Stays.All(g => g.Agent.HasReachedRoom), 105,
                    "day " + day + " all " + count + " guests physically reach their rooms");
                // Presentation raises its callback in LateUpdate, one frame before this driver's next sample.
                foreach (var guest in currentDay.Stays) ObserveGuest(guest);
                Require(currentDay.ReachedRoom.Count == count, "every guest completed a real room route");

                if (day == 1)
                {
                    yield return Capture(soloTour ? "solo-service" : "split-screen", "Real staggered arrivals and physical room routes completed");
                    var moving = currentDay.Stays.Single(g => g.RoomId == 102);
                    yield return Until(() => moving.Agent.InAssignedRoom, 25, "relocation guest is physically available after any natural outing");
                    Require(session.Simulation.Keys.PickUp(0, 105).Success, "diagnostic model relocation-key pickup");
                    var result = session.Simulation.MoveGuest(0, moving.GuestId, 105);
                    Require(result.Success, "diagnostic public relocation to room105: " + result.Message);
                    yield return Until(() => moving.Agent.InAssignedRoom && moving.RoomId == 105 && !moving.Agent.IsRelocating,
                        25, "guest physically exits room102 and enters room105");
                    actualRoomMoves++;
                    facts.Add("Day1 relocation102->105: real origin door, departure token, destination door and arrival callback.");
                    yield return CaptureGuestDetail();
                }
                else if (day == 2) yield return DemonstrateHeaterAndCircuit();

                yield return Until(() => session.Phase == DayPhase.Settlement, 105, "day " + day + " natural service completion");
                Require(session.Reports.Count == day, "one report per completed day");
                Require(currentDay.Stays.All(g => g.Agent.HasReachedRoom && g.Elapsed > 10), "meaningful room time for every stay");
                Require(session.Report.Receipts.Count == count && session.Report.Receipts.All(r => r.Price > 0 && r.Net > 0),
                    "paid served stays " + count + " on day " + day);
                yield return Capture("settlement-day" + day, "Actual paid stays and consequences / no room-time shortcut");
                session.ContinueAfterSettlement(Actor(day));
                if (day < 3)
                {
                    if (day == 1) yield return Capture("maintenance", "Production maintenance choice between real guest days");
                    var choice = day == 1 && session.Cash >= session.Economy.CheapPatchCost ? MaintenanceChoice.CheapPatch : MaintenanceChoice.Defer;
                    session.ChooseMaintenance(Actor(day), choice);
                    Require(session.Phase == DayPhase.Planning && session.Day == day + 1, "maintenance advances the calendar");
                    facts.Add("After day" + day + " maintenance=" + choice + ".");
                }
            }

            Require(session.Phase == DayPhase.Results && session.Reports.Count == 3, "three meaningful day reports/results");
            yield return Capture("results", "Actual three-day living-hotel results");
            Require(actualRoomMoves > 0 && cleaningArrivals > 0 && cleaningCompletions > 0, "physical relocation and explicit model turnover occurred");
            var completed = session.Simulation;
            completedReports = session.Reports.ToArray(); finalCash = session.Cash; finalReputation = completed.Economy.Reputation;
            var oldHeater = heater.State;
            session.RestartSession(0);
            for (int i = 0; i < 12; i++) yield return null;
            Require(!ReferenceEquals(completed, session.Simulation) && session.Day == 1 && session.Reports.Count == 0 && session.Phase == DayPhase.Planning,
                "new session replaces simulation and clears calendar/reports");
            Require(session.Simulation.Guests.Count == 0 && guests.VisibleGuestCount == 0, "new session discards old guest bodies");
            Require(session.Rooms.All(r => !r.Occupied && !r.Reserved && r.DepartingGuestId == null), "new session clears ownership and departure locks");
            Require(session.Simulation.Housekeeping.Tasks.Count == 0, "new session restores the initial clean-room queue");
            Require(session.Simulation.Electrical.Circuits.All(c => !c.Tripped && c.TripCount == 0), "new session clears electrical history");
            Require(session.Simulation.Heaters.Items.Count == 2 && heater.State != null && !ReferenceEquals(oldHeater, heater.State) &&
                session.Simulation.Heaters.Items.All(h => !h.SwitchedOn && h.EffectiveHeatOutput == 0), "fresh heater registry contains both physical tools, switched off");
            Require(session.Simulation.Services.Cases.Count == 0 && session.Simulation.Services.Promises.Count == 0 &&
                session.Simulation.Services.BlanketsAvailable == session.config.services.blanketStock &&
                session.Simulation.Services.BulbsAvailable == session.config.services.bulbStock &&
                session.Simulation.Services.Items.All(i => i.Location == ServiceItemLocation.OnShelf && !i.PlayerId.HasValue),
                "new session restores service stock and clears requests, promises and item ownership");
            resetVerified = true;
            if (soloTour) VerifySoloComposition();
            yield return Capture("new-session", "Verified clean reset / two physical heaters, fresh service stock and no old promises");
        }

        IEnumerator PrepareNextDay(int day)
        {
            Require(session.Phase == DayPhase.Planning, "preparation phase for day " + day);
            if (!ManagementUI.Instance.IsOpen) ManagementUI.Instance.Open(0);
            ManagementUI.Instance.SendMessage("OpenHousekeeping");
            yield return Until(() => session.Rooms.All(r => r.DepartingGuestId == null), 30, "prior guests physically vacate before linen work");
            yield return Capture("housekeeping-day" + day, "Dirty-room ledger / owners perform linen turnover");
            ManagementUI.Instance.Close();
            if (day == 2)
            {
                Position(coop.Players[0], new Vector3(-3, .08f, 28.8f), new Vector3(-3, 1.1f, 30.7f));
                Position(coop.Players[1], new Vector3(-4.65f, .08f, 10), new Vector3(-5.95f, 1.01f, 10));
                yield return Capture("linen-source", "Finite clean linen on the utility shelf / dirty linen on a vacated bed");
            }
            foreach (var task in session.Simulation.Housekeeping.Tasks.ToArray())
            {
                var simulation = session.Simulation;
                Require(simulation.PickUpLinen(0, task.DirtyLinenId).Success, "model adapter removes dirty linen");
                Require(simulation.DepositDirtyLinen(0, task.DirtyLinenId).Success, "model adapter delivers dirty linen");
                var clean = simulation.Housekeeping.Linens.First(item => item.Kind == LinenKind.Clean && item.Location == LinenLocation.OnShelf);
                Require(simulation.PickUpLinen(0, clean.Id).Success, "model adapter takes finite clean stock");
                Require(simulation.BeginMakeBed(0, task.RoomId, clean.Id).Success, "model adapter starts short bed action");
                while (simulation.Housekeeping.Find(task.RoomId) == task)
                {
                    Require(simulation.AdvanceMakeBed(0, task.RoomId, Time.deltaTime).Success, "model adapter advances bed action by real frame time");
                    yield return null;
                }
            }
            Require(session.Rooms.All(r => r.Cleanliness == Cleanliness.Clean && r.DepartingGuestId == null) &&
                !session.Simulation.Housekeeping.HasPendingWork, "explicit linen commands prepare rooms without automatic cleaning");
            ManagementUI.Instance.Close(); ManagementUI.Instance.Open(0);
        }

        void ObserveCleaning(HousekeepingTask task, string reason)
        {
            if (task.State == HousekeepingState.Cleaning && reachedCleaning.Add(task))
            {
                Require(session.Rooms.Single(room => room.Profile.Id == task.RoomId).DepartingGuestId == null,
                    "bed-making starts only after the real departing body clears the room");
                cleaningArrivals++;
            }
            if (task.State == HousekeepingState.None && task.Progress01 >= 1 && finishedCleaning.Add(task)) cleaningCompletions++;
        }

        void AssignPlan(int count)
        {
            var available = session.Plan.Applications.ToList();
            var kinds = new[] { GuestKind.Budget, GuestKind.ColdSensitive, GuestKind.Business, GuestKind.Budget, GuestKind.Business, GuestKind.ColdSensitive };
            Require(available.Count >= count, "production offers for the requested day");
            for (int i = 0; i < count; i++)
            {
                var application = available.FirstOrDefault(a => a.Archetype.Kind == kinds[i]) ?? available[0]; available.Remove(application);
                int price = session.Economy.MinPrice + Mathf.RoundToInt((application.ReferencePrice - session.Economy.MinPrice) /
                    (float)session.Economy.PriceStep) * session.Economy.PriceStep;
                session.Assign(Actor(i), application.Id, 101 + i, Mathf.Clamp(price, session.Economy.MinPrice, session.Economy.MaxPrice));
            }
            Require(session.Plan.Assignments.Count == count, "all diagnostic planning commands accepted");
        }

        IEnumerator Until(Func<bool> condition, float realSeconds, string operation)
        {
            float deadline = Time.realtimeSinceStartup + realSeconds;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Require(condition(), operation + " (timeout; day " + session.Day + ", " + session.Phase + ", clock " + session.Simulation.Elapsed.ToString("F1") + ")");
        }

        static void Require(bool success, string operation)
        { if (!success) throw new InvalidOperationException("VERIFY failed: " + operation); }

        void BindSyntheticStaff()
        {
            Require(coop != null && coop.Players.Count(p => p != null) == ActiveActors, "expected verification input readers");
            for (int actor = 0; actor < ActiveActors; actor++)
            {
                var pad = verificationPads[actor];
                Require(pad != null && pad.added && pad.enabled && pad.canRunInBackground,
                    "created background-capable synthetic staff device remains available");
                Require(coop.Players[actor] != null, "verification staff rig exists");
                var input = coop.Players[actor].Input;
                if (!ReferenceEquals(input.Gamepad, pad))
                {
                    // A real-device hotplug can make the normal bootstrap reassign its readers.
                    // Correct only these readers; never disable, remove or queue input to real devices.
                    input.Bind(pad, null, null);
                    input.Read();
                }
                Require(ReferenceEquals(input.Gamepad, pad) && input.Gamepad.deviceId == pad.deviceId,
                    "staff " + (actor + 1) + " reads its exact created synthetic pad");
            }
        }

        void VerifySoloComposition()
        {
            Require(coop.IsSolo && coop.Players[0] && coop.Players[1] == null, "SOLO has only actor0, no inactive partner");
            Require(FindObjectsByType<FirstPersonController>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 1,
                "no hidden second staff object in SOLO");
            Require(FindObjectsByType<Camera>(FindObjectsSortMode.None).Count(c => c.enabled && c.gameObject.activeInHierarchy) == 1 &&
                coop.Players[0].PlayerCamera.rect == new Rect(0, 0, 1, 1), "one full-screen SOLO camera");
            Require(FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Count(a => a.enabled) == 1, "one SOLO listener");
            facts.Add("SoloComposition=True StaffObjects=1 ActiveCameras=1 Listeners=1 SyntheticPads=1 Day=" + session.Day);
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= PrepareLegacyVerification;
            if (legacyVerificationConfig) Destroy(legacyVerificationConfig);
            Application.logMessageReceived -= OnLog;
            if (observedSimulation?.Housekeeping != null) observedSimulation.Housekeeping.Changed -= ObserveCleaning;
            foreach (var pad in verificationPads)
                if (pad != null && pad.added) InputSystem.RemoveDevice(pad);
            if (verificationLayoutRegistered) InputSystem.RemoveLayout(VerificationPadLayout);
        }
    }
}
#endif
