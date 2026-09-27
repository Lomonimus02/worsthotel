#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem.LowLevel;

namespace WorstHotel
{
    /// <summary>Opt-in two-process diagnostic. Stage files coordinate assertions, never gameplay state.</summary>
    [DefaultExecutionOrder(-550)]
    public sealed partial class LanDevelopmentVerification : MonoBehaviour
    {
        const string PadLayout = "WorstHotelLanVerificationGamepad";
        string output, side;
        bool host, finished, layoutRegistered, clockTracking, capture, agency, services, continuous;
        int errors, checks, stableClockChecks;
        float began, serviceClockStart, lastClientClock;
        long lastClientSequence;
        Gamepad pad;
        LanSession lan;
        LocalCoopBootstrap coop;
        GameSession session;
        RoomKeyItem key;
        readonly List<string> facts = new List<string>();
        SessionConfig legacyVerificationConfig;
        SessionConfig continuousFundingConfig;
        EconomyConfig continuousFundingEconomy;

        [Serializable] sealed class PoseMeasurement
        {
            public Vector3 staff, key;
            public long epoch;
            public string note = "Host measurement for assertions only. Never applied as game state.";
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            var args = Environment.GetCommandLineArgs();
            int flag = Array.IndexOf(args, "-verifyHotelLAN");
            if (flag < 0 || flag + 1 >= args.Length) return;
            bool isHost = Array.IndexOf(args, "-hotelHost") >= 0;
            bool isClient = Array.IndexOf(args, "-hotelJoin") >= 0;
            if (isHost == isClient) return;
            var runner = new GameObject("Opt-in actual LAN player verification").AddComponent<LanDevelopmentVerification>();
            runner.output = Path.GetFullPath(args[flag + 1]); runner.host = isHost;
            runner.capture = Array.IndexOf(args, "-verifyLanCapture") >= 0;
            runner.agency = Array.IndexOf(args, "-verifyLanAgency") >= 0;
            runner.services = Array.IndexOf(args, "-verifyLanServices") >= 0;
            runner.continuous = Array.IndexOf(args, "-verifyLanContinuous") >= 0;
            runner.side = isHost ? "host" : "client"; runner.began = Time.realtimeSinceStartup;
            Directory.CreateDirectory(runner.output);
            DontDestroyOnLoad(runner.gameObject);
            Application.runInBackground = true; Application.targetFrameRate = 60;
            if (isClient)
            {
                InputSystem.RegisterLayout("{\"extend\":\"Gamepad\",\"runInBackground\":\"enabled\"}", PadLayout);
                runner.layoutRegistered = true;
                runner.pad = (Gamepad)InputSystem.AddDevice(PadLayout, "LANVerificationOwnedClientPad");
            }
            SceneManager.sceneLoaded += runner.PrepareLegacyVerification;
            Application.logMessageReceived += runner.OnLog;
        }

        // Configure opt-in fixtures before scene Start/transport startup. Continuous keeps
        // production rules with labelled capital-test funds; old flags retain historical shifts.
        void PrepareLegacyVerification(Scene scene, LoadSceneMode mode)
        {
            var current = GameSession.Instance;
            if (continuous && current && current.gameObject.scene == scene)
            {
                if (continuousFundingConfig) return;
                continuousFundingConfig = Instantiate(current.config);
                continuousFundingEconomy = Instantiate(current.config.economy);
                continuousFundingEconomy.startingCash = ContinuousCapitalFixtureCash;
                continuousFundingConfig.economy = continuousFundingEconomy;
                current.config = continuousFundingConfig; current.NewGame();
                facts.Add("CONTINUOUS CAPITAL FIXTURE: cloned economy starting cash=" + ContinuousCapitalFixtureCash +
                    "; production continuous calendar, service, capacity, wear and prices remain unchanged. This is funded command/accounting verification, not an affordability or natural-profit claim.");
                return;
            }
            if (!DevelopmentVerification.ShouldUseLegacyFixture(Environment.GetCommandLineArgs()) || legacyVerificationConfig || !current || current.gameObject.scene != scene) return;
            legacyVerificationConfig = Instantiate(current.config);
            legacyVerificationConfig.continuousOperations = false;
            current.config = legacyVerificationConfig;
            current.NewGame();
            facts.Add("LEGACY SHIFT FIXTURE: cloned configuration; this driver does not verify continuous 0.4 bookings.");
        }

        void OnLog(string message, string trace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Assert && type != LogType.Exception) return;
            errors++;
            if (facts.Count < 100) facts.Add("ERROR: " + message);
        }

