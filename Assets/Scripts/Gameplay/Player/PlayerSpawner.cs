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
        //
        // IsLive rather than a null check, and it matters here: a despawned player object is
        // parked by the pool rather than destroyed, so a stale one being handed back would
        // make this bail out and a rejoining player would never spawn at all.
        if (runner.GetPlayerObject(player).IsLive())
        {
            return;
        }

        // Position always comes from a spawn point, even for a returning player: dropping
        // someone back where they vanished can rematerialise them inside a live wave.
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

        // Applied after Spawn rather than inside it: Spawned() resets health and score, so
        // anything written earlier would be overwritten.
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

        if (savedStates != null && savedStates.TryTake(runner, player, out SavedPlayerState savedState))
        {
            RestoreReturningPlayer(playerObject, savedState);
            return;
        }

        // Nobody the host recognises. Spawned() has already given them full health and a
        // zero score, so the only thing left to decide is which ability they get.
        if (playerObject.TryGetComponent(out PlayerAbility ability))
        {
            AssignComplementaryAbility(runner, player, ability);
        }
    }

    /// <summary>
    /// Gives a new player whichever ability nobody else is currently holding, so a two-player
    /// match always fields one Shield and one Heal rather than risking two of the same.
    ///
    /// Falls back to a coin flip when there is nothing to complement: the first player into
    /// the arena, or the case where both abilities are somehow already represented.
    ///
    /// Runs after runner.Spawn, so the joining player is already in ActivePlayers and has to
    /// be skipped explicitly - otherwise they would be complementing themselves.
    /// </summary>
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

        // Exactly one is spoken for, so the choice makes itself.
        if (shieldTaken != healTaken)
        {
            ability.AssignAbility(shieldTaken
                ? PlayerAbility.AbilityType.Heal
                : PlayerAbility.AbilityType.Shield);

            return;
        }

        ability.AssignRandomAbility();
    }

    /// <summary>
    /// Puts back everything a rejoining player left with: their health, and the ability they
    /// had along with its remaining duration and cooldown. The team score is not per player,
    /// so it lives on GameStateManager and survives a disconnect without being snapshotted.
    /// </summary>
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

        // IsLive rather than a null check: capturing the snapshot below reads networked
        // state, which throws on an object that has already been despawned and parked.
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
    /// The host's snapshot store, or null if there is no bootstrap to ask. Null is a normal
    /// outcome rather than an error - it just means nothing can be saved or restored, and
    /// every spawn is treated as a brand new player.
    /// </summary>
    private PlayerStateStore GetSavedStates()
    {
        return FusionBootstrap.Instance != null ? FusionBootstrap.Instance.PlayerStates : null;
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
