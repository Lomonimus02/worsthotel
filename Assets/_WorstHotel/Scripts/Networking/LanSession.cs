using System;
using System.Collections;
using System.Linq;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace WorstHotel
{
    /// <summary>Two-computer direct-IP prototype. The host alone executes gameplay and physical interaction.</summary>
    [DefaultExecutionOrder(500)]
    public sealed partial class LanSession : MonoBehaviour
    {
        const string InputMessage = "hotel/input", CommandMessage = "hotel/command", HotelMessage = "hotel/state", WorldMessage = "hotel/world";
        public static LanSession Instance { get; private set; }
        public LanRole Role { get; private set; }
        public bool IsActive => Role != LanRole.Offline;
        public bool IsClientReplica => Role == LanRole.Client;
        public bool IsSolo => Role == LanRole.Offline && coop && coop.IsSolo;
        public bool MenuOpen { get; private set; } = true;
        public bool HasSnapshot { get; private set; }
        public bool PeerConnected { get; private set; }
        public long Epoch { get; private set; }
        public long AppliedModelSequence { get; private set; }
        public string Status { get; private set; } = "Two owners. One hotel. Connect on the same local network.";
        public string RemoteRepairStatus { get; private set; }
        public int LastModelBytes { get; private set; }
        public int LastWorldBytes { get; private set; }
        public int LastWorldDecodedBytes { get; private set; }
        public int AcceptedRemoteCommands { get; private set; }
        public int RejectedRemoteCommands { get; private set; }
        public string[] Arguments { get; private set; }
        NetworkManager manager;
        UnityTransport transport;
        LocalCoopBootstrap coop;
        LanWorldReplicator world;
        HotelSimulation hostModel;
        ulong remoteClient = ulong.MaxValue;
        long modelSequence, worldSequence, inputSequence, commandSequence, lastCommandSequence, openLedgerRevision, receivedLedgerRevision;
        long openGuestRevision, receivedGuestRevision;
        long openServiceRevision, receivedServiceRevision;
        long openBoilerRevision, receivedBoilerRevision;
        bool servicePhone;
        string conversationGuestId;
        bool conversationThroughDoor;
        float nextModel, nextWorld, lastSnapshotAt, connectionBegan;
        bool leaving, soloStarted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            var args = Environment.GetCommandLineArgs();
            // Existing editor integration suites and the explicit three-day driver retain their local fixtures.
            if (args.Any(arg => arg.Equals("-runTests", StringComparison.OrdinalIgnoreCase) || arg == "-verifyHotel")) return;
            if (!Instance) new GameObject("LAN session").AddComponent<LanSession>();
        }

        void Awake()
        {
            if (Instance && Instance != this) { Destroy(gameObject); return; }
            Instance = this; Arguments = Environment.GetCommandLineArgs();
            DontDestroyOnLoad(gameObject);
            Application.runInBackground = true;
        }

        IEnumerator Start()
        {
            while (!GameSession.Instance || !LocalCoopBootstrap.Instance || !LocalCoopBootstrap.Instance.Players[0]) yield return null;
            coop = LocalCoopBootstrap.Instance;
            world = GameSession.Instance.gameObject.AddComponent<LanWorldReplicator>();
            coop.RemoteLedgerRequested += RequestRemoteLedger;
            ManagementUI.Instance?.Close();
            coop.SetPaused(true);
            int hostFlag = Array.IndexOf(Arguments, "-hotelHost"), joinFlag = Array.IndexOf(Arguments, "-hotelJoin");
            if (hostFlag >= 0) StartHost();
            else if (joinFlag >= 0 && joinFlag + 1 < Arguments.Length) Join(Arguments[joinFlag + 1]);
            else if (Array.IndexOf(Arguments, "-hotelSolo") >= 0) StartSolo();
        }

        void EnsureManager()
        {
            if (manager) return;
            var root = new GameObject("Unity LAN transport");
            DontDestroyOnLoad(root);
            transport = root.AddComponent<UnityTransport>();
            transport.ConnectTimeoutMS = 500; transport.MaxConnectAttempts = 12; transport.DisconnectTimeoutMS = 4000;
            transport.MaxPacketQueueSize = 512;
            manager = root.AddComponent<NetworkManager>();
            manager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport, EnableSceneManagement = false, ConnectionApproval = true,
                ConnectionData = Encoding.UTF8.GetBytes(LanProtocol.BuildCompatibility), TickRate = 30,
                ForceSamePrefabs = false
            };
            manager.ConnectionApprovalCallback = Approve;
            manager.OnClientConnectedCallback += Connected;
            manager.OnClientDisconnectCallback += Disconnected;
        }

        ushort Port
        {
            get
            {
                int flag = Array.IndexOf(Arguments, "-hotelPort");
                return flag >= 0 && flag + 1 < Arguments.Length && ushort.TryParse(Arguments[flag + 1], out ushort port) && port > 0 ?
                    port : LanProtocol.DefaultPort;
            }
        }

        public bool StartHost()
        {
            if (!coop || Role != LanRole.Offline || manager && (manager.IsListening || manager.ShutdownInProgress)) return false;
            GameSession.Instance.GetComponent<DeveloperPanel>()?.CloseForLan();
            EnsureManager();
            soloStarted = false;
            Role = LanRole.Host; MenuOpen = false; HasSnapshot = true; PeerConnected = false;
            Epoch = DateTime.UtcNow.Ticks; remoteClient = ulong.MaxValue;
            coop.ConfigureLan(Role, 0); world.Configure(Role); coop.SetRemoteConnected(false);
            GameSession.Instance.NewGame(); hostModel = GameSession.Instance.Simulation;
            transport.SetConnectionData("127.0.0.1", Port, "0.0.0.0");
            if (!manager.StartHost()) { Role = LanRole.Offline; MenuOpen = true; coop.ConfigureLan(Role, 0); world.Configure(Role); coop.SetPaused(true);
                Status = "Could not host. Check whether the UDP port is already in use."; return false; }
            RegisterMessages(); coop.SetPaused(false);
            Status = "HOST · UDP " + Port + " · waiting for a second owner";
            ManagementUI.Instance?.Close();
            return true;
        }

        public bool Join(string address)
        {
            if (!coop || Role != LanRole.Offline || manager && (manager.IsListening || manager.ShutdownInProgress)) return false;
            if (!LanProtocol.ValidAddress(address)) { Status = "Enter the host's IPv4 address, for example 192.168.1.10 or 127.0.0.1."; return false; }
            GameSession.Instance.GetComponent<DeveloperPanel>()?.CloseForLan();
            EnsureManager();
            soloStarted = false;
            Role = LanRole.Client; MenuOpen = false; HasSnapshot = PeerConnected = false; Epoch = 0;
            AppliedModelSequence = receivedLedgerRevision = receivedGuestRevision = receivedServiceRevision = inputSequence = commandSequence = 0;
            coop.ConfigureLan(Role, 1); coop.SetRemoteConnected(false);
            GameSession.Instance.PrepareLanReplica();
            world.Configure(Role);
            ManagementUI.Instance?.Close();
            transport.SetConnectionData(address, Port);
            if (!manager.StartClient()) { MenuOpen = true; Status = "Could not start the connection. Return to menu and try again."; return false; }
            RegisterMessages(); connectionBegan = Time.unscaledTime; coop.SetPaused(false);
            Status = "Connecting to " + address + ":" + Port + "…";
            return true;
        }

        void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            bool compatible = request.Payload != null && request.Payload.Length <= 128 &&
                Encoding.UTF8.GetString(request.Payload) == LanProtocol.BuildCompatibility;
            bool slot = request.ClientNetworkId == NetworkManager.ServerClientId || remoteClient == ulong.MaxValue;
            response.Approved = compatible && slot;
            response.CreatePlayerObject = false;
            response.Reason = !compatible ? "Use the same Worst Hotel build on both computers." : "This prototype supports two owners; the hotel is full.";
            if (response.Approved && request.ClientNetworkId != NetworkManager.ServerClientId) remoteClient = request.ClientNetworkId;
        }

        void RegisterMessages()
        {
            manager.CustomMessagingManager.RegisterNamedMessageHandler(InputMessage, ReceiveInput);
            manager.CustomMessagingManager.RegisterNamedMessageHandler(CommandMessage, ReceiveCommand);
            manager.CustomMessagingManager.RegisterNamedMessageHandler(HotelMessage, ReceiveHotel);
            manager.CustomMessagingManager.RegisterNamedMessageHandler(WorldMessage, ReceiveWorld);
        }

        void Connected(ulong clientId)
        {
            if (Role == LanRole.Host && clientId != NetworkManager.ServerClientId)
            {
                if (clientId != remoteClient) { manager.DisconnectClient(clientId); return; }
                PeerConnected = true; lastCommandSequence = 0;
                openLedgerRevision = 0;
                openGuestRevision = openServiceRevision = openBoilerRevision = 0; conversationGuestId = null;
                coop.ResetInputEpoch(Epoch); coop.SetRemoteConnected(true);
                nextModel = nextWorld = 0; Status = "HOST · second owner connected · UDP " + Port;
            }
            else if (Role == LanRole.Client && clientId == manager.LocalClientId)
            { PeerConnected = true; receivedBoilerRevision = 0; Status = "Connected. Receiving the host's hotel…"; }
        }

        void Disconnected(ulong clientId)
        {
            if (leaving) return;
            if (Role == LanRole.Host && clientId == remoteClient)
            {
                GameSession.Instance.ClearGuestConversation(1);
                GameSession.Instance.ClearServicePhone(1);
                BoilerServiceInteraction.Instance?.ClearSelection(1);
                coop.SetRemoteConnected(false); PeerConnected = false; remoteClient = ulong.MaxValue;
                GameSession.Instance.Wait?.Stop("The second owner disconnected.");
                Status = "Second owner disconnected. Hosting continues; waiting for them to rejoin.";
            }
            else if (Role == LanRole.Client && (clientId == manager.LocalClientId || !manager.IsConnectedClient))
            {
                PeerConnected = false; HasSnapshot = false; coop.SetRemoteConnected(false); coop.SetPaused(true);
                MenuOpen = true;
                string reason = manager.DisconnectReason;
                Status = reason == "Use the same Worst Hotel build on both computers." ||
                    reason == "This prototype supports two owners; the hotel is full." ? reason :
                    "Connection ended. Return to the main menu to join the host again.";
            }
        }

        public void NotifyHostNewGame()
        {
            if (Role != LanRole.Host) return;
            Epoch = Math.Max(Epoch + 1, DateTime.UtcNow.Ticks);
            modelSequence = worldSequence = lastCommandSequence = 0;
            openLedgerRevision = 0;
            openGuestRevision = openServiceRevision = openBoilerRevision = 0; conversationGuestId = null;
            coop?.ResetInputEpoch(Epoch);
            nextModel = nextWorld = 0;
        }

        void Update()
        {
            ReadMenuInput();
            if (!coop || !IsActive) return;
            if (Role == LanRole.Client && PeerConnected && HasSnapshot)
                Send(InputMessage, NetworkManager.ServerClientId, coop.CaptureLocalInput(Epoch, ++inputSequence), LanProtocol.MaxInputBytes);
            if (Role == LanRole.Client && !MenuOpen && ((!HasSnapshot && Time.unscaledTime - connectionBegan > 10) ||
                HasSnapshot && Time.unscaledTime - lastSnapshotAt > 5))
            { Status = "Host state timed out. Return to the menu and reconnect."; MenuOpen = true; coop.SetPaused(true); manager.Shutdown(); }
        }

        void LateUpdate()
        {
            if (Role != LanRole.Host || !manager || !manager.IsListening || !PeerConnected || remoteClient == ulong.MaxValue) return;
            var session = GameSession.Instance;
            if (hostModel != session.Simulation) { hostModel = session.Simulation; nextModel = nextWorld = 0; }
            // Connection-menu input runs after the ordinary time controller this frame.
            // Revoke before publishing so no paused packet retains active advance consent.
            var advance = session.Wait;
            if (advance && (advance.IsWaiting || advance.HasSleepConsent(0) || advance.HasSleepConsent(1)) &&
                (coop.IsPaused || MenuOpen))
                advance.RevokeSleep(coop.IsPaused ? StaffWakeReason.Paused : StaffWakeReason.MenuOpened);
            if (Time.unscaledTime >= nextModel)
            {
                nextModel = Time.unscaledTime + .2f;
                var frame = session.CaptureLanFrame(Epoch, ++modelSequence);
                frame.openLedgerRevision = openLedgerRevision;
                frame.openGuestRevision = openGuestRevision;
                frame.conversationGuestId = conversationGuestId;
                frame.conversationThroughDoor = conversationThroughDoor;
                frame.openServiceRevision = openServiceRevision;
                frame.openBoilerRevision = openBoilerRevision;
                frame.servicePhone = servicePhone;
                frame.hostPaused = coop.IsPaused;
                LastModelBytes = Send(HotelMessage, remoteClient, frame, LanProtocol.MaxSnapshotBytes);
            }
            if (Time.unscaledTime >= nextWorld)
            {
                nextWorld = Time.unscaledTime + .05f;
                LastWorldBytes = Send(WorldMessage, remoteClient, world.Capture(Epoch, ++worldSequence), LanProtocol.MaxSnapshotBytes, compressedWorld: true);
            }
        }

        void RequestRemoteLedger(int actorId)
        { if (Role == LanRole.Host && actorId == 1) { openLedgerRevision++; nextModel = 0; } }

        public void RequestRemoteGuestConversation(string guestId, bool throughDoor)
        {
            if (Role != LanRole.Host || !PeerConnected) return;
            conversationGuestId = guestId; conversationThroughDoor = throughDoor;
            openGuestRevision++; nextModel = 0;
        }

        public void RequestRemoteServiceDesk(bool phone)
        {
            if (Role != LanRole.Host || !PeerConnected) return;
            servicePhone = phone; openServiceRevision++; nextModel = 0;
        }

        public void RequestRemoteBoilerInspection()
        {
            if (Role != LanRole.Host || !PeerConnected || openBoilerRevision == long.MaxValue) return;
            openBoilerRevision++; nextModel = 0;
        }

        public bool SubmitCommand(LanCommandKind kind, string subject = null, int roomId = 0, int amount = 0, int reservationRevision = -1,
            string directIntentId = null, int directIntentRevision = -1, int maintenanceRevision = -1,
            int policyRevision = -1, bool openForSale = false)
        {
            if (!IsClientReplica || !HasSnapshot || !PeerConnected || MenuOpen) return false;
            var session = GameSession.Instance;
            var command = new LanCommand { epoch = Epoch, sequence = ++commandSequence, day = session.Day,
                phase = session.Phase, kind = kind, subject = subject, roomId = roomId, amount = amount,
                expectedReservationRevision = reservationRevision, expectedDirectIntentId = directIntentId,
                expectedDirectIntentRevision = directIntentRevision, expectedMaintenanceRevision = maintenanceRevision,
                expectedPolicyRevision = policyRevision, openForSale = openForSale };
            Send(CommandMessage, NetworkManager.ServerClientId, command, LanProtocol.MaxCommandBytes);
            return true;
        }

        void ReceiveInput(ulong sender, FastBufferReader reader)
        {
            if (Role != LanRole.Host || sender != remoteClient || !PeerConnected) return;
            var frame = Read<LanInputFrame>(reader, LanProtocol.MaxInputBytes);
            if (frame != null && frame.epoch == Epoch && !MenuOpen) coop.SubmitRemoteInput(frame);
        }

        void ReceiveCommand(ulong sender, FastBufferReader reader)
        {
            if (Role != LanRole.Host || sender != remoteClient || !PeerConnected) return;
            var command = Read<LanCommand>(reader, LanProtocol.MaxCommandBytes);
            var session = GameSession.Instance;
            if (MenuOpen || !LanProtocol.ValidCommand(command, Epoch, lastCommandSequence, session.Day, session.Phase,
                session.Simulation.ContinuousOperations, session.Simulation.AutomaticBookingsEnabled) ||
                session.Phase == DayPhase.Service && !coop.Players[1].IsUIBlocked)
            { RejectedRemoteCommands++; return; }
            lastCommandSequence = command.sequence;
            session.ExecuteLanCommand(1, command); AcceptedRemoteCommands++; nextModel = 0;
        }

        void ReceiveHotel(ulong sender, FastBufferReader reader)
        {
            if (!IsClientReplica || sender != NetworkManager.ServerClientId) return;
            var frame = Read<LanHotelFrame>(reader, LanProtocol.MaxSnapshotBytes);
            if (frame == null || frame.version != LanProtocol.Version || frame.epoch < Epoch ||
                frame.epoch == Epoch && frame.sequence <= AppliedModelSequence) return;
            if (!GameSession.Instance.ApplyLanFrame(frame).Success) return;
            bool freshEpoch = frame.epoch != Epoch;
            Epoch = frame.epoch; AppliedModelSequence = frame.sequence; HasSnapshot = true;
            RemoteRepairStatus = frame.repairStatus; lastSnapshotAt = Time.unscaledTime;
            coop.SetRemoteHostPaused(frame.hostPaused);
            coop.SetRemoteConnected(true);
            if (freshEpoch) { coop.ResetInputEpoch(Epoch); receivedLedgerRevision = receivedGuestRevision = receivedServiceRevision = receivedBoilerRevision = 0; }
            if (frame.openLedgerRevision > receivedLedgerRevision)
            { receivedLedgerRevision = frame.openLedgerRevision; ManagementUI.Instance?.Open(coop.LocalActorId); }
            if (frame.openGuestRevision > receivedGuestRevision)
            {
                receivedGuestRevision = frame.openGuestRevision;
                ManagementUI.Instance?.OpenGuestContext(coop.LocalActorId, frame.conversationGuestId, frame.conversationThroughDoor);
            }
            if (frame.openServiceRevision > receivedServiceRevision)
            {
                receivedServiceRevision = frame.openServiceRevision;
                if (frame.servicePhone) ManagementUI.Instance?.OpenWakePhone(coop.LocalActorId);
                else ManagementUI.Instance?.OpenReceptionServiceBoard(coop.LocalActorId);
            }
            if (frame.openBoilerRevision > receivedBoilerRevision)
            {
                receivedBoilerRevision = frame.openBoilerRevision;
                ManagementUI.Instance?.OpenBoilerInspection(coop.LocalActorId);
            }
            Status = frame.hostPaused ? "CLIENT · host paused the hotel · UDP " + Port :
                "CLIENT · connected to the host · UDP " + Port;
        }

        void ReceiveWorld(ulong sender, FastBufferReader reader)
        {
            if (!IsClientReplica || sender != NetworkManager.ServerClientId || !HasSnapshot) return;
            var frame = Read<LanWorldFrame>(reader, LanProtocol.MaxSnapshotBytes, compressedWorld: true);
            if (frame != null && frame.epoch == Epoch) world.Apply(frame);
        }

        int Send<T>(string name, ulong recipient, T payload, int limit, bool compressedWorld = false)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload));
            if (bytes.Length > limit) { Debug.LogError("LAN payload exceeds its bound: " + name + " " + bytes.Length); return 0; }
            if (compressedWorld)
            {
                // Full scene JSON repeats static labels and poses. Sending it uncompressed at
                // 20 Hz can queue old reliable fragments ahead of the current camera pose.
                LastWorldDecodedBytes = bytes.Length;
                bytes = LanWorldPayload.Encode(bytes, limit);
                if (bytes.Length > limit + LanWorldPayload.MaxPacketOverhead) { Debug.LogError("LAN compressed world exceeds its bound: " + bytes.Length); return 0; }
            }
            using (var writer = new FastBufferWriter(bytes.Length + 4, Allocator.Temp))
            {
                writer.WriteValueSafe(bytes.Length); writer.WriteBytesSafe(bytes);
                manager.CustomMessagingManager.SendNamedMessage(name, recipient, writer, NetworkDelivery.ReliableFragmentedSequenced);
            }
            return bytes.Length;
        }

        static T Read<T>(FastBufferReader reader, int limit, bool compressedWorld = false) where T : class
        {
            try
            {
                if (reader.Length < 4) return null;
                reader.ReadValueSafe(out int length);
                int wireLimit = compressedWorld ? limit + LanWorldPayload.MaxPacketOverhead : limit;
                if (length < 2 || length > wireLimit || reader.Length - reader.Position != length) return null;
                var bytes = new byte[length]; reader.ReadBytesSafe(ref bytes, length);
                if (compressedWorld && !LanWorldPayload.TryDecode(bytes, limit, out bytes)) return null;
                return JsonUtility.FromJson<T>(Encoding.UTF8.GetString(bytes));
            }
            catch (Exception exception) when (exception is ArgumentException || exception is OverflowException || exception is InvalidOperationException)
            { return null; }
        }

        public void LeaveToMenu()
        {
            if (!coop) return;
            leaving = true;
            coop.SetRemoteConnected(false);
            foreach (var player in coop.Players) if (player) player.Interactor.CancelInteraction();
            manager?.Shutdown();
            Role = LanRole.Offline; PeerConnected = HasSnapshot = false; Epoch = 0; MenuOpen = true;
            soloStarted = false;
            world.Configure(LanRole.Offline); coop.ConfigureSolo();
            GameSession.Instance.NewGame(); ManagementUI.Instance?.Close(); coop.SetPaused(true);
            Status = "Start Solo, host a new hotel, or join by IP.";
            leaving = false;
        }

        public void StartLocalMode()
        {
            if (IsActive) LeaveToMenu();
            soloStarted = false; MenuOpen = false; coop.ConfigureLan(LanRole.Offline, 0); coop.SetPaused(false);
            GameSession.Instance.NewGame();
        }

        public bool StartSolo()
        {
            if (!coop || IsActive || manager && (manager.IsListening || manager.ShutdownInProgress)) return false;
            GameSession.Instance.GetComponent<DeveloperPanel>()?.CloseForLan();
            coop.ConfigureSolo(); world.Configure(LanRole.Offline);
            GameSession.Instance.NewGame(); ManagementUI.Instance?.Close();
            soloStarted = true; MenuOpen = false; coop.SetPaused(false);
            Status = "SOLO · one owner · the same three-day hotel";
            return true;
        }

        void OnDestroy()
        {
            if (coop) coop.RemoteLedgerRequested -= RequestRemoteLedger;
            if (manager)
            {
                manager.OnClientConnectedCallback -= Connected;
                manager.OnClientDisconnectCallback -= Disconnected;
                manager.Shutdown();
                Destroy(manager.gameObject);
            }
            if (Instance == this) Instance = null;
        }
    }
}
