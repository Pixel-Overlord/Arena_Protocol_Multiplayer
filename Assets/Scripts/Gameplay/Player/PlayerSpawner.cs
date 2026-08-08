using Fusion;
using UnityEngine;

/// <summary>
/// Manages spawning and despawning of player objects in the game.
/// </summary>
/// <remarks>Handles both new and returning players by restoring saved state when available, and ensures that each
/// player receives a complementary ability.</remarks>
public class PlayerSpawner : MonoBehaviour
{
    // Why not GameObject?
    // Because NetworkPrefabRef is a special type that allows us to reference a prefab that can be spawned over the network.
    [SerializeField] private NetworkPrefabRef playerPrefab;

    [Tooltip("List of spawn points for players.")]
    [SerializeField] private Transform[] spawnPoints;

    private GameStateManager gameStateManager;

    private void Awake()
    {
        gameStateManager = FindObjectOfType<GameStateManager>();

        if (gameStateManager == null)
        {
            Debug.LogWarning("PlayerSpawner: no GameStateManager in the arena, enemies will never spawn.", this);
        }
    }

    /// <summary>
    /// Spawns a player at a designated spawn point.
    /// </summary>
    /// <remarks>Only the server can spawn players. Prevents multiple spawns for the same player and ensures
    /// proper registration and initialization.</remarks>
    /// <param name="runner">The network runner managing the networked game state.</param>
    /// <param name="player">The reference to the player to spawn.</param>
    public void SpawnPlayer(NetworkRunner runner, PlayerRef player)
    {
        // Only the state authority is allowed to spawn networked objects.
        if (!runner.IsServer)
        {
            return;
        }

        // Guard against being asked twice for the same player.
        if (runner.GetPlayerObject(player).IsLive())
        {
            return;
        }

        // Position always comes from a spawn point.
        Transform spawnPoint = GetSpawnPoint(player);

        NetworkObject playerObject = runner.Spawn(
            playerPrefab,
            spawnPoint.position,
            spawnPoint.rotation,
            player);

        // Register the spawned object with the runner so it knows which player it belongs to.
        runner.SetPlayerObject(player, playerObject);

        // Apply any saved state for returning players, or assign a complementary ability for new players.
        ApplyInitialState(runner, player, playerObject);

        // Reported only after the spawn actually succeeded, so a failed spawn can't be
        // counted towards the "everyone has arrived" check that releases the first wave.
        if (gameStateManager != null)
        {
            gameStateManager.NotifyPlayerSpawned(runner);
        }
    }

    /// <summary>
    /// Sets up a newly spawned player: either everything they disconnected with, or - for
    /// someone the host has never seen - whichever ability the other player is not using.
    /// </summary>
    private void ApplyInitialState(NetworkRunner runner, PlayerRef player, NetworkObject playerObject)
    {
        PlayerStateStore savedStates = GetSavedStates();

        // If the player has a saved state, restore it. Otherwise, assign a complementary ability.
        // This is for if player reconnects after a disconnect.
        if (savedStates != null && savedStates.TryTake(runner, player, out SavedPlayerState savedState))
        {
            RestoreReturningPlayer(playerObject, savedState);
            return;
        }

        // If no saved state is found, assign a complementary ability to the new player.
        if (playerObject.TryGetComponent(out PlayerAbility ability))
        {
            AssignComplementaryAbility(runner, player, ability);
        }
    }

    /// <summary>
    /// Assigns a complementary ability to a new player based on the abilities of active players.
    /// </summary>
    /// <param name="runner">The network runner managing the game state.</param>
    /// <param name="newPlayer">The reference to the new player being assigned an ability.</param>
    /// <param name="ability">The ability object to which a complementary ability will be assigned.</param>
    private void AssignComplementaryAbility(NetworkRunner runner, PlayerRef newPlayer, PlayerAbility ability)
    {
        bool shieldTaken = false;
        bool healTaken = false;

        foreach (PlayerRef player in runner.ActivePlayers)
        {
            if (player == newPlayer)
            {
                continue;
            }

            NetworkObject playerObject = runner.GetPlayerObject(player);

            if (!playerObject.IsLive() || !playerObject.TryGetComponent(out PlayerAbility otherAbility))
            {
                continue;
            }

            if (otherAbility.Type == PlayerAbility.AbilityType.Shield)
            {
                shieldTaken = true;
            }
            else
            {
                healTaken = true;
            }
        }

        // If one ability is taken and the other is not,
        // assign the complementary ability to the new player.
        if (shieldTaken && !healTaken)
        {
            ability.AssignAbility(PlayerAbility.AbilityType.Heal);
        }
        else if (healTaken && !shieldTaken)
        {
            ability.AssignAbility(PlayerAbility.AbilityType.Shield);
        }
    }

    /// <summary>
    /// Restores a returning player's health and ability state from saved data.
    /// </summary>
    /// <param name="playerObject">The network object representing the player.</param>
    /// <param name="savedState">The saved state containing the player's health and ability information.</param>
    private void RestoreReturningPlayer(NetworkObject playerObject, SavedPlayerState savedState)
    {
        if (playerObject.TryGetComponent(out PlayerHealth health))
        {
            health.RestoreState(savedState);
        }

        if (playerObject.TryGetComponent(out PlayerAbility ability))
        {
            ability.RestoreState(savedState);
        }
    }

    public void DespawnPlayer(NetworkRunner runner, PlayerRef player)
    {
        if (!runner.IsServer)
        {
            return;
        }

        NetworkObject playerObject = runner.GetPlayerObject(player);

        // Guard against being asked to despawn a player that is not currently spawned.
        if (!playerObject.IsLive())
        {
            return;
        }

        // Snapshot before despawning - this is the last moment the state exists anywhere.
        SaveStateForRejoin(runner, player, playerObject);

        runner.Despawn(playerObject);
    }

    /// <summary>
    /// Records a disconnecting player's health and score so a rejoin can restore them.
    /// </summary>
    private void SaveStateForRejoin(NetworkRunner runner, PlayerRef player, NetworkObject playerObject)
    {
        PlayerStateStore savedStates = GetSavedStates();

        if (savedStates == null)
        {
            return;
        }

        SavedPlayerState state = default;

        if (playerObject.TryGetComponent(out PlayerHealth health))
        {
            health.CaptureStateInto(ref state);
        }

        if (playerObject.TryGetComponent(out PlayerAbility ability))
        {
            ability.CaptureStateInto(ref state);
        }

        savedStates.Save(runner, player, state);
    }

    /// <summary>
    /// Retrieves the saved player states from the FusionBootstrap instance.
    /// </summary>
    /// <returns>The saved player states if the FusionBootstrap instance is not null; otherwise, null.</returns>
    private PlayerStateStore GetSavedStates()
    {
        return FusionBootstrap.Instance != null ? FusionBootstrap.Instance.PlayerStates : null;
    }

    /// <summary>
    /// Retrieves the spawn point assigned to the specified player.
    /// </summary>
    /// <param name="player">The player for whom to retrieve the spawn point.</param>
    /// <returns>A transform representing the player's spawn point, or the default transform if no spawn points are assigned.</returns>
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