        void Update()
        {
            if (finished) return;
            float watchdog = continuous ? 420 : services ? 330 : 140;
            if (Time.realtimeSinceStartup - began > watchdog) { Fail(watchdog + "-second internal watchdog"); return; }
            if (agency && host) MaintainAgencyFixture();
            if (services && host) MaintainServicesFixture();
            if (host || !coop || pad == null || coop.LanRole != LanRole.Client || !coop.Players[1]) return;
            // This hidden-process fixture owns only this virtual device. It does not enable,
            // disable, remove or queue input to any physical device on the user's computer.
            coop.SendMessage("OnApplicationFocus", true, SendMessageOptions.DontRequireReceiver);
            var input = coop.Players[1].Input;
            if (!ReferenceEquals(input.Gamepad, pad)) input.Bind(pad, null, null);
        }

        void LateUpdate()
        {
            if (finished || host || !clockTracking || !lan || !session || !lan.HasSnapshot) return;
            float clock = session.Simulation.Clock.SimulationTime;
            long sequence = lan.AppliedModelSequence;
            if (sequence == lastClientSequence)
            {
                if (Mathf.Abs(clock - lastClientClock) > .00001f) { Fail("Client clock advanced without a new host snapshot"); return; }
                stableClockChecks++;
            }
            lastClientSequence = sequence; lastClientClock = clock;
        }

        IEnumerator Start()
        {
            var stack = new Stack<IEnumerator>(); stack.Push(Run());
            while (stack.Count > 0 && !finished)
            {
                object wait = null;
                Exception failure = null;
                try
                {
                    var current = stack.Peek();
                    if (!current.MoveNext()) { stack.Pop(); continue; }
                    if (current.Current is IEnumerator child) { stack.Push(child); continue; }
                    wait = current.Current;
                }
                catch (Exception exception) { failure = exception; }
                if (failure != null) { Fail(failure.ToString()); yield break; }
                yield return wait;
            }
            if (finished) yield break;
            finished = true;
            WriteReport(errors == 0 ? "PASS" : "FAIL");
            Application.Quit(errors == 0 ? 0 : 2);
        }

        IEnumerator Run()
        {
            yield return Until(() => LanSession.Instance && GameSession.Instance && LocalCoopBootstrap.Instance &&
                LocalCoopBootstrap.Instance.Players[1], 30, "normal LAN scene ready");
            lan = LanSession.Instance; session = GameSession.Instance; coop = LocalCoopBootstrap.Instance;
            yield return Until(() => lan.Role == (host ? LanRole.Host : LanRole.Client), 15, "normal CLI role selected");
            key = RoomKeyRack.Instance.Find(101);
            Require(key && key.rackAnchor, "real authored room101 key exists");
            facts.Add("Diagnostic fixture only: same EXE, normal -hotelHost/-hotelJoin, real NGO messages. No diagnostic RPC.");
            facts.Add("Stage files coordinate waits and compare measured poses; they never apply hotel state or input.");
            if (continuous) { if (host) yield return RunContinuousHost(); else yield return RunContinuousClient(); }
            else if (services) { if (host) yield return RunServicesHost(); else yield return RunServicesClient(); }
            else if (agency) { if (host) yield return RunAgencyHost(); else yield return RunAgencyClient(); }
            else { if (host) yield return RunHost(); else yield return RunClient(); }
            Require(errors == 0, "no logged Unity errors");
        }

        void CheckCameraAndAuthority()
        {
            Require(coop.Players.Count(player => player.PlayerCamera.enabled) == 1, "one enabled staff camera");
            Require(FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Count(listener => listener.enabled) == 1,
                "one enabled AudioListener");
            Require(coop.LocalActorId == (host ? 0 : 1), "network identity is role based");
            Require(coop.Players[coop.LocalActorId].PlayerCamera.rect == new Rect(0, 0, 1, 1), "local camera covers the full screen");
            Require(coop.Players.All(player => player.HasWorldAuthority == host && player.BodyCollider.enabled == host &&
                player.Interactor.HasWorldAuthority == host), "both rigs execute physics only on the host");
            facts.Add("OneCamera=True OneListener=True LocalActor=" + coop.LocalActorId + " WorldAuthority=" + host);
        }

