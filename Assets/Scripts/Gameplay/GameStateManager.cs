using Fusion;
using UnityEngine;

/// <summary>
/// Gates the start of the match. Nothing in the project detected "everyone has arrived"
/// before this - players simply spawned as they connected. The first enemy wave waits on
/// State flipping to InProgress.
///
/// Must live on a scene NetworkObject in Arena.unity. A plain MonoBehaviour or a bare
/// SimulationBehaviour will never receive Fusion callbacks - that is the exact trap called
/// out in PlayerSpawner's header comment.
/// </summary>
public class GameStateManager : NetworkBehaviour
{
    public enum MatchState
    {
        WaitingForPlayers,
        InProgress,
        Ended
    }

    [Tooltip("Replicated so clients can show a 'waiting for opponent' message later.")]
    [Networked] public MatchState State { get; set; }

    [Tooltip("How many players must be in the session before the first wave spawns. Set to 1 while testing solo in the editor.")]
    [SerializeField] private int requiredPlayers = 2;

    [SerializeField] private EnemySpawner enemySpawner;

    public override void Spawned()
    {
        // Enums default to their zero value, which is already WaitingForPlayers. Setting it
        // explicitly matters because a pooled/reloaded scene object could carry stale state,
        // and because it documents the starting point.
        if (Object.HasStateAuthority)
        {
            State = MatchState.WaitingForPlayers;
        }
    }

    /// <summary>
    /// Called by PlayerSpawner every time a player successfully spawns.
    /// </summary>
    public void NotifyPlayerSpawned(NetworkRunner runner)
    {
        // Only the host decides when the match starts. Clients are told via the
        // replicated State property.
        if (!runner.IsServer)
        {
            return;
        }

        // Already running. Without this guard a player rejoining mid-match would fire a
        // second opening wave on top of the enemies already in the arena.
        if (State != MatchState.WaitingForPlayers)
        {
            return;
        }

        // ActivePlayers is an IEnumerable<PlayerRef>. Counted with a plain foreach rather
        // than Linq's Count() to avoid the per-call allocation - this runs on the host.
        int playerCount = 0;

        foreach (PlayerRef player in runner.ActivePlayers)
        {
            playerCount++;
        }

        if (playerCount < requiredPlayers)
        {
            return;
        }

        State = MatchState.InProgress;

        if (enemySpawner == null)
        {
            Debug.LogError("GameStateManager: no EnemySpawner assigned, match started with no enemies.", this);
            return;
        }

        enemySpawner.SpawnInitialWave(runner);
    }
}
