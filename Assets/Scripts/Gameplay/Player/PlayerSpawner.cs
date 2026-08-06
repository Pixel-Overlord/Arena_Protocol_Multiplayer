using Fusion;
using UnityEngine;

/// <summary>
/// Holds the arena's spawn points and performs the actual spawn.
///
/// This is a plain MonoBehaviour on purpose. As a SimulationBehaviour it relied on
/// Fusion discovering it in the loaded scene, which never happened: Fusion only
/// registers NetworkObjects (NetworkSceneManagerDefault.cs:662) and a NetworkObject
/// only tracks NetworkBehaviours, so IPlayerJoined was never dispatched here.
/// FusionBootstrap now drives it explicitly through INetworkRunnerCallbacks.
/// </summary>
public class PlayerSpawner : MonoBehaviour
{
    // Why not GameObject?
    // Because NetworkPrefabRef is a special type that allows us to reference a prefab that can be spawned over the network.
    [SerializeField] private NetworkPrefabRef playerPrefab;

    [SerializeField] private Transform[] spawnPoints;

    // Cached rather than searched per spawn - FindObjectOfType is expensive and this runs
    // on the join path. Assigned in Awake because both objects live in the arena scene.
    private GameStateManager gameStateManager;

    private void Awake()
    {
        gameStateManager = FindObjectOfType<GameStateManager>();

        if (gameStateManager == null)
        {
            Debug.LogWarning("PlayerSpawner: no GameStateManager in the arena, enemies will never spawn.", this);
        }
    }

    public void SpawnPlayer(NetworkRunner runner, PlayerRef player)
    {
        // Only the state authority is allowed to spawn networked objects.
        if (!runner.IsServer)
        {
            return;
        }

        // Guard against being asked twice for the same player.
        if (runner.GetPlayerObject(player) != null)
        {
            return;
        }

        Transform spawnPoint = GetSpawnPoint(player);
        NetworkObject playerObject = runner.Spawn(
            playerPrefab,
            spawnPoint.position,
            spawnPoint.rotation,
            player);

        if (playerObject == null)
        {
            Debug.LogError($"Spawn returned null for {player}.", this);
            return;
        }

        // Registers the object against the player so despawning can find it
        // again - without this GetPlayerObject always returns null.
        runner.SetPlayerObject(player, playerObject);

        playerObject.GetComponent<PlayerAbility>().Type = UnityEngine.Random.Range(0, 2) == 0 ? PlayerAbility.AbilityType.Shield : PlayerAbility.AbilityType.Heal;

        // Reported only after the spawn actually succeeded, so a failed spawn can't be
        // counted towards the "everyone has arrived" check that releases the first wave.
        if (gameStateManager != null)
        {
            gameStateManager.NotifyPlayerSpawned(runner);
        }
    }

    public void DespawnPlayer(NetworkRunner runner, PlayerRef player)
    {
        if (!runner.IsServer)
        {
            return;
        }

        NetworkObject playerObject = runner.GetPlayerObject(player);

        if (playerObject != null)
        {
            runner.Despawn(playerObject);
        }
    }

    /// <summary>
    /// Picks a spawn point for the joining player, wrapping around if more
    /// players join than there are points. Falls back to this object's own
    /// transform so a missing/empty array can never null-reference.
    /// </summary>
    private Transform GetSpawnPoint(PlayerRef player)
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogWarning("PlayerSpawner: no spawn points assigned.", this);
            return transform;
        }

        int index = player.PlayerId % spawnPoints.Length;
        return spawnPoints[index] != null ? spawnPoints[index] : transform;
    }
}