        IEnumerator RunHost()
        {
            Require(NetworkManager.Singleton && NetworkManager.Singleton.IsListening, "actual NGO host listens");
            WriteStage("host-listening");
            yield return Until(() => lan.PeerConnected, 35, "actual remote client connected");
            CheckCameraAndAuthority();
            yield return Until(() => session.Phase == DayPhase.Service && session.Simulation.Guests.Count == 1,
                25, "remote Assign and Commit reached host");
            Require(lan.AcceptedRemoteCommands >= 2, "host accepted two normal client ledger commands");
            Require(session.CommittedBookings.Single().RoomId == 101, "host committed room101 from remote command");
            serviceClockStart = session.Simulation.Elapsed;
            // The fixture positions an empty remote STAFF rig once. It never moves, owns or approves the key.
            var approach = new GameObject("DIAGNOSTIC empty staff rack approach");
            approach.transform.position = new Vector3(key.rackAnchor.position.x, .08f, key.rackAnchor.position.z - 1.45f);
            coop.Players[1].ResetToSpawn(approach.transform); Destroy(approach);
            Require(key.State.Location == RoomKeyLocation.OnRack, "key remains on its authored rack before real F input");
            yield return new WaitForSecondsRealtime(.4f);
            Vector3 departure = coop.Players[1].transform.position;
            facts.Add("DIAGNOSTIC: empty actor1 placed near rack; all item pickup/carry/drop uses real client pad through NGO.");
            WriteStage("host-rack-ready");

            yield return Until(() => coop.Players[1].Interactor.HeldBody == key.Body, 18, "remote F physically picked up rack key");
            Require(key.State.Location == RoomKeyLocation.HeldByPlayer && key.State.PlayerId == 1,
                "host model names remote actor as the key owner");
            Require(key.Body.GetComponent<ConfigurableJoint>() && !key.Body.isKinematic,
                "host uses the actual dynamic body and physical carry joint");
            WriteStage("host-pickup-verified");
            yield return Stage("client-moved", 18);
            float moved = Horizontal(coop.Players[1].transform.position, departure);
            Require(moved > 1.7f && coop.Players[1].Interactor.HeldBody == key.Body,
                "remote actor physically carried key more than 1.7 metres");
            yield return new WaitForSecondsRealtime(.5f);
            WriteText("host-pose.json", JsonUtility.ToJson(new PoseMeasurement
            { epoch = lan.Epoch, staff = coop.Players[1].transform.position, key = key.Body.position }));
            yield return Stage("client-pose-verified", 8);
            yield return Until(() => coop.Players[1].Interactor.HeldBody == null && key.State.Location == RoomKeyLocation.Dropped,
                12, "remote F dropped the real key");
            yield return null;
            Require(key.Body.GetComponent<ConfigurableJoint>() == null && !key.State.PlayerId.HasValue, "drop cleared joint and model owner");
            WriteStage("host-drop-verified");

            yield return Until(() => coop.Players[1].Interactor.HeldBody == key.Body, 18, "remote F physically regrabs the dropped key");
            WriteStage("host-held-for-disconnect");
            yield return Until(() => !lan.PeerConnected && coop.Players[1].Interactor.HeldBody == null,
                15, "real transport disconnect releases remote carry");
            yield return null;
            Require(key.State.Location == RoomKeyLocation.Dropped && !key.State.PlayerId.HasValue &&
                !key.Body.GetComponent<ConfigurableJoint>(), "disconnect leaves no key owner or joint");
            Require(!coop.Players[1].Input.PrimaryHeld && !coop.Players[1].Input.WaitHeld,
                "disconnect leaves no held interaction or WAIT input");
            Require(session.Simulation.Elapsed > serviceClockStart + 2, "host advanced service using normal Update ticks");
            facts.Add("RemoteCommands=" + lan.AcceptedRemoteCommands + " PhysicalPickup=True CarryMetres=" + moved.ToString("F2") +
                " Drop=True Regrab=True DisconnectRelease=True HostClockAdvanced=True");
            facts.Add("ModelBytes=" + lan.LastModelBytes + " WorldBytes=" + lan.LastWorldBytes);
            WriteStage("host-disconnect-verified");
            yield return Stage("client-complete", 10);
        }

