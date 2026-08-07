using System.Linq;
using Fusion;
using UnityEngine;

/// <summary>
/// Manages the overall game state, including player status, wave progression, and team score in a networked arena
/// match.
/// </summary>
/// <remarks>Coordinates match flow, tracks live enemies, handles wave transitions, and synchronizes state across
/// networked clients. Provides static access for efficient event reporting and ensures consistent game logic during
/// match lifecycle events.</remarks>
public class GameStateManager : NetworkBehaviour
{
    public enum MatchState
    {
        WaitingForPlayers,
        InProgress,
        Ended
    }

    /// <summary>
    /// Gets the singleton instance of the GameStateManager.
    /// </summary>
    public static GameStateManager Instance { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the match has ended.
    /// </summary>
    public static bool IsMatchOver
    {
        get
        {
            if (Instance == null || Instance.Object == null || !Instance.Object.IsValid)
            {
                return false;
            }

            return Instance.State == MatchState.Ended;
        }
    }

    [Tooltip("Replicated so clients can show a 'waiting for opponent' message later.")]
    [Networked] public MatchState State { get; set; }

    [Tooltip("Shared team score.")]
    [Networked] public int TeamScore { get; set; }

    [Tooltip("Tells which wave is running.")]
    [Networked] public int WaveNumber { get; set; }

    [Tooltip("Enemies still alive in the current wave. Replicated so the HUD can show it without asking the host.")]
    [Networked] public int CurrentEnemyCount { get; set; }

    [Tooltip("Counts down the breather between one wave being cleared and the next spawning.")]
    [Networked] private TickTimer waveBreakTimer { get; set; }

    [Tooltip("True while waiting out the gap between waves.")]
    [Networked] private NetworkBool waveBreakPending { get; set; }

    [Tooltip("How many players must be in the session before the first wave spawns.")]
    [SerializeField] private int requiredPlayers = 2;

    [Tooltip("Time between clearing a wave and the next one arriving.")]
    [SerializeField] private float secondsBetweenWaves = 3f;

    [SerializeField] private EnemySpawner enemySpawner;

    // This prevents the match from ending immediately on the first tick after State flips to InProgress,
    // when the player objects are not yet resolvable.
    // Once a player has been seen, playerObjectCount dropping back to zero is a real end condition.
    private bool anyPlayerHasSpawned;

    public override void Spawned()
    {
        Instance = this;

        // Enums default to their zero value, which is already WaitingForPlayers. Setting it
        // explicitly matters because a pooled/reloaded scene object could carry stale state,
        // and because it documents the starting point.
        if (Object.HasStateAuthority)
        {
            State = MatchState.WaitingForPlayers;
            TeamScore = 0;
            WaveNumber = 0;
            CurrentEnemyCount = 0;
            waveBreakPending = false;
            anyPlayerHasSpawned = false;
        }
    }

    /// <summary>
    /// Clears the static reference so a second session can't reach into the dead one.
    /// </summary>
    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>
    /// Watches for the whole team being wiped out. Runs only on the host; every other peer
    /// learns the match ended through the replicated State property.
    /// </summary>
    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority || State != MatchState.InProgress)
        {
            return;
        }

        int playerObjectCount = 0;
        int aliveCount = 0;

        foreach (PlayerRef player in Runner.ActivePlayers)
        {
            NetworkObject playerObject = Runner.GetPlayerObject(player);

            if (!playerObject.IsLive() || !playerObject.TryGetComponent(out PlayerHealth health))
            {
                continue;
            }

            playerObjectCount++;

            if (!health.isDead)
            {
                aliveCount++;
            }
        }

        if (playerObjectCount > 0)
        {
            anyPlayerHasSpawned = true;
        }

        // Nothing spawned yet. Without this the match would end on the very first tick after
        // State flips to InProgress, because the player objects are not resolvable yet.
        //
        // The latch is what separates that from "everyone has left": once a player has been
        // seen, playerObjectCount dropping back to zero is a real end condition and falls
        // through to the aliveCount check below.
        if (!anyPlayerHasSpawned)
        {
            return;
        }

        // Per the GDD the match ends the moment one player goes down - this is a co-op run that
        // both players either survive or lose together, not a last-man-standing mode.
        bool someoneDied = aliveCount < playerObjectCount;

        // If everybody disconnects mid-match the loop would
        // otherwise sit in InProgress forever with no players left to die.
        bool everyoneLeft = playerObjectCount == 0;

        if (someoneDied || everyoneLeft)
        {
            State = MatchState.Ended;
            return;
        }

        TickWaveLoop();
    }

    /// <summary>
    /// Spawns the next wave once the arena has been cleared and the breather has elapsed.
    ///
    /// Only reached while State is InProgress, which is what stops a fresh wave arriving
    /// after GAME OVER.
    /// </summary>
    private void TickWaveLoop()
    {
        // A wave is still being fought.
        if (CurrentEnemyCount > 0)
        {
            return;
        }

        // Nothing is waiting to spawn. This is the guard that matters: without it, a
        // TickTimer that was never started reads as expired and wave 2 would appear the
        // instant wave 1 spawned.
        if (!waveBreakPending)
        {
            return;
        }

        if (!waveBreakTimer.Expired(Runner))
        {
            return;
        }

        StartNextWave();
    }

    /// <summary>
    /// Starts the next wave by incrementing the wave number and spawning enemies using the assigned enemy spawner.
    /// </summary>
    private void StartNextWave()
    {
        if (enemySpawner == null)
        {
            Debug.LogError("GameStateManager: no EnemySpawner assigned, no enemies will spawn.");
            return;
        }

        waveBreakPending = false;
        WaveNumber++;

        // SpawnWave returns how many enemies actually spawned.
        CurrentEnemyCount = enemySpawner.SpawnWave(Runner, WaveNumber);
    }

    /// <summary>
    /// Decrements the current enemy count and starts the wave break timer if all enemies are defeated.
    /// </summary>
    public void NotifyEnemyKilled()
    {
        if (!Object.HasStateAuthority || State != MatchState.InProgress)
        {
            return;
        }

        if (CurrentEnemyCount <= 0)
        {
            return;
        }

        CurrentEnemyCount--;

        if (CurrentEnemyCount == 0)
        {
            waveBreakPending = true;
            waveBreakTimer = TickTimer.CreateFromSeconds(Runner, secondsBetweenWaves);
        }
    }

    /// <summary>
    /// Increments the team's score by the specified number of points.
    /// </summary>
    /// <param name="points">The number of points to add to the team's score.</param>
    public void AddScore(int points)
    {
        if (!Object.HasStateAuthority || points <= 0)
        {
            return;
        }

        TeamScore += points;
    }

    /// <summary>
    /// Notifies the game that a player has spawned and initiates the match if all conditions are met.
    /// </summary>
    /// <param name="runner">The network runner managing the current game session.</param>
    public void NotifyPlayerSpawned(NetworkRunner runner)
    {
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

        int playerCount = runner.ActivePlayers.Count();

        if (playerCount < requiredPlayers)
        {
            return;
        }

        State = MatchState.InProgress;

        StartNextWave();
    }
}
