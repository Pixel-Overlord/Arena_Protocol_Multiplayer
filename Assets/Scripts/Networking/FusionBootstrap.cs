using Fusion;
using Fusion.Sockets;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Owns the NetworkRunner, starts host/client sessions and drives player spawning.
///
/// Join callbacks are registered explicitly with runner.AddCallbacks rather than
/// relying on Fusion discovering an IPlayerJoined in the loaded scene - Fusion only
/// registers NetworkObjects from a loaded scene, and a NetworkObject only tracks
/// NetworkBehaviours, so a plain SimulationBehaviour is never called.
/// </summary>
public class FusionBootstrap : MonoBehaviour, INetworkRunnerCallbacks
{
    public static FusionBootstrap Instance { get; private set; }

    private NetworkRunner runner;

    [Tooltip("Scene to load after successfully creating or joining a session.")]
    [SerializeField] private SceneRef arenaScene;

    /// <summary>
    /// To make the NetworkRunner available to other scenes, we can use the Singleton pattern.
    /// This allows us to access the NetworkRunner from any other script in our project.
    /// </summary>
    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
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
        runner.ProvideInput = true;
        runner.AddCallbacks(this);

        StartGameArgs args = new StartGameArgs();

        args.GameMode = mode;
        args.SessionName = roomName;
        args.SceneManager = GetOrAdd<NetworkSceneManagerDefault>();
        args.ObjectProvider = GetOrAdd<NetworkObjectProviderDefault>();

        // Only the host loads the arena. A joining client is told which scene
        // to load by the host, so passing a scene here would fight that.
        if (mode != GameMode.Client)
        {
            NetworkSceneInfo sceneInfo = new NetworkSceneInfo();
            sceneInfo.AddSceneRef(arenaScene, LoadSceneMode.Single);
            args.Scene = sceneInfo;
        }

        StartGameResult result = await runner.StartGame(args);

        // StartGame reports failure through its result rather than throwing,
        // so without this check a failed connection looks like nothing happened.
        if (!result.Ok)
        {
            Debug.LogError($"Failed to start {mode}: {result.ShutdownReason}");
        }
    }

    void INetworkRunnerCallbacks.OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (!runner.IsServer)
        {
            return;
        }

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
            spawner.SpawnPlayer(runner, player);
        }
    }

    void INetworkRunnerCallbacks.OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    void INetworkRunnerCallbacks.OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    void INetworkRunnerCallbacks.OnInput(NetworkRunner runner, NetworkInput input) { }
    void INetworkRunnerCallbacks.OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    void INetworkRunnerCallbacks.OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
    void INetworkRunnerCallbacks.OnConnectedToServer(NetworkRunner runner) { }
    void INetworkRunnerCallbacks.OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
    void INetworkRunnerCallbacks.OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    void INetworkRunnerCallbacks.OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    void INetworkRunnerCallbacks.OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    void INetworkRunnerCallbacks.OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    void INetworkRunnerCallbacks.OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    void INetworkRunnerCallbacks.OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data) { }
    void INetworkRunnerCallbacks.OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    void INetworkRunnerCallbacks.OnSceneLoadStart(NetworkRunner runner) { }

    /// <summary>
    /// Fusion requires a scene manager and an object provider on the runner's
    /// GameObject. Adding them on demand keeps this working no matter how the
    /// bootstrap object is set up in the scene.
    /// </summary>
    private T GetOrAdd<T>() where T : Component
    {
        T component = GetComponent<T>();
        return component != null ? component : gameObject.AddComponent<T>();
    }
}