        IEnumerator RunClient()
        {
            yield return Until(() => lan.PeerConnected && lan.HasSnapshot, 35, "client receives actual host model");
            CheckCameraAndAuthority();
            Require(session.IsLanReplica && session.Simulation.IsReadOnlyMirror, "client model is read-only");
            Require(pad != null && pad.added && pad.enabled && pad.canRunInBackground, "owned background-capable test pad");
            yield return Until(() => ReferenceEquals(coop.Players[1].Input.Gamepad, pad), 3, "client reads only its owned diagnostic pad");
            facts.Add("Only client actor1 receives synthetic pad state; host remote reader is untouched by fixture code.");
            lastClientSequence = lan.AppliedModelSequence;
            lastClientClock = session.Simulation.Clock.SimulationTime; clockTracking = true;

            var offer = session.Plan.Applications.First();
            session.Assign(1, offer.Id, 101, session.Economy.MinPrice);
            yield return Until(() => session.Plan.Assignments.Any(booking => booking.RoomId == 101 && booking.BookingId == offer.Id),
                12, "Assign roundtrip returns in the read-only client snapshot");
            session.CommitPlan(1);
            yield return Until(() => session.Phase == DayPhase.Service && session.Simulation.Guests.Count == 1,
                12, "Commit roundtrip opens the same host service");
            Require(session.Simulation.Guests.Single().RoomId == 101, "same guest room reached client snapshot");
            float before = session.Simulation.Elapsed;
            session.Simulation.Tick(3);
            Require(Mathf.Abs(session.Simulation.Elapsed - before) < .00001f, "direct client tick cannot advance mirror time");
            ManagementUI.Instance?.Close();
            yield return Stage("host-rack-ready", 12);
            yield return Until(() => Horizontal(coop.Players[1].transform.position,
                new Vector3(key.rackAnchor.position.x, 0, key.rackAnchor.position.z - 1.45f)) < .12f,
                5, "client receives diagnostic host staff pose through world snapshots");
            yield return Aim(() => key.Body.worldCenterOfMass);
            yield return TapGrab();
            yield return Until(() => MirrorKeyHeld(), 8, "client receives authoritative key ownership after real F");
            yield return Stage("host-pickup-verified", 8);

            var destination = new Vector3(-.7f, 0, -.55f);
            yield return Walk(destination);
            WriteStage("client-moved");
            yield return Until(() => File.Exists(Path.Combine(output, "host-pose.json")), 8, "host pose assertion sample exists");
            var measurement = JsonUtility.FromJson<PoseMeasurement>(File.ReadAllText(Path.Combine(output, "host-pose.json")));
            Require(measurement.epoch == lan.Epoch, "pose comparison belongs to this host epoch");
            yield return Until(() => Vector3.Distance(coop.Players[1].transform.position, measurement.staff) < .18f &&
                Vector3.Distance(key.Body.position, measurement.key) < .30f, 5, "actual client staff/key poses agree with host measurements");
            facts.Add("WorldPoseAgreement=True StaffError=" + Vector3.Distance(coop.Players[1].transform.position, measurement.staff).ToString("F3") +
                " KeyError=" + Vector3.Distance(key.Body.position, measurement.key).ToString("F3"));
            WriteStage("client-pose-verified");
            yield return TapGrab();
            yield return Until(() => session.Simulation.Keys.Find(101).Location == RoomKeyLocation.Dropped, 8,
                "drop is mirrored from the host");
            yield return Stage("host-drop-verified", 8);
            yield return new WaitForSecondsRealtime(.5f);
            var remoteActor = coop.Players[1];
            Vector3 beforeRetreat = remoteActor.transform.position;
            Vector3 away = beforeRetreat - key.Body.worldCenterOfMass; away.y = 0;
            if (away.sqrMagnitude < .01f)
            { away = -remoteActor.transform.forward; away.y = 0; }
            // A floor item directly beneath the camera cannot be aimed at within the normal
            // pitch clamp. Step away through the actual network controller; never move the item.
            yield return Walk(beforeRetreat + away.normalized * .8f, requireCarry: false);
            Require(session.Simulation.Keys.Find(101).Location == RoomKeyLocation.Dropped,
                "floor-key approach did not alter ownership");
            facts.Add("PhysicalFloorApproach=True RetreatMetres=" + Horizontal(beforeRetreat, remoteActor.transform.position).ToString("F2") +
                " KeyHorizontalGap=" + Horizontal(remoteActor.transform.position, key.Body.position).ToString("F2"));
            yield return Aim(() => key.Body.worldCenterOfMass);
            if (capture) yield return Capture("client-one-camera-play");
            yield return TapGrab();
            yield return Until(() => MirrorKeyHeld(), 8, "regrab returns as authoritative ownership");
            yield return Stage("host-held-for-disconnect", 8);
            Queue(default);
            Require(stableClockChecks >= 10, "client clock stayed constant between host snapshots");
            facts.Add("AssignRoundtrip=True CommitRoundtrip=True ReadOnlyTickRejected=True SnapshotOnlyClockChecks=" + stableClockChecks);
            // Normal transport shutdown, keeping the disconnected replica in its supported read-only mode.
            NetworkManager.Singleton.Shutdown();
            yield return Until(() => !lan.PeerConnected && lan.MenuOpen, 12, "client observes its real disconnect");
            yield return Stage("host-disconnect-verified", 12);
            yield return new WaitForSecondsRealtime(.4f);
            before = session.Simulation.Elapsed;
            session.Simulation.Tick(3);
            yield return new WaitForSecondsRealtime(.3f);
            Require(lan.IsClientReplica && session.Simulation.IsReadOnlyMirror &&
                Mathf.Abs(session.Simulation.Elapsed - before) < .00001f, "disconnected client stays frozen and read-only");
            Require(coop.Players.All(player => !player.Interactor.HasWorldAuthority && !player.BodyCollider.enabled),
                "disconnect did not grant client physics authority");
            if (capture) yield return Capture("client-connection-menu");
            facts.Add("DisconnectedReadOnly=True ClientPhysicsDisabled=True (verified before any intentional return to main menu)");
            if (capture)
            {
                clockTracking = false;
                lan.LeaveToMenu();
                yield return Until(() => lan.Role == LanRole.Offline && lan.MenuOpen && !session.Simulation.IsReadOnlyMirror,
                    4, "normal LeaveToMenu creates a fresh local menu session");
                yield return null;
                Require(coop.IsSolo && coop.Players[1] == null &&
                    FindObjectsByType<FirstPersonController>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 1,
                    "return from LAN removes the second rig and restores one-person menu");
                yield return Capture("client-host-join-menu");
                Require(lan.StartSolo(), "SOLO starts from the actual connection menu after leaving LAN");
                yield return null;
                Require(coop.IsSolo && !lan.IsActive && !lan.MenuOpen && !session.IsLanReplica &&
                    coop.Players[0].HasWorldAuthority && coop.Players[0].PlayerCamera.rect == new Rect(0, 0, 1, 1),
                    "SOLO restores local simulation and full-screen authority after LAN");
                facts.Add("IntentionalLeaveToMenu=True FreshLocalSession=True SoloAfterLan=True StaffObjects=1; the earlier disconnected replica assertion does not describe this new session.");
            }
            WriteStage("client-complete");
        }

