using Fusion;
using Fusion.Sockets;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Manages the networked game session lifecycle, including player connections, disconnections, and session state.
/// Implements INetworkRunnerCallbacks to handle network events and coordinates scene transitions and player state
/// persistence.
/// </summary>
/// <remarks>Uses a singleton pattern to ensure only one active instance persists across scene loads.</remarks>
public class FusionBootstrap : MonoBehaviour, INetworkRunnerCallbacks
{
    public static FusionBootstrap Instance { get; private set; }

    private const string MenuSceneName = "Menu";

    private NetworkRunner runner;

    [Tooltip("Scene to load after successfully creating or joining a session.")]
    [SerializeField] private SceneRef arenaScene;

    private string statusMessageForMenu;
    private bool sessionEnded;

    /// <summary>
    /// Host-side record of players who dropped out mid-match. Read by PlayerSpawner on both
    /// the spawn and despawn paths. Empty on clients - only the host ever writes it.
    /// </summary>
    public PlayerStateStore PlayerStates { get; } = new PlayerStateStore();

    /// <summary>
    /// Initializes the singleton instance, preserves status messages across scene loads, and ensures the object
    /// persists between scenes.
    /// </summary>
    /// <remarks>Called when the script instance is loaded. Ensures only one instance exists and retrieves the
    /// NetworkRunner component.</remarks>
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // Carried over before the old object dies, otherwise the reason for the
            // disconnect is lost right when the menu is about to display it.
            statusMessageForMenu = Instance.statusMessageForMenu;
            Destroy(Instance.gameObject);
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        runner = GetComponent<NetworkRunner>();
    }

    /// <summary>
    /// Starts a new networked game session as a host with the specified room name.
    /// </summary>
    public async void StartHost(string roomName)
    {
        await StartGame(GameMode.Host, roomName);
    }

    /// <summary>
    /// Starts a new networked game session as a client with the specified room name.
    /// </summary>
    public async void StartClient(string roomName)
    {
        await StartGame(GameMode.Client, roomName);
    }

    private async Task StartGame(GameMode mode, string roomName)
    {
        if (sessionEnded)
        {
            Debug.LogWarning("FusionBootstrap: this session has already ended.");
            return;
        }

        runner.ProvideInput = true;
        runner.AddCallbacks(this);

        StartGameArgs args = new StartGameArgs();

        args.GameMode = mode;
        args.SessionName = roomName;
        args.SceneManager = GetComponent<NetworkSceneManagerDefault>();
        args.ObjectProvider = GetComponent<PooledNetworkObjectProvider>();

        // The stable identity that makes rejoining work. The host reads it back with
        // Runner.GetPlayerUserId and uses it to match a joining player against the state
        // saved when that same person disconnected.
        args.AuthValues = new Photon.Realtime.AuthenticationValues(GetOrCreateLocalUserId());

        // Only the host loads the arena. A joining client is told which scene
        // to load by the host, so passing a scene here would fight that.
        if (mode != GameMode.Client)
        {
            NetworkSceneInfo sceneInfo = new NetworkSceneInfo();
            sceneInfo.AddSceneRef(arenaScene, LoadSceneMode.Single);
            args.Scene = sceneInfo;
        }

        StartGameResult result = await runner.StartGame(args);

        // StartGame reports failure through its result rather than throwing, so without
        // this check a failed connection looks like nothing happened - and the menu would
        // sit there with its buttons still disabled.
        if (!result.Ok)
        {
            Debug.LogError($"Failed to start {mode}: {result.ShutdownReason}");

            string verb = mode == GameMode.Host ? "create" : "join";
            EndSession($"Could not {verb} '{roomName}': {result.ShutdownReason}");

            return;
        }
    }

    /// <summary>
    /// Returns this installation's permanent player id, creating it on first run.
    ///
    /// Stored in PlayerPrefs so it survives the game being closed entirely - that is what
    /// lets someone alt-F4, relaunch, retype the room name and still be recognised as the
    /// player who left. The editor deliberately uses a different key: the editor and a
    /// standalone build on one machine share PlayerPrefs, and two peers in the same room
    /// must never present the same user id.
    /// </summary>
    private string GetOrCreateLocalUserId()
    {
#if UNITY_EDITOR
        const string UserIdKey = "ArenaProtocol.UserId.Editor";
#else
        const string UserIdKey = "ArenaProtocol.UserId";
#endif

        string userId = PlayerPrefs.GetString(UserIdKey, string.Empty);

        if (string.IsNullOrEmpty(userId))
        {
            userId = Guid.NewGuid().ToString();
            PlayerPrefs.SetString(UserIdKey, userId);
            PlayerPrefs.Save();
        }

        return userId;
    }

    /// <summary>
    /// Drops the player back on the main menu with a short explanation.
    ///
    /// The runner is not destroyed here: reloading the menu brings in a replacement
    /// bootstrap whose Awake destroys this whole object, dead runner and all. Doing it that
    /// way avoids racing a deferred Destroy against a deferred scene load.
    ///
    /// Safe to call more than once - a dropped connection usually produces both a
    /// disconnect and a shutdown callback, and a failed StartGame lands here too.
    /// </summary>
    private void EndSession(string statusMessage)
    {
        if (sessionEnded)
        {
            return;
        }

        sessionEnded = true;
        statusMessageForMenu = statusMessage;

        // A snapshot only means anything inside the match it was taken in.
        PlayerStates.Clear();

        // Reloaded even when already on the menu (a StartGame that failed before the arena
        // loaded): the reload is what supplies a bootstrap with a usable runner, so skipping
        // it would leave the player unable to try again.
        SceneManager.LoadScene(MenuSceneName);
    }

    /// <summary>
    /// Read once by MenuUI when the menu loads, then cleared so a later voluntary visit to
    /// the menu does not show a stale disconnect message.
    /// </summary>
    public string ConsumeStatusMessage()
    {
        string message = statusMessageForMenu;
        statusMessageForMenu = null;
        return message;
    }

    /// <summary>
    /// Turns Fusion's shutdown enum into something worth putting in front of a player.
    /// </summary>
    private static string DescribeShutdown(ShutdownReason reason)
    {
        switch (reason)
        {
            case ShutdownReason.Ok:
                return "You left the match.";

            case ShutdownReason.ConnectionTimeout:
            case ShutdownReason.PhotonCloudTimeout:
            case ShutdownReason.OperationTimeout:
                return "Connection lost. Rejoin the same room to continue.";

            case ShutdownReason.GameClosed:
            case ShutdownReason.ConnectionRefused:
            case ShutdownReason.DisconnectedByPluginLogic:
                return "The host closed the match.";

            case ShutdownReason.GameNotFound:
                return "That room does not exist. Check the name and try again.";

            case ShutdownReason.GameIsFull:
                return "That room is full.";

            case ShutdownReason.ServerInRoom:
                return "Someone is already hosting that room - join it instead.";

            default:
                return $"Match ended: {reason}.";
        }
    }

    /// <summary>
    /// Polled by Fusion every tick to gather this peer's local input. Requires
    /// runner.ProvideInput, which StartGame sets. The result is sent to the host
    /// and replayed during resimulation.
    /// </summary>
    void INetworkRunnerCallbacks.OnInput(NetworkRunner runner, NetworkInput input)
    {
        NetworkInputData data = new NetworkInputData();

        Vector3 direction = Vector3.zero;

        NetworkButtons buttons = default;
        buttons.Set((int)InputButton.Fire, Input.GetMouseButton(0));
        buttons.Set((int)InputButton.Dash, Input.GetKey(KeyCode.LeftShift));
        buttons.Set((int)InputButton.Ability, Input.GetKey(KeyCode.Q));
        data.Buttons = buttons;

        if (Input.GetKey(KeyCode.W))
        {
            direction += Vector3.forward;
        }

        if (Input.GetKey(KeyCode.S))
        {
            direction += Vector3.back;
        }

        if (Input.GetKey(KeyCode.A))
        {
            direction += Vector3.left;
        }

        if (Input.GetKey(KeyCode.D))
        {
            direction += Vector3.right;
        }

        // Pressing W+D gives (1,0,1), which has length 1.41 - normalizing keeps
        // diagonal movement the same speed as the cardinal directions.
        data.Direction = direction.normalized;

        input.Set(data);
    }

    void INetworkRunnerCallbacks.OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (!runner.IsServer)
        {
            return;
        }

        // Cached now because GetPlayerUserId stops resolving once the player has gone, and
        // the leave path is exactly where the id is needed.
        PlayerStates.RememberUserId(runner, player);

        PlayerSpawner spawner = FindObjectOfType<PlayerSpawner>();

        // Null while the arena is still loading. OnSceneLoadDone sweeps every
        // active player once it is up, so nothing is lost by returning here.
        if (spawner != null)
        {
            spawner.SpawnPlayer(runner, player);
        }
    }

    void INetworkRunnerCallbacks.OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        // DespawnPlayer snapshots the leaver first - the moment the object is despawned is
        // the last moment their health and score can be read.
        PlayerSpawner spawner = FindObjectOfType<PlayerSpawner>();

        if (spawner != null)
        {
            spawner.DespawnPlayer(runner, player);
        }
    }

    void INetworkRunnerCallbacks.OnSceneLoadDone(NetworkRunner runner)
    {
        if (!runner.IsServer)
        {
            return;
        }

        PlayerSpawner spawner = FindObjectOfType<PlayerSpawner>();

        if (spawner == null)
        {
            Debug.LogError("Arena loaded but it has no PlayerSpawner.");
            return;
        }

        // Catches the host, which joins before the arena finishes loading.
        foreach (PlayerRef player in runner.ActivePlayers)
        {
            PlayerStates.RememberUserId(runner, player);
            spawner.SpawnPlayer(runner, player);
        }
    }

    void INetworkRunnerCallbacks.OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        Debug.Log($"Disconnected from server: {reason}");

        // Not torn down directly. Shutdown routes back into OnShutdown, so exactly one
        // place decides the message and exactly one place ends the session.
        runner.Shutdown();
    }

    void INetworkRunnerCallbacks.OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
    {
        EndSession($"Could not connect: {reason}");
    }

    /// <summary>
    /// Fires on every peer when its runner stops - including every client when the host
    /// quits, which is what makes "the match ends for everyone" work with no extra messaging.
    /// </summary>
    void INetworkRunnerCallbacks.OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        EndSession(DescribeShutdown(shutdownReason));
    }

    #region Unused Callbacks
    void INetworkRunnerCallbacks.OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    void INetworkRunnerCallbacks.OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    void INetworkRunnerCallbacks.OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    void INetworkRunnerCallbacks.OnConnectedToServer(NetworkRunner runner) { }
    void INetworkRunnerCallbacks.OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    void INetworkRunnerCallbacks.OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    void INetworkRunnerCallbacks.OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    void INetworkRunnerCallbacks.OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    void INetworkRunnerCallbacks.OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data) { }
    void INetworkRunnerCallbacks.OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    void INetworkRunnerCallbacks.OnSceneLoadStart(NetworkRunner runner) { }

    #endregion
}