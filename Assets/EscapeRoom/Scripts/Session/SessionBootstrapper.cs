using System;
using EscapeRoom.Colocation;
using EscapeRoom.UI;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace EscapeRoom.Session
{
    public enum SessionState
    {
        Discovering,
        StartingHost,
        Joining,
        WaitingForAnchor,
        InSession,
    }

    /// <summary>
    /// Drives the whole colocated session:
    ///  - Every headset starts by listening for a colocation advertisement.
    ///  - One player presses A to host. They should stand in the middle of the room facing the
    ///    front wall: that pose becomes the shared room origin anchor.
    ///  - The host starts Netcode, creates the anchor, advertises its LAN address over Meta's
    ///    colocation discovery (Bluetooth), and shares the anchor to that colocation group.
    ///  - Everyone else hears the advert, connects to the host over the LAN, loads the shared
    ///    anchor and aligns their tracking space to it.
    /// </summary>
    public class SessionBootstrapper : MonoBehaviour
    {
        public NetworkManager networkManager;
        public UnityTransport transport;
        public RoomAligner aligner;
        public StatusHud hud;

        [Tooltip("Room geometry that is hidden until this headset is aligned to the shared origin.")]
        public GameObject roomContent;

        public ushort port = 7777;
        public string sessionName = "Escape Room";
        public float anchorRetrySeconds = 2f;
        public int anchorMaxAttempts = 30;

        [Tooltip("Address used by the editor shortcut J (join without discovery), e.g. when testing over Quest Link.")]
        public string directJoinAddress = "127.0.0.1";

        public SessionState State { get; private set; }

        Guid _groupUuid;
        bool _advertising;
        bool _discovering;
        string _hostAddress;

        void Start()
        {
            if (!networkManager)
                networkManager = NetworkManager.Singleton;
            if (!transport && networkManager)
                transport = networkManager.GetComponent<UnityTransport>();

            networkManager.OnClientConnectedCallback += OnClientCountChanged;
            networkManager.OnClientDisconnectCallback += OnClientDisconnected;

            SetRoomVisible(false);
            StartDiscovery();
        }

        void OnDestroy()
        {
            OVRColocationSession.ColocationSessionDiscovered -= OnColocationSessionDiscovered;
            if (networkManager)
            {
                networkManager.OnClientConnectedCallback -= OnClientCountChanged;
                networkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            }
        }

        void Update()
        {
            if (State == SessionState.Discovering)
            {
                if (OVRInput.GetDown(OVRInput.Button.One) || KeyDown(KeyCode.H))
                    BeginHost();
                else if (KeyDown(KeyCode.J))
                    BeginJoin(directJoinAddress, port, loadSharedAnchor: false);
            }
            else if (State == SessionState.InSession || State == SessionState.WaitingForAnchor)
            {
                if (OVRInput.GetDown(OVRInput.Button.Two) || KeyDown(KeyCode.Escape))
                    LeaveSession();
            }
        }

        // ---------------------------------------------------------------- discovery

        async void StartDiscovery(string reason = null)
        {
            SetState(SessionState.Discovering,
                (reason != null ? reason + "\n\n" : "") +
                "Looking for a session in this room...\n" +
                "To HOST: stand in the centre of the room facing the front wall, then press A.");

            if (_discovering)
                return;

            OVRColocationSession.ColocationSessionDiscovered -= OnColocationSessionDiscovered;
            OVRColocationSession.ColocationSessionDiscovered += OnColocationSessionDiscovered;

            var result = await OVRColocationSession.StartDiscoveryAsync();
            _discovering = result.Success;
            if (!result.Success)
            {
                Debug.LogWarning($"[EscapeRoom] Colocation discovery failed to start: {result.Status}");
                hud?.SetStatus($"Can't search for sessions ({result.Status}).\nPress A to host instead.");
            }
        }

        async void StopDiscovery()
        {
            OVRColocationSession.ColocationSessionDiscovered -= OnColocationSessionDiscovered;
            if (!_discovering)
                return;

            _discovering = false;
            await OVRColocationSession.StopDiscoveryAsync();
        }

        void OnColocationSessionDiscovered(OVRColocationSession.Data data)
        {
            if (State != SessionState.Discovering)
                return;

            if (!SessionAdvert.TryRead(data.Metadata, out var advert))
            {
                Debug.LogWarning($"[EscapeRoom] Ignoring colocation session {data.AdvertisementUuid}: no usable metadata.");
                return;
            }

            Debug.Log($"[EscapeRoom] Discovered '{advert.name}' at {advert.ip}:{advert.port} (group {data.AdvertisementUuid})");
            StopDiscovery();
            _groupUuid = data.AdvertisementUuid;
            BeginJoin(advert.ip, (ushort)advert.port, loadSharedAnchor: true);
        }

        // ---------------------------------------------------------------- hosting

        async void BeginHost()
        {
            StopDiscovery();
            SetState(SessionState.StartingHost, "Starting host...");

            transport.SetConnectionData("127.0.0.1", port, "0.0.0.0");
            if (!networkManager.StartHost())
            {
                Fail("Could not start the host. Is another app using port " + port + "?");
                return;
            }

            hud?.SetStatus("Creating the room anchor...\nHold still, facing the front wall.");
            var anchor = await SharedAnchorService.CreateAnchorAsync(aligner.GetCalibrationPoseFromHead());
            if (!anchor)
            {
                networkManager.Shutdown();
                Fail("Could not create a spatial anchor here.\nCheck the headset has scanned this room.");
                return;
            }

            aligner.AlignTo(anchor);
            SetRoomVisible(true);

            _hostAddress = LocalNetwork.GetLocalIPv4();
            var advert = new SessionAdvert { ip = _hostAddress, port = port, name = sessionName };
            var advertResult = await OVRColocationSession.StartAdvertisementAsync(advert.ToBytes());
            if (!advertResult.TryGetValue(out _groupUuid))
            {
                Debug.LogError($"[EscapeRoom] StartAdvertisementAsync failed: {advertResult.Status}");
                SetState(SessionState.InSession,
                    $"Hosting at {_hostAddress}, but advertising failed ({advertResult.Status}).\nOther headsets won't find this session.");
                return;
            }

            _advertising = true;
            bool shared = await SharedAnchorService.ShareToGroupAsync(anchor, _groupUuid);
            SetState(SessionState.InSession, shared
                ? HostStatus()
                : $"Hosting at {_hostAddress}, but sharing the room anchor failed.\nCheck Enhanced Spatial Services is on.");
        }

        string HostStatus() =>
            $"Hosting '{sessionName}' at {_hostAddress}\nPlayers in room: {networkManager.ConnectedClientsIds.Count}\n(B to end the session)";

        // ---------------------------------------------------------------- joining

        async void BeginJoin(string address, ushort hostPort, bool loadSharedAnchor)
        {
            SetState(SessionState.Joining, $"Joining {address}:{hostPort}...");

            transport.SetConnectionData(address, hostPort);
            if (!networkManager.StartClient())
            {
                Fail($"Could not connect to {address}:{hostPort}.");
                return;
            }

            if (!loadSharedAnchor)
            {
                SetRoomVisible(true);
                SetState(SessionState.InSession, $"Joined {address} without a shared anchor (editor test mode).");
                return;
            }

            SetState(SessionState.WaitingForAnchor, "Connected. Finding the shared room anchor...\nLook around the room.");

            for (int attempt = 1; attempt <= anchorMaxAttempts; attempt++)
            {
                if (State != SessionState.WaitingForAnchor)
                    return; // left or disconnected while waiting

                var anchor = await SharedAnchorService.LoadFromGroupAsync(_groupUuid);
                if (anchor)
                {
                    aligner.AlignTo(anchor);
                    SetRoomVisible(true);
                    SetState(SessionState.InSession, $"In the room with {networkManager.ConnectedClientsIds.Count} players.\n(B to leave)");
                    return;
                }

                hud?.SetStatus($"Connected. Finding the shared room anchor... (try {attempt}/{anchorMaxAttempts})\nLook around the room.");
                await Awaitable.WaitForSecondsAsync(anchorRetrySeconds);
            }

            networkManager.Shutdown();
            Fail("Couldn't find the host's room anchor.\nMake sure both headsets have Enhanced Spatial Services on.");
        }

        // ---------------------------------------------------------------- lifecycle

        async void LeaveSession()
        {
            if (_advertising)
            {
                _advertising = false;
                await OVRColocationSession.StopAdvertisementAsync();
            }

            networkManager.Shutdown();
            aligner.ClearAlignment();
            SetRoomVisible(false);
            StartDiscovery();
        }

        void OnClientCountChanged(ulong clientId)
        {
            if (State != SessionState.InSession)
                return;

            hud?.SetStatus(networkManager.IsHost
                ? HostStatus()
                : $"In the room with {networkManager.ConnectedClientsIds.Count} players.\n(B to leave)");
        }

        void OnClientDisconnected(ulong clientId)
        {
            if (networkManager.IsServer)
            {
                OnClientCountChanged(clientId);
                return;
            }

            // We are a client and lost the host.
            if (State == SessionState.InSession || State == SessionState.WaitingForAnchor || State == SessionState.Joining)
            {
                aligner.ClearAlignment();
                SetRoomVisible(false);
                Fail("Lost connection to the host.");
            }
        }

        void Fail(string message)
        {
            Debug.LogWarning("[EscapeRoom] " + message);
            StartDiscovery(message);
        }

        void SetState(SessionState state, string message)
        {
            State = state;
            Debug.Log($"[EscapeRoom] {state}: {message.Replace('\n', ' ')}");
            hud?.SetStatus(message);
        }

        void SetRoomVisible(bool visible)
        {
            if (roomContent)
                roomContent.SetActive(visible);
        }

        static bool KeyDown(KeyCode key)
        {
#if ENABLE_LEGACY_INPUT_MANAGER && UNITY_EDITOR
            return Input.GetKeyDown(key);
#else
            return false;
#endif
        }
    }
}