        bool MirrorKeyHeld()
        {
            var state = session.Simulation.Keys.Find(101);
            return state != null && state.Location == RoomKeyLocation.HeldByPlayer && state.PlayerId == 1;
        }

        IEnumerator Aim(Func<Vector3> target)
        {
            var actor = coop.Players[1];
            float deadline = Time.realtimeSinceStartup + 9;
            while (Time.realtimeSinceStartup < deadline)
            {
                Vector3 delta = target() - actor.PlayerCamera.transform.position;
                float yaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
                float pitch = -Mathf.Atan2(delta.y, new Vector2(delta.x, delta.z).magnitude) * Mathf.Rad2Deg;
                float x = Mathf.DeltaAngle(actor.transform.eulerAngles.y, yaw);
                float y = Mathf.DeltaAngle(actor.PlayerCamera.transform.localEulerAngles.x, pitch);
                if (Mathf.Abs(x) < 1.3f && Mathf.Abs(y) < 1.3f) break;
                Queue(new GamepadState { rightStick = new Vector2(LookAxis(x), -LookAxis(y)) });
                yield return null;
            }
            Queue(default);
            yield return new WaitForSecondsRealtime(.25f);
            Require(Vector3.Angle(actor.PlayerCamera.transform.forward, target() - actor.PlayerCamera.transform.position) < 4,
                "actual network look acquired the physical key");
            yield return Until(() => actor.Interactor.ReplicaPickup &&
                (actor.Interactor.ReplicaCaption ?? "").Contains("Room 101 key"), 4,
                "host's real pickup raycast is visible in the replicated prompt");
        }
        static float LookAxis(float error) => Mathf.Abs(error) < 1.3f ? 0 : Mathf.Sign(error) * Mathf.Clamp(Mathf.Abs(error) / 60, .18f, .32f);

        IEnumerator TapGrab() => TapButton(GamepadButton.RightShoulder);

        IEnumerator TapButton(GamepadButton button)
        {
            Queue(new GamepadState().WithButton(button));
            yield return new WaitForSecondsRealtime(.10f);
            Queue(default);
            yield return new WaitForSecondsRealtime(.20f);
        }

        IEnumerator Capture(string name)
        {
            Queue(default);
            yield return null; yield return null;
            Texture2D pixels = null;
            try
            {
                // Render the real single active camera and native IMGUI list, independent of a hidden window's backbuffer.
                var views = coop.Players.Where(player => player && player.PlayerCamera.enabled).ToArray();
                pixels = VerificationOffscreenCapture.Capture(views);
                Require(VerificationOffscreenCapture.LastOverlaySubmitted, "real IMGUI submitted for " + name);
                File.WriteAllBytes(Path.Combine(output, name + ".png"), pixels.EncodeToPNG());
                facts.Add("GPU candidate=" + name + ".png NativeIMGUI=True SubmittedCameraCount=" + views.Length +
                    "; pixels and layout require human inspection.");
            }
            finally { if (pixels) Destroy(pixels); }
        }

        IEnumerator Walk(Vector3 destination, bool requireCarry = true)
        {
            var actor = coop.Players[1];
            float deadline = Time.realtimeSinceStartup + 12;
            while (Horizontal(actor.transform.position, destination) > .16f && Time.realtimeSinceStartup < deadline)
            {
                if (requireCarry) Require(MirrorKeyHeld(), "mirror still reports held key during actual carry");
                Vector3 delta = destination - actor.transform.position; delta.y = 0;
                Vector3 direction = actor.transform.InverseTransformDirection(delta.normalized);
                float speed = Mathf.Clamp(delta.magnitude * .45f, .25f, .6f);
                Queue(new GamepadState { leftStick = new Vector2(direction.x, direction.z) * speed });
                yield return null;
            }
            Queue(default);
            yield return new WaitForSecondsRealtime(.4f);
            Require(Horizontal(actor.transform.position, destination) < .35f, "network movement reached the open lobby destination");
        }

        void Queue(GamepadState state)
        { if (pad != null && pad.added) InputSystem.QueueStateEvent(pad, state); }
        static float Horizontal(Vector3 a, Vector3 b) { a.y = b.y = 0; return Vector3.Distance(a, b); }

        IEnumerator Until(Func<bool> condition, float seconds, string operation)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                if (File.Exists(Path.Combine(output, host ? "client-failed.stage" : "host-failed.stage")))
                    throw new InvalidOperationException("Peer reported failure during " + operation);
                yield return null;
            }
            Require(condition(), operation + " (timeout)");
        }
        IEnumerator Stage(string name, float timeout) => Until(() => File.Exists(Path.Combine(output, name + ".stage")), timeout, name);
        void WriteStage(string name) => WriteText(name + ".stage", DateTime.UtcNow.ToString("O"));
        void WriteText(string name, string contents)
        {
            string path = Path.Combine(output, name), temporary = path + ".tmp";
            File.WriteAllText(temporary, contents);
            if (File.Exists(path)) File.Delete(path);
            File.Move(temporary, path);
        }
        void Require(bool condition, string operation)
        {
            if (!condition) throw new InvalidOperationException(operation);
            checks++;
        }
        void Fail(string reason)
        {
            if (finished) return;
            finished = true;
            facts.Add("FAIL: " + reason);
            Debug.LogError("LAN VERIFY FAILED [" + side + "]: " + reason);
            WriteStage(side + "-failed"); WriteReport("FAIL"); Application.Quit(2);
        }
        void WriteReport(string outcome)
        {
            WriteText(side + "-report.txt", "Outcome=" + outcome + " Errors=" + errors + " Role=" + side +
                " Mode=" + (continuous ? "ContinuousFixtures" : services ? "ServiceFixtures" : agency ? "AgencyFixtures" : "PhysicalKeys") +
                " Checks=" + checks + "\nUtc=" + DateTime.UtcNow.ToString("O") + "\nRun=" + Path.GetFileName(output) +
                "\n" + string.Join("\n", facts) +
                "\nScope=localhost two actual development EXE processes; no human controls, remote-machine LAN, image or performance claim.\n");
        }
        void OnDestroy()
        {
            SceneManager.sceneLoaded -= PrepareLegacyVerification;
            if (legacyVerificationConfig) Destroy(legacyVerificationConfig);
            if (continuousFundingConfig) Destroy(continuousFundingConfig);
            if (continuousFundingEconomy) Destroy(continuousFundingEconomy);
            Application.logMessageReceived -= OnLog;
            if (pad != null && pad.added) InputSystem.RemoveDevice(pad);
            if (layoutRegistered) InputSystem.RemoveLayout(PadLayout);
        }
    }
}
#endif
